using System.Text.Json;
using AutoAuction.Core.Models;

namespace AutoAuction.Core.Services;

/// <summary>Summary of a category catalogue fetch.</summary>
public sealed record CategoryFetchResult(int TotalNodes, int LeafCount, DateTime FetchedUtc);

/// <summary>
/// Lightweight, single-level view of a category (no nested subcategories). Returned by the
/// drill-down navigation methods so the AI/UI can explore one level at a time and keep the
/// prompt small.
/// </summary>
public sealed record CategorySummary(
    string Name,
    string Number,
    string Path,
    bool IsLeaf,
    bool HasChildren);

/// <summary>
/// Fetches the TradeMe category tree from the public catalogue API and caches it as
/// <c>categories.json</c> in the app root, so it can be fed into the AI prompt and
/// correlated with the listing UI during automation. Refreshed on demand from Settings.
/// </summary>
public interface ICategoryService
{
    /// <summary>Full path to the cached <c>categories.json</c>.</summary>
    string FilePath { get; }

    /// <summary>True if a cached catalogue file exists.</summary>
    bool Exists { get; }

    /// <summary>When the cache was last written (UTC), or null if never.</summary>
    DateTime? LastFetchedUtc { get; }

    /// <summary>Downloads the latest tree from TradeMe and overwrites the cache.</summary>
    Task<CategoryFetchResult> FetchAndSaveAsync(CancellationToken ct = default);

    /// <summary>Loads the cached tree (root node), or null if not fetched / unreadable.</summary>
    TradeMeCategory? Load();

    /// <summary>The top-level General-item categories (one level only).</summary>
    IReadOnlyList<CategorySummary> GetTopLevel();

    /// <summary>
    /// The immediate children of the category with the given number (with or without the
    /// trailing dash). Empty if the number is unknown or a leaf. This is the drill-down step.
    /// </summary>
    IReadOnlyList<CategorySummary> GetChildren(string number);
}

/// <inheritdoc />
public sealed class CategoryService : ICategoryService
{
    private const string ApiUrl = "https://api.trademe.co.nz/v1/Categories.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    // Single shared client (avoids socket exhaustion); category fetch is infrequent.
    private static readonly HttpClient Http = new() {Timeout = TimeSpan.FromSeconds(60)};

    private readonly IAppConfig _config;

    public CategoryService(IAppConfig config)
    {
        _config = config;
    }

    public string FilePath => _config.CategoriesPath;

    public bool Exists => File.Exists(_config.CategoriesPath);

    public DateTime? LastFetchedUtc =>
        Exists ? File.GetLastWriteTimeUtc(_config.CategoriesPath) : null;

    public async Task<CategoryFetchResult> FetchAndSaveAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        request.Headers.UserAgent.ParseAdd("AutoAuction/0.1");

        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);

        var full = JsonSerializer.Deserialize<TradeMeCategory>(json, ReadOptions)
                   ?? throw new InvalidOperationException("Category response was empty.");

        // Keep only the General-item subset (AreaOfBusiness == 1) so the cache is small;
        // this drops the Motors/Property/Jobs/Services/Flatmates trees.
        var root = FilterGeneralItems(full);

        var (total, leaves) = Count(root);
        var outJson = JsonSerializer.Serialize(root, WriteOptions);

        Directory.CreateDirectory(_config.RootPath);
        var tempPath = _config.CategoriesPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, outJson, ct);
        File.Move(tempPath, _config.CategoriesPath, overwrite: true);

        return new CategoryFetchResult(total, leaves, DateTime.UtcNow);
    }

    public TradeMeCategory? Load()
    {
        if (!Exists)
            return null;

        try
        {
            var json = File.ReadAllText(_config.CategoriesPath);
            return JsonSerializer.Deserialize<TradeMeCategory>(json, ReadOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public IReadOnlyList<CategorySummary> GetTopLevel()
    {
        var root = Load();
        return root is null
            ? Array.Empty<CategorySummary>()
            : root.Subcategories.Select(Summarize).ToList();
    }

    public IReadOnlyList<CategorySummary> GetChildren(string number)
    {
        var root = Load();
        if (root is null || string.IsNullOrWhiteSpace(number))
            return Array.Empty<CategorySummary>();

        var node = FindByNumber(root, number.Trim().TrimEnd('-'));
        return node is null
            ? Array.Empty<CategorySummary>()
            : node.Subcategories.Select(Summarize).ToList();
    }

    private static CategorySummary Summarize(TradeMeCategory c) =>
        new(c.Name, c.Number, c.Path, c.IsLeaf, c.Subcategories.Count > 0);

    private static TradeMeCategory? FindByNumber(TradeMeCategory node, string number)
    {
        foreach (var child in node.Subcategories)
        {
            if (child.Number.TrimEnd('-') == number)
                return child;
            var found = FindByNumber(child, number);
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Prunes the tree to only General-item categories (AreaOfBusiness == 1). The root is
    /// kept as a container; every other retained node must itself be AreaOfBusiness == 1,
    /// so a category only reachable through a non-general branch (e.g. under Motors) is dropped.
    /// </summary>
    private static TradeMeCategory FilterGeneralItems(TradeMeCategory root)
    {
        root.Subcategories = PruneToGeneralItems(root.Subcategories);
        return root;
    }

    private static List<TradeMeCategory> PruneToGeneralItems(List<TradeMeCategory> nodes)
    {
        var kept = new List<TradeMeCategory>();
        foreach (var node in nodes)
        {
            if (node.AreaOfBusiness != 1)
                continue;
            node.Subcategories = PruneToGeneralItems(node.Subcategories);
            kept.Add(node);
        }
        return kept;
    }

    private static (int Total, int Leaves) Count(TradeMeCategory node)
    {
        var total = 1;
        var leaves = node.Subcategories.Count == 0 ? 1 : 0;
        foreach (var child in node.Subcategories)
        {
            var (t, l) = Count(child);
            total += t;
            leaves += l;
        }
        return (total, leaves);
    }
}
