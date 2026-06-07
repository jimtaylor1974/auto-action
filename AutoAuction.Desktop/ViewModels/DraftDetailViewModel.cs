using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using AutoAuction.Core.Models;
using AutoAuction.Core.Services;
using AutoAuction.Desktop.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>
/// Full-page editor for a single draft. Binds a <see cref="ListingModel"/> to the form and
/// auto-saves any change back to the draft's <c>listing.json</c> (debounced). While open, it
/// registers itself as the active listing so the bridge server can serve it to the extension.
/// </summary>
public class DraftDetailViewModel : ViewModelBase
{
    private readonly IDraftService _draftService;
    private readonly IActiveListingProvider _activeListing;
    private readonly IListingGenerator _generator;
    private readonly ICategoryService _categories;
    private readonly MainWindowViewModel _shell;
    private string _folderPath;

    // Debounce so we write the file once the user pauses typing, not on every keystroke.
    private CancellationTokenSource? _saveCts;
    private const int SaveDebounceMs = 500;
    private bool _closed;

    // True while AI is writing fields, so the change-tracking doesn't treat those writes as
    // "user edits" and lock the groups it just filled.
    private bool _applyingAi;

    public ListingModel Listing { get; }

    public ObservableCollection<DraftImageViewModel> Images { get; } = new();

    public string Title => string.IsNullOrWhiteSpace(Listing.Title) ? "(untitled draft)" : Listing.Title;

    /// <summary>Human-readable category path resolved from <c>Listing.CategoryId</c>.</summary>
    public string CategoryPathDisplay => string.IsNullOrWhiteSpace(Listing.CategoryId)
        ? string.Empty
        : _categories.GetDisplayPath(Listing.CategoryId) ?? "Unknown category";

    private string _saveStatus = string.Empty;
    /// <summary>Status text shown in the editor (e.g. "Saved").</summary>
    public string SaveStatus
    {
        get => _saveStatus;
        set => this.RaiseAndSetIfChanged(ref _saveStatus, value);
    }

    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> DiscardCommand { get; }
    public ReactiveCommand<Unit, Unit> MarkListedCommand { get; }
    public ReactiveCommand<Unit, Unit> AddShippingOptionCommand { get; }
    public ReactiveCommand<ShippingOption, Unit> RemoveShippingOptionCommand { get; }
    public ReactiveCommand<Unit, Unit> GenerateWithAiCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyAiCommand { get; }
    public ReactiveCommand<Unit, Unit> DiscardAiCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearLocksCommand { get; }
    public ReactiveCommand<DraftImageViewModel, Unit> RotateLeftCommand { get; }
    public ReactiveCommand<DraftImageViewModel, Unit> RotateRightCommand { get; }

    /// <summary>Groups the user has set that AI won't overwrite, e.g. "Pricing, Shipping". Empty if none.</summary>
    public string LockSummary => ListingFieldApplier.DescribeLocks(Listing.LockedGroups);

    /// <summary>True when at least one field group is locked against AI.</summary>
    public bool HasLocks => Listing.LockedGroups != ListingFieldGroup.None;

    private bool _isGenerating;
    public bool IsGenerating
    {
        get => _isGenerating;
        set
        {
            this.RaiseAndSetIfChanged(ref _isGenerating, value);
            this.RaisePropertyChanged(nameof(ShowGenerationLog));
        }
    }

    /// <summary>Live, newest-first status lines emitted while the AI generator runs.</summary>
    public ObservableCollection<string> GenerationLog { get; } = new();

    public bool ShowGenerationLog => IsGenerating || GenerationLog.Count > 0;

    /// <summary>The activity log (oldest-first) plus any error, for copying to the clipboard.</summary>
    public string BuildDiagnosticsText()
    {
        var lines = GenerationLog.Reverse().ToList();
        if (!string.IsNullOrWhiteSpace(AiError))
            lines.Add($"ERROR: {AiError}");
        return $"AutoAuction AI log — \"{Title}\"\n" + string.Join(Environment.NewLine, lines);
    }

    private string _aiError = string.Empty;
    public string AiError
    {
        get => _aiError;
        set => this.RaiseAndSetIfChanged(ref _aiError, value);
    }

    private AiPreviewViewModel? _aiPreview;
    /// <summary>The AI's proposed fields, awaiting Apply/Discard. Null when there's no pending preview.</summary>
    public AiPreviewViewModel? AiPreview
    {
        get => _aiPreview;
        set
        {
            this.RaiseAndSetIfChanged(ref _aiPreview, value);
            this.RaisePropertyChanged(nameof(HasAiPreview));
        }
    }

    public bool HasAiPreview => _aiPreview is not null;

    public bool CanGenerate => Images.Count > 0;

    // Option sources for the form's combo boxes.
    public IReadOnlyList<ConditionType> ConditionOptions { get; } = Enum.GetValues<ConditionType>();
    public IReadOnlyList<PickupOption> PickupOptions { get; } = Enum.GetValues<PickupOption>();
    public IReadOnlyList<ShippingMethod> ShippingMethodOptions { get; } = Enum.GetValues<ShippingMethod>();
    public IReadOnlyList<ShippingRegion> RegionOptions { get; } = ShippingRegions.All;
    public IReadOnlyList<string> RuralOptions { get; } = ShippingRegions.RuralOptions;
    public IReadOnlyList<int> DurationOptions { get; } = new[] { 2, 3, 4, 5, 6, 7, 10, 14 };

    /// <summary>True when custom shipping options apply (drives their visibility in the form).</summary>
    public bool IsSpecifyShipping => Listing.Shipping == ShippingMethod.Specify;

    public DraftDetailViewModel(
        MainWindowViewModel shell,
        IDraftService draftService,
        IActiveListingProvider activeListing,
        IListingGenerator generator,
        ICategoryService categories,
        DraftFolder draft)
    {
        _shell = shell;
        _draftService = draftService;
        _activeListing = activeListing;
        _generator = generator;
        _categories = categories;
        _folderPath = draft.FolderPath;
        Listing = draft.Listing;

        // Load images BEFORE creating the AI command so its CanExecute sees the photos.
        LoadImages();

        GenerationLog.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(ShowGenerationLog));

        BackCommand = ReactiveCommand.Create(Back);
        OpenFolderCommand = ReactiveCommand.Create(OpenFolder);
        DiscardCommand = ReactiveCommand.Create(Discard);
        MarkListedCommand = ReactiveCommand.Create(MarkListed);
        AddShippingOptionCommand = ReactiveCommand.Create(AddShippingOption);
        RemoveShippingOptionCommand = ReactiveCommand.Create<ShippingOption>(RemoveShippingOption);

        var canGenerate = this.WhenAnyValue(x => x.IsGenerating, generating => !generating && CanGenerate);
        GenerateWithAiCommand = ReactiveCommand.CreateFromTask(GenerateWithAiAsync, canGenerate);
        ApplyAiCommand = ReactiveCommand.Create(ApplyAi);
        DiscardAiCommand = ReactiveCommand.Create(() => { AiPreview = null; AiError = string.Empty; });
        ClearLocksCommand = ReactiveCommand.Create(ClearLocks);
        RotateLeftCommand = ReactiveCommand.Create<DraftImageViewModel>(img => RotateImage(img, clockwise: false));
        RotateRightCommand = ReactiveCommand.Create<DraftImageViewModel>(img => RotateImage(img, clockwise: true));

        // This draft is now the one the Chrome extension bridge should serve.
        _activeListing.ActiveFolderPath = _folderPath;

        AttachChangeTracking();
    }

    private void LoadImages()
    {
        Images.Clear();
        foreach (var fileName in Listing.LocalImagePaths)
            Images.Add(new DraftImageViewModel(Path.Combine(_folderPath, fileName)));
    }

    private void RotateImage(DraftImageViewModel? image, bool clockwise)
    {
        if (image is null)
            return;

        _draftService.RotateImage(_folderPath, image.FileName, clockwise);
        image.ReloadThumbnail();
        SaveStatus = "Image rotated";
    }

    private void AttachChangeTracking()
    {
        Listing.PropertyChanged += OnListingChanged;
        Listing.ShippingOptions.CollectionChanged += OnShippingOptionsChanged;
        foreach (var option in Listing.ShippingOptions)
            option.PropertyChanged += OnShippingOptionChanged;
    }

    private void DetachChangeTracking()
    {
        Listing.PropertyChanged -= OnListingChanged;
        Listing.ShippingOptions.CollectionChanged -= OnShippingOptionsChanged;
        foreach (var option in Listing.ShippingOptions)
            option.PropertyChanged -= OnShippingOptionChanged;
    }

    private void OnListingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ListingModel.Title))
            this.RaisePropertyChanged(nameof(Title));
        if (e.PropertyName == nameof(ListingModel.CategoryId))
            this.RaisePropertyChanged(nameof(CategoryPathDisplay));
        if (e.PropertyName == nameof(ListingModel.Shipping))
            this.RaisePropertyChanged(nameof(IsSpecifyShipping));
        if (e.PropertyName == nameof(ListingModel.LockedGroups))
        {
            this.RaisePropertyChanged(nameof(LockSummary));
            this.RaisePropertyChanged(nameof(HasLocks));
        }

        LockFromUser(ListingFieldApplier.GroupFor(e.PropertyName));
        ScheduleSave();
    }

    private void OnShippingOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        LockFromUser(ListingFieldGroup.Shipping);
        ScheduleSave();
    }

    private void OnShippingOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ShippingOption option in e.OldItems)
                option.PropertyChanged -= OnShippingOptionChanged;

        if (e.NewItems is not null)
            foreach (ShippingOption option in e.NewItems)
                option.PropertyChanged += OnShippingOptionChanged;

        LockFromUser(ListingFieldGroup.Shipping);
        ScheduleSave();
    }

    /// <summary>Locks a group when the user (not AI) changes one of its fields.</summary>
    private void LockFromUser(ListingFieldGroup group)
    {
        if (_applyingAi || group == ListingFieldGroup.None)
            return;
        if (!Listing.LockedGroups.HasFlag(group))
            Listing.LockedGroups |= group; // raises LockedGroups change → refreshes LockSummary
    }

    private void ClearLocks()
    {
        Listing.LockedGroups = ListingFieldGroup.None;
        _shell.StatusMessage = "Locks cleared — AI may fill every field";
    }

    /// <summary>Returns to Home, leaving the draft saved in place.</summary>
    private void Back()
    {
        Close();
        _shell.NavigateToDrafts();
    }

    private void OpenFolder() => SystemFolder.Open(_folderPath);

    /// <summary>Discards this draft: moves its photos back to the inbox and deletes the folder.</summary>
    private void Discard()
    {
        Close();
        var moved = _draftService.DiscardDraft(_folderPath);
        _shell.StatusMessage = moved > 0
            ? $"Draft discarded; {moved} photo(s) returned to inbox"
            : "Draft discarded";
        _shell.NavigateToHome();
    }

    /// <summary>Moves this draft into 3_Listed and opens the Listed page.</summary>
    private void MarkListed()
    {
        Close();
        _draftService.MarkListed(_folderPath);
        _shell.StatusMessage = $"Listed: {Title}";
        _shell.NavigateToListed();
    }

    /// <summary>Runs AI generation on the draft's photos; result lands in <see cref="AiPreview"/>.</summary>
    private async Task GenerateWithAiAsync()
    {
        AiError = string.Empty;
        AiPreview = null;
        GenerationLog.Clear();
        IsGenerating = true;

        // Progress is created on the UI thread, so its callbacks marshal back here for live updates.
        var progress = new Progress<string>(line =>
            GenerationLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}"));

        try
        {
            var result = await _generator.GenerateAsync(new DraftFolder(_folderPath, Listing), progress);
            if (result.Ok && result.Fields is not null)
                AiPreview = new AiPreviewViewModel(result.Fields);
            else
                AiError = result.Error ?? "AI generation failed.";
        }
        catch (Exception ex)
        {
            AiError = ex.Message;
        }
        finally
        {
            IsGenerating = false;
        }
    }

    /// <summary>
    /// Writes the previewed AI fields into the listing (auto-save then persists them). Locked groups
    /// are preserved — the applier skips them. The <see cref="_applyingAi"/> guard stops the
    /// change-tracking from re-locking the groups AI just filled.
    /// </summary>
    private void ApplyAi()
    {
        if (AiPreview is null)
            return;

        _applyingAi = true;
        try
        {
            ListingFieldApplier.Apply(Listing, AiPreview.Fields, respectLocks: true);
        }
        finally
        {
            _applyingAi = false;
        }

        _shell.StatusMessage = LockSummary.Length > 0
            ? $"Applied AI draft (kept: {LockSummary})"
            : "Applied AI draft";
        AiPreview = null;
    }

    private void AddShippingOption()
    {
        // Default a unique Rural so a second nationwide row doesn't collide (TradeMe rejects
        // duplicate region+rural rows): Any → Urban → Rural.
        var used = Listing.ShippingOptions.Select(o => o.Rural).ToHashSet();
        var rural = ShippingRegions.RuralOptions.FirstOrDefault(r => !used.Contains(r))
                    ?? ShippingRegions.DefaultRural;
        Listing.ShippingOptions.Add(new ShippingOption {Rural = rural});
    }

    private void RemoveShippingOption(ShippingOption? option)
    {
        if (option is not null)
            Listing.ShippingOptions.Remove(option);
    }

    /// <summary>Stops auto-save/tracking and clears the active listing before leaving the page.</summary>
    private void Close()
    {
        if (_closed)
            return;
        _closed = true;

        _saveCts?.Cancel();
        DetachChangeTracking();

        if (_activeListing.ActiveFolderPath == _folderPath)
            _activeListing.ActiveFolderPath = null;
    }

    private async void ScheduleSave()
    {
        if (_closed)
            return;

        SaveStatus = "Saving...";

        _saveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveCts = cts;

        try
        {
            await Task.Delay(SaveDebounceMs, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return; // a newer change superseded this one
        }

        try
        {
            await Task.Run(() => _draftService.SaveListing(_folderPath, Listing));
            SaveStatus = "Saved";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Save failed: {ex.Message}";
        }
    }
}
