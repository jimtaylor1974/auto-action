namespace AutoAuction.Core.Models;

/// <summary>
/// A node in the TradeMe category tree, as returned by the public catalogue API
/// (https://api.trademe.co.nz/v1/Categories.json). Saved locally to categories.json
/// so the AI prompt and the listing automation share one source of truth.
/// </summary>
public sealed class TradeMeCategory
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Hierarchical id, e.g. "0153-0435-3648-".</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>URL-style path, e.g. "/Clothing-Fashion/Boys/Tops-tshirts".</summary>
    public string Path { get; set; } = string.Empty;

    public bool IsLeaf { get; set; }

    /// <summary>1 = Marketplace (General items); other values = Motors/Property/Jobs.</summary>
    public int AreaOfBusiness { get; set; }

    public bool CanHaveSecondCategory { get; set; }

    public List<TradeMeCategory> Subcategories { get; set; } = new();
}
