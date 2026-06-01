using System.Collections.ObjectModel;
using ReactiveUI;

namespace AutoAuction.Core.Models;

/// <summary>
/// The data for a single listing. This class is serialized to <c>listing.json</c>
/// inside each draft folder and is the local source of truth for the listing's state.
///
/// The field set deliberately mirrors what the TradeMe "Sell" web form needs so that the
/// Chrome extension bridge can map it directly onto the page's DOM. Character limits and
/// required fields are enforced in the UI.
/// </summary>
public class ListingModel : ReactiveObject
{
    // ----- Metadata (local only) -----

    /// <summary>Stable identifier for the draft. Also used as the draft folder name.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>When the draft was created (UTC). Used for sorting the drafts list.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    private ListingStatus _status = ListingStatus.Draft;
    /// <summary>Where this listing sits in its lifecycle: Draft → Listed → Sold.</summary>
    public ListingStatus Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>When the listing was marked as Listed (moved to 3_Listed). Null while a draft.</summary>
    public DateTime? ListedUtc { get; set; }

    /// <summary>When the listing was marked as Sold. Null until sold.</summary>
    public DateTime? SoldUtc { get; set; }

    /// <summary>TradeMe listing id, captured by the extension after publishing. Null until listed.</summary>
    public string? TradeMeListingId { get; set; }

    /// <summary>Full TradeMe listing URL, captured by the extension after publishing. Null until listed.</summary>
    public string? TradeMeListingUrl { get; set; }

    // ----- 1. Core Details -----

    private string _title = string.Empty;
    /// <summary>REQUIRED. Listing title. TradeMe enforces a hard 50 character limit.</summary>
    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    private string _subtitle = string.Empty;
    /// <summary>Optional. Max 50 characters. (TradeMe charges a fee for subtitles.)</summary>
    public string Subtitle
    {
        get => _subtitle;
        set => this.RaiseAndSetIfChanged(ref _subtitle, value);
    }

    private string _categoryId = string.Empty;
    /// <summary>
    /// REQUIRED. TradeMe category id - a hyphen separated path such as "0153-0447-0818-".
    /// In Phase 1 this is a free text box; Phase 2 adds a category search.
    /// </summary>
    public string CategoryId
    {
        get => _categoryId;
        set => this.RaiseAndSetIfChanged(ref _categoryId, value);
    }

    private ConditionType _condition = ConditionType.Used;
    /// <summary>REQUIRED for almost all categories.</summary>
    public ConditionType Condition
    {
        get => _condition;
        set => this.RaiseAndSetIfChanged(ref _condition, value);
    }

    private string _description = string.Empty;
    /// <summary>
    /// REQUIRED. Stored locally as a single multi-line string. The TradeMe form expects
    /// an array of paragraphs; the split on newlines is handled at fill time by the extension.
    /// </summary>
    public string Description
    {
        get => _description;
        set => this.RaiseAndSetIfChanged(ref _description, value);
    }

    private string _aiImageDescription = string.Empty;
    /// <summary>
    /// In-depth, factual description of the item's photos produced by the AI. Local only -
    /// NOT sent to TradeMe; kept for context and to inform re-generation.
    /// </summary>
    public string AiImageDescription
    {
        get => _aiImageDescription;
        set => this.RaiseAndSetIfChanged(ref _aiImageDescription, value);
    }

    // ----- 2. Pricing Configuration -----
    // Mirrors TradeMe's two independent toggles: "Run an auction" and "Set a Buy Now price"
    // (either or both may be on).

    private bool _isAuction = true;
    /// <summary>"Run an auction" — uses <see cref="StartPrice"/> / <see cref="ReservePrice"/>.</summary>
    public bool IsAuction
    {
        get => _isAuction;
        set => this.RaiseAndSetIfChanged(ref _isAuction, value);
    }

    private bool _hasBuyNow;
    /// <summary>"Set a Buy Now price" — uses <see cref="BuyNowPrice"/>.</summary>
    public bool HasBuyNow
    {
        get => _hasBuyNow;
        set => this.RaiseAndSetIfChanged(ref _hasBuyNow, value);
    }

    private bool _allowOffers = true;
    /// <summary>"Allow buyers to make an offer" (offers accepted until the reserve is met).</summary>
    public bool AllowOffers
    {
        get => _allowOffers;
        set => this.RaiseAndSetIfChanged(ref _allowOffers, value);
    }

    private decimal _startPrice;
    /// <summary>REQUIRED when <see cref="IsAuction"/>. The opening bid.</summary>
    public decimal StartPrice
    {
        get => _startPrice;
        set => this.RaiseAndSetIfChanged(ref _startPrice, value);
    }

    private decimal _reservePrice;
    /// <summary>Optional. Must be greater than or equal to <see cref="StartPrice"/>.</summary>
    public decimal ReservePrice
    {
        get => _reservePrice;
        set => this.RaiseAndSetIfChanged(ref _reservePrice, value);
    }

    private decimal _buyNowPrice;
    /// <summary>REQUIRED when <see cref="HasBuyNow"/>. The instant purchase price.</summary>
    public decimal BuyNowPrice
    {
        get => _buyNowPrice;
        set => this.RaiseAndSetIfChanged(ref _buyNowPrice, value);
    }

    private int _durationDays = 7;
    /// <summary>REQUIRED. Listing duration in days. Valid TradeMe values: 2,3,4,5,6,7,10,14.</summary>
    public int DurationDays
    {
        get => _durationDays;
        set => this.RaiseAndSetIfChanged(ref _durationDays, value);
    }

    // ----- 3. Shipping & Logistics -----

    private PickupOption _pickupOption = PickupOption.Allow;
    /// <summary>REQUIRED. Whether the buyer may / must collect in person.</summary>
    public PickupOption PickupOption
    {
        get => _pickupOption;
        set => this.RaiseAndSetIfChanged(ref _pickupOption, value);
    }

    private ShippingMethod _shipping = ShippingMethod.Free;
    /// <summary>How delivery is offered (Free / Courier / Specify / Unknown).</summary>
    public ShippingMethod Shipping
    {
        get => _shipping;
        set => this.RaiseAndSetIfChanged(ref _shipping, value);
    }

    /// <summary>Custom shipping methods used when <see cref="Shipping"/> is <see cref="ShippingMethod.Specify"/>.</summary>
    public ObservableCollection<ShippingOption> ShippingOptions { get; set; } = new();

    // ----- 4. Media -----

    /// <summary>
    /// File names (relative to the draft folder) of the images attached to this draft.
    /// Stored as names rather than absolute paths so the folder can be moved
    /// (e.g. 2_Drafts -> 3_Listed) without breaking the references.
    /// </summary>
    public List<string> LocalImagePaths { get; set; } = new();
}
