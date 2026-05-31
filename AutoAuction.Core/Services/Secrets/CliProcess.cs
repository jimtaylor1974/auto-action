using System.Diagnostics;

namespace AutoAuction.Core.Services.Secrets;

/// <summary>Tiny helper to invoke a CLI tool (keychain helpers) and capture its result.</summary>
internal static class CliProcess
{
    internal readonly record struct Result(int ExitCode, string StdOut, string StdErr)
    {
        public bool Ok => ExitCode == 0;
    }

    /// <summary>
    /// Runs <paramref name="fileName"/> with the given arguments, optionally writing
    /// <paramref name="stdin"/> to its standard input. Returns exit code + captured output.
    /// </summary>
    internal static Result Run(string fileName, IEnumerable<string> arguments, string? stdin = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new Result(process.ExitCode, stdout, stderr);
    }
}
