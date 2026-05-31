/* eslint-disable */
/**
 * TradeMe "General item" listing — repeatable automation steps.
 *
 * Discovered by walking the real flow over CDP (tools/cdp/eval.mjs) on
 * https://www.trademe.co.nz/a/marketplace/create-listing/{guid}/<step>.
 *
 * These functions are framework-agnostic DOM automation (no eval, no string
 * injection) so they work two ways:
 *   1. Dev/iteration: paste/run via tools/cdp/eval.mjs (CDP Runtime.evaluate).
 *   2. Production: ship inside the extension and inject with
 *      chrome.scripting.executeScript({ func: STEP, args: [listing] }) in the
 *      ISOLATED world — both avoid the page CSP 'unsafe-eval' block.
 *
 * Flow / routes (single SPA, one draft GUID):
 *   /title-category -> /item-details -> /photos -> /price-payment -> /delivery -> /promote
 * Final publish button text: "Start listing"  (NEVER auto-click without explicit user confirm).
 *
 * `listing` shape (subset of AutoAuction.Core ListingModel):
 *   { title, categoryPath:["Home & living","Lounge, dining & hall","Cabinets & bookshelves","Sideboards & buffets"],
 *     subtitle?, description, condition:"Used"|"New",
 *     sell:{ auction:bool, startPrice?, reservePrice?, buyNow:bool, buyNowPrice? },
 *     shipping:"free"|"calculate"|"specify"|"unknown",
 *     promote:"basic"|"gallery"|"feature"|"combo" }
 */

// ---------- helpers ----------
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function waitFor(fn, ms = 12000, poll = 150) {
    const start = Date.now();
    for (;;) {
        const v = fn();
        if (v) return v;
        if (Date.now() - start > ms) return null;
        await sleep(poll);
    }
}

// Angular ignores a plain `el.value = x`; use the native setter + input/change/blur.
function setInputValue(el, value) {
    if (!el) return false;
    const proto =
        el instanceof HTMLTextAreaElement
            ? HTMLTextAreaElement.prototype
            : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, String(value));
    el.dispatchEvent(new Event('input', {bubbles: true}));
    el.dispatchEvent(new Event('change', {bubbles: true}));
    el.dispatchEvent(new Event('blur', {bubbles: true}));
    return true;
}

const norm = s => (s || '').replace(/\s+/g, ' ').trim();
const byText = (selector, text) =>
    [...document.querySelectorAll(selector)].find(
        el => norm(el.textContent).toLowerCase() === text.toLowerCase()
    );
const byTextStarts = (selector, text) =>
    [...document.querySelectorAll(selector)].find(el =>
        norm(el.textContent).toLowerCase().startsWith(text.toLowerCase())
    );
const clickNext = () => byText('button', 'Next')?.click();
const onStep = re => () => (re.test(location.pathname) ? location.href : null);

// Select a custom radio/checkbox by the visible text of its associated <label for>.
function selectByLabel(inputName, labelMatch) {
    const radios = [...document.querySelectorAll(`input[name="${inputName}"]`)];
    const target = radios.find(r => {
        const lbl = document.querySelector(`label[for="${r.id}"]`);
        return new RegExp(labelMatch, 'i').test(norm(lbl?.innerText));
    });
    const lbl = target && document.querySelector(`label[for="${target.id}"]`);
    (lbl || target)?.click();
    return !!target;
}

// ---------- entry ----------
async function start_fromHome() {
    document.querySelector('a[href="/a/list"]')?.click();
    await waitFor(() => location.pathname.startsWith('/a/list'));
    const general =
        document.querySelector('a[href="/a/marketplace/list"]') ||
        byTextStarts('a.tm-list-vertical-picker__link', 'General item');
    general?.click();
    // Lands on /title-category for a fresh start, OR on the "Continue or start
    // again?" interstitial when in-progress drafts already exist.
    await waitFor(() => /create-listing/.test(location.pathname));
    await handleStartNewInterstitial();
    return await waitFor(onStep(/title-category/));
}

