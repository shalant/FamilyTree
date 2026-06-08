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
