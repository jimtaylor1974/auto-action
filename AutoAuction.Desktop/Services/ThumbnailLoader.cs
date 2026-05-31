using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace AutoAuction.Desktop.Services;

/// <summary>Loads image files from disk into down-scaled Avalonia bitmaps for display.</summary>
public static class ThumbnailLoader
{
    /// <summary>
    /// Decodes the image at <paramref name="path"/> scaled to <paramref name="width"/> pixels wide.
    /// Returns null if the file is missing or cannot be decoded.
    /// </summary>
    public static Bitmap? Load(string path, int width = 200)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception)
        {
            // Unreadable / unsupported image - skip the thumbnail rather than crash.
            return null;
        }
    }
}
