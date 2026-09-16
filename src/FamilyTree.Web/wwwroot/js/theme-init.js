// Runs synchronously, before any stylesheet loads, so the correct data-theme
// attribute is already on <html> by the time app.css/MudBlazor.min.css apply their
// [data-theme="dark"] rules on first paint — avoids the flash of the wrong theme
// that ThemeService.InitAsync() alone can't prevent, since that only runs later via
// JS interop in OnAfterRenderAsync, well after first paint.
(function () {
    try {
        var theme = localStorage.getItem('ft-theme');
        if (theme === 'dark' || theme === 'light') {
            document.documentElement.setAttribute('data-theme', theme);
        }
    } catch (e) {
        // localStorage unavailable (private browsing, etc.) — default theme stands.
    }
})();
