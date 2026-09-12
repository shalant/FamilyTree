using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FamilyTree.E2E.Tests;

// Replaces the old dead stub (FamilyTree.Web.Tests.UiTests.StorySubmissionFlowTest,
// permanently [Fact(Skip = "Requires running server on localhost:5000")], and hardcoded
// a "testtoken" placeholder that no seeded data ever backed). Self-contained regardless
// of what other tests do to the shared database — creates its own invite record with a
// real, freshly generated token, so it never collides with anything else in this run.
public class StorySubmissionFlowTests(E2eAppFixture fixture) : E2eTestBase(fixture)
{
    [Fact]
    public async Task AnonymousInviteRecipient_CanSubmitAStory()
    {
        var inviterUserId = await Fixture.Seeder.CreateUserAsync($"inviter-{Guid.NewGuid():N}@example.com");
        var token = await Fixture.Seeder.CreateStoryInviteAsync(
            invitedEmail: $"recipient-{Guid.NewGuid():N}@example.com",
            invitedByUserId: inviterUserId,
            unlinkedPersonName: "Bill Small");

        var page = await NewPageAsync();
        await page.GotoAsync($"{Fixture.BaseUrl}/story/respond/{token}");

        // Root cause found via the app's own request log (E2eAppFixture.AppendLog):
        // ValidateTokenAsync ran TWICE for one navigation — Blazor Server's default
        // prerender pass renders the component statically first, then the interactive
        // circuit reconnects over SignalR and re-initializes it as a *second, distinct*
        // component instance. Typing into the form before that second instance takes
        // over gets silently discarded when the interactive render replaces the
        // prerendered DOM. NetworkIdle is the right signal to wait for here (the
        // circuit's WebSocket upgrade is itself an HTTP request Playwright does track,
        // unlike ordinary in-circuit SignalR traffic afterward).
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.GetByPlaceholder("Share what you remember...").FillAsync("A great memory of Bill.");
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Share this memory" }).ClickAsync();

        await Expect(page.GetByText("Thank you")).ToBeVisibleAsync();
    }
}
