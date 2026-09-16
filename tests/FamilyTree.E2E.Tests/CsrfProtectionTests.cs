using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FamilyTree.E2E.Tests;

// Regression coverage for the CSRF gap on /auth/do-login (found via manual security
// review — see docs/TodoList.md's Site Quality Checklist Audit entry). Neither endpoint
// validated an antiforgery token before the fix: app.UseAntiforgery() was registered,
// but a plain MapPost minimal-API endpoint isn't auto-protected by it unless the handler
// itself calls IAntiforgery.ValidateRequestAsync. A hostile page could have auto-submitted
// a plain cross-site POST to force a visitor's browser to log in as an attacker-controlled
// account (session fixation). Fixed by validating the token and switching every real
// caller onto a real <AntiforgeryToken /> -bearing <form> instead of a JS-built one with
// no token. The matching /auth/do-logout coverage lives in CsrfLogoutIsolatedTests — see
// that file for why it's isolated from this shared "E2E" collection.
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
}
