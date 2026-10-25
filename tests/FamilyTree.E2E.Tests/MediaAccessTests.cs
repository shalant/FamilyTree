using Microsoft.Playwright;
using Xunit;

namespace FamilyTree.E2E.Tests;

// S8 (docs/security.md, 2026-10-25): media are served by the app at /media/{blob}, only
// to a signed-in family member, so the storage container can be private. Which family
// may see what is covered by MediaAccessServiceTests and CrossTenantMatrixTests; this
// checks the real pipeline: a signed-out request is sent to sign in, never served.
// (Uploading needs blob storage, which the E2E run doesn't have.)
public class MediaAccessTests(E2eAppFixture fixture) : E2eTestBase(fixture)
{
    [Fact]
    public async Task SignedOutRequest_IsSentToSignIn()
    {
        await using var api = await Fixture.Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = Fixture.BaseUrl,
            IgnoreHTTPSErrors = true,
        });

        var response = await api.GetAsync("/media/photo-00000000-0000-0000-0000-000000000000.jpg",
            new APIRequestContextOptions { MaxRedirects = 0 });

        Assert.Equal(302, response.Status);
        Assert.Contains("ReturnUrl=%2Fmedia", response.Headers["location"]);
    }

    [Fact]
    public async Task UnknownInviteToken_IsNotFound()
    {
        await using var api = await Fixture.Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = Fixture.BaseUrl,
            IgnoreHTTPSErrors = true,
        });

        var response = await api.GetAsync("/media/invite/no-such-token",
            new APIRequestContextOptions { MaxRedirects = 0 });

        Assert.Equal(404, response.Status);
    }
}
