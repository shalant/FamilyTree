using Microsoft.JSInterop;

namespace FamilyTree.Web.Services;

/// <summary>
/// Singleton that owns the dark-mode flag.
/// Persists the preference in localStorage via JS interop.
/// Raise OnChange to notify any subscriber (MainLayout) to re-render.
/// </summary>
public sealed class ThemeService
{
    private readonly IJSRuntime _js;
    private bool _isDark;

    public ThemeService(IJSRuntime js) => _js = js;

    public bool IsDark => _isDark;

    public event Action? OnChange;

    /// <summary>
    /// Call once from MainLayout.OnAfterRenderAsync(firstRender=true)
    /// to restore the user's saved preference.
    /// </summary>
    public async Task InitAsync()
    {
        try
        {
            var saved = await _js.InvokeAsync<string?>("localStorage.getItem", "ft-theme");
            _isDark = saved == "dark";
        }
        catch
        {
            // JS not available during SSR pre-render — ignore
        }
        await ApplyAsync();
    }

    public async Task ToggleAsync()
    {
        _isDark = !_isDark;
        await ApplyAsync();
        OnChange?.Invoke();
    }

    // Writes data-theme="dark"|"light" on <html> so our CSS selector fires.
    // MudBlazor also reads this attribute internally.
    private async Task ApplyAsync()
    {
        try
        {
            var theme = _isDark ? "dark" : "light";
            await _js.InvokeVoidAsync("eval",
                $"document.documentElement.setAttribute('data-theme', '{theme}')");
            await _js.InvokeVoidAsync("localStorage.setItem", "ft-theme", theme);
        }
        catch { /* SSR pre-render — skip */ }
    }
}