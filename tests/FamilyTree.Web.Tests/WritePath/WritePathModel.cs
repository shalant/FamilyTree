using FamilyTree.Core.Data;
using FamilyTree.Core.Services;
using FamilyTree.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Web.Tests.WritePath;

// The database as rows, soft-deleted ones included: what a rejected write must leave untouched.
internal sealed record PersonRow(
    Guid Id, string First, string Last, DateOnly? Birth, DateOnly? Death, Gender? Gender,
    DateTime? DeletedAt, DateTime? UpdatedAt);

internal sealed record RelRow(
    Guid Id, Guid A, Guid B, RelationshipType Type, DateOnly? Start, DateOnly? End, bool IsEnded, DateTime? CreatedAt,
    DateTime? DeletedAt);

internal sealed record World(List<PersonRow> People, List<RelRow> Rels)
{
    public static async Task<World> ReadAsync(IDbContextFactory<AppDbContext> db)
    {
        await using var ctx = await db.CreateDbContextAsync();
        var people = await ctx.People.IgnoreQueryFilters().AsNoTracking()
            .Select(p => new PersonRow(p.Id, p.FirstName, p.LastName, p.BirthDate, p.DeathDate, p.Gender, p.DeletedAt, p.UpdatedAt))
            .ToListAsync();
        var rels = await ctx.Relationships.IgnoreQueryFilters().AsNoTracking()
            .Select(r => new RelRow(r.Id, r.PersonAId, r.PersonBId, r.Type, r.StartDate, r.EndDate, r.IsEnded, r.CreatedAt, r.DeletedAt))
            .ToListAsync();
        return new World([.. people.OrderBy(p => p.Id)], [.. rels.OrderBy(r => r.Id)]);
    }

    public bool SameAs(World other) => People.SequenceEqual(other.People) && Rels.SequenceEqual(other.Rels);

    public bool IsLive(Guid id) => People.Any(p => p.Id == id && p.DeletedAt == null);

    // What the app shows: live people, and live links between two live people.
    public Tree Logical()
    {
        var t = new Tree();
        foreach (var p in People.Where(p => p.DeletedAt == null))
            t.People[p.Id] = new Life(p.First, p.Last, p.Birth, p.Death, p.Gender);
        foreach (var r in Rels.Where(r => r.DeletedAt == null && t.People.ContainsKey(r.A) && t.People.ContainsKey(r.B)))
            t.AddRow(r);
        return t;
    }
}

internal sealed record Life(string First, string Last, DateOnly? Birth, DateOnly? Death, Gender? Gender)
{
    public LifeDates Dates => new($"{First} {Last}".Trim(), Birth, Death, Gender);
}

// The family as the user sees it. Both what the database holds now, and what it should
// hold after a save, are one of these, so "did the save do exactly what was asked" is a
// comparison of two Trees.
internal sealed class Tree
{
    public Dictionary<Guid, Life> People { get; } = [];
    public HashSet<(Guid Parent, Guid Child)> ParentLinks { get; } = [];
    public Dictionary<(Guid, Guid), bool> Marriages { get; } = []; // canonical pair -> active
    public HashSet<(Guid, Guid)> Siblings { get; } = [];

    public static (Guid, Guid) Canon(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);

    public void AddRow(RelRow r)
    {
        switch (r.Type)
        {
            case RelationshipType.Parent: ParentLinks.Add((r.A, r.B)); break;
            case RelationshipType.Spouse: Marriages[Canon(r.A, r.B)] = !r.IsEnded && r.End == null; break;
            case RelationshipType.Sibling: Siblings.Add(Canon(r.A, r.B)); break;
        }
    }

    public Tree Clone()
    {
        var t = new Tree();
        foreach (var (k, v) in People) t.People[k] = v;
        t.ParentLinks.UnionWith(ParentLinks);
        foreach (var (k, v) in Marriages) t.Marriages[k] = v;
        t.Siblings.UnionWith(Siblings);
        return t;
    }

    public void RemoveLinksOf(Guid id)
    {
        ParentLinks.RemoveWhere(l => l.Parent == id || l.Child == id);
        foreach (var k in Marriages.Keys.Where(k => k.Item1 == id || k.Item2 == id).ToList()) Marriages.Remove(k);
        Siblings.RemoveWhere(k => k.Item1 == id || k.Item2 == id);
    }

