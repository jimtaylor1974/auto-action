using System.Net;
using System.Net.Sockets;
using System.Reactive;
using AutoAuction.Core.Services;
using AutoAuction.Desktop.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>
/// Settings page: local bridge server port/URL, OpenAI key + model, and general info.
/// Persists to <c>settings.json</c> in the app root folder.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly OpenAiClient _openAi;
    private readonly LocalBridgeServer _server;
    private readonly IAppConfig _config;
    private readonly ICategoryService _categoryService;

    private int _serverPort;
    public int ServerPort
    {
        get => _serverPort;
        set
        {
            this.RaiseAndSetIfChanged(ref _serverPort, value);
            this.RaisePropertyChanged(nameof(ServerUrl));
        }
    }

    private bool _serverAutoStart;
    public bool ServerAutoStart
    {
        get => _serverAutoStart;
        set => this.RaiseAndSetIfChanged(ref _serverAutoStart, value);
    }

    public string ServerUrl => $"http://localhost:{ServerPort}";

    public bool IsServerRunning => _server.IsRunning;

    public string ServerStatus => _server.IsRunning
        ? $"Running on {_server.Url}"
        : _server.LastError is null ? "Stopped" : $"Error: {_server.LastError}";

    // --- OpenAI API key (set-only) ---------------------------------------------------------
    // The real key is never read back into the UI; it lives encrypted in the OS secret store.
    // HasKey + IsEditingKey drive whether we show the masked "key is set" row or an entry box.

    private bool _hasKey;
    public bool HasKey
    {
        get => _hasKey;
        set
        {
            this.RaiseAndSetIfChanged(ref _hasKey, value);
            RaiseKeyStateChanged();
        }
    }

    private bool _isEditingKey;
    public bool IsEditingKey
    {
        get => _isEditingKey;
        set
        {
            this.RaiseAndSetIfChanged(ref _isEditingKey, value);
            RaiseKeyStateChanged();
        }
    }

    private string _newKey = string.Empty;
    /// <summary>Bound to the entry box while adding/replacing a key; cleared after it's stored.</summary>
    public string NewKey
    {
        get => _newKey;
        set => this.RaiseAndSetIfChanged(ref _newKey, value);
    }

    /// <summary>Show the entry box when no key is stored, or when replacing one.</summary>
    public bool ShowKeyEntry => !HasKey || IsEditingKey;

    /// <summary>Show the masked "key is set" row when a key exists and we're not editing.</summary>
    public bool ShowKeyMasked => HasKey && !IsEditingKey;

    /// <summary>Only offer Cancel when there's an existing key to fall back to.</summary>
    public bool CanCancelKeyEdit => HasKey && IsEditingKey;

    private void RaiseKeyStateChanged()
    {
        this.RaisePropertyChanged(nameof(ShowKeyEntry));
        this.RaisePropertyChanged(nameof(ShowKeyMasked));
        this.RaisePropertyChanged(nameof(CanCancelKeyEdit));
    }

    private string? _openAiModel;
    public string? OpenAiModel
    {
        get => _openAiModel;
        set => this.RaiseAndSetIfChanged(ref _openAiModel, value);
    }

    // --- Quick import folder (synced cloud folder) -----------------------------------------

    private string? _cloudPhotosPath;
    /// <summary>Synced cloud folder the Home "quick import" button opens into.</summary>
    public string? CloudPhotosPath
    {
        get => _cloudPhotosPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _cloudPhotosPath, value);
            this.RaisePropertyChanged(nameof(ShowDetectedSuggestion));
        }
    }

    /// <summary>A cloud folder detected on this PC, offered as a one-click suggestion (not auto-applied).</summary>
    public string? DetectedCloudFolder { get; } = SuggestDefaultCloudFolder();

    /// <summary>Show the "use detected folder" suggestion only when it differs from the current value.</summary>
    public bool ShowDetectedSuggestion =>
        !string.IsNullOrEmpty(DetectedCloudFolder) &&
        !string.Equals(DetectedCloudFolder, CloudPhotosPath?.Trim(), StringComparison.OrdinalIgnoreCase);

    private string? _cloudPhotosLabel;
    /// <summary>Friendly label for the cloud button, e.g. "OneDrive".</summary>
    public string? CloudPhotosLabel
    {
        get => _cloudPhotosLabel;
        set => this.RaiseAndSetIfChanged(ref _cloudPhotosLabel, value);
    }

    private string _testResult = string.Empty;
    public string TestResult
    {
        get => _testResult;
        set => this.RaiseAndSetIfChanged(ref _testResult, value);
    }

    private string _saveStatus = string.Empty;
    public string SaveStatus
    {
        get => _saveStatus;
        set => this.RaiseAndSetIfChanged(ref _saveStatus, value);
    }

    private string _categoriesStatus = string.Empty;
    public string CategoriesStatus
    {
        get => _categoriesStatus;
        set => this.RaiseAndSetIfChanged(ref _categoriesStatus, value);
    }

    public string RootPath => _config.RootPath;

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> FindFreePortCommand { get; }
    public ReactiveCommand<Unit, Unit> StartServerCommand { get; }
    public ReactiveCommand<Unit, Unit> StopServerCommand { get; }
    public ReactiveCommand<Unit, Unit> TestCommand { get; }
    public ReactiveCommand<Unit, Unit> ChangeKeyCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelKeyEditCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveKeyCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearKeyCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenRootFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> FetchCategoriesCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCloudFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> UseDetectedFolderCommand { get; }

    public SettingsViewModel(
        ISettingsService settings,
        OpenAiClient openAi,
        LocalBridgeServer server,
        IAppConfig config,
        ICategoryService categoryService)
    {
        _settings = settings;
        _openAi = openAi;
        _server = server;
        _config = config;
        _categoryService = categoryService;

        var current = settings.Current;
        _serverPort = current.ServerPort;
        _serverAutoStart = current.ServerAutoStart;
        _openAiModel = current.OpenAiModel;
        _cloudPhotosPath = current.CloudPhotosPath;
        _cloudPhotosLabel = current.CloudPhotosLabel;
        _hasKey = _openAi.HasApiKey;

        SaveCommand = ReactiveCommand.Create(Save);
        FindFreePortCommand = ReactiveCommand.Create(FindFreePort);
        StartServerCommand = ReactiveCommand.Create(StartServer);
        StopServerCommand = ReactiveCommand.Create(StopServer);
        TestCommand = ReactiveCommand.CreateFromTask(TestAsync);
        ChangeKeyCommand = ReactiveCommand.Create(BeginKeyEdit);
        CancelKeyEditCommand = ReactiveCommand.Create(CancelKeyEdit);
        SaveKeyCommand = ReactiveCommand.Create(SaveKey);
        ClearKeyCommand = ReactiveCommand.Create(ClearKey);
        OpenRootFolderCommand = ReactiveCommand.Create(OpenRootFolder);
        FetchCategoriesCommand = ReactiveCommand.CreateFromTask(FetchCategoriesAsync);
        ClearCloudFolderCommand = ReactiveCommand.Create(ClearCloudFolder);
        UseDetectedFolderCommand = ReactiveCommand.Create(() => { CloudPhotosPath = DetectedCloudFolder; });

        CategoriesStatus = DescribeCachedCategories();
    }

    private string DescribeCachedCategories()
    {
        var last = _categoryService.LastFetchedUtc;
        return last is null
            ? "No category catalogue downloaded yet."
            : $"Catalogue cached {last.Value.ToLocalTime():g}.";
    }

    private async System.Threading.Tasks.Task FetchCategoriesAsync()
    {
        CategoriesStatus = "Fetching categories from TradeMe…";
        try
        {
            var result = await _categoryService.FetchAndSaveAsync();
            CategoriesStatus =
                $"Saved {result.LeafCount:N0} categories ({result.TotalNodes:N0} nodes) at {result.FetchedUtc.ToLocalTime():g}.";
        }
        catch (Exception ex)
        {
            CategoriesStatus = $"Fetch failed: {ex.Message}";
        }
    }

    private void Save()
    {
        var portChanged = _settings.Current.ServerPort != ServerPort;

        _settings.Current.ServerPort = ServerPort;
        _settings.Current.ServerAutoStart = ServerAutoStart;
        _settings.Current.OpenAiModel = string.IsNullOrWhiteSpace(OpenAiModel)
            ? AutoAuction.Core.Models.AppSettings.DefaultOpenAiModel
            : OpenAiModel;
        _settings.Current.CloudPhotosPath = string.IsNullOrWhiteSpace(CloudPhotosPath)
            ? null
            : CloudPhotosPath.Trim();
        _settings.Current.CloudPhotosLabel = string.IsNullOrWhiteSpace(CloudPhotosLabel)
            ? null
            : CloudPhotosLabel.Trim();
        _settings.Save();

        // Apply a port change to a running server immediately.
        if (portChanged && _server.IsRunning)
            _server.Restart(ServerPort);

        RefreshServerState();
        SaveStatus = "Settings saved.";
    }

    private void FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        ServerPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
    }

    private void StartServer()
    {
        _server.Start(ServerPort);
        RefreshServerState();
    }

    private void StopServer()
    {
        _server.Stop();
        RefreshServerState();
    }

    private void RefreshServerState()
    {
        this.RaisePropertyChanged(nameof(IsServerRunning));
        this.RaisePropertyChanged(nameof(ServerStatus));
    }

    private void BeginKeyEdit()
    {
        NewKey = string.Empty;
        IsEditingKey = true;
    }

    private void CancelKeyEdit()
    {
        NewKey = string.Empty;
        IsEditingKey = false;
    }

    private void SaveKey()
    {
        var key = NewKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            TestResult = "✗ Enter a key first.";
            return;
        }

        _openAi.SetApiKey(key);
        NewKey = string.Empty;
        IsEditingKey = false;
        HasKey = true;
        TestResult = "Key saved securely. Use “Test connection” to verify it.";
    }

    private void ClearKey()
    {
        _openAi.ClearApiKey();
        NewKey = string.Empty;
        IsEditingKey = false;
        HasKey = false;
        TestResult = "Key cleared.";
    }

    private async System.Threading.Tasks.Task TestAsync()
    {
        TestResult = "Testing OpenAI connection…";

        // Test the key being entered if we're mid-edit; otherwise test the stored key.
        var (ok, message) = IsEditingKey && !string.IsNullOrWhiteSpace(NewKey)
            ? await _openAi.TestAsync(NewKey.Trim(), OpenAiModel)
            : await _openAi.TestAsync();

        TestResult = ok ? $"✓ {message}" : $"✗ {message}";
    }

    private void OpenRootFolder() => SystemFolder.Open(_config.RootPath);

    private void ClearCloudFolder()
    {
        CloudPhotosPath = null;
        CloudPhotosLabel = null;
    }

    /// <summary>
    /// Probes common cloud-sync locations and returns the first that exists, so the field
    /// is pre-filled with a sensible default the first time Settings is opened.
    /// </summary>
    private static string? SuggestDefaultCloudFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            System.IO.Path.Combine(home, "Pictures", "iCloud Photos"),
            System.IO.Path.Combine(home, "OneDrive", "Pictures"),
            System.IO.Path.Combine(home, "OneDrive"),
            System.IO.Path.Combine(home, "Dropbox"),
            System.IO.Path.Combine(home, "My Drive"),
            @"G:\My Drive",
        };

        return candidates.FirstOrDefault(System.IO.Directory.Exists);
    }
}
