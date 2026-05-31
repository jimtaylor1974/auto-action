using System.Runtime.Versioning;

namespace AutoAuction.Core.Services.Secrets;

/// <summary>
/// Linux backend: stores secrets in the desktop keyring (GNOME Keyring / KWallet) through
/// libsecret's <c>secret-tool</c>. Items are keyed by <c>service</c> + <c>account</c> attributes.
/// </summary>
/// <remarks>
/// Written but not exercised on Windows CI — verify on Linux before relying on it. Requires the
/// <c>libsecret-tools</c> package and a running keyring/Secret Service. <c>secret-tool store</c>
/// reads the value from stdin, so it never appears in the process arguments.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretToolSecretStore : ISecretStore
{
    private const string Tool = "secret-tool";

    public bool Has(string name) => Get(name) is not null;

    public string? Get(string name)
    {
        var result = CliProcess.Run(Tool, new[] { "lookup", "service", SecretStore.ServiceName, "account", name });
        if (!result.Ok)
            return null;

        // secret-tool prints the value with no trailing newline, but trim defensively.
        var value = result.StdOut.TrimEnd('\n', '\r');
        return value.Length == 0 ? null : value;
    }

    public void Set(string name, string value)
    {
        var result = CliProcess.Run(
            Tool,
            new[]
            {
                "store", "--label", $"{SecretStore.ServiceName} {name}",
                "service", SecretStore.ServiceName, "account", name,
            },
            stdin: value);
        if (!result.Ok)
            throw new InvalidOperationException($"Keyring store failed: {result.StdErr.Trim()}");
    }

    public void Delete(string name)
    {
        // Idempotent: a missing item just returns non-zero, which we ignore.
        CliProcess.Run(Tool, new[] { "clear", "service", SecretStore.ServiceName, "account", name });
    }
}
