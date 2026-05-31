import React, {useCallback, useEffect, useMemo, useRef, useState} from 'react';
import {createRoot} from 'react-dom/client';
import 'bootstrap/dist/css/bootstrap.min.css';
import {Alert, Badge, Button, FormGroup, Input, Label} from 'reactstrap';
import {
    DEFAULT_BRIDGE_URL,
    fetchActivePayload,
    readStoredBridgeUrl,
    sanitizeBridgeUrl,
    storeBridgeUrl,
    testBridgeConnection
} from './bridge';
import {ActiveImage, BridgeTestResult, FillMessage} from './types';

const STATUS_LABELS = ['Draft', 'Listed', 'Sold'];
const CREATE_LISTING_URL = 'https://www.trademe.co.nz/a/marketplace/create-listing';

// Runs in the page's MAIN world (injected with world:'MAIN'), where assigning input.files and
// building File objects works — unlike the isolated content-script world. Must be self-contained.
function mainWorldAttachPhotos(images: {fileName: string; contentType: string; base64: string}[]) {
    const input = (document.querySelector('input[name="listing-progress-add-photo"]') ||
        document.querySelector('input[type=file]')) as HTMLInputElement | null;
    if (!input) return {ok: false, reason: 'no file input'};
    try {
        const dt = new DataTransfer();
        for (const img of images) {
            const bytes = Uint8Array.from(atob(img.base64), c => c.charCodeAt(0));
            dt.items.add(new File([bytes], img.fileName, {type: img.contentType || 'image/jpeg'}));
        }
        input.files = dt.files;
        input.dispatchEvent(new Event('input', {bubbles: true}));
        input.dispatchEvent(new Event('change', {bubbles: true}));
        return {ok: true, filesLength: input.files.length};
    } catch (e) {
        return {ok: false, reason: e instanceof Error ? e.message : String(e)};
    }
}

interface TestLogEntry {
    id: string;
    at: string;
    bridgeUrl: string;
    result: BridgeTestResult;
}

type FillState = 'idle' | 'filling' | 'ready' | 'listed' | 'error';

const waitForTabComplete = (tabId: number) =>
    new Promise<void>(resolve => {
        const listener = (id: number, info: chrome.tabs.TabChangeInfo) => {
            if (id === tabId && info.status === 'complete') {
                chrome.tabs.onUpdated.removeListener(listener);
                resolve();
            }
        };
        chrome.tabs.onUpdated.addListener(listener);
    });

async function findOrCreateTradeMeTab(): Promise<number> {
    const tabs = await chrome.tabs.query({url: '*://*.trademe.co.nz/*'});
    if (tabs[0]?.id != null) {
        await chrome.tabs.update(tabs[0].id, {active: true});
        return tabs[0].id;
    }
    const created = await chrome.tabs.create({url: CREATE_LISTING_URL});
    return created.id!;
}

