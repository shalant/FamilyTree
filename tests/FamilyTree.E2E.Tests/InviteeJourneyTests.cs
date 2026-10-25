using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FamilyTree.E2E.Tests;

// A6 (docs/TodoList.md, 2026-10-25): the path a relative takes from a story invite to
// their first view of the tree, as production runs it (invite-only), on a desktop and a
// phone. GoldenPathTests covers the bare invite link in Open mode only.
// FAMILYTREE_E2E_SCREENSHOTS=<dir> saves a screenshot of every step, for looking the
// journey over by eye.
[Collection("E2E-InviteOnly")]
public class InviteeJourneyTests(InviteOnlyAppFixture fixture) : E2eTestBase(fixture)
{
    private const string Password = "CorrectHorseBatteryStaple9!";

    public static TheoryData<string> Devices => new() { "desktop", "phone" };

    private async Task<IPage> NewDevicePageAsync(string device)
    {
        var context = await NewContextAsync(device == "phone"
            ? new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true,
                ViewportSize = new() { Width = 390, Height = 844 },
                IsMobile = true,
                HasTouch = true,
                DeviceScaleFactor = 2,
            }
            : new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true,
                ViewportSize = new() { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        page.Console += (_, msg) => Console.WriteLine($"[browser console:{msg.Type}] {msg.Text}");
        page.PageError += (_, error) => Console.WriteLine($"[browser page error] {error}");
        return page;
    }

    private static async Task ShotAsync(IPage page, string device, string step)
    {
        var dir = Environment.GetEnvironmentVariable("FAMILYTREE_E2E_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{device}-{step}.png"), FullPage = true });
    }

    // The right edge of the open dialog's content, against the screen width. A fixed
    // min-width:400px pushed the linking pickers 34px off a 390px phone.
    private static async Task AssertDialogFitsAsync(IPage page)
    {
        var edges = await page.EvaluateAsync<double[]>(
            "() => [Math.max(...[...document.querySelectorAll('.mud-dialog *')].map(e => e.getBoundingClientRect().right)), window.innerWidth]");
        var (right, width) = (edges[0], edges[1]);
        Assert.True(right <= width, $"Dialog content reaches x={right}, past the {width}px screen.");
    }

    private async Task<(Guid FamilyId, Guid RoseId, string Token, string Email)> SeedStoryInviteAsync(string device)
    {
        var familyId = await Fixture.Seeder.CreateFamilyAsync();
        var roseId = await Fixture.Seeder.CreatePersonAsync("Rose", "Garden", familyId);
        var inviterPersonId = await Fixture.Seeder.CreatePersonAsync("Doug", "Garden", familyId);
        var inviterId = await Fixture.Seeder.CreateLinkedUserAsync(
            $"inviter-{Guid.NewGuid():N}@example.com", Password, inviterPersonId, familyId);
        var email = $"willa-{device}-{Guid.NewGuid():N}@example.com";
        var token = await Fixture.Seeder.CreateStoryInviteAsync(email, inviterId, null, personId: roseId);
        return (familyId, roseId, token, email);
    }

    private static async Task ShareMemoryAsync(IPage page, string baseUrl, string token)
    {
        await page.GotoAsync($"{baseUrl}/story/respond/{token}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(page.GetByText("Share your memory of Rose Garden")).ToBeVisibleAsync();
        await page.GetByPlaceholder("So we know who to thank").FillAsync("Willa");
        await page.GetByPlaceholder("Share what you remember...").FillAsync("Rose taught me to swim at the lake.");
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Share this memory" }).ClickAsync();
        await Expect(page.GetByText("Thank you")).ToBeVisibleAsync();
    }

    [Theory]
    [MemberData(nameof(Devices))]
    public async Task StoryInvite_ToFirstTreeView(string device)
    {
        var (_, roseId, token, _) = await SeedStoryInviteAsync(device);
        var page = await NewDevicePageAsync(device);

        // 1-2. The link in the invite email, then thanks and the offer of an account.
        await ShareMemoryAsync(page, Fixture.BaseUrl, token);
        await ShotAsync(page, device, "2-thanks");
        await page.GetByRole(AriaRole.Link, new() { Name = "Create a free account" }).ClickAsync();

        // 3. Registration, email filled in from the invite, Google offered too.
        await page.WaitForURLAsync(url => url.Contains("/register"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(page.GetByText("You've been invited")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Continue with Google" })).ToBeVisibleAsync();
        await ShotAsync(page, device, "3-register");

        await page.GetByPlaceholder("e.g. Jane").FillAsync("Willa");
        await page.GetByPlaceholder("e.g. Smith").FillAsync("Garden");
        await page.GetByPlaceholder("Minimum 10 characters").FillAsync(Password);
        await page.GetByPlaceholder("Re-enter your password").FillAsync(Password);
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        // 4. Linking to the tree.
        await Expect(page.GetByText("You're all set!")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Link to your tree →" }).ClickAsync();

        await Expect(page.GetByText("Are you already on this family tree?")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "No, I'm not" }).ClickAsync();

        await Expect(page.GetByText("Do you know anyone on this tree?")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Yes, I do" }).ClickAsync();

        await Expect(page.GetByText("Who do you know?")).ToBeVisibleAsync();
        await AssertDialogFitsAsync(page);
        await page.GetByPlaceholder("Select a person…").ClickAsync();
        await ShotAsync(page, device, "6-who");
        await page.GetByRole(AriaRole.Option, new() { Name = "Rose Garden" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

        await Expect(page.GetByText("How are you related to them?")).ToBeVisibleAsync();
        await page.GetByPlaceholder("Select a relationship…").ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "They're my parent" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

        // 5. The tree, signed in as the new account, with a tip that fits the device.
        await page.WaitForURLAsync(url => !url.Contains("/register"), new() { Timeout = 10_000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // The tip waits for the tree's first render; a cold app on its first test run took
        // longer than the default 5s once.
        await Expect(page.GetByText(device == "phone" ? "Pinch to zoom" : "Scroll to zoom"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await ShotAsync(page, device, "8-tree");

        var children = await Fixture.Seeder.GetChildrenAsync(roseId);
        Assert.Contains(children, p => p.FirstName == "Willa");
    }

    // Someone sent a link from Admin > Users who then answers a story invite. The second
    // invite for the address failed, and "Create a free account" pointed at
    // /register?invite= with no ID: "Invitation required" in invite-only mode.
    [Fact]
    public async Task StoryInvite_ToSomeoneAlreadyInvited_StillOffersTheirInvite()
    {
        var (familyId, _, token, email) = await SeedStoryInviteAsync("already-invited");
        await Fixture.Seeder.CreateUserInviteAsync(email, familyId);
        var page = await NewDevicePageAsync("desktop");

        await ShareMemoryAsync(page, Fixture.BaseUrl, token);
        await page.GetByRole(AriaRole.Link, new() { Name = "Create a free account" }).ClickAsync();

        await page.WaitForURLAsync(url => url.Contains("/register"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(page.GetByText("You've been invited")).ToBeVisibleAsync();
    }

    // An expired link used to show the blank form, which failed only after it was filled in.
    [Theory]
    [MemberData(nameof(Devices))]
    public async Task ExpiredInviteLink_SaysSoBeforeTheForm(string device)
    {
        var familyId = await Fixture.Seeder.CreateFamilyAsync();
        var expired = await Fixture.Seeder.CreateUserInviteAsync(
            $"late-{Guid.NewGuid():N}@example.com", familyId, ttlDays: -1);
        var page = await NewDevicePageAsync(device);

        await page.GotoAsync($"{Fixture.BaseUrl}/register?invite={expired}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(page.GetByText("This invite link no longer works")).ToBeVisibleAsync();
        await Expect(page.GetByPlaceholder("Minimum 10 characters")).ToHaveCountAsync(0);
        await ShotAsync(page, device, "x2-expired-invite");
    }
}
