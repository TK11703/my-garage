// Shows only the line item card section for the chosen pricing method (Flat Cost or Labor and Parts).
// The server also renders the right state, so the page works the same after a reload or a rejected save.
function sync(form) {
    const chosen = form.querySelector('input[data-pricing-choice]:checked')?.value;
    if (!chosen) {
        return;
    }

    form.querySelectorAll('[data-pricing-section]').forEach(section => {
        section.hidden = section.dataset.pricingSection !== chosen;
    });
}

document.addEventListener('change', event => {
    if (event.target instanceof HTMLInputElement && event.target.hasAttribute('data-pricing-choice') && event.target.form) {
        sync(event.target.form);
    }
});

// Restores the right state when the browser brings back a cached page with its form values.
window.addEventListener('pageshow', () => {
    document.querySelectorAll('input[data-pricing-choice]:checked').forEach(radio => radio.form && sync(radio.form));
});
