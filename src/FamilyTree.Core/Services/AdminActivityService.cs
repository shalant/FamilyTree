using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class AdminActivityService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<AdminActivityService> logger) : IAdminActivityService
{
    public async Task<ServiceResponse<List<UserActivity>>> GetRecentActivityAsync(int limit = 200, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var activity = await ctx.UserActivities
                .AsNoTracking()
                .OrderByDescending(a => a.Date)
                .Take(limit)
                .ToListAsync(ct);

            return ServiceResponse<List<UserActivity>>.Ok(activity);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading user activity");
            return ServiceResponse<List<UserActivity>>.Fail("Failed to load activity data.");
        }
    }

    public async Task<ServiceResponse<List<AuditLog>>> GetActivityAuditLogsAsync(Guid? userId, DateOnly date, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var logs = await ctx.AuditLogs
                .AsNoTracking()
                .Where(a => a.UserId == userId &&
                            DateOnly.FromDateTime(a.Timestamp) == date)
                .OrderByDescending(a => a.Timestamp)
                .ToListAsync(ct);

            return ServiceResponse<List<AuditLog>>.Ok(logs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading activity audit logs for user {UserId}", userId);
            return ServiceResponse<List<AuditLog>>.Fail("Failed to load audit logs.");
        }
    }
}
