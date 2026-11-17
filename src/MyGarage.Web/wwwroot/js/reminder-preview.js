// Shows when a reminder will next be due while the interval and last-done values are typed.
// The server renders the same text on every load (ReminderFormat.NextDuePreview), so the form works without this script; keep the two in step.
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const EMPTY = 'Enter how often it repeats and when it was last done to see when it is next due.';

function integer(form, selector, min, max) {
    const input = form.querySelector(selector);
    const text = input ? input.value.trim() : '';
    if (!/^\d+$/.test(text)) {
        return null;
    }

    const value = Number(text);
    return value >= min && value <= max ? value : null;
}

function dueDate(lastDone, count, unit) {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(lastDone);
    if (!match) {
        return null;
    }

    const months = unit === 'years' ? count * 12 : count;
    const total = (Number(match[2]) - 1) + months;
    const year = Number(match[1]) + Math.floor(total / 12);
    const month = total % 12;
    const lastDay = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
    const day = Math.min(Number(match[3]), lastDay);
    return year > 9999 ? null : `${MONTHS[month]} ${day}, ${year}`;
}

function preview(form) {
    const miles = integer(form, '#interval-miles', 1, 999999);
    const lastMileage = integer(form, '#last-done-mileage', 0, 9999999);
    const time = integer(form, '#interval-time-value', 1, 600);
    const unit = form.querySelector('#interval-time-unit')?.value === 'years' ? 'years' : 'months';
    const lastDone = form.querySelector('#last-done-on')?.value ?? '';

    const distance = miles !== null && lastMileage !== null ? `${(lastMileage + miles).toLocaleString('en-US')} ${lastMileage + miles === 1 ? 'mile' : 'miles'}` : null;
    const date = time !== null ? dueDate(lastDone, time, unit) : null;

    if (distance && date) {
        return `${distance} or ${date}, whichever comes first`;
    }

    return distance ?? date ?? EMPTY;
}

function update(form) {
    const target = form.querySelector('[data-reminder-preview] [data-preview-text]');
    if (target) {
        target.textContent = preview(form);
    }
}

function handle(event) {
    const form = event.target instanceof Element ? event.target.closest('form') : null;
    if (form && form.querySelector('[data-reminder-preview]')) {
        update(form);
    }
}

document.addEventListener('input', handle);
document.addEventListener('change', handle);

// Restores the right text when the browser brings back a cached page with its form values.
window.addEventListener('pageshow', () => {
    document.querySelectorAll('form').forEach(form => {
        if (form.querySelector('[data-reminder-preview]')) {
            update(form);
        }
    });
});
