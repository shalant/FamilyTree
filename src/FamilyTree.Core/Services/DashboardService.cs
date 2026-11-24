using FamilyTree.Core.Data;
using FamilyTree.Shared.DTOs.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Core.Services;

/// <summary>
/// Admin › Dashboard counts. A family Admin sees their own family; the super-user sees every family.
/// Until 2026-11-24 every Admin saw site-wide counts and the latest audit entries of every family
/// (the Admin role is global, docs/security.md), which told one family's Admin how many people,
/// users and invites another family had.
/// </summary>
public class DashboardService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUserService currentUser) : IDashboardService
{
    public async Task<DashboardStatsDto> GetDashboardStatsAsync()
    {
        try
        {
            var isSuperUser = currentUser.IsSuperUser;
            var familyId = currentUser.FamilyId;
            // Anyone who isn't the super-user or an Admin with a family gets nothing.
            if (!isSuperUser && !(currentUser.IsAdmin && familyId.HasValue))
                return new();

            await using var ctx = await dbFactory.CreateDbContextAsync();
            var today = DateTime.UtcNow.Date;
            var now = DateTime.UtcNow;

            var people = ctx.People.Where(p => isSuperUser || p.FamilyId == familyId);
            var personIds = people.Select(p => p.Id);
            // AuditLog has no family column: a family Admin sees what their own members did.
            var memberIds = ctx.UserFamilies.Where(uf => uf.FamilyId == familyId).Select(uf => uf.UserId);
            var audit = ctx.AuditLogs.Where(a => isSuperUser || (a.UserId != null && memberIds.Contains(a.UserId.Value)));

            return new DashboardStatsDto
            {
                IsSiteWide = isSuperUser,
                TotalPeople = await people.CountAsync(),
                DeletedCount = await ctx.People.IgnoreQueryFilters()
                    .CountAsync(p => p.DeletedAt != null && (isSuperUser || p.FamilyId == familyId)),
                TotalRelationships = await ctx.Relationships
                    .CountAsync(r => personIds.Contains(r.PersonAId) && personIds.Contains(r.PersonBId)),
                FamilyCount = isSuperUser ? await ctx.Families.CountAsync() : 1,
                UserCount = isSuperUser
                    ? await ctx.AppUsers.CountAsync()
                    : await ctx.UserFamilies.CountAsync(uf => uf.FamilyId == familyId),
                PendingInvites = await ctx.UserInvites.CountAsync(i =>
                    i.AcceptedAt == null && i.CancelledAt == null && i.ExpiresAt > now
                    && (isSuperUser || i.FamilyId == familyId)),
                TotalAuditEntries = await audit.CountAsync(),
                AuditEntriesToday = await audit.CountAsync(a => a.Timestamp >= today),
                RecentAudit = (await audit
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
        }
        catch
        {
            return new();
        }
    }
}
