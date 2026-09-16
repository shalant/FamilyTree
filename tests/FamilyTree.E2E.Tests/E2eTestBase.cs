using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Base for E2E test classes. xUnit v2 (used throughout this repo) has no built-in
/// dynamic test skip, so an unavailable fixture (E2E database unreachable) fails loudly
/// with a clear message instead — matching this codebase's stated testing philosophy
/// (TestInfrastructure.cs: fail loudly rather than silently returning a default) over
/// a silently-passing test that would give false confidence about coverage that never
/// actually ran.
/// </summary>
[Collection("E2E")]
public abstract class E2eTestBase
{
    protected readonly E2eAppFixture Fixture;

    protected E2eTestBase(E2eAppFixture fixture)
    {
        Fixture = fixture;
        if (!fixture.IsAvailable)
            throw new InvalidOperationException(
                $"E2E fixture unavailable — {fixture.SkipReason}");
    }

    // Ignores HTTPS errors defensively — the local dev cert is trusted system-wide on
    // this machine (dotnet dev-certs https --trust), but that shouldn't be a hard
    // requirement for a CI runner or another contributor's machine to run these tests.
    protected async Task<IPage> NewPageAsync()
    {
        var context = await NewContextAsync();
        return await context.NewPageAsync();
    }

    protected Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions? options = null) =>
        Fixture.Browser.NewContextAsync(options ?? new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
}
