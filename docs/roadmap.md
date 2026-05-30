# TradeMe Listing Helper - Project Overview & Implementation Roadmap

## 🌟 Project Overview
We are building a cross-platform desktop application using **C# and Avalonia UI**. The goal is to eliminate "listing fatigue" for TradeMe sellers by creating an extremely fast, AI-assisted workflow for generating and publishing item listings. 

**Core Architecture & Philosophy:**
*   **File System as Database:** To keep the app offline-friendly, transparent, and resilient, we will not use a traditional database like SQLite. Instead, the local folder structure (`Inbox` -> `Drafts` -> `Listed`) dictates state. Data for each listing is saved in a local `listing.json` file inside its respective folder.
*   **Human-in-the-Loop:** The AI does the heavy lifting (analyzing photos, writing descriptions, guessing prices/categories), but the user *always* reviews and edits the data before it is pushed to TradeMe.
*   **Preference Memory:** The app will feature a lightweight memory system (`preferences.json`) to learn user habits (e.g., "Always charge $8 shipping for T-shirts").

## 🗺️ Roadmap Strategy: Why Phased?
To prevent context overload and ensure a stable application, we are building this incrementally. **Do not skip ahead.**
*   **Phase 1 (Foundation):** Focuses strictly on local state. We build the UI, the folder-watching mechanisms, and local JSON serialization. No external APIs. If the local data flow isn't perfect, nothing else will work.
*   **Phase 2 (Integration):** Focuses strictly on TradeMe. We introduce OAuth 1.0a, category fetching, photo uploading, and publishing. We build this against the manual editor to isolate API bugs from AI bugs.
*   **Phase 3 (Intelligence):** Focuses on the "Magic". We introduce the AI Vision API to replace manual data entry and implement the memory prompt-injection system.

---

## 🏗️ Phase 1: Foundation (The Local Manual Editor)
**Objective:** Build the core Avalonia desktop app, establish the folder-based data architecture, and create a manual UI for editing listings. *No external APIs or AI yet.*

**Step 1: Project Setup & Architecture**
*   Create a new Avalonia UI desktop project using the MVVM pattern (CommunityToolkit.Mvvm recommended).
*   Create an `AppConfig` service to define and initialize the local folder structure on startup:
    *   `📁 1_Inbox` (Raw photos get dropped here)
    *   `📁 2_Drafts` (Work-in-progress listings)
    *   `📁 3_Listed` (Successfully published listings)

**Step 2: Inbox & Gallery UI**
*   Build a "Gallery" view on the left side of the app that reads the `1_Inbox` folder.
*   Implement an image thumbnail viewer.
*   Allow the user to multi-select images in the Inbox.
*   Create a "Create Draft" button. When clicked:
    *   Generate a unique folder (e.g., `2_Drafts/GUID/`).
    *   Move the selected images into this folder.
    *   Create an empty/default `listing.json` in this folder.

**Step 3: The Draft Editor UI (Right Panel)**
*   Create a `ListingModel` C# class that mirrors basic TradeMe requirements (Title, Subtitle, CategoryId, Description, StartPrice, ReservePrice, BuyNowPrice, Condition).
*   Build a data-bound form in the UI for the selected draft.
*   Show thumbnails of the images attached to this draft at the top of the form.
*   Implement Auto-save: Any changes made in the text boxes automatically serialize to `listing.json` in the draft's folder.

**Milestone 1 check:** The user can drop photos into the inbox, click them to make a draft, manually type in the details, and close/reopen the app to see the data persist via `listing.json`.

---

## 🔌 Phase 2: Integration (TradeMe API)
**Objective:** Connect the local editor to the TradeMe Sandbox/Live API. Fetch metadata, upload photos, and successfully publish a manual listing.

**Step 1: Auth & RestSharp Setup**
*   Add the `RestSharp` NuGet package.
*   Create a `TradeMeApiService` class.
*   Configure RestSharp to use `OAuth1Authenticator` (Consumer Key, Consumer Secret, OAuth Token, OAuth Token Secret). *Note for AI: Point to `api.tmsandbox.co.nz` for development.*

**Step 2: Metadata & Categories**
*   Implement a method to fetch the TradeMe Category Tree (GET `/v1/Categories.json`).
*   Add a Category selector to the Phase 1 UI. Because the tree is massive, implement a simple text search (e.g., typing "sneaker" searches the cached local tree and returns the category ID).

**Step 3: The Publish Pipeline**
*   **Upload Photos:** Implement POST `/v1/Photos.json`. The app needs to iterate through the images in the draft folder, upload them, and collect the returned `PhotoId`s.
*   **Submit Listing:** Implement POST `/v1/Selling.json`. Map the `ListingModel` + the `PhotoId`s into the TradeMe JSON payload format and submit.

**Step 4: Post-Publish Workflow**
*   Upon a `200 OK` response from TradeMe, extract the new `ListingId`.
*   Update `listing.json` with the `ListingId` and the TradeMe Web URL.
*   Move the entire draft folder from `2_Drafts` to `3_Listed`.
*   Update the UI to reflect this state change.

**Milestone 2 check:** The user can manually fill out a draft, click "Publish", the app uploads photos, lists the item on TradeMe Sandbox, and safely archives the folder locally.

---

## 🧠 Phase 3: Intelligence (AI & Memory)
**Objective:** Replace manual data entry with AI Vision. Introduce the memory system so the AI gets smarter about pricing and formatting preferences over time.

