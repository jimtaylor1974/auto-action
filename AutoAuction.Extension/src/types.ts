// Shape of the listing served by the AutoAuction desktop bridge at
// GET /api/drafts/active. This mirrors AutoAuction.Core/Models/ListingModel.cs
// (only the fields the extension currently reads are typed here).
export interface ActiveListing {
    Id: string;
    Title: string;
    Subtitle: string;
    CategoryId: string;
    Description: string;
    Status: number; // 0 = Draft, 1 = Listed, 2 = Sold
    IsBuyNowOnly: boolean;
    StartPrice: number;
    ReservePrice: number;
    BuyNowPrice: number;
    DurationDays: number;
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
