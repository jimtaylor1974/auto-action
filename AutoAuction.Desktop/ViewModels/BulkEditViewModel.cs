using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using AutoAuction.Core.Models;
using AutoAuction.Core.Services;
using ReactiveUI;

namespace AutoAuction.Desktop.ViewModels;

/// <summary>
/// Overlay panel for applying shared settings to several drafts at once. The user fills a template
/// listing, ticks which sections to apply, and Apply copies those sections into every target draft
/// and locks each applied group so a later "Draft with AI" won't overwrite them.
/// </summary>
public class BulkEditViewModel : ViewModelBase
{
    private readonly IDraftService _draftService;
    private readonly ICategoryService _categories;
    private readonly IReadOnlyList<DraftRowViewModel> _targets;
    private readonly Action<int> _onClose;

    /// <summary>Template values the user edits; copied into the targets on Apply.</summary>
    public ListingModel Template { get; } = new();

    public int TargetCount => _targets.Count;
    public string Heading => $"Bulk edit — {TargetCount} draft(s) selected";

    // Reused option sources (mirror the single-draft editor).
    public IReadOnlyList<ConditionType> ConditionOptions { get; } = Enum.GetValues<ConditionType>();
    public IReadOnlyList<PickupOption> PickupOptions { get; } = Enum.GetValues<PickupOption>();
    public IReadOnlyList<ShippingMethod> ShippingMethodOptions { get; } = Enum.GetValues<ShippingMethod>();
    public IReadOnlyList<ShippingRegion> RegionOptions { get; } = ShippingRegions.All;
    public IReadOnlyList<string> RuralOptions { get; } = ShippingRegions.RuralOptions;
    public IReadOnlyList<int> DurationOptions { get; } = new[] {2, 3, 4, 5, 6, 7, 10, 14};

    public ReactiveCommand<Unit, Unit> AddShippingOptionCommand { get; }
    public ReactiveCommand<ShippingOption, Unit> RemoveShippingOptionCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public BulkEditViewModel(
        IDraftService draftService,
        ICategoryService categories,
        IReadOnlyList<DraftRowViewModel> targets,
        Action<int> onClose)
    {
        _draftService = draftService;
        _categories = categories;
        _targets = targets;
        _onClose = onClose;

        Template.PropertyChanged += OnTemplateChanged;

        AddShippingOptionCommand = ReactiveCommand.Create(AddShippingOption);
        RemoveShippingOptionCommand = ReactiveCommand.Create<ShippingOption>(o =>
        {
            if (o is not null) Template.ShippingOptions.Remove(o);
        });

        var canApply = this.WhenAnyValue(
            x => x.ApplyPricing, x => x.ApplyShipping, x => x.ApplyCategory, x => x.ApplyCondition,
            (p, s, cat, cond) => p || s || cat || cond);
        ApplyCommand = ReactiveCommand.Create(Apply, canApply);
        CancelCommand = ReactiveCommand.Create(() => _onClose(0));
    }

    // --- section toggles -----------------------------------------------------------------------

    private bool _applyPricing;
    public bool ApplyPricing { get => _applyPricing; set => this.RaiseAndSetIfChanged(ref _applyPricing, value); }

    private bool _applyShipping;
    public bool ApplyShipping { get => _applyShipping; set => this.RaiseAndSetIfChanged(ref _applyShipping, value); }

    private bool _applyCategory;
    public bool ApplyCategory { get => _applyCategory; set => this.RaiseAndSetIfChanged(ref _applyCategory, value); }

    private bool _applyCondition;
    public bool ApplyCondition { get => _applyCondition; set => this.RaiseAndSetIfChanged(ref _applyCondition, value); }

    public bool IsSpecifyShipping => Template.Shipping == ShippingMethod.Specify;

    public string CategoryPathDisplay => string.IsNullOrWhiteSpace(Template.CategoryId)
        ? string.Empty
        : _categories.GetDisplayPath(Template.CategoryId) ?? "Unknown category";

    private void OnTemplateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ListingModel.Shipping))
            this.RaisePropertyChanged(nameof(IsSpecifyShipping));
        if (e.PropertyName == nameof(ListingModel.CategoryId))
            this.RaisePropertyChanged(nameof(CategoryPathDisplay));
    }

    private void AddShippingOption()
    {
        var used = Template.ShippingOptions.Select(o => o.Rural).ToHashSet();
        var rural = ShippingRegions.RuralOptions.FirstOrDefault(r => !used.Contains(r))
                    ?? ShippingRegions.DefaultRural;
        Template.ShippingOptions.Add(new ShippingOption {Rural = rural});
    }

    // --- apply ---------------------------------------------------------------------------------

    private void Apply()
    {
        foreach (var row in _targets)
        {
            var listing = row.Listing;

            if (ApplyPricing)
            {
                listing.IsAuction = Template.IsAuction;
                listing.HasBuyNow = Template.HasBuyNow;
                listing.AllowOffers = Template.AllowOffers;
                listing.StartPrice = Template.StartPrice;
                listing.ReservePrice = Template.ReservePrice;
                listing.BuyNowPrice = Template.BuyNowPrice;
                listing.DurationDays = Template.DurationDays;
                listing.LockedGroups |= ListingFieldGroup.Pricing;
            }

            if (ApplyShipping)
            {
                listing.Shipping = Template.Shipping;
                listing.PickupOption = Template.PickupOption;
                listing.ShippingOptions.Clear();
                foreach (var o in Template.ShippingOptions)
                    listing.ShippingOptions.Add(new ShippingOption
                    {
                        Price = o.Price, Region = o.Region, Rural = o.Rural, Signed = o.Signed
                    });
                listing.LockedGroups |= ListingFieldGroup.Shipping;
            }

            if (ApplyCategory)
            {
                listing.CategoryId = Template.CategoryId;
                listing.LockedGroups |= ListingFieldGroup.Category;
            }

            if (ApplyCondition)
            {
                listing.Condition = Template.Condition;
                listing.LockedGroups |= ListingFieldGroup.Condition;
            }

            _draftService.SaveListing(row.FolderPath, listing);
            row.Refresh(new DraftFolder(row.FolderPath, listing));
        }

        _onClose(_targets.Count);
    }
}
