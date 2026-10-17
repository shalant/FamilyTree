using System.Text;
using CsCheck;
using FamilyTree.Core.Mappers;
using FamilyTree.Core.Models;
using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using FamilyTree.Web.Services;
using Xunit;
using Xunit.Sdk;

namespace FamilyTree.Web.Tests.ServiceTests;

// Property-based fuzz harness for FamilyTreeLayoutEngine (Leg 1 of
// docs/Testing/fuzz-and-adversarial-review-plan.md, added 2026-10-10).
//
// Every layout bug found so far came from a genealogy shape nobody had hand-written a
// test for yet (see CLAUDE.md, "Where Layout Bugs Hide"). Instead of hand-writing more
// scenarios, this generates random genealogies (couples, remarriages, former spouses,
// unmarried co-parents, orphan siblings, cross-root marriages, dateless people,
// disconnected components) and checks invariants that must hold for ANY tree.
//
// The generated data goes through the production derivation path, not a
// reimplementation of it: Person + Relationship rows -> PersonMapper.MapPersonToDto ->
// LastName/FirstName ordering (PersonService.GetAllAsync) -> CoupleHelper.Derive ->
// ComputeLayout.
//
// Runs are deterministic: iteration i uses PCG stream i with a pinned base seed, so CI
// sees the same ~200 cases every time. On failure CsCheck shrinks the case to a minimal
// family and the message carries a seed that replays it. Override via env vars:
//   FAMILYTREE_FUZZ_ITER=<n>      iterations per invariant (default 200)
//   FAMILYTREE_FUZZ_SEED=<ulong>  base seed (default PinnedSeed)
//   FAMILYTREE_FUZZ_SOAK=1        enables the local-only soak test (random seed, 5000 cases)
//
// Known-open failures: the full-domain test is [Fact(Skip = ...)] naming its
// docs/TodoList.md "[LAYOUT FUZZ 2026-10-10]" entry, and where a useful domain is left, an
// "_ExcludingKnownOpen" twin runs the SAME invariant over families free of the known-open
// shapes (the KnownOpen enum), so the harness keeps guarding everything else meanwhile.
// The invariant itself is never loosened to get green. When a bug is fixed, un-skip its
// full-domain test(s) and drop its KnownOpen flag.
public class FamilyTreeLayoutFuzzTests
{
    // Mirrors of private constants in FamilyTreeLayoutEngine. If those change, these must too.
    private const int SpouseSpacingX = 200;

    private const ulong PinnedSeed = 20261010;
    private static int Iterations => int.TryParse(Environment.GetEnvironmentVariable("FAMILYTREE_FUZZ_ITER"), out var n) ? n : 200;
    private static ulong BaseSeed => ulong.TryParse(Environment.GetEnvironmentVariable("FAMILYTREE_FUZZ_SEED"), out var s) ? s : PinnedSeed;

    private const string SkipPrefix = "Known open, see docs/TodoList.md '[LAYOUT FUZZ 2026-10-10]' ";

    // ─────────────────────────────────────────────────────────────────────────
    // INVARIANTS
    // ─────────────────────────────────────────────────────────────────────────

    // 1. No two nodes overlap: center distance >= sum of radii.
    [Fact(Skip = SkipPrefix + "R4 (exact-Y collision check, the open 2026-09-09 bug, which also " +
                 "hits dateless families), R9 (no vertical clearance check in PlaceGroup), plus R2.")]
    public void Invariant1_NoTwoNodesOverlap() => Run(GenFamily, AssertNoOverlap);

    [Fact]
    public void Invariant1_NoTwoNodesOverlap_ExcludingKnownOpen() =>
        Run(Excluding(Invariant1Exclusions), AssertNoOverlap);

    private const KnownOpen Invariant1Exclusions =
        KnownOpen.PartnerWithChildrenElsewhere |
        KnownOpen.DatelessPerson | KnownOpen.AnchorPlacedPerson |
        KnownOpen.PartnerSameEraAsInLaw;

    // 2. Every input person appears exactly once in the output, and nobody else does.
    [Fact]
    public void Invariant2_EveryPersonAppearsExactlyOnce() => Run(GenFamily, AssertEveryPersonOnce);

    private static void AssertEveryPersonOnce(BuiltFamily f)
    {
        var layout = f.Layout();
        var errors = new List<string>();
        var counts = layout.Nodes.GroupBy(n => n.Person.Id).ToDictionary(g => g.Key, g => g.Count());
        foreach (var p in f.People)
        {
            var c = counts.GetValueOrDefault(p.Id);
            if (c != 1) errors.Add($"{f.Label(p.Id)} appears {c} times");
        }
        foreach (var id in counts.Keys.Where(id => f.People.All(p => p.Id != id)))
            errors.Add($"unknown id {id} in output");
        Fail(errors);
    }

    // 3. A child's Y is strictly below every parent's Y, including when nobody has a date.
    // Generated birth years are always consistent (child >= 20 years after each parent),
    // so any inversion comes from inference/placement, not from contradictory input.
    // No _ExcludingKnownOpen twin: the shape this guards (dateless people) is R3, now fixed, and with
    // every date known the invariant is true by construction.
    [Fact]
    public void Invariant3_ChildRendersBelowEveryParent() => Run(GenFamily, f =>
    {
        var nodes = f.Layout().Nodes.ToDictionary(n => n.Person.Id);
        var errors = new List<string>();
        foreach (var child in f.People)
            foreach (var pid in child.ParentIds ?? [])
                if (nodes[child.Id].Y <= nodes[pid].Y)
                    errors.Add($"child {f.Label(child.Id)} Y={nodes[child.Id].Y} is not below parent {f.Label(pid)} Y={nodes[pid].Y}");
        Fail(errors);
    });

    // 4. Couples sit within SpouseSpacingX of each other, and share a Y when neither
    // partner has a birth date.
    //
    // Deliberate refinement of the plan's "couples share a Y": the Y axis is a birth-year
    // timeline (CLAUDE.md, ADR 001), so two DATED spouses born years apart are supposed to
    // sit at different heights. Only when the engine has no date for either partner is a
    // Y difference an artifact rather than data. The X half is asserted unconditionally.
    [Fact(Skip = SkipPrefix + "R2, R6 (cross-root partners the post-placement pull can't bring together), R7.")]
    public void Invariant4_CouplesSitTogether() => Run(GenFamily, AssertCouplesTogether);

    [Fact]
    public void Invariant4_CouplesSitTogether_ExcludingKnownOpen() =>
        Run(Excluding(Invariant4Exclusions), AssertCouplesTogether);

    private const KnownOpen Invariant4Exclusions =
        KnownOpen.PartnerWithChildrenElsewhere | KnownOpen.CrossRootCouple |
        KnownOpen.SpouseAnchoredPartner;

    // 5a. Determinism: the same input, laid out twice, gives identical output.
    [Fact]
    public void Invariant5a_SameInputGivesSameOutput() => Run(GenFamily, AssertDeterministic);

