// FAQ page: filters questions as you type (every typed word must match; matches open) and lets a card's
// "Browse all questions" button open or close every answer in that card. Delegated so it survives enhanced navigation.
function applyFilter(search) {
    const grid = document.querySelector("[data-faq-grid]");
    if (!grid) {
        return;
    }

    const words = search.value.toLowerCase().split(/\s+/).filter(Boolean);
    let shown = 0;

    for (const item of grid.querySelectorAll("[data-faq-item]")) {
        const match = words.every(word => item.textContent.toLowerCase().includes(word));
        item.hidden = !match;
        const details = item.querySelector("details");
        if (details) {
            details.open = match && words.length > 0;
        }

        if (match) {
            shown++;
        }
    }

    for (const category of grid.querySelectorAll("[data-faq-category]")) {
        category.hidden = !category.querySelector("[data-faq-item]:not([hidden])");
    }

    const empty = document.querySelector("[data-faq-empty]");
    if (empty) {
        empty.hidden = shown > 0;
    }
}

document.addEventListener("input", event => {
    if (event.target instanceof HTMLInputElement && event.target.hasAttribute("data-faq-search")) {
        applyFilter(event.target);
    }
});

document.addEventListener("click", event => {
    const button = event.target instanceof Element ? event.target.closest("[data-faq-expand]") : null;
    if (!button) {
        return;
    }

    const details = [...button.closest("[data-faq-category]").querySelectorAll("[data-faq-item]:not([hidden]) details")];
    const open = details.some(item => !item.open);
    for (const item of details) {
        item.open = open;
    }

    button.setAttribute("aria-expanded", open ? "true" : "false");
});
