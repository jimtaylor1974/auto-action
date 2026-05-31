// Shape of the listing served by the AutoAuction desktop bridge at
// GET /api/drafts/active. This mirrors AutoAuction.Core/Models/ListingModel.cs
// (only the fields the extension currently reads are typed here).
export interface ActiveListing {
    Id: string;
    Title: string;
    Subtitle: string;
    CategoryId: string;
    /** Category name segments (top-level first), added by the bridge to drive the picker. */
    CategoryPath: string[];
    Condition: number; // 1 = Used, 2 = New
    Description: string;
    Status: number; // 0 = Draft, 1 = Listed, 2 = Sold
    IsBuyNowOnly: boolean;
    StartPrice: number;
    ReservePrice: number;
    BuyNowPrice: number;
    DurationDays: number;
    PickupOption: number; // 1 = Allow, 2 = Demand, 3 = Forbid
    IsFreeShipping: boolean;
    LocalImagePaths: string[];
}

// One image from GET /api/drafts/active/images.
export interface ActiveImage {
    fileName: string;
    contentType: string;
    base64: string;
}

// Result of a bridge connection test.
export type BridgeTestResult =
    | {kind: 'connected'; listing: ActiveListing; imageCount: number}
    | {kind: 'no-draft'} // reached the server, but no draft is open
    | {kind: 'error'; message: string};

// Messages from the injected fill script / service worker → side panel.
export type FillMessage =
    | {source: 'aa-fill'; kind: 'progress'; line: string}
    | {source: 'aa-fill'; kind: 'ready'}
    | {source: 'aa-fill'; kind: 'listed'; listingId: string; listingUrl: string}
    | {source: 'aa-fill'; kind: 'error'; error: string};

// Side panel → service worker: arm/disarm the published-listing capture for a tab.
export interface ArmCaptureMessage {
    type: 'aa-arm-capture';
    tabId: number;
    bridgeUrl: string;
}
