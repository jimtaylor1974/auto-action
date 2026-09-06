# Facebook Marketplace "Item for sale" flow — findings

Captured 2026-09-06 by driving the live site over CDP (`tools/cdp/eval.mjs --match marketplace/create`),
logged in as the seller in the dedicated debug profile. Stopped at the **Publish** button; nothing was
published. Companion to [`trademe-listing-flow.md`](trademe-listing-flow.md).

## Running scripts on the page (CSP)

Same story as TradeMe: Facebook's CSP blocks in-page string eval, but CDP `Runtime.evaluate` (dev) and
`chrome.scripting.executeScript({func})` in the extension's isolated world (production) are both exempt.
The existing native-setter + `input`/`change` dispatch in `steps.ts` is exactly what Facebook's React
inputs need — it worked first time on Title, Price and Description.

Facebook has **no stable ids or class names**: ids are React `_r_xx_` and classes are hashed. Every
selector below is by `role`, `aria-*` or visible text.

## Route map

```
/marketplace/create/item                    →  step 1: photos + details (single page, "More details" expander)
/marketplace/create/item?step=audience      →  step 2: "List in more places" (Marketplace + up to 20 groups)
[Publish]                                   →  (not exercised) listing page /marketplace/item/{id}/
```

Step nav: `[role=button]` text **"Next"** (step 1 → 2), **"Previous"** (2 → 1), **"Publish"** (final — hold off).
There is also a **"Save Draft"** button; drafts are otherwise not auto-saved.

## Step 1 — fields, selectors, behaviour

| Field | Required | Control | How to drive it |
|---|---|---|---|
| **Photos** | yes (up to **10**) | `input[type=file][accept="image/*,image/heif,image/heic"][multiple]` | `DataTransfer` → `input.files` → dispatch `change`. Uploads to the FB CDN immediately; counter reads "Photos · n/10", each thumb has `[role=button][aria-label="Remove photo n of m"]`. **webp/heic accepted** (unlike TradeMe). A second `input[type=file][accept="video/*"]` exists for one video. |
| **Title** | yes | `input[type=text]` inside a `<label>` whose first `<span>` is "Title" | native setter + `input` event |
| **Price** | yes | `input[type=text]` labelled "Price" | native setter; FB reformats to `$25` itself |
| **Category** | yes | `label[role=combobox]` whose text starts "Category", `aria-expanded` | **Needs a real pointer sequence** (`pointerdown, mousedown, pointerup, mouseup, click`); a bare `.click()` does nothing. Opens `[role=dialog][aria-label="Drop-down menu"]` containing a **flat grouped list of leaf categories** as `[role=button]` (text may have "Delivery available" appended — match with `startsWith`). Clicking a leaf closes the dialog. |
| **Condition** | yes | `label[role=combobox][aria-haspopup=listbox]` text "Condition" | plain `.click()` opens a `[role=listbox]`; choose `[role=option]` by text. **Escape does not close it** — pick an option. |
| Description | no (strongly recommended) | `textarea` labelled "Description" (under "More details") | native setter; multi-line preserved |
| Category attributes | no | extra `label[role=combobox]` pickers that appear after choosing a category (e.g. Bags & luggage → Brand, Colour, Luggage material, Material) | same listbox pattern as Condition |
| Availability | no | `label[role=combobox]` "Availability", default **List as in stock** | options: "List as single item" / "List as in stock" |
| Product tags | no | `textarea` "Product tags" (limit 20) | native setter |
| SKU | no | `input[type=text]` "SKU" (only visible to seller) | native setter |
| Location | pre-filled | `input[role=combobox][aria-label="Location"]` typeahead, prefilled from profile ("Paraparaumu") | leave alone |
| Meetup preferences | no | three `div[role=checkbox]` (`aria-checked`): **Public meetup / Door pick-up / Door drop-off** | `.click()` toggles; match on parent text |
| Promote listing after publish | no | `input[type=checkbox][role=switch][aria-label="Promote listing after publish"]` | leave off |
| Hide from friends | no | `input[type=checkbox][role=switch][aria-label="Hide from friends"]` | user preference |

