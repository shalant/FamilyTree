using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace FamilyTree.E2E.Tests;

// S7 (docs/security.md, 2026-10-25): the 14-day auth cookie was never checked against the
// user's security stamp, so a password reset or a demotion left every other session signed
// in. The fixture sets Auth:SessionCheckInterval to zero, so the stamp is checked on every
// request here (and in every other E2E test, which proves the check doesn't break ordinary
// signed-in use). Isolated collection: it adds a real sign-in, and /auth/do-login allows
// five per 15 minutes per IP.
[Collection("E2E-Isolated")]
public class SessionRevocationIsolatedTests(E2eAppFixture fixture) : E2eTestBase(fixture)
{
    [Fact]
    public async Task RotatedSecurityStamp_SignsTheCookieOut()
    {
        var page = await NewPageAsync();
        await using var _ = page.Context;

        var user = await AuthFlowHelper.RegisterWithoutTreeLinkAsync(page, Fixture.BaseUrl);
        await Expect(RoleChip(page)).ToBeVisibleAsync();

        // Unchanged stamp: a fresh request with the same cookie is still signed in. The
        // AppBar's role chip only renders for a signed-in user (its search box renders
        // either way, so it can't tell the two apart).
        var stillIn = await page.Context.NewPageAsync();
        await stillIn.GotoAsync(Fixture.BaseUrl);
        await stillIn.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(RoleChip(stillIn)).ToBeVisibleAsync();

        await Fixture.Seeder.RotateSecurityStampAsync(user.Email);

        var signedOut = await page.Context.NewPageAsync();
        await signedOut.GotoAsync(Fixture.BaseUrl);
        await signedOut.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(signedOut.Locator("form[action='/auth/do-login']")).ToBeVisibleAsync();
        await Expect(RoleChip(signedOut)).ToHaveCountAsync(0);
    }

    private static ILocator RoleChip(IPage page) =>
        page.Locator(".mud-chip", new() { HasTextString = "Member" });
}
