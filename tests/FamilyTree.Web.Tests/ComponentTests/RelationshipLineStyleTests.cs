using Bunit;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FamilyTree.Web.Services;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Found live 2026-10-24: an ended marriage and a sibling link were both drawn as grey 4-3
// dashes, so on Doug's tree Ray hung from what read as a sibling line and four dashed lines
// around Louis couldn't be told apart. Each kind now has its own look, on the canvas and in
// the SVG export.
public class RelationshipLineStyleTests : ComponentTestBase
{
    // Ann's marriage to Ben ended; Cal is Ann's brother with no parents on the tree.
    private static List<PersonDto> People()
    {
        var (ann, ben, cal) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        return
        [
            new() { Id = ann, FirstName = "Ann", LastName = "Test", FormerSpouseIds = [ben], SiblingIds = [cal] },
            new() { Id = ben, FirstName = "Ben", LastName = "Test", FormerSpouseIds = [ann] },
            new() { Id = cal, FirstName = "Cal", LastName = "Test", SiblingIds = [ann] },
        ];
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void Canvas_DrawsAnEndedMarriageAndASiblingLinkDifferently()
    {
        JSInterop.SetupModule("/js/canvas-interaction.js?v=2").SetupVoid("init", _ => true);
        var people = People();
        var cut = RenderComponent<FamilyTreeCanvas>(p => p
            .Add(x => x.People, people)
            .Add(x => x.Couples, CoupleHelper.Derive(people))
            .Add(x => x.FocusId, people[0].Id));

        var former = cut.Find(".ft-line-former");
        former.TextContent.Should().Contain("💔");
        var formerDash = former.QuerySelector("path")!.GetAttribute("stroke-dasharray");

        var sibling = cut.Find(".ft-line-sibling path");
        sibling.GetAttribute("stroke-linecap").Should().Be("round");
        sibling.GetAttribute("stroke-dasharray").Should().NotBe(formerDash);
    }

    [Fact]
    public void Export_DrawsTheSiblingLink_AndABrokenHeartOnTheEndedMarriage()
    {
        var people = People();
        var svg = new SvgExportService(new FamilyTreeLayoutEngine()).GenerateSvg(people, people[0], "Test");

        svg.Should().Contain("&#x1F494;", "the ended marriage gets a broken heart");
        svg.Should().Contain("stroke-dasharray=\"0.1 4\"", "the sibling link is drawn, dotted");
        svg.Should().Contain("stroke-dasharray=\"6 4\"", "the ended marriage keeps its own dashes");
    }
}
