using FamilyTree.Core.Data;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class DataCheckService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUserService currentUser,
    ILogger<DataCheckService> logger) : IDataCheckService
{
    public async Task<ServiceResponse<List<DataCheckFindingDto>>> GetFindingsAsync(CancellationToken ct = default)
    {
        try
        {
            // Family Admins check their own family; the super-user checks every family.
            // The Admin role is global (docs/security.md), so the family scope below is what
            // keeps one family's Admin out of another's data.
            if (!currentUser.IsSuperUser && !(currentUser.IsAdmin && currentUser.FamilyId.HasValue))
                return ServiceResponse<List<DataCheckFindingDto>>.Fail("Only an admin can run the data check.");

            await using var ctx = await dbFactory.CreateDbContextAsync(ct);

            // Default query filters: soft-deleted people and relationships are left out,
            // which is the set the tree is drawn from.
            var familyId = currentUser.FamilyId;
            var isSuperUser = currentUser.IsSuperUser;
            var scoped = ctx.People.Where(p => isSuperUser || p.FamilyId == familyId);
            var people = await scoped.AsNoTracking().ToListAsync(ct);
            var visibleIds = scoped.Select(p => p.Id);
            var relationships = await ctx.Relationships.AsNoTracking()
                .Where(r => visibleIds.Contains(r.PersonAId) && visibleIds.Contains(r.PersonBId))
                .ToListAsync(ct);
            var familyNames = await ctx.Families.AsNoTracking()
                .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

            var today = DateOnly.FromDateTime(DateTime.Today);
            var names = people.ToDictionary(p => p.Id, p => $"{p.FirstName} {p.LastName}".Trim());
            var result = new List<DataCheckFindingDto>();

            // Each family on its own, so a rule never pairs people from two families (a
            // super-user's duplicate check would otherwise match across them).
            foreach (var family in people.GroupBy(p => p.FamilyId).OrderBy(g => g.Key is { } k && familyNames.TryGetValue(k, out var n) ? n : ""))
            {
                var members = family.Select(p => p.Id).ToHashSet();
                var familyLinks = relationships
                    .Where(r => members.Contains(r.PersonAId) && members.Contains(r.PersonBId))
                    .ToList();
                var familyName = family.Key is { } fk && familyNames.TryGetValue(fk, out var fn) ? fn : "(no family)";

                result.AddRange(DataCheckRules.Find([.. family], familyLinks, today).Select(f => new DataCheckFindingDto
                {
                    Rule = f.Rule.ToString(),
                    IsImpossible = f.IsImpossible,
                    Message = f.Message,
                    People = [.. f.PersonIds.Select(id => new DataCheckPersonDto { PersonId = id, Name = names[id] })],
                    FamilyId = family.Key,
                    FamilyName = familyName,
                }));
            }

            return ServiceResponse<List<DataCheckFindingDto>>.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running the data check.");
            return ServiceResponse<List<DataCheckFindingDto>>.Fail("An error occurred running the data check.");
        }
    }
}
