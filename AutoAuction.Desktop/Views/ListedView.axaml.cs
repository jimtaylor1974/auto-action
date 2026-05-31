using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AutoAuction.Desktop.Views;

public partial class ListedView : UserControl
{
    public ListedView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
