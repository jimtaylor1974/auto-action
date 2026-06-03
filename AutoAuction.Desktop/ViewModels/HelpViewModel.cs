using System;
using System.IO;
using Avalonia.Platform;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>Strongly-typed container for a rendered help topic's markdown.</summary>
public record HelpDoc(string Markdown);

/// <summary>
/// Help page: a sidebar of topics plus a markdown-rendered content pane. Topic content lives
/// as <c>.md</c> files under <c>Assets/Help</c> (bundled as AvaloniaResource) and is loaded on
/// demand. A fresh instance is built per navigation; <paramref name="navigationCallback"/> lets
/// the in-page sidebar buttons re-navigate via the shell so deep-links and selection stay in sync.
/// </summary>
public class HelpViewModel : ViewModelBase
{
    private readonly Action<string>? _navigationCallback;

    public HelpViewModel(string initialTopic = "Setup", Action<string>? navigationCallback = null)
    {
        _navigationCallback = navigationCallback;
        _selectedTopic = initialTopic;
        UpdateHelpContent();
    }

    private string _selectedTopic;
    public string SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTopic, value);
            UpdateHelpContent();
            this.RaisePropertyChanged(nameof(IsSetupSelected));
            this.RaisePropertyChanged(nameof(IsUserGuideSelected));
        }
    }

    public bool IsSetupSelected => SelectedTopic == "Setup";
    public bool IsUserGuideSelected => SelectedTopic == "User Guide";

    private HelpDoc _currentDoc = new("");
    public HelpDoc CurrentDoc
    {
        get => _currentDoc;
        set => this.RaiseAndSetIfChanged(ref _currentDoc, value);
    }

    // Sidebar buttons route through the shell so the page rebuilds with the right topic.
    public void SelectSetup() => _navigationCallback?.Invoke("Setup");
    public void SelectUserGuide() => _navigationCallback?.Invoke("User Guide");

    private void UpdateHelpContent()
    {
        var file = SelectedTopic switch
        {
            "User Guide" => "user-guide.md",
            _ => "setup.md"
        };
        CurrentDoc = new HelpDoc(LoadMarkdown(file));
    }

    /// <summary>Loads a bundled help markdown file, returning a friendly message if it's missing.</summary>
    private static string LoadMarkdown(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://AutoAuction.Desktop/Assets/Help/{fileName}");
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return $"# Help unavailable\n\nThe help topic **{fileName}** could not be loaded.";
        }
    }
}
