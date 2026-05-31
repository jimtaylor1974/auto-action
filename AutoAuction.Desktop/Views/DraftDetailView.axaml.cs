using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AutoAuction.Desktop.Views;

public partial class DraftDetailView : UserControl
{
    public DraftDetailView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
