using System;

namespace AutoAuction.Core.Models;

/// <summary>
/// Groups of listing fields that can be independently "locked" against AI overwrite.
/// Once the user sets a group (manually in the editor or via bulk-apply), AI generation
/// still produces values for it but the apply step skips locked groups.
/// </summary>
[Flags]
public enum ListingFieldGroup
{
    None = 0,
    /// <summary>Title, Subtitle, Description.</summary>
    Content = 1,
    /// <summary>CategoryId.</summary>
    Category = 2,
    /// <summary>Condition.</summary>
    Condition = 4,
    /// <summary>Auction/BuyNow/offers toggles, Start/Reserve/BuyNow prices, Duration.</summary>
    Pricing = 8,
    /// <summary>Shipping method, custom options, and Pickup.</summary>
    Shipping = 16
}
