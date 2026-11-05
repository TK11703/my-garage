// Shows the window sticker the user chose before anything is sent: an image or an embedded PDF made from a browser-only URL.
// Accept import stays disabled until a usable file is chosen. Without this script the form still works: pick a file and press the button.
const FORM = 'form[data-sticker-import]';
const MAX_BYTES = 10 * 1024 * 1024;
const TYPES = ['application/pdf', 'image/jpeg', 'image/png', 'image/webp'];
const urls = new WeakMap();

function parts(form) {
    return {
        input: form.querySelector('input[type=file]'),
        preview: form.querySelector('[data-sticker-preview]'),
        clear: form.querySelector('[data-sticker-clear]'),
        accept: form.querySelector('button[type=submit]'),
    };
}

function size(bytes) {
    return bytes >= 1048576 ? `${(bytes / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

function problemWith(file) {
    if (file.type && !TYPES.includes(file.type)) {
        return 'That file is not a PDF, JPG, PNG, or WebP.';
    }

    return file.size > MAX_BYTES ? 'That file is larger than 10 MB.' : null;
}

function refresh(form) {
    const { input, preview, clear, accept } = parts(form);
    if (!input || !preview || !accept) {
        return;
    }

    if (urls.has(form)) {
        URL.revokeObjectURL(urls.get(form));
        urls.delete(form);
    }

    preview.replaceChildren();
    const file = input.files[0];
    accept.disabled = !file;
    if (clear) {
        clear.hidden = !file;
    }

    if (!file) {
        return;
    }

    const label = document.createElement('p');
    label.className = 'mb-2 small';
    label.textContent = `${file.name} (${size(file.size)})`;
    preview.append(label);

    const problem = problemWith(file);
    if (problem) {
        const message = document.createElement('p');
        message.className = 'text-danger small mb-0';
        message.textContent = problem;
        preview.append(message);
        accept.disabled = true;
        return;
    }

    const url = URL.createObjectURL(file);
    urls.set(form, url);

    if (file.type.startsWith('image/')) {
        const image = document.createElement('img');
        image.src = url;
        image.alt = `Preview of ${file.name}`;
        image.className = 'img-fluid rounded border';
        image.style.maxHeight = '320px';
        preview.append(image);
    } else if (file.type === 'application/pdf') {
        const frame = document.createElement('object');
        frame.type = 'application/pdf';
        frame.data = url;
        frame.className = 'w-100 border rounded';
        frame.style.height = '420px';
        frame.setAttribute('aria-label', `Preview of ${file.name}`);
        frame.textContent = 'Your browser cannot show this PDF here, but it can still be imported.';
        preview.append(frame);
    }
}

function formOf(target) {
    return target instanceof Element ? target.closest(FORM) : null;
}

document.addEventListener('change', event => {
    const form = formOf(event.target);
    if (form && event.target instanceof HTMLInputElement && event.target.type === 'file') {
        refresh(form);
    }
});

document.addEventListener('click', event => {
    const button = event.target instanceof Element ? event.target.closest('[data-sticker-clear]') : null;
    const form = button ? formOf(button) : null;
    if (form) {
        parts(form).input.value = '';
        refresh(form);
    }
});

document.addEventListener('dragover', event => {
    const form = formOf(event.target);
    if (form) {
        event.preventDefault();
        form.classList.add('is-dragging');
    }
});

document.addEventListener('dragleave', event => {
    const form = formOf(event.target);
    if (form && !form.contains(event.relatedTarget)) {
        form.classList.remove('is-dragging');
    }
});

document.addEventListener('drop', event => {
    const form = formOf(event.target);
    if (!form) {
        return;
    }

    event.preventDefault();
    form.classList.remove('is-dragging');
    const file = event.dataTransfer.files[0];
    if (file) {
        const transfer = new DataTransfer();
        transfer.items.add(file);
        parts(form).input.files = transfer.files;
        refresh(form);
    }
});

// A page restored from the back/forward cache keeps its file input, so the preview is rebuilt.
window.addEventListener('pageshow', () => document.querySelectorAll(FORM).forEach(refresh));

document.querySelectorAll(FORM).forEach(refresh);

// After an import the result summary is focused so screen readers announce it, when its pane is the one showing.
const summary = document.getElementById('sticker-result');
if (summary && summary.offsetParent !== null) {
    summary.focus();
}
