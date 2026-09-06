# AutoAuction Bridge (Chrome Extension)

Chrome extension side-panel UI that connects to the **AutoAuction desktop app's local bridge
server** and (eventually) fills out TradeMe "Sell" forms from the active draft. The desktop app
runs a small HTTP server on `http://localhost:5999` exposing the currently-open draft at
`GET /api/drafts/active` (and its images at `GET /api/drafts/active/images`); this extension reads
that and drives the TradeMe page in the user's normal, authenticated browser session — no TradeMe
API involved.

**This first version is a connectivity test only.** The side panel has a single **Test connection**
button that calls the bridge and reports whether the desktop app is reachable, what draft (if any)
is currently active, and how many photos it has. Form-filling on TradeMe comes next.

## Build

From this folder:

```powershell
npm install
npm run build
```

The build writes the Chrome extension bundle to:

```text
AutoAuction.Extension/dist
```

That folder is the unpacked extension root. It contains the generated `manifest.json`,
`sidepanel.html`, `sidepanel.js`, and `serviceWorker.js` files that Chrome loads.

For development:

```powershell
npm run watch
```

The watcher rebuilds `dist` when source files change. Chrome does not auto-reload unpacked
extensions — after each rebuild, reload the extension card in `chrome://extensions`.

## Load In Chrome

1. Build the extension with `npm run build` (or keep `npm run watch` running).
2. Open Chrome and go to `chrome://extensions`.
3. Turn on **Developer mode**.
4. Click **Load unpacked** and select `AutoAuction.Extension/dist`.
5. Click the extension's action button to open the side panel.

## Testing the bridge

1. Launch the **AutoAuction desktop app**. The bridge server auto-starts (see
   Settings → Local Bridge Server; default port `5999`).
2. Open the extension side panel and click **Test connection**. Expected outcomes:
   - **✓ Connected** — the server is reachable *and* a draft is open in AutoAuction. The panel
     shows that draft's title, status, photo count, and id.
   - **✓ Connected to the bridge / No draft open** — the server is reachable but no draft is
     currently open. Open a draft in the desktop app and test again. (This still proves the
     connection works.)
   - **✗ Could not reach the bridge** — the desktop app isn't running, the server is stopped, or
     the URL/port is wrong.
3. If you changed the port in the desktop app's Settings, update the **Bridge server URL** field
   to match (e.g. `http://localhost:6001`); it's saved to extension storage between sessions.

## How the connection works

The side panel is an extension page (`chrome-extension://…`). Its `fetch` to
`http://localhost:5999` is permitted by the `host_permissions` entry for `http://localhost/*`,
so the request succeeds regardless of the server's CORS headers. (The desktop server additionally
sends `Access-Control-Allow-Origin: https://www.trademe.co.nz`, which is what lets a future
**content script running on the TradeMe page** read it — content scripts run with the page origin
and *are* subject to CORS.)

## Scripts

```powershell
npm run build         # production bundle in dist
npm run watch         # development rebuilds into dist
npm run format        # format TypeScript and TSX files
npm run format:check  # verify formatting
```

## Architecture

- `src/sidepanel.tsx` — the side panel React app. Owns the bridge-URL field and the **Test
  connection** button, calls the bridge, and renders the result + a small history log.
- `src/bridge.ts` — bridge-URL sanitisation + `chrome.storage.local` persistence, and
  `testBridgeConnection()` which hits `/api/drafts/active` (treating `404` as "connected, no
  draft open").
- `src/types.ts` — the `ActiveListing` / `ActiveImage` shapes returned by the bridge. Mirrors
  `AutoAuction.Core/Models/ListingModel.cs`.
- `src/serviceWorker.ts` — minimal background script: opens the side panel on the extension action.
- `public/manifest.json` — Manifest V3. `host_permissions` cover `http://localhost/*`,
  `http://127.0.0.1/*` (the bridge) and `https://www.trademe.co.nz/*` (for the upcoming form-fill
  content script). `scripting`/`activeTab`/`tabs` are present for that next step.

The desktop side of the bridge is `AutoAuction.Core/Services/LocalBridgeServer.cs`, fed the active
draft by `IActiveListingProvider` (set when a draft is opened in the desktop UI).

## Roadmap

- **Done:** TradeMe fill end to end (`src/steps.ts`, `src/trademeFill.ts`), stopping at "Start listing";
  published id/URL capture in the service worker marks the draft Listed.
- **Next: Facebook Marketplace as a second listing target.** Discovery is complete; see below.

## Facebook Marketplace — continuation notes (parked 2026-09-06)

**Where we got to.** The whole "Item for sale" flow was walked over CDP with a throwaway listing:
every field filled, one photo uploaded, advanced to the audience (groups) step, stopped at Publish.
The throwaway draft was then deleted. Full selectors, quirks, category/condition lists and the model
mapping are in [`docs/facebook-marketplace-flow.md`](../docs/facebook-marketplace-flow.md).
Nothing has been coded yet.

**Plan for next session:** pick a genuine item, create it as a draft in the desktop app, then run the
Facebook flow end to end (including Publish) and capture the resulting listing URL pattern, which is
still unverified (assumed `/marketplace/item/{id}/`).

**Restart the discovery environment:**

```powershell
& "C:\Program Files\Google\Chrome\Application\chrome.exe" `
    --remote-debugging-port=9222 --user-data-dir="C:\Theta\chrome-debug-profile" `
    https://www.facebook.com/marketplace/create/item
node tools/cdp/eval.mjs --match marketplace/create --expr "return document.title"
```

The debug profile is separate from everyday Chrome. It was logged in to Facebook on 2026-09-06 but
may need a fresh login (and a TradeMe login if that flow is exercised again). The walk-through
snippets were ad hoc and not kept; the flow doc has enough detail to rewrite them.

**Implementation checklist (extension):**

1. `manifest.json`: add `https://www.facebook.com/*` to `host_permissions`.
2. `src/facebookSteps.ts` + `src/facebookFill.ts`, mirroring the TradeMe pair. Lift `setInputValue`,
   `byText`, `waitFor` out of `steps.ts` into a shared module. Category needs the pointer-event
   sequence (pointerdown → click), not a bare `.click()`; listboxes close by choosing an option, not
   Escape; expand "More details" before touching Description/attributes/meetup.
3. Step 2 (`?step=audience`): read the group rows, send them to the side panel, which POSTs them to
   the bridge (`POST /api/facebook/groups`); tick rows whose name matches the draft's `FacebookGroups`;
   report unmatched names.
4. Side panel: a second button "Fill Facebook listing" that opens/reuses a facebook.com tab and injects
   `facebookFill.js`; arm capture for that tab.
5. Service worker capture: treat navigation from `/marketplace/create` to `/marketplace/item/...` as
   published; POST to a platform-aware endpoint (e.g. `/api/drafts/active/listed?platform=facebook`).

**Implementation checklist (desktop / Core):**

- `ListingModel`: `FacebookCategory` (leaf name), `UsedGrade` (like new / good / fair), `FacebookGroups`
  (names), and a per-platform listing record replacing the TradeMe-only `TradeMeListingId/Url`.
- Bridge: `POST /api/facebook/groups` cached to `facebook-groups.json`; extend CORS to facebook.com.
- Draft editor: Facebook category picker (flat list, in the flow doc), used grade, group checkboxes,
  and a default-groups preference.

## Caveats

- **PoC.** `host_permissions` is broad for localhost during development; tighten before any release.
- Icons are PNG (`public/icons/auto-auction-{16,32,48,128}.png`), rasterized from `auto-auction-icon.svg`; Chrome does not render SVG manifest icons.
