using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace FamilyTree.E2E.Tests;

// S9 (docs/security.md): a signed-out visit to a protected page is challenged to
// /?ReturnUrl=/stories, but sign-in ignored it and always landed on the tree (found
// 2026-10-11, smoke test). Covers the whole path: challenge, the LoginOverlay's hidden
// field, the native POST and /auth/do-login's redirect. Isolated collection for the
// login rate limit (five per 15 minutes per IP).
[Collection("E2E-Isolated")]
public class ReturnUrlIsolatedTests(E2eAppFixture fixture) : E2eTestBase(fixture)
{
    [Fact]
    [Trait("Category", "Regression")]
    public async Task SignedOutDeepLink_LandsThereAfterSignIn()
    {
        var familyId = await Fixture.Seeder.CreateFamilyAsync();
        var personId = await Fixture.Seeder.CreatePersonAsync("Ret", "Urn", familyId);
        var email = $"return-url-{Guid.NewGuid():N}@example.com";
        const string password = "CorrectHorseBatteryStaple9!";
        await Fixture.Seeder.CreateLinkedUserAsync(email, password, personId, familyId);

        var page = await NewPageAsync();
        await using var _ = page.Context;

        await page.GotoAsync($"{Fixture.BaseUrl}/stories");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Contains("ReturnUrl=%2Fstories", page.Url);

        await page.GetByPlaceholder("your@email.com").FillAsync(email);
        await page.GetByPlaceholder("••••••••••").FillAsync(password);
        await Expect(page.Locator("input[name='email']")).ToHaveValueAsync(email);
        await Expect(page.Locator("input[name='password']")).ToHaveValueAsync(password);
        await Expect(page.Locator("input[name='returnUrl']")).ToHaveValueAsync("/stories");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Equal($"{Fixture.BaseUrl}/stories", page.Url);
    }
}
