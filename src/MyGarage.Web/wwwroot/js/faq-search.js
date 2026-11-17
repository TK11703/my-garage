// FAQ page: filters questions as you type (every typed word must match; matches open) and hides the topics that
// have no match left. Delegated so it survives enhanced navigation.
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
        const visible = !!category.querySelector("[data-faq-item]:not([hidden])");
        category.hidden = !visible;
        const topic = grid.querySelector(`[data-faq-topic="${category.dataset.faqCategory}"]`);
        if (topic) {
            topic.hidden = !visible;
        }
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