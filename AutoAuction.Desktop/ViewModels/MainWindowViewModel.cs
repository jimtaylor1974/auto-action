using System;
using System.Reactive;
using System.Threading.Tasks;
using AutoAuction.Core.Services;
using Avalonia.Threading;
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
    private readonly OpenAiClient _openAi;
    private readonly ICategoryService _categoryService;
    private readonly IPreflightService _preflight;
    private readonly IListingGenerator _generator;

    private AppPage _currentPageEnum = AppPage.Home;

    public MainWindowViewModel(
        IDraftService draftService,
        IAppConfig config,
        ISettingsService settings,
        IActiveListingProvider activeListing,
        LocalBridgeServer server,
        OpenAiClient openAi,
        ICategoryService categoryService,
        IPreflightService preflight,
        IListingGenerator generator)
    {
        _draftService = draftService;
        _config = config;
        _settings = settings;
        _activeListing = activeListing;
        _server = server;
        _openAi = openAi;
        _categoryService = categoryService;
        _preflight = preflight;
        _generator = generator;

        NavigateToHomeCommand = ReactiveCommand.Create(NavigateToHome);
        NavigateToListedCommand = ReactiveCommand.Create(NavigateToListed);
        NavigateToSettingsCommand = ReactiveCommand.Create(NavigateToSettings);
        RecheckPreflightCommand = ReactiveCommand.CreateFromTask(RunPreflightAsync);
        ExitCommand = ReactiveCommand.Create(Exit);

        // The bridge server raises this (off the UI thread) when the extension publishes a listing.
        _server.ListingPublished += OnListingPublished;
    }

    private void OnListingPublished(DraftFolder listed)
    {
        void Apply()
        {
            StatusMessage = $"Listed on TradeMe: {listed.Listing.Title}";
            NavigateToListed();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
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
    public ReactiveCommand<Unit, Unit> RecheckPreflightCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    // --- Preflight banner ------------------------------------------------------------------
    // A shell-wide nudge shown when launch checks fail (no internet, no categories, no key).

    private bool _hasPreflightIssues;
    public bool HasPreflightIssues
    {
        get => _hasPreflightIssues;
        set => this.RaiseAndSetIfChanged(ref _hasPreflightIssues, value);
    }

    private string _preflightSummary = string.Empty;
    public string PreflightSummary
    {
        get => _preflightSummary;
        set => this.RaiseAndSetIfChanged(ref _preflightSummary, value);
    }

    // Selection state for the navigation rail highlight.
    public bool IsHomeSelected => _currentPageEnum == AppPage.Home;
    public bool IsListedSelected => _currentPageEnum == AppPage.Listed;
    public bool IsSettingsSelected => _currentPageEnum == AppPage.Settings;

    /// <summary>Called once the window is shown to display the first page.</summary>
    public void Initialize()
    {
        NavigateToHome();
        _ = RunPreflightAsync();
    }

    /// <summary>
    /// Runs the launch checks off the UI thread and updates the banner. Safe to call
    /// fire-and-forget; property writes are marshalled back to the UI thread.
    /// </summary>
    private async Task RunPreflightAsync()
    {
        var report = await _preflight.RunAsync();

        void Apply()
        {
            HasPreflightIssues = !report.AllOk;
            PreflightSummary = report.AllOk
                ? string.Empty
                : $"Setup needed: {report.FailureSummary}. Open Settings to finish.";
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void SetPage(AppPage page)
    {
        _currentPageEnum = page;
        this.RaisePropertyChanged(nameof(IsHomeSelected));
        this.RaisePropertyChanged(nameof(IsListedSelected));
        this.RaisePropertyChanged(nameof(IsSettingsSelected));
    }

    public void NavigateToHome()
    {
        // Re-check after the user has been in Settings (where they'd fix a failed check).
        var cameFromSettings = _currentPageEnum == AppPage.Settings;

        _activeListing.ActiveFolderPath = null;
        SetPage(AppPage.Home);
        CurrentPage = new HomeViewModel(this, _draftService, _config);
        StatusMessage = "Home";

        if (cameFromSettings)
            _ = RunPreflightAsync();
    }

    public void NavigateToDraft(DraftFolder draft)
    {
        SetPage(AppPage.DraftDetail);
        CurrentPage = new DraftDetailViewModel(this, _draftService, _activeListing, _generator, _categoryService, draft);
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
        CurrentPage = new SettingsViewModel(_settings, _openAi, _server, _config, _categoryService);
        StatusMessage = "Settings";
    }

    private static void Exit() => Environment.Exit(0);
}
