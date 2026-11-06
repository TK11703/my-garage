// Remembers which tab of a vehicle's page is open: the Edit link opens the same tab, and ?tab= follows the tab so a reload stays on it.
const EDIT_TABS = new Set(["media", "description"]);

document.addEventListener("shown.bs.tab", event => {
    const button = event.target;
    const target = button instanceof Element ? button.getAttribute("data-bs-target") : null;
    if (!target || !target.startsWith("#tab-")) {
        return;
    }

    const tab = target.slice("#tab-".length);

    for (const link of document.querySelectorAll("a[data-tab-link]")) {
        const url = new URL(link.getAttribute("href"), window.location.origin);
        if (EDIT_TABS.has(tab)) {
            url.searchParams.set("tab", tab);
        } else {
            url.searchParams.delete("tab");
        }

        link.setAttribute("href", url.pathname + url.search);
    }

    const current = new URL(window.location.href);
    current.searchParams.set("tab", tab);
    window.history.replaceState(window.history.state, "", current.pathname + current.search);
});
