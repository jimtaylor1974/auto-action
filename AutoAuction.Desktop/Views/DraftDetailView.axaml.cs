using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AutoAuction.Desktop.ViewModels;

namespace AutoAuction.Desktop.Views;

public partial class DraftDetailView : UserControl
{
    public DraftDetailView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnCopyLogClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DraftDetailViewModel vm)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(vm.BuildDiagnosticsText());
    }
}