// If TradeMe shows "Continue or start again?" (because drafts already exist),
// click "Start new listing" to get a fresh draft. No-op if we're already in a draft.
async function handleStartNewInterstitial() {
    // A real draft URL has a GUID segment: /create-listing/<guid>/<step>.
    if (/\/create-listing\/[0-9a-f-]{8,}\//i.test(location.pathname)) return false;
    const btn = await waitFor(() => byText('button', 'Start new listing'), 5000);
    if (!btn) return false;
    btn.click();
    await waitFor(onStep(/title-category/));
    return true;
}

// ---------- step 1: Title & category ----------
async function step_titleCategory(listing) {
    await waitFor(() => document.querySelector('input[placeholder="Listing title"]'));
    setInputValue(document.querySelector('input[placeholder="Listing title"]'), listing.title);

    // Open the category modal and walk the tree by the provided path.
    document.querySelector('.koru-category-selector__choose-button')?.click();
    await waitFor(() => document.querySelector('tm-tree-picker .tm-tree-picker__children'));

    for (const name of listing.categoryPath) {
        const node = [
            ...document.querySelectorAll('tm-tree-picker .tm-tree-picker__children')
        ].find(b => norm(b.querySelector('tg-media-block-content')?.textContent) === name);
        if (!node) return {ok: false, error: `Category node not found: ${name}`};
        node.click();
        // Wait for either the next level to render or the modal to close (leaf selected).
        await waitFor(() => {
            const modalGone = !document.querySelector('.o-modal__dialog');
            const top = norm(
                document.querySelector(
                    'tm-tree-picker .tm-tree-picker__children tg-media-block-content'
                )?.textContent
            );
            return modalGone || (top && top !== name) ? true : null;
        });
    }
    await waitFor(() => !document.querySelector('.o-modal__dialog'));

    if (listing.subtitle) {
        const sub = [...document.querySelectorAll('tg-input-container,.o-input')].find(c =>
            /subtitle/i.test(c.querySelector('.o-input__label,label')?.innerText || '')
        );
        setInputValue(sub?.querySelector('input'), listing.subtitle);
    }

    clickNext();
    return {ok: !!(await waitFor(onStep(/item-details/))), url: location.href};
}

// ---------- step 2: Item details ----------
async function step_itemDetails(listing) {
    await waitFor(() => document.querySelector('textarea'));
    setInputValue(document.querySelector('textarea'), listing.description);
    // Condition radios: input[name="attribute:Condition"], chosen via <label for> text.
    selectByLabel('attribute:Condition', `^${listing.condition}$`);
    // (Category attributes such as "Colour" are optional here; values come from the
    //  category Attributes.json — see findings doc. Set them the same way if desired.)
    await sleep(300);
    clickNext();
    return {ok: !!(await waitFor(onStep(/photos/))), url: location.href};
}

// ---------- step 3: Photos ----------
// `files` is an array of File objects (build from the bridge's base64 image payloads).
async function step_photos(files) {
    const input = await waitFor(() =>
        document.querySelector('input[name="listing-progress-add-photo"]')
    );
    if (input && files && files.length) {
        const dt = new DataTransfer();
        files.forEach(f => dt.items.add(f));
        input.files = dt.files;
        input.dispatchEvent(new Event('change', {bubbles: true}));
        // Wait for thumbnails / upload to register before moving on.
        await sleep(1500);
    }
    clickNext();
    return {ok: !!(await waitFor(onStep(/price-payment/))), url: location.href};
}

// ---------- step 4: Price & payment ----------
async function step_pricePayment(listing) {
    await waitFor(() => byTextStarts('label', 'Run an auction'));
    const setCheckbox = (labelStart, want) => {
        const cb = byTextStarts('label', labelStart)?.querySelector('input[type=checkbox]');
        if (cb && cb.checked !== want) cb.click();
    };
    const s = listing.sell || {};
    setCheckbox('Run an auction', !!s.auction);
    setCheckbox('Set a Buy Now price', !!s.buyNow);
    await sleep(300);

    const priceInput = labelRe => {
        const c = [...document.querySelectorAll('tg-input-container,.o-input')].find(c =>
            labelRe.test(c.querySelector('.o-input__label,label')?.innerText || '')
        );
        return c?.querySelector('input');
    };
    if (s.auction) {
        setInputValue(priceInput(/start price/i), s.startPrice ?? 1);
        if (s.reservePrice != null) setInputValue(priceInput(/reserve price/i), s.reservePrice);
    }
    if (s.buyNow) setInputValue(priceInput(/buy now price/i), s.buyNowPrice ?? 0);

    await sleep(300);
    clickNext();
    return {ok: !!(await waitFor(onStep(/delivery/))), url: location.href};
}

// ---------- step 5: Shipping & pick-up ----------
async function step_delivery(listing) {
    const shippingLabel = {
        free: 'Free shipping within New Zealand',
        calculate: 'Calculate courier costs',
        specify: 'Specify shipping costs',
        unknown: 'I don’t know costs yet'
    }[listing.shipping || 'free'];
    await waitFor(() => byTextStarts('label,[role=radio],button,tg-radio', shippingLabel));
    byTextStarts('label,[role=radio],button,tg-radio', shippingLabel)?.click();
    // Location ("Where is the item?") is pre-filled from the account.
    // "Allow pick-up?" left at its default unless we add a mapping.
    await sleep(300);
    clickNext();
    return {ok: !!(await waitFor(onStep(/promote/))), url: location.href};
}

// ---------- step 6: Promote (then STOP — do not publish) ----------
async function step_promote(listing) {
    await waitFor(() => document.querySelector('input[name="selector:package"]'));
    const pkg = {
        combo: 'Feature Combo',
        feature: 'Feature ',
        gallery: 'Gallery',
        basic: 'Basic'
    }[listing.promote || 'gallery'];
    selectByLabel('selector:package', pkg);
    await sleep(300);
    // FINAL publish is byText('button','Start listing') — intentionally NOT clicked here.
    const ready = !!byText('button', 'Start listing');
    return {ok: ready, publishButtonPresent: ready, note: 'Stopped before publish.'};
}

// ---------- orchestrator (stops before publish) ----------
async function listItem(listing, files) {
    const log = [];
    const run = async (name, fn) => {
        const r = await fn();
        log.push({step: name, ...(r || {})});
        return r;
    };
    await run('start', () => start_fromHome());
    await run('titleCategory', () => step_titleCategory(listing));
    await run('itemDetails', () => step_itemDetails(listing));
    await run('photos', () => step_photos(files));
    await run('pricePayment', () => step_pricePayment(listing));
    await run('delivery', () => step_delivery(listing));
    await run('promote', () => step_promote(listing));
    return log;
}

// Export for CDP eval / module use.
if (typeof module !== 'undefined') {
    module.exports = {
        start_fromHome,
        handleStartNewInterstitial,
        step_titleCategory,
        step_itemDetails,
        step_photos,
        step_pricePayment,
        step_delivery,
        step_promote,
        listItem,
        helpers: {waitFor, setInputValue, selectByLabel, byText, byTextStarts}
    };
}
