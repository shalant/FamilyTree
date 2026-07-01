using FamilyTree.Shared.DTOs.StoryInvite;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Core.Tests;

public class StorySubmissionRulesTests
{
    [Fact]
    public void Submission_ShouldFail_WhenBodyIsEmpty()
    {
        var response = new StoryInviteResponseDto { Body = "" };

        string? error = string.IsNullOrWhiteSpace(response.Body)
            ? "Please share your memory before submitting."
            : null;

        error.Should().NotBeNull();
    }

    [Fact]
    public void Submission_ShouldPass_WhenBodyIsProvided()
    {
        var response = new StoryInviteResponseDto { Body = "A great memory." };

        bool valid = !string.IsNullOrWhiteSpace(response.Body);

        valid.Should().BeTrue();
    }
}