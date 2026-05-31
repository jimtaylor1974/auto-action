namespace AutoAuction.Core.Models;

/// <summary>
/// Whether the buyer may / must collect the item in person.
/// Values map directly to the TradeMe Selling API (used in Phase 2).
/// </summary>
public enum PickupOption
{
    /// <summary>Pickup is allowed but not required.</summary>
    Allow = 1,

    /// <summary>Pickup only - shipping is not offered.</summary>
    Demand = 2,

    /// <summary>Pickup is not offered.</summary>
    Forbid = 3
}