"More details" is a collapsed `[role=button]` whose text starts "More details" — click it once before
touching Description/attributes/tags/SKU/meetup.

**No shipping / checkout in NZ.** Even categories tagged "Delivery available" only offered meetup
preferences; there is no shipping price, no auction, reserve, Buy Now, duration or promote package.

### Category list (NZ, 2026-09)

Home & garden: Tools · Furniture · Household · Garden · Appliances
Entertainment: Video Games · Books, films & music
Clothing & accessories: Bags & luggage · Women's clothing & shoes · Men's clothing & shoes · Jewellery and accessories
Family: Health & beauty · Pet supplies · Baby & children · Toys and games
Electronics: Electronics & computers · Mobile phones
Hobbies: Bicycles · Arts & crafts · Sport and outdoors · Car parts · Musical Instruments · Antiques and collectibles
Classifieds: Garage sale · Miscellaneous
Vehicles (separate flow, ignore)

### Condition options

New · Used – like new · Used – good · Used – fair  (en-dash, not hyphen)

## Step 2 — audience (groups)

`?step=audience`. "List publicly on Marketplace" is fixed; below it a list of the user's buy/sell groups
(up to 20 selectable) as `[role=checkbox]` rows. Then **Previous** / **Publish**. The right-hand preview
shows the composed listing and names any ticked groups.

Each row: `div[role=checkbox][aria-checked]` inside a `[role=button]` whose text is
`"<group name><N members · Private|Public>"`. Parse with `/^(.*?)(\d[\d.,]*K? members · (Private|Public))$/`
to split name from meta. Verified: `.click()` on the checkbox flips `aria-checked` and the preview
updates; clicking again unticks. Eight groups were listed for this account with no "see more" control;
the list is presumably capped by what the account is a member of.

**Fetch-and-select design:**

1. The injected step-2 script reads the rows (name, member count, visibility) and sends them to the
   side panel, which POSTs them to the bridge (`POST /api/facebook/groups`) so the desktop can cache
   them (`facebook-groups.json` next to `categories.json`).
2. The desktop shows the cached groups as checkboxes on the draft (and a "default groups" preference,
   à la `preferences.json`). The draft stores `FacebookGroups: string[]` (names).
3. On fill, the step-2 script ticks each row whose parsed name matches a requested name, reports any
   requested name it couldn't find, and leaves the user on the Publish button.

Match on the full name (case-insensitive, whitespace-normalised); names are unique in practice but
member counts change, so never match on the meta text.

## Mapping to AutoAuction `ListingModel`

| ListingModel | Facebook |
|---|---|
| `Title` | Title (no hard 50-char limit observed; keep TradeMe's) |
| `Description` | Description |
| `Condition` Used/New | New → "New"; Used → needs a choice of like new / good / fair (**new field**, default "Used – good") |
| `CategoryId` (TradeMe) | **not reusable** — needs a Facebook category leaf name (**new field**); could default via a TradeMe-top-level → FB map |
| `BuyNowPrice` (or `StartPrice` if auction-only) | Price |
| `PickupOption` | Meetup preferences: Allow/Demand → Door pick-up (+ Public meetup); Forbid → none |
| `Shipping*`, `DurationDays`, `ReservePrice`, `AllowOffers`, promote | no equivalent — ignore |
| `LocalImagePaths` | Photos file input (first 10) |
| `TradeMeListingId/Url` | needs a per-platform record (`FacebookListingId/Url`) captured after Publish |
| — | `FacebookGroups: string[]` (**new field**) — group names to tick on step 2, see above |

## Status

Filled Title, Price, Category (Bags & luggage), Condition (Used – good), Description, one photo, Public
meetup; advanced to `?step=audience`; **did not click Publish**. The debug Chrome window was left on
that step so it can be inspected or discarded (Previous → close tab; FB may offer to save a draft).

Not yet verified: the published listing URL pattern (assumed `/marketplace/item/{id}/`), behaviour of the
Location typeahead, and whether rapid programmatic input trips any anti-automation check (none seen).
