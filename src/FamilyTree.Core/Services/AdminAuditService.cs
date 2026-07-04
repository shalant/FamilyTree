using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class AdminAuditService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AdminAuditService> logger) : IAdminAuditService
{
    public async Task<ServiceResponse<List<AuditLog>>> GetAllAuditLogsAsync(int limit = 500, CancellationToken ct = default)
    {
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
