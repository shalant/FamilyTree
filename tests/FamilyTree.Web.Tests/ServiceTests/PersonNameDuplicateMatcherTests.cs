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
}
