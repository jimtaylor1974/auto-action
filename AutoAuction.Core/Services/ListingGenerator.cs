using System.Linq;
using System.Text;
using System.Text.Json;
using AutoAuction.Core.Models;
using AutoAuction.Core.Services.OpenAi;

namespace AutoAuction.Core.Services;

/// <summary>Fields the AI proposes for a listing (raw values; the UI maps them onto the model).</summary>
public sealed class GeneratedFields
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string CategoryNumber { get; set; } = string.Empty;
    public string CategoryPath { get; set; } = string.Empty;
    public string Condition { get; set; } = "Used";
    public string Description { get; set; } = string.Empty;
    public string ImageDescription { get; set; } = string.Empty;
    public bool IsAuction { get; set; } = true;
    public bool HasBuyNow { get; set; }
    public bool AllowOffers { get; set; } = true;
    public decimal StartPrice { get; set; }
    public decimal ReservePrice { get; set; }
    public decimal BuyNowPrice { get; set; }
    public int DurationDays { get; set; } = 7;
    public string PickupOption { get; set; } = "Allow";
    public string Shipping { get; set; } = "free"; // free | courier | specify | unknown
    public List<GeneratedShipping> ShippingOptions { get; set; } = new();
}

public sealed record GeneratedShipping(decimal Price, string Region, string Rural, bool Signed);

public sealed record ListingDraftResult(bool Ok, string? Error, GeneratedFields? Fields);