    private static void AssertDeterministic(BuiltFamily f)
    {
        var a = Snapshot(f.Layout());
        var b = Snapshot(f.Clone().Layout());
        Fail(a.SequenceEqual(b) ? [] : ["two layouts of the identical input differ"]);
    }

    // 5b. Shuffling the input list does not change relative placement (the left/right and
    // above/below order of every pair of nodes).
    [Fact]
    public void Invariant5b_ShufflingInputDoesNotChangeRelativePlacement() =>
        Run(GenFamily, f => AssertSameRelativePlacement(f, f.Shuffled(tiesOnly: false)));

    // 5c. Production-reachable form of 5b: PersonService.GetAllAsync orders by LastName then
    // FirstName, and SQL Server returns rows that tie on both in no guaranteed order. The
    // same family must render the same way whichever order the tied rows come back in.
    // No _ExcludingKnownOpen twin: without name ties the reorder is a no-op.
    [Fact]
    public void Invariant5c_ReorderingNameTiesDoesNotChangeRelativePlacement() =>
        Run(GenFamily, f => AssertSameRelativePlacement(f, f.Shuffled(tiesOnly: true)));

    // 6. Adding one childless leaf must not move any node in a component the leaf is not
    // connected to. A component may shift as a rigid whole (a wider neighbour pushes it
    // right; a new earliest/latest birth year moves the timeline origin), but nodes inside
    // it must keep their positions relative to each other. This is the general form of the
    // "classification transition" property in CLAUDE.md.
    [Fact]
    public void Invariant6_AddingALeafDoesNotMoveUnrelatedComponents() => Run(GenFamily, AssertLeafStability);

    private static void AssertLeafStability(BuiltFamily f)
    {
        var after = f.WithLeaf();
        var beforeNodes = f.Layout().Nodes.ToDictionary(n => n.Person.Id);
        var afterNodes = after.Layout().Nodes.ToDictionary(n => n.Person.Id);
        var errors = new List<string>();
        foreach (var comp in f.Components().Where(c => !c.Contains(f.LeafParentId)))
        {
            var origin = comp[0];
            var dx0 = afterNodes[origin].X - beforeNodes[origin].X;
            var dy0 = afterNodes[origin].Y - beforeNodes[origin].Y;
            foreach (var id in comp)
            {
                var dx = afterNodes[id].X - beforeNodes[id].X;
                var dy = afterNodes[id].Y - beforeNodes[id].Y;
                if (dx != dx0 || dy != dy0)
                    errors.Add($"{f.Label(id)} moved by ({dx},{dy}) but its component moved by ({dx0},{dy0}) " +
                               $"after adding a leaf under unrelated {f.Label(f.LeafParentId)}");
            }
        }
        if (errors.Count > 0)
            errors.Add("family after adding the leaf:\n" + after.Dump() +
                       "\nnode X before -> after: " + string.Join(", ", f.People.Select(p =>
                           $"{f.Label(p.Id).Split(' ')[0]} {beforeNodes[p.Id].X}->{afterNodes[p.Id].X}")));
        Fail(errors);
    }

    // 7. No exception and no NaN/infinite coordinates on malformed input: dangling ids in
    // every relationship list, self-references, duplicate ids, null lists, asymmetric
    // Parent/Child lists, spouse-who-is-also-sibling, and a dangling focus/anchor.
    // Parent cycles are covered separately (Invariants 7b and 7c): they used to crash the
    // test host outright (R0, fixed 2026-10-11).
    [Fact]
    public void Invariant7_MalformedInputNeverThrowsOrProducesNonFiniteCoordinates() =>
        Run(GenMalformed, f => AssertFiniteLayout(f.Layout()));

    // 7b. Minimal parent cycle reachable from a root. R0 (fixed 2026-10-11): before the fix
    // PlaceGroup/MeasureGroup/MarkDescendants recursed forever on this and the StackOverflow
    // killed the whole test host ('Test host process crashed : Stack overflow', confirmed
    // 2026-10-10). The write paths now refuse to record a cycle, and the engine survives one
    // that is already in the data.
    [Fact]
    public void Invariant7b_ParentCycleDoesNotCrash()
    {
        // Root+Root2 -> A -> B -> A. A third parent is what makes the cycle reachable: with
        // only two, A's cycle parent always forms a couple group with A's root-side parent,
        // and that group is never a root, so PlaceGroup never descends into the cycle.
        var root = Guid.NewGuid();
        var root2 = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var people = new List<PersonDto>
        {
            new() { Id = root, FirstName = "Root", LastName = "Cycle", ParentIds = [], ChildIds = [a], SpouseIds = [root2] },
            new() { Id = root2, FirstName = "Root2", LastName = "Cycle", ParentIds = [], ChildIds = [a], SpouseIds = [root] },
            new() { Id = a, FirstName = "A", LastName = "Cycle", ParentIds = [root, root2, b], ChildIds = [b] },
            new() { Id = b, FirstName = "B", LastName = "Cycle", ParentIds = [a], ChildIds = [a] },
        };
        AssertFiniteLayout(new FamilyTreeLayoutEngine().ComputeLayout(people, CoupleHelper.Derive(people), root));
    }

    // 7c. Generalises 7b: random families (any shape the generator makes) with one or two
    // parent cycles injected through the real PersonMapper path, including a person recorded
    // as their own parent. A cycle must never crash or hang the engine, every person must
    // still be drawn exactly once, coordinates must stay finite, and the layout must stay
    // deterministic. The ONLY thing these families don't promise is a pretty layout: a
    // cycle is bad data, and which edge the engine ignores to survive it is not specified.
    [Fact]
    public void Invariant7c_RandomParentCyclesNeverCrashAndEveryoneIsStillDrawn() =>
        Run(GenCyclic, f =>
        {
            AssertFiniteLayout(f.Layout());
            AssertEveryPersonOnce(f);
            AssertDeterministic(f);
        });

    // The cyclic generator must really produce cycles, or 7c passes vacuously.
    [Fact]
    public void GeneratorCyclic_ActuallyProducesParentCycles()
    {
        var withCycle = 0;
        for (var i = 0; i < Iterations; i++)
            if (HasParentCycle(Build(GenCyclic.Generate(new PCG((uint)i, BaseSeed), null, out _)).People))
                withCycle++;
        Fail(withCycle >= Iterations * 9 / 10
            ? []
            : [$"only {withCycle}/{Iterations} cyclic families contained a parent cycle (need 90%)"]);
    }

    private static bool HasParentCycle(List<PersonDto> people)
    {
        var byId = people.ToDictionary(p => p.Id);
        var done = new HashSet<Guid>();
        var onPath = new HashSet<Guid>();
        bool Visit(Guid id)
        {
            if (onPath.Contains(id)) return true;
            if (!done.Add(id)) return false;
            onPath.Add(id);
            foreach (var parent in byId[id].ParentIds ?? [])
                if (byId.ContainsKey(parent) && Visit(parent)) return true;
            onPath.Remove(id);
            return false;
        }
        return people.Any(p => Visit(p.Id));
    }

