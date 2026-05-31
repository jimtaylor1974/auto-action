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

        // Core services. AppConfig owns the folder structure; DraftService reads/writes it.
        services.AddSingleton<IAppConfig, AppConfig>();
        services.AddSingleton<IDraftService, DraftService>();

        // View models.
        services.AddTransient<MainWindowViewModel>();

        ServiceProvider = services.BuildServiceProvider();

        // Create the data folders on startup before anything tries to read them.
        ServiceProvider.GetRequiredService<IAppConfig>().Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = ServiceProvider.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
