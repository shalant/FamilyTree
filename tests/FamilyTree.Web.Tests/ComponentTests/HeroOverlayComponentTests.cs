using Bunit;
using FluentAssertions;
using Xunit;
using FamilyTree.Web.Modules.Components;   // for HeroOverlayComponent, FamilyTreeCanvas, PersonDetailDrawer

namespace FamilyTree.Web.Tests.ComponentTests;

public class HeroOverlayComponentTests : ComponentTestBase
{
    [Fact]
    public void ShouldShowOverlay_WhenVisible()
    {
        var cut = RenderComponent<HeroOverlayComponent>(p => p.Add(x => x.Visible, true));

        cut.Markup.Should().Contain("hero-overlay");
    }
}