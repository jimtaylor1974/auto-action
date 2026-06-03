using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AutoAuction.Desktop.ViewModels;

namespace AutoAuction.Desktop.Views;

public partial class DraftsView : UserControl
{
    public DraftsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDraftDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DraftsViewModel vm
            && DraftsGrid.SelectedItem is DraftRowViewModel row)
            vm.OpenCommand.Execute(row).Subscribe();
    }
}
