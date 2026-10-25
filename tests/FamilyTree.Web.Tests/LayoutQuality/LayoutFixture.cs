using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyTree.Core.Mappers;
using FamilyTree.Core.Models;
using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using FamilyTree.Web.Services;

namespace FamilyTree.Web.Tests.LayoutQuality;

// A family as stored: Person + Relationship rows, in the JSON shape
// scripts/export-layout-fixture.sql writes. Loaded through PersonMapper and CoupleHelper,
// the same path PersonService.GetAllAsync and FamilyTreeCanvas use, so a live export lays
// out exactly as it does on the site (ADR 004, step 1).
//
// Checked-in fixtures live in LayoutQuality/Fixtures. Live exports go in
// LayoutQuality/Fixtures/private, which is gitignored: the repo is public and a live
// export holds real names and birth dates.
//
// Optional "focus" and "anchor" (person ids) match what the site passes: focus sets node
// sizes, anchor is the signed-in user's own person, which decides which lineage the
// archived engine draws on which side. Without an anchor a fixture can lay out mirrored
// from what that user sees.
public sealed record LayoutFixture(
    string? Name,
    Guid? Focus,
    Guid? Anchor,
    List<LayoutFixture.FixturePerson> People,
    List<LayoutFixture.FixtureRelationship> Relationships)
{
    public sealed record FixturePerson(Guid Id, string FirstName, string LastName, DateOnly? BirthDate);
    // IsEnded (added 2026-10-25, B7 step 0): a marriage ended with no known date, as A1 stores it.
    // Optional, so exports from before it existed still load.
    public sealed record FixtureRelationship(Guid PersonAId, Guid PersonBId, RelationshipType Type, DateOnly? EndDate, bool IsEnded = false);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "LayoutQuality", "Fixtures");

    // Reads from the first '{' to the last '}', so sqlcmd's column-header lines (it can't
    // print unlimited width with headers off) or a pasted query-editor cell can be saved as is.
    public static LayoutFixture Load(string path)
    {
        var text = File.ReadAllText(path);
        var (start, end) = (text.IndexOf('{'), text.LastIndexOf('}'));
        if (start < 0 || end < start) throw new InvalidDataException($"{path} has no JSON object");
        var fixture = JsonSerializer.Deserialize<LayoutFixture>(text[start..(end + 1)], Json)
            ?? throw new InvalidDataException($"{path} is empty");
        return fixture with { Name = fixture.Name ?? Path.GetFileNameWithoutExtension(path) };
    }

    public static LayoutFixture LoadNamed(string name) => Load(Path.Combine(FixturesDirectory, name + ".json"));

    // Checked-in fixtures first, then private exports, each alphabetically.
    public static IEnumerable<LayoutFixture> LoadAll()
    {
        var privateDir = Path.Combine(FixturesDirectory, "private");
        var paths = Directory.GetFiles(FixturesDirectory, "*.json").Order()
            .Concat(Directory.Exists(privateDir) ? Directory.GetFiles(privateDir, "*.json").Order() : []);
        return paths.Select(Load);
    }

    // Mapped like PersonService.GetAllAsync: PersonMapper, then LastName/FirstName order.
    public List<PersonDto> MapPeople()
    {
        var rels = Relationships.Select(r => new Relationship
        {
            Id = Guid.NewGuid(), PersonAId = r.PersonAId, PersonBId = r.PersonBId, Type = r.Type, EndDate = r.EndDate, IsEnded = r.IsEnded,
        }).ToList();
        return People
            .Select(p => PersonMapper.MapPersonToDto(new Person
            {
                Id = p.Id, FirstName = p.FirstName, LastName = p.LastName, BirthDate = p.BirthDate,
            }, rels))
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .ToList();
    }

    // The archived engine unless another is given.
    public (FamilyTreeLayout Layout, List<PersonDto> People, List<CoupleDto> Couples) Layout(IFamilyTreeLayoutEngine? engine = null)
    {
        var people = MapPeople();
        var couples = CoupleHelper.Derive(people);
        var focus = Focus ?? people[0].Id;
        return ((engine ?? new FamilyTreeLayoutEngine()).ComputeLayout(people, couples, focus, Anchor), people, couples);
    }
}
