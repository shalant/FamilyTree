using FamilyTree.Core.Data;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Admin;
using FamilyTree.Shared.Enums;
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

    public async Task<ServiceResponse<List<ParentCycleDto>>> FindParentCyclesAsync(CancellationToken ct = default)
    {
        try
        {
            if (currentUser.UserId.HasValue && !currentUser.IsSuperUser)
                return ServiceResponse<List<ParentCycleDto>>.Fail("Only a super-user can run the parent-loop check.");

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            // Default query filters: soft-deleted people and relationships are excluded, which
            // is exactly the set the tree is rendered from.
            var people = await ctx.People.AsNoTracking()
                .Select(p => new { p.Id, p.FirstName, p.LastName, p.FamilyId })
                .ToDictionaryAsync(p => p.Id, ct);
            var links = (await ctx.Relationships.AsNoTracking()
                    .Where(r => r.Type == RelationshipType.Parent)
                    .Select(r => new { r.Id, Parent = r.PersonAId, Child = r.PersonBId })
                    .ToListAsync(ct))
                .Where(r => people.ContainsKey(r.Parent) && people.ContainsKey(r.Child))
                .ToList();

            string Name(Guid id) => $"{people[id].FirstName} {people[id].LastName}".Trim();

            var result = new ParentGraph(links.Select(l => (l.Parent, l.Child))).FindCycles()
                .Select(cycle =>
                {
                    var members = cycle.ToHashSet();
                    return new ParentCycleDto
                    {
                        People = [.. cycle.Select(id => new ParentCyclePersonDto
                            { PersonId = id, Name = Name(id), FamilyId = people[id].FamilyId })],
                        Links = [.. links.Where(l => members.Contains(l.Parent) && members.Contains(l.Child))
                            .Select(l => new ParentCycleLinkDto
                            {
                                RelationshipId = l.Id,
                                ParentId = l.Parent, ParentName = Name(l.Parent),
                                ChildId = l.Child, ChildName = Name(l.Child),
                            })],
                    };
                })
                .ToList();

            if (result.Count > 0)
                logger.LogWarning(
                    "Parent-loop check found {Count} loop(s) among live people: {Loops}. Remove one Parent link " +
                    "from each to repair it.",
                    result.Count,
                    string.Join("; ", result.Select(c => string.Join(" -> ", c.People.Select(p => $"{p.Name} ({p.PersonId})")))));

            return ServiceResponse<List<ParentCycleDto>>.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running the parent-loop integrity check.");
            return ServiceResponse<List<ParentCycleDto>>.Fail("An error occurred running the parent-loop check.");
        }
    }
}
