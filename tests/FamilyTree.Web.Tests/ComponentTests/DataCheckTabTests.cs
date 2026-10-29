using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Admin;
using FamilyTree.Web.Modules.Admin.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// B8 (2026-10-27): the Data check tab lists findings by rule, marks the impossible ones,
// and links each person to their edit page. It changes nothing itself.
public class DataCheckTabTests : ComponentTestBase
{
    private static readonly Guid MaxId = Guid.NewGuid();
    private static readonly Guid BillId = Guid.NewGuid();
    private static readonly Guid WilliamId = Guid.NewGuid();

    private readonly FakeDataCheckService _service = new();

    public DataCheckTabTests() => Services.AddSingleton<IDataCheckService>(_service);

    private static DataCheckFindingDto Finding(string rule, bool impossible, string message, string family, params (Guid Id, string Name)[] people) => new()
    {
        Rule = rule, IsImpossible = impossible, Message = message, FamilyName = family, FamilyId = Guid.Empty,
        People = [.. people.Select(p => new DataCheckPersonDto { PersonId = p.Id, Name = p.Name })],
    };

    [Fact]
    public void Findings_AreGroupedByRule_WithLinksToEachPersonsEditPage()
    {
        _service.Result = ServiceResponse<List<DataCheckFindingDto>>.Ok(
        [
            Finding("Dates", true, "Max Fine: Birth date must be before death date.", "Ours", (MaxId, "Max Fine")),
            Finding("Duplicate", false, "Bill Small and William Small may be the same person.", "Ours",
                (BillId, "Bill Small"), (WilliamId, "William Small")),
        ]);

        var cut = RenderComponent<DataCheckTab>();

        cut.Markup.Should().Contain("A person's dates (1)").And.Contain("Possible duplicates (1)");
        cut.Markup.Should().Contain("Can't be right").And.Contain("Probably wrong");
        var hrefs = cut.FindAll("a").Select(a => a.GetAttribute("href")).ToList();
        hrefs.Should().Contain([$"/people/{MaxId}/edit", $"/people/{BillId}/edit", $"/people/{WilliamId}/edit"]);
    }

    [Fact]
    public void NoFindings_SaysSo()
    {
        _service.Result = ServiceResponse<List<DataCheckFindingDto>>.Ok([]);
        var cut = RenderComponent<DataCheckTab>();
        cut.Markup.Should().Contain("no likely slips found");
    }

    [Fact]
    public void FamilyHeadings_ShowOnlyWhenThereIsMoreThanOneFamily()
    {
        var one = Finding("Dates", true, "A", "Family One", (MaxId, "Max Fine"));
        var two = Finding("Dates", true, "B", "Family Two", (BillId, "Bill Small"));
        two.FamilyId = Guid.NewGuid();

        _service.Result = ServiceResponse<List<DataCheckFindingDto>>.Ok([one]);
        RenderComponent<DataCheckTab>().FindAll("h6").Should().BeEmpty();

        _service.Result = ServiceResponse<List<DataCheckFindingDto>>.Ok([one, two]);
        RenderComponent<DataCheckTab>().FindAll("h6").Select(h => h.TextContent.Trim())
            .Should().Equal("Family One", "Family Two");
    }

    [Fact]
    public void CheckAgain_RunsTheCheckAgain()
    {
        _service.Result = ServiceResponse<List<DataCheckFindingDto>>.Ok([]);
        var cut = RenderComponent<DataCheckTab>();

        cut.FindAll("button").First(b => b.TextContent.Contains("Check again")).Click();

        _service.Calls.Should().Be(2);
    }
}

class FakeDataCheckService : IDataCheckService
{
    public ServiceResponse<List<DataCheckFindingDto>> Result { get; set; } = ServiceResponse<List<DataCheckFindingDto>>.Ok([]);
    public int Calls { get; private set; }

    public Task<ServiceResponse<List<DataCheckFindingDto>>> GetFindingsAsync(CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}
