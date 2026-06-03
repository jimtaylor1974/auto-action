using System;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using AutoAuction.Core.Models;
using AutoAuction.Core.Services;
using AutoAuction.Desktop.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>Where a draft sits in a batch run.</summary>
public enum DraftProcessingState
{
    Idle,
    Queued,
    Working,
    Done,
    Failed
}

/// <summary>
/// A selectable, reactive row on the Drafts page: cover/title/counts plus selection, batch
/// processing state, the AI-drafted badge and the lock summary. Re-reads from disk via
/// <see cref="Refresh"/> after a batch or bulk write.
/// </summary>
public sealed class DraftRowViewModel : ReactiveObject
{
    private DraftFolder _draft;

    public DraftRowViewModel(DraftFolder draft)
    {
        _draft = draft;

        var firstImage = draft.Listing.LocalImagePaths.FirstOrDefault();
        if (firstImage is not null)
            CoverThumbnail = ThumbnailLoader.Load(Path.Combine(draft.FolderPath, firstImage), 120);
    }

    public DraftFolder Draft => _draft;
    public string FolderPath => _draft.FolderPath;
    public ListingModel Listing => _draft.Listing;

    public Bitmap? CoverThumbnail { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    private DraftProcessingState _state = DraftProcessingState.Idle;
    public DraftProcessingState State
    {
        get => _state;
        private set
        {
            this.RaiseAndSetIfChanged(ref _state, value);
            this.RaisePropertyChanged(nameof(IsProcessing));
        }
    }

    private string _stateText = string.Empty;
    /// <summary>Short status shown in the State column (e.g. "Queued", "Working…", "Done", or an error).</summary>
    public string StateText
    {
        get => _stateText;
        private set => this.RaiseAndSetIfChanged(ref _stateText, value);
    }

    public bool IsProcessing => _state is DraftProcessingState.Queued or DraftProcessingState.Working;

    public bool IsAiDrafted => Listing.AiDraftedUtc is not null;
    public string AiBadge => IsAiDrafted ? "✨" : string.Empty;

    public string LockSummary => ListingFieldApplier.DescribeLocks(Listing.LockedGroups);

    public string DisplayTitle => string.IsNullOrWhiteSpace(Listing.Title)
        ? "(untitled draft)"
        : Listing.Title;

    public int PhotoCount => Listing.LocalImagePaths.Count;
    public string CreatedDisplay => Listing.CreatedUtc.ToLocalTime().ToString("g");
    public string StatusDisplay => Listing.Status.ToString();

    public void SetState(DraftProcessingState state, string? text = null)
    {
        State = state;
        StateText = text ?? state switch
        {
            DraftProcessingState.Queued => "Queued",
            DraftProcessingState.Working => "Working…",
            DraftProcessingState.Done => "Done",
            DraftProcessingState.Failed => "Failed",
            _ => string.Empty
        };
    }

    /// <summary>Re-reads the listing (after AI/bulk write) and refreshes the derived columns.</summary>
    public void Refresh(DraftFolder draft)
    {
        _draft = draft;
        this.RaisePropertyChanged(nameof(DisplayTitle));
        this.RaisePropertyChanged(nameof(PhotoCount));
        this.RaisePropertyChanged(nameof(StatusDisplay));
        this.RaisePropertyChanged(nameof(IsAiDrafted));
        this.RaisePropertyChanged(nameof(AiBadge));
        this.RaisePropertyChanged(nameof(LockSummary));
    }
}
