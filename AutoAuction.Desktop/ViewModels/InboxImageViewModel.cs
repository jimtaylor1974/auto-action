using Avalonia.Media.Imaging;
using AutoAuction.Desktop.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>A single selectable photo shown in the Inbox gallery.</summary>
public class InboxImageViewModel : ViewModelBase
{
    /// <summary>Full path to the image file in the Inbox folder.</summary>
    public string FullPath { get; }

    public string FileName { get; }

    public Bitmap? Thumbnail { get; }

    private bool _isSelected;
    /// <summary>Whether this image is ticked for inclusion in the next "Create Draft".</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public InboxImageViewModel(string fullPath)
    {
        FullPath = fullPath;
        FileName = System.IO.Path.GetFileName(fullPath);
        Thumbnail = ThumbnailLoader.Load(fullPath);
    }
}
