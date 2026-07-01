using Bunit;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class HeroOverlayComponentTests : ComponentTestBase
{
    [Fact]
    public void ShouldShowOverlay_WhenVisible()
    {
        var cut = RenderComponent<HeroOverlayComponent>(p => p
            .Add(x => x.FocusPerson, new PersonDto { FirstName = "Doug" })
            .Add(x => x.PeopleCount, 10)
            .Add(x => x.CoupleCount, 5)
            .Add(x => x.Visible, true)
        );

        cut.Markup.Should().Contain("hero-overlay");
    }
}