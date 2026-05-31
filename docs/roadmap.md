# AutoAuction - Implementation Roadmap

## 🌟 Project Overview
We are building a local-first, cross-platform utility using **C# (Avalonia UI)** and a **Chrome Extension**. The goal is to eliminate "listing fatigue" for TradeMe sellers by creating an extremely fast, AI-assisted workflow for generating item listings, and a seamless browser bridge to publish them.

### 🚫 The Architecture: The Local Companion Bridge (no TradeMe API)
We are **not** using the TradeMe selling API. Due to TradeMe's 2026 restrictions on live API
access for casual sellers, and the aggressive Cloudflare anti-bot protections against headless
browser automation (Playwright/Selenium), publishing happens **entirely through a Chrome
extension bridge** driving the real, authenticated browser session:
1.  **The Desktop App (Avalonia):** Handles local files, offline JSON storage, and AI processing. It runs a tiny background HTTP server on localhost that exposes the active draft.
2.  **The Chrome Extension:** Runs in your normal, authenticated browser. It fetches the active listing data from the local C# server and uses vanilla JavaScript DOM manipulation to instantly fill out the TradeMe web forms.

### Core Architecture & Philosophy
*   **File System as Database:** We will not use SQLite. The local folder structure (`1_Inbox` -> `2_Drafts` -> `3_Listed`) dictates state. Data for each listing is saved in a local `listing.json` file inside its respective folder. A `ListingStatus` (Draft → Listed → Sold) on the model tracks lifecycle within `3_Listed`, with **Relist** moving an item back to `2_Drafts`.
*   **Human-in-the-Loop:** The AI analyzes photos and drafts the listing, but the user always reviews it in the desktop app before clicking "Fill" in the browser.
*   **Preference Memory:** A lightweight `preferences.json` file remembers user habits (e.g., "Always charge $8 shipping for shoes").

### 🧱 Adopted Tech Stack (aligned with the pii-scanner companion app)
*   **MVVM:** **ReactiveUI** (`ReactiveObject` / `RaiseAndSetIfChanged` / `ReactiveCommand`). CommunityToolkit.Mvvm has been fully removed so both solutions share one paradigm.
*   **Navigation shell:** top menu + 90px left icon rail (`Material.Icons.Avalonia`) + a `ContentControl` whose page is swapped by `MainWindowViewModel` via the convention `ViewLocator`. Pages: **Home**, **Listed**, **Settings** (more to come).
*   **Tables:** `Avalonia.Controls.DataGrid` for the Drafts and Listed lists.
*   **Settings:** stored as `settings.json` in the app root folder (`Documents/AutoAuction`) via `ISettingsService` — server port and OpenAI model id. The **API key is never in `settings.json`**: it's held encrypted via `ISecretStore` (Windows DPAPI / macOS Keychain / Linux libsecret), entered through a set-only field (masked once stored, with Change/Clear).
*   **AI:** the **OpenAI Responses API**, called directly over HTTP (`OpenAiClient`, no SDK). Listing-from-photos generation lands in Phase 3; secure key entry + connection test exist now.

### 🧭 Current UI Layout
*   **Home:** Inbox gallery (drag/drop + Add, multi-select) on the left; **Drafts DataGrid** on the right. Double-click a draft → full-page **Draft Detail** editor (auto-save, Discard, Open folder, **Mark as Listed**).
*   **Listed:** DataGrid of `3_Listed` items with **Mark Sold** and **Relist** per row.
*   **Settings:** bridge server (port / URL / find-free-port / start-stop / auto-start), OpenAI key + model, app-folder access.
*   **Preflight banner:** on launch `PreflightService` checks internet, cached categories, and a stored OpenAI key; a shell-wide banner (below the menu) nudges the user to Settings when any check fails, with Open Settings / Re-check, and auto-hides once everything passes.

---

## 🏗️ Phase 1: Foundation (The Local Manual Editor) — ✅ Done
**Objective:** Build the core Avalonia desktop app, establish the folder-based data architecture, and create a manual UI for editing listings. *No external APIs, browser extensions, or AI yet.*
*Status: complete, and since extended with the ReactiveUI navigation shell, Settings, and the Listed lifecycle described above.*

**Step 1: Project Setup & Architecture**
*   Initialize a C# Solution (`AutoAuction.sln`) with MVVM architecture (`CommunityToolkit.Mvvm`).
*   Create an `AppConfigService` to initialize the local folder structure on startup:
    *   `📁 1_Inbox` (Raw photos drop here)
    *   `📁 2_Drafts` (Work-in-progress listings)
    *   `📁 3_Listed` (Successfully published listings)

