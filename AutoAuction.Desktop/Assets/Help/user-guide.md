# User Guide

This guide covers the everyday workflow: from photos in the Inbox to a published TradeMe listing. The flow is always the same — **Inbox → Draft → Listed**.

## 1. Get photos into the Inbox

The **Home** page shows your Inbox gallery. There are three ways to add photos:

- **Add** — opens a file picker to choose photos from your computer.
- **Drag & drop** — drag image files straight from File Explorer onto the gallery.
- **Cloud button** — if you've configured a quick-import folder in Settings, this opens straight into your synced cloud folder (iCloud / OneDrive / Dropbox / Google Drive).

Supported formats: JPG, PNG, WEBP, BMP, GIF, and HEIC/HEIF. **HEIC/HEIF photos (typical iPhone format) are converted to JPG automatically** on import, with rotation baked in so they always display upright.

Use **Open folder** to view the Inbox in your file manager, or **Refresh** to rescan it.

## 2. Turn photos into a draft

1. **Tick** the photos you want in the gallery.
2. Choose how to group them:
   - **Create 1 listing** — bundles all the selected photos into a single listing (e.g. several angles of one item).
   - **Create N listings (1 photo each)** — makes one listing per photo (great for listing many separate items quickly).

The selected photos move out of the Inbox and into a new draft folder, and you land in the **Draft editor**.

## 3. Edit the draft

The Draft editor is a form for everything TradeMe needs — title, subtitle, category, condition, description, pricing (start / reserve / Buy Now), duration, pickup, and shipping. Two things to know:

- **Auto-save** — every change is saved to the draft's `listing.json` as you type. There's no Save button to remember.
- **Draft with AI** — click this to have AI analyse the photos and fill in the title, description, condition, and a suggested price. Review and tweak anything before continuing. *(Requires an OpenAI key — see Setup.)*

Use **Open folder** to see the draft's files, or **Discard** to send its photos back to the Inbox and delete the draft.

## 4. Publish to TradeMe

Publishing uses the Chrome extension and your real browser session:

1. In AutoAuction, open the draft you want to publish so it becomes the **active draft**.
2. In Chrome, go to the TradeMe **Sell** page and start a new listing.
3. Click **⚡ Fill from AutoAuction** — the extension pulls your active draft from the local bridge server and fills the form, including the photos.
4. Review everything on TradeMe, then submit the listing there.

**Note:** Make sure the bridge server is running (**Settings → Local Bridge Server**) — the extension needs it to fetch the draft.

## 5. Track listed items

Once an item is live, click **Mark as Listed** in the editor. It moves to the **Listed** page, where each row offers:

- **Mark Sold** — record that the item sold (it stays in Listed for your records).
- **Relist** — move it back to Drafts to list it again.

## Quick reference

| I want to… | Do this |
| --- | --- |
| Add phone photos fast | Configure a Quick import folder, then use the **cloud button** on Home |
| Group photos into one listing | Tick them → **Create 1 listing** |
| List many items at once | Tick them → **Create N listings (1 photo each)** |
| Auto-write a listing | Open the draft → **Draft with AI** |
| Publish | Open draft → TradeMe Sell page → **⚡ Fill from AutoAuction** |
| Mark a sale | **Listed** page → **Mark Sold** |
