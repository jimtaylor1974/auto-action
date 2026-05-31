# CDP eval bridge (dev / discovery only)

`eval.mjs` runs JavaScript on a live Chrome tab via the Chrome DevTools Protocol
(`Runtime.evaluate`). CDP eval is **exempt from the page's Content-Security-Policy**
(just like the DevTools console), so we can run discovery/automation snippets on the
real, authenticated TradeMe tab without hitting the `unsafe-eval` block that kills
in-page `eval` / `new Function`.

This is a **development tool** for working out the TradeMe listing steps. The shipped
Chrome extension does **not** use eval — it injects typed functions via
`chrome.scripting.executeScript({func})`. (If the extension ever needs ad-hoc eval at
runtime, the in-extension equivalent of this tool is the `chrome.debugger` permission +
`Runtime.evaluate`.)

## 1. Launch Chrome with remote debugging

Use a **dedicated profile dir** so it doesn't touch your normal Chrome and you don't
have to close existing windows. Log in to TradeMe once in this window.

```powershell
& "C:\Program Files\Google\Chrome\Application\chrome.exe" `
    --remote-debugging-port=9222 `
    --user-data-dir="C:\Theta\chrome-debug-profile"
```

The debugging port binds to localhost only.

## 2. Run snippets

```powershell
node tools/cdp/eval.mjs --list                  # list open page tabs
node tools/cdp/eval.mjs --expr "document.title"  # run an expression
node tools/cdp/eval.mjs --file snippet.js        # run a snippet file
```

Snippets are wrapped in an async IIFE, so you can use `return` and `await`. Return
JSON-serializable values (plain objects/arrays/strings). By default it targets the first
tab whose URL contains `trademe`; override with `--match <substring>` or `--port <n>`.
