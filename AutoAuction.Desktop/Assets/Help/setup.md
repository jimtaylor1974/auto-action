# Setup

Welcome to **AutoAuction** — a local-first helper for creating TradeMe listings fast. Everything runs on your own PC: your photos, drafts, and settings never leave your machine. This page walks you through first-time setup.

## 1. Install and run

AutoAuction is a desktop app. Launch it and you'll land on the **Home** page, which shows your photo **Inbox** on the left. The app creates its data folder automatically at **Documents\AutoAuction**.

Inside it you'll find three folders that track each listing's lifecycle:

| Folder | Purpose |
| --- | --- |
| **1_Inbox** | Raw photos waiting to be turned into listings |
| **2_Drafts** | Work-in-progress listings |
| **3_Listed** | Listings you've published |

You can open this folder any time from **Settings → App data folder → Open**.

## 2. Configure the local bridge server

The Chrome extension talks to AutoAuction through a tiny server running on your own machine. Open **Settings → Local Bridge Server**:

1. Leave the **Port** at **5999** unless something else is using it — if so, click **Find free port**.
2. Tick **Start the server automatically when the app launches** so it's always ready.
3. Use **Start** / **Stop** to control it manually, and check the status line to confirm it's running.

## 3. Add your OpenAI key (for AI drafting)

AutoAuction can draft a listing's title, description, and pricing from your photos. Open **Settings → OpenAI**:

1. Paste your API key into the key field and click **Save key**. The key is encrypted by your operating system (Windows DPAPI / macOS Keychain / Linux keyring) — it is **never** stored in **settings.json**.
2. Set the **Model** if you want something other than the default.
3. Click **Test connection** to confirm the key works.

**Note:** You can use AutoAuction without a key — you'll just fill in listing details manually instead of using **Draft with AI**.

## 4. Download the TradeMe categories

Open **Settings → TradeMe Categories** and click **Fetch / refresh categories**. This caches the full category tree locally (as **categories.json**) so listings can be matched to the right category. Refresh it occasionally if TradeMe changes their categories.

## 5. Install the Chrome extension

Publishing happens through your real, logged-in browser — no TradeMe API required.

1. Load the AutoAuction Chrome extension in your browser.
2. When prompted, allow its host permissions for **localhost** (the bridge server) and **trademe.co.nz**.
3. On a TradeMe **Sell** page you'll see a **⚡ Fill from AutoAuction** button that fills the form from your active draft.

## 6. Set up quick photo import (optional)

The fastest way to get phone photos onto your PC is a cloud sync folder. If your phone syncs photos to a folder via **iCloud for Windows**, **OneDrive**, **Dropbox**, or **Google Drive**, point AutoAuction at it:

1. Open **Settings → Quick import folder**.
2. If a likely folder is detected, click **Use this** — otherwise **Browse…** to it.
3. Click **Save settings**.

A **cloud button** then appears in the Inbox header on Home. Click it to open straight into that folder and pick the photos you want. HEIC/HEIF photos from iPhones are converted to JPG automatically on import.

## You're all set

Head to the **User Guide** to learn the day-to-day listing workflow.
