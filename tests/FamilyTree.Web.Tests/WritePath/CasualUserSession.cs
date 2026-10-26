using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.DTOs.Relationship;
using FamilyTree.Shared.Enums;
using FamilyTree.Web.Services;

namespace FamilyTree.Web.Tests.WritePath;

// One generated edit. Kind picks the action; Args are small ints the action reads as it
// likes (which person, which name, which date...). Everything is taken modulo whatever is
// there when the step runs, so any shrunk sequence is still a valid session.
public sealed record Step(int Kind, int[] Args)
{
    public int Arg(int i) => Args.Length == 0 ? 0 : Args[i % Args.Length];
}

// Plays a sequence of Steps against the real PersonService and RelationshipService (EF Core
// InMemory), as a family Admin using the same screens a casual user would: PersonAdd's
// create, PersonEdit's full-form save, delete, Admin's restore, and PersonAdd's sibling
// links. After every step it checks the outcome against its own idea of what should have
// happened (Tree), and the whole database against rules that hold for any family.
internal sealed class CasualUserSession
{
    private static readonly string[] FirstNames =
        ["Ada", "Ben", "Cora", "Dov", "Elsa", "Finn", "Gail", "Hugo", "Iris", "Jude", "Kira", "Leon", "Mae", "Nate", "Opal", "Pia"];
    private static readonly string[] LastNames = ["Arden", "Brook", "Calder", "Dunmore", "Ellery", "Fenwick"];

    private readonly WritePathDbFactory _db = new();
    private readonly RecordingLogger<PersonService> _personLog = new();
    private readonly RecordingLogger<RelationshipService> _relLog = new();
    private readonly PersonService _people;
    private readonly RelationshipService _relationships;
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    private readonly List<Guid> _created = []; // creation order, so picks are stable
    private readonly Dictionary<Guid, string> _labels = [];

    public List<string> Transcript { get; } = [];
    public List<string> Violations { get; } = [];
    // "add ok", "update refused [loop]", ... counted by the coverage test.
    public List<string> Outcomes { get; } = [];

    public CasualUserSession()
    {
        var admin = new FakeCurrentUserService { UserId = Guid.NewGuid(), FamilyId = Guid.NewGuid(), IsAdmin = true };
        _people = new PersonService(_db, _personLog, new NoAuditLog(), admin);
        _relationships = new RelationshipService(_db, _relLog, new NoAuditLog(), admin);
    }

    private string Label(Guid id) => _labels.GetValueOrDefault(id, id == Placeholder ? "(new person)" : id.ToString()[..8]);

    private static readonly Guid Placeholder = new("00000000-0000-0000-0000-00000000ffff");

    public async Task RunAsync(IReadOnlyList<Step> steps, bool layoutEveryStep = false)
    {
        for (var i = 0; i < steps.Count && Violations.Count == 0; i++)
        {
            var before = await World.ReadAsync(_db);
            var attempt = Plan(steps[i], before);
            if (attempt is null)
            {
                Transcript.Add($"{i + 1}. (nothing to do)");
                continue;
            }
            await TryAsync(i + 1, attempt, before);
            if (Violations.Count == 0) CheckDatabase(await World.ReadAsync(_db), $"after step {i + 1}");
            if (layoutEveryStep && Violations.Count == 0) await CheckLayoutAsync($"after step {i + 1}");
        }
        if (Violations.Count == 0 && !layoutEveryStep) await CheckLayoutAsync("at the end");
    }

    public string Report() =>
        "Session:\n  " + string.Join("\n  ", Transcript) + "\nViolations:\n  " + string.Join("\n  ", Violations);

    // ─────────────────────────────────────────────────────────────────────────
    // ONE ATTEMPT
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record Outcome(bool Success, string? Message, Guid? CreatedId = null);

    // Kind: "add", "update", ... Expected: the family as it should be if the write is
    // accepted, given the new person's ID (Placeholder before the call). FieldProblem: a
    // reason the form itself is invalid (blank or overlong name).
    private sealed record Attempt(
        string Kind, string What, Func<Guid, Tree> Expected, Func<Task<Outcome>> Call,
        string? FieldProblem = null, Guid? Subject = null);

