using Bunit;
using FamilyTree.Web.Modules.Admin.Components;
using FamilyTree.Web.Modules.Admin.Pages;
using FluentAssertions;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Asked 2026-11-27: Admin tabs load once, so seeing a new invite or problem meant reloading
// the whole page. Refresh rebuilds the showing tab, which loads its data again.
public class AdminRefreshTests : ComponentTestBase
{
    [Fact]
    public async Task Refresh_StartsTheShowingTabOver()
    {
        ComponentFactories.AddStub<DashboardTab>();
        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<Admin>();
        var before = cut.FindComponent<Bunit.TestDoubles.Stub<DashboardTab>>().Instance;

        await ClickAsync(cut.Find("button.ft-admin-refresh"));

        cut.FindComponent<Bunit.TestDoubles.Stub<DashboardTab>>().Instance.Should().NotBeSameAs(before);
    }
}
