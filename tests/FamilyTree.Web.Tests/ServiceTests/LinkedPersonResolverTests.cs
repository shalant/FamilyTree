using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Pages;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

// Regression coverage for the 2026-07-08 "signed-in user's linked Person soft-deleted"
// bug: a dangling PersonId (set, but the Person it points at is gone) must be treated the
// same as "never linked," not as "resolved."
public class LinkedPersonResolverTests
{
    private static PersonDto Person(Guid id) => new() { Id = id, FirstName = "A", LastName = "B" };

    [Fact]
    public void NeverLinked_NullPersonId_IsNotResolved()
    {
        var people = new List<PersonDto> { Person(Guid.NewGuid()) };

        LinkedPersonResolver.IsResolved(null, people).Should().BeFalse();
    }

    [Fact]
    public void LinkedToAPersonStillOnTheRoster_IsResolved()
    {
        var id = Guid.NewGuid();
        var people = new List<PersonDto> { Person(id) };

        LinkedPersonResolver.IsResolved(id, people).Should().BeTrue();
    }

    [Fact]
    public void LinkedToAPersonNoLongerOnTheRoster_IsNotResolved()
    {
        // Simulates a soft-deleted Person: PersonId is still set, but PersonService.GetAllAsync
        // (soft-delete-filtered) no longer returns them.
        var danglingId = Guid.NewGuid();
        var people = new List<PersonDto> { Person(Guid.NewGuid()) };

        LinkedPersonResolver.IsResolved(danglingId, people).Should().BeFalse();
    }

    [Fact]
    public void EmptyRoster_IsNeverResolved()
    {
        LinkedPersonResolver.IsResolved(Guid.NewGuid(), []).Should().BeFalse();
    }
}