    private async Task TryAsync(int n, Attempt attempt, World before)
    {
        var beforeTree = before.Logical();
        var beforeProblems = beforeTree.Problems(_today, Label);
        var expected = attempt.Expected(Placeholder);
        var why = attempt.FieldProblem ??
                  expected.Problems(_today, Label).Except(beforeProblems).FirstOrDefault();

        Outcome outcome;
        var logged = _personLog.Errors.Count + _relLog.Errors.Count;
        try
        {
            outcome = await attempt.Call();
        }
        catch (Exception ex)
        {
            Transcript.Add($"{n}. {attempt.What} -> THREW {ex.GetType().Name}");
            Violations.Add($"step {n} threw {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (outcome.CreatedId is { } newId && outcome.Success) Name(newId, attempt);
        Transcript.Add($"{n}. {attempt.What} -> {(outcome.Success ? "saved" : $"refused: {outcome.Message}")}");
        var key = why is null ? "" : " " + why[..(why.IndexOf(']') + 1)];
        Outcomes.Add($"{attempt.Kind} {(outcome.Success ? "ok" : "refused")}{key}");

        var errors = _personLog.Errors.Concat(_relLog.Errors).Skip(logged).ToList();
        foreach (var e in errors) Violations.Add($"step {n} logged an error (a crash the user saw as a refusal): {e}");

        if (why != null && outcome.Success)
            Violations.Add($"step {n} was saved, but it shouldn't be possible: {why}");
        if (why == null && !outcome.Success)
            Violations.Add($"step {n} was refused, but nothing is wrong with it: \"{outcome.Message}\"");

        var after = await World.ReadAsync(_db);
        if (!outcome.Success)
        {
            if (string.IsNullOrWhiteSpace(outcome.Message))
                Violations.Add($"step {n} was refused with no message");
            if (!after.SameAs(before))
                Violations.Add($"step {n} was refused but still changed the database");
            return;
        }

        var actual = after.Logical().Signature(Label);
        var wanted = attempt.Expected(outcome.CreatedId ?? Placeholder).Signature(Label);
        if (actual != wanted)
            Violations.Add($"step {n} saved something other than what was asked:\n" + Diff(wanted, actual));
    }

    private void Name(Guid id, Attempt attempt)
    {
        _created.Add(id);
        var first = attempt.What.Split('\'')[1].Trim();
        _labels[id] = $"{(first.Length > 12 ? first[..12] + "..." : first)}#{_created.Count}";
    }

    private static string Diff(string wanted, string actual)
    {
        var w = wanted.Split('\n');
        var a = actual.Split('\n');
        return "    missing: " + string.Join("; ", w.Except(a)) + "\n    unexpected: " + string.Join("; ", a.Except(w));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WHOLE-DATABASE RULES (after every step)
    // ─────────────────────────────────────────────────────────────────────────

    private void CheckDatabase(World world, string when)
    {
        var problems = new List<string>();
        foreach (var r in world.Rels.Where(r => r.DeletedAt == null))
        {
            if (!world.IsLive(r.A) || !world.IsLive(r.B))
                problems.Add($"a live {r.Type} link between {Label(r.A)} and {Label(r.B)} involves a deleted person");
            if (r.Type is RelationshipType.Spouse or RelationshipType.Sibling && r.A.CompareTo(r.B) >= 0)
                problems.Add($"{r.Type} link {Label(r.A)}-{Label(r.B)} isn't stored lower ID first");
            if (r.Start != null)
                problems.Add($"{r.Type} link {Label(r.A)}-{Label(r.B)} has a start date nobody typed");
            if (r.End is { } end)
            {
                var deaths = world.People.Where(p => p.Id == r.A || p.Id == r.B).Select(p => p.Death).OfType<DateOnly>().ToList();
                if (deaths.Count == 0 || deaths.Min() != end)
                    problems.Add($"marriage {Label(r.A)}-{Label(r.B)} ended {end}, which isn't the earlier partner's death date " +
                                 $"({(deaths.Count == 0 ? "neither has died" : deaths.Min().ToString())})");
            }
        }
        // SQL Server's unique index on (PersonAId, PersonBId, Type) covers soft-deleted rows
        // too; InMemory doesn't enforce it, so this stands in for it.
        foreach (var dup in world.Rels.GroupBy(r => (r.A, r.B, r.Type)).Where(g => g.Count() > 1))
            problems.Add($"{dup.Count()} {dup.Key.Type} rows for {Label(dup.Key.A)}-{Label(dup.Key.B)} (SQL Server's unique index would refuse)");

        problems.AddRange(world.Logical().Problems(_today, Label));
        foreach (var p in problems) Violations.Add($"{when}: {p}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LAYOUT (what the family sees afterwards, under both engines)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task CheckLayoutAsync(string when)
    {
        var all = await _people.GetAllAsync();
        if (!all.Success) { Violations.Add($"{when}: loading the tree failed: {all.Message}"); return; }
        var people = all.Data!;
        if (people.Count == 0) return;
        var couples = CoupleHelper.Derive(people);

        foreach (var engine in new IFamilyTreeLayoutEngine[] { new FamilyTreeLayoutEngine(), new TimelineLayoutEngine() })
        {
            var name = engine.GetType().Name;
            FamilyTreeLayout layout;
            try
            {
                layout = engine.ComputeLayout(people, couples, people[0].Id);
            }
            catch (Exception ex)
            {
                Violations.Add($"{when}: {name} threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var counts = layout.Nodes.GroupBy(n => n.Person.Id).ToDictionary(g => g.Key, g => g.Count());
            foreach (var p in people.Where(p => counts.GetValueOrDefault(p.Id) != 1))
                Violations.Add($"{when}: {name} drew {Label(p.Id)} {counts.GetValueOrDefault(p.Id)} times");
            foreach (var node in layout.Nodes.Where(n => !double.IsFinite(n.X) || !double.IsFinite(n.Y)))
                Violations.Add($"{when}: {name} put {Label(node.Person.Id)} at a non-finite position");

            // The archived engine is frozen (ADR 004: fixes only if production breaks), so it is
            // held to "draws everyone, once, somewhere"; where it puts them on odd data is
            // recorded in docs/TodoList.md (B7), not asserted. The prototype is held to more.
            if (engine is FamilyTreeLayoutEngine) continue;

            var nodes = layout.Nodes.GroupBy(n => n.Person.Id).ToDictionary(g => g.Key, g => g.First());
            var plausible = PlausibleFamilies(people);
            Outcomes.Add(plausible.Count == people.Count ? "layout checked" : "layout partly skipped (implausible dates)");
            foreach (var child in people.Where(c => plausible.Contains(c.Id)))
                foreach (var pid in child.ParentIds ?? [])
                {
                    if (nodes.TryGetValue(pid, out var pn) && nodes.TryGetValue(child.Id, out var cn) && cn.Y <= pn.Y)
                        Violations.Add($"{when}: {name} drew {Label(child.Id)} (y={cn.Y}) level with or above parent {Label(pid)} (y={pn.Y})");
                }
        }
    }

    // Implausible but allowed data can leave no row where a child sits below every parent
    // while partners and siblings stay near each other, which is what both engines try to
    // do: a parent "aged 5" saved after a warning; a grandparent 15 years older than the
    // grandchild with the parent between undated; an undated man whose mother was born in
    // 1981 married to a woman who died in 1987; someone recorded as both a sibling and a
    // niece of the same person. Where to draw such a family is the data's doing, so the
    // child-below-parent check applies only to a family that passes two tests, each a system
    // of difference constraints solved by Bellman-Ford (feasible exactly when it has no
    // negative cycle):
    //   years: birth years can be filled in with every parent at least MinParentAge older
    //     than their child, and partners and linked siblings within PartnerYears;
    //   generations: every child at least one generation below each parent, and linked
    //     siblings in the same generation (partners may be of different generations).
    private const int PartnerYears = 40;

    // Returns the people in families that pass, family by family.
    private static HashSet<Guid> PlausibleFamilies(List<PersonDto> people)
    {
        var byId = people.ToDictionary(p => p.Id);
        var family = people.ToDictionary(p => p.Id, p => p.Id);
        Guid Find(Guid x) => family[x] == x ? x : family[x] = Find(family[x]);
        foreach (var p in people)
            foreach (var q in Relatives(p).Where(byId.ContainsKey))
                family[Find(p.Id)] = Find(q);

        var plausible = new HashSet<Guid>();
        foreach (var members in people.GroupBy(p => Find(p.Id)))
        {
            var zero = Guid.Empty; // the "year 0" this family's recorded birth years count from
            var years = new List<(Guid From, Guid To, int W)>(); // x[To] - x[From] <= W
            var generations = new List<(Guid From, Guid To, int W)>();
            foreach (var p in members)
            {
                if (p.BirthDate is { } b)
                {
                    years.Add((zero, p.Id, b.Year));
                    years.Add((p.Id, zero, -b.Year));
                }
                foreach (var parent in (p.ParentIds ?? []).Where(byId.ContainsKey))
                {
                    years.Add((p.Id, parent, -PlausibilityRules.MinParentAge));
                    generations.Add((p.Id, parent, -1));
                }
                foreach (var q in (p.SpouseIds ?? []).Concat(p.FormerSpouseIds ?? []).Concat(p.SiblingIds ?? []).Where(byId.ContainsKey))
                    years.Add((p.Id, q, PartnerYears));
                foreach (var q in (p.SiblingIds ?? []).Where(byId.ContainsKey))
                    generations.Add((p.Id, q, 0));
            }
            var ids = members.Select(p => p.Id).Append(zero).ToList();
            if (Feasible(ids, years) && Feasible(ids, generations))
                plausible.UnionWith(members.Select(p => p.Id));
        }
        return plausible;
    }

    private static bool Feasible(List<Guid> ids, List<(Guid From, Guid To, int W)> edges)
    {
        var dist = ids.ToDictionary(id => id, _ => 0L);
        for (var i = 0; i < ids.Count; i++)
            foreach (var (from, to, w) in edges)
                if (dist[from] + w < dist[to]) dist[to] = dist[from] + w;
        return edges.All(e => dist[e.From] + e.W >= dist[e.To]);
    }

    private static IEnumerable<Guid> Relatives(PersonDto p) =>
        (p.ParentIds ?? []).Concat(p.ChildIds ?? []).Concat(p.SpouseIds ?? [])
            .Concat(p.FormerSpouseIds ?? []).Concat(p.SiblingIds ?? []);

    // ─────────────────────────────────────────────────────────────────────────
    // ACTIONS
    // ─────────────────────────────────────────────────────────────────────────

    private enum Role { Parent, Child, Spouse, FormerSpouse, Sibling }

    private Attempt? Plan(Step s, World world)
    {
        var tree = world.Logical();
        var live = _created.Where(tree.People.ContainsKey).ToList();
        var deleted = _created.Where(id => !tree.People.ContainsKey(id)).ToList();
        var k = s.Kind % 100;

        if (k < 30 || live.Count == 0) return PlanAdd(s, tree, live);
        var p = live[s.Arg(0) % live.Count];
        return k switch
        {
            < 40 => PlanAddRelative(s, tree, live, p),
            < 47 => PlanEdit(tree, p, "removes a relative", (dto, _) => RemoveOneRelative(dto, s.Arg(1))),
            < 57 => PlanEdit(tree, p, "changes dates", (dto, _) => ChangeDates(dto, s, tree)),
            < 62 => PlanEdit(tree, p, "ends a marriage", (dto, _) => Move(dto.SpouseIds, dto.FormerSpouseIds, s.Arg(1))),
            < 65 => PlanEdit(tree, p, "takes back an ended marriage", (dto, _) => Move(dto.FormerSpouseIds, dto.SpouseIds, s.Arg(1))),
            < 70 => PlanEdit(tree, p, "renames", (dto, _) => { (dto.FirstName, dto.LastName) = TypedName(s.Arg(1), s.Arg(2), s.Arg(3), tree); return "to '" + dto.FirstName + "'"; }),
            < 74 => PlanEdit(tree, p, "changes gender", (dto, _) => { dto.Gender = TypedGender(s.Arg(1)); return $"to {dto.Gender?.ToString() ?? "blank"}"; }),
            < 81 => PlanDelete(tree, p),
            < 88 => deleted.Count == 0 ? null : PlanRestore(world, tree, deleted[s.Arg(1) % deleted.Count]),
            < 96 => PlanLinkSibling(tree, live, p, s.Arg(1)),
            < 97 => PlanAddBetween(s, tree, live),
            < 98 => PlanWrongChild(tree, live, p),
            _ => PlanEdit(tree, p, "saves without changing anything", (_, _) => ""),
        };
    }

    // "Add a person" with no dates, as the child of one person and the parent of another:
    // the missing link between two people already on the tree, picked casually.
    private Attempt PlanAddBetween(Step s, Tree tree, List<Guid> live)
    {
        var dto = new PersonUpsertDto();
        (dto.FirstName, dto.LastName) = TypedName(4, s.Arg(1), s.Arg(2), tree);
        var above = live[s.Arg(3) % live.Count];
        var below = live[s.Arg(4) % live.Count];
        dto.ParentIds.Add(above);
        if (below != above) dto.ChildIds.Add(below);
        return new Attempt("add", $"adds '{dto.FirstName}' {Describe(dto)} between parent {Label(above)} and child {Label(below)}",
            id => Apply(tree, id, dto, isNew: true),
            async () =>
            {
                var r = await _people.CreateAsync(dto);
                return new Outcome(r.Success, r.Message, r.Data?.Id);
            });
    }

    // Picks the wrong one of two people in the list: as a child, someone born before one of
    // this person's own dated ancestors.
    private Attempt PlanWrongChild(Tree tree, List<Guid> live, Guid p)
    {
        var graph = new ParentGraph(tree.ParentLinks);
        var oldest = graph.Ancestors(p).Select(a => tree.People[a].Birth).OfType<DateOnly>().DefaultIfEmpty(DateOnly.MaxValue).Min();
        var wrong = live.FirstOrDefault(q => q != p && tree.People[q].Birth < oldest);
        if (wrong == Guid.Empty) return PlanEdit(tree, p, "saves without changing anything", (_, _) => "");
        return PlanEdit(tree, p, $"adds Child {Label(wrong)} (the wrong one from the list)", (dto, _) =>
        {
            if (!dto.ChildIds.Contains(wrong)) dto.ChildIds.Add(wrong);
            return "";
        });
    }

    private Attempt PlanAdd(Step s, Tree tree, List<Guid> live)
    {
        var dto = new PersonUpsertDto();
        (dto.FirstName, dto.LastName) = TypedName(s.Arg(0), s.Arg(1), s.Arg(2), tree);
        dto.Gender = TypedGender(s.Arg(5));

        var relatives = new List<(Role Role, Guid Id)>();
        var count = live.Count == 0 ? 0 : (s.Arg(6) % 4) switch { 0 => 0, 3 => 2, _ => 1 };
        for (var i = 0; i < count; i++)
        {
            var role = (Role)((s.Arg(6) / (4 * (i + 1))) % 5);
            var id = live[(s.Arg(7) / (i == 0 ? 1 : 7)) % live.Count];
            if (relatives.Any(r => r.Id == id && r.Role == role)) continue;
            relatives.Add((role, id));
            ListFor(dto, role).Add(id);
        }

        int? anchor = relatives.Count == 0 ? null : AnchorYear(relatives[0].Role, tree.People[relatives[0].Id], s.Arg(3));
        dto.BirthDate = TypedDate(s.Arg(3), anchor);
        dto.DeathDate = TypedDeath(s.Arg(4), dto.BirthDate);

        var what = $"adds '{dto.FirstName}' {Describe(dto)}" +
                   string.Concat(relatives.Select(r => $", {r.Role} {Label(r.Id)}"));
        return new Attempt("add", what,
            id => Apply(tree, id, dto, isNew: true),
            async () =>
            {
                var r = await _people.CreateAsync(dto);
                return new Outcome(r.Success, r.Message, r.Data?.Id);
            },
            FieldProblem(dto));
    }

    private Attempt PlanAddRelative(Step s, Tree tree, List<Guid> live, Guid p)
    {
        var role = (Role)(s.Arg(1) % 5);
        var others = live.Where(id => id != p).ToList();
        if (others.Count == 0) return PlanEdit(tree, p, "saves without changing anything", (_, _) => "");
        var q = others[s.Arg(2) % others.Count];
        return PlanEdit(tree, p, $"adds {role} {Label(q)}", (dto, _) =>
        {
            var list = ListFor(dto, role);
            if (!list.Contains(q)) list.Add(q);
            // The form's spouse and former-spouse pickers: a former spouse picked as a
            // current one moves across, as PersonForm does.
            if (role == Role.Spouse) dto.FormerSpouseIds.Remove(q);
            return "";
        });
    }

    private Attempt PlanEdit(Tree tree, Guid p, string verb, Func<PersonUpsertDto, Tree, string> change)
    {
        var dto = new PersonUpsertDto(); // filled in when the step runs, from the person as loaded
        var current = tree.People[p];
        PersonUpsertDto Loaded()
        {
            var d = new PersonUpsertDto
            {
                FirstName = current.First, LastName = current.Last, BirthDate = current.Birth, DeathDate = current.Death,
                Gender = current.Gender,
                ParentIds = [.. tree.ParentLinks.Where(l => l.Child == p).Select(l => l.Parent)],
                ChildIds = [.. tree.ParentLinks.Where(l => l.Parent == p).Select(l => l.Child)],
                SpouseIds = [.. tree.Marriages.Where(m => m.Value && Has(m.Key, p)).Select(m => Other(m.Key, p))],
                FormerSpouseIds = [.. tree.Marriages.Where(m => !m.Value && Has(m.Key, p)).Select(m => Other(m.Key, p))],
                SiblingIds = [.. tree.Siblings.Where(x => Has(x, p)).Select(x => Other(x, p))],
            };
            return d;
        }
        dto = Loaded();
        var detail = change(dto, tree);
        var what = $"edits {Label(p)}: {verb}{(detail.Length > 0 ? " " + detail : "")}";
        return new Attempt("update", what,
            _ => Apply(tree, p, dto, isNew: false),
            async () =>
            {
                // What PersonEdit does: load the person, start the form from them, save it all.
                var loaded = await _people.GetByIdAsync(p);
                if (!loaded.Success) return new Outcome(false, "could not load: " + loaded.Message);
                var form = FromDto(loaded.Data!);
                if (!SameLinks(form, Loaded()))
                    return new Outcome(false, "the loaded person's relatives differ from the database");
                return await Save(p, dto);
            },
            FieldProblem(dto), p);
    }

    private async Task<Outcome> Save(Guid p, PersonUpsertDto dto)
    {
        var r = await _people.UpdateAsync(p, dto);
        return new Outcome(r.Success, r.Message);
    }

    private Attempt PlanDelete(Tree tree, Guid p) =>
        new("delete", $"deletes {Label(p)}",
            _ => { var t = tree.Clone(); t.Remove(p); return t; },
            async () => { var r = await _people.DeleteAsync(p); return new Outcome(r.Success, r.Message); },
            Subject: p);

    // Restoring brings back the links deleted along with the person, except those to
    // someone who is still deleted: they come back when that person does. A marriage comes
    // back ended if its spouse has a current one, or the person has a newer one coming back.
    private Attempt PlanRestore(World world, Tree tree, Guid p)
    {
        var row = world.People.Single(x => x.Id == p);
        return new Attempt("restore", $"restores {Label(p)}",
            _ =>
            {
                var t = tree.Clone();
                t.People[p] = new Life(row.First, row.Last, row.Birth, row.Death, row.Gender);
                var back = world.Rels
                    .Where(r => r.DeletedAt != null && r.DeletedAt == row.DeletedAt && (r.A == p || r.B == p))
                    .Where(r => t.People.ContainsKey(r.A == p ? r.B : r.A))
                    .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
                    .ToList();
                foreach (var r in back)
                {
                    if (r.Type == RelationshipType.Spouse && !r.IsEnded && r.End == null)
                    {
                        var spouse = r.A == p ? r.B : r.A;
                        if (t.ActiveSpousesOf(spouse).Any() || t.ActiveSpousesOf(p).Any())
                        {
                            t.AddRow(r with { IsEnded = true });
                            continue;
                        }
                    }
                    t.AddRow(r);
                }
                return t;
            },
            async () => { var r = await _people.RestoreAsync(p); return new Outcome(r.Success, r.Message); },
            Subject: p);
    }

    private Attempt PlanLinkSibling(Tree tree, List<Guid> live, Guid p, int pick)
    {
        var others = live.Where(id => id != p).ToList();
        if (others.Count == 0) return PlanEdit(tree, p, "saves without changing anything", (_, _) => "");
        var q = others[pick % others.Count];
        var already = tree.Siblings.Contains(Tree.Canon(p, q));
        return new Attempt("link-sibling", $"links {Label(p)} and {Label(q)} as siblings (PersonAdd's dialog)",
            _ => { var t = tree.Clone(); t.Siblings.Add(Tree.Canon(p, q)); return t; },
            async () =>
            {
                var r = await _relationships.CreateAsync(new RelationshipUpsertDto { PersonAId = p, PersonBId = q, Type = RelationshipType.Sibling });
                return new Outcome(r.Success, r.Message);
            },
            already ? "[duplicate] already siblings" : null);
    }

    // The family after saving dto as person id: their own fields, and their links replaced
    // by the form's lists (what PersonEdit's save means).
    private static Tree Apply(Tree before, Guid id, PersonUpsertDto dto, bool isNew)
    {
        var t = before.Clone();
        if (!isNew) t.RemoveLinksOf(id);
        t.People[id] = new Life(dto.FirstName.Trim(), dto.LastName.Trim(), dto.BirthDate, dto.DeathDate, dto.Gender);
        foreach (var q in dto.ParentIds) t.ParentLinks.Add((q, id));
        foreach (var q in dto.ChildIds) t.ParentLinks.Add((id, q));
        foreach (var q in dto.SiblingIds) t.Siblings.Add(Tree.Canon(id, q));
        foreach (var q in dto.FormerSpouseIds) t.Marriages[Tree.Canon(id, q)] = false;
        foreach (var q in dto.SpouseIds) t.Marriages[Tree.Canon(id, q)] = true;
        return t;
    }

    private static string? FieldProblem(PersonUpsertDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FirstName) || string.IsNullOrWhiteSpace(dto.LastName))
            return "[name] blank name";
        if (dto.FirstName.Length > 100 || dto.LastName.Length > 100)
            return "[name] name over 100 characters";
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WHAT A CASUAL USER TYPES
    // ─────────────────────────────────────────────────────────────────────────

    private (string First, string Last) TypedName(int style, int f, int l, Tree tree)
    {
        var first = FirstNames[f % FirstNames.Length];
        var last = LastNames[l % LastNames.Length];
        return (style % 20) switch
        {
            0 => ("   ", last),                                   // left blank
            1 => ($"  {first} ", $"{last}  "),                    // stray spaces
            2 => (first + new string('a', 100), last),            // pasted something long
            3 when tree.People.Count > 0 => (tree.People.Values.First().First, tree.People.Values.First().Last), // a second "Ada Arden"
            _ => (first, last),
        };
    }

    private static Gender? TypedGender(int r) => (r % 5) switch
    {
        0 or 1 => null,
        2 => Gender.Male,
        3 => Gender.Female,
        _ => Gender.Unknown,
    };

    private static int? AnchorYear(Role role, Life relative, int r)
    {
        if (relative.Birth is not { } b) return null;
        var gap = 18 + r % 28;
        return role switch
        {
            Role.Parent => b.Year + gap,        // the relative is the new person's parent
            Role.Child => b.Year - gap,
            Role.Spouse or Role.FormerSpouse => b.Year + (r % 13) - 6,
            _ => b.Year + (r % 21) - 10,
        };
    }

    // Half the time blank; mostly plausible; sometimes the slips people make.
    private DateOnly? TypedDate(int r, int? anchorYear)
    {
        var month = 1 + (r / 20) % 12;
        var day = 1 + (r / 240) % 28;
        var year = anchorYear ?? 1880 + (r / 7) % 130;
        return (r % 20) switch
        {
            < 10 => null,
            < 16 => Safe(year, month, day),
            16 => Safe(year + 100, month, day),               // 2052 for 1952
            17 => Safe(_today.Year + 1 + r % 3, month, day),  // the future
            18 => Safe(1700 + r % 99, month, day),            // before 1800
            _ => Safe(year, 1, 1),                            // only knew the year
        };
    }

    private DateOnly? TypedDeath(int r, DateOnly? birth)
    {
        var month = 1 + (r / 10) % 12;
        var day = 1 + (r / 120) % 28;
        return (r % 10) switch
        {
            < 7 => null,
            7 or 8 => birth is { } b ? Safe(b.Year + 30 + r % 70, month, day) : Safe(1900 + r % 120, month, day),
            _ => birth is { } b2 ? Safe(b2.Year - 1 - r % 5, month, day) : null, // died before birth
        };
    }

    private static DateOnly Safe(int year, int month, int day) => new(Math.Clamp(year, 1, 9999), month, day);

    private string ChangeDates(PersonUpsertDto dto, Step s, Tree tree)
    {
        var anchor = dto.ParentIds.Concat(dto.ChildIds).Select(id => tree.People[id]).FirstOrDefault(x => x.Birth != null);
        int? year = anchor is null ? dto.BirthDate?.Year
            : AnchorYear(dto.ParentIds.Any(id => tree.People[id] == anchor) ? Role.Parent : Role.Child, anchor, s.Arg(2));
        switch (s.Arg(1) % 4)
        {
            case 0: dto.BirthDate = TypedDate(s.Arg(2), year); break;
            case 1: dto.DeathDate = TypedDeath(s.Arg(3), dto.BirthDate); break;
            case 2: dto.BirthDate = TypedDate(s.Arg(2), year); dto.DeathDate = TypedDeath(s.Arg(3), dto.BirthDate); break;
            default: dto.DeathDate = Safe(_today.Year - s.Arg(3) % 60, 1 + s.Arg(2) % 12, 1); break; // "they died in ..."
        }
        return Describe(dto);
    }

    private static string RemoveOneRelative(PersonUpsertDto dto, int pick)
    {
        var all = new[] { dto.ParentIds, dto.ChildIds, dto.SpouseIds, dto.FormerSpouseIds, dto.SiblingIds }
            .SelectMany(list => list.Select(id => (list, id))).ToList();
        if (all.Count == 0) return "(has none)";
        var (l, chosen) = all[pick % all.Count];
        l.Remove(chosen);
        return "";
    }

    private static string Move(List<Guid> from, List<Guid> to, int pick)
    {
        if (from.Count == 0) return "(has none)";
        var id = from[pick % from.Count];
        from.Remove(id);
        to.Add(id);
        return "";
    }

    private static List<Guid> ListFor(PersonUpsertDto dto, Role role) => role switch
    {
        Role.Parent => dto.ParentIds,
        Role.Child => dto.ChildIds,
        Role.Spouse => dto.SpouseIds,
        Role.FormerSpouse => dto.FormerSpouseIds,
        _ => dto.SiblingIds,
    };

    private static string Describe(PersonUpsertDto dto) =>
        $"(b. {dto.BirthDate?.ToString("yyyy-MM-dd") ?? "?"}, d. {dto.DeathDate?.ToString("yyyy-MM-dd") ?? "-"}{(dto.Gender is { } g ? ", " + g : "")})";

    private static PersonUpsertDto FromDto(PersonDto p) => new()
    {
        FirstName = p.FirstName, LastName = p.LastName, BirthDate = p.BirthDate, DeathDate = p.DeathDate, Gender = p.Gender,
        ParentIds = p.ParentIds ?? [], ChildIds = p.ChildIds ?? [], SpouseIds = p.SpouseIds ?? [],
        FormerSpouseIds = p.FormerSpouseIds ?? [], SiblingIds = p.SiblingIds ?? [],
    };

    private static bool SameLinks(PersonUpsertDto a, PersonUpsertDto b) =>
        a.ParentIds.ToHashSet().SetEquals(b.ParentIds) && a.ChildIds.ToHashSet().SetEquals(b.ChildIds) &&
        a.SpouseIds.ToHashSet().SetEquals(b.SpouseIds) && a.FormerSpouseIds.ToHashSet().SetEquals(b.FormerSpouseIds) &&
        a.SiblingIds.ToHashSet().SetEquals(b.SiblingIds);

    private static bool Has((Guid, Guid) pair, Guid id) => pair.Item1 == id || pair.Item2 == id;
    private static Guid Other((Guid, Guid) pair, Guid id) => pair.Item1 == id ? pair.Item2 : pair.Item1;
}
