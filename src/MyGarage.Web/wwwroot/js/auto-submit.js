// Submits a switch's or select's form as soon as it changes; without script the form keeps a visible Apply button.
document.addEventListener('change', event => {
    const target = event.target;
    if ((target instanceof HTMLInputElement || target instanceof HTMLSelectElement) && target.hasAttribute('data-auto-submit')) {
        target.form?.requestSubmit();
    }
});
