// Shows only the line item card section for the chosen pricing method (Flat Cost or Labor and Parts).
// The server also renders the right state, so the page works the same after a reload or a rejected save.
// A form with several line items marks each one's pricing area with data-pricing-scope; otherwise the whole form is the scope.
function scopeOf(radio) {
    return radio.closest('[data-pricing-scope]') ?? radio.form;
}

function sync(scope) {
    const chosen = scope.querySelector('input[data-pricing-choice]:checked')?.value;
    if (!chosen) {
        return;
    }

    scope.querySelectorAll('[data-pricing-section]').forEach(section => {
        section.hidden = section.dataset.pricingSection !== chosen;
    });
}

document.addEventListener('change', event => {
    if (event.target instanceof HTMLInputElement && event.target.hasAttribute('data-pricing-choice')) {
        const scope = scopeOf(event.target);
        if (scope) {
            sync(scope);
        }
    }
});

// Restores the right state when the browser brings back a cached page with its form values.
window.addEventListener('pageshow', () => {
    document.querySelectorAll('input[data-pricing-choice]:checked').forEach(radio => {
        const scope = scopeOf(radio);
        if (scope) {
            sync(scope);
        }
    });
});
