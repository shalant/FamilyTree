using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FamilyTree.Core.Data;
using FamilyTree.Core.Services;
using FamilyTree.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyTree.Core.Tests.Helpers;

internal sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    public AppDbContext CreateDbContext() => new(_options);
}

// Builds a real UserManager<AppUser> (not a mock) backed by the same in-memory
// database as a TestDbContextFactory, so AuthService tests exercise the actual
// Identity UserStore behavior (uniqueness checks, concurrency stamps, etc.).
// Deliberately does not register any token providers — fine for the
// tree-linking methods under test (FindByIdAsync/CreateAsync/UpdateAsync only);
// don't use this helper for password-reset-token tests without adding one.
internal static class TestUserManagerFactory
{
    public static UserManager<AppUser> Create(TestDbContextFactory factory)
    {
        var store = new UserStore<AppUser, IdentityRole<Guid>, AppDbContext, Guid>(factory.CreateDbContext());
        return new UserManager<AppUser>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<AppUser>(),
            new List<IUserValidator<AppUser>> { new UserValidator<AppUser>() },
            new List<IPasswordValidator<AppUser>> { new PasswordValidator<AppUser>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services: null!,
            NullLogger<UserManager<AppUser>>.Instance);
    }
}

internal sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid?   UserId      { get; set; }
    public Guid?   FamilyId    { get; set; }
    public string? Email       { get; set; }
    public bool    IsSuperUser { get; set; }
}

internal sealed class FakeAuditLogService : IAuditLogService
{
    public Task LogAsync(string action, string entityType, Guid? entityId,
        string? oldValue = null, string? newValue = null,
        Guid? userId = null, string? ipAddress = null,
        CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class FakeEmailSender : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => Task.CompletedTask;
}
