using Bunit;
using Bunit.TestDoubles;
using FamilyTree.Core.Services;
using FamilyTree.Web.Layout;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// UX/UI review L1 (docs/uxui/, 2026-11-18): signed out, the tree page is only the sign-in card, but
// the app bar (logo, search, avatar) showed as a blurred ghost behind it.
public abstract class TreePageAppBarTestsBase : ComponentTestBase
{
    protected TreePageAppBarTestsBase(bool signedIn)
    {
        Services.AddSingleton<IPersonService>(new FakePersonService());
        var auth = this.AddTestAuthorization();
        if (signedIn) auth.SetAuthorized("Test User");
        // TreeLayout renders its own MudPopoverProvider; a second one throws.
    }

    protected IRenderedComponent<TreeLayout> RenderLayout() =>
        RenderComponent<TreeLayout>(p => p.Add(x => x.Body,
            (RenderFragment)(b => b.AddMarkupContent(0, "<p id='page'>x</p>"))));
}

public class TreePageAppBarSignedOutTests() : TreePageAppBarTestsBase(signedIn: false)
{
    [Fact]
    public void NoAppBarBehindTheSignInCard()
    {
        var cut = RenderLayout();

        cut.FindAll("#ft-appbar").Should().BeEmpty();
        cut.FindAll("#page").Should().HaveCount(1);
    }
}

public class TreePageAppBarSignedInTests() : TreePageAppBarTestsBase(signedIn: true)
{
    [Fact]
    public void SignedInUsersStillGetTheAppBar()
    {
        var cut = RenderLayout();

        cut.FindAll("#ft-appbar").Should().HaveCount(1);
    }
}
