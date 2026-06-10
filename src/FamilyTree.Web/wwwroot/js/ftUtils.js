window.ftDownloadFile = (filename, content, mimeType) => {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
};

window.ftSubmitLogin = (email, password) => {
    const form = document.createElement('form');
    form.method = 'POST';
    form.action = '/auth/do-login';
    form.style.display = 'none';
    const add = (name, value) => {
        const i = document.createElement('input');
        i.type = 'hidden'; i.name = name; i.value = value;
        form.appendChild(i);
    };
    add('email', email);
    add('password', password);
    document.body.appendChild(form);
    form.submit();
};

window.ftSubmitLogout = () => {
    const form = document.createElement('form');
    form.method = 'POST';
    form.action = '/auth/do-logout';
    form.style.display = 'none';
    document.body.appendChild(form);
    form.submit();
};

window.ftOpenUrl = (url) =>
    window.open(url, '_blank', 'noopener,noreferrer');

// Compresses an image from <input id=inputId> at fileIndex.
// Returns base64-encoded JPEG, or null if not an image / compression fails.
window.ftCompressImage = (inputId, fileIndex, maxPx, quality) => {
    const input = document.getElementById(inputId);
    if (!input || !input.files || input.files.length <= fileIndex) return Promise.resolve(null);
    const file = input.files[fileIndex];
    if (!file.type.startsWith('image/')) return Promise.resolve(null);

    return new Promise((resolve) => {
        const img = new Image();
        const url = URL.createObjectURL(file);
        img.onload = () => {
            URL.revokeObjectURL(url);
            let w = img.naturalWidth, h = img.naturalHeight;
            if (w > maxPx || h > maxPx) {
                const r = Math.min(maxPx / w, maxPx / h);
                w = Math.round(w * r); h = Math.round(h * r);
            }
            const canvas = document.createElement('canvas');
            canvas.width = w; canvas.height = h;
            canvas.getContext('2d').drawImage(img, 0, 0, w, h);
            canvas.toBlob(blob => {
                if (!blob) { resolve(null); return; }
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result.split(',')[1]);
                reader.onerror = () => resolve(null);
                reader.readAsDataURL(blob);
            }, 'image/jpeg', quality);
        };
        img.onerror = () => { URL.revokeObjectURL(url); resolve(null); };
        img.src = url;
    });
};

window.ftSvgToPng = async (svgContent, filename) => {
    const parser = new DOMParser();
    const svgEl  = parser.parseFromString(svgContent, 'image/svg+xml').documentElement;
    let w = parseInt(svgEl.getAttribute('width'))  || 1600;
    let h = parseInt(svgEl.getAttribute('height')) || 900;

    const maxW = 3000;
    if (w > maxW) { h = Math.round(h * maxW / w); w = maxW; }

    const blob = new Blob([svgContent], { type: 'image/svg+xml;charset=utf-8' });
    const url  = URL.createObjectURL(blob);

    await new Promise(resolve => {
        const img = new Image(w, h);
        img.onload = () => {
            const canvas = document.createElement('canvas');
            canvas.width = w; canvas.height = h;
            const ctx = canvas.getContext('2d');
            ctx.fillStyle = '#f7f5f0';
            ctx.fillRect(0, 0, w, h);
            ctx.drawImage(img, 0, 0, w, h);
            URL.revokeObjectURL(url);
            canvas.toBlob(png => {
                if (!png) { resolve(); return; }
                const a = document.createElement('a');
                a.href = URL.createObjectURL(png);
                a.download = filename;
                a.click();
                setTimeout(() => URL.revokeObjectURL(a.href), 1000);
                resolve();
            }, 'image/png');
        };
        img.onerror = () => { URL.revokeObjectURL(url); resolve(); };
        img.src = url;
    });
};
