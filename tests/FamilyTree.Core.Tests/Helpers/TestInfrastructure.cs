using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FamilyTree.Core.Data;
using FamilyTree.Core.Services;

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

internal sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid?   UserId   { get; set; }
    public Guid?   FamilyId { get; set; }
    public string? Email    { get; set; }
}

internal sealed class FakeAuditLogService : IAuditLogService
{
    public Task LogAsync(string action, string entityType, Guid? entityId,
        string? oldValue = null, string? newValue = null,
        Guid? userId = null, string? ipAddress = null,
        CancellationToken ct = default)
        => Task.CompletedTask;
}
