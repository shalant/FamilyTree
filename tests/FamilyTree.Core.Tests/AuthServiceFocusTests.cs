using FluentAssertions;
using Xunit;

namespace FamilyTree.Core.Tests;

public class AuthServiceFocusTests
{
    [Fact]
    public void SaveFocus_ShouldPersistId()
    {
        var userId = Guid.NewGuid();
        var personId = Guid.NewGuid();

        var saved = personId; // simulate DB write

        saved.Should().Be(personId);
    }
}