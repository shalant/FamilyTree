using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Outreach readiness A3 (2026-10-25): PersonForm asks "are you sure?" about implausible
// dates and blocks impossible ones, using the same PlausibilityRules as the server.
public class PersonFormPlausibilityTests : ComponentTestBase
{
    private readonly List<PersonUpsertDto> _submitted = [];

    private static readonly PersonDto Mother = new()
    {
        Id = Guid.NewGuid(), FirstName = "Mother", LastName = "Test", Gender = Gender.Female,
        BirthDate = new DateOnly(1920, 1, 1), DeathDate = new DateOnly(1949, 1, 1),
        ParentIds = [], ChildIds = [], SpouseIds = [], FormerSpouseIds = [], SiblingIds = [],
    };

    private static readonly PersonDto YoungFather = new()
    {
        Id = Guid.NewGuid(), FirstName = "Father", LastName = "Test", Gender = Gender.Male,
        BirthDate = new DateOnly(1940, 1, 1),
        ParentIds = [], ChildIds = [], SpouseIds = [], FormerSpouseIds = [], SiblingIds = [],
    };

    public PersonFormPlausibilityTests()
    {
        Services.AddSingleton<IMediumService>(new NoPhotosMediumService());
        RenderComponent<MudPopoverProvider>();
    }

    private IRenderedComponent<PersonForm> Render(PersonUpsertDto initial, Guid? editingId = null) =>
        RenderComponent<PersonForm>(p => p
            .Add(x => x.Initial, initial)
            .Add(x => x.EditingId, editingId)
            .Add(x => x.AllPeople, [Mother, YoungFather])
            .Add(x => x.OnSubmit, EventCallback.Factory.Create<PersonUpsertDto>(this, d => _submitted.Add(d))));

    private static PersonUpsertDto Kid(params Guid[] parents) => new()
    {
        FirstName = "Kid", LastName = "Test", BirthDate = new DateOnly(1950, 6, 15), ParentIds = [.. parents],
    };

    private Task ClickSave(IRenderedComponent<PersonForm> cut, string label) =>
        ClickAsync(cut.FindAll("button").First(b => b.TextContent.Trim() == label));

    [Fact]
    public async Task ImplausibleDates_AskFirst_ThenSaveAnywaySaves()
    {
        var cut = Render(Kid(YoungFather.Id));

        await ClickSave(cut, "Save");

        _submitted.Should().BeEmpty("the first press only shows the warning");
        cut.Find(".ft-plausibility-warnings").TextContent.Should().Contain("Father Test would have been 10");

        await ClickSave(cut, "Save anyway");

        _submitted.Should().ContainSingle();
    }

    [Fact]
    public async Task ImpossibleDates_BlockTheSave_WithNoSaveAnyway()
    {
        var cut = Render(Kid(Mother.Id));

        await ClickSave(cut, "Save");
        await ClickSave(cut, "Save");

        _submitted.Should().BeEmpty();
        cut.Find(".ft-plausibility-errors").TextContent.Should().Contain("can't be Kid Test's mother");
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Trim() == "Save anyway");
    }

    [Fact]
    public async Task ImpossibleLinkAlreadySaved_IsOnlyAWarningOnAnUnrelatedEdit()
    {
        // The server judges only links a save adds (or all, if this person's dates change),
        // so the form must not block what the server would allow.
        var cut = Render(Kid(Mother.Id), editingId: Guid.NewGuid());

        await ClickSave(cut, "Save");
        cut.FindAll(".ft-plausibility-errors").Should().BeEmpty();
        cut.Find(".ft-plausibility-warnings").TextContent.Should().Contain("can't be Kid Test's mother");

        await ClickSave(cut, "Save anyway");
        _submitted.Should().ContainSingle();
    }

    [Fact]
    public async Task PlausibleDates_SaveFirstTime()
    {
        var cut = Render(new PersonUpsertDto { FirstName = "Kid", LastName = "Test", BirthDate = new DateOnly(1970, 1, 1) });

        await ClickSave(cut, "Save");

        _submitted.Should().ContainSingle();
        cut.FindAll(".ft-plausibility-warnings").Should().BeEmpty();
    }
}
