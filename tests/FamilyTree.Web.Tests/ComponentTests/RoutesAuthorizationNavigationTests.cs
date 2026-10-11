using Bunit;
using Bunit.TestDoubles;
using FamilyTree.Core.Services;
using FamilyTree.Web.App;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class RoutesAuthorizationNavigationTests : ComponentTestBase
{
    public RoutesAuthorizationNavigationTests()
    {
        Services.AddSingleton<IPersonService>(new FakePersonService());
        this.AddTestAuthorization(); // signed out
    }

    // Found live 2026-10-11 during the production smoke test: signed out, on the public
    // /faq page, clicking "Go to dashboard" left the URL at /dashboard with the FAQ still
    // on screen and a red error banner. Routes.razor wrapped the NotAuthorized content in
    // its own <LayoutView Layout=MainLayout>, but AuthorizeRouteView already renders that
    // content inside DefaultLayout (also MainLayout), so the result was a MainLayout nested
    // in a MainLayout — two MudPopoverProviders — and MudBlazor threw "There is already a
    // subscriber to the content with the given section ID 'mud-overlay-to-popover-provider'",
    // which killed the circuit. Navigating from one MainLayout page into a protected one is
    // the only path that exercises it.
    [Fact]
    [Trait("Category", "Regression")]
    public void SignedOut_NavigatingFromPublicPageToProtectedPage_ShowsAccessDeniedWithoutNestingLayouts()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/faq");
        var cut = RenderComponent<Routes>();
        cut.Markup.Should().Contain("Frequently asked questions");

        cut.InvokeAsync(() => nav.NavigateTo("/dashboard"));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Access denied"));
        cut.FindAll("#ft-appbar").Count.Should().Be(1, "the access-denied card must not be wrapped in a second MainLayout");
    }
}
