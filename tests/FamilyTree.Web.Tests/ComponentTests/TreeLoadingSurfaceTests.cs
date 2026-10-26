using Bunit;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class TreeLoadingSurfaceTests : ComponentTestBase
{
    // Home.razor and App.razor's pre-circuit splash both render this, so the wait before and
    // after the circuit connects looks like one. It has to stay static markup (no services,
    // no JS) to render in App.razor's non-interactive shell.
    [Fact]
    public void Renders_AsAnAnnouncedStatus_WithTheSketchAndAMessage()
    {
        var cut = RenderComponent<TreeLoadingSurface>();

        cut.Find(".ft-loading-surface").GetAttribute("role").Should().Be("status");
        cut.Find(".ft-loading-text").TextContent.Should().Be("Opening your tree");
        cut.FindAll(".ft-loading-nodes circle").Should().NotBeEmpty();
        cut.Find("svg").GetAttribute("aria-hidden").Should().Be("true");
    }
}
