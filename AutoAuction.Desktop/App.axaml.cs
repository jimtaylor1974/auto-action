using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AutoAuction.Core.Services;
using AutoAuction.Desktop.ViewModels;
using AutoAuction.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AutoAuction.Desktop;

public partial class App : Application
{
    public IServiceProvider ServiceProvider { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Core services. AppConfig owns the folder structure; the rest read/write it.
        services.AddSingleton<IAppConfig, AppConfig>();
        services.AddSingleton<IDraftService, DraftService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IActiveListingProvider, ActiveListingProvider>();
        services.AddSingleton<LocalBridgeServer>();
        services.AddSingleton<AiClientFactory>();

        // View models.
        services.AddTransient<MainWindowViewModel>();

        ServiceProvider = services.BuildServiceProvider();

        // Create the data folders on startup before anything tries to read them.
        ServiceProvider.GetRequiredService<IAppConfig>().Initialize();

        // Start the local bridge server if configured to auto-start.
        var settings = ServiceProvider.GetRequiredService<ISettingsService>();
        var server = ServiceProvider.GetRequiredService<LocalBridgeServer>();
        if (settings.Current.ServerAutoStart)
            server.Start(settings.Current.ServerPort);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = ServiceProvider.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = vm };

            // Build the first page once the window is shown (pii-scanner pattern).
            window.Opened += (_, _) => vm.Initialize();

            // Stop the bridge server cleanly on exit.
            desktop.ShutdownRequested += (_, _) => server.Stop();

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
