using System.Runtime.InteropServices;

namespace AutoAuction.Core.Services.Secrets;

/// <summary>
/// Stores small secrets (e.g. the OpenAI API key) encrypted at rest, never in plaintext
/// <c>settings.json</c>. Secrets are addressed by a logical <c>name</c>; the backend chosen
/// per-OS decides where/how they're protected (Windows DPAPI, macOS Keychain, Linux libsecret).
/// </summary>
public interface ISecretStore
{
    /// <summary>True when a secret with this name is stored. Must not surface the value itself.</summary>
    bool Has(string name);

    /// <summary>Returns the decrypted secret, or null when it isn't set (or can't be read).</summary>
    string? Get(string name);

    /// <summary>Stores (or replaces) the secret, encrypted at rest.</summary>
    void Set(string name, string value);

    /// <summary>Removes the secret if present (no-op when it isn't).</summary>
    void Delete(string name);
}

/// <summary>Picks the right <see cref="ISecretStore"/> backend for the current operating system.</summary>
public static class SecretStore
{
    /// <summary>Logical service name used to namespace secrets in OS keychains.</summary>
    public const string ServiceName = "AutoAuction";

    public static ISecretStore ForCurrentPlatform(IAppConfig config)
    {
        if (OperatingSystem.IsWindows())
            return new WindowsDpapiSecretStore(config);
        if (OperatingSystem.IsMacOS())
            return new MacKeychainSecretStore();
        if (OperatingSystem.IsLinux())
            return new LinuxSecretToolSecretStore();

        throw new PlatformNotSupportedException(
            $"No secure secret store is available for {RuntimeInformation.OSDescription}.");
    }
}
