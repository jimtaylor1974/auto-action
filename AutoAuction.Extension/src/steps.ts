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

    if (data.subtitle) {
        const sub = Array.from(
            document.querySelectorAll<HTMLElement>('tg-input-container,.o-input')
        ).find(c => /subtitle/i.test(c.querySelector('.o-input__label,label')?.textContent || ''));
        setInputValue(sub?.querySelector('input'), data.subtitle);
    }

    clickNext();
    if (!(await waitFor(onStep(/item-details/)))) throw new Error('Could not reach Item details.');
}

export async function step_itemDetails(data: FillData, report: Report): Promise<void> {
    await waitFor(() => document.querySelector('textarea'));
    setInputValue(document.querySelector('textarea'), data.description);
    selectByLabel('attribute:Condition', new RegExp(`^${data.condition}$`, 'i'));
    report(`Description + Condition: ${data.condition}`);
    await sleep(300);
    clickNext();
    if (!(await waitFor(onStep(/photos/)))) throw new Error('Could not reach Photos.');
}

export async function step_photos(images: FillImage[], report: Report): Promise<void> {
    const input = await waitFor(() =>
        document.querySelector<HTMLInputElement>('input[name="listing-progress-add-photo"]')
    );
    if (input && images.length) {
        const dt = new DataTransfer();
        for (const img of images) {
            const bytes = Uint8Array.from(atob(img.base64), c => c.charCodeAt(0));
            dt.items.add(new File([bytes], img.fileName, {type: img.contentType}));
        }
        input.files = dt.files;
        input.dispatchEvent(new Event('change', {bubbles: true}));
        report(`Uploading ${images.length} photo(s)…`);
        await sleep(2500);
    }
    clickNext();
    if (!(await waitFor(onStep(/price-payment/)))) throw new Error('Could not reach Price & payment.');
}

export async function step_pricePayment(data: FillData, report: Report): Promise<void> {
    await waitFor(() => byTextStarts('label', 'Run an auction'));

    const setCheckbox = (labelStart: string, want: boolean) => {
        const cb = byTextStarts('label', labelStart)?.querySelector<HTMLInputElement>(
            'input[type=checkbox]'
        );
        if (cb && cb.checked !== want) cb.click();
    };

    const auction = !data.isBuyNowOnly;
    const buyNow = data.isBuyNowOnly || data.buyNowPrice > 0;
    setCheckbox('Run an auction', auction);
    setCheckbox('Set a Buy Now price', buyNow);
    await sleep(300);

    const priceInput = (labelRe: RegExp) => {
        const c = Array.from(
            document.querySelectorAll<HTMLElement>('tg-input-container,.o-input')
        ).find(c => labelRe.test(c.querySelector('.o-input__label,label')?.textContent || ''));
        return c?.querySelector<HTMLInputElement>('input');
    };

    if (auction) {
        setInputValue(priceInput(/start price/i), String(data.startPrice || 1));
        if (data.reservePrice > 0) setInputValue(priceInput(/reserve price/i), String(data.reservePrice));
    }
    if (buyNow) setInputValue(priceInput(/buy now price/i), String(data.buyNowPrice || 0));
    report(auction ? `Auction · start $${data.startPrice}` : `Buy Now $${data.buyNowPrice}`);

    await sleep(300);
    clickNext();
    if (!(await waitFor(onStep(/delivery/)))) throw new Error('Could not reach Shipping.');
}

export async function step_delivery(data: FillData, report: Report): Promise<void> {
    const label = data.isFreeShipping ? 'free shipping within new zealand' : 'i don';
    await waitFor(() => byTextStarts('label,[role=radio],button,tg-radio', label));
    byTextStarts('label,[role=radio],button,tg-radio', label)?.click();
    report(data.isFreeShipping ? 'Shipping: free' : 'Shipping: costs unknown');
    await sleep(300);
    clickNext();
    if (!(await waitFor(onStep(/promote/)))) throw new Error('Could not reach Promote.');
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
