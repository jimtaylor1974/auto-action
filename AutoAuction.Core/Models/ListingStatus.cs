namespace AutoAuction.Core.Models;

/// <summary>
/// Lifecycle state of a listing. Mirrors the folder it lives in:
/// <see cref="Draft"/> = 2_Drafts; <see cref="Listed"/>/<see cref="Sold"/> = 3_Listed.
/// </summary>
public enum ListingStatus
{
    Draft = 0,
    Listed = 1,
    Sold = 2
}
