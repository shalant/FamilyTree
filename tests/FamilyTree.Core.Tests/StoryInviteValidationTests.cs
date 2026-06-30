using FamilyTree.Shared.DTOs.StoryInvite;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Core.Tests;

public class StoryInviteValidationTests
{
    [Fact]
    public void Invite_ShouldBeInvalid_WhenExpired()
    {
        var dto = new StoryInviteValidationDto
        {
            IsExpired = true,
            IsValid = false
        };

        dto.IsValid.Should().BeFalse();
        dto.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void Invite_ShouldBeInvalid_WhenUsed()
    {
        var dto = new StoryInviteValidationDto
        {
            IsUsed = true,
            IsValid = false
        };

        dto.IsValid.Should().BeFalse();
        dto.IsUsed.Should().BeTrue();
    }
}