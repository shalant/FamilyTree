using FamilyTree.Core.Models;
using FamilyTree.Shared.Enums;

namespace FamilyTree.Core.Services;

public enum DataCheckRule
{
    /// <summary>A person's own dates (<see cref="PlausibilityRules.CheckRecordedDates"/>, <see cref="PlausibilityRules.CheckPerson"/>).</summary>
    Dates,
    /// <summary>A parent and child (<see cref="PlausibilityRules.CheckParentChild"/>).</summary>
    ParentChild,
    /// <summary>An ancestor and descendant with nobody dated in between.</summary>
    Ancestor,
    /// <summary>A marriage's dates (<see cref="PlausibilityRules.CheckMarriage"/>).</summary>
    Marriage,
    /// <summary>Recorded siblings born generations apart (<see cref="PlausibilityRules.CheckSiblings"/>).</summary>
    Siblings,
    /// <summary>Two people recorded as related in two ways that can't both be true.</summary>
    RoleClash,
    /// <summary>A date equal to the day the record was entered.</summary>
    EntryDay,
    /// <summary>Close relatives whose surnames are a letter or two apart.</summary>
    Surname,
    /// <summary>Two records that may be the same person.</summary>
    Duplicate,
}

/// <param name="IsImpossible">True when the data can't be right (a write-path refusal the row
/// predates); false when it's only unlikely.</param>
public sealed record DataCheckFinding(DataCheckRule Rule, bool IsImpossible, string Message, IReadOnlyList<Guid> PersonIds);

/// <summary>
/// The "Data check" admin list (B8, 2026-10-27): finds data that is probably a slip, across a
/// whole family, for a person to look at. Never changes anything. The write path only judges
/// what a save adds, so rows saved before a rule existed (or by an older import path) are
/// found only here. Date rules live in <see cref="PlausibilityRules"/>, so the form, the
/// server and this list agree; the name rules (surnames, duplicates) live here.
///
/// Pure: give it one family's live people and relationships. Relationships to anyone not in
/// <c>people</c> are ignored, so a legacy cross-family row can't leak a name into a finding.
/// Only biological Parent links count as parentage, as in the write path.
/// </summary>
public static class DataCheckRules
{
    /// <summary>A date this close to the record's CreatedAt (UTC) counts as "the day it was
    /// entered", which leaves room for the user's time zone.</summary>
    private const int EntryDaySlackDays = 1;

    /// <summary>Two records born further apart than this are taken to be different people.</summary>
    private const int DuplicateBirthSlackDays = 2 * 365;

    public static List<DataCheckFinding> Find(
        IReadOnlyCollection<Person> people, IReadOnlyCollection<Relationship> relationships, DateOnly today)
    {
        var byId = people.ToDictionary(p => p.Id);
        var links = relationships
            .Where(r => r.PersonAId != r.PersonBId && byId.ContainsKey(r.PersonAId) && byId.ContainsKey(r.PersonBId))
            .ToList();
        var tree = new Tree(byId, links);

        var findings = new List<DataCheckFinding>();
        findings.AddRange(PersonDates(people, today));
        findings.AddRange(ParentChild(tree));
        findings.AddRange(Ancestors(tree));
        findings.AddRange(Marriages(tree));
        findings.AddRange(Siblings(tree));
        findings.AddRange(RoleClashes(tree));
        findings.AddRange(EntryDays(tree));
        findings.AddRange(Surnames(tree));
        findings.AddRange(Duplicates(tree));

        return [.. findings
            .DistinctBy(f => (f.Rule, f.Message))
            .OrderBy(f => f.Rule)
            .ThenBy(f => f.Message, StringComparer.Ordinal)];
    }

    private sealed class Tree
    {
        public Dictionary<Guid, Person> People { get; }
        public List<Relationship> Links { get; }
        public Dictionary<Guid, List<Guid>> ChildrenOf { get; } = new();
        public Dictionary<Guid, List<Guid>> ParentsOf { get; } = new();
        public Dictionary<Guid, HashSet<Guid>> SiblingsOf { get; } = new();

        public Tree(Dictionary<Guid, Person> people, List<Relationship> links)
        {
            People = people;
            Links = links;
            foreach (var r in links.Where(r => r.Type == RelationshipType.Parent))
            {
                Add(ChildrenOf, r.PersonAId, r.PersonBId);
                Add(ParentsOf, r.PersonBId, r.PersonAId);
            }

            // Siblings: an explicit Sibling link, or a shared parent (as PersonMapper infers).
            foreach (var r in links.Where(r => r.Type == RelationshipType.Sibling))
            {
                AddSibling(r.PersonAId, r.PersonBId);
            }
            foreach (var kids in ChildrenOf.Values)
                foreach (var a in kids)
                    foreach (var b in kids)
                        if (a != b) AddSibling(a, b);
        }

        private void AddSibling(Guid a, Guid b)
        {
            if (!SiblingsOf.TryGetValue(a, out var sa)) SiblingsOf[a] = sa = [];
            if (!SiblingsOf.TryGetValue(b, out var sb)) SiblingsOf[b] = sb = [];
            sa.Add(b);
            sb.Add(a);
        }

        private static void Add(Dictionary<Guid, List<Guid>> map, Guid from, Guid to)
        {
            if (!map.TryGetValue(from, out var list)) map[from] = list = [];
            if (!list.Contains(to)) list.Add(to);
        }

        public IReadOnlyList<Guid> Children(Guid id) => ChildrenOf.TryGetValue(id, out var c) ? c : [];
        public IReadOnlyList<Guid> Parents(Guid id) => ParentsOf.TryGetValue(id, out var p) ? p : [];
        public IReadOnlyCollection<Guid> Siblings(Guid id) => SiblingsOf.TryGetValue(id, out var s) ? s : [];

        public bool Linked(Guid a, Guid b) =>
            Links.Any(r => (r.PersonAId == a && r.PersonBId == b) || (r.PersonAId == b && r.PersonBId == a));

        public LifeDates Dates(Guid id) => PersonService.ToLifeDates(People[id]);
        public string Name(Guid id) => Name(People[id]);
        public static string Name(Person p) => $"{p.FirstName} {p.LastName}".Trim();
    }

    private static DataCheckFinding From(DataCheckRule rule, PlausibilityFinding f, params Guid[] ids) =>
        new(rule, f.Severity == PlausibilitySeverity.Error, f.Message, ids);

    private static IEnumerable<DataCheckFinding> PersonDates(IEnumerable<Person> people, DateOnly today)
    {
        foreach (var p in people)
        {
            var name = Tree.Name(p);
            foreach (var f in PlausibilityRules.CheckRecordedDates(p.BirthDate, p.DeathDate, today))
                yield return new(DataCheckRule.Dates, true, $"{name}: {f.Message}", [p.Id]);
            foreach (var f in PlausibilityRules.CheckPerson(PersonService.ToLifeDates(p), today))
                yield return From(DataCheckRule.Dates, f, p.Id);
        }
    }

    private static IEnumerable<DataCheckFinding> ParentChild(Tree t) =>
        t.Links.Where(r => r.Type == RelationshipType.Parent)
            .SelectMany(r => PlausibilityRules.CheckParentChild(t.Dates(r.PersonAId), t.Dates(r.PersonBId))
                .Select(f => From(DataCheckRule.ParentChild, f, r.PersonAId, r.PersonBId)));

    // An ancestor against each dated descendant reached through undated people only. If
    // anyone in between is dated, a contradiction shows up on a shorter pair (or a single
    // parent link) instead, so each problem is reported once, where it can be fixed.
    private static IEnumerable<DataCheckFinding> Ancestors(Tree t)
    {
        foreach (var (id, person) in t.People)
        {
            if (person.BirthDate is null) continue;
            var ancestor = t.Dates(id);
            var seen = new HashSet<Guid> { id };
            var queue = new Queue<(Guid Id, int Generations)>();
            foreach (var c in t.Children(id)) queue.Enqueue((c, 1));

            while (queue.Count > 0)
            {
                var (at, g) = queue.Dequeue();
                if (!seen.Add(at)) continue;
                if (t.People[at].BirthDate is null)
                {
                    foreach (var c in t.Children(at)) queue.Enqueue((c, g + 1));
                    continue;
                }
                if (g < 2) continue;

                var descendant = t.Dates(at);
                var f = PlausibilityRules.CheckAncestor(ancestor, descendant, g)
                        ?? PlausibilityRules.CheckAncestorGap(ancestor, descendant, g);
                if (f is not null) yield return From(DataCheckRule.Ancestor, f, id, at);
            }
        }
    }

    private static IEnumerable<DataCheckFinding> Marriages(Tree t) =>
        t.Links.Where(r => r.Type == RelationshipType.Spouse)
            .SelectMany(r => PlausibilityRules.CheckMarriage(t.Dates(r.PersonAId), t.Dates(r.PersonBId), r.StartDate, r.EndDate)
                .Select(f => From(DataCheckRule.Marriage, f, r.PersonAId, r.PersonBId)));

    private static IEnumerable<DataCheckFinding> Siblings(Tree t) =>
        t.Links.Where(r => r.Type == RelationshipType.Sibling)
            .Select(r => (r, f: PlausibilityRules.CheckSiblings(t.Dates(r.PersonAId), t.Dates(r.PersonBId))))
            .Where(x => x.f is not null)
            .Select(x => From(DataCheckRule.Siblings, x.f!, x.r.PersonAId, x.r.PersonBId));

    private static IEnumerable<DataCheckFinding> RoleClashes(Tree t)
    {
        // A parent (or child) who is also a spouse or sibling. The write path refuses this
        // since B7 (2026-10-25); older rows can still have it.
        var parentPairs = t.Links.Where(r => r.Type == RelationshipType.Parent)
            .Select(r => (Parent: r.PersonAId, Child: r.PersonBId)).ToList();
        foreach (var (parent, child) in parentPairs)
        {
            var other = t.Links.FirstOrDefault(r => r.Type is RelationshipType.Spouse or RelationshipType.Sibling &&
                ((r.PersonAId == parent && r.PersonBId == child) || (r.PersonAId == child && r.PersonBId == parent)));
            if (other is null) continue;
            var role = other.Type == RelationshipType.Spouse ? "spouse" : "sibling";
            yield return new(DataCheckRule.RoleClash, true,
                $"{t.Name(parent)} is recorded as both {t.Name(child)}'s parent and {t.Name(child)}'s {role}.",
                [parent, child]);
        }

        // Someone who is both a sibling and a niece or nephew of the same person (B7): their
        // own parent is recorded as that person's sibling.
        foreach (var x in t.People.Keys)
            foreach (var parent in t.Parents(x))
                foreach (var y in t.Siblings(parent))
                {
                    if (y == x || !t.Siblings(x).Contains(y)) continue;
                    yield return new(DataCheckRule.RoleClash, false,
                        $"{t.Name(x)} is recorded as both {t.Name(y)}'s sibling and {t.Name(y)}'s niece or nephew " +
                        $"(through {t.Name(parent)}).",
                        [x, y, parent]);
                }
    }

    // A date equal to the day the record was entered is often "today" left in a date field.
    private static IEnumerable<DataCheckFinding> EntryDays(Tree t)
    {
        static bool OnEntryDay(DateOnly? date, DateTime? created) =>
            date is { } d && created is { } c &&
            Math.Abs(d.DayNumber - DateOnly.FromDateTime(c).DayNumber) <= EntryDaySlackDays;

        foreach (var p in t.People.Values)
        {
            if (OnEntryDay(p.BirthDate, p.CreatedAt))
                yield return EntryDay(p.Id, $"{Tree.Name(p)}'s birth date ({Format(p.BirthDate!.Value)})");
            if (OnEntryDay(p.DeathDate, p.CreatedAt))
                yield return EntryDay(p.Id, $"{Tree.Name(p)}'s death date ({Format(p.DeathDate!.Value)})");
        }

        foreach (var r in t.Links.Where(r => r.Type == RelationshipType.Spouse))
        {
            var couple = $"{t.Name(r.PersonAId)} and {t.Name(r.PersonBId)}";
            if (OnEntryDay(r.StartDate, r.CreatedAt))
                yield return EntryDay(r.PersonAId, $"The start of {couple}'s marriage ({Format(r.StartDate!.Value)})", r.PersonBId);
            if (OnEntryDay(r.EndDate, r.CreatedAt))
                yield return EntryDay(r.PersonAId, $"The end of {couple}'s marriage ({Format(r.EndDate!.Value)})", r.PersonBId);
        }

        static DataCheckFinding EntryDay(Guid id, string what, Guid? other = null) =>
            new(DataCheckRule.EntryDay, false,
                $"{what} is the day the record was entered. If it was left as today's date, correct or clear it.",
                other is { } o ? [id, o] : [id]);
    }

