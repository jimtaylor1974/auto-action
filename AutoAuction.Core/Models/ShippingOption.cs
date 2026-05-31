using ReactiveUI;

namespace AutoAuction.Core.Models;

/// <summary>
/// A single custom shipping choice offered to the buyer.
/// Used when <see cref="ListingModel.IsFreeShipping"/> is false and the seller
/// defines specific courier options.
/// </summary>
public class ShippingOption : ReactiveObject
{
    private string _method = string.Empty;
    /// <summary>Human readable method, e.g. "CourierPost Nationwide".</summary>
    public string Method
    {
        get => _method;
        set => this.RaiseAndSetIfChanged(ref _method, value);
    }

    private decimal _price;
    /// <summary>Price charged for this shipping method.</summary>
    public decimal Price
    {
        get => _price;
        set => this.RaiseAndSetIfChanged(ref _price, value);
    }
}
