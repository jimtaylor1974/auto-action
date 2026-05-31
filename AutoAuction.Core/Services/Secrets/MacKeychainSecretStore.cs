using System.Runtime.Versioning;

namespace AutoAuction.Core.Services.Secrets;

/// <summary>
/// macOS backend: stores secrets as generic passwords in the login Keychain via the built-in
/// <c>security</c> tool. The OS encrypts them at rest and gates access per app/user.
/// </summary>
/// <remarks>
/// Written but not exercised on Windows CI — verify on a Mac before relying on it. Note that
/// <c>add-generic-password -w</c> passes the value as a process argument (briefly visible in the
/// process list); acceptable for a single-user personal app, revisit if that ever matters.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainSecretStore : ISecretStore
{
    private const string Tool = "security";

    public bool Has(string name)
        // Query attributes only (omit -w) so we don't trigger a Keychain access prompt.
        => CliProcess.Run(Tool, new[] { "find-generic-password", "-a", name, "-s", SecretStore.ServiceName }).Ok;

    public string? Get(string name)
    {
        var result = CliProcess.Run(
            Tool, new[] { "find-generic-password", "-a", name, "-s", SecretStore.ServiceName, "-w" });
        if (!result.Ok)
            return null;

        var value = result.StdOut.TrimEnd('\n', '\r');
        return value.Length == 0 ? null : value;
    }

    public void Set(string name, string value)
    {
        // -U updates the item in place when it already exists rather than failing.
        var result = CliProcess.Run(Tool, new[]
        {
            "add-generic-password", "-a", name, "-s", SecretStore.ServiceName, "-w", value, "-U",
        });
        if (!result.Ok)
            throw new InvalidOperationException($"Keychain store failed: {result.StdErr.Trim()}");
    }

    public void Delete(string name)
    {
        // Non-zero exit just means it wasn't there — treat Delete as idempotent.
        CliProcess.Run(Tool, new[] { "delete-generic-password", "-a", name, "-s", SecretStore.ServiceName });
    }
}
