// Injected into the TradeMe tab via chrome.scripting.executeScript({files:['trademeFill.js']}).
// Fetches the active draft + images from the local bridge, fills the listing flow, and stops at the
// review (the user clicks "Start listing"). Capture of the published id/URL is armed by the side
// panel + handled in the service worker (survives the publish navigation/reload).

import {
    FillData,
    FillImage,
    handleStartNewInterstitial,
    step_delivery,
    step_itemDetails,
    step_photos,
    step_pricePayment,
    step_promote,
    step_titleCategory,
    waitFor
} from './steps';
import {ActiveListing} from './types';

// The side panel fetches the draft (the TradeMe page can't reach localhost) and stashes it here.
const PAYLOAD_STORAGE_KEY = 'aa-fill-payload';

const send = (msg: Record<string, unknown>) =>
    chrome.runtime.sendMessage({source: 'aa-fill', ...msg});
const report = (line: string) => send({kind: 'progress', line});

interface Payload {
    listing: ActiveListing;
    images: FillImage[];
}

function readPayload(): Promise<Payload | undefined> {
    return new Promise(resolve => {
        chrome.storage.local.get(PAYLOAD_STORAGE_KEY, result => resolve(result?.[PAYLOAD_STORAGE_KEY]));
    });
}

function toFillData(l: ActiveListing): FillData {
    return {
        title: l.Title,
        subtitle: l.Subtitle || undefined,
        categoryPath: l.CategoryPath || [],
        description: l.Description,
        condition: l.Condition === 2 ? 'New' : 'Used',
        isBuyNowOnly: l.IsBuyNowOnly,
        startPrice: l.StartPrice,
        reservePrice: l.ReservePrice,
        buyNowPrice: l.BuyNowPrice,
        durationDays: l.DurationDays,
        pickupOption: l.PickupOption === 2 ? 'Demand' : l.PickupOption === 3 ? 'Forbid' : 'Allow',
        isFreeShipping: l.IsFreeShipping,
        promote: 'gallery'
    };
}

(async () => {
    try {
        report('Reading the active draft…');
        const payload = await readPayload();
        if (!payload?.listing) {
            send({kind: 'error', error: 'No draft payload found — re-run “Fill TradeMe listing”.'});
            return;
        }
        void chrome.storage.local.remove(PAYLOAD_STORAGE_KEY);

        const listing = payload.listing;
        const images = payload.images ?? [];
        const data = toFillData(listing);
        if (!data.categoryPath.length) {
            send({kind: 'error', error: 'No category set on the draft — choose a category first.'});
            return;
        }

        await handleStartNewInterstitial(report);
        if (!(await waitFor(() => /title-category/.test(location.pathname)))) {
            send({kind: 'error', error: 'Could not reach the listing form (title & category).'});
            return;
        }

        await step_titleCategory(data, report);
        await step_itemDetails(data, report);
        // The side panel performs the file attach in the MAIN world and replies with the result.
        await step_photos(images, report, () =>
            chrome.runtime.sendMessage({source: 'aa-fill', kind: 'inject-photos'})
        );
        await step_pricePayment(data, report);
        await step_delivery(data, report);
        await step_promote(data, report);

        send({kind: 'ready'});
    } catch (e) {
        send({kind: 'error', error: e instanceof Error ? e.message : String(e)});
    }
})();
