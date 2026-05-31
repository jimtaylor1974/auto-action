using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AutoAuction.Desktop.Views;

public partial class DraftEditorView : UserControl
{
    public DraftEditorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
