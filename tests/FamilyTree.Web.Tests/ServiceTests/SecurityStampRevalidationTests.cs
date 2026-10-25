using System.Security.Claims;
using FamilyTree.Core.Models;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

// S7 (docs/security.md, 2026-10-25): an open Blazor circuit keeps the user it started
// with. SecurityStampRevalidatingAuthStateProvider re-checks the stamp, so a password
// reset or role change signs the circuit out too, not just the next HTTP request.
public class SecurityStampRevalidationTests
{
    private static readonly IdentityOptions Options = new();

    private static (UserManager<AppUser>, StampStore, AppUser) Setup()
    {
        var user = new AppUser { Id = Guid.NewGuid(), UserName = "u@x.test", SecurityStamp = "stamp-1" };
        var store = new StampStore(user);
        var manager = new UserManager<AppUser>(store, Microsoft.Extensions.Options.Options.Create(Options),
            new PasswordHasher<AppUser>(), [], [], new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, NullLogger<UserManager<AppUser>>.Instance);
        return (manager, store, user);
    }

    private static ClaimsPrincipal SignedInAs(AppUser user, string stamp) =>
        new(new ClaimsIdentity(
        [
            new Claim(Options.ClaimsIdentity.UserIdClaimType, user.Id.ToString()),
            new Claim(Options.ClaimsIdentity.SecurityStampClaimType, stamp),
        ], "Identity.Application"));

    [Fact]
    public async Task SameStamp_StaysSignedIn()
    {
        var (manager, _, user) = Setup();

        (await SecurityStampRevalidatingAuthStateProvider.StampStillMatchesAsync(
            manager, SignedInAs(user, "stamp-1"), Options)).Should().BeTrue();
    }

    [Fact]
    public async Task RotatedStamp_SignsOut()
    {
        var (manager, _, user) = Setup();
        var principal = SignedInAs(user, "stamp-1");

        await manager.UpdateSecurityStampAsync(user);

        (await SecurityStampRevalidatingAuthStateProvider.StampStillMatchesAsync(
            manager, principal, Options)).Should().BeFalse();
    }

    [Fact]
    public async Task DeletedAccount_SignsOut()
    {
        var (manager, store, user) = Setup();
        store.User = null;

        (await SecurityStampRevalidatingAuthStateProvider.StampStillMatchesAsync(
            manager, SignedInAs(user, "stamp-1"), Options)).Should().BeFalse();
    }

    // Just enough of a user store for UserManager's find and stamp calls.
    private sealed class StampStore(AppUser? user) : IUserSecurityStampStore<AppUser>
    {
        public AppUser? User { get; set; } = user;

        public Task<AppUser?> FindByIdAsync(string userId, CancellationToken ct) =>
            Task.FromResult(User?.Id.ToString() == userId ? User : null);
        public Task SetSecurityStampAsync(AppUser u, string stamp, CancellationToken ct) { u.SecurityStamp = stamp; return Task.CompletedTask; }
        public Task<string?> GetSecurityStampAsync(AppUser u, CancellationToken ct) => Task.FromResult(u.SecurityStamp);
        public Task<IdentityResult> UpdateAsync(AppUser u, CancellationToken ct) => Task.FromResult(IdentityResult.Success);

        public Task<string> GetUserIdAsync(AppUser u, CancellationToken ct) => Task.FromResult(u.Id.ToString());
        public Task<string?> GetUserNameAsync(AppUser u, CancellationToken ct) => Task.FromResult(u.UserName);
        public Task SetUserNameAsync(AppUser u, string? name, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetNormalizedUserNameAsync(AppUser u, CancellationToken ct) => Task.FromResult(u.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(AppUser u, string? name, CancellationToken ct) => Task.CompletedTask;
        public Task<IdentityResult> CreateAsync(AppUser u, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdentityResult> DeleteAsync(AppUser u, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppUser?> FindByNameAsync(string name, CancellationToken ct) => Task.FromResult<AppUser?>(null);
        public void Dispose() { }
    }
}
