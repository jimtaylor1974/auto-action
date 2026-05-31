using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
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
    private readonly MainWindowViewModel _shell;
    private string _folderPath;

    // Debounce so we write the file once the user pauses typing, not on every keystroke.
    private CancellationTokenSource? _saveCts;
    private const int SaveDebounceMs = 500;
    private bool _closed;

    public ListingModel Listing { get; }

    public ObservableCollection<DraftImageViewModel> Images { get; } = new();

    public string Title => string.IsNullOrWhiteSpace(Listing.Title) ? "(untitled draft)" : Listing.Title;

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

    // Option sources for the form's combo boxes.
    public IReadOnlyList<ConditionType> ConditionOptions { get; } = Enum.GetValues<ConditionType>();
    public IReadOnlyList<PickupOption> PickupOptions { get; } = Enum.GetValues<PickupOption>();
    public IReadOnlyList<int> DurationOptions { get; } = new[] { 2, 3, 4, 5, 6, 7, 10, 14 };

    public DraftDetailViewModel(
        MainWindowViewModel shell,
        IDraftService draftService,
        IActiveListingProvider activeListing,
        DraftFolder draft)
    {
        _shell = shell;
        _draftService = draftService;
        _activeListing = activeListing;
        _folderPath = draft.FolderPath;
        Listing = draft.Listing;

        BackCommand = ReactiveCommand.Create(Back);
        OpenFolderCommand = ReactiveCommand.Create(OpenFolder);
        DiscardCommand = ReactiveCommand.Create(Discard);
        MarkListedCommand = ReactiveCommand.Create(MarkListed);
        AddShippingOptionCommand = ReactiveCommand.Create(AddShippingOption);
        RemoveShippingOptionCommand = ReactiveCommand.Create<ShippingOption>(RemoveShippingOption);

        // This draft is now the one the Chrome extension bridge should serve.
        _activeListing.ActiveFolderPath = _folderPath;

        LoadImages();
        AttachChangeTracking();
    }

    private void LoadImages()
    {
        Images.Clear();
        foreach (var fileName in Listing.LocalImagePaths)
            Images.Add(new DraftImageViewModel(Path.Combine(_folderPath, fileName)));
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
        ScheduleSave();
    }

    private void OnShippingOptionChanged(object? sender, PropertyChangedEventArgs e) => ScheduleSave();

    private void OnShippingOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ShippingOption option in e.OldItems)
                option.PropertyChanged -= OnShippingOptionChanged;

        if (e.NewItems is not null)
            foreach (ShippingOption option in e.NewItems)
                option.PropertyChanged += OnShippingOptionChanged;

        ScheduleSave();
    }

    /// <summary>Returns to Home, leaving the draft saved in place.</summary>
    private void Back()
    {
        Close();
        _shell.NavigateToHome();
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

    private void AddShippingOption() => Listing.ShippingOptions.Add(new ShippingOption());

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
