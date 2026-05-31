using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoAuction.Core.Models;

/// <summary>
/// A single custom shipping choice offered to the buyer.
/// Used when <see cref="ListingModel.IsFreeShipping"/> is false and the seller
/// defines specific courier options (TradeMe ShippingType = Custom in Phase 2).
/// </summary>
public partial class ShippingOption : ObservableObject
{
    /// <summary>Human readable method, e.g. "CourierPost Nationwide".</summary>
    [ObservableProperty]
    private string _method = string.Empty;

    /// <summary>Price charged for this shipping method.</summary>
    [ObservableProperty]
    private decimal _price;
}
