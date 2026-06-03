using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using AutoAuction.Desktop.ViewModels;

namespace AutoAuction.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnBrowseCloudFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a synced cloud folder",
            AllowMultiple = false
        });

        var path = folders
            .Select(f => f.TryGetLocalPath())
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        if (!string.IsNullOrEmpty(path))
            vm.CloudPhotosPath = path;
    }
}
