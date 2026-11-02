// Hides a server-rendered error as soon as the user edits the field it belongs to.
document.addEventListener('input', event => {
    const field = event.target;
    if (!(field instanceof HTMLInputElement) || !field.dataset.clearError) {
        return;
    }

    document.getElementById(field.dataset.clearError)?.remove();
    field.removeAttribute('aria-describedby');
});
