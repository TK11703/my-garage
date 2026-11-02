// Turns a picture upload form into a click-or-drop area: choosing or dropping pictures sends them straight away.
// Without this script the form still works through its visible Upload button.
const ZONE = 'form[data-dropzone]';

function zoneOf(target) {
    return target instanceof Element ? target.closest(ZONE) : null;
}

// One picture is an ordinary form post; several are posted one after another, then the page shows the last result.
async function send(form, files) {
    if (files.length === 0) {
        return;
    }

    form.classList.add('is-busy');

    if (files.length === 1) {
        const transfer = new DataTransfer();
        transfer.items.add(files[0]);
        form.querySelector('input[type=file]').files = transfer.files;
        form.requestSubmit();
        return;
    }

    let last;
    try {
        for (const file of files) {
            const data = new FormData(form);
            data.set('file', file);
            last = await fetch(form.action, { method: 'POST', body: data, credentials: 'same-origin' });
        }
    } catch {
        // The page reload below shows whatever was saved.
    }

    window.location.assign(last?.url ?? window.location.href);
}

document.addEventListener('change', event => {
    const form = zoneOf(event.target);
    if (form && event.target instanceof HTMLInputElement && event.target.type === 'file') {
        send(form, [...event.target.files]);
    }
});

document.addEventListener('dragover', event => {
    const form = zoneOf(event.target);
    if (form) {
        event.preventDefault();
        form.classList.add('is-dragging');
    }
});

document.addEventListener('dragleave', event => {
    const form = zoneOf(event.target);
    if (form && !form.contains(event.relatedTarget)) {
        form.classList.remove('is-dragging');
    }
});

document.addEventListener('drop', event => {
    const form = zoneOf(event.target);
    if (form) {
        event.preventDefault();
        form.classList.remove('is-dragging');
        send(form, [...event.dataTransfer.files]);
    }
});
