using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// Regression tests for StoryService.GetPendingLinkingSubjectAsync — the query that
// decides whether the self-service tree-linking modal walks a new user through adding
// a story's subject first. Getting this wrong (2026-07-04, the "Tom" bug) meant a
// self-authored story about yourself was mistaken for a third party's story, and the
// modal ended up asking "is Tom related to anyone?" about Tom while Tom answered it.
public class StoryPendingLinkingSubjectTests
{
    private const string Email = "willa@example.com";

    private static (StoryService service, TestDbContextFactory factory) CreateSut()
    {
        var factory = new TestDbContextFactory();
        var service = new StoryService(
            factory,
            NullLogger<StoryService>.Instance,
            new FakeAuditLogService(),
            new FakeCurrentUserService());
        return (service, factory);
    }

    [Fact]
    public async Task ExcludesSelfAuthoredStory_EvenWithMatchingInviteEmail()
    {
        var (service, factory) = CreateSut();
        await using (var ctx = factory.CreateDbContext())
        {
            var invite = new StoryInvite
            {
                Id = Guid.NewGuid(), Token = "tok", InvitedEmail = Email,
                InvitedByUserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
            };
            ctx.StoryInvites.Add(invite);
            ctx.Stories.Add(new Story
            {
                Id = Guid.NewGuid(), Title = "t", Body = "b",
                UnlinkedPersonName = "Tom Kuh",
                AuthorId = Guid.NewGuid(),          // self-authored — must be excluded
                InviteId = invite.Id,
                CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var result = await service.GetPendingLinkingSubjectAsync(Email);

        result.Success.Should().BeTrue();
        result.Data.Should().BeNull("a self-authored story must never drive the subject-first flow");
    }

    [Fact]
    public async Task ReturnsAnonymousInviteResponseStory()
    {
        var (service, factory) = CreateSut();
        await using (var ctx = factory.CreateDbContext())
        {
            var invite = new StoryInvite
            {
                Id = Guid.NewGuid(), Token = "tok", InvitedEmail = Email,
                InvitedByUserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
            };
            ctx.StoryInvites.Add(invite);
            ctx.Stories.Add(new Story
            {
                Id = Guid.NewGuid(), Title = "t", Body = "b",
                UnlinkedPersonName = "Bill Small",
                AuthorId = null, AuthorName = "Willa Kuh",  // anonymous invite response
                InviteId = invite.Id,
                CreatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var result = await service.GetPendingLinkingSubjectAsync(Email);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.UnlinkedPersonName.Should().Be("Bill Small");
    }

    [Fact]
    public async Task ReturnsMostRecentWhenMultipleCandidatesExist()
    {
        var (service, factory) = CreateSut();
        await using (var ctx = factory.CreateDbContext())
        {
            var invite = new StoryInvite
            {
                Id = Guid.NewGuid(), Token = "tok", InvitedEmail = Email,
                InvitedByUserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
            };
            ctx.StoryInvites.Add(invite);
            ctx.Stories.AddRange(
                new Story
                {
                    Id = Guid.NewGuid(), Title = "t1", Body = "b1",
                    UnlinkedPersonName = "Older Subject",
                    InviteId = invite.Id, CreatedAt = DateTime.UtcNow.AddDays(-5),
                },
                new Story
                {
                    Id = Guid.NewGuid(), Title = "t2", Body = "b2",
                    UnlinkedPersonName = "Newer Subject",
                    InviteId = invite.Id, CreatedAt = DateTime.UtcNow,
                });
            await ctx.SaveChangesAsync();
        }

        var result = await service.GetPendingLinkingSubjectAsync(Email);

        result.Data!.UnlinkedPersonName.Should().Be("Newer Subject");
    }

    [Fact]
    public async Task ReturnsNull_WhenNoPendingStoryExists()
    {
        var (service, _) = CreateSut();

        var result = await service.GetPendingLinkingSubjectAsync(Email);

        result.Success.Should().BeTrue();
        result.Data.Should().BeNull();
    }
}
