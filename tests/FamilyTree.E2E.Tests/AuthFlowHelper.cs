using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Drives the real registration -> LinkToTreeModal -> auto-login flow via the actual UI
/// (never DevAuth — DevAuth replaces the entire cookie/Identity pipeline Program.cs
/// registers, which would make this exact flow impossible to exercise for real). Shared
/// by any test that just needs "an authenticated session" as a precondition, plus the
/// dedicated golden-path test that asserts on the flow itself.
/// </summary>
public static class AuthFlowHelper
{
    public sealed record RegisteredUser(string Email, string Password, string FirstName, string LastName);

    /// <summary>
    /// Registers a brand-new account with no invite and no existing tree connection —
    /// the simplest path through LinkToTreeModal (IsUserOnTree -> No, IsUserRelated ->
    /// No, "skip for now"). Leaves the browser authenticated on the home page. Use this
    /// when a test just needs "some logged-in user," not the linking flow itself.
    /// </summary>
    public static async Task<RegisteredUser> RegisterWithoutTreeLinkAsync(IPage page, string baseUrl)
    {
        var user = new RegisteredUser(
            Email: $"e2e-{Guid.NewGuid():N}@example.com",
            Password: "CorrectHorseBatteryStaple9!",
            FirstName: "Ellis",
            LastName: "Testuser");

        await page.GotoAsync($"{baseUrl}/register");

        // Blazor Server prerenders statically first, then the interactive circuit
        // reconnects over SignalR and re-initializes the component as a second,
        // distinct instance — typing before that second instance takes over gets
        // silently discarded (see StorySubmissionFlowTests for the full diagnosis).
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.GetByPlaceholder("e.g. Jane").FillAsync(user.FirstName);
        await page.GetByPlaceholder("e.g. Smith").FillAsync(user.LastName);
        await page.GetByPlaceholder("you@example.com").FillAsync(user.Email);
        await page.GetByPlaceholder("Minimum 10 characters").FillAsync(user.Password);
        await page.GetByPlaceholder("Re-enter your password").FillAsync(user.Password);
        await page.Keyboard.PressAsync("Tab");

        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await Expect(page.GetByText("You're all set!")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Link to your tree →" }).ClickAsync();

        await Expect(page.GetByText("Are you already on this family tree?")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "No, I'm not" }).ClickAsync();

        await Expect(page.GetByText("Do you know anyone on this tree?")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "No, I don't" }).ClickAsync();

        // "Done" once _complete is true (see LinkToTreeModal.razor) — closes the dialog,
        // which triggers Register.razor's real POST to /auth/do-login (a hidden, real
        // <form> with <AntiforgeryToken />, submitted via JS's ftSubmitFormById — see
        // CsrfProtectionTests). That POST is a native form submit (not fetch/XHR), so it
        // causes a real full-page navigation; wait for it explicitly rather than racing
        // the click.
        await page.GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        return user;
    }

    /// <summary>
    /// Registers using a real invite link and links to the tree via an existing anchor
    /// person (IsUserOnTree -> No, IsUserRelated -> Yes, pick the anchor, pick a
    /// relationship) — the real path a newly invited relative takes, per docs/TodoList.md's
    /// Golden-Path Walkthrough. Requires anchorPersonName to already exist in the DB
    /// (e.g. via TestDataSeeder.CreatePersonAsync) before calling this.
    /// </summary>
    public static async Task<RegisteredUser> RegisterViaInviteAndLinkToAnchorAsync(
        IPage page, string baseUrl, Guid inviteId, string inviteEmail, string anchorPersonName, string relationshipOptionText)
    {
        var user = new RegisteredUser(
            Email: inviteEmail,
            Password: "CorrectHorseBatteryStaple9!",
            FirstName: "Willa",
            LastName: "Testuser");

        await page.GotoAsync($"{baseUrl}/register?invite={inviteId}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Email is pre-filled and disabled by Register.razor when an invite is present —
        // only name and password need filling.
        await page.GetByPlaceholder("e.g. Jane").FillAsync(user.FirstName);
        await page.GetByPlaceholder("e.g. Smith").FillAsync(user.LastName);
        await page.GetByPlaceholder("Minimum 10 characters").FillAsync(user.Password);
        await page.GetByPlaceholder("Re-enter your password").FillAsync(user.Password);
        await page.Keyboard.PressAsync("Tab");

        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await Expect(page.GetByText("You're all set!")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Link to your tree →" }).ClickAsync();

        await Expect(page.GetByText("Are you already on this family tree?")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "No, I'm not" }).ClickAsync();

        await Expect(page.GetByText("Do you know anyone on this tree?")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Yes, I do" }).ClickAsync();

        await Expect(page.GetByText("Who do you know?")).ToBeVisibleAsync();
        await SelectMudOptionAsync(page, "Select a person…", anchorPersonName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

        await Expect(page.GetByText("How are you related to them?")).ToBeVisibleAsync();
        await SelectMudOptionAsync(page, "Select a relationship…", relationshipOptionText);
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

        // Unlike the "no connection" path above, LinkToNewPersonAsync closes the dialog
        // itself (CompleteWithMessage, then a 500ms pause, then SafeDialogClose, then
        // Register.razor's real auto-login navigation) — there's no "Done" button to
        // click, and the whole sequence happens fast enough that asserting on the
        // intermediate completion message races the navigation away from it (observed
        // directly: the assertion timed out, but the resulting page had already fully
        // navigated to the authenticated tree view with the new person present — the
        // flow succeeded, the assertion was just checking for content that had already
        // been replaced). Waiting for the navigation itself is the reliable signal.
        await page.WaitForURLAsync(url => !url.Contains("/register"), new() { Timeout = 10_000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        return user;
    }

    // MudSelect renders as a readonly <input placeholder="..."> that opens a popover of
    // MudSelectItems on click, not a native <select> — GetByText doesn't match a
    // placeholder *attribute*, only real text nodes, so this needs GetByPlaceholder.
    private static async Task SelectMudOptionAsync(IPage page, string selectPlaceholder, string optionText)
    {
        await page.GetByPlaceholder(selectPlaceholder).ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = optionText }).ClickAsync();
    }
}
