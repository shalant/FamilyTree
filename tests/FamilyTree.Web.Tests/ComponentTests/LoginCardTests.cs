using Bunit;
using Bunit.TestDoubles;
using FamilyTree.Web.Layout;
using FamilyTree.Web.Modules.Components;
using FamilyTree.Core.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// UX/UI review #11, #12 and L1 (docs/uxui/, 2026-11-18): the sign-in card offered "Create an account"
// to people with no invitation (it led to an error), a password placeholder of ten bullets that read as
// an already-filled field, and a blurred ghost of the app bar showed behind it.
public class LoginCardTests : ComponentTestBase
{
    public LoginCardTests()
    {
        Services.AddSingleton<IPersonService>(new FakePersonService());
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public void SignInCard_DoesNotOfferToCreateAnAccount_ButSaysJoiningIsByInvitation()
    {
        var cut = RenderComponent<LoginOverlay>();

        cut.FindAll("a").Should().NotContain(a => a.GetAttribute("href") == "/register");
        cut.Markup.Should().NotContain("Create an account");
        cut.Find(".auth-invite-note").TextContent.Should().Contain("by invitation").And.Contain("invitation email");
        cut.Find("a[href='/forgot-password']").Should().NotBeNull();
    }

    [Fact]
    public void PasswordField_HasAWordPlaceholder_NotBullets()
    {
        var cut = RenderComponent<LoginOverlay>();

        var password = cut.FindAll("input").Single(i => i.GetAttribute("type") == "password");
        password.GetAttribute("placeholder").Should().Be("Your password");
    }
}
