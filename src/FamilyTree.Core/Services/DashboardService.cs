using FamilyTree.Core.Data;
using FamilyTree.Shared.DTOs.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Core.Services;

public class DashboardService(IDbContextFactory<AppDbContext> dbFactory) : IDashboardService
{
    public async Task<DashboardStatsDto> GetDashboardStatsAsync()
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync();
            var today = DateTime.UtcNow.Date;

            var stats = new DashboardStatsDto
            {
                TotalPeople = await ctx.People.CountAsync(),
                DeletedCount = await ctx.People.IgnoreQueryFilters().CountAsync(p => p.DeletedAt != null),
                TotalRelationships = await ctx.Relationships.CountAsync(),
                FamilyCount = await ctx.Families.CountAsync(),
                UserCount = await ctx.AppUsers.CountAsync(),
                PendingInvites = await ctx.UserInvites
                    .CountAsync(i => i.AcceptedAt == null && i.CancelledAt == null && i.ExpiresAt > DateTime.UtcNow),
                TotalAuditEntries = await ctx.AuditLogs.CountAsync(),
                AuditEntriesToday = await ctx.AuditLogs.CountAsync(a => a.Timestamp >= today),
                RecentAudit = (await ctx.AuditLogs
                    .AsNoTracking()
                    .OrderByDescending(a => a.Timestamp)
                    .Take(10)
                    .ToListAsync())
                    .Select(a => new AuditEntryDto
                    {
                        Action = a.Action,
                        EntityType = a.EntityType,
                        EntityId = a.EntityId,
                        Timestamp = a.Timestamp
                    })
                    .ToList()
            };

            return stats;
        }
        catch
        {
            return new();
        }
    }
}
