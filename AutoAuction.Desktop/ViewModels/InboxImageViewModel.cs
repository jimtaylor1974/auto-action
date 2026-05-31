using Avalonia.Media.Imaging;
using AutoAuction.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>A single selectable photo shown in the Inbox gallery.</summary>
public partial class InboxImageViewModel : ViewModelBase
{
    /// <summary>Full path to the image file in the Inbox folder.</summary>
    public string FullPath { get; }

    public string FileName { get; }

    public Bitmap? Thumbnail { get; }

    /// <summary>Whether this image is ticked for inclusion in the next "Create Draft".</summary>
    [ObservableProperty]
    private bool _isSelected;

    public InboxImageViewModel(string fullPath)
    {
        FullPath = fullPath;
        FileName = System.IO.Path.GetFileName(fullPath);
        Thumbnail = ThumbnailLoader.Load(fullPath);
    }
}
