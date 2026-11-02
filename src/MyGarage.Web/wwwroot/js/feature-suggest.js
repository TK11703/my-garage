// Fills a feature box's <datalist> with names other vehicles already use, and adds the feature when one is picked.
// Without this script the box still works: type a name and press Add.
const DELAY_MS = 200;
let timer;
let controller;

async function load(input, list) {
    controller?.abort();
    const text = input.value.trim();
    if (!text) {
        list.replaceChildren();
        return;
    }

    controller = new AbortController();
    try {
        const response = await fetch(`${input.dataset.suggestUrl}?query=${encodeURIComponent(text)}`, {
            signal: controller.signal,
            credentials: 'same-origin',
        });
        if (response.ok) {
            const names = await response.json();
            list.replaceChildren(...names.map(name => Object.assign(document.createElement('option'), { value: name })));
        }
    } catch {
        // Suggestions are a convenience; a failed lookup leaves the box working as plain text.
    }
}

document.addEventListener('input', event => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || !input.dataset.suggestUrl) {
        return;
    }

    const list = document.getElementById(input.getAttribute('list') ?? '');
    if (!list) {
        return;
    }

    // Choosing a suggestion replaces the text without typing, so it arrives without a typing input type.
    const chosen = event.inputType === undefined || event.inputType === 'insertReplacementText';
    if (chosen && [...list.options].some(option => option.value === input.value)) {
        input.form?.requestSubmit();
        return;
    }

    clearTimeout(timer);
    timer = setTimeout(() => load(input, list), DELAY_MS);
});
