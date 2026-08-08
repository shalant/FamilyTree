using Bunit;
using FluentAssertions;
using Xunit;
using FamilyTree.Web.Modules.Components;

namespace FamilyTree.Web.Tests.ComponentTests;

public class FamilyTreeCanvasRenderTests : ComponentTestBase
{
    [Fact]
    public void ShouldRenderCanvas()
    {
        JSInterop.SetupModule("/js/canvas-interaction.js?v=2").SetupVoid("init", _ => true);
        var cut = RenderComponent<FamilyTreeCanvas>();

        cut.Markup.Should().Contain("ft-viewport");
    }
}