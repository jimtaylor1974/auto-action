namespace AutoAuction.Core.Models;

/// <summary>
/// Item condition. Values map directly to the TradeMe Selling API
/// (used in Phase 2), so do not renumber.
/// </summary>
public enum ConditionType
{
    Used = 1,
    New = 2
}
