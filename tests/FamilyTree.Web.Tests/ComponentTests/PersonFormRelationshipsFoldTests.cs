using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// UX/UI review F1 (docs/uxui/, 2026-11-17): Add person put five relationship pickers between the
// name and the notes, so adding someone by name alone meant scrolling past all of them. They are
// folded away until asked for, and open from the start for someone who already has relationships.
public class PersonFormRelationshipsFoldTests : ComponentTestBase
{
    public PersonFormRelationshipsFoldTests()
    {
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForPersonForm());
        RenderComponent<MudPopoverProvider>();
    }

    private static PersonDto Relative(string first) => new()
    {
        Id = Guid.NewGuid(), FirstName = first, LastName = "Test",
        ParentIds = [], ChildIds = [], SpouseIds = [], FormerSpouseIds = [], SiblingIds = [],
    };

    [Fact]
    public void NewPerson_StartsWithTheRelationshipPickersFolded_AndAnAddButton()
    {
        var cut = RenderComponent<PersonForm>(p => p.Add(x => x.AllPeople, [Relative("Ann")]));

        cut.FindAll(".ft-relationships-grid").Should().BeEmpty();
        cut.Markup.Should().NotContain("Spouses / partners");
        cut.Find(".ft-relationships-toggle").TextContent.Trim().Should().Be("Add relationships");
        cut.Markup.Should().Contain("Optional");
    }

    [Fact]
    public async Task AddRelationships_OpensThePickers_AndHideFoldsThemAgain()
    {
        var cut = RenderComponent<PersonForm>(p => p.Add(x => x.AllPeople, [Relative("Ann")]));

        await ClickAsync(cut.Find(".ft-relationships-toggle"));

        cut.FindAll(".ft-relationships-grid").Should().HaveCount(1);
        foreach (var label in new[] { "Parents", "Spouses / partners", "Former partners", "Siblings", "Children" })
            cut.Markup.Should().Contain(label);
        cut.Find(".ft-relationships-toggle").TextContent.Trim().Should().Be("Hide");

        await ClickAsync(cut.Find(".ft-relationships-toggle"));

        cut.FindAll(".ft-relationships-grid").Should().BeEmpty();
    }

    [Fact]
    public void PersonWithRelationships_OpensWithThePickersShown()
    {
        var mum = Relative("Mum");
        var child = Relative("Kid");
        var initial = Relative("Self");
        initial.ParentIds = [mum.Id];

        var cut = RenderComponent<PersonForm>(p => p
            .Add(x => x.AllPeople, [mum, child, initial])
            .Add(x => x.Initial, FamilyTree.Core.Mappers.PersonMapper.ToUpsertDto(initial))
            .Add(x => x.EditingId, initial.Id));

        cut.FindAll(".ft-relationships-grid").Should().HaveCount(1);
        cut.Find(".ft-relationships-toggle").TextContent.Trim().Should().Be("Hide");
    }

    [Fact]
    public async Task FoldedWithConnections_SaysHowManyWereAdded()
    {
        var mum = Relative("Mum");
        var initial = Relative("Self");
        initial.ParentIds = [mum.Id];
        var cut = RenderComponent<PersonForm>(p => p
            .Add(x => x.AllPeople, [mum, initial])
            .Add(x => x.Initial, FamilyTree.Core.Mappers.PersonMapper.ToUpsertDto(initial))
            .Add(x => x.EditingId, initial.Id));

        await ClickAsync(cut.Find(".ft-relationships-toggle"));

        cut.Find(".ft-relationships-hint").TextContent.Trim().Should().Be("1 connection added");
        cut.Find(".ft-relationships-toggle").TextContent.Trim().Should().Be("Show relationships");
    }
}