const App: React.FC = () => {
    const [bridgeUrlInput, setBridgeUrlInput] = useState(DEFAULT_BRIDGE_URL);
    const [isTesting, setIsTesting] = useState(false);
    const [log, setLog] = useState<TestLogEntry[]>([]);

    const [fillState, setFillState] = useState<FillState>('idle');
    const [fillLog, setFillLog] = useState<string[]>([]);
    const [fillError, setFillError] = useState('');
    const [listedUrl, setListedUrl] = useState('');

    const tabIdRef = useRef<number | null>(null);
    const bridgeUrlRef = useRef(DEFAULT_BRIDGE_URL);
    const imagesRef = useRef<ActiveImage[]>([]);

    const sanitized = useMemo(() => sanitizeBridgeUrl(bridgeUrlInput), [bridgeUrlInput]);

    useEffect(() => {
        let mounted = true;
        readStoredBridgeUrl()
            .then(url => mounted && setBridgeUrlInput(url))
            .catch(() => undefined);
        return () => {
            mounted = false;
        };
    }, []);

    // Listen for progress/ready/listed/error + the MAIN-world photo-attach request.
    useEffect(() => {
        const onMessage = (
            message: FillMessage | {source: 'aa-fill'; kind: 'inject-photos'},
            _sender: chrome.runtime.MessageSender,
            sendResponse: (r: unknown) => void
        ): boolean | void => {
            if (message?.source !== 'aa-fill') return;

            // Content script asks us to attach the photos in the page's MAIN world.
            if (message.kind === 'inject-photos') {
                const tabId = tabIdRef.current;
                if (tabId == null) {
                    sendResponse({ok: false, reason: 'no tab'});
                    return true;
                }
                chrome.scripting
                    .executeScript({
                        target: {tabId},
                        world: 'MAIN',
                        func: mainWorldAttachPhotos,
                        args: [imagesRef.current]
                    })
                    .then(res => sendResponse(res?.[0]?.result ?? {ok: false, reason: 'no result'}))
                    .catch(e =>
                        sendResponse({ok: false, reason: e instanceof Error ? e.message : String(e)})
                    );
                return true; // keep the message channel open for the async response
            }

            const stamp = new Date().toLocaleTimeString();
            switch (message.kind) {
                case 'progress':
                    setFillLog(lines => [`${stamp}  ${message.line}`, ...lines]);
                    break;
                case 'ready':
                    setFillState('ready');
                    setFillLog(lines => [`${stamp}  Form filled — review and click “Start listing”.`, ...lines]);
                    if (tabIdRef.current != null) {
                        chrome.runtime.sendMessage({
                            type: 'aa-arm-capture',
                            tabId: tabIdRef.current,
                            bridgeUrl: bridgeUrlRef.current
                        });
                    }
                    break;
                case 'listed':
                    setFillState('listed');
                    setListedUrl(message.listingUrl);
                    setFillLog(lines => [`${stamp}  Listed ✓ ${message.listingUrl}`, ...lines]);
                    break;
                case 'error':
                    setFillState('error');
                    setFillError(message.error);
                    setFillLog(lines => [`${stamp}  ERROR: ${message.error}`, ...lines]);
                    break;
            }
        };
        chrome.runtime.onMessage.addListener(onMessage);
        return () => chrome.runtime.onMessage.removeListener(onMessage);
    }, []);

    const runTest = useCallback(async () => {
        if (!sanitized.url) return;
        setIsTesting(true);
        try {
            const url = await storeBridgeUrl(sanitized.url).catch(() => sanitized.url!);
            const result = await testBridgeConnection(url);
            setLog(entries =>
                [
                    {
                        id: `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`,
                        at: new Date().toLocaleTimeString(),
                        bridgeUrl: url,
                        result
                    },
                    ...entries
                ].slice(0, 10)
            );
        } finally {
            setIsTesting(false);
        }
    }, [sanitized.url]);

    const runFill = useCallback(async () => {
        if (!sanitized.url) return;
        setFillState('filling');
        setFillError('');
        setListedUrl('');
        setFillLog([`${new Date().toLocaleTimeString()}  Opening the TradeMe tab…`]);
        const stamp = () => new Date().toLocaleTimeString();
        const note = (line: string) => setFillLog(lines => [`${stamp()}  ${line}`, ...lines]);
        try {
            const bridgeUrl = await storeBridgeUrl(sanitized.url).catch(() => sanitized.url!);
            bridgeUrlRef.current = bridgeUrl;

            // Fetch the draft here (the TradeMe page can't reach localhost) and stash it for the
            // injected script to read from chrome.storage.
            note(`GET ${bridgeUrl}/api/drafts/active …`);
            const payload = await fetchActivePayload(bridgeUrl);
            note(`Draft "${payload.listing.Title}" + ${payload.images.length} image(s) loaded.`);
            imagesRef.current = payload.images;
            await chrome.storage.local.set({'aa-fill-payload': payload});

            const tabId = await findOrCreateTradeMeTab();
            tabIdRef.current = tabId;

            note(`Navigating tab ${tabId} to the create-listing page…`);
            const loaded = waitForTabComplete(tabId);
            await chrome.tabs.update(tabId, {url: CREATE_LISTING_URL});
            await loaded;

            note('Injecting the fill script…');
            await chrome.scripting.executeScript({target: {tabId}, files: ['trademeFill.js']});
            // Progress + completion now arrive via runtime messages.
        } catch (e) {
            setFillState('error');
            const msg = e instanceof Error ? e.message : String(e);
            setFillError(msg);
            note(`ERROR: ${msg}`);
        }
    }, [sanitized.url]);

    const latest = log[0]?.result;
    const connected = latest?.kind === 'connected';
    const isFilling = fillState === 'filling';

    return (
        <div className="d-flex flex-column p-3" style={{minWidth: 320, height: '100vh', overflow: 'auto'}}>
            <div className="d-flex align-items-center gap-2 mb-1">
                <h6 className="mb-0">AutoAuction Bridge</h6>
            </div>
            <p className="text-muted small">
                Fills a TradeMe listing from the active draft in the AutoAuction desktop app.
            </p>

            <FormGroup>
                <Label for="bridge-url" className="small fw-semibold">
                    Bridge server URL
                </Label>
                <Input
                    id="bridge-url"
                    type="url"
                    value={bridgeUrlInput}
                    placeholder={DEFAULT_BRIDGE_URL}
                    onChange={event => setBridgeUrlInput(event.target.value)}
                    onKeyDown={event => {
                        if (event.key === 'Enter' && sanitized.url) void runTest();
                    }}
                />
                <div className={`small mt-1 text-break ${sanitized.url ? 'text-muted' : 'text-danger'}`}>
                    {sanitized.url ?? sanitized.error ?? 'Enter a valid URL'}
                </div>
            </FormGroup>

            <div className="d-flex gap-2">
                <Button color="secondary" outline onClick={() => runTest()} disabled={!sanitized.url || isTesting}>
                    {isTesting ? 'Testing…' : 'Test connection'}
                </Button>
                <Button color="primary" onClick={() => runFill()} disabled={!connected || isFilling}>
                    {isFilling ? 'Filling…' : 'Fill TradeMe listing'}
                </Button>
            </div>

            {/* Connection result */}
            {latest && (
                <div className="mt-3">
                    {latest.kind === 'connected' && (
                        <Alert color="success" className="mb-0">
                            <div className="fw-semibold">✓ Connected</div>
                            <div className="small mt-1">
                                Active draft: <strong>{latest.listing.Title || '(untitled draft)'}</strong>
                            </div>
                            <div className="small text-muted">
                                Status: {STATUS_LABELS[latest.listing.Status] ?? latest.listing.Status} · Photos:{' '}
                                {latest.imageCount}
                            </div>
                        </Alert>
                    )}
                    {latest.kind === 'no-draft' && (
                        <Alert color="info" className="mb-0">
                            <div className="fw-semibold">✓ Connected to the bridge</div>
                            <div className="small mt-1">
                                No draft is open in AutoAuction. Open a draft, then test again.
                            </div>
                        </Alert>
                    )}
                    {latest.kind === 'error' && (
                        <Alert color="danger" className="mb-0">
                            <div className="fw-semibold">✗ Could not reach the bridge</div>
                            <div className="small mt-1 text-break">{latest.message}</div>
                        </Alert>
                    )}
                </div>
            )}

            {/* Fill status */}
            {fillState === 'ready' && (
                <Alert color="warning" className="mt-3 mb-0 small">
                    Form filled. Review it in the TradeMe tab and click <strong>Start listing</strong> to
                    publish — AutoAuction will then mark the draft Listed automatically.
                </Alert>
            )}
            {fillState === 'listed' && (
                <Alert color="success" className="mt-3 mb-0 small">
                    <div className="fw-semibold">Listed ✓</div>
                    {listedUrl && (
                        <a href={listedUrl} target="_blank" rel="noreferrer" className="text-break">
                            {listedUrl}
                        </a>
                    )}
                </Alert>
            )}
            {fillState === 'error' && (
                <Alert color="danger" className="mt-3 mb-0 small text-break">
                    {fillError}
                </Alert>
            )}

            {/* Fill activity log */}
            {fillLog.length > 0 && (
                <div className="mt-3">
                    <div className="small fw-semibold text-muted mb-1">Activity</div>
                    <div style={{maxHeight: 220, overflow: 'auto'}}>
                        {fillLog.map((line, i) => (
                            <div key={i} className="small font-monospace text-break" style={{opacity: 0.85}}>
                                {line}
                            </div>
                        ))}
                    </div>
                </div>
            )}

            <div className="mt-auto pt-3 small text-muted">
                Default URL is <code>{DEFAULT_BRIDGE_URL}</code> — match the port in AutoAuction → Settings.
            </div>
        </div>
    );
};

createRoot(document.getElementById('root')!).render(<App />);
