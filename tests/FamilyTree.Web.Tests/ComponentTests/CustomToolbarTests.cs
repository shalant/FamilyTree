using Bunit;
using FamilyTree.Web.Modules.Components;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class CustomToolbarTests : ComponentTestBase
{
    public CustomToolbarTests()
    {
        // MudTooltip resolves popovers through the shared PopoverService — it throws
        // "Missing <MudPopoverProvider />" unless one has rendered first.
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public void ForceCollapsed_RendersMiniPill_NotFullToolbar()
    {
        var cut = RenderComponent<CustomToolbar>(p => p.Add(x => x.ForceCollapsed, true));

        cut.Markup.Should().Contain("ft-toolbar-mini");
        cut.Markup.Should().NotContain("ft-toolbar-full");
    }

    // On mobile (ForceCollapsed), the mini pill's expand arrow has no inline
    // toolbar to grow into — it opens MobileControlPanel via TreeContextService
    // instead, rather than the desktop-only local _collapsed toggle.
    [Fact]
    public void ForceCollapsed_ExpandArrow_OpensMobilePanel_InsteadOfExpandingInline()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
        var cut = RenderComponent<CustomToolbar>(p => p.Add(x => x.ForceCollapsed, true));

        treeContext.MobilePanelOpen.Should().BeFalse();

        cut.Find(".ft-toolbar-mini .mud-icon-button:last-child").Click();

        treeContext.MobilePanelOpen.Should().BeTrue();
        cut.Markup.Should().Contain("ft-toolbar-mini");
        cut.Markup.Should().NotContain("ft-toolbar-full");
    }

    [Fact]
    public void NotForceCollapsed_RendersFullToolbar_ByDefault()
    {
        var cut = RenderComponent<CustomToolbar>(p => p.Add(x => x.ForceCollapsed, false));

        cut.Markup.Should().Contain("ft-toolbar-full");
    }

    [Fact]
    public void NotForceCollapsed_CollapseThenExpand_TogglesLocalStateOnly()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
        var cut = RenderComponent<CustomToolbar>(p => p.Add(x => x.ForceCollapsed, false));

        cut.FindAll(".ft-toolbar-full .mud-icon-button").Last().Click(); // collapse
        cut.Markup.Should().Contain("ft-toolbar-mini");

        cut.Find(".ft-toolbar-mini .mud-icon-button:last-child").Click(); // expand
        cut.Markup.Should().Contain("ft-toolbar-full");
        treeContext.MobilePanelOpen.Should().BeFalse();
    }
}
