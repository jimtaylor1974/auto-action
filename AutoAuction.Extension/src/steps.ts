// TradeMe "General item" listing steps, ported from tools/cdp/trademe-listing-steps.js and
// docs/trademe-listing-flow.md. Runs in the injected content script (isolated world). Each step
// advances the SPA and reports a human-readable line via `report`. Stops before "Start listing".

export interface FillData {
    title: string;
    subtitle?: string;
    categoryPath: string[]; // segment names, top-level first
    description: string;
    condition: 'Used' | 'New';
    isBuyNowOnly: boolean;
    startPrice: number;
    reservePrice: number;
    buyNowPrice: number;
    durationDays: number;
    pickupOption: 'Allow' | 'Demand' | 'Forbid';
    isFreeShipping: boolean;
    promote: 'gallery' | 'basic';
}

export interface FillImage {
    fileName: string;
    contentType: string;
    base64: string;
}

export type Report = (line: string) => void;

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));

export async function waitFor<T>(
    fn: () => T | null | undefined | false,
    ms = 12000,
    poll = 150
): Promise<T | null> {
    const start = Date.now();
    for (;;) {
        const v = fn();
        if (v) return v as T;
        if (Date.now() - start > ms) return null;
        await sleep(poll);
    }
}

const norm = (s: string | null | undefined) => (s || '').replace(/\s+/g, ' ').trim();

function setInputValue(el: Element | null | undefined, value: string): boolean {
    if (!el) return false;
    const proto =
        el instanceof HTMLTextAreaElement
            ? HTMLTextAreaElement.prototype
            : HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
    setter?.call(el, value);
    el.dispatchEvent(new Event('input', {bubbles: true}));
    el.dispatchEvent(new Event('change', {bubbles: true}));
    el.dispatchEvent(new Event('blur', {bubbles: true}));
    return true;
}

const byText = (selector: string, text: string): HTMLElement | undefined =>
    Array.from(document.querySelectorAll<HTMLElement>(selector)).find(
        el => norm(el.textContent).toLowerCase() === text.toLowerCase()
    );

const byTextStarts = (selector: string, text: string): HTMLElement | undefined =>
    Array.from(document.querySelectorAll<HTMLElement>(selector)).find(el =>
        norm(el.textContent).toLowerCase().startsWith(text.toLowerCase())
    );

const clickNext = () => byText('button', 'Next')?.click();

const onStep = (re: RegExp) => () => (re.test(location.pathname) ? location.href : null);

// Collect any validation / error text currently shown (helps diagnose a stuck step).
function collectValidation(): string {
    const msgs = Array.from(
        document.querySelectorAll<HTMLElement>(
            '.o-input__footer, [role=alert], [class*="error"], [class*="validation"]'
        )
    )
        .map(e => norm(e.innerText))
        .filter(Boolean);
    return Array.from(new Set(msgs)).slice(0, 3).join(' | ');
}

// Click "Next" and wait to land on the target step. TradeMe can be slow / rate-limited (429),
// so wait generously and retry the click once before giving up.
async function advance(targetRe: RegExp, what: string, report: Report): Promise<void> {
    clickNext();
    if (await waitFor(onStep(targetRe), 18000)) return;

    report(`Still on "${location.pathname.split('/').pop()}" — retrying Next…`);
    await sleep(1000);
    clickNext();
    if (await waitFor(onStep(targetRe), 18000)) return;

    const err = collectValidation();
    throw new Error(
        `Could not reach ${what} (still at ${location.pathname}${err ? `; page says: ${err}` : ''}).`
    );
}

// Select a custom radio/checkbox by the visible text of its associated <label for>.
function selectByLabel(inputName: string, labelMatch: RegExp): boolean {
    const radios = Array.from(
        document.querySelectorAll<HTMLInputElement>(`input[name="${inputName}"]`)
    );
    const target = radios.find(r => {
        const lbl = document.querySelector<HTMLElement>(`label[for="${r.id}"]`);
        return labelMatch.test(norm(lbl?.innerText));
    });
    const lbl = target && document.querySelector<HTMLElement>(`label[for="${target.id}"]`);
    (lbl || target)?.click();
    return !!target;
}

// If "Continue or start again?" shows (existing drafts), click "Start new listing".
export async function handleStartNewInterstitial(report: Report): Promise<void> {
    if (/\/create-listing\/[0-9a-f-]{8,}\//i.test(location.pathname)) return;
    const btn = await waitFor(() => byText('button', 'Start new listing'), 5000);
    if (btn) {
        report('Existing drafts found → Start new listing');
        btn.click();
        await waitFor(onStep(/title-category/));
    }
}

export async function step_titleCategory(data: FillData, report: Report): Promise<void> {
    await waitFor(() => document.querySelector('input[placeholder="Listing title"]'));
    setInputValue(document.querySelector('input[placeholder="Listing title"]'), data.title);
    report(`Title: ${data.title}`);

    document.querySelector<HTMLElement>('.koru-category-selector__choose-button')?.click();
    await waitFor(() => document.querySelector('tm-tree-picker .tm-tree-picker__children'));

    for (const name of data.categoryPath) {
        const node = Array.from(
            document.querySelectorAll<HTMLElement>('tm-tree-picker .tm-tree-picker__children')
        ).find(b => norm(b.querySelector('tg-media-block-content')?.textContent) === name);
        if (!node) throw new Error(`Category step not found: "${name}"`);
        report(`Category › ${name}`);
        node.click();
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

    // If the modal is still open, the last path segment wasn't a final category in TradeMe's tree.
    if (document.querySelector('.o-modal__dialog')) {
        const kids = Array.from(
            document.querySelectorAll<HTMLElement>(
                'tm-tree-picker .tm-tree-picker__children tg-media-block-content'
            )
        )
            .map(e => norm(e.textContent))
            .slice(0, 12);
        const last = data.categoryPath[data.categoryPath.length - 1];
        throw new Error(
            `Category "${last}" isn't final on TradeMe; it still offers: ${kids.join(', ') || '(none)'}`
        );
    }

    if (data.subtitle) {
        const sub = Array.from(
            document.querySelectorAll<HTMLElement>('tg-input-container,.o-input')
        ).find(c => /subtitle/i.test(c.querySelector('.o-input__label,label')?.textContent || ''));
        setInputValue(sub?.querySelector('input'), data.subtitle);
    }

    // Let the category save commit before advancing — the "Choose category" button is replaced
    // once a category is set. Clicking Next too soon makes TradeMe bounce back to the draft picker.
    await waitFor(() => !document.querySelector('.koru-category-selector__choose-button'), 8000);
    await sleep(1000);

    await advance(/item-details/, 'Item details', report);
}

export async function step_itemDetails(data: FillData, report: Report): Promise<void> {
    await waitFor(() => document.querySelector('textarea'));
    setInputValue(document.querySelector('textarea'), data.description);
    selectByLabel('attribute:Condition', new RegExp(`^${data.condition}$`, 'i'));
    report(`Description + Condition: ${data.condition}`);
    await sleep(300);
    await advance(/photos/, 'Photos', report);
}

/** Attaches files in the page's MAIN world (the content-script world can't set input.files). */
export type AttachPhotos = () => Promise<{ok: boolean; filesLength?: number; reason?: string} | undefined>;

export interface PriceFill {
    auction: boolean;
    buyNow: boolean;
    startPrice: number;
    reservePrice: number;
    buyNowPrice: number;
}

/** Sets the currency-masked price inputs in the MAIN world (isolated-world value sets don't stick). */
export type SetPrices = (
    prices: PriceFill
) => Promise<{ok: boolean; start?: string; buyNow?: string} | undefined>;

export async function step_photos(
    images: FillImage[],
    report: Report,
    attachPhotos: AttachPhotos
): Promise<void> {
    const input = await waitFor(
        () =>
            document.querySelector<HTMLInputElement>('input[name="listing-progress-add-photo"]') ??
            document.querySelector<HTMLInputElement>('input[type=file]'),
        20000
    );

    if (!input) throw new Error('Photo upload field not found on the Photos step.');
    if (!images.length) throw new Error('No images were provided for this listing.');

    const cardCount = () => document.querySelectorAll('tm-sdui-photo-card').length;

    // Delegate the actual DataTransfer to the MAIN world (assigning input.files from the isolated
    // content-script world is silently dropped — verified: input.files.length stayed 0). Retry a
    // few times: the attach/card render can race, especially when TradeMe is throttling (429).
    const MAX_ATTEMPTS = 3;
    let cards = 0;
    for (let attempt = 1; attempt <= MAX_ATTEMPTS && cards === 0; attempt++) {
        await sleep(attempt === 1 ? 600 : 1500);
        const res = await attachPhotos();
        if (!res?.ok) {
            report(`attempt ${attempt}: attach failed${res?.reason ? ` (${res.reason})` : ''}`);
            continue;
        }
        report(`attempt ${attempt}: input.files.length=${res.filesLength ?? '?'}, waiting for card…`);
        await waitFor(() => cardCount() > 0, 12000);
        cards = cardCount();
    }

    const rejected = document.querySelectorAll('.tm-sdui-photo-card__error').length;
    const errText = Array.from(
        document.querySelectorAll<HTMLElement>('.tm-sdui-photo-card__error, [role=alert]')
    )
        .map(e => norm(e.innerText))
        .filter(Boolean)
        .slice(0, 3)
        .join(' | ');
    report(`photo cards=${cards} rejected=${rejected}${errText ? ` :: ${errText}` : ''}`);

    if (cards === 0)
        throw new Error('Photo did not attach (0 cards) after retries. See the diagnostics above.');
    if (rejected >= cards)
        throw new Error(`TradeMe rejected the photo${errText ? `: ${errText}` : ''}.`);

    // Let any in-flight upload settle before advancing.
    await sleep(2000);
    await advance(/price-payment/, 'Price & payment', report);
}

export async function step_pricePayment(
    data: FillData,
    report: Report,
    setPrices: SetPrices
): Promise<void> {
    await waitFor(() => byTextStarts('label', 'Run an auction'));

    // The checkbox input is referenced by the label's for= (not nested). Clicking the label toggles it.
    const setCheckbox = (labelStart: string, want: boolean) => {
        const label = byTextStarts('label', labelStart);
        const forId = label?.getAttribute('for');
        const cb = forId ? (document.getElementById(forId) as HTMLInputElement | null) : null;
        if (label && cb && cb.checked !== want) label.click();
    };

    const auction = !data.isBuyNowOnly;
    const buyNow = data.isBuyNowOnly || data.buyNowPrice > 0;
    setCheckbox('Run an auction', auction);
    setCheckbox('Set a Buy Now price', buyNow);

    const priceInput = (labelRe: RegExp) => {
        const c = Array.from(
            document.querySelectorAll<HTMLElement>('tg-input-container,.o-input')
        ).find(c => labelRe.test(c.querySelector('.o-input__label')?.textContent || ''));
        return c?.querySelector<HTMLInputElement>('input');
    };

    // The price inputs render asynchronously after the checkboxes are ticked — wait for them.
    if (auction) await waitFor(() => priceInput(/start price/i), 8000);
    if (buyNow) await waitFor(() => priceInput(/buy now price/i), 8000);

    // Set the currency-masked inputs in the MAIN world (isolated-world value sets don't stick).
    const res = await setPrices({
        auction,
        buyNow,
        startPrice: data.startPrice || 1,
        reservePrice: data.reservePrice,
        buyNowPrice: data.buyNowPrice
    });
    report(`prices set: start="${res?.start ?? ''}" buyNow="${res?.buyNow ?? ''}"`);

    await sleep(400);
    await advance(/delivery/, 'Shipping', report);
}

export async function step_delivery(data: FillData, report: Report): Promise<void> {
    // Shipping methods are radios referenced by label for=. Clicking the label selects them.
    // Avoid "Specify shipping costs" (needs option rows we don't fill) → use a safe method.
    const wanted = data.isFreeShipping ? 'free shipping within new zealand' : 'i don';
    await waitFor(() => byTextStarts('label', wanted));
    const optLabel = byTextStarts('label', wanted);
    if (!optLabel) throw new Error('Shipping method option not found.');
    const forId = optLabel.getAttribute('for');
    const radio = forId ? (document.getElementById(forId) as HTMLInputElement | null) : null;
    optLabel.click();
    const ok = await waitFor(() => (radio ? radio.checked : false), 4000);
    report(`Shipping: ${data.isFreeShipping ? 'free' : "don't know costs yet"} (selected=${!!ok})`);
    if (!ok) throw new Error('Could not select a shipping method.');
    await sleep(400);
    await advance(/promote/, 'Promote', report);
}

export async function step_promote(data: FillData, report: Report): Promise<void> {
    await waitFor(() => document.querySelector('input[name="selector:package"]'));
    const pkg = data.promote === 'basic' ? 'Basic' : 'Gallery';
    selectByLabel('selector:package', new RegExp(pkg, 'i'));
    report(`Promote: ${pkg}`);
    await sleep(300);
    // STOP — the final "Start listing" button is intentionally left for the user to click.
    if (!byText('button', 'Start listing')) throw new Error('Review/Start-listing button not found.');
}
