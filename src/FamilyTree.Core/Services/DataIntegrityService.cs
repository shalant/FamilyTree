using FamilyTree.Core.Data;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class DataIntegrityService(
    IDbContextFactory<AppDbContext> dbFactory,
    IAuditLogService auditLog,
    ICurrentUserService currentUser,
    ILogger<DataIntegrityService> logger) : IDataIntegrityService
{
    public async Task<ServiceResponse<List<OrphanedRelationshipFixDto>>> FixOrphanedRelationshipsAsync(
        CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            // IgnoreQueryFilters on both sides: People, because a soft-deleted Person is
            // exactly what we're looking for; Relationships, because the default filter
            // would already exclude any relationship this check has fixed on a prior run,
            // which is fine, but leaving it on Relationships here too keeps the intent
            // explicit rather than relying on ambient scoping.
            var people = await ctx.People
                .IgnoreQueryFilters()
                .Select(p => new { p.Id, p.FirstName, p.LastName, p.DeletedAt })
                .ToDictionaryAsync(p => p.Id, ct);

            var deletedPersonIds = people.Values
                .Where(p => p.DeletedAt != null)
                .Select(p => p.Id)
                .ToHashSet();

            var liveRelationships = await ctx.Relationships
                .IgnoreQueryFilters()
                .Where(r => r.DeletedAt == null)
                .ToListAsync(ct);

            var orphaned = liveRelationships
                .Where(r => deletedPersonIds.Contains(r.PersonAId) || deletedPersonIds.Contains(r.PersonBId))
                .ToList();

            var now = DateTime.UtcNow;
            var userId = currentUser.UserId; // null when run unattended (e.g. at startup)
            var fixes = new List<OrphanedRelationshipFixDto>();

            foreach (var rel in orphaned)
            {
                rel.DeletedAt = now;
                rel.DeletedBy = userId;

                fixes.Add(new OrphanedRelationshipFixDto
                {
                    RelationshipId = rel.Id,
                    PersonAId = rel.PersonAId,
                    PersonAName = people.TryGetValue(rel.PersonAId, out var pa) ? $"{pa.FirstName} {pa.LastName}" : "(unknown)",
                    PersonBId = rel.PersonBId,
                    PersonBName = people.TryGetValue(rel.PersonBId, out var pb) ? $"{pb.FirstName} {pb.LastName}" : "(unknown)",
                    Type = rel.Type.ToString(),
                });
            }

            if (fixes.Count > 0)
            {
                await ctx.SaveChangesAsync(ct);

                logger.LogWarning(
                    "Data integrity check auto-fixed {Count} orphaned relationship(s) referencing a soft-deleted Person.",
                    fixes.Count);

                foreach (var fix in fixes)
                {
                    _ = auditLog.LogAsync("AutoFix", "Relationship", fix.RelationshipId,
                        newValue: $"Orphaned {fix.Type} relationship between {fix.PersonAName} and {fix.PersonBName} " +
                                  "auto-soft-deleted (one side was soft-deleted outside the normal cascade).",
                        userId: userId, ct: ct);
                }
            }

            return ServiceResponse<List<OrphanedRelationshipFixDto>>.Ok(fixes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running orphaned-relationship integrity check.");
            return ServiceResponse<List<OrphanedRelationshipFixDto>>.Fail(
                "An error occurred running the integrity check.");
        }
    }
}
