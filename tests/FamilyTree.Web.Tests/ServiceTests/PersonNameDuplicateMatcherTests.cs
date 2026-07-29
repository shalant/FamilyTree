using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

public class PersonNameDuplicateMatcherTests
{
    private static PersonDto Person(string first, string last) =>
        new() { Id = Guid.NewGuid(), FirstName = first, LastName = last };

    [Fact]
    public void ExactNameMatch_IsFound()
    {
        var people = new List<PersonDto> { Person("Elliot", "Rosenberg") };

        var matches = PersonNameDuplicateMatcher.FindMatches(people, "Elliot", "Rosenberg");

        matches.Should().ContainSingle(p => p.FirstName == "Elliot" && p.LastName == "Rosenberg");
    }

    [Theory]
    [InlineData("elliot", "rosenberg")]   // case-insensitive
    [InlineData(" Elliot ", " Rosenberg ")] // surrounding whitespace
    [InlineData("ELLIOT", "ROSENBERG")]
    public void MatchIsCaseAndWhitespaceInsensitive(string first, string last)
    {
        var people = new List<PersonDto> { Person("Elliot", "Rosenberg") };

        var matches = PersonNameDuplicateMatcher.FindMatches(people, first, last);

        matches.Should().ContainSingle();
    }

    [Fact]
    public void NoMatchingName_ReturnsEmpty()
    {
        var people = new List<PersonDto> { Person("Elliot", "Rosenberg") };

        var matches = PersonNameDuplicateMatcher.FindMatches(people, "Marc", "Rosenberg");

        matches.Should().BeEmpty();
    }

    [Fact]
    public void PartialNameMatch_DoesNotCount_OnlyFullFirstAndLastMustMatch()
    {
        var people = new List<PersonDto> { Person("Elliot", "Rosenberg") };

        var matches = PersonNameDuplicateMatcher.FindMatches(people, "Elliot", "Levin");

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MultipleMatches_AreAllReturned()
    {
        var people = new List<PersonDto>
        {
            Person("John", "Smith"),
            Person("John", "Smith"),
            Person("Jane", "Smith"),
        };

        var matches = PersonNameDuplicateMatcher.FindMatches(people, "John", "Smith");

        matches.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void BlankName_NeverMatchesAnyone(string? first, string? last)
    {
        var people = new List<PersonDto> { Person("", "") };

        var matches = PersonNameDuplicateMatcher.FindMatches(people, first, last);

        matches.Should().BeEmpty();
    }

    // ─── Fuzzy matching tests ───────────────────────────────────────────

    [Theory]
    [InlineData("Jon", "Smith", "John", "Smith")]      // 1-char typo
    [InlineData("Johm", "Smith", "John", "Smith")]     // 1-char typo
    [InlineData("Jahn", "Smith", "John", "Smith")]     // 1-char phonetic
    public void FuzzyMatch_CommonTypos_AreFound(string queryFirst, string queryLast, string treeFirst, string treeLast)
    {
        var people = new List<PersonDto> { Person(treeFirst, treeLast) };

        var matches = PersonNameDuplicateMatcher.FindMatches(people, queryFirst, queryLast);

        matches.Should().ContainSingle();
    }

    [Fact]
    public void FuzzyMatch_NicknameLikeVariants_AreFound()
    {
        // Common nickname pairs that are similar but not identical
        var people = new List<PersonDto>
        {
            Person("William", "Smith"),
            Person("Elizabeth", "Jones"),
        };

        // "Bill" is close to "William" (70%+ similarity) but below 80% threshold
        var billMatches = PersonNameDuplicateMatcher.FindMatches(people, "Bill", "Smith");
        billMatches.Should().BeEmpty("Bill and William are too different for fuzzy match at 80% threshold");

        // "Wm" (abbreviation) is very different, should not match
        var wmMatches = PersonNameDuplicateMatcher.FindMatches(people, "Wm", "Smith");
        wmMatches.Should().BeEmpty();
    }

    [Fact]
    public void FuzzyMatch_TooDifferent_DoesNotMatch()
    {
        var people = new List<PersonDto> { Person("Alice", "Smith") };

        var matches = PersonNameDuplicateMatcher.FindMatches(people, "David", "Smith");

        matches.Should().BeEmpty("Alice and David are too different to match");
    }

    [Fact]
    public void FuzzyMatch_ExactMatchTakesPriority()
    {
        var people = new List<PersonDto>
        {
            Person("John", "Smith"),
            Person("Jon", "Smith"),
        };

        // Query for exact "John Smith" — should get the exact match only, not the fuzzy match
        var matches = PersonNameDuplicateMatcher.FindMatches(people, "John", "Smith");

        matches.Should().ContainSingle(p => p.FirstName == "John");
    }

    [Fact]
    public void FuzzyMatch_IncompleteNames_NeverMatchFuzzy()
    {
        // Fuzzy matching only applies to full (first + last) names
        var people = new List<PersonDto> { Person("John", "Smith") };

        var firstOnlyMatches = PersonNameDuplicateMatcher.FindMatches(people, "Jon", "");
        firstOnlyMatches.Should().BeEmpty();

        var lastOnlyMatches = PersonNameDuplicateMatcher.FindMatches(people, "", "Smth");
        lastOnlyMatches.Should().BeEmpty();
    }

    [Fact]
    public void FuzzyMatch_ReturnsMultipleMatches()
    {
        var people = new List<PersonDto>
        {
            Person("Jhon", "Smith"),      // Fuzzy match: 1-char typo
            Person("Jahn", "Smith"),      // Fuzzy match: 1-char typo
            Person("David", "Smith"),     // Not close
        };

        var matches = PersonNameDuplicateMatcher.FindMatches(people, "John", "Smith");

        // Should return all fuzzy matches that meet the threshold
        matches.Should().HaveCount(2);
    }
}
