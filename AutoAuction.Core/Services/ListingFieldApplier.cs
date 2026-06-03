using System;
using System.Collections.Generic;
using AutoAuction.Core.Models;

namespace AutoAuction.Core.Services;

/// <summary>
/// Maps AI <see cref="GeneratedFields"/> onto a <see cref="ListingModel"/>. The single source of
/// truth for "apply AI to a draft", used by both the single-draft editor and the batch runner so
/// behaviour stays identical. When <c>respectLocks</c> is set, groups in
/// <see cref="ListingModel.LockedGroups"/> are skipped (the AI still generated them; we just don't
/// write them). Every apply stamps <see cref="ListingModel.AiDraftedUtc"/>.
/// </summary>
public static class ListingFieldApplier
{
    /// <summary>TradeMe's allowed listing durations (days).</summary>
    private static readonly HashSet<int> AllowedDurations = new() {2, 3, 4, 5, 6, 7, 10, 14};

    public static void Apply(ListingModel listing, GeneratedFields f, bool respectLocks)
    {
        bool Locked(ListingFieldGroup g) => respectLocks && listing.LockedGroups.HasFlag(g);

        // AiImageDescription is AI-only scratch (never user-edited), so it is always refreshed.
        listing.AiImageDescription = f.ImageDescription;

        if (!Locked(ListingFieldGroup.Content))
        {
            listing.Title = Truncate(f.Title, 50);
            listing.Subtitle = Truncate(f.Subtitle, 50);
            listing.Description = f.Description;
        }

        if (!Locked(ListingFieldGroup.Category))
            listing.CategoryId = f.CategoryNumber;

        if (!Locked(ListingFieldGroup.Condition))
            listing.Condition = string.Equals(f.Condition, "New", StringComparison.OrdinalIgnoreCase)
                ? ConditionType.New
                : ConditionType.Used;

        if (!Locked(ListingFieldGroup.Pricing))
        {
            listing.IsAuction = f.IsAuction;
            listing.HasBuyNow = f.HasBuyNow;
            listing.AllowOffers = f.AllowOffers;
            listing.StartPrice = f.StartPrice;
            listing.ReservePrice = f.ReservePrice;
            listing.BuyNowPrice = f.BuyNowPrice;
            listing.DurationDays = AllowedDurations.Contains(f.DurationDays) ? f.DurationDays : 7;
        }

        if (!Locked(ListingFieldGroup.Shipping))
        {
            listing.PickupOption = Enum.TryParse<PickupOption>(f.PickupOption, ignoreCase: true, out var pickup)
                ? pickup
                : PickupOption.Allow;
            listing.Shipping = f.Shipping switch
            {
                "courier" => ShippingMethod.Courier,
                "specify" => ShippingMethod.Specify,
                "unknown" => ShippingMethod.Unknown,
                _ => ShippingMethod.Free
            };

            listing.ShippingOptions.Clear();
            foreach (var s in f.ShippingOptions)
                listing.ShippingOptions.Add(new ShippingOption
                {
                    Price = s.Price,
                    Region = string.IsNullOrWhiteSpace(s.Region) ? ShippingRegions.DefaultRegion : s.Region,
                    Rural = string.IsNullOrWhiteSpace(s.Rural) ? ShippingRegions.DefaultRural : s.Rural,
                    Signed = s.Signed
                });
        }

        listing.AiDraftedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// The lock group a <see cref="ListingModel"/> property belongs to, so the editor's
    /// change-tracking can auto-lock a group when the user edits one of its fields.
    /// Returns <see cref="ListingFieldGroup.None"/> for metadata / non-lockable fields.
    /// </summary>
    public static ListingFieldGroup GroupFor(string? propertyName) => propertyName switch
    {
        nameof(ListingModel.Title) or nameof(ListingModel.Subtitle) or nameof(ListingModel.Description)
            => ListingFieldGroup.Content,
        nameof(ListingModel.CategoryId) => ListingFieldGroup.Category,
        nameof(ListingModel.Condition) => ListingFieldGroup.Condition,
        nameof(ListingModel.IsAuction) or nameof(ListingModel.HasBuyNow) or nameof(ListingModel.AllowOffers)
            or nameof(ListingModel.StartPrice) or nameof(ListingModel.ReservePrice)
            or nameof(ListingModel.BuyNowPrice) or nameof(ListingModel.DurationDays)
            => ListingFieldGroup.Pricing,
        nameof(ListingModel.Shipping) or nameof(ListingModel.PickupOption) or nameof(ListingModel.ShippingOptions)
            => ListingFieldGroup.Shipping,
        _ => ListingFieldGroup.None
    };

    /// <summary>Human-readable summary of the locked groups, e.g. "Pricing, Shipping".</summary>
    public static string DescribeLocks(ListingFieldGroup groups)
    {
        if (groups == ListingFieldGroup.None) return string.Empty;
        var parts = new List<string>();
        if (groups.HasFlag(ListingFieldGroup.Content)) parts.Add("Content");
        if (groups.HasFlag(ListingFieldGroup.Category)) parts.Add("Category");
        if (groups.HasFlag(ListingFieldGroup.Condition)) parts.Add("Condition");
        if (groups.HasFlag(ListingFieldGroup.Pricing)) parts.Add("Pricing");
        if (groups.HasFlag(ListingFieldGroup.Shipping)) parts.Add("Shipping");
        return string.Join(", ", parts);
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
