using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FamilyTree.E2E.Tests;

// Automates the manual walkthrough in docs/TodoList.md's "Golden-Path Walkthrough
// (2026-09-07)" — invite -> register -> account creation -> tree-linking modal ->
// landing on the tree. That manual walkthrough found 3 real bugs (a false "memory
// saved" toast, a broken avatar-initials glyph, silent form validation) before this
// branch's work gave two of them regression tests (see PersonFormTests,
// RegisterToastTests). This is the automated version of the walkthrough itself — the
// single test most likely to catch the next "basic feature suddenly breaks" surprise,
// since it exercises the full stack a real newly-invited family member hits.
public class GoldenPathTests(E2eAppFixture fixture) : E2eTestBase(fixture)
{
    [Fact]
    public async Task InvitedRelative_CanRegisterAndLinkToAnAnchorRelative()
    {
        var familyId = await Fixture.Seeder.CreateFamilyAsync();
        var anchorPersonId = await Fixture.Seeder.CreatePersonAsync("Grandma", "Rose", familyId);
        var inviteEmail = $"willa-{Guid.NewGuid():N}@example.com";
        var inviteId = await Fixture.Seeder.CreateUserInviteAsync(inviteEmail, familyId);

        var page = await NewPageAsync();
        await AuthFlowHelper.RegisterViaInviteAndLinkToAnchorAsync(
            page, Fixture.BaseUrl, inviteId, inviteEmail,
            anchorPersonName: "Grandma Rose",
            relationshipOptionText: "They're my parent");

        // Confirm the new user actually landed on the tree authenticated, not back at
        // LoginOverlay — the AppBar's "Add person" action is only reachable once past it.
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Edit profile" })).ToBeVisibleAsync();

        // Prove a real new Person row was created and linked, not just that the modal's
        // own success message appeared — query the seeded anchor's own relationships.
        var childOfAnchor = await Fixture.Seeder.GetChildrenAsync(anchorPersonId);
        Assert.Contains(childOfAnchor, p => p.FirstName == "Willa" && p.LastName == "Testuser");
    }
}
