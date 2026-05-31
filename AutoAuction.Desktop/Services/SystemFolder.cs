using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AutoAuction.Desktop.Services;

/// <summary>
/// Helpers for revealing files and folders in the host operating system's file manager
/// (Explorer on Windows, Finder on macOS, the default handler on Linux).
/// </summary>
public static class SystemFolder
{
    /// <summary>
    /// Opens the given folder in the OS file manager. No-op if the folder does not exist.
    /// </summary>
    public static void Open(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // explorer.exe interprets the path itself; UseShellExecute keeps it detached.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folderPath}\"")
            {
                UseShellExecute = true
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", $"\"{folderPath}\"");
        }
        else
        {
            Process.Start("xdg-open", $"\"{folderPath}\"");
        }
    }
}
