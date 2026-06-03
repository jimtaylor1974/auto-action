using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using AutoAuction.Core.Services;
using AutoAuction.Desktop.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>
/// Home page: the Inbox gallery. The user ticks photos then chooses to turn them into either
/// one multi-photo listing or one listing per photo. Drafts themselves live on the Drafts page.
/// </summary>
public class HomeViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _shell;
    private readonly IDraftService _draftService;
    private readonly IAppConfig _config;

    private readonly BehaviorSubject<bool> _hasSelectionSubject = new(false);

    public ObservableCollection<InboxImageViewModel> InboxImages { get; } = new();

    public ReactiveCommand<Unit, Unit> RefreshInboxCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenInboxFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateOneListingCommand { get; }
    public ReactiveCommand<Unit, Unit> CreatePerPhotoCommand { get; }

    public HomeViewModel(MainWindowViewModel shell, IDraftService draftService, IAppConfig config)
    {
        _shell = shell;
        _draftService = draftService;
        _config = config;

        RefreshInboxCommand = ReactiveCommand.Create(RefreshInbox);
        OpenInboxFolderCommand = ReactiveCommand.Create(OpenInboxFolder);
        CreateOneListingCommand = ReactiveCommand.Create(CreateOneListing, _hasSelectionSubject);
        CreatePerPhotoCommand = ReactiveCommand.Create(CreatePerPhoto, _hasSelectionSubject);

        RefreshInbox();
    }

    /// <summary>True when at least one inbox image is ticked.</summary>
    public bool HasSelection => InboxImages.Any(i => i.IsSelected);

    private int SelectedCount => InboxImages.Count(i => i.IsSelected);

    /// <summary>Label for the "make one listing from all selected photos" button.</summary>
    public string CreateOneListingText => $"Create 1 listing ({SelectedCount} photos)";

    /// <summary>Label for the "one listing per selected photo" button.</summary>
    public string CreatePerPhotoText => $"Create {SelectedCount} listings (1 photo each)";

    /// <summary>The per-photo option only makes sense for 2+ photos.</summary>
    public bool ShowPerPhoto => SelectedCount > 1;

    public string InboxPath => _config.InboxPath;

    /// <summary>Imports external image files (drag-drop or file picker) into the inbox.</summary>
    public void ImportPhotos(IEnumerable<string> paths)
    {
        var count = _draftService.ImportToInbox(paths);
        RefreshInbox();
        _shell.StatusMessage = count > 0
            ? $"Imported {count} photo(s) into the inbox"
            : "No image files were imported";
    }

    private void OpenInboxFolder()
    {
        Directory.CreateDirectory(_config.InboxPath);
        SystemFolder.Open(_config.InboxPath);
    }

    public void RefreshInbox()
    {
        foreach (var image in InboxImages)
            image.PropertyChanged -= OnInboxImageChanged;
        InboxImages.Clear();

        foreach (var path in _draftService.GetInboxImages())
        {
            var vm = new InboxImageViewModel(path);
            vm.PropertyChanged += OnInboxImageChanged;
            InboxImages.Add(vm);
        }

        RaiseSelectionState();
        UpdateInboxStatus();
    }

    private List<string> SelectedPaths() =>
        InboxImages.Where(i => i.IsSelected).Select(i => i.FullPath).ToList();

    /// <summary>Moves all selected photos into a single new draft and opens it.</summary>
    private void CreateOneListing()
    {
        var selectedPaths = SelectedPaths();
        if (selectedPaths.Count == 0)
            return;

        var draft = _draftService.CreateDraft(selectedPaths);
        RefreshInbox();
        _shell.StatusMessage = $"Created 1 listing from {selectedPaths.Count} photo(s)";
        _shell.NavigateToDraft(draft);
    }

    /// <summary>Creates one draft per selected photo, then lands on the Drafts page to batch them.</summary>
    private void CreatePerPhoto()
    {
        var selectedPaths = SelectedPaths();
        if (selectedPaths.Count == 0)
            return;

        foreach (var path in selectedPaths)
            _draftService.CreateDraft(new[] { path });

        RefreshInbox();
        _shell.StatusMessage = $"Created {selectedPaths.Count} listing(s), one per photo";
        _shell.NavigateToDrafts();
    }

    private void OnInboxImageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InboxImageViewModel.IsSelected))
        {
            RaiseSelectionState();
            UpdateInboxStatus();
        }
    }

    private void RaiseSelectionState()
    {
        this.RaisePropertyChanged(nameof(HasSelection));
        this.RaisePropertyChanged(nameof(CreateOneListingText));
        this.RaisePropertyChanged(nameof(CreatePerPhotoText));
        this.RaisePropertyChanged(nameof(ShowPerPhoto));
        _hasSelectionSubject.OnNext(HasSelection);
    }

    private void UpdateInboxStatus()
    {
        var selected = SelectedCount;
        _shell.StatusMessage = selected > 0
            ? $"{selected} of {InboxImages.Count} photo(s) selected"
            : $"{InboxImages.Count} photo(s) in inbox";
    }
}
