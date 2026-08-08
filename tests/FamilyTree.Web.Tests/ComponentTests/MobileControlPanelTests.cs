using Bunit;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class MobileControlPanelTests : ComponentTestBase
{
    [Fact]
    public void Hidden_WhenMobilePanelClosed()
    {
        var cut = RenderComponent<MobileControlPanel>();

        cut.Markup.Should().BeEmpty();
    }

    [Fact]
    public void Open_RendersFocusPersonInfoAndStats()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
        treeContext.SetContext(new PersonDto { FirstName = "Doug", LastName = "Rosenberg" }, 12, 4);
        treeContext.ToggleMobilePanel();

        var cut = RenderComponent<MobileControlPanel>();

        cut.Markup.Should().Contain("Doug Rosenberg");
        cut.Markup.Should().Contain("12 people");
        cut.Markup.Should().Contain("4 couples");
    }

    // Zoom in/out are repeatable actions — the panel should stay open so a
    // user can tap either several times in a row.
    [Fact]
    public void ZoomButtons_RequestZoom_AndKeepPanelOpen()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
        treeContext.ToggleMobilePanel();
        var zoomOutRaised = false;
        var zoomInRaised = false;
        treeContext.OnZoomOutRequested += () => zoomOutRaised = true;
        treeContext.OnZoomInRequested += () => zoomInRaised = true;

        var cut = RenderComponent<MobileControlPanel>();
        var buttons = cut.FindAll(".ft-mobile-panel-btn");
        buttons[0].Click(); // Zoom out
        buttons[1].Click(); // Zoom in

        zoomOutRaised.Should().BeTrue();
        zoomInRaised.Should().BeTrue();
        treeContext.MobilePanelOpen.Should().BeTrue();
    }

    // Center/Reset are one-shot actions — the panel should close afterward,
    // matching the previous Center-tree-only incarnation's behavior.
    [Fact]
    public void CenterButton_RequestsCenterTree_AndClosesPanel()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
        treeContext.ToggleMobilePanel();
        var raised = false;
        treeContext.OnCenterTreeRequested += () => raised = true;

        var cut = RenderComponent<MobileControlPanel>();
        cut.FindAll(".ft-mobile-panel-btn")[2].Click(); // Center

        raised.Should().BeTrue();
        treeContext.MobilePanelOpen.Should().BeFalse();
    }

    [Fact]
    public void ResetButton_RequestsResetView_AndClosesPanel()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
        treeContext.ToggleMobilePanel();
        var raised = false;
        treeContext.OnResetViewRequested += () => raised = true;

        var cut = RenderComponent<MobileControlPanel>();
        cut.FindAll(".ft-mobile-panel-btn")[3].Click(); // Reset

        raised.Should().BeTrue();
        treeContext.MobilePanelOpen.Should().BeFalse();
    }

    [Fact]
    public void BackdropTap_ClosesPanel()
    {
        var treeContext = Services.GetRequiredService<TreeContextService>();
        treeContext.ToggleMobilePanel();

        var cut = RenderComponent<MobileControlPanel>();
        cut.Find(".ft-mobile-panel-backdrop").Click();

        treeContext.MobilePanelOpen.Should().BeFalse();
    }
}
