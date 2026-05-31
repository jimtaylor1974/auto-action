using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AutoAuction.Core.Services.Secrets;

/// <summary>
/// Windows backend: encrypts each secret with DPAPI (<see cref="ProtectedData"/>,
/// <see cref="DataProtectionScope.CurrentUser"/>) and writes the ciphertext to a
/// per-secret <c>.bin</c> file under the app's <c>secrets</c> folder. Only the same
/// Windows user on the same machine can decrypt it; no key management required.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiSecretStore : ISecretStore
{
    // App-specific entropy mixed into the DPAPI blob as a second factor. Not itself a secret;
    // it just means a blob can't be decrypted without this exact additional value.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AutoAuction.Secrets.v1");

    private readonly IAppConfig _config;

    public WindowsDpapiSecretStore(IAppConfig config)
    {
        _config = config;
    }

    public bool Has(string name) => File.Exists(PathFor(name));

    public string? Get(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
            return null;

        try
        {
            var cipher = File.ReadAllBytes(path);
            var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // Blob written by a different user/machine, or corrupt — treat as "not readable".
            return null;
        }
    }

    public void Set(string name, string value)
    {
        Directory.CreateDirectory(_config.SecretsPath);
        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);

        // Temp-then-move so a crash mid-write can't leave a half-written secret.
        var path = PathFor(name);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, cipher);
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string PathFor(string name) => Path.Combine(_config.SecretsPath, name + ".bin");
}
