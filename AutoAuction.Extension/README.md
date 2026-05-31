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

- **Now:** connectivity test (this version).
- **Next:** a floating "⚡ Fill from AutoAuction" button injected onto TradeMe sell pages, mapping
  the active draft's fields onto the form DOM (dispatching `input`/`change` events so React
  registers them), and the `DataTransfer` trick to push the draft's photos into the upload control.

## Caveats

- **PoC.** `host_permissions` is broad for localhost during development; tighten before any release.
- SVG action icons may log a load warning in some Chrome builds; it's non-fatal for local dev.
