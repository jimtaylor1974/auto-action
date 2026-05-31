using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using AutoAuction.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>
/// Root view model. Owns the Inbox gallery and the Drafts list on the left, and the
/// currently open <see cref="DraftEditorViewModel"/> on the right.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAppConfig _config;
    private readonly IDraftService _draftService;

    public ObservableCollection<InboxImageViewModel> InboxImages { get; } = new();
    public ObservableCollection<DraftListItemViewModel> Drafts { get; } = new();

    /// <summary>The draft currently open in the editor panel (null shows a placeholder).</summary>
    [ObservableProperty]
    private DraftEditorViewModel? _currentDraft;

    /// <summary>The selected row in the Drafts list. Setting it opens that draft.</summary>
    [ObservableProperty]
    private DraftListItemViewModel? _selectedDraft;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public MainWindowViewModel(IAppConfig config, IDraftService draftService)
    {
        _config = config;
        _draftService = draftService;

        RefreshInbox();
        RefreshDrafts();
    }

    /// <summary>True when at least one inbox image is ticked.</summary>
    public bool HasSelection => InboxImages.Any(i => i.IsSelected);

    /// <summary>The folder photos should be dropped into. Shown in the UI as a hint.</summary>
    public string InboxPath => _config.InboxPath;

    partial void OnSelectedDraftChanged(DraftListItemViewModel? value)
    {
        if (value is null)
        {
            CurrentDraft = null;
            return;
        }

        CurrentDraft = new DraftEditorViewModel(_draftService, value.Draft);
        StatusMessage = $"Editing: {value.DisplayTitle}";
    }

    [RelayCommand]
    private void RefreshInbox()
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

        CreateDraftCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelection));
        UpdateInboxStatus();
    }

    [RelayCommand]
    private void RefreshDrafts()
    {
        Drafts.Clear();
        foreach (var draft in _draftService.GetDrafts())
            Drafts.Add(new DraftListItemViewModel(draft));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CreateDraft()
    {
        var selectedPaths = InboxImages
            .Where(i => i.IsSelected)
            .Select(i => i.FullPath)
            .ToList();

        if (selectedPaths.Count == 0)
            return;

        var draft = _draftService.CreateDraft(selectedPaths);

        // The images were moved out of the Inbox; refresh both lists to reflect new state.
        RefreshInbox();
        RefreshDrafts();

        // Open the freshly created draft in the editor.
        var newItem = Drafts.FirstOrDefault(d => d.Draft.FolderPath == draft.FolderPath);
        if (newItem is not null)
            SelectedDraft = newItem;

        StatusMessage = $"Created draft from {selectedPaths.Count} photo(s)";
    }

    private void OnInboxImageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InboxImageViewModel.IsSelected))
        {
            CreateDraftCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasSelection));
            UpdateInboxStatus();
        }
    }

    private void UpdateInboxStatus()
    {
        var selected = InboxImages.Count(i => i.IsSelected);
        StatusMessage = selected > 0
            ? $"{selected} of {InboxImages.Count} photo(s) selected"
            : $"{InboxImages.Count} photo(s) in inbox";
    }
}
