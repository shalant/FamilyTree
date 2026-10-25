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

    [Fact]
    public async Task GetAllUsersAsync_ShowsWhoIsAdmin()
    {
        var admin = await AddUserAsync(isAdmin: true);

        var users = (await Service().GetAllUsersAsync()).Data!;

        users.Single(u => u.Id == admin.Id).IsAdmin.Should().BeTrue();
    }
}
