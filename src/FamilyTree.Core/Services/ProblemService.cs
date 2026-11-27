using System.Text.RegularExpressions;
using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public record ProblemView(Problem Problem, string? PersonName);

public interface IProblemService
{
    /// <summary>Newest first. Site owner only.</summary>
    Task<ServiceResponse<List<ProblemView>>> GetRecentAsync(int limit = 200, CancellationToken ct = default);
}

public partial class ProblemService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<ProblemService> logger,
    ICurrentUserService currentUser) : IProblemService
{
    [GeneratedRegex(@"^/people/([0-9a-fA-F-]{36})(/|$)")]
    private static partial Regex PersonPage();

    public async Task<ServiceResponse<List<ProblemView>>> GetRecentAsync(int limit = 200, CancellationToken ct = default)
    {
        // Reports name people and accounts from every family, like the audit log.
        if (!currentUser.IsSuperUser)
            return ServiceResponse<List<ProblemView>>.Fail("Only the site owner can view problems.");

        try
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var problems = await ctx.Problems.AsNoTracking()
                .OrderByDescending(p => p.OccurredAt)
                .Take(limit)
                .ToListAsync(ct);

            var ids = problems.Select(p => PersonId(p.Page)).OfType<Guid>().Distinct().ToList();
            var names = await ctx.People.IgnoreQueryFilters().AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => $"{p.FirstName} {p.LastName}".Trim(), ct);

            return ServiceResponse<List<ProblemView>>.Ok(problems
                .Select(p => new ProblemView(p, PersonId(p.Page) is { } id ? names.GetValueOrDefault(id) : null))
                .ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading problems");
            return ServiceResponse<List<ProblemView>>.Fail("Failed to load problems.");
        }
    }

    private static Guid? PersonId(string? page) =>
        page is not null && PersonPage().Match(page) is { Success: true } m && Guid.TryParse(m.Groups[1].Value, out var id)
            ? id
            : null;
}