    // Close relatives (parent and child, or siblings) whose surnames are a letter or two
    // apart: "Schumevith" vs "Schmulevith". Spouses are left out, since their surnames often
    // differ for a reason.
    private static IEnumerable<DataCheckFinding> Surnames(Tree t)
    {
        var pairs = t.Links.Where(r => r.Type == RelationshipType.Parent)
            .Select(r => (r.PersonAId, r.PersonBId))
            .Concat(t.SiblingsOf.SelectMany(kv => kv.Value.Select(s => (kv.Key, s))))
            .Select(p => p.Item1.CompareTo(p.Item2) < 0 ? (A: p.Item1, B: p.Item2) : (A: p.Item2, B: p.Item1))
            .Distinct();

        foreach (var (a, b) in pairs)
        {
            var x = t.People[a].LastName.Trim();
            var y = t.People[b].LastName.Trim();
            if (x.Length < 4 || y.Length < 4 || string.Equals(x, y, StringComparison.OrdinalIgnoreCase)) continue;
            var d = NameSimilarity.Distance(x, y);
            if (d > 2) continue;
            yield return new(DataCheckRule.Surname, false,
                $"{t.Name(a)} and {t.Name(b)} are close relatives whose surnames differ by {(d == 1 ? "one letter" : "two letters")} " +
                $"(\"{x}\" and \"{y}\"). Is one a typo?",
                [a, b]);
        }
    }

    // Two records that may be the same person: similar surnames (or one's maiden name is the
    // other's surname) and the same first name, or two forms of it ("Bill" and "William").
    // Left out: people linked to each other, siblings, and people born more than two years
    // apart.
    private static IEnumerable<DataCheckFinding> Duplicates(Tree t)
    {
        var list = t.People.Values.OrderBy(p => p.Id).ToList();
        for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
            {
                var (p, q) = (list[i], list[j]);
                if (!SimilarSurname(p, q) || !SimilarFirstName(p.FirstName, q.FirstName)) continue;
                if (p.Gender is { } pg && q.Gender is { } qg && pg != qg) continue;
                if (p.BirthDate is { } pb && q.BirthDate is { } qb &&
                    Math.Abs(pb.DayNumber - qb.DayNumber) > DuplicateBirthSlackDays) continue;
                if (t.Linked(p.Id, q.Id) || t.Siblings(p.Id).Contains(q.Id)) continue;

                yield return new(DataCheckRule.Duplicate, false,
                    $"{Tree.Name(p)}{Born(p)} and {Tree.Name(q)}{Born(q)} may be the same person.",
                    [p.Id, q.Id]);
            }

        static string Born(Person p) => p.BirthDate is { } b ? $" (born {Format(b)})" : "";

        static bool SimilarSurname(Person p, Person q)
        {
            bool Close(string? a, string? b) =>
                !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
                NameSimilarity.Distance(a.Trim(), b.Trim()) <= (Math.Min(a.Trim().Length, b.Trim().Length) >= 4 ? 1 : 0);
            return Close(p.LastName, q.LastName) || Close(p.MaidenName, q.LastName) || Close(p.LastName, q.MaidenName);
        }

        // The same name, two forms of one name, or (for names of four letters or more) one
        // letter apart, so "Jon"/"John" and "Elliot"/"Eliot" count but "Max"/"Mae" doesn't.
        // A name and its feminine form ("Johann"/"Johanna", "Louis"/"Louise") are one letter
        // apart too, but they're two people, often brother and sister.
        static bool SimilarFirstName(string a, string b)
        {
            if (NameSimilarity.SameFirstName(a, b)) return true;
            var (x, y) = (a.Trim(), b.Trim());
            if (Math.Min(x.Length, y.Length) < 4 || NameSimilarity.Distance(x, y) > 1) return false;
            var (shorter, longer) = x.Length < y.Length ? (x, y) : (y, x);
            var feminine = longer.Length == shorter.Length + 1 &&
                           longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase) &&
                           (char.ToLowerInvariant(longer[^1]) is 'a' or 'e');
            return !feminine;
        }
    }

    private static string Format(DateOnly d) => d.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
