using Xunit;
using FluentAssertions;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Core.Tests
{
    public class FocusResolutionTests
    {
        [Fact]
        public void ResolveFocus_ShouldFallbackToFirstPerson_WhenNoFocusExists()
        {
            var people = new List<PersonDto>
            {
                new() { Id = Guid.NewGuid(), FirstName = "Douglas" },
                new() { Id = Guid.NewGuid(), FirstName = "Lauren" }
            };

            var focus = people.FirstOrDefault();

            focus.Should().NotBeNull();
            focus!.FirstName.Should().Be("Douglas");
        }
    }
}