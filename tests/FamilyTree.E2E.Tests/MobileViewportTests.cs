using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FamilyTree.E2E.Tests;

// Targets the 6-of-7 mobile CSS bugs from the mobile refinement round (CLAUDE.md's
// "Mobile/Tablet Responsive Pattern" section) that had ZERO automated coverage before
// this branch — they're all real CSS/layout facts (touch target size, drawer height,
// close animation, the ?focus= caret bug) that bUnit structurally cannot see, since it
// diffs a virtual DOM rather than running a real CSS layout engine. This is also
// exactly the category CLAUDE.md flags as a "known gap" in manual testing (the
// browser-automation tooling used for live testing couldn't reliably shrink its own
// viewport) — Playwright's headless Chromium has no such limitation.
public class MobileViewportTests(E2eAppFixture fixture) : E2eTestBase(fixture)
{
    // iPhone 12 Pro dimensions — comfortably under the 768px breakpoint in app.css.
    private static readonly ViewportSize MobileViewport = new() { Width = 390, Height = 844 };

    // A real login POSTs to /auth/do-login, which is rate-limited (5 requests per 15
    // min per IP — a deliberate security feature, not something to weaken for tests).
    // Every test method in this class used to log in independently, which alone burned
    // 4 of that budget in one run and collided with GoldenPathTests/NavigationFlowTests'
    // own logins in the shared collection ("toomany" — confirmed via the fixture's
    // AppLogTail-style diagnostics before this fix). Logging in ONCE and reusing
    // Playwright's storage state (cookies) across every test's own fresh context is the
    // standard fix — each test still gets an isolated browser context, just not an
    // independent real login.
    private static readonly SemaphoreSlim AuthLock = new(1, 1);
    private static string? _cachedStorageState;
    private static Guid _sharedPersonId;

    private async Task<(IPage page, Guid personId)> NewMobilePageAsync()
    {
        if (_cachedStorageState is null)
        {
            await AuthLock.WaitAsync();
            try
            {
                if (_cachedStorageState is null)
                {
                    var familyId = await Fixture.Seeder.CreateFamilyAsync();
                    _sharedPersonId = await Fixture.Seeder.CreatePersonAsync("Mo", "Bile", familyId);
                    var email = $"mobile-test-{Guid.NewGuid():N}@example.com";
                    const string password = "CorrectHorseBatteryStaple9!";
                    await Fixture.Seeder.CreateLinkedUserAsync(email, password, _sharedPersonId, familyId);

                    var loginContext = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
                    {
                        IgnoreHTTPSErrors = true,
                        ViewportSize = MobileViewport,
                    });
                    var loginPage = await loginContext.NewPageAsync();
                    await loginPage.GotoAsync(Fixture.BaseUrl);
                    await loginPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await loginPage.GetByPlaceholder("your@email.com").FillAsync(email);
                    await loginPage.GetByPlaceholder("••••••••••").FillAsync(password);
                    await Expect(loginPage.Locator("input[name='email']")).ToHaveValueAsync(email);
                    await Expect(loginPage.Locator("input[name='password']")).ToHaveValueAsync(password);
                    await loginPage.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
                    await loginPage.WaitForLoadStateAsync(LoadState.NetworkIdle);

                    _cachedStorageState = await loginContext.StorageStateAsync();
                    await loginContext.CloseAsync();
                }
            }
            finally
            {
                AuthLock.Release();
            }
        }

