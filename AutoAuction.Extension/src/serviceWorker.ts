import {ArmCaptureMessage} from './types';

// Open the side panel when the user clicks the extension's action button.
chrome.runtime.onInstalled.addListener(() => {
    chrome.sidePanel
        .setPanelBehavior({openPanelOnActionClick: true})
        .catch(error => console.error('Unable to set side panel behavior', error));
});

// --- Published-listing capture --------------------------------------------------------------
// Armed by the side panel after the form is filled. When the TradeMe tab navigates away from the
// create-listing flow (i.e. the user clicked "Start listing"), we read the resulting listing
// id/URL and POST it back to the bridge, which marks the draft Listed. Doing this in the service
// worker survives the publish navigation/reload that would kill an in-page watcher.

let armed: {tabId: number; bridgeUrl: string} | null = null;
let armTimer: ReturnType<typeof setTimeout> | undefined;

function disarm() {
    armed = null;
    if (armTimer) clearTimeout(armTimer);
    armTimer = undefined;
}

chrome.runtime.onMessage.addListener((message: ArmCaptureMessage) => {
    if (message?.type === 'aa-arm-capture') {
        armed = {tabId: message.tabId, bridgeUrl: message.bridgeUrl};
        if (armTimer) clearTimeout(armTimer);
        armTimer = setTimeout(disarm, 10 * 60 * 1000); // auto-disarm after 10 minutes
    }
});

const progress = (line: string) =>
    chrome.runtime.sendMessage({source: 'aa-fill', kind: 'progress', line});

async function captureFromUrl(url: string, bridgeUrl: string) {
    const listingId = url.match(/(\d{6,})/)?.[1] ?? '';
    const endpoint = `${bridgeUrl}/api/drafts/active/listed`;
    progress(`Published → ${url}`);
    progress(`POST ${endpoint} (listingId=${listingId || '?'}) …`);
    try {
        const resp = await fetch(endpoint, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({listingId, listingUrl: url})
        });
        if (!resp.ok) {
            chrome.runtime.sendMessage({
                source: 'aa-fill',
                kind: 'error',
                error: `POST ${endpoint} returned HTTP ${resp.status}: ${await resp.text()}`
            });
            return;
        }
        chrome.runtime.sendMessage({source: 'aa-fill', kind: 'listed', listingId, listingUrl: url});
    } catch (e) {
        chrome.runtime.sendMessage({
            source: 'aa-fill',
            kind: 'error',
            error: `POST ${endpoint} failed: ${e instanceof Error ? e.message : String(e)}`
        });
    }
}

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
    if (!armed || tabId !== armed.tabId) return;

    const url = changeInfo.url ?? (changeInfo.status === 'complete' ? tab.url : undefined);
    if (!url) return;

    // Still inside the create-listing flow → not published yet.
    if (url.includes('/create-listing') || !url.includes('trademe.co.nz')) return;

    const bridgeUrl = armed.bridgeUrl;
    disarm();
    void captureFromUrl(url, bridgeUrl);
});

chrome.tabs.onRemoved.addListener(tabId => {
    if (armed && armed.tabId === tabId) disarm();
});
