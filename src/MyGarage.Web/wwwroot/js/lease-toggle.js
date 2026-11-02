// Follows the "Purchased or leased" choice: shows "Date lease ends" only for Leased, and words the date and mileage labels to match.
// The server also renders the right state, so the page works the same after a reload or a rejected save.
function sync(select) {
    const field = document.getElementById(select.dataset.leaseSource);
    if (field) {
        field.hidden = select.value !== 'Leased';
    }

    const kind = select.value === 'Leased' ? 'leased' : select.value === 'Purchased' ? 'purchased' : 'unset';
    select.form?.querySelectorAll('[data-ownership-text]').forEach(label => {
        label.textContent = label.dataset[kind];
    });
}

document.addEventListener('change', event => {
    if (event.target instanceof HTMLSelectElement && event.target.dataset.leaseSource) {
        sync(event.target);
    }
});

// Restores the right state when the browser brings back a cached page with its form values.
window.addEventListener('pageshow', () => {
    document.querySelectorAll('select[data-lease-source]').forEach(sync);
});
