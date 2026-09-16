using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace FamilyTree.E2E.Tests;

// Regression coverage for the CSRF gap on /auth/do-logout (see CsrfProtectionTests for
// the /auth/do-login half and the fix's full context). Isolated in its own collection
// (its own E2eAppFixture — separate app process, separate database) rather than sharing
// "E2E" with the rest of the suite: this test was found to reliably break whichever test
// ran immediately after it there (GoldenPathTests timed out waiting on a dropdown option
// that never appeared, deterministically, 5/5 CI runs and reproduced locally). The exact
// mechanism wasn't pinned down — leading theory is that this test's two real Blazor
// circuits plus a raw forged POST (bypassing the normal form flow) leaves a DB
// connection/transaction in a state that blocks the next test's query — but isolating it
// removes the interaction entirely rather than papering over a specific symptom.
[Collection("E2E-Isolated")]
public class CsrfLogoutIsolatedTests(E2eAppFixture fixture) : E2eTestBase(fixture)
{
    [Fact]
    [Trait("Category", "Regression")]
    public async Task DoLogout_WithoutAntiforgeryToken_DoesNotSignTheUserOut()
    {
        var page = await NewPageAsync();
        await using var _ = page.Context;

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
