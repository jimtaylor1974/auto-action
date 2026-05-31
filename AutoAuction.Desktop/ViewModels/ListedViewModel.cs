using System.Collections.ObjectModel;
using System.Reactive;
using AutoAuction.Core.Services;
using AutoAuction.Desktop.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>
/// Listed page: a table of items under 3_Listed with per-row Mark Sold / Relist actions.
/// </summary>
public class ListedViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _shell;
    private readonly IDraftService _draftService;

    public ObservableCollection<DraftListItemViewModel> Listed { get; } = new();

    private DraftListItemViewModel? _selectedItem;
    public DraftListItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<DraftListItemViewModel, Unit> MarkSoldCommand { get; }
    public ReactiveCommand<DraftListItemViewModel, Unit> RelistCommand { get; }
    public ReactiveCommand<DraftListItemViewModel, Unit> ViewOnTradeMeCommand { get; }

    public ListedViewModel(MainWindowViewModel shell, IDraftService draftService)
    {
        _shell = shell;
        _draftService = draftService;

        RefreshCommand = ReactiveCommand.Create(Refresh);
        MarkSoldCommand = ReactiveCommand.Create<DraftListItemViewModel>(MarkSold);
        RelistCommand = ReactiveCommand.Create<DraftListItemViewModel>(Relist);
        ViewOnTradeMeCommand = ReactiveCommand.Create<DraftListItemViewModel>(ViewOnTradeMe);

        Refresh();
    }

    private static void ViewOnTradeMe(DraftListItemViewModel? item)
    {
        if (!string.IsNullOrWhiteSpace(item?.TradeMeListingUrl))
            SystemFolder.OpenUrl(item.TradeMeListingUrl);
    }

    public void Refresh()
    {
        Listed.Clear();
        foreach (var item in _draftService.GetListed())
            Listed.Add(new DraftListItemViewModel(item));
    }

    private void MarkSold(DraftListItemViewModel? item)
    {
        if (item is null)
            return;

        _draftService.MarkSold(item.Draft.FolderPath);
        _shell.StatusMessage = $"Marked sold: {item.DisplayTitle}";
        Refresh();
    }

    private void Relist(DraftListItemViewModel? item)
    {
        if (item is null)
            return;

        var draft = _draftService.Relist(item.Draft.FolderPath);
        _shell.StatusMessage = $"Relisted as draft: {item.DisplayTitle}";
        _shell.NavigateToDraft(draft);
    }
}