    public void Remove(Guid id)
    {
        RemoveLinksOf(id);
        People.Remove(id);
    }

    public IEnumerable<Guid> ActiveSpousesOf(Guid id) =>
        Marriages.Where(m => m.Value && (m.Key.Item1 == id || m.Key.Item2 == id))
                 .Select(m => m.Key.Item1 == id ? m.Key.Item2 : m.Key.Item1);

    public string Signature(Func<Guid, string> label) => string.Join("\n",
        People.OrderBy(p => p.Key).Select(p => $"{label(p.Key)}: {p.Value}")
            .Concat(ParentLinks.Select(l => $"{label(l.Parent)} parent of {label(l.Child)}").Order())
            .Concat(Marriages.Select(m => $"{label(m.Key.Item1)} {(m.Value ? "married to" : "formerly married to")} {label(m.Key.Item2)}").Order())
            .Concat(Siblings.Select(s => $"{label(s.Item1)} sibling of {label(s.Item2)}").Order()));

    // Everything that can't be right in a family, whatever the screens allow. Each problem
    // starts with a [key] so the coverage test can count which kinds were exercised.
    public List<string> Problems(DateOnly today, Func<Guid, string> label)
    {
        var problems = new List<string>();
        foreach (var (id, p) in People)
        {
            if (string.IsNullOrWhiteSpace(p.First) || string.IsNullOrWhiteSpace(p.Last))
                problems.Add($"[name] {label(id)} has a blank name");
            if (p.Birth > today || p.Death > today)
                problems.Add($"[date-range] {label(id)} has a date in the future");
            if (p.Birth < new DateOnly(1800, 1, 1))
                problems.Add($"[date-range] {label(id)} was born before 1800");
            if (p.Birth >= p.Death)
                problems.Add($"[date-range] {label(id)} died on or before their birth");
        }

        var graph = new ParentGraph(ParentLinks);
        foreach (var (parent, child) in ParentLinks)
        {
            if (parent == child || graph.Descendants(child).Contains(parent))
                problems.Add($"[loop] {label(parent)} parent of {label(child)} closes a loop");
            var error = PlausibilityRules.CheckParentChild(People[parent].Dates, People[child].Dates)
                .FirstOrDefault(f => f.Severity == PlausibilitySeverity.Error);
            if (error != null)
                problems.Add($"[impossible-parent] {error.Message}");
        }

        foreach (var ancestor in People.Keys.Where(id => People[id].Birth != null))
            foreach (var (descendant, generations) in graph.DescendantGenerations(ancestor))
            {
                if (generations < 2 || descendant == ancestor || !People.TryGetValue(descendant, out var d)) continue;
                if (PlausibilityRules.CheckAncestor(People[ancestor].Dates, d.Dates, generations) is { } f)
                    problems.Add($"[impossible-ancestor] {f.Message}");
            }

        foreach (var (parent, child) in ParentLinks)
            if (Marriages.ContainsKey(Canon(parent, child)) || Siblings.Contains(Canon(parent, child)))
                problems.Add($"[relative-link] {label(parent)} is {label(child)}'s parent and also their spouse or sibling");

        foreach (var id in People.Keys)
        {
            var spouses = ActiveSpousesOf(id).ToList();
            if (spouses.Count > 1)
                problems.Add($"[two-marriages] {label(id)} is married to {string.Join(" and ", spouses.Select(label))} at once");
        }

        foreach (var (a, b) in Marriages.Keys.Concat(Siblings).Where(k => k.Item1 == k.Item2))
            problems.Add($"[self-link] {label(a)} is linked to themselves");

        return problems;
    }
}

internal sealed class WritePathDbFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;

    public AppDbContext CreateDbContext() => new(_options);
}

internal sealed class NoAuditLog : IAuditLogService
{
    public Task LogAsync(string action, string entityType, Guid? entityId, string? oldValue = null,
        string? newValue = null, Guid? userId = null, string? ipAddress = null, CancellationToken ct = default)
        => Task.CompletedTask;
}

// The services catch every exception, log it as an error and return "An error occurred
// ...", so a crash looks like an ordinary refusal unless the log is watched.
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Errors { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Error)
            Errors.Add(formatter(state, exception) + (exception is null ? "" : $" ({exception.GetType().Name}: {exception.Message})"));
    }
}