**Step 2: Inbox & Gallery UI**
*   Build a "Gallery" view on the left side of the app watching `1_Inbox`.
*   Allow the user to multi-select images.
*   "Create Draft" button logic: Generate a unique folder (`2_Drafts/GUID/`), move selected images there, and create an empty `listing.json`.

**Step 3: The Draft Editor & Data Model**
*   Create a `ListingModel.cs` class. Based on TradeMe requirements, it must include:
    *   `Title` (max 50 chars), `Subtitle`, `CategoryId` (string).
    *   `Condition` (Enum: 1=Used, 2=New).
    *   `Description` (string).
    *   `IsBuyNowOnly` (bool), `StartPrice`, `ReservePrice`, `BuyNowPrice` (decimals).
    *   `DurationDays` (int).
    *   `PickupOption` (Enum: 1=Allow, 2=Demand, 3=Forbid).
    *   `ShippingType` (Enum: 1=Custom, 2=Free, 3=Undecided).
    *   `LocalImagePaths` (List of file paths).
*   Build a data-bound UI form for this model.
*   Implement Auto-save: Any keystroke changes in the UI serialize back to `listing.json`.

---

## 🔌 Phase 2: The Browser Bridge (Chrome Extension)
**Objective:** Implement a local HTTP server in the Avalonia app and build a Chrome Extension to safely automate data entry in the browser based on the active local draft.

**Step 1: The Local Server (C# Desktop) — ✅ Done (brought forward)**
*   `AutoAuction.Core/Services/LocalBridgeServer` implements a lightweight `System.Net.HttpListener` server.
*   Runs on `http://localhost:{ServerPort}` (default `5999`, configurable in Settings; auto-starts with the app and is started/stopped from Settings).
*   Endpoint 1: `GET /api/drafts/active` returns the `listing.json` of the draft currently open in the editor (via `IActiveListingProvider`, read fresh from disk so it reflects the latest auto-save). 404 when no draft is active.
*   Endpoint 2: `GET /api/drafts/active/images` returns the active draft's images as base64 (`{fileName, contentType, base64}`).
*   CORS headers allow requests from `https://www.trademe.co.nz` (incl. `OPTIONS` preflight).

**Step 2: Chrome Extension Setup — ⏳ Next (front-end not yet built)**
*   Create a Manifest V3 extension.
*   Grant host permissions for `http://localhost:5999/*` and `https://www.trademe.co.nz/*`.
*   Inject a floating button onto TradeMe "Sell" pages: *"⚡ Fill from AutoAuction"*.

**Step 3: Text & DOM Injection (JavaScript)**
*   When the extension button is clicked, fetch the JSON from Endpoint 1.
*   Map the JSON to the TradeMe DOM elements (e.g., `document.querySelector('input[name="Title"]').value = draft.Title`).
*   **Crucial:** Because TradeMe uses React/modern frameworks, the extension must dispatch `input` and `change` events after altering a value so the frontend registers the text.

**Step 4: Image Injection (The DataTransfer Trick)**
*   When on the Photos step, fetch the images from Endpoint 2.
*   Use JavaScript's `DataTransfer` object to construct a simulated `FileList` containing the downloaded image blobs.
*   Find TradeMe's hidden `<input type="file">` inside the dropzone, assign the fake `FileList` to it, and dispatch a `change` event.

---

## 🧠 Phase 3: Intelligence (AI & Memory)
**Objective:** Replace manual typing in Phase 1 with AI Vision and generate listing details automatically from the photos.

**Step 1: AI Vision Integration**
*   AI client already exists: `OpenAiClient` (direct OpenAI Responses API calls) with the key/model configured in Settings. This step wires it into draft generation.
*   Update the "Create Draft" button logic:
    *   Resize/compress images in memory, convert to Base64.
    *   Send to the AI with a strict JSON schema prompt: *"You are a TradeMe seller. Output a JSON object matching this schema with Title, Description, Condition, and Price..."*
    *   Save the AI's output to `listing.json` automatically.

**Step 2: The Memory System (`preferences.json`)**
*   Create a `preferences.json` file in the root app data folder.
*   Add a UI checkbox: "Save pricing/shipping rules for similar items".
*   When checked, append a rule (e.g., *"Keyword: Jackets -> $10 Shipping, 7 Day Duration"*).
*   Inject this file's contents into the AI System Prompt so the AI learns the user's listing habits over time.
