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
            if (Fields.IsBuyNowOnly)
                parts.Add($"Buy Now ${Fields.BuyNowPrice:0.##}");
            else
            {
                parts.Add($"Start ${Fields.StartPrice:0.##}");
                if (Fields.ReservePrice > 0) parts.Add($"Reserve ${Fields.ReservePrice:0.##}");
                if (Fields.BuyNowPrice > 0) parts.Add($"Buy Now ${Fields.BuyNowPrice:0.##}");
            }
            parts.Add($"{Fields.DurationDays} days");
            return string.Join(" · ", parts);
        }
    }

    public string ShippingSummary => Fields.IsFreeShipping
        ? $"Free shipping · Pickup: {Fields.PickupOption}"
        : Fields.ShippingOptions.Count == 0
            ? $"Pickup: {Fields.PickupOption}"
            : string.Join(", ", Fields.ShippingOptions.Select(s => $"{s.Method} ${s.Price:0.##}")) +
              $" · Pickup: {Fields.PickupOption}";
}
