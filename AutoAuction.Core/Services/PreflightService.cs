using System.Net.Http;

namespace AutoAuction.Core.Services;

/// <summary>One launch-time readiness check: a short title, whether it passed, and a message.</summary>
public sealed record PreflightCheck(string Title, bool Ok, string Detail);

/// <summary>The result of running every preflight check.</summary>
public sealed class PreflightReport
{
    public PreflightReport(IReadOnlyList<PreflightCheck> checks) => Checks = checks;

    public IReadOnlyList<PreflightCheck> Checks { get; }

    /// <summary>True when nothing needs the user's attention.</summary>
    public bool AllOk => Checks.All(c => c.Ok);

    /// <summary>The checks that failed (what to nudge the user about).</summary>
    public IEnumerable<PreflightCheck> Failures => Checks.Where(c => !c.Ok);

    /// <summary>A one-line, comma-joined summary of the failing checks (empty when all pass).</summary>
    public string FailureSummary => string.Join(", ", Failures.Select(f => f.Detail));
}

/// <summary>
/// Runs the launch-time readiness checks (internet, cached categories, OpenAI key) so the shell
/// can nudge the user to Settings before they hit a dead end mid-workflow.
/// </summary>
public interface IPreflightService
{
    Task<PreflightReport> RunAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PreflightService : IPreflightService
{
    // A well-known no-content endpoint used purely as a connectivity probe; any successful
    // response means we have a working internet connection.
    private const string ConnectivityUrl = "https://www.google.com/generate_204";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly ICategoryService _categories;
    private readonly OpenAiClient _openAi;

    public PreflightService(ICategoryService categories, OpenAiClient openAi)
    {
        _categories = categories;
        _openAi = openAi;
    }

    public async Task<PreflightReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<PreflightCheck>
        {
            await CheckInternetAsync(cancellationToken),
            CheckCategories(),
            CheckOpenAiKey(),
        };

        return new PreflightReport(checks);
    }

    private static async Task<PreflightCheck> CheckInternetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(
                ConnectivityUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return new PreflightCheck("Internet", true, "Internet connection available.");
        }
        catch
        {
            // Any failure (DNS, timeout, no route) means we should treat the app as offline.
            return new PreflightCheck("Internet", false, "no internet connection");
        }
    }

    private PreflightCheck CheckCategories()
        => _categories.Exists
            ? new PreflightCheck("Categories", true, "TradeMe categories downloaded.")
            : new PreflightCheck("Categories", false, "TradeMe categories not downloaded");

    private PreflightCheck CheckOpenAiKey()
        => _openAi.HasApiKey
            ? new PreflightCheck("OpenAI key", true, "OpenAI API key set.")
            : new PreflightCheck("OpenAI key", false, "OpenAI API key not set");
}
