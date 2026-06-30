using Microsoft.Playwright;
using Xunit;

namespace FamilyTree.Web.Tests.UiTests;

public class OnboardingToastFlowTests : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        await _browser!.DisposeAsync();
        _playwright!.Dispose();
    }

    [Fact]
    public async Task ShouldShowOnboardingToast_ForNewUser()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("http://localhost:5000/");

        await page.WaitForSelectorAsync("text=Choose a person to center your tree");
    }
}