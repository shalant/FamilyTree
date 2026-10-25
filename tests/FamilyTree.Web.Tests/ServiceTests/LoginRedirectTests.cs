using Bunit;
using FamilyTree.Web.Modules.Components;
using FamilyTree.Web.Services;
using FamilyTree.Web.Tests.ComponentTests;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

// S9 (docs/security.md): /auth/do-login ignored ?ReturnUrl=, so a signed-out visit to
// /admin landed on the tree after sign-in (found 2026-10-11, smoke test). Honouring it
// must not open a redirect to another site.
public class LoginRedirectTests
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/people?status=living")]
    [InlineData("/person/0b5e/edit#photos")]
    public void LocalPath_IsFollowed(string returnUrl) =>
        LoginRedirect.SafeReturnUrl(returnUrl).Should().Be(returnUrl);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/admin")]
    [InlineData("//evil.example/admin")]
    [InlineData("/\\evil.example/admin")]
    [InlineData("evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/admin\r\nSet-Cookie: x=1")]
    [InlineData("/auth/do-logout")]
    [InlineData("/AUTH/google")]
    [InlineData("/")]
    [InlineData("/?loginError=invalid")]
    public void AnythingElse_IsIgnored(string? returnUrl) =>
        LoginRedirect.SafeReturnUrl(returnUrl).Should().BeNull();

    [Fact]
    public void AfterSignIn_ReturnUrlWinsOverFocus()
    {
        var focus = Guid.NewGuid();

        LoginRedirect.AfterSignIn("/admin", focus).Should().Be("/admin");
        LoginRedirect.AfterSignIn("//evil.example", focus).Should().Be($"/?focus={focus}");
        LoginRedirect.AfterSignIn(null, null).Should().Be("/");
    }

    [Fact]
    public void LoginError_KeepsASafeReturnUrlForTheNextAttempt()
    {
        LoginRedirect.LoginError("invalid", "/people?status=living")
            .Should().Be("/?loginError=invalid&ReturnUrl=%2Fpeople%3Fstatus%3Dliving");
        LoginRedirect.LoginError("invalid", "https://evil.example")
            .Should().Be("/?loginError=invalid");
    }
}

public class LoginOverlayReturnUrlTests : ComponentTestBase
{
    [Fact]
    [Trait("Category", "Regression")]
    public void ReturnUrl_IsPostedWithTheSignInForm()
    {
        var cut = RenderComponent<LoginOverlay>(p => p.Add(x => x.ReturnUrl, "/admin"));

        cut.Find("form[action='/auth/do-login'] input[name='returnUrl']")
            .GetAttribute("value").Should().Be("/admin");
    }

    [Fact]
    public void NoReturnUrl_PostsNoReturnField()
    {
        var cut = RenderComponent<LoginOverlay>();

        cut.FindAll("input[name='returnUrl']").Should().BeEmpty();
    }
}
