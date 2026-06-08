using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Core.Services;

public class AuditLogService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AuditLogService> logger) : IAuditLogService
{
    public async Task LogAsync(string action, string entityType, Guid? entityId,
        string? oldValue = null, string? newValue = null,
        Guid? userId = null, string? ipAddress = null,
        CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            ctx.AuditLogs.Add(new AuditLog
            {
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                OldValue = oldValue,
                NewValue = newValue,
                UserId = userId,
                IpAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Audit log failures must never break the main operation
            logger.LogError(ex, "Failed to write audit log entry: {Action} {EntityType} {EntityId}",
                action, entityType, entityId);
        }
    }
}
