// Shows a button marked data-busy-text as busy once its form is submitted, so a slow save gives feedback and cannot be sent twice.
// Without script the button simply submits.
const restore = new Map();

function lock(button) {
    const form = button.form;
    const submitters = form ? [...form.elements].filter(el => el instanceof HTMLButtonElement && el.type === 'submit') : [button];

    for (const submitter of submitters) {
        restore.set(submitter, { disabled: submitter.disabled, children: [...submitter.childNodes] });
        submitter.disabled = true;
    }

    const spinner = document.createElement('span');
    spinner.className = 'spinner-border spinner-border-sm';
    spinner.setAttribute('aria-hidden', 'true');
    button.replaceChildren(spinner, ' ' + button.dataset.busyText);
    button.setAttribute('aria-busy', 'true');
}

document.addEventListener('submit', event => {
    const button = event.submitter;
    if (button instanceof HTMLButtonElement && button.hasAttribute('data-busy-text')) {
        // Disabling after the form data is built keeps the button's name and value in the post.
        setTimeout(() => lock(button), 0);
    }
});

// Undoes the busy state when the browser brings back a cached page.
window.addEventListener('pageshow', event => {
    if (!event.persisted) {
        return;
    }

    for (const [submitter, original] of restore) {
        submitter.disabled = original.disabled;
        submitter.replaceChildren(...original.children);
        submitter.removeAttribute('aria-busy');
    }

    restore.clear();
});
