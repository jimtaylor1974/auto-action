import React, {useCallback, useEffect, useMemo, useState} from 'react';
import {createRoot} from 'react-dom/client';
import 'bootstrap/dist/css/bootstrap.min.css';
import {Alert, Badge, Button, FormGroup, Input, Label} from 'reactstrap';
import {
    DEFAULT_BRIDGE_URL,
    readStoredBridgeUrl,
    sanitizeBridgeUrl,
    storeBridgeUrl,
    testBridgeConnection
} from './bridge';
import {BridgeTestResult} from './types';

const STATUS_LABELS = ['Draft', 'Listed', 'Sold'];

interface TestLogEntry {
    id: string;
    at: string;
    bridgeUrl: string;
    result: BridgeTestResult;
}

const App: React.FC = () => {
    const [bridgeUrlInput, setBridgeUrlInput] = useState(DEFAULT_BRIDGE_URL);
    const [isTesting, setIsTesting] = useState(false);
    const [log, setLog] = useState<TestLogEntry[]>([]);

    const sanitized = useMemo(
        () => sanitizeBridgeUrl(bridgeUrlInput),
        [bridgeUrlInput]
    );

    useEffect(() => {
        let mounted = true;
        readStoredBridgeUrl()
            .then(url => {
                if (mounted) setBridgeUrlInput(url);
            })
            .catch(() => undefined);
        return () => {
            mounted = false;
        };
    }, []);

    const runTest = useCallback(async () => {
        if (!sanitized.url) return;

        setIsTesting(true);
        try {
            // Persist the (sanitized) URL so it sticks between sessions.
            const url = await storeBridgeUrl(sanitized.url).catch(
                () => sanitized.url!
            );
            const result = await testBridgeConnection(url);
            setLog(entries =>
                [
                    {
                        id: `${Date.now().toString(36)}-${Math.random()
                            .toString(36)
                            .slice(2)}`,
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

    const latest = log[0]?.result;

    return (
        <div
            className="d-flex flex-column p-3"
            style={{minWidth: 320, height: '100vh', overflow: 'auto'}}
        >
            <div className="d-flex align-items-center gap-2 mb-1">
                <h6 className="mb-0">AutoAuction Bridge</h6>
                <Badge color="secondary" pill>
                    test
                </Badge>
            </div>
            <p className="text-muted small">
                Checks that this extension can reach the AutoAuction desktop
                app's local bridge server.
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
                        if (event.key === 'Enter' && sanitized.url) {
                            void runTest();
                        }
                    }}
                />
                <div
                    className={`small mt-1 text-break ${
                        sanitized.url ? 'text-muted' : 'text-danger'
                    }`}
                >
                    {sanitized.url ?? sanitized.error ?? 'Enter a valid URL'}
                </div>
            </FormGroup>

            <Button
                color="primary"
                onClick={() => runTest()}
                disabled={!sanitized.url || isTesting}
            >
                {isTesting ? 'Testing…' : 'Test connection'}
            </Button>

            {/* Latest result, prominently */}
            {latest && (
                <div className="mt-3">
                    {latest.kind === 'connected' && (
                        <Alert color="success" className="mb-0">
                            <div className="fw-semibold">✓ Connected</div>
                            <div className="small mt-1">
                                Active draft:{' '}
                                <strong>
                                    {latest.listing.Title || '(untitled draft)'}
                                </strong>
                            </div>
                            <div className="small text-muted">
                                Status:{' '}
                                {STATUS_LABELS[latest.listing.Status] ??
                                    latest.listing.Status}{' '}
                                · Photos: {latest.imageCount} · Id:{' '}
                                <code>{latest.listing.Id}</code>
                            </div>
                        </Alert>
                    )}
                    {latest.kind === 'no-draft' && (
                        <Alert color="info" className="mb-0">
                            <div className="fw-semibold">
                                ✓ Connected to the bridge
                            </div>
                            <div className="small mt-1">
                                No draft is currently open in AutoAuction. Open a
                                draft in the desktop app, then test again.
                            </div>
                        </Alert>
                    )}
                    {latest.kind === 'error' && (
                        <Alert color="danger" className="mb-0">
                            <div className="fw-semibold">
                                ✗ Could not reach the bridge
                            </div>
                            <div className="small mt-1 text-break">
                                {latest.message}
                            </div>
                        </Alert>
                    )}
                </div>
            )}

            {/* History */}
            {log.length > 0 && (
                <div className="mt-3">
                    <div className="d-flex align-items-center small text-muted mb-1">
                        <span className="fw-semibold flex-grow-1">History</span>
                        <Button
                            color="link"
                            size="sm"
                            className="p-0 small"
                            onClick={() => setLog([])}
                        >
                            Clear
                        </Button>
                    </div>
                    {log.map(entry => {
                        const ok = entry.result.kind !== 'error';
                        return (
                            <div
                                key={entry.id}
                                className="small d-flex align-items-center gap-2 border-bottom py-1"
                            >
                                <Badge color={ok ? 'success' : 'danger'}>
                                    {entry.result.kind}
                                </Badge>
                                <span className="text-muted">{entry.at}</span>
                                <span
                                    className="text-muted text-truncate flex-grow-1"
                                    title={entry.bridgeUrl}
                                >
                                    {entry.bridgeUrl}
                                </span>
                            </div>
                        );
                    })}
                </div>
            )}

            <div className="mt-auto pt-3 small text-muted">
                Default URL is <code>{DEFAULT_BRIDGE_URL}</code>. Change it to
                match the port in AutoAuction → Settings → Local Bridge Server.
            </div>
        </div>
    );
};

createRoot(document.getElementById('root')!).render(<App />);
