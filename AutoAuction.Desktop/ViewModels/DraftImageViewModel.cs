using Avalonia.Media.Imaging;
using AutoAuction.Desktop.Services;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>A read-only thumbnail of an image attached to a draft, shown in the editor.</summary>
public sealed class DraftImageViewModel
{
    public string FileName { get; }
    public Bitmap? Thumbnail { get; }

    public DraftImageViewModel(string fullPath)
    {
        FileName = System.IO.Path.GetFileName(fullPath);
        Thumbnail = ThumbnailLoader.Load(fullPath, 160);
    }
}