    // Local-only soak: thousands of cases from a random base seed, every check that is
    // active in CI (known-open full-domain invariants excluded). Prints the base seed so a failure can be replayed with
    // FAMILYTREE_FUZZ_SEED.
    [SoakFact]
    public void Soak_AllActiveInvariants()
    {
        var seed = (ulong)Random.Shared.NextInt64();
        var iterations = Environment.GetEnvironmentVariable("FAMILYTREE_FUZZ_ITER") is { } s && int.TryParse(s, out var n) ? n : 5000;
        var checks = new (string Name, Gen<FamilySpec> Gen, Action<BuiltFamily> Assert)[]
        {
            ("1 (excluding known open)", Excluding(Invariant1Exclusions), AssertNoOverlap),
            ("2", GenFamily, AssertEveryPersonOnce),
            ("4 (excluding known open)", Excluding(Invariant4Exclusions), AssertCouplesTogether),
            ("5a", GenFamily, AssertDeterministic),
            ("6", GenFamily, AssertLeafStability),
            ("7", GenMalformed, f => AssertFiniteLayout(f.Layout())),
            ("7c", GenCyclic, f => { AssertFiniteLayout(f.Layout()); AssertEveryPersonOnce(f); AssertDeterministic(f); }),
        };
        foreach (var (name, gen, assert) in checks)
        {
            try
            {
                Run(gen, assert, iterations, seed);
            }
            catch (Exception ex)
            {
                throw new XunitException($"Soak: invariant {name} failed with FAMILYTREE_FUZZ_SEED={seed}\n{ex.Message}");
            }
        }
    }

    // Sanity check on the harness itself: the generator really does produce the shapes the
    // invariants are meant to exercise. A generator that silently stopped producing, say,
    // cross-root marriages would leave the invariants passing vacuously.
    [Fact]
    public void Generator_CoversTheInterestingShapes()
    {
        var seen = new Dictionary<string, int>
        {
            ["dateless person"] = 0, ["entirely dateless family"] = 0, ["former spouse"] = 0,
            ["two spouses at once"] = 0, ["orphan sibling"] = 0, ["unmarried co-parents"] = 0,
            ["cross-root marriage"] = 0, ["multiple components"] = 0, ["3+ generations"] = 0,
            ["name tie"] = 0, ["3+ recorded parents"] = 0,
        };
        for (var i = 0; i < Iterations; i++)
        {
            var f = Build(GenFamily.Generate(new PCG((uint)i, BaseSeed), null, out _));
            foreach (var shape in f.Shapes()) seen[shape]++;
        }
        var missing = seen.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();
        Fail(missing.Select(m => $"generator never produced: {m}").ToList());
    }

