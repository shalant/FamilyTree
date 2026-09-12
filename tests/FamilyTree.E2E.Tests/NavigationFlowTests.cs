using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FamilyTree.E2E.Tests;

// Replaces the old dead stub (FamilyTree.Web.Tests.UiTests.NavigationFlowTests,
// permanently [Fact(Skip = "Requires running server on localhost:5000")]). The original
// stub asserted on unauthenticated DOM content — found live in this branch's work that
// Playwright's ToBeVisibleAsync checks CSS visibility, not z-index stacking, so content
// sitting *behind* LoginOverlay (which covers the whole viewport for a signed-out
// visitor) still reads as "visible" even though a real user would never see it. A
// meaningful test of Home.razor's content needs a real authenticated session.
//
// Uses a directly-seeded, already-linked user (bypassing LinkToTreeModal via
// TestDataSeeder.CreateLinkedUserAsync + a real /auth/do-login POST) rather than the
// full registration flow — that flow is the dedicated subject of GoldenPathTests, and
// this test's own subject is just "does an authenticated user's tree render."
public class NavigationFlowTests(E2eAppFixture fixture) : E2eTestBase(fixture)
{
    [Fact]
    public async Task AuthenticatedUser_SeesTheirFocusPerson()
    {
        var familyId = await Fixture.Seeder.CreateFamilyAsync();
        var personId = await Fixture.Seeder.CreatePersonAsync("Rose", "Anchor", familyId);
        var email = $"nav-flow-test-{Guid.NewGuid():N}@example.com";
        const string password = "CorrectHorseBatteryStaple9!";
        await Fixture.Seeder.CreateLinkedUserAsync(email, password, personId, familyId);

        var page = await NewPageAsync();
        await page.GotoAsync(Fixture.BaseUrl);

        // Blazor Server prerenders statically first, then the interactive circuit
        // reconnects over SignalR and re-initializes the component as a second,
        // distinct instance — typing before that second instance takes over gets
        // silently discarded (see StorySubmissionFlowTests for the full diagnosis).
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.GetByPlaceholder("your@email.com").FillAsync(email);
        await page.GetByPlaceholder("••••••••••").FillAsync(password);

        // LoginOverlay.razor's real <form method="post" action="/auth/do-login"> submits
        // hidden <input type="hidden" name="email"/"password"> fields, not the visible
        // MudTextFields directly — those hidden inputs only get Blazor's current value
        // once the Immediate="true" oninput's SignalR round-trip lands and re-renders.
        // Submitting before that round-trip completes POSTs stale (empty) hidden values,
        // which /auth/do-login correctly rejects as "missing." Wait for the actual value
        // rather than guessing at a delay.
        await Expect(page.Locator("input[name='email']")).ToHaveValueAsync(email);
        await Expect(page.Locator("input[name='password']")).ToHaveValueAsync(password);

        // The login form is a real <form method="post" action="/auth/do-login"> (not a
        // JS/SignalR call), so submitting it is a real full-page navigation.
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.False(page.Url.Contains("loginError"), $"Login redirected with an error: {page.Url}");

        // The canvas node shows initials ("RA") and the hero title shows a possessive
        // first-name form ("Rose's Family Tree") — neither renders the literal string
        // "Rose Anchor" anywhere. The hero overlay's "Edit profile" link is the one
        // element that ties unambiguously back to the exact seeded person by id, so
        // assert on that instead of guessing at display-text formatting.
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Edit profile" }))
            .ToHaveAttributeAsync("href", $"/people/{personId}/edit");
    }
}
