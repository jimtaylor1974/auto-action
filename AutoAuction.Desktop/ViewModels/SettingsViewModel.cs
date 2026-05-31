using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Reactive;
using AutoAuction.Core.Models;
using AutoAuction.Core.Services;
using AutoAuction.Desktop.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>
/// Settings page: local bridge server port/URL, AI providers + keys (LlmTornado), and
/// general info. Persists to <c>settings.json</c> in the app root folder.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly AiClientFactory _aiFactory;
    private readonly LocalBridgeServer _server;
    private readonly IAppConfig _config;

    public IReadOnlyList<string> KnownProviders { get; } = AiClientFactory.KnownProviders;

    public ObservableCollection<AiProviderConfig> AiProviders { get; }

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

    private AiProviderConfig? _selectedProvider;
    public AiProviderConfig? SelectedProvider
    {
        get => _selectedProvider;
        set => this.RaiseAndSetIfChanged(ref _selectedProvider, value);
    }

    private string? _activeProviderName;
    public string? ActiveProviderName
    {
        get => _activeProviderName;
        set => this.RaiseAndSetIfChanged(ref _activeProviderName, value);
    }

    private string? _activeModel;
    public string? ActiveModel
    {
        get => _activeModel;
        set => this.RaiseAndSetIfChanged(ref _activeModel, value);
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

    public string RootPath => _config.RootPath;

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> FindFreePortCommand { get; }
    public ReactiveCommand<Unit, Unit> StartServerCommand { get; }
    public ReactiveCommand<Unit, Unit> StopServerCommand { get; }
    public ReactiveCommand<Unit, Unit> AddProviderCommand { get; }
    public ReactiveCommand<AiProviderConfig, Unit> RemoveProviderCommand { get; }
    public ReactiveCommand<AiProviderConfig, Unit> TestProviderCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenRootFolderCommand { get; }

    public SettingsViewModel(
        ISettingsService settings,
        AiClientFactory aiFactory,
        LocalBridgeServer server,
        IAppConfig config)
    {
        _settings = settings;
        _aiFactory = aiFactory;
        _server = server;
        _config = config;

        var current = settings.Current;
        _serverPort = current.ServerPort;
        _serverAutoStart = current.ServerAutoStart;
        _activeProviderName = current.ActiveProviderName;
        _activeModel = current.ActiveModel;
        AiProviders = current.AiProviders;

        SaveCommand = ReactiveCommand.Create(Save);
        FindFreePortCommand = ReactiveCommand.Create(FindFreePort);
        StartServerCommand = ReactiveCommand.Create(StartServer);
        StopServerCommand = ReactiveCommand.Create(StopServer);
        AddProviderCommand = ReactiveCommand.Create(AddProvider);
        RemoveProviderCommand = ReactiveCommand.Create<AiProviderConfig>(RemoveProvider);
        TestProviderCommand = ReactiveCommand.CreateFromTask<AiProviderConfig>(TestProviderAsync);
        OpenRootFolderCommand = ReactiveCommand.Create(OpenRootFolder);
    }

    private void Save()
    {
        var portChanged = _settings.Current.ServerPort != ServerPort;

        _settings.Current.ServerPort = ServerPort;
        _settings.Current.ServerAutoStart = ServerAutoStart;
        _settings.Current.ActiveProviderName = ActiveProviderName;
        _settings.Current.ActiveModel = ActiveModel;
        // AiProviders is the same instance bound to the grid, so it's already up to date.
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

    private void AddProvider() => AiProviders.Add(new AiProviderConfig());

    private void RemoveProvider(AiProviderConfig? provider)
    {
        if (provider is not null)
            AiProviders.Remove(provider);
    }

    private async System.Threading.Tasks.Task TestProviderAsync(AiProviderConfig? provider)
    {
        if (provider is null)
            return;

        TestResult = $"Testing {provider.Provider}...";
        var (ok, message) = await _aiFactory.TestAsync(provider);
        TestResult = ok ? $"✓ {provider.Provider}: {message}" : $"✗ {provider.Provider}: {message}";
    }

    private void OpenRootFolder() => SystemFolder.Open(_config.RootPath);
}
