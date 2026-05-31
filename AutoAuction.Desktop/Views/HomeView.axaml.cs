using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using AutoAuction.Desktop.ViewModels;

namespace AutoAuction.Desktop.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not HomeViewModel vm)
            return;

        var paths = e.Data.GetFiles()?
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToList();

        if (paths is { Count: > 0 })
            vm.ImportPhotos(paths);
    }

    private void OnDraftDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is HomeViewModel vm)
            vm.OpenDraft(vm.SelectedDraft);
    }

    private async void OnAddPhotosClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var imageFilter = new FilePickerFileType("Images")
        {
            Patterns = new[]
            {
                "*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp", "*.gif", "*.heic", "*.heif"
            }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add photos to inbox",
            AllowMultiple = true,
            FileTypeFilter = new[] { imageFilter }
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToList();

        if (paths.Count > 0)
            vm.ImportPhotos(paths);
    }
}
