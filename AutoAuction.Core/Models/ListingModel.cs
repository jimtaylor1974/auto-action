using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoAuction.Core.Models;

/// <summary>
/// The data for a single listing. This class is serialized to <c>listing.json</c>
/// inside each draft folder and is the local source of truth for the listing's state.
///
/// The field set deliberately mirrors what the TradeMe <c>Selling.json</c> API will
/// require in Phase 2 so that the Phase 1 manual editor produces data that is already
/// API-ready. Character limits and required fields are enforced in the UI.
/// </summary>
public partial class ListingModel : ObservableObject
{
    // ----- Metadata (local only) -----

    /// <summary>Stable identifier for the draft. Also used as the draft folder name.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>When the draft was created (UTC). Used for sorting the drafts list.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // ----- 1. Core Details -----

    /// <summary>REQUIRED. Listing title. TradeMe enforces a hard 50 character limit.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Optional. Max 50 characters. (TradeMe charges a fee for subtitles.)</summary>
    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>
    /// REQUIRED. TradeMe category id - a hyphen separated path such as "0153-0447-0818-".
    /// In Phase 1 this is a free text box; Phase 2 adds a category search.
    /// </summary>
    [ObservableProperty]
    private string _categoryId = string.Empty;

    /// <summary>REQUIRED for almost all categories.</summary>
    [ObservableProperty]
    private ConditionType _condition = ConditionType.Used;

    /// <summary>
    /// REQUIRED. Stored locally as a single multi-line string. The TradeMe API expects
    /// an array of paragraphs; the split on newlines is handled in Phase 2 at submit time.
    /// </summary>
    [ObservableProperty]
    private string _description = string.Empty;

    // ----- 2. Pricing Configuration -----

    /// <summary>If true the item is a fixed price "Buy Now Only" listing rather than an auction.</summary>
    [ObservableProperty]
    private bool _isBuyNowOnly;

    /// <summary>REQUIRED for auctions. The opening bid.</summary>
    [ObservableProperty]
    private decimal _startPrice;

    /// <summary>REQUIRED for auctions. Must be greater than or equal to <see cref="StartPrice"/>.</summary>
    [ObservableProperty]
    private decimal _reservePrice;

    /// <summary>Optional. The instant purchase price.</summary>
    [ObservableProperty]
    private decimal _buyNowPrice;

    /// <summary>REQUIRED. Listing duration in days. Valid TradeMe values: 2,3,4,5,6,7,10,14.</summary>
    [ObservableProperty]
    private int _durationDays = 7;

    // ----- 3. Shipping &amp; Logistics -----

    /// <summary>REQUIRED. Whether the buyer may / must collect in person.</summary>
    [ObservableProperty]
    private PickupOption _pickupOption = PickupOption.Allow;

    /// <summary>
    /// True = free shipping. False = custom options defined in <see cref="ShippingOptions"/>.
    /// (Maps to TradeMe ShippingType in Phase 2.)
    /// </summary>
    [ObservableProperty]
    private bool _isFreeShipping;

    /// <summary>Custom shipping methods used when <see cref="IsFreeShipping"/> is false.</summary>
    public ObservableCollection<ShippingOption> ShippingOptions { get; set; } = new();

    // ----- 4. Media -----

    /// <summary>
    /// File names (relative to the draft folder) of the images attached to this draft.
    /// Stored as names rather than absolute paths so the folder can be moved
    /// (e.g. Drafts -> Listed in Phase 2) without breaking the references.
    /// In Phase 2 these files are uploaded and TradeMe returns integer PhotoIds.
    /// </summary>
    public List<string> LocalImagePaths { get; set; } = new();
}
