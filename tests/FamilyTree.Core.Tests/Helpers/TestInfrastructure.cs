using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FamilyTree.Core.Data;
using FamilyTree.Core.Services;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
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
    public bool    IsAdmin     { get; set; }
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

// Only UserExistsAsync/CreateInviteAsync are exercised (by StoryInviteService); every
// other member throws so a test relying on unstubbed behavior fails loudly instead of
// silently returning a default value.
internal sealed class FakeAuthService : IAuthService
{
    public bool UserExistsResult { get; set; }
    public Guid CreateInviteResultId { get; set; } = Guid.NewGuid();
    public int CreateInviteCallCount { get; private set; }

    public Task<ServiceResponse<bool>> UserExistsAsync(string email) => Task.FromResult(ServiceResponse<bool>.Ok(UserExistsResult));

    public Task<ServiceResponse<Guid>> CreateInviteAsync(string email)
    {
        CreateInviteCallCount++;
        return Task.FromResult(ServiceResponse<Guid>.Ok(CreateInviteResultId));
    }

    public Task<ServiceResponse<Guid>> RegisterAsync(string firstName, string lastName, string email, string password, Guid? inviteId = null)
        => Task.FromResult(ServiceResponse<Guid>.Fail("Not implemented in test mock"));
    public Task<ServiceResponse> LinkPersonAsync(Guid userId, Guid? personId)
        => Task.FromResult(ServiceResponse.Fail("Not implemented in test mock"));
    public Task<ServiceResponse> EnsureUserFamilyAsync(Guid userId)
        => Task.FromResult(ServiceResponse.Ok());
    public Task<ServiceResponse<List<UserInvite>>> GetPendingInvitesAsync()
        => Task.FromResult(ServiceResponse<List<UserInvite>>.Ok([]));
    public Task<ServiceResponse> CancelInviteAsync(Guid inviteId)
        => Task.FromResult(ServiceResponse.Fail("Not implemented in test mock"));
    public Task<ServiceResponse<UserInvite?>> ValidateInviteAsync(Guid inviteId)
        => Task.FromResult(ServiceResponse<UserInvite?>.Ok(null));
    public string GetRegistrationMode() => "Open";
    public Task<ServiceResponse> SaveFocusPersonAsync(Guid userId, Guid? personId)
        => Task.FromResult(ServiceResponse.Ok());
    public Task<ServiceResponse<Guid?>> GetFocusPersonIdAsync(Guid userId)
        => Task.FromResult(ServiceResponse<Guid?>.Ok(null));
    public Task<ServiceResponse> RequestPasswordResetAsync(string email, string? baseUrl = null)
        => Task.FromResult(ServiceResponse.Fail("Not implemented in test mock"));
    public Task<ServiceResponse<bool>> IsResetRequestValidAsync(Guid requestId)
        => Task.FromResult(ServiceResponse<bool>.Ok(false));
    public Task<ServiceResponse> ResetPasswordAsync(Guid requestId, string newPassword)
        => Task.FromResult(ServiceResponse.Fail("Not implemented in test mock"));
    public Task<ServiceResponse<List<PasswordResetRequest>>> GetPendingResetRequestsAsync()
        => Task.FromResult(ServiceResponse<List<PasswordResetRequest>>.Ok([]));
    public Task<ServiceResponse> DismissResetRequestAsync(Guid id)
        => Task.FromResult(ServiceResponse.Ok());
    public Task<ServiceResponse<Guid?>> LinkUserToTreeAsync(Guid userId, Guid? personId, string? firstName, string? lastName, Guid? connectedPersonId = null, string? relationshipType = null)
        => Task.FromResult(ServiceResponse<Guid?>.Fail("Not implemented in test mock"));
    public Task<ServiceResponse<Guid>> CreateUnlinkedPersonAsync(string? firstName, string? lastName, Guid connectedPersonId, string relationshipType, Guid createdByUserId)
        => Task.FromResult(ServiceResponse<Guid>.Fail("Not implemented in test mock"));
    public Task<ServiceResponse<HashSet<Guid>>> GetLinkedPersonIdsAsync(IEnumerable<Guid> personIds)
        => Task.FromResult(ServiceResponse<HashSet<Guid>>.Ok([]));
}
