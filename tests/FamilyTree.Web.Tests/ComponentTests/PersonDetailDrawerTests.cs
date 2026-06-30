using Bunit;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;   // for HeroOverlayComponent, FamilyTreeCanvas, PersonDetailDrawer
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class PersonDetailDrawerTests : ComponentTestBase
{
    public PersonDetailDrawerTests()
    {
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<FamilyTreeLayoutEngine>();
    }

    [Fact]
    public void ShouldRenderPersonName()
    {
        var person = new PersonDto { FirstName = "Douglas" };

        var cut = RenderComponent<PersonDetailDrawer>(p => p.Add(x => x.Person, person));

        cut.Markup.Should().Contain("Douglas");
    }
}