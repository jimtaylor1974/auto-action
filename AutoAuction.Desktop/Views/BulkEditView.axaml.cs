using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AutoAuction.Desktop.Views;

public partial class BulkEditView : UserControl
{
    public BulkEditView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
