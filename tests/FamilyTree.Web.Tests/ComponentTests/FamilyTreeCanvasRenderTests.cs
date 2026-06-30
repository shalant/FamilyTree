using Bunit;
using FluentAssertions;
using Xunit;
using FamilyTree.Web.Modules.Components;   // for HeroOverlayComponent, FamilyTreeCanvas, PersonDetailDrawer

namespace FamilyTree.Web.Tests.ComponentTests;

public class FamilyTreeCanvasRenderTests : ComponentTestBase
{
    [Fact]
    public void ShouldRenderCanvas()
    {
        var cut = RenderComponent<FamilyTreeCanvas>();

        cut.Markup.Should().Contain("canvas");
    }
}