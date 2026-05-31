import {ActiveImage, ActiveListing, BridgeTestResult} from './types';

// The AutoAuction desktop app runs the bridge server on this URL by default
// (configurable via Settings -> Local Bridge Server in the desktop app).
export const DEFAULT_BRIDGE_URL = 'http://localhost:5999';

const BRIDGE_URL_STORAGE_KEY = 'AutoAuctionExtension.bridgeUrl';

/** The active draft + its images, fetched in the extension context (the page can't reach localhost). */
export interface ActivePayload {
    listing: ActiveListing;
    images: ActiveImage[];
}

export async function fetchActivePayload(bridgeUrl: string): Promise<ActivePayload> {
    const listingResp = await fetch(`${bridgeUrl}/api/drafts/active`, {cache: 'no-store'});
    if (!listingResp.ok) throw new Error(`No active draft (HTTP ${listingResp.status}).`);
    const listing = (await listingResp.json()) as ActiveListing;

    const imagesResp = await fetch(`${bridgeUrl}/api/drafts/active/images`, {cache: 'no-store'});
    const images = imagesResp.ok ? ((await imagesResp.json()) as ActiveImage[]) : [];

    return {listing, images};
}

export interface SanitizeUrlResult {
    url?: string;
    error?: string;
}

// Normalises user input to an origin (no path/query) so "http://localhost:5999/foo"
// is stored as "http://localhost:5999".
export const sanitizeBridgeUrl = (value: string): SanitizeUrlResult => {
    const trimmedValue = value.trim();

    if (!trimmedValue) {
        return {error: 'Enter a URL'};
    }

    try {
        const parsedUrl = new URL(trimmedValue);

        if (parsedUrl.protocol !== 'http:' && parsedUrl.protocol !== 'https:') {
            return {error: 'Use an http or https URL'};
        }

        if (!parsedUrl.hostname) {
            return {error: 'Enter a URL with a host'};
        }

        return {url: parsedUrl.origin};
    } catch {
        return {error: 'Enter a valid absolute URL'};
    }
};

export const readStoredBridgeUrl = async (): Promise<string> =>
    new Promise(resolve => {
        chrome.storage.local.get(BRIDGE_URL_STORAGE_KEY, result => {
            if (chrome.runtime.lastError) {
                resolve(DEFAULT_BRIDGE_URL);
                return;
            }

            const stored = result[BRIDGE_URL_STORAGE_KEY];
            const sanitized =
                typeof stored === 'string' ? sanitizeBridgeUrl(stored) : {};

            resolve(sanitized.url ?? DEFAULT_BRIDGE_URL);
        });
    });

export const storeBridgeUrl = async (url: string): Promise<string> => {
    const sanitized = sanitizeBridgeUrl(url);

    if (!sanitized.url) {
        throw new Error(sanitized.error ?? 'Invalid bridge URL');
    }

    await new Promise<void>((resolve, reject) => {
        chrome.storage.local.set(
            {[BRIDGE_URL_STORAGE_KEY]: sanitized.url},
            () => {
                const error = chrome.runtime.lastError;
                if (error) {
                    reject(new Error(error.message));
                    return;
                }
                resolve();
            }
        );
    });

    return sanitized.url;
};

export const errorToString = (error: unknown): string =>
    error instanceof Error ? `${error.name}: ${error.message}` : String(error);

// Hits GET /api/drafts/active on the desktop bridge. A 404 means we reached the
// server but no draft is currently open in AutoAuction - that still proves the
// connection works, so it's surfaced as a distinct "no-draft" outcome.
export const testBridgeConnection = async (
    bridgeUrl: string
): Promise<BridgeTestResult> => {
    let response: Response;
    try {
        response = await fetch(`${bridgeUrl}/api/drafts/active`, {
            method: 'GET',
            cache: 'no-store'
        });
    } catch (error) {
        return {
            kind: 'error',
            message: `${errorToString(error)} - is the AutoAuction desktop app running with the bridge server started?`
        };
    }

    if (response.status === 404) {
        return {kind: 'no-draft'};
    }

    if (!response.ok) {
        return {kind: 'error', message: `Server responded ${response.status}`};
    }

    let listing: ActiveListing;
    try {
        listing = (await response.json()) as ActiveListing;
    } catch (error) {
        return {kind: 'error', message: `Bad JSON: ${errorToString(error)}`};
    }

    // Best-effort image count; failure here shouldn't fail the connection test.
    let imageCount = listing.LocalImagePaths?.length ?? 0;
    try {
        const imagesResponse = await fetch(
            `${bridgeUrl}/api/drafts/active/images`,
            {method: 'GET', cache: 'no-store'}
        );
        if (imagesResponse.ok) {
            const images = (await imagesResponse.json()) as ActiveImage[];
            imageCount = images.length;
        }
    } catch {
        // keep the LocalImagePaths-based count
    }

    return {kind: 'connected', listing, imageCount};
};