public interface IListingGenerator
{
    /// <summary>
    /// Generates listing fields from the draft's photos via OpenAI + category drill-down tools.
    /// <paramref name="progress"/> receives human-readable status lines as work proceeds.
    /// </summary>
    Task<ListingDraftResult> GenerateAsync(
        DraftFolder draft, IProgress<string>? progress = null, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ListingGenerator : IListingGenerator
{
    private const int MaxToolLoops = 8;
    private const int MaxImages = 6;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"
    };

    private readonly OpenAiClient _openAi;
    private readonly ICategoryService _categories;
    private readonly IDraftService _drafts;

    public ListingGenerator(OpenAiClient openAi, ICategoryService categories, IDraftService drafts)
    {
        _openAi = openAi;
        _categories = categories;
        _drafts = drafts;
    }

    public async Task<ListingDraftResult> GenerateAsync(
        DraftFolder draft, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var imageNames = draft.Listing.LocalImagePaths.Take(MaxImages).ToList();
        var images = BuildImageParts(draft);
        if (images.Count == 0)
            return new ListingDraftResult(false, "This draft has no photos to analyse.", null);

        progress?.Report($"Submitting {images.Count} photo(s): {string.Join(", ", imageNames)}");

        var instructions = BuildInstructions();
        var tools = BuildTools();
        progress?.Report("Asking the model to analyse the photos and choose a category…");

        var firstParts = new List<ContentPart>
        {
            ContentPart.TextPart(
                "Create a TradeMe listing for the item shown in these photos. Use the category tools to " +
                "find the most specific correct category, then call submit_listing with all fields.")
        };
        firstParts.AddRange(images);

        IReadOnlyList<InputItem> input = new[] {InputItem.UserMessage(firstParts)};
        string? previousResponseId = null;

        try
        {
            for (var loop = 0; loop < MaxToolLoops; loop++)
            {
                progress?.Report($"Round {loop + 1}: waiting for the model…");
                var response = await _openAi.CreateResponseAsync(input, tools, instructions, previousResponseId, ct);
                previousResponseId = response.Id;

                var calls = response.FunctionCalls.ToList();
                if (calls.Count == 0)
                {
                    var msg = response.MessageText;
                    progress?.Report("Model replied without submitting a listing.");
                    return new ListingDraftResult(false,
                        string.IsNullOrWhiteSpace(msg) ? "The model returned no listing." : msg, null);
                }

                var outputs = new List<InputItem>();
                foreach (var call in calls)
                {
                    if (call.Name == "submit_listing")
                    {
                        progress?.Report("Model returned a complete listing ✓");
                        return new ListingDraftResult(true, null, ParseSubmit(call.Arguments));
                    }

                    var result = DispatchTool(call);
                    progress?.Report($"Tool: {DescribeCall(call)} → {DescribeResult(result)}");
                    outputs.Add(InputItem.FunctionOutput(call.CallId, result));
                }

                // Next turn carries prior context via previous_response_id; send only tool outputs.
                input = outputs;
            }

            progress?.Report("Gave up after too many category lookups.");
            return new ListingDraftResult(false, "Gave up after too many category lookups.", null);
        }
        catch (OperationCanceledException)
        {
            return new ListingDraftResult(false, "Cancelled.", null);
        }
        catch (Exception ex)
        {
            return new ListingDraftResult(false, ex.Message, null);
        }
    }

    // ----- tool dispatch -----

    private string DispatchTool(FunctionCallItem call) => call.Name switch
    {
        "get_top_categories" => Project(_categories.GetTopLevel()),
        "get_subcategories" => Project(_categories.GetChildren(ReadNumber(call.Arguments))),
        _ => JsonSerializer.Serialize(new {error = $"Unknown tool: {call.Name}"})
    };

    private static string DescribeCall(FunctionCallItem call)
    {
        if (call.Name == "get_subcategories")
            return $"get_subcategories({ReadNumber(call.Arguments)})";
        return call.Name;
    }

    private static string DescribeResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var names = doc.RootElement.EnumerateArray()
                    .Select(e => e.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Take(8)
                    .ToList();
                var extra = doc.RootElement.GetArrayLength() - names.Count;
                var list = string.Join(", ", names);
                return extra > 0 ? $"{list}, +{extra} more" : list;
            }
        }
        catch
        {
            // fall through
        }
        return "(done)";
    }

    private static string ReadNumber(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            return doc.RootElement.TryGetProperty("number", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Project(IReadOnlyList<CategorySummary> cats) =>
        JsonSerializer.Serialize(cats.Select(c => new
        {
            name = c.Name,
            number = c.Number,
            isLeaf = c.IsLeaf,
            hasChildren = c.HasChildren
        }));

    // ----- prompt -----

    private string BuildInstructions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert TradeMe (New Zealand) seller assistant. From the supplied photos, produce a complete General-item listing.");
        sb.AppendLine();
        sb.AppendLine("Style: New Zealand English. Matter-of-fact and factual — describe what the item is, its features, materials, size, and visible condition. Avoid hype, marketing flourishes, and exclamation marks.");
        sb.AppendLine();
        sb.AppendLine("Return BOTH:");
        sb.AppendLine("- image_description: a thorough, factual description of exactly what is visible in the photos (item, materials, markings, wear, included parts).");
        sb.AppendLine("- description: the listing's sales description for buyers (concise, factual, paragraphs separated by newlines).");
        sb.AppendLine();
        sb.AppendLine("Category: call get_top_categories, then get_subcategories({number}) to drill down until you reach a leaf category (isLeaf=true). Submit that leaf's exact Number as category_number and its readable path as category_path. The top-level categories are:");
        foreach (var top in _categories.GetTopLevel())
            sb.AppendLine($"  - {top.Name} ({top.Number})");
        sb.AppendLine();

        var recent = BuildRecentListings();
        if (!string.IsNullOrEmpty(recent))
        {
            sb.AppendLine("Recently listed items (for category and pricing guidance — if the new item clearly matches one of these categories you may reuse its category_number without drilling):");
            sb.AppendLine(recent);
            sb.AppendLine();
        }

        sb.AppendLine("Pricing (NZD): suggest sensible, conservative values. run_auction and set_buy_now are independent — turn run_auction on for an auction (low start_price + optional reserve_price), set_buy_now on for a fixed/instant price (buy_now_price); both may be on. allow_offers lets buyers make an offer. duration_days defaults to 7 (allowed: 2,3,4,5,6,7,10,14).");
        sb.AppendLine("condition is \"Used\" or \"New\". pickup_option is \"Allow\", \"Demand\", or \"Forbid\".");
        sb.AppendLine("shipping_method is one of: \"free\" (free NZ shipping), \"courier\" (calculate courier costs), \"specify\" (you MUST then provide shipping_options), or \"unknown\" (decide later).");
        sb.AppendLine("Each specify shipping_options row has: price (NZD), region (default \"nz\" = nationwide New Zealand), rural (\"Any\", \"Urban\", or \"Rural\"), and optional signed. Rows must be UNIQUE by (region, rural). To offer two nationwide rates, use region \"nz\" with rural \"Urban\" for the cheaper urban rate and rural \"Rural\" for the higher rural rate — never two rows with the same region+rural.");
        sb.AppendLine("Keep title <= 50 characters. When confident, call submit_listing exactly once with every field.");
        return sb.ToString();
    }

    private string BuildRecentListings()
    {
        var recent = _drafts.GetListed()
            .Concat(_drafts.GetDrafts())
            .Where(d => !string.IsNullOrWhiteSpace(d.Listing.Title) && !string.IsNullOrWhiteSpace(d.Listing.CategoryId))
            .OrderByDescending(d => d.Listing.CreatedUtc)
            .Take(10)
            .ToList();

        var sb = new StringBuilder();
        foreach (var d in recent)
        {
            var l = d.Listing;
            sb.AppendLine($"  - \"{l.Title}\" → category {l.CategoryId}; start ${l.StartPrice:0.##}, buyNow ${l.BuyNowPrice:0.##}, {l.Condition}");
        }
        return sb.ToString();
    }

    // ----- tools / schema -----

    private static IReadOnlyList<FunctionTool> BuildTools() => new[]
    {
        FunctionTool.Create("get_top_categories",
            "List the top-level TradeMe General-item categories.",
            new {type = "object", properties = new {}, additionalProperties = false}),

        FunctionTool.Create("get_subcategories",
            "List the immediate child categories of a category, given its Number (e.g. \"0004-\").",
            new
            {
                type = "object",
                properties = new {number = new {type = "string", description = "Category Number to expand."}},
                required = new[] {"number"},
                additionalProperties = false
            }),

        FunctionTool.Create("submit_listing",
            "Submit the finished listing. Call once, after choosing a leaf category.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["title"] = new {type = "string", maxLength = 50},
                    ["subtitle"] = new {type = "string"},
                    ["category_number"] = new {type = "string", description = "Leaf category Number, e.g. 0153-0435-3648-"},
                    ["category_path"] = new {type = "string"},
                    ["condition"] = new {type = "string", @enum = new[] {"Used", "New"}},
                    ["description"] = new {type = "string"},
                    ["image_description"] = new {type = "string"},
                    ["run_auction"] = new {type = "boolean"},
                    ["set_buy_now"] = new {type = "boolean"},
                    ["allow_offers"] = new {type = "boolean"},
                    ["start_price"] = new {type = "number"},
                    ["reserve_price"] = new {type = "number"},
                    ["buy_now_price"] = new {type = "number"},
                    ["duration_days"] = new {type = "integer", @enum = new[] {2, 3, 4, 5, 6, 7, 10, 14}},
                    ["pickup_option"] = new {type = "string", @enum = new[] {"Allow", "Demand", "Forbid"}},
                    ["shipping_method"] = new {type = "string", @enum = new[] {"free", "courier", "specify", "unknown"}},
                    ["shipping_options"] = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["price"] = new {type = "number"},
                                ["region"] = new
                                {
                                    type = "string",
                                    @enum = ShippingRegions.All.Select(r => r.Value).ToArray()
                                },
                                ["rural"] = new {type = "string", @enum = new[] {"Any", "Urban", "Rural"}},
                                ["signed"] = new {type = "boolean"}
                            },
                            required = new[] {"price", "region", "rural"}
                        }
                    }
                },
                required = new[]
                {
                    "title", "category_number", "condition", "description", "image_description",
                    "run_auction", "set_buy_now", "duration_days", "pickup_option", "shipping_method"
                }
            })
    };

    // ----- parse submit_listing arguments -----

    private static GeneratedFields ParseSubmit(string argumentsJson)
    {
        var f = new GeneratedFields();
        using var doc = JsonDocument.Parse(argumentsJson);
        var r = doc.RootElement;

        f.Title = Str(r, "title");
        f.Subtitle = Str(r, "subtitle");
        f.CategoryNumber = Str(r, "category_number");
        f.CategoryPath = Str(r, "category_path");
        f.Condition = Str(r, "condition", "Used");
        f.Description = Str(r, "description");
        f.ImageDescription = Str(r, "image_description");
        f.IsAuction = Bool(r, "run_auction");
        f.HasBuyNow = Bool(r, "set_buy_now");
        f.AllowOffers = Bool(r, "allow_offers");
        f.StartPrice = Dec(r, "start_price");
        f.ReservePrice = Dec(r, "reserve_price");
        f.BuyNowPrice = Dec(r, "buy_now_price");
        f.DurationDays = Int(r, "duration_days", 7);
        f.PickupOption = Str(r, "pickup_option", "Allow");
        f.Shipping = Str(r, "shipping_method", "free").ToLowerInvariant();

        if (r.TryGetProperty("shipping_options", out var so) && so.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in so.EnumerateArray())
                f.ShippingOptions.Add(new GeneratedShipping(
                    Dec(item, "price"),
                    Str(item, "region", ShippingRegions.DefaultRegion),
                    Str(item, "rural", ShippingRegions.DefaultRural),
                    Bool(item, "signed")));
        }

        return f;
    }

    private static string Str(JsonElement e, string name, string fallback = "") =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True ||
            (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));

    private static int Int(JsonElement e, string name, int fallback)
    {
        if (!e.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return fallback;
    }

    private static decimal Dec(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0m;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var s)) return s;
        return 0m;
    }

    // ----- images -----

    private List<ContentPart> BuildImageParts(DraftFolder draft)
    {
        var parts = new List<ContentPart>();
        foreach (var fileName in draft.Listing.LocalImagePaths.Take(MaxImages))
        {
            var path = Path.Combine(draft.FolderPath, fileName);
            if (!File.Exists(path) || !ImageExtensions.Contains(Path.GetExtension(path)))
                continue;

            var bytes = File.ReadAllBytes(path);
            var dataUrl = $"data:{MimeFor(path)};base64,{Convert.ToBase64String(bytes)}";
            parts.Add(ContentPart.ImagePart(dataUrl));
        }
        return parts;
    }

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        _ => "image/jpeg"
    };
}
