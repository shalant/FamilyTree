using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FamilyTree.Web.Tests.LayoutQuality;

// B8 (2026-10-27): the Data check must stay quiet on families that are fine, or nobody will
// read it. The checked-in layout fixtures are made-up but realistic families (private live
// exports are left out: their findings are real, and that's what the tab is for).
//
// Fixtures carry birth dates only, so anyone born over 110 years ago "would be N today"; that
// one rule is expected and filtered out here.
public class DataCheckFixtureTests(ITestOutputHelper output)
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    public static IEnumerable<object[]> CheckedInFixtures() =>
        Directory.GetFiles(LayoutFixture.FixturesDirectory, "*.json").Order()
            .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });

    [Theory, MemberData(nameof(CheckedInFixtures))]
    public void CheckedInFixture_HasNoDataCheckFindings(string name)
    {
        var fixture = LayoutFixture.LoadNamed(name);
        var people = fixture.People.Select(p => new Person
        {
            Id = p.Id, FirstName = p.FirstName, LastName = p.LastName, BirthDate = p.BirthDate,
        }).ToList();
        var links = fixture.Relationships.Select(r => new Relationship
        {
            Id = Guid.NewGuid(), PersonAId = r.PersonAId, PersonBId = r.PersonBId, Type = r.Type,
            EndDate = r.EndDate, IsEnded = r.IsEnded,
        }).ToList();

        var findings = DataCheckRules.Find(people, links, Today)
            .Where(f => !(f.Rule == DataCheckRule.Dates && f.Message.Contains("today. If they have died")))
            .ToList();

        foreach (var f in findings) output.WriteLine($"{f.Rule}: {f.Message}");
        findings.Should().BeEmpty();
    }
}
