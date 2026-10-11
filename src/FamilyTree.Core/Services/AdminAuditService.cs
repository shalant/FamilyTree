using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class AdminAuditService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AdminAuditService> logger,
    ICurrentUserService currentUser) : IAdminAuditService
{
    // Audit rows carry no FamilyId, so they can't be scoped to a family Admin; the log
    // shows every family's user and entity IDs. SuperUser-only (finding S2,
    // docs/security.md, 2026-10-10).
    public async Task<ServiceResponse<List<AuditLog>>> GetAllAuditLogsAsync(int limit = 500, CancellationToken ct = default)
    {
        if (!currentUser.IsSuperUser)
            return ServiceResponse<List<AuditLog>>.Fail("Only the site owner can view the audit log.");

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var logs = await ctx.AuditLogs
                .AsNoTracking()
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .ToListAsync(ct);

            return ServiceResponse<List<AuditLog>>.Ok(logs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading audit logs");
            return ServiceResponse<List<AuditLog>>.Fail("Failed to load audit logs.");
        }
    }
}
