using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using AutoAuction.Desktop.ViewModels;
using AutoAuction.Desktop.Views;
using Markdown.Avalonia;

// Headless probe for the "Unsupported IBinding implementation 'StaticBinding'" crash.
// For each markdown snippet we render it TWO ways and compare:
//   BARE     - a plain MarkdownScrollViewer (what a naive probe does)
//   HELPVIEW - the real compiled HelpView + HelpViewModel from AutoAuction.Desktop
//              (ContentControl + DataTemplate + x:CompileBindings="False" — exactly the app)
// If they disagree, the app's hosting is what matters and HELPVIEW is the source of truth.

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        AppBuilder.Configure<ProbeApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .SetupWithoutStarting();

        var cases = new (string Name, string Md)[]
        {
            ("plain paragraph", "Hello world."),
            ("bold + italic", "This is **bold** and *italic*."),
            ("inline code", "Open `listing.json` now."),
            ("bullet list", "- one\n- two"),
            ("bullet list + code in item", "- use `code` here\n- two"),
            ("numbered list + code in item", "1. use `code` here\n2. two"),
            ("table (plain cells)", "| A | B |\n| --- | --- |\n| 1 | 2 |"),
            ("table + code in cell", "| A | B |\n| --- | --- |\n| `code` | 2 |"),
            ("table + bold in cell", "| A | B |\n| --- | --- |\n| **x** | 2 |"),
            ("blockquote", "Intro.\n\n> a quoted note\n\nOutro."),
            ("horizontal rule", "above\n\n---\n\nbelow"),
            ("fenced code block", "```\ncode block\n```"),
        };

        Console.WriteLine($"  {"CONSTRUCT",-22}  {"BARE",-6}  HELPVIEW");
        Console.WriteLine($"  {new string('-', 22)}  {new string('-', 6)}  {new string('-', 8)}");
        foreach (var (name, md) in cases)
            Console.WriteLine($"  {name,-22}  {Render(() => new MarkdownScrollViewer { Markdown = md }),-6}  {RenderHelpView(md)}");

        // Bisect setup.md block-by-block through the real HelpView to isolate the offender.
        var helpDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "AutoAuction.Desktop", "Assets", "Help");
        Bisect(Path.Combine(helpDir, "setup.md"));
        Bisect(Path.Combine(helpDir, "user-guide.md"));
    }

    private static void Bisect(string path)
    {
        Console.WriteLine($"\n  BISECT {Path.GetFileName(path)} (growing prefix, via HelpView):");
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var blocks = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        Console.WriteLine($"  {"ISO",-5} {"CUMUL",-6} block");
        for (var i = 1; i <= blocks.Length; i++)
        {
            var iso = RenderHelpView(blocks[i - 1]);
            var cum = RenderHelpView(string.Join("\n\n", blocks.Take(i)));
            var added = blocks[i - 1].Replace("\n", " ").Trim();
            if (added.Length > 55) added = added[..55] + "…";
            Console.WriteLine($"  {iso,-5} {cum,-6} #{i,2}: {added}");
        }
    }

    private static string RenderHelpView(string md) => Render(() =>
    {
        var vm = new HelpViewModel("Setup");
        vm.CurrentDoc = new HelpDoc(md);   // override with the snippet under test
        return new HelpView { DataContext = vm };
    });

    private static string Render(Func<Control> build)
    {
        try
        {
            var window = new Window { Width = 600, Height = 400, Content = build() };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try { window.CaptureRenderedFrame(); } catch { }
            Dispatcher.UIThread.RunJobs();
            window.Close();
            return "PASS";
        }
        catch (Exception ex)
        {
            return ex is NotSupportedException && ex.Message.Contains("StaticBinding")
                ? "FAIL*"               // the StaticBinding crash
                : $"ERR({ex.GetType().Name})";
        }
    }
}

public class ProbeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
