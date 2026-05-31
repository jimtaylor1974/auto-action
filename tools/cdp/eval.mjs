#!/usr/bin/env node
// CDP eval bridge — run JavaScript on a live Chrome tab via the DevTools Protocol.
//
// The page's Content-Security-Policy blocks string eval *inside the page's own JS
// context* (eval / new Function need 'unsafe-eval'). CDP Runtime.evaluate is exempt
// (same as the DevTools console), so this lets us run arbitrary discovery/automation
// snippets on the real, authenticated TradeMe tab without fighting CSP.
//
// Prereq: start Chrome with remote debugging, e.g. (PowerShell):
//   & "C:\Program Files\Google\Chrome\Application\chrome.exe" `
//       --remote-debugging-port=9222 --user-data-dir="C:\Theta\chrome-debug-profile"
//   (then log in to TradeMe once in that window)
//
// Usage:
//   node eval.mjs --list                       # list open page tabs
//   node eval.mjs --expr "document.title"       # run an expression
//   node eval.mjs --file snippet.js             # run a snippet file
//   echo "return location.href" | node eval.mjs # run from stdin
//
// Snippets are wrapped in an async IIFE, so you can use `return` and `await`.
// The returned value must be JSON-serializable (return plain objects/arrays/strings).
//
// Zero dependencies: uses Node 21+ global fetch and global WebSocket.

import {readFileSync} from 'node:fs';

const args = process.argv.slice(2);
const opt = (name, def) => {
    const i = args.indexOf(name);
    return i >= 0 ? args[i + 1] : def;
};

const port = opt('--port', '9222');
const match = (opt('--match', 'trademe') || '').toLowerCase();
const file = opt('--file', null);
const exprArg = opt('--expr', null);
const listOnly = args.includes('--list');

const base = `http://127.0.0.1:${port}`;

let targets;
try {
    const res = await fetch(`${base}/json`);
    targets = await res.json();
} catch (e) {
    console.error(
        `Could not reach Chrome DevTools on ${base}.\n` +
            `Start Chrome with --remote-debugging-port=${port} (see header of this file). (${e.message})`
    );
    process.exit(2);
}

const pages = targets.filter(t => t.type === 'page');

if (listOnly) {
    console.log(
        JSON.stringify(
            pages.map(p => ({title: p.title, url: p.url})),
            null,
            2
        )
    );
    process.exit(0);
}

const target = pages.find(p => p.url.toLowerCase().includes(match)) || pages[0];
if (!target) {
    console.error('No page tab found. Open the TradeMe tab, then retry. Use --list to see tabs.');
    process.exit(2);
}

const code = file ? readFileSync(file, 'utf8') : (exprArg ?? readFileSync(0, 'utf8'));

const ws = new WebSocket(target.webSocketDebuggerUrl);
let id = 0;
const pending = new Map();
const send = (method, params) =>
    new Promise(resolve => {
        const i = ++id;
        pending.set(i, resolve);
        ws.send(JSON.stringify({id: i, method, params}));
    });

ws.addEventListener('message', ev => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) {
        pending.get(msg.id)(msg);
        pending.delete(msg.id);
    }
});
ws.addEventListener('error', e => {
    console.error('WebSocket error:', e.message || String(e));
    process.exit(2);
});

await new Promise(r => ws.addEventListener('open', r, {once: true}));

await send('Runtime.enable');
const resp = await send('Runtime.evaluate', {
    expression: `(async () => {\n${code}\n})()`,
    awaitPromise: true,
    returnByValue: true,
    userGesture: true
});

const r = resp.result;
if (r?.exceptionDetails) {
    const ex = r.exceptionDetails;
    console.error(
        'EXCEPTION:',
        ex.exception?.description || ex.text || JSON.stringify(ex)
    );
    ws.close();
    process.exit(1);
}

const val = r?.result?.value;
console.log(typeof val === 'string' ? val : JSON.stringify(val, null, 2));
ws.close();
process.exit(0);
