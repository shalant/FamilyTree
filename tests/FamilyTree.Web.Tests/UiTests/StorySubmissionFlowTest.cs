using Microsoft.Playwright;
using Xunit;

namespace FamilyTree.Web.Tests.UiTests;

public class StorySubmissionFlowTests : IAsyncLifetime
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

    [Fact(Skip = "Requires running server on localhost:5000")]
    public async Task ShouldSubmitStorySuccessfully()
    {
        var page = await _browser!.NewPageAsync();

        // IMPORTANT: Use full URL for Playwright
        await page.GotoAsync("http://localhost:5000/story/respond/testtoken");

        await page.FillAsync("textarea[placeholder='Share what you remember...']", "A great memory");
        await page.ClickAsync("text=Share this memory");

        await page.WaitForSelectorAsync("text=Thank you");
    }
}