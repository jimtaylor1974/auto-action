using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using AutoAuction.Core.Services;
using AutoAuction.Desktop.Services;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>A row in the Drafts list: cover thumbnail, display title and image count.</summary>
public sealed class DraftListItemViewModel
{
    public DraftFolder Draft { get; }

    public string DisplayTitle { get; }

    public string Summary { get; }

    public Bitmap? CoverThumbnail { get; }

    public DraftListItemViewModel(DraftFolder draft)
    {
        Draft = draft;

        DisplayTitle = string.IsNullOrWhiteSpace(draft.Listing.Title)
            ? "(untitled draft)"
            : draft.Listing.Title;

        var imageCount = draft.Listing.LocalImagePaths.Count;
        Summary = imageCount == 1 ? "1 photo" : $"{imageCount} photos";

        var firstImage = draft.Listing.LocalImagePaths.FirstOrDefault();
        if (firstImage is not null)
        {
            var fullPath = Path.Combine(draft.FolderPath, firstImage);
            CoverThumbnail = ThumbnailLoader.Load(fullPath, 120);
        }
    }
}
