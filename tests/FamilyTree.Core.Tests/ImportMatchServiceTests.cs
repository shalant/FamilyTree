using FamilyTree.Core.Services;
using FamilyTree.Shared.DTOs.Import;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Core.Tests;

public class ImportMatchServiceTests
{
    private static PersonDto Existing(string first, string last,
        int? birthYear = null, Gender? gender = null, string? maiden = null) => new()
    {
        Id         = Guid.NewGuid(),
        FirstName  = first,
        LastName   = last,
        MaidenName = maiden,
        BirthDate  = birthYear is int y ? new DateOnly(y, 1, 1) : null,
        Gender     = gender,
    };

    private static ImportPersonDto Imported(string first, string last,
        string? birth = null, string gender = "Unknown",
        string[]? aliases = null, string? maiden = null) => new()
    {
        FirstName  = first,
        LastName   = last,
        BirthDate  = birth,
        Gender     = gender,
        Aliases    = aliases,
        MaidenName = maiden,
    };

    [Fact]
    public void FindCandidates_SpellingVariantSameYear_IsCandidate()
    {
        var existing = new[] { Existing("Sarah", "Herskovitz", 1887) };
        var imported = Imported("Sara", "Herskovitz", "1887");

        var result = ImportMatchService.FindCandidates(imported, existing);

        result.Should().ContainSingle().Which.PersonId.Should().Be(existing[0].Id);
    }

    [Fact]
    public void FindCandidates_SameNameConflictingYears_Excluded()
    {
        var existing = new[] { Existing("Sarah", "Herskovitz", 1887) };
        var imported = Imported("Sarah", "Herskovitz", "1950");

        var result = ImportMatchService.FindCandidates(imported, existing);

        result.Should().BeEmpty();
    }

    [Fact]
    public void FindCandidates_AliasMatchesExistingFirstName_IsCandidate()
    {
        var existing = new[] { Existing("Sadie", "Reva", 1887) };
        var imported = Imported("Sarah", "Reva", "1887", aliases: ["Sadie"]);

        var result = ImportMatchService.FindCandidates(imported, existing);

        result.Should().ContainSingle();
    }

    [Fact]
    public void FindCandidates_MaidenNameMatchesExistingLastName_IsCandidate()
    {
        var existing = new[] { Existing("Lena", "Lavin", 1867) };
        var imported = Imported("Lena", "Herskovitz", "1867", maiden: "Lavin");

        var result = ImportMatchService.FindCandidates(imported, existing);

        result.Should().ContainSingle();
    }

    [Fact]
    public void FindCandidates_DifferentNames_NoMatch()
    {
        var existing = new[] { Existing("Marc", "Rosenberg", 1955) };
        var imported = Imported("Israel", "Herskovitz", "1865");

        var result = ImportMatchService.FindCandidates(imported, existing);

        result.Should().BeEmpty();
    }

    [Fact]
    public void FindCandidates_RanksExactMatchFirst()
    {
        var exact   = Existing("Israel", "Herskovitz", 1865);
        var weaker  = Existing("Israel", "Herskovitz", 1867);
        var existing = new[] { weaker, exact };
        var imported = Imported("Israel", "Herskovitz", "1865");

        var result = ImportMatchService.FindCandidates(imported, existing);

        result.Should().HaveCount(2);
        result.First().PersonId.Should().Be(exact.Id);
    }

    [Fact]
    public void FindCandidates_NoExistingPeople_ReturnsEmpty()
    {
        var result = ImportMatchService.FindCandidates(
            Imported("Israel", "Herskovitz", "1865"), []);

        result.Should().BeEmpty();
    }
}
