using System;
using System.Reactive;
using AutoAuction.Core.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

public enum AppPage
{
    Home,
    DraftDetail,
    Listed,
    Settings
}

/// <summary>
/// Shell coordinator. Owns the navigation rail/top menu, swaps the active page into
/// <see cref="CurrentPage"/>, and builds page view models (passing itself for navigation).
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private readonly IDraftService _draftService;
    private readonly IAppConfig _config;
    private readonly ISettingsService _settings;
    private readonly IActiveListingProvider _activeListing;
    private readonly LocalBridgeServer _server;
    private readonly AiClientFactory _aiFactory;

    private AppPage _currentPageEnum = AppPage.Home;

    public MainWindowViewModel(
        IDraftService draftService,
        IAppConfig config,
        ISettingsService settings,
        IActiveListingProvider activeListing,
        LocalBridgeServer server,
        AiClientFactory aiFactory)
    {
        _draftService = draftService;
        _config = config;
        _settings = settings;
        _activeListing = activeListing;
        _server = server;
        _aiFactory = aiFactory;

        NavigateToHomeCommand = ReactiveCommand.Create(NavigateToHome);
        NavigateToListedCommand = ReactiveCommand.Create(NavigateToListed);
        NavigateToSettingsCommand = ReactiveCommand.Create(NavigateToSettings);
        ExitCommand = ReactiveCommand.Create(Exit);
    }

    private ViewModelBase? _currentPage;
    public ViewModelBase? CurrentPage
    {
        get => _currentPage;
        set => this.RaiseAndSetIfChanged(ref _currentPage, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ReactiveCommand<Unit, Unit> NavigateToHomeCommand { get; }
    public ReactiveCommand<Unit, Unit> NavigateToListedCommand { get; }
    public ReactiveCommand<Unit, Unit> NavigateToSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    // Selection state for the navigation rail highlight.
    public bool IsHomeSelected => _currentPageEnum == AppPage.Home;
    public bool IsListedSelected => _currentPageEnum == AppPage.Listed;
    public bool IsSettingsSelected => _currentPageEnum == AppPage.Settings;

    /// <summary>Called once the window is shown to display the first page.</summary>
    public void Initialize() => NavigateToHome();

    private void SetPage(AppPage page)
    {
        _currentPageEnum = page;
        this.RaisePropertyChanged(nameof(IsHomeSelected));
        this.RaisePropertyChanged(nameof(IsListedSelected));
        this.RaisePropertyChanged(nameof(IsSettingsSelected));
    }

    public void NavigateToHome()
    {
        _activeListing.ActiveFolderPath = null;
        SetPage(AppPage.Home);
        CurrentPage = new HomeViewModel(this, _draftService, _config);
        StatusMessage = "Home";
    }

    public void NavigateToDraft(DraftFolder draft)
    {
        SetPage(AppPage.DraftDetail);
        CurrentPage = new DraftDetailViewModel(this, _draftService, _activeListing, draft);
        StatusMessage = "Editing draft";
    }

    public void NavigateToListed()
    {
        _activeListing.ActiveFolderPath = null;
        SetPage(AppPage.Listed);
        CurrentPage = new ListedViewModel(this, _draftService);
        StatusMessage = "Listed";
    }

    public void NavigateToSettings()
    {
        SetPage(AppPage.Settings);
        CurrentPage = new SettingsViewModel(_settings, _aiFactory, _server, _config);
        StatusMessage = "Settings";
    }

    private static void Exit() => Environment.Exit(0);
}
