using Avalonia.Media.Imaging;
using AutoAuction.Desktop.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>A thumbnail of an image attached to a draft, shown in the editor. Can be rotated,
/// which rewrites the file on disk and reloads the thumbnail.</summary>
public sealed class DraftImageViewModel : ReactiveObject
{
    private const int ThumbnailWidth = 160;

    public string FileName { get; }
    public string FullPath { get; }

    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set => this.RaiseAndSetIfChanged(ref _thumbnail, value);
    }

    public DraftImageViewModel(string fullPath)
    {
        FullPath = fullPath;
        FileName = System.IO.Path.GetFileName(fullPath);
        Thumbnail = ThumbnailLoader.Load(fullPath, ThumbnailWidth);
    }

    /// <summary>Re-reads the thumbnail from disk after the underlying file changes (e.g. a rotate).</summary>
    public void ReloadThumbnail()
    {
        var old = Thumbnail;
        Thumbnail = ThumbnailLoader.Load(FullPath, ThumbnailWidth);
        old?.Dispose();
    }
}