        var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = MobileViewport,
            StorageState = _cachedStorageState,
        });
        var page = await context.NewPageAsync();
        return (page, _sharedPersonId);
    }

    // The drawer's entrance is an overshoot-then-settle bounce (ft-nav-drop, 0.38s) —
    // measuring bounding boxes mid-animation caught transient, smaller-than-final sizes
    // and produced flaky-looking failures unrelated to any real CSS regression. Settle
    // past the animation's own duration before measuring anything.
    private static Task SettleDrawerAnimationAsync() => Task.Delay(450);

    [Fact]
    public async Task MobileDrawer_TouchTargetsMeetMinimumSize()
    {
        var (page, _) = await NewMobilePageAsync();
        await page.GotoAsync(Fixture.BaseUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.Locator(".ft-hamburger-btn").ClickAsync();
        await Expect(page.Locator(".ft-mobile-nav")).ToBeVisibleAsync();
        await SettleDrawerAnimationAsync();

        // WAI-ARIA / Apple HIG minimum touch target — CLAUDE.md documents both
        // .ft-drawer-close (was 27x26px) and .ft-drawer-item rows (was 38px tall) as
        // real bugs found on a real Android device and fixed to be exactly compliant.
        var closeBox = await page.Locator(".ft-drawer-close").BoundingBoxAsync();
        Assert.NotNull(closeBox);
        Assert.True(closeBox!.Width >= 44, $"Close button width {closeBox.Width} < 44px");
        Assert.True(closeBox.Height >= 44, $"Close button height {closeBox.Height} < 44px");

        var itemBox = await page.Locator(".ft-drawer-item").First.BoundingBoxAsync();
        Assert.NotNull(itemBox);
        Assert.True(itemBox!.Height >= 44, $"Drawer item height {itemBox.Height} < 44px");
    }

    [Fact]
    public async Task MobileDrawer_MaxHeightAllowsNearlyFullViewport_NotAFlat55vh()
    {
        var (page, _) = await NewMobilePageAsync();
        await page.GotoAsync(Fixture.BaseUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.Locator(".ft-hamburger-btn").ClickAsync();
        await Expect(page.Locator(".ft-mobile-nav")).ToBeVisibleAsync();
        await SettleDrawerAnimationAsync();

        // max-height is a ceiling, not a forced height — this 7-item nav list naturally
        // renders around 385px regardless of the cap, which is correct (no reason to
        // stretch a short list to fill an 844px screen). The actual guarantee worth
        // testing is the CSS property's *value*, not the rendered box size: the old bug
        // was max-height: 55vh (~464px on this viewport), a real ceiling a longer list
        // would have hit well short of the screen; the fix is calc(100dvh - 76px)
        // (~768px here). Read getComputedStyle directly rather than asserting on
        // content-dependent rendered height.
        var maxHeightPx = await page.Locator(".ft-mobile-nav")
            .EvaluateAsync<string>("el => getComputedStyle(el).maxHeight");
        var value = double.Parse(maxHeightPx.Replace("px", ""));

        Assert.True(value > 700, $"max-height computed to {maxHeightPx}, looks like the old 55vh cap (~464px on this viewport), not calc(100dvh - 76px) (~768px)");
    }

    [Fact]
    public async Task MobileDrawer_ClosesAndIsRemovedFromLayout()
    {
        var (page, _) = await NewMobilePageAsync();
        await page.GotoAsync(Fixture.BaseUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.Locator(".ft-hamburger-btn").ClickAsync();
        await Expect(page.Locator(".ft-mobile-nav")).ToBeVisibleAsync();

        await page.Locator(".ft-drawer-close").ClickAsync();

        // CustomAppBar's two-phase close (apply --closing, delay 250ms, then unmount)
        // means immediately-not-visible isn't enough to prove — wait past the delay and
        // confirm the element is actually gone, not just faded out and forgotten
        // (the exact stuck-overlay failure mode the pointer-events:none safety net and
        // this test both guard against).
        await Expect(page.Locator(".ft-mobile-nav")).Not.ToBeAttachedAsync(new() { Timeout = 2000 });
    }

    [Fact]
    public async Task IdentityCaret_IsTappable_OnAFocusQueryStringUrl()
    {
        var (page, personId) = await NewMobilePageAsync();

        // Regression for the exact bug in CustomAppBarTests.
        // TappingIdentityBlock_TogglesMobilePanel_WhenUrlHasFocusQueryString, confirmed
        // here against a real rendered mobile viewport rather than bUnit's virtual DOM.
        await page.GotoAsync($"{Fixture.BaseUrl}/?focus={personId}");

        // NetworkIdle covers the page load and the SignalR circuit's initial WebSocket
        // upgrade, but not the async work the circuit then does *over* that connection
        // (loading people/couples, resolving Focus into TreeContext) — waiting on the
        // caret itself, which CustomAppBar only renders once CanRevealMobileToolbar is
        // true, is a direct signal of the thing actually under test rather than a
        // fixed delay guessing how long that resolution takes.
        var identityBlock = page.Locator(".ft-appbar-family-title");
        await Expect(identityBlock).ToBeVisibleAsync();
        await Expect(page.Locator(".ft-appbar-caret")).ToBeVisibleAsync();

        await identityBlock.ClickAsync();
        await Expect(page.Locator(".ft-mobile-panel")).ToBeVisibleAsync();
    }
}
