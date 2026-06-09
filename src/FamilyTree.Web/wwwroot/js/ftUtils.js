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
