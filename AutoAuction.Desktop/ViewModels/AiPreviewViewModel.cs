using System.Linq;
using AutoAuction.Core.Services;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>
/// Read-only view of the fields the AI proposed, shown in the preview panel before the user
/// applies them to the draft. Wraps <see cref="GeneratedFields"/> with display-friendly text.
/// </summary>
public sealed class AiPreviewViewModel : ViewModelBase
{
    public GeneratedFields Fields { get; }

    public AiPreviewViewModel(GeneratedFields fields)
    {
        Fields = fields;
    }

    public string Title => Fields.Title;
    public string Subtitle => Fields.Subtitle;
    public string Condition => Fields.Condition;
    public string Description => Fields.Description;
    public string ImageDescription => Fields.ImageDescription;

    public string CategoryDisplay => string.IsNullOrWhiteSpace(Fields.CategoryPath)
        ? Fields.CategoryNumber
        : $"{Fields.CategoryPath}  ({Fields.CategoryNumber})";

    public string PriceSummary
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Fields.IsAuction)
            {
                parts.Add($"Start ${Fields.StartPrice:0.##}");
                if (Fields.ReservePrice > 0) parts.Add($"Reserve ${Fields.ReservePrice:0.##}");
            }
            if (Fields.HasBuyNow) parts.Add($"Buy Now ${Fields.BuyNowPrice:0.##}");
            if (Fields.AllowOffers) parts.Add("offers ok");
            parts.Add($"{Fields.DurationDays} days");
            return string.Join(" · ", parts);
        }
    }

    public string ShippingSummary
    {
        get
        {
            var method = Fields.Shipping switch
            {
                "courier" => "Courier costs",
                "specify" => Fields.ShippingOptions.Count > 0
                    ? string.Join(", ", Fields.ShippingOptions.Select(s => $"{s.Method} ${s.Price:0.##}"))
                    : "Specify costs",
                "unknown" => "Costs TBD",
                _ => "Free shipping"
            };
            return $"{method} · Pickup: {Fields.PickupOption}";
        }
    }
}
