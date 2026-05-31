# TradeMe "General item" listing flow — findings

Captured by driving the live site over CDP (`tools/cdp/eval.mjs`). The repeatable
automation steps are in [`tools/cdp/trademe-listing-steps.js`](../tools/cdp/trademe-listing-steps.js).

## Running scripts on the page (CSP)

TradeMe sends `Content-Security-Policy: script-src 'self'` — so **in-page `eval`/`new Function`
is blocked** (that was the original error). Two CSP-safe paths:

- **Dev/discovery:** CDP `Runtime.evaluate` (`tools/cdp/eval.mjs`) — exempt from page CSP, like the console.
- **Production (extension):** `chrome.scripting.executeScript({ func, args })` in the ISOLATED world —
  injects a real function (no string eval), so the page CSP doesn't apply.

Never inject code as a string. The step library is plain functions for exactly this reason.

## Route map (single SPA, one draft GUID)

```
/a/  →  /a/list  →  (General item)  →  /a/marketplace/create-listing/{guid}/title-category
  → /item-details → /photos → /price-payment → /delivery → /promote → [Start listing]
```

Entry clicks: `a[href="/a/list"]` → `a[href="/a/marketplace/list"]` (General item).
Step nav tabs are `<button>`s with the step names, so you can jump directly.
Advance button is `<button>` text **"Next"**; final publish is **"Start listing"** (hold off).

### Existing-drafts interstitial ("Continue or start again?")

If you already have in-progress drafts, **General item lands on `/a/marketplace/create-listing`
(no GUID) showing "You have started a listing. Select a draft below to continue or start a new
listing."** instead of a fresh form. The automation must click **`<button class="o-button2">Start
new listing</button>`** (matched by text "Start new listing") to get a fresh draft, which then
routes to `/create-listing/{guid}/title-category`. Handled by `handleStartNewInterstitial()` in the
step library — a real draft URL is detected by the `/create-listing/<guid>/` GUID segment, so it's
a no-op when already inside a draft.

## Steps, selectors & required fields

| Step | Required | Optional / notes | Key selectors |
|---|---|---|---|
| **Title & category** | **Listing title**, **Category** | Subtitle (paid; appears after category) | title `input[placeholder="Listing title"]`; open `.koru-category-selector__choose-button`; tree nodes `tm-tree-picker .tm-tree-picker__children` (match `tg-media-block-content` text); leaf click closes `.o-modal__dialog` |
| **Item details** | **Description** (`textarea`), **Condition** (New/Used) | Category attributes (e.g. *Colour*) render here, optional | condition radios `input[name="attribute:Condition"]`, selected via `label[for=<id>]` text ("New"/"Used") — ids are dynamic |
| **Photos** | (validated later) | up to 20 photos | `input[name="listing-progress-add-photo"]` (`multiple`, accept **bmp/gif/jpg/jpeg/png** — **no webp/heic**). Inject via `DataTransfer` → `input.files` → dispatch `change` |
| **Price & payment** | depends on mode | "Run an auction" → **Start price** (req), Reserve (opt); "Set a Buy Now price" → **Buy Now price** (req). Both can be on together. Payment: Buyer-Preferred/instant (on by default), Cash, send instructions; "authenticated bidders only" | sell-method native `input[type=checkbox]` inside the labels; price inputs are `type="tel"`, matched by label text |
| **Shipping & pick-up** (`/delivery`) | a shipping method choice | Location pre-filled from account ("Where is the item?"). Methods: **Free shipping within New Zealand** / Calculate courier costs / Specify shipping costs / I don't know costs yet. "Allow pick-up?" toggle | options matched by visible text |
| **Promote** | — (Basic is free default) | Packages: **Feature Combo $3.95** / **Feature $3.45** / **Gallery 55¢** / Basic (free). Also "optimise closing time" (day/time). | package radios `input[name="selector:package"]`, selected via `label[for]` text. **Default in our automation: Gallery** (per requirement) |

**Universal required to publish:** Title, Category, Description, Condition, a price (Start or Buy Now),
a shipping method. Duration wasn't surfaced as a field in this walk (TradeMe default = 7 days;
per-category `AllowedDurations` come from the API below).

## Categories & attributes come from TradeMe's public API (no auth)

Far more reliable than scraping the picker:

- **Full category tree:** `GET https://api.trademe.co.nz/v1/Categories.json` (**6,481 nodes,
  5,589 leaves, depth 4**; 24 General-item top-level categories, with Motors/Property/Jobs/Services/
  Flatmates being other `AreaOfBusiness` values). Each node: `Name, Number, Path, Subcategories,
  IsLeaf, AreaOfBusiness, CanHaveSecondCategory`.
  - The **desktop app** caches the **General-item subset only** (`AreaOfBusiness == 1`, ~5,931 nodes,
    indented) to `categories.json` in the app root — fetched on first run and refreshable from
    Settings (`ICategoryService`). A full-tree dev dump can be regenerated with the `eval.mjs`
    fetch one-liner if needed (it's gitignored, not committed).
- **Per-category details:** `GET /v1/Categories/{number}/Details.json` — `CanListAuctions`,
  `CanListClassifieds`, `DefaultDuration`, `AllowedDurations`, `Fees` (incl. Gallery 55¢,
  Feature, success-fee tiers), `FreePhotoCount`/`MaximumPhotoCount` (20), and `Attributes`.
- **Per-category attributes:** `GET /v1/Categories/{number}/Attributes.json` — list of
  `{Name, DisplayName, Type, Options[]}` (e.g. Colour/Size/Brand). **No `IsRequired` flag** —
  required-ness is enforced by the listing UI, not the catalogue API.

(`{number}` is the category `Number` with trailing dashes stripped, e.g. `0153-0435-3648`.)

## Mapping to AutoAuction `ListingModel`

| ListingModel | TradeMe step / action |
|---|---|
| `Title` | Title input |
| `CategoryId` / path | Category picker (use API `Path`/`Number`) |
| `Subtitle` | Subtitle (optional, paid) |
| `Description` | Description textarea |
| `Condition` (Used/New) | Condition radio |
| `IsBuyNowOnly` / `StartPrice` / `ReservePrice` / `BuyNowPrice` | Price & payment toggles + tel inputs |
| `DurationDays` | default 7 (control not seen; validate against `AllowedDurations`) |
| `IsFreeShipping` / `ShippingOptions` | Shipping method radios |
| `PickupOption` | "Allow pick-up?" |
| `LocalImagePaths` | Photos file input (DataTransfer) |

## Status

Walked end-to-end with a real draft (GUID `d397fc25…`), filled Title/Category/Description/Condition,
Start + Buy Now price, Free shipping, and selected the **Gallery** promo — **stopped before
"Start listing"**. The draft remains unpublished in the debug Chrome profile.
