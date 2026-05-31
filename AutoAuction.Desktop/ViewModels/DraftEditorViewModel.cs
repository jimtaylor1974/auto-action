using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoAuction.Core.Models;
using AutoAuction.Core.Services;
using AutoAuction.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>
/// Editor for a single draft. Binds a <see cref="ListingModel"/> to the form and
/// auto-saves any change back to the draft's <c>listing.json</c> (debounced).
/// </summary>
public partial class DraftEditorViewModel : ViewModelBase
{
    private readonly IDraftService _draftService;
    private readonly string _folderPath;

    // Debounce so we write the file once the user pauses typing, not on every keystroke.
    private CancellationTokenSource? _saveCts;
    private const int SaveDebounceMs = 500;

    public ListingModel Listing { get; }

    public ObservableCollection<DraftImageViewModel> Images { get; } = new();

    /// <summary>Status text shown in the editor (e.g. "Saved").</summary>
    [ObservableProperty]
    private string _saveStatus = string.Empty;

    // Option sources for the form's combo boxes.
    public IReadOnlyList<ConditionType> ConditionOptions { get; } =
        Enum.GetValues<ConditionType>();

    public IReadOnlyList<PickupOption> PickupOptions { get; } =
        Enum.GetValues<PickupOption>();

    public IReadOnlyList<int> DurationOptions { get; } =
        new[] { 2, 3, 4, 5, 6, 7, 10, 14 };

    public DraftEditorViewModel(IDraftService draftService, DraftFolder draft)
    {
        _draftService = draftService;
        _folderPath = draft.FolderPath;
        Listing = draft.Listing;

        LoadImages();
        AttachChangeTracking();
    }

    private void LoadImages()
    {
        Images.Clear();
        foreach (var fileName in Listing.LocalImagePaths)
        {
            var fullPath = Path.Combine(_folderPath, fileName);
            Images.Add(new DraftImageViewModel(fullPath));
        }
    }

    /// <summary>
    /// Wires up auto-save: any change to the listing, the shipping options collection,
    /// or an individual shipping option triggers a debounced save.
    /// </summary>
    private void AttachChangeTracking()
    {
        Listing.PropertyChanged += OnListingChanged;
        Listing.ShippingOptions.CollectionChanged += OnShippingOptionsChanged;
        foreach (var option in Listing.ShippingOptions)
            option.PropertyChanged += OnShippingOptionChanged;
    }

    private void OnListingChanged(object? sender, PropertyChangedEventArgs e) => ScheduleSave();

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

    /// <summary>
    /// Raised after the user discards this draft. The argument is the number of photos
    /// moved back into the inbox. The owner uses this to refresh its lists and close the editor.
    /// </summary>
    public event Action<int>? Discarded;

    /// <summary>Opens this draft's folder in the OS file manager.</summary>
    [RelayCommand]
    private void OpenFolder() => SystemFolder.Open(_folderPath);

    /// <summary>
    /// Discards this draft: cancels any pending auto-save, moves its photos back to the
    /// inbox, deletes the folder, and notifies the owner via <see cref="Discarded"/>.
    /// </summary>
    [RelayCommand]
    private void Discard()
    {
        // Cancel a debounced save so it can't recreate listing.json after we delete the folder.
        _saveCts?.Cancel();

        var moved = _draftService.DiscardDraft(_folderPath);
        Discarded?.Invoke(moved);
    }

    [RelayCommand]
    private void AddShippingOption() => Listing.ShippingOptions.Add(new ShippingOption());

    [RelayCommand]
    private void RemoveShippingOption(ShippingOption? option)
    {
        if (option is not null)
            Listing.ShippingOptions.Remove(option);
    }

    private async void ScheduleSave()
    {
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
