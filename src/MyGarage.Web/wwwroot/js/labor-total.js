// Keeps "Total labor" in step with the labor hours and hourly rate.
// With both given, the total is their product and cannot be edited; with either empty, the owner enters the total.
// The server renders the same state, so the page works the same after a reload or a rejected save.
function fieldsOf(input) {
    const scope = input.closest('[data-pricing-scope]') ?? input.form;
    return scope ? {
        hours: scope.querySelector('input[data-labor-hours]'),
        rate: scope.querySelector('input[data-labor-rate]'),
        total: scope.querySelector('input[data-labor-total]'),
    } : null;
}

function sync(fields) {
    const { hours, rate, total } = fields;
    if (!hours || !rate || !total) {
        return;
    }

    const calculated = hours.value.trim() !== '' && rate.value.trim() !== ''
        && Number.isFinite(Number(hours.value)) && Number.isFinite(Number(rate.value));
    if (calculated) {
        total.value = (Math.round(Number(hours.value) * Number(rate.value) * 100) / 100).toString();
    }

    total.readOnly = calculated;
}

document.addEventListener('input', event => {
    const target = event.target;
    if (target instanceof HTMLInputElement && (target.hasAttribute('data-labor-hours') || target.hasAttribute('data-labor-rate'))) {
        const fields = fieldsOf(target);
        if (fields) {
            sync(fields);
        }
    }
});

// Restores the right state when the browser brings back a cached page with its form values.
window.addEventListener('pageshow', () => {
    document.querySelectorAll('input[data-labor-total]').forEach(total => {
        const fields = fieldsOf(total);
        if (fields) {
            sync(fields);
        }
    });
});
