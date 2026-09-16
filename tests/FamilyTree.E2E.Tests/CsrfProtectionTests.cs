using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FamilyTree.E2E.Tests;

// Regression coverage for the CSRF gap on /auth/do-login and /auth/do-logout (found via
// manual security review — see docs/TodoList.md's Site Quality Checklist Audit entry).
// Neither endpoint validated an antiforgery token before the fix: app.UseAntiforgery()
// was registered, but a plain MapPost minimal-API endpoint isn't auto-protected by it
// unless the handler itself calls IAntiforgery.ValidateRequestAsync. A hostile page could
// have auto-submitted a plain cross-site POST to force a visitor's browser to log in as
// an attacker-controlled account (session fixation) or to force a log-out. Fixed by
// validating the token in both handlers and switching every real caller (LoginOverlay's
// form, Register.razor's post-registration auto-login, CustomAppBar's sign-out) onto a
// real <AntiforgeryToken /> -bearing <form> instead of a JS-built one with no token.
public class CsrfProtectionTests(E2eAppFixture fixture) : E2eTestBase(fixture)
{
    [Fact]
    [Trait("Category", "Regression")]
    public async Task DoLogin_WithoutAntiforgeryToken_IsRejectedBeforeCredentialsAreChecked()
    {
        // A brand-new API request context with no prior GET — no antiforgery cookie, no
        // token field. This is exactly what an attacker's own page can produce: it can
        // auto-submit a form to arborkin's /auth/do-login, but it has no way to read a
        // real antiforgery token, since that value is minted server-side per-response.
        await using var api = await Fixture.Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = Fixture.BaseUrl,
            IgnoreHTTPSErrors = true,
        });

        var form = api.CreateFormData();
        form.Set("email", "nobody@example.com");
        form.Set("password", "whatever-not-a-real-password");

        var response = await api.PostAsync("/auth/do-login", new APIRequestContextOptions { Form = form });

        // Redirects (followed by default) land back on "/" with a dedicated error code —
        // proving the request was rejected for missing CSRF protection, not merely
        // because "nobody@example.com" isn't a real account (that would be loginError=
        // invalid, a completely different code path further down the same handler).
        Assert.Contains("loginError=csrf", response.Url);
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task DoLogout_WithoutAntiforgeryToken_DoesNotSignTheUserOut()
    {
        var page = await NewPageAsync();
        await AuthFlowHelper.RegisterWithoutTreeLinkAsync(page, Fixture.BaseUrl);

        // RegisterWithoutTreeLinkAsync's own final await (WaitForLoadStateAsync(NetworkIdle)
        // right after the "Done" click) can settle on a brief transitional idle gap before
        // the real auto-login POST->redirect actually lands — a negative assertion like
        // "the login button isn't visible" would trivially pass against that blank
        // in-between frame too (no button in the DOM yet either way), asserting nothing.
        // CustomAppBar's search box only ever renders once actually authenticated, so
        // waiting for it positively proves the auto-login landed, not just that some page
        // rendered.
        var searchBox = page.GetByPlaceholder("Search people…");
        await Expect(searchBox).ToBeVisibleAsync();

        // Same browser context, so the real session + antiforgery cookies ride along
        // automatically (cookies aren't same-origin restricted — that's the whole CSRF
        // threat model), but this bypasses the real <form>+<AntiforgeryToken/> entirely,
        // so no valid token field is sent. Content-Type is deliberately set to a real
        // form encoding (empty body alone isn't representative — a genuine hostile
        // <form method="post"> with no inputs still POSTs as
        // application/x-www-form-urlencoded) — exactly what a hostile page's own auto-
        // submitting forged form would produce for a victim who's currently signed in.
        var forged = await page.Context.APIRequest.PostAsync($"{Fixture.BaseUrl}/auth/do-logout",
            new APIRequestContextOptions { Form = page.Context.APIRequest.CreateFormData() });
        Assert.True(forged.Ok); // rejected cleanly, not a 500 — see Program.cs's try/catch

        // A real (pre-fix) sign-out invalidates the auth cookie backing the still-open
        // page's Blazor Server circuit, which can race ftUtils.js's own auto-reload-on-
        // rejected-circuit handler against a plain page.ReloadAsync() call and abort the
        // navigation (ERR_ABORTED) — a flaky symptom of the real bug, not something to
        // paper over. A fresh page in the same (cookie-sharing) browser context sidesteps
        // that stale circuit entirely and asks the one real question: is this cookie jar
        // still authenticated?
        var checkPage = await page.Context.NewPageAsync();
        await checkPage.GotoAsync(Fixture.BaseUrl);

        // Still authenticated — the forged request was rejected rather than silently
        // signing the user out before this fix existed.
        await Expect(checkPage.GetByPlaceholder("Search people…")).ToBeVisibleAsync();
    }
}
