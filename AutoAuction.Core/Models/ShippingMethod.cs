namespace AutoAuction.Core.Models;

/// <summary>
/// How the seller offers delivery, mirroring the TradeMe "Shipping &amp; pick-up" choices.
/// </summary>
public enum ShippingMethod
{
    /// <summary>Free shipping within New Zealand.</summary>
    Free = 0,

    /// <summary>"Calculate courier costs" (Book a Courier mates' rates).</summary>
    Courier = 1,

    /// <summary>"Specify shipping costs" — uses the custom <see cref="ListingModel.ShippingOptions"/>.</summary>
    Specify = 2,

    /// <summary>"I don't know costs yet".</summary>
    Unknown = 3
}
