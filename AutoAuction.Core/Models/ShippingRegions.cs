using System.Collections.Generic;

namespace AutoAuction.Core.Models;

/// <summary>A TradeMe "Specify shipping costs" region: the form's value plus its display name.</summary>
public sealed record ShippingRegion(string Value, string Display);

/// <summary>
/// The fixed choices in TradeMe's "Specify shipping costs" rows (Region + Rural), shared by the
/// editor form, the AI schema, and the extension so all three stay aligned. Values match the
/// TradeMe &lt;select&gt; option values exactly (discovered via CDP).
/// </summary>
public static class ShippingRegions
{
    /// <summary>Region options (top = "New Zealand" = nationwide, the default).</summary>
    public static readonly IReadOnlyList<ShippingRegion> All = new[]
    {
        new ShippingRegion("nz", "New Zealand"),
        new ShippingRegion("nz_ni", "North Island"),
        new ShippingRegion("nz_ni_ntl", "Northland"),
        new ShippingRegion("nz_ni_auk", "Auckland"),
        new ShippingRegion("nz_ni_wko", "Waikato"),
        new ShippingRegion("nz_ni_bop", "Bay Of Plenty"),
        new ShippingRegion("nz_ni_gis", "Gisborne"),
        new ShippingRegion("nz_ni_hkb", "Hawke's Bay"),
        new ShippingRegion("nz_ni_tki", "Taranaki"),
        new ShippingRegion("nz_ni_mwt", "Manawatu / Whanganui"),
        new ShippingRegion("nz_ni_wgn", "Wellington"),
        new ShippingRegion("nz_si", "South Island"),
        new ShippingRegion("nz_si_nsn", "Nelson / Tasman"),
        new ShippingRegion("nz_si_mbh", "Marlborough"),
        new ShippingRegion("nz_si_wtc", "West Coast"),
        new ShippingRegion("nz_si_can", "Canterbury"),
        new ShippingRegion("nz_si_ota", "Otago"),
        new ShippingRegion("nz_si_stl", "Southland")
    };

    /// <summary>Rural options. A row is unique by (Region, Rural), so Urban/Rural split a nationwide rate.</summary>
    public static readonly IReadOnlyList<string> RuralOptions = new[] {"Any", "Urban", "Rural"};

    public const string DefaultRegion = "nz";
    public const string DefaultRural = "Any";

    /// <summary>Display name for a region value (falls back to the raw value if unknown).</summary>
    public static string DisplayName(string value)
    {
        foreach (var r in All)
            if (r.Value == value)
                return r.Display;
        return value;
    }
}
