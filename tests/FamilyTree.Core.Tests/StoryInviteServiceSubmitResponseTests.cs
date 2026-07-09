using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.DTOs.StoryInvite;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// Regression coverage for the bug found 2026-07-06: Ellen, who already had an account,
// wrote a story about Morton (invited by Doug) and was still redirected to /register
// after submitting — StoryInviteService.SubmitResponseAsync unconditionally minted a
// fresh registration invite without checking whether the invited email already had an
// account. Fixed by checking AuthService.UserExistsAsync first.
public class StoryInviteServiceSubmitResponseTests
{
    private static (StoryInviteService service, TestDbContextFactory factory, FakeAuthService authService) CreateSut()
    {
        var factory = new TestDbContextFactory();
        var authService = new FakeAuthService();
        var service = new StoryInviteService(
            factory,
            NullLogger<StoryInviteService>.Instance,
            new FakeAuditLogService(),
            new FakeCurrentUserService(),
            new FakeEmailSender(),
            new ConfigurationBuilder().Build(),
            authService);
        return (service, factory, authService);
    }

    private static async Task<string> SeedInviteAsync(
        TestDbContextFactory factory, string invitedEmail, bool isUsed = false, DateTime? expiresAt = null)
    {
        await using var ctx = factory.CreateDbContext();
        var person = new Person { FirstName = "Morton", LastName = "Small", CreatedAt = DateTime.UtcNow };
        ctx.People.Add(person);
        var invite = new StoryInvite
        {
            Id = Guid.NewGuid(),
            Token = Guid.NewGuid().ToString("N"),
            PersonId = person.Id,
            InvitedEmail = invitedEmail,
            InvitedByUserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(30),
            IsUsed = isUsed,
        };
        ctx.StoryInvites.Add(invite);
        await ctx.SaveChangesAsync();
        return invite.Token;
    }

    [Fact]
    public async Task SubmitResponseAsync_RecipientHasNoAccount_CreatesUserInvite()
    {
        var (service, factory, authService) = CreateSut();
        authService.UserExistsResult = false;
        var token = await SeedInviteAsync(factory, "ellen@example.com");

        var result = await service.SubmitResponseAsync(new StoryInviteResponseDto { Token = token, Body = "A lovely memory." });

        result.Success.Should().BeTrue();
        result.Data!.RecipientHasAccount.Should().BeFalse();
        result.Data.RecipientEmail.Should().Be("ellen@example.com");
        result.Data.UserInviteId.Should().NotBeNull("no account exists yet, so a registration invite should be minted");
        authService.CreateInviteCallCount.Should().Be(1);
    }

    [Fact]
    public async Task SubmitResponseAsync_RecipientAlreadyHasAccount_SkipsInviteCreation()
    {
        var (service, factory, authService) = CreateSut();
        authService.UserExistsResult = true;
        var token = await SeedInviteAsync(factory, "ellen@example.com");

        var result = await service.SubmitResponseAsync(new StoryInviteResponseDto { Token = token, Body = "A lovely memory." });

        result.Success.Should().BeTrue();
        result.Data!.RecipientHasAccount.Should().BeTrue(
            "Ellen already has an account — the response page should offer sign-in, not registration");
        result.Data.UserInviteId.Should().BeNull(
            "minting a registration invite for someone who can already log in is pointless and misleading");
        authService.CreateInviteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task SubmitResponseAsync_InvalidToken_Fails()
    {
        var (service, _, _) = CreateSut();

        var result = await service.SubmitResponseAsync(new StoryInviteResponseDto { Token = "nonexistent", Body = "Hello" });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitResponseAsync_AlreadyUsedInvite_Fails()
    {
        var (service, factory, _) = CreateSut();
        var token = await SeedInviteAsync(factory, "ellen@example.com", isUsed: true);

        var result = await service.SubmitResponseAsync(new StoryInviteResponseDto { Token = token, Body = "Hello" });

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitResponseAsync_ExpiredInvite_Fails()
    {
        var (service, factory, _) = CreateSut();
        var token = await SeedInviteAsync(factory, "ellen@example.com", expiresAt: DateTime.UtcNow.AddDays(-1));

        var result = await service.SubmitResponseAsync(new StoryInviteResponseDto { Token = token, Body = "Hello" });

        result.Success.Should().BeFalse();
    }
}
