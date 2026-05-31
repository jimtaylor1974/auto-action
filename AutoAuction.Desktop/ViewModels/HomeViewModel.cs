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
/// Home page: the Inbox gallery on the left and the Drafts table on the right.
/// Creating or opening a draft navigates (via the shell) to the Draft Detail page.
/// </summary>
public class HomeViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _shell;
    private readonly IDraftService _draftService;
    private readonly IAppConfig _config;

    private readonly BehaviorSubject<bool> _hasSelectionSubject = new(false);

    public ObservableCollection<InboxImageViewModel> InboxImages { get; } = new();
    public ObservableCollection<DraftListItemViewModel> Drafts { get; } = new();

    private DraftListItemViewModel? _selectedDraft;
    /// <summary>Selected row in the Drafts table (opened via double-tap / Open command).</summary>
    public DraftListItemViewModel? SelectedDraft
    {
        get => _selectedDraft;
        set => this.RaiseAndSetIfChanged(ref _selectedDraft, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshInboxCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshDraftsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenInboxFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateDraftCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSelectedDraftCommand { get; }

    public HomeViewModel(MainWindowViewModel shell, IDraftService draftService, IAppConfig config)
    {
        _shell = shell;
        _draftService = draftService;
        _config = config;

        RefreshInboxCommand = ReactiveCommand.Create(RefreshInbox);
        RefreshDraftsCommand = ReactiveCommand.Create(RefreshDrafts);
        OpenInboxFolderCommand = ReactiveCommand.Create(OpenInboxFolder);
        CreateDraftCommand = ReactiveCommand.Create(CreateDraft, _hasSelectionSubject);
        OpenSelectedDraftCommand = ReactiveCommand.Create(OpenSelectedDraft);

        RefreshInbox();
        RefreshDrafts();
    }

    /// <summary>True when at least one inbox image is ticked.</summary>
    public bool HasSelection => InboxImages.Any(i => i.IsSelected);

    public string InboxPath => _config.InboxPath;

    /// <summary>Opens the given draft in the Draft Detail page.</summary>
    public void OpenDraft(DraftListItemViewModel? item)
    {
        if (item is not null)
            _shell.NavigateToDraft(item.Draft);
    }

    private void OpenSelectedDraft() => OpenDraft(SelectedDraft);

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

        this.RaisePropertyChanged(nameof(HasSelection));
        _hasSelectionSubject.OnNext(HasSelection);
        UpdateInboxStatus();
    }

    public void RefreshDrafts()
    {
        Drafts.Clear();
        foreach (var draft in _draftService.GetDrafts())
            Drafts.Add(new DraftListItemViewModel(draft));
    }

    private void CreateDraft()
    {
        var selectedPaths = InboxImages
            .Where(i => i.IsSelected)
            .Select(i => i.FullPath)
            .ToList();

        if (selectedPaths.Count == 0)
            return;

        var draft = _draftService.CreateDraft(selectedPaths);

        RefreshInbox();
        RefreshDrafts();

        _shell.StatusMessage = $"Created draft from {selectedPaths.Count} photo(s)";

        // Open the freshly created draft straight away.
        _shell.NavigateToDraft(draft);
    }

    private void OnInboxImageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InboxImageViewModel.IsSelected))
        {
            this.RaisePropertyChanged(nameof(HasSelection));
            _hasSelectionSubject.OnNext(HasSelection);
            UpdateInboxStatus();
        }
    }

    private void UpdateInboxStatus()
    {
        var selected = InboxImages.Count(i => i.IsSelected);
        _shell.StatusMessage = selected > 0
            ? $"{selected} of {InboxImages.Count} photo(s) selected"
            : $"{InboxImages.Count} photo(s) in inbox";
    }
}