**Step 1: AI Vision Integration**
*   Add an AI client integration (OpenAI GPT-4o or Anthropic Claude 3.5 Sonnet).
*   Update the "Create Draft" button logic:
    *   Compress/resize images in memory and convert to Base64.
    *   Send the images to the AI with a strict JSON schema prompt (System Prompt: *"You are an expert TradeMe seller. Examine these images and output a JSON object with Title, Description, estimated Category Name, Condition, and suggested Price."*).
    *   Save the AI's parsed response directly into the new `listing.json`.

**Step 2: Auto-Category Matching**
*   Take the AI's "Estimated Category Name" (e.g., "Mens Sneakers") and pass it into the local Category Search (built in Phase 2).
*   Auto-assign the closest matching TradeMe Category ID to the draft so the user doesn't have to search for it.

**Step 3: The Memory System**
*   Create a `preferences.json` file in the root app config folder.
*   Add a UI checkbox in the editor: "Save pricing/shipping rules for similar items".
*   When checked, append a rule to `preferences.json` (e.g., *"Keyword: Shoes -> Always add $10 shipping, Reserve is 80% of Buy Now"*).
*   Update the AI Prompt in Step 1: Inject the contents of `preferences.json` into the System Prompt before calling the Vision API, ensuring the AI adheres to historical rules.

**Milestone 3 check:** Drop 5 photos of a jacket into the app, click "Create Draft". In 10 seconds, the form is fully populated with an SEO-friendly title, formatted description, correct category, and preferred pricing template. The user makes one tiny manual edit, clicks "Publish", and it's live on TradeMe.

## Phase 1 notes

To ensure your Phase 1 UI and `listing.json` are perfectly aligned with what the TradeMe `Selling.json` API will eventually require in Phase 2, you need to map out the exact fields TradeMe expects for a **General Item**.

Even though Phase 1 is completely manual and offline, adding UI validation for these fields now (like character limits) will prevent your API calls from failing later. 

Here is the exact schema you should define for your `ListingModel.cs` in Phase 1, broken down by section. 

### 1. Core Details
These are the fundamental fields required to identify the item.
*   **`Title` (String):** **REQUIRED.** Maximum 50 characters. TradeMe is very strict on this limit.
*   **`Subtitle` (String):** Optional. Maximum 50 characters. *(Note: TradeMe usually charges a fee for subtitles, but it's good to have in the UI).*
*   **`CategoryId` (String):** **REQUIRED.** TradeMe category IDs are string paths separated by hyphens (e.g., `"0153-0447-0818-"`). For Phase 1, just make this a plain text box where you can manually paste an ID for testing.
*   **`Condition` (Integer/Enum):** **REQUIRED** for almost all categories. 
    *   `1` = Used
    *   `2` = New
*   **`Description` (String or List<string>):** **REQUIRED.** 
    *   *API Quirk:* The TradeMe API actually expects the description as an **array of strings** (each string is a paragraph). In your UI, make it a standard multi-line Textbox, but in your C# Model, you may want to split it by `\n` before serializing to JSON, or just store it as a single string locally and handle the splitting in Phase 2.

### 2. Pricing Configuration
TradeMe allows either "Auctions" or "Buy Now Only". Your UI needs to support these combinations.
*   **`IsBuyNowOnly` (Boolean):** If true, the item is not an auction.
*   **`StartPrice` (Decimal):** **REQUIRED** if it is an auction. The opening bid.
*   **`ReservePrice` (Decimal):** **REQUIRED** if it is an auction. Must be greater than or equal to the `StartPrice`. 
*   **`BuyNowPrice` (Decimal):** Optional. The instant-purchase price. 
*   **`Duration` (Integer):** **REQUIRED.** How long the listing runs. Valid TradeMe values are `2, 3, 4, 5, 6, 7, 10,` or `14` days. Make this a dropdown in your UI.

### 3. Shipping & Logistics
Shipping is notoriously the easiest place to get an API error. Standardize this now.
*   **`Pickup` (Integer/Enum):** **REQUIRED.** 
    *   `1` = Allow pickup
    *   `2` = Demand pickup (Pick up only)
    *   `3` = Forbid pickup
*   **`ShippingType` (Integer/Enum):** **REQUIRED.** 
    *   `1` = Custom/Specified (You will define options below)
    *   `2` = Free Shipping
    *   `3` = Undecided (To be arranged)
*   **`ShippingOptions` (List of Objects):** If `ShippingType == 1`, you need an array of shipping options. Each option needs:
    *   `Method` (String): e.g., "CourierPost Nationwide"
    *   `Price` (Decimal): e.g., `8.50`

### 4. Media
*   **`PhotoIds` (List<int>):** In Phase 1, this will be empty in the JSON. However, your local UI should display the file paths of the actual images sitting in the `Drafts/GUID/` folder. In Phase 2, the API will upload these files, get integer IDs back from TradeMe, and populate this array.

---

### How to prompt your AI Agent for this:
When you are ready to build the Data Model and UI for Phase 1, give your AI this prompt:

> "In `AutoAuction.Core`, create a new file called `ListingModel.cs`. This class will be serialized to `listing.json`. 
> 
> Based on TradeMe API requirements, it must include the following observable properties: `Title` (max 50 chars), `Subtitle`, `CategoryId` (string), `Condition` (enum: 1=Used, 2=New), `Description` (string), `IsBuyNowOnly` (bool), `StartPrice` (decimal), `ReservePrice` (decimal), `BuyNowPrice` (decimal), `DurationDays` (int, default 7), `PickupOption` (enum: 1=Allow, 2=Demand, 3=Forbid), `IsFreeShipping` (bool), and a `List<ShippingOption>` (a sub-class with `Method` string and `Price` decimal). 
> 
> Also, include a `List<string> LocalImagePaths` so the Avalonia UI knows which images to display from the draft folder. Use CommunityToolkit.Mvvm `[ObservableProperty]` attributes for all of these."