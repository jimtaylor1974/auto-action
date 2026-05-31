// Open the side panel when the user clicks the extension's action button.
chrome.runtime.onInstalled.addListener(() => {
    chrome.sidePanel
        .setPanelBehavior({openPanelOnActionClick: true})
        .catch(error =>
            console.error('Unable to set side panel behavior', error)
        );
});
