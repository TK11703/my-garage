// Submits the switch's form as soon as it is flipped; without script the form shows an Apply button instead.
document.addEventListener('change', event => {
    const target = event.target;
    if (target instanceof HTMLInputElement && target.hasAttribute('data-auto-submit')) {
        target.form?.requestSubmit();
    }
});
