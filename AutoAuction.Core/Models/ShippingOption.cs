using ReactiveUI;

namespace AutoAuction.Core.Models;

/// <summary>
/// A single custom shipping row, mirroring TradeMe's "Specify shipping costs" UI
/// (used when <see cref="ListingModel.Shipping"/> is <see cref="ShippingMethod.Specify"/>).
/// TradeMe requires each row to be unique by (<see cref="Region"/>, <see cref="Rural"/>),
/// so an urban/rural split is the usual way to offer two nationwide rates.
/// </summary>
public class ShippingOption : ReactiveObject
{
    private decimal _price;
    /// <summary>Price charged for this shipping row.</summary>
    public decimal Price
    {
        get => _price;
        set => this.RaiseAndSetIfChanged(ref _price, value);
    }

    private string _region = ShippingRegions.DefaultRegion;
    /// <summary>TradeMe region value, e.g. "nz" (New Zealand / nationwide). See <see cref="ShippingRegions"/>.</summary>
    public string Region
    {
        get => _region;
        set => this.RaiseAndSetIfChanged(ref _region, value);
    }

    private string _rural = ShippingRegions.DefaultRural;
    /// <summary>Rural qualifier: "Any", "Urban", or "Rural".</summary>
    public string Rural
    {
        get => _rural;
        set => this.RaiseAndSetIfChanged(ref _rural, value);
    }

    private bool _signed;
    /// <summary>Whether a signature is required on delivery.</summary>
    public bool Signed
    {
        get => _signed;
        set => this.RaiseAndSetIfChanged(ref _signed, value);
    }
}
