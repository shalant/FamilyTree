using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// S7 (docs/security.md, 2026-10-25): AppUser.IsAdmin is the only Admin source, and the
// Users tab changes it through SetAdminAsync, which must end the user's other sessions.
public class AdminUserServiceTests
{
    private readonly TestDbContextFactory _db = new();

    private AdminUserService Service(bool superUser = true) =>
        new(_db, NullLogger<AdminUserService>.Instance, new FakeCurrentUserService { IsSuperUser = superUser });

    private async Task<AppUser> AddUserAsync(bool isAdmin = false, bool isSuperUser = false)
    {
        await using var ctx = _db.CreateDbContext();
        var user = new AppUser
        {
            Id = Guid.NewGuid(), UserName = "u@x.test", Email = "u@x.test", DisplayName = "U",
            SecurityStamp = "original", IsAdmin = isAdmin, IsSuperUser = isSuperUser, CreatedAt = DateTime.UtcNow,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    private async Task<AppUser> ReloadAsync(Guid id)
    {
        await using var ctx = _db.CreateDbContext();
        return await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SetAdminAsync_ChangesTheRoleAndRotatesTheSecurityStamp(bool before, bool after)
    {
        var user = await AddUserAsync(isAdmin: before);

        var result = await Service().SetAdminAsync(user.Id, after);

        result.Success.Should().BeTrue(result.Message);
        var saved = await ReloadAsync(user.Id);
        saved.IsAdmin.Should().Be(after);
        saved.SecurityStamp.Should().NotBe("original");
    }

    [Fact]
    public async Task SetAdminAsync_NoChange_LeavesSessionsAlone()
    {
        var user = await AddUserAsync(isAdmin: true);

        (await Service().SetAdminAsync(user.Id, true)).Success.Should().BeTrue();

        (await ReloadAsync(user.Id)).SecurityStamp.Should().Be("original");
    }

    [Fact]
    public async Task SetAdminAsync_RefusesTheSuperUserAndNonSuperUsers()
    {
        var owner = await AddUserAsync(isSuperUser: true);
        (await Service().SetAdminAsync(owner.Id, true)).Success.Should().BeFalse();

        var member = await AddUserAsync();
        (await Service(superUser: false).SetAdminAsync(member.Id, true)).Success.Should().BeFalse();
        (await ReloadAsync(member.Id)).IsAdmin.Should().BeFalse();
    }

    // The pending lists used to filter only on expiry (and, for resets, dismissal), so an
    // invite someone had already used or the owner had cancelled, and a reset that had
    // been completed, still showed as pending with a copy-link button.
    [Fact]
    public async Task GetPendingInvitesAsync_ListsOnlyInvitesThatCanStillBeUsed()
    {
        var open = NewInvite("open@x.test");
        var accepted = NewInvite("accepted@x.test", i => i.AcceptedAt = DateTime.UtcNow);
        var cancelled = NewInvite("cancelled@x.test", i => i.CancelledAt = DateTime.UtcNow);
        var expired = NewInvite("expired@x.test", i => i.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));
        await using (var ctx = _db.CreateDbContext())
        {
            ctx.UserInvites.AddRange(open, accepted, cancelled, expired);
            await ctx.SaveChangesAsync();
        }

        var pending = (await Service().GetPendingInvitesAsync()).Data!;

        pending.Select(i => i.Email).Should().Equal("open@x.test");
    }

    [Fact]
    public async Task GetPendingPasswordResetsAsync_ListsOnlyResetsThatCanStillBeUsed()
    {
        var open = NewReset("open@x.test");
        var completed = NewReset("completed@x.test", r => r.CompletedAt = DateTime.UtcNow);
        var dismissed = NewReset("dismissed@x.test", r => r.DismissedAt = DateTime.UtcNow);
        var expired = NewReset("expired@x.test", r => r.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));
        await using (var ctx = _db.CreateDbContext())
        {
            ctx.PasswordResetRequests.AddRange(open, completed, dismissed, expired);
            await ctx.SaveChangesAsync();
        }

        var pending = (await Service().GetPendingPasswordResetsAsync()).Data!;

        pending.Select(r => r.Email).Should().Equal("open@x.test");
    }

    private static UserInvite NewInvite(string email, Action<UserInvite>? tweak = null)
    {
        var invite = new UserInvite
        {
            Id = Guid.NewGuid(), Email = email, FamilyId = Guid.NewGuid(), RoleToGrant = "Member",
            Token = Guid.NewGuid().ToString("N"), CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        tweak?.Invoke(invite);
        return invite;
    }

    private static PasswordResetRequest NewReset(string email, Action<PasswordResetRequest>? tweak = null)
    {
        var reset = new PasswordResetRequest
        {
            Id = Guid.NewGuid(), Email = email, Token = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(24),
        };
        tweak?.Invoke(reset);
        return reset;
    }

    [Fact]
    public async Task GetAllUsersAsync_ShowsWhoIsAdmin()
    {
        var admin = await AddUserAsync(isAdmin: true);

        var users = (await Service().GetAllUsersAsync()).Data!;

        users.Single(u => u.Id == admin.Id).IsAdmin.Should().BeTrue();
    }
}
