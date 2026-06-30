using Bunit;
using FamilyTree.Shared.DTOs.Person;
using FluentAssertions;
using Xunit;
using FamilyTree.Web.Modules.Components;   // for HeroOverlayComponent, FamilyTreeCanvas, PersonDetailDrawer

namespace FamilyTree.Web.Tests.ComponentTests;

public class PersonDetailDrawerTests : TestContext
{
    [Fact]
    public void ShouldRenderPersonName()
    {
        var person = new PersonDto { FirstName = "Douglas" };

        var cut = RenderComponent<PersonDetailDrawer>(p => p.Add(x => x.Person, person));

        cut.Markup.Should().Contain("Douglas");
    }
}