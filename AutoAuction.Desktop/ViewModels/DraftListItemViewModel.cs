using System;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using AutoAuction.Core.Models;
using AutoAuction.Core.Services;
using AutoAuction.Desktop.Services;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>A row in the Drafts / Listed tables: cover thumbnail, title, counts and status.</summary>
public sealed class DraftListItemViewModel
{
    public DraftFolder Draft { get; }

    public string DisplayTitle { get; }

    public string Summary { get; }

    public int PhotoCount { get; }

    public DateTime CreatedUtc { get; }

    /// <summary>Local-time created date for display in a table column.</summary>
    public string CreatedDisplay { get; }

    public ListingStatus Status { get; }

    public string StatusDisplay { get; }

    public Bitmap? CoverThumbnail { get; }

    public DraftListItemViewModel(DraftFolder draft)
    {
        Draft = draft;

        DisplayTitle = string.IsNullOrWhiteSpace(draft.Listing.Title)
            ? "(untitled draft)"
            : draft.Listing.Title;

        PhotoCount = draft.Listing.LocalImagePaths.Count;
        Summary = PhotoCount == 1 ? "1 photo" : $"{PhotoCount} photos";

        CreatedUtc = draft.Listing.CreatedUtc;
        CreatedDisplay = draft.Listing.CreatedUtc.ToLocalTime().ToString("g");

        Status = draft.Listing.Status;
        StatusDisplay = draft.Listing.Status.ToString();

        var firstImage = draft.Listing.LocalImagePaths.FirstOrDefault();
        if (firstImage is not null)
        {
            var fullPath = Path.Combine(draft.FolderPath, firstImage);
            CoverThumbnail = ThumbnailLoader.Load(fullPath, 120);
        }
    }
}