    // The _ExcludingKnownOpen domains must still contain real trees, or those tests pass
    // vacuously. Checks each domain still produces couples, 3+ generations, dateless
    // people where allowed, and several components.
    [Theory]
    [InlineData(nameof(Invariant1_NoTwoNodesOverlap_ExcludingKnownOpen))]
    [InlineData(nameof(Invariant4_CouplesSitTogether_ExcludingKnownOpen))]
    public void ExcludingKnownOpenDomains_StillProduceRealTrees(string test)
    {
        var excluded = test switch
        {
            nameof(Invariant1_NoTwoNodesOverlap_ExcludingKnownOpen) => Invariant1Exclusions,
            _ => Invariant4Exclusions,
        };
        var families = Enumerable.Range(0, Iterations)
            .Select(i => Build(Excluding(excluded).Generate(new PCG((uint)i, BaseSeed), null, out _)))
            .ToList();
        var errors = new List<string>();
        void Require(string what, int count, int min)
        {
            if (count < min) errors.Add($"only {count}/{families.Count} families had {what} (need {min})");
        }
        Require("a couple", families.Count(f => f.Couples.Count > 0), families.Count / 4);
        Require("3+ generations", families.Count(f => f.Shapes().Contains("3+ generations")), families.Count / 10);
        Require("2+ components", families.Count(f => f.Shapes().Contains("multiple components")), families.Count / 10);
        if ((excluded & KnownOpen.DatelessPerson) == 0)
            Require("a dateless person", families.Count(f => f.Shapes().Contains("dateless person")), families.Count / 10);
        Fail(errors);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INVARIANT BODIES (shared with the soak test)
    // ─────────────────────────────────────────────────────────────────────────

    private static void AssertNoOverlap(BuiltFamily f)
    {
        var nodes = f.Layout().Nodes;
        var errors = new List<string>();
        for (var i = 0; i < nodes.Count; i++)
            for (var j = i + 1; j < nodes.Count; j++)
            {
                var a = nodes[i];
                var b = nodes[j];
                var dist = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
                var minDist = (a.Size + b.Size) / 2.0;
                if (dist < minDist)
                    errors.Add($"{f.Label(a.Person.Id)} at ({a.X},{a.Y}) r={a.Size / 2} overlaps " +
                               $"{f.Label(b.Person.Id)} at ({b.X},{b.Y}) r={b.Size / 2} (distance {dist:F1} < {minDist})");
            }
        Fail(errors);
    }

    private static void AssertCouplesTogether(BuiltFamily f)
    {
        var nodes = f.Layout().Nodes.ToDictionary(n => n.Person.Id);
        var byId = f.People.ToDictionary(p => p.Id);
        var errors = new List<string>();
        foreach (var c in f.Couples)
        {
            var a = nodes[c.PersonAId];
            var b = nodes[c.PersonBId];
            var kind = c.IsFormer ? "former couple" : "couple";
            if (Math.Abs(a.X - b.X) > SpouseSpacingX)
                errors.Add($"{kind} {f.Label(a.Person.Id)} x={a.X} and {f.Label(b.Person.Id)} x={b.X} are " +
                           $"{Math.Abs(a.X - b.X)}px apart (> SpouseSpacingX {SpouseSpacingX})");
            if (byId[c.PersonAId].BirthDate is null && byId[c.PersonBId].BirthDate is null && a.Y != b.Y)
                errors.Add($"dateless {kind} {f.Label(a.Person.Id)} y={a.Y} and {f.Label(b.Person.Id)} y={b.Y} do not share a row");
        }
        Fail(errors);
    }

    private static void AssertSameRelativePlacement(BuiltFamily original, BuiltFamily reordered)
    {
        var a = original.Layout().Nodes.ToDictionary(n => n.Person.Id);
        var b = reordered.Layout().Nodes.ToDictionary(n => n.Person.Id);
        var ids = original.People.Select(p => p.Id).ToList();
        var errors = new List<string>();
        for (var i = 0; i < ids.Count; i++)
            for (var j = i + 1; j < ids.Count; j++)
            {
                var (p, q) = (ids[i], ids[j]);
                var sx1 = Math.Sign(a[p].X - a[q].X);
                var sx2 = Math.Sign(b[p].X - b[q].X);
                var sy1 = Math.Sign(a[p].Y - a[q].Y);
                var sy2 = Math.Sign(b[p].Y - b[q].Y);
                if (sx1 != sx2 || sy1 != sy2)
                    errors.Add($"{original.Label(p)} vs {original.Label(q)}: x-order {sx1}->{sx2}, y-order {sy1}->{sy2}");
            }
        if (errors.Count > 0)
            errors.Insert(0, "input order " + string.Join(", ", original.People.Select(p => original.Label(p.Id))) +
                             "\n  reordered to " + string.Join(", ", reordered.People.Select(p => original.Label(p.Id))));
        Fail(errors);
    }

    private static void AssertFiniteLayout(FamilyTreeLayout layout)
    {
        var values = new List<double> { layout.CanvasWidth, layout.CanvasHeight };
        values.AddRange(layout.Nodes.SelectMany(n => new double[] { n.X, n.Y, n.Size }));
        foreach (var u in layout.Connectors)
        {
            values.AddRange([u.Stem.X, u.Stem.TopY, u.Stem.BotY]);
            if (u.Couple is { } c) values.AddRange([c.PartnerAX, c.PartnerAY, c.PartnerBX, c.PartnerBY, c.PeakY, c.MidX, c.HeartY]);
            if (u.Span is { } s) values.AddRange([s.LeftX, s.RightX, s.Y]);
            values.AddRange(u.Drops.SelectMany(d => new[] { d.CenterX, d.BotY }));
        }
        values.AddRange(layout.SiblingLinks.SelectMany(l => new[] { l.AX, l.AY, l.BX, l.BY }));
        values.AddRange(layout.ComponentDividers.SelectMany(d => new[] { d.X, d.TopY, d.BotY }));
        Fail(values.Any(v => !double.IsFinite(v)) ? ["layout contains NaN or infinite coordinates"] : []);
    }

    private static void Fail(IReadOnlyCollection<string> errors)
    {
        if (errors.Count == 0) return;
        throw new XunitException($"{errors.Count} violation(s):\n  " + string.Join("\n  ", errors.Take(12)) +
                                 (errors.Count > 12 ? $"\n  ... and {errors.Count - 12} more" : ""));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DRIVER
    // ─────────────────────────────────────────────────────────────────────────

    private static void Run(Gen<FamilySpec> gen, Action<BuiltFamily> invariant, int? iterations = null, ulong? baseSeed = null)
    {
        var n = iterations ?? Iterations;
        var seed = baseSeed ?? BaseSeed;
        void Assert(FamilySpec spec) => invariant(Build(spec));

        for (var i = 0; i < n; i++)
        {
            var pcg = new PCG((uint)i, seed);
            var state = pcg.Seed;
            var spec = gen.Generate(pcg, null, out _);
            try
            {
                Assert(spec);
            }
            catch (Exception first)
            {
                var replay = pcg.ToString(state);
                string shrunk;
                try
                {
                    // Re-runs the failing seed, then spends the remaining iterations hunting for
                    // a smaller failing family. Always throws, since the seed itself fails.
                    gen.Sample(Assert, seed: replay, iter: 3000, threads: 1, print: s => Build(s).Dump());
                    shrunk = "(shrink pass did not reproduce the failure; original case below)\n" + Build(spec).Dump();
                }
                catch (Exception ex)
                {
                    shrunk = ex.Message;
                }
                throw new XunitException(
                    $"Invariant failed on iteration {i} (base seed {seed}, CsCheck replay seed \"{replay}\").\n" +
                    $"First failure: {first.Message}\n\nShrunk:\n{shrunk}\n\nOriginal case:\n{Build(spec).Dump()}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GENERATOR
    //
    // A family is a list of small integer "recipes" interpreted by Build(). Keeping the
    // generated value this dumb is what makes shrinking work: CsCheck shrinks arrays
    // shorter and ints toward zero, and every op degrades gracefully (picks are taken
    // modulo whatever is eligible, ineligible ops are no-ops).
    // ─────────────────────────────────────────────────────────────────────────

    // Shapes that trigger a known-open, already-logged bug. Each "_ExcludingKnownOpen" test
    // runs its invariant unchanged, but only over families free of the listed shapes, so
    // the harness still guards everything else while the bug is open. When a bug is
    // fixed: drop its flag from the exclusion, un-skip the full-domain test, delete the flag.
    [Flags]
    public enum KnownOpen
    {
        None = 0,
        // R2: a couple only gets a placement slot if it is the primary group of BOTH
        // partners. When one partner also has children outside the couple, and the couple
        // has children of its own, or the other partner has children elsewhere, or the
        // other partner is already placed under their own recorded parents (blended
        // families, children by two partners, the extra co-parent couples CoupleHelper
        // derives for a 3-parent child), the couple loses its slot and the partners land
        // 240px+ apart. (Harvey-style partners with no parents and no children are fine:
        // the safety net anchors them next to their spouse.) TodoList "[LAYOUT FUZZ 2026-10-10] R2".
        PartnerWithChildrenElsewhere = 2,
        // Any person with no birth date. R3 (greedy inference) is fixed, so this now only
        // covers the placement collisions R4/R9 that dateless families also reach (a
        // former-spouse partner placed ~55px from a dated person). Only Invariant 1's twin
        // and the dateless-person richness check still use it.
        DatelessPerson = 4,
        // R4: people placed by FindOpenXAdjacentTo (explicit-sibling orphans, extra spouses;
        // spouse-anchored partners are flagged separately as SpouseAnchoredPartner and must be
        // excluded alongside this) collide with near-miss Y rows. TodoList "[LAYOUT BUG, not yet fixed, found live
        // 2026-09-09]" + "[LAYOUT FUZZ 2026-10-10] R4".
        AnchorPlacedPerson = 8,
        // R6: partners anchored under two different root groups. Since 2026-10-12 the orphan
        // slack sits after the joined families and a leaf partner slides toward the other
        // (within their own parents' span), which fixes the common case, but the pull is
        // still blocked when a sibling holds the inner edge or both partners head families
        // of their own. TodoList "[LAYOUT FUZZ 2026-10-10] R6".
        // R10 (a cross-root couple's children dropped at the midpoint with no collision
        // check) was fixed 2026-10-15, and Invariant 1 no longer excludes this flag; only
        // Invariant 4 still does, for R6. TodoList "[LAYOUT FUZZ 2026-10-10] R10".
        CrossRootCouple = 16,
        // R7: someone in two or more couples, or with children outside a couple. Since
        // 2026-10-15 FindOpenXAdjacentTo takes the nearer free side and checks collisions at
        // the placed person's own row, so these families no longer overlap and Invariant 1
        // no longer excludes them. Invariant 4 still does: a person in three couples can't
        // sit within SpouseSpacingX of all three, and a partner bridging two families that
        // each have their own group (Harvey married to Florence and Dora) needs the
        // families reordered, not a better slot. TodoList "[LAYOUT FUZZ 2026-10-10] R7".
        SpouseAnchoredPartner = 32,
        // 64 was SiblingLinkedPartner (R8, an orphan sibling placed beside their sibling
        // instead of their partner), retired 2026-10-17: no family free of R2 still splits.
        // R9: PlaceGroup positions purely by tree structure and never checks vertical
        // clearance, while Y is a birth-year timeline. A partner placed at their spouse's
        // slot +/-100px can land right under the spouse's parent, and if the two were born
        // within ~12 years of each other (about one node height at PxPerYear 6.5) the nodes
        // overlap. TodoList "[LAYOUT FUZZ 2026-10-10] R9".
        PartnerSameEraAsInLaw = 256,
    }

    // Families free of the given known-open shapes. Rather than rejection-filtering (which
    // starves on big families, nearly all of which contain SOME excluded shape), each op is kept
    // only if the family is still clean with it, and skipped otherwise, so the domain is
    // never empty and shrinking still works. Every founder also starts out married, so "child
    // of a couple" ops have couples to use; ExcludingKnownOpenDomains_StillProduceRealTrees
    // checks the result stays rich.
    private static Gen<FamilySpec> Excluding(KnownOpen shapes) => GenFamily.Select(spec =>
    {
        bool Clean(FamilySpec x) => (Build(x).KnownOpenShapes() & shapes) == 0;
        OpSeed[] marriages = [.. spec.Founders.Select((f, i) => new OpSeed(7, i, 0, 1, f with { Name = f.Name + 7 }))];
        var s = spec with { Ops = [.. marriages, .. spec.Ops] };
        if ((shapes & KnownOpen.DatelessPerson) != 0 || !Clean(s with { Ops = marriages }))
            s = s with { DateMode = 2 };
        if (!Clean(s with { Ops = marriages }))
            return s with { Ops = [] }; // founders only, all dated: nothing left to exclude
        var kept = new List<OpSeed>(marriages);
        foreach (var op in spec.Ops)
            if (Clean(s with { Ops = [.. kept, op] }))
                kept.Add(op);
        return s with { Ops = [.. kept] };
    });

    public sealed record PersonSeed(int Name, int Date, int Jitter);
    public sealed record OpSeed(int Kind, int A, int B, int C, PersonSeed New);

    // DateMode: 0 = nobody has a birth date, 1 = mixed (~75% dated), 2 = everyone dated.
    public sealed record FamilySpec(
        int DateMode, PersonSeed[] Founders, OpSeed[] Ops,
        int FocusPick, int AnchorPick, int LeafPick, int Salt, int Malform, int Cycle = 0);

    private static readonly Gen<PersonSeed> GenPerson =
        Gen.Select(Gen.Int[0, 255], Gen.Int[0, 3], Gen.Int[0, 59], (n, d, j) => new PersonSeed(n, d, j));

    private static readonly Gen<OpSeed> GenOp =
        Gen.Select(Gen.Int[0, 13], Gen.Int[0, 999], Gen.Int[0, 999], Gen.Int[0, 999], GenPerson,
            (k, a, b, c, p) => new OpSeed(k, a, b, c, p));

    private static readonly Gen<FamilySpec> GenFamily =
        Gen.Select(Gen.Int[0, 2], GenPerson.Array[1, 4], GenOp.Array[0, 30],
                   Gen.Select(Gen.Int[0, 999], Gen.Int[0, 999], Gen.Int[0, 999], Gen.Int[0, 1_000_000], (f, a, l, s) => (f, a, l, s)),
            (mode, founders, ops, picks) => new FamilySpec(mode, founders, ops, picks.f, picks.a, picks.l, picks.s, 0));

    // Same families, then a non-zero Malform seed drives post-mapping corruption of the DTOs.
    private static readonly Gen<FamilySpec> GenMalformed =
        Gen.Select(GenFamily, Gen.Int[1, 1_000_000], (spec, m) => spec with { Malform = m });

    // Same families, then a non-zero Cycle seed makes someone their own ancestor.
    private static readonly Gen<FamilySpec> GenCyclic =
        Gen.Select(GenFamily, Gen.Int[1, 1_000_000], (spec, c) => spec with { Cycle = c });

    private static readonly string[] FirstNames =
        ["Ann", "Bill", "Bud", "Dora", "Ellen", "Florence", "Gish", "Harvey", "Lauren", "Marc", "Max", "Ray", "Rose", "Sarah", "Willa", "Doug"];
    private static readonly string[] LastNames = ["Small", "Kuh", "Rosenberg", "Fleishman", "Lee", "Diaz"];

    private sealed class Draft(int index, string first, string last, int year, bool hasDate, int jitter)
    {
        public int Index { get; } = index;
        public string First { get; } = first;
        public string Last { get; } = last;
        public int Year { get; } = year;
        public bool HasDate { get; } = hasDate;
        public int Jitter { get; } = jitter;
        public List<int> Parents { get; } = [];
    }

    private static BuiltFamily Build(FamilySpec spec)
    {
        var drafts = new List<Draft>();
        var active = new HashSet<(int, int)>();
        var former = new HashSet<(int, int)>();
        var siblings = new HashSet<(int, int)>();
        static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        bool HasDate(PersonSeed s) => spec.DateMode switch { 0 => false, 2 => true, _ => s.Date != 0 };

        int Add(PersonSeed s, int year, string? lastName = null)
        {
            var d = new Draft(drafts.Count, FirstNames[s.Name % FirstNames.Length],
                lastName ?? LastNames[s.Name / FirstNames.Length % LastNames.Length], year, HasDate(s), s.Jitter);
            drafts.Add(d);
            return d.Index;
        }

        bool IsAncestor(int ancestor, int of)
        {
            var stack = new Stack<int>(drafts[of].Parents);
            var seen = new HashSet<int>();
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p == ancestor) return true;
                if (seen.Add(p)) foreach (var gp in drafts[p].Parents) stack.Push(gp);
            }
            return false;
        }
        bool BloodLine(int a, int b) => a == b || IsAncestor(a, b) || IsAncestor(b, a);
        bool AreSiblings(int a, int b) => siblings.Contains(Key(a, b)) || drafts[a].Parents.Intersect(drafts[b].Parents).Any();
        bool Partnered(int a, int b) => active.Contains(Key(a, b)) || former.Contains(Key(a, b)) ||
                                        drafts.Any(d => d.Parents.Contains(a) && d.Parents.Contains(b));
        int PartnerCount(int a) => active.Concat(former).Count(k => k.Item1 == a || k.Item2 == a);

        // Someone a could plausibly marry / co-parent with: not blood, not a sibling, not
        // already partnered, and of roughly the same generation.
        List<int> MateCandidates(int a) => drafts.Select(d => d.Index)
            .Where(b => !BloodLine(a, b) && !AreSiblings(a, b) && !Partnered(a, b) &&
                        Math.Abs(drafts[a].Year - drafts[b].Year) <= 12 && PartnerCount(b) < 3)
            .ToList();

        void AddChild(OpSeed op, params int[] parents)
        {
            var year = parents.Max(p => drafts[p].Year) + 20 + op.New.Jitter % 25;
            var child = Add(op.New, year, drafts[parents[op.C % parents.Length]].Last);
            drafts[child].Parents.AddRange(parents.Distinct());
        }

        foreach (var f in spec.Founders)
            Add(f, 1850 + f.Jitter * 2);

        foreach (var op in spec.Ops)
        {
            var a = op.A % drafts.Count;
            switch (op.Kind)
            {
                case 0 or 1 or 2: // child of an existing couple (falls back to a single parent)
                {
                    var couples = active.Concat(former).OrderBy(k => k).ToList();
                    if (couples.Count == 0) { AddChild(op, a); break; }
                    var (x, y) = couples[op.A % couples.Count];
                    AddChild(op, x, y);
                    break;
                }
                case 3: // child of two unmarried co-parents
                {
                    if (PartnerCount(a) >= 3) break;
                    var mates = MateCandidates(a);
                    if (mates.Count == 0) break;
                    AddChild(op, a, mates[op.B % mates.Count]);
                    break;
                }
                case 4: // child with a single recorded parent
                    AddChild(op, a);
                    break;
                case 5 or 6: // marry someone already on the tree (6 = marriage has ended)
                {
                    if (PartnerCount(a) >= 3) break;
                    var mates = MateCandidates(a);
                    if (mates.Count == 0) break;
                    (op.Kind == 5 ? active : former).Add(Key(a, mates[op.B % mates.Count]));
                    break;
                }
                case 7: // marry in a new person with no other recorded relatives
                {
                    if (PartnerCount(a) >= 3) break;
                    var spouse = Add(op.New, drafts[a].Year + op.New.Jitter % 21 - 10);
                    (op.C % 4 == 0 ? former : active).Add(Key(a, spouse));
                    break;
                }
                case 8: // orphan sibling: explicit Sibling link, no shared parent recorded
                {
                    var sib = Add(op.New, drafts[a].Year + op.New.Jitter % 17 - 8, drafts[a].Last);
                    siblings.Add(Key(a, sib));
                    break;
                }
                case 9: // explicit Sibling link between two people already on the tree
                {
                    var cands = drafts.Select(d => d.Index)
                        .Where(b => !BloodLine(a, b) && !AreSiblings(a, b) && !Partnered(a, b) &&
                                    Math.Abs(drafts[a].Year - drafts[b].Year) <= 10)
                        .ToList();
                    if (cands.Count > 0) siblings.Add(Key(a, cands[op.B % cands.Count]));
                    break;
                }
                case 10: // a new unrelated founder (separate component)
                    Add(op.New, 1850 + op.New.Jitter * 2);
                    break;
                case 11 or 12: // child of a person and their first active spouse, if any
                {
                    var spouse = active.Where(k => k.Item1 == a || k.Item2 == a)
                        .Select(k => k.Item1 == a ? k.Item2 : k.Item1).OrderBy(x => x).Cast<int?>().FirstOrDefault();
                    if (spouse is { } s) AddChild(op, a, s); else AddChild(op, a);
                    break;
                }
                case 13: // one more recorded parent (step/adoptive parent entered as Parent;
                         // PersonForm has no cap). Never a descendant, so never a cycle.
                {
                    var parents = drafts[a].Parents;
                    if (parents.Count is 0 or >= 3) break;
                    var cands = drafts.Select(d => d.Index)
                        .Where(p => p != a && !IsAncestor(a, p) && !parents.Contains(p) && drafts[p].Year <= drafts[a].Year - 20)
                        .ToList();
                    if (cands.Count > 0) parents.Add(cands[op.B % cands.Count]);
                    break;
                }
            }
        }

        // R0 injection (Invariant 7c only): record a descendant (or the person themself) as an
        // extra parent of someone, closing a cycle. The ops above never do this on their own.
        if (spec.Cycle != 0)
        {
            var rng = new Random(spec.Cycle);
            for (var k = 0; k < 1 + rng.Next(2); k++)
            {
                var a = rng.Next(drafts.Count);
                var candidates = drafts.Select(x => x.Index).Where(x => x == a || IsAncestor(a, x)).ToList();
                var cyclePick = candidates[rng.Next(candidates.Count)];
                if (!drafts[a].Parents.Contains(cyclePick)) drafts[a].Parents.Add(cyclePick);
            }
        }

        // Person + Relationship rows -> PersonMapper, exactly like PersonService.GetAllAsync.
        var ids = drafts.Select(d => MakeGuid(spec.Salt, d.Index)).ToList();
        var persons = drafts.Select(d => new Person
        {
            Id = ids[d.Index],
            FirstName = d.First,
            LastName = d.Last,
            BirthDate = d.HasDate ? new DateOnly(d.Year, 1 + d.Jitter % 12, 1 + d.Jitter % 28) : null,
        }).ToList();

        var rels = new List<Relationship>();
        Relationship Rel(RelationshipType t, int a, int b, DateOnly? end = null)
        {
            // Canonical ordering (PersonAId < PersonBId) for the bidirectional types.
            var (x, y) = t == RelationshipType.Parent || ids[a].CompareTo(ids[b]) < 0 ? (a, b) : (b, a);
            return new Relationship { Id = Guid.NewGuid(), Type = t, PersonAId = ids[x], PersonBId = ids[y], EndDate = end };
        }
        foreach (var d in drafts)
            foreach (var p in d.Parents)
                rels.Add(Rel(RelationshipType.Parent, p, d.Index));
        foreach (var (a, b) in active.OrderBy(k => k)) rels.Add(Rel(RelationshipType.Spouse, a, b));
        foreach (var (a, b) in former.OrderBy(k => k)) rels.Add(Rel(RelationshipType.Spouse, a, b, new DateOnly(2000, 1, 1)));
        foreach (var (a, b) in siblings.OrderBy(k => k)) rels.Add(Rel(RelationshipType.Sibling, a, b));

        // Stable sort, so ties keep build order here; Shuffled(tiesOnly: true) permutes them.
        var people = persons
            .Select(p => PersonMapper.MapPersonToDto(p, rels))
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .ToList();

        var labels = drafts.ToDictionary(d => ids[d.Index],
            d => $"P{d.Index} {d.First} {d.Last} ({(d.HasDate ? d.Year.ToString() : "no date")})");

        var focus = ids[spec.FocusPick % ids.Count];
        Guid? anchor = spec.AnchorPick % (ids.Count + 1) is var ai && ai < ids.Count ? ids[ai] : null;

        if (spec.Malform != 0)
            (focus, anchor) = Malform(people, spec.Malform, focus, anchor);

        return new BuiltFamily(spec, people, focus, anchor, labels, rels);
    }

    // Corrupts mapped DTOs in ways PersonMapper itself can't produce but a future caller,
    // a stale cache, or a half-applied edit could: dangling ids, self-references, duplicate
    // entries, null lists, asymmetric Parent/Child lists, spouse-who-is-also-sibling. Never
    // adds an EXISTING id to a ParentIds list, which could form a parent cycle (that is Invariant 7c's job).
    private static (Guid focus, Guid? anchor) Malform(List<PersonDto> people, int seed, Guid focus, Guid? anchor)
    {
        var rng = new Random(seed);
        Guid Dangling() => new(rng.Next(), (short)rng.Next(), (short)rng.Next(), new byte[8]);
        PersonDto Any() => people[rng.Next(people.Count)];

        for (var k = 0; k < 1 + rng.Next(6); k++)
        {
            var p = Any();
            switch (rng.Next(10))
            {
                case 0: p.ParentIds = [.. p.ParentIds ?? [], Dangling()]; break;
                case 1: p.ChildIds = [.. p.ChildIds ?? [], Dangling()]; break;
                case 2: p.SpouseIds = [.. p.SpouseIds ?? [], rng.Next(2) == 0 ? Dangling() : p.Id]; break;
                case 3: p.FormerSpouseIds = [.. p.FormerSpouseIds ?? [], rng.Next(2) == 0 ? Dangling() : p.Id]; break;
                case 4: p.SiblingIds = [.. p.SiblingIds ?? [], rng.Next(2) == 0 ? Dangling() : p.Id]; break;
                case 5: p.ChildIds = [.. p.ChildIds ?? [], Any().Id]; break; // asymmetric: child doesn't list p as parent
                case 6: // duplicate entries in every list
                    p.ParentIds = [.. p.ParentIds ?? [], .. p.ParentIds ?? []];
                    p.SpouseIds = [.. p.SpouseIds ?? [], .. p.SpouseIds ?? []];
                    p.SiblingIds = [.. p.SiblingIds ?? [], .. p.SiblingIds ?? []];
                    break;
                case 7: // null lists instead of empty
                    p.ChildIds = null; p.SpouseIds = null; p.FormerSpouseIds = null; p.SiblingIds = null;
                    break;
                case 8: // spouse who is also listed as a sibling
                {
                    var q = Any();
                    if (q.Id == p.Id) break;
                    p.SpouseIds = [.. p.SpouseIds ?? [], q.Id];
                    p.SiblingIds = [.. p.SiblingIds ?? [], q.Id];
                    break;
                }
                case 9:
                    if (rng.Next(2) == 0) focus = Dangling(); else anchor = Dangling();
                    break;
            }
        }
        return (focus, anchor);
    }

    // SplitMix64 over (salt, index): deterministic across runs and processes (unlike
    // HashCode.Combine), and GUID sort order still varies with the salt, which matters
    // because canonical relationship ordering compares GUIDs.
    private static Guid MakeGuid(int salt, int index)
    {
        static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
        var key = ((ulong)(uint)salt << 32) | (uint)index;
        var bytes = new byte[16];
        BitConverter.GetBytes(Mix(key)).CopyTo(bytes, 0);
        BitConverter.GetBytes(Mix(key ^ 0xA5A5A5A5A5A5A5A5)).CopyTo(bytes, 8);
        return new Guid(bytes);
    }

    private static List<(Guid Id, int X, int Y, int Size)> Snapshot(FamilyTreeLayout layout) =>
        layout.Nodes.OrderBy(n => n.Person.Id).Select(n => (n.Person.Id, n.X, n.Y, n.Size)).ToList();

    public sealed class BuiltFamily(
        FamilySpec spec, List<PersonDto> people, Guid focus, Guid? anchor,
        Dictionary<Guid, string> labels, List<Relationship> rels)
    {
        private readonly Dictionary<Guid, string> labels = labels;
        private readonly List<Relationship> rels = rels;
        public FamilySpec Spec { get; } = spec;
        public List<PersonDto> People { get; } = people;
        public List<CoupleDto> Couples { get; } = CoupleHelper.Derive(people);
        public Guid Focus { get; } = focus;
        public Guid? Anchor { get; } = anchor;
        private FamilyTreeLayout? _layout;

        public FamilyTreeLayout Layout() =>
            _layout ??= new FamilyTreeLayoutEngine().ComputeLayout(People, Couples, Focus, Anchor);

        public string Label(Guid id) => labels.GetValueOrDefault(id, id.ToString()[..8]);

        // The person Invariant 6 hangs its new leaf under.
        public Guid LeafParentId => People.OrderBy(p => p.Id).ElementAt(Spec.LeafPick % People.Count).Id;

        public BuiltFamily Clone() => Build(Spec);

        public BuiltFamily WithLeaf()
        {
            var parentIndex = int.Parse(Label(LeafParentId).Split(' ')[0][1..]);
            var leaf = new OpSeed(12, parentIndex, 0, 0, new PersonSeed(Spec.LeafPick, 1, Spec.LeafPick % 60));
            // Keep the SAME focus and anchor: Build() picks them modulo the person count, so a
            // fresh Build would otherwise silently re-pick them, and the anchor drives
            // lineage-side root ordering. Only the leaf may differ between before and after.
            var after = Build(Spec with { Ops = [.. Spec.Ops, leaf] });
            return new BuiltFamily(after.Spec, after.People, Focus, Anchor, after.labels, after.rels);
        }

        public BuiltFamily Shuffled(bool tiesOnly)
        {
            var rng = new Random(Spec.Salt);
            var reordered = tiesOnly
                ? People.GroupBy(p => (p.LastName, p.FirstName))
                        .SelectMany(g => g.OrderBy(_ => rng.Next())).ToList()
                : People.OrderBy(_ => rng.Next()).ToList();
            return new BuiltFamily(Spec, reordered, Focus, Anchor, labels, rels);
        }

        // Genealogical components (Parent, Spouse, former Spouse, Sibling), each listed in
        // stable people-list order.
        public List<List<Guid>> Components()
        {
            var adj = People.ToDictionary(p => p.Id, _ => new List<Guid>());
            foreach (var r in rels)
            {
                adj[r.PersonAId].Add(r.PersonBId);
                adj[r.PersonBId].Add(r.PersonAId);
            }
            var seen = new HashSet<Guid>();
            var result = new List<List<Guid>>();
            foreach (var p in People)
            {
                if (!seen.Add(p.Id)) continue;
                var comp = new List<Guid>();
                var queue = new Queue<Guid>([p.Id]);
                while (queue.Count > 0)
                {
                    var id = queue.Dequeue();
                    comp.Add(id);
                    foreach (var n in adj[id]) if (seen.Add(n)) queue.Enqueue(n);
                }
                var order = People.Select((x, i) => (x.Id, i)).ToDictionary(t => t.Id, t => t.i);
                result.Add([.. comp.OrderBy(id => order[id])]);
            }
            return result;
        }

        // ── Known-open shapes (see the KnownOpen enum) ──
        public KnownOpen KnownOpenShapes()
        {
            var shapes = KnownOpen.None;
            var byId = People.ToDictionary(p => p.Id);
            bool HasParents(Guid id) => byId[id].ParentIds?.Count > 0;
            bool ChildrenOutside(Guid id, CoupleDto c) =>
                People.Any(p => p.ParentIds?.Contains(id) == true && !c.ChildIds.Contains(p.Id));
            bool SplitBy(Guid a, Guid b, CoupleDto c) =>
                ChildrenOutside(a, c) && (c.ChildIds.Count > 0 || ChildrenOutside(b, c) || HasParents(b));
            if (Couples.Any(c => SplitBy(c.PersonAId, c.PersonBId, c) || SplitBy(c.PersonBId, c.PersonAId, c)))
                shapes |= KnownOpen.PartnerWithChildrenElsewhere;
            if (People.Any(p => p.BirthDate is null))
                shapes |= KnownOpen.DatelessPerson;
            if (rels.Any(r => r.Type == RelationshipType.Sibling) ||
                People.Any(p => (p.SpouseIds?.Count ?? 0) + (p.FormerSpouseIds?.Count ?? 0) > 1))
                shapes |= KnownOpen.AnchorPlacedPerson;
            HashSet<Guid> Ancestors(Guid id)
            {
                var result = new HashSet<Guid>();
                var stack = new Stack<Guid>(byId[id].ParentIds ?? []);
                while (stack.Count > 0)
                {
                    var a = stack.Pop();
                    if (!result.Add(a)) continue;
                    if (byId.TryGetValue(a, out var p))
                        foreach (var gp in p.ParentIds ?? []) stack.Push(gp);
                }
                return result;
            }
            if (Couples.Any(c =>
                    HasParents(c.PersonAId) && HasParents(c.PersonBId) && !Ancestors(c.PersonAId).Overlaps(Ancestors(c.PersonBId))))
                shapes |= KnownOpen.CrossRootCouple;
            if (People.Any(p => Couples.Count(c => c.PersonAId == p.Id || c.PersonBId == p.Id) > 1) ||
                Couples.Any(c => ChildrenOutside(c.PersonAId, c) || ChildrenOutside(c.PersonBId, c)))
                shapes |= KnownOpen.SpouseAnchoredPartner;
            bool SameEraAsInLaw(Guid partner, Guid other) =>
                byId[other].BirthDate is { } b && (byId[partner].ParentIds ?? []).Any(p =>
                    byId.TryGetValue(p, out var parent) && parent.BirthDate is { } pb && Math.Abs(pb.Year - b.Year) <= 12);
            if (Couples.Any(c => SameEraAsInLaw(c.PersonAId, c.PersonBId) || SameEraAsInLaw(c.PersonBId, c.PersonAId)))
                shapes |= KnownOpen.PartnerSameEraAsInLaw;
            return shapes;
        }

        public IEnumerable<string> Shapes()
        {
            if (People.Any(p => p.BirthDate is null)) yield return "dateless person";
            if (People.All(p => p.BirthDate is null)) yield return "entirely dateless family";
            if (People.Any(p => p.FormerSpouseIds?.Count > 0)) yield return "former spouse";
            if (People.Any(p => p.SpouseIds?.Count > 1)) yield return "two spouses at once";
            if (People.Any(p => p.ParentIds?.Count == 0 && rels.Any(r => r.Type == RelationshipType.Sibling &&
                    (r.PersonAId == p.Id || r.PersonBId == p.Id)))) yield return "orphan sibling";
            if (Couples.Any(c => c.ChildIds.Count > 0 && !People.Any(p => p.Id == c.PersonAId &&
                    ((p.SpouseIds ?? []).Contains(c.PersonBId) || (p.FormerSpouseIds ?? []).Contains(c.PersonBId)))))
                yield return "unmarried co-parents";
            if (Components().Count > 1) yield return "multiple components";
            if (People.GroupBy(p => (p.LastName, p.FirstName)).Any(g => g.Count() > 1)) yield return "name tie";
            if (People.Any(p => p.ParentIds?.Count >= 3)) yield return "3+ recorded parents";

            var byId = People.ToDictionary(p => p.Id);
            HashSet<Guid> Roots(Guid id)
            {
                var parents = byId[id].ParentIds ?? [];
                return parents.Count == 0 ? [id] : [.. parents.SelectMany(Roots)];
            }
            int Depth(Guid id) => (byId[id].ParentIds ?? []).Select(Depth).DefaultIfEmpty(-1).Max() + 1;
            if (People.Any(p => Depth(p.Id) >= 2)) yield return "3+ generations";
            if (People.Any(p => (p.SpouseIds ?? []).Concat(p.FormerSpouseIds ?? []).Any(s =>
                    (p.ParentIds?.Count ?? 0) > 0 && (byId[s].ParentIds?.Count ?? 0) > 0 &&
                    !Roots(p.Id).Overlaps(Roots(s)))))
                yield return "cross-root marriage";
        }

        // Minimal, human-readable family dump for failure messages: enough to rebuild the
        // case by hand as a FamilyTreeLayoutEngineTests regression test.
        public string Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{People.Count} people, focus {Label(Focus)}, anchor {(Anchor is { } a ? Label(a) : "none")}");
            sb.AppendLine("people (in the order passed to ComputeLayout):");
            foreach (var p in People) sb.AppendLine($"  {Label(p.Id)}");
            sb.AppendLine("relationships:");
            foreach (var r in rels)
            {
                var verb = r.Type switch
                {
                    RelationshipType.Parent => "is parent of",
                    RelationshipType.Spouse => r.EndDate is null ? "is married to" : "was married to",
                    _ => "is sibling of",
                };
                sb.AppendLine($"  {Label(r.PersonAId)} {verb} {Label(r.PersonBId)}");
            }
            if (Spec.Malform != 0) sb.AppendLine($"  (plus DTO corruption from Malform seed {Spec.Malform})");
            if (Spec.Cycle != 0) sb.AppendLine($"  (plus injected parent cycle(s) from Cycle seed {Spec.Cycle})");
            return sb.ToString();
        }
    }
}

// Local-only: skipped unless FAMILYTREE_FUZZ_SOAK=1, so `dotnet test` shows it as an
// explicit skip rather than silently passing.
public sealed class SoakFactAttribute : FactAttribute
{
    public SoakFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FAMILYTREE_FUZZ_SOAK") != "1")
            Skip = "Local-only soak test. Set FAMILYTREE_FUZZ_SOAK=1 (optionally FAMILYTREE_FUZZ_ITER) to run.";
    }
}
