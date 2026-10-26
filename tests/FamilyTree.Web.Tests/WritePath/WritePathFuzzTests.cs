using CsCheck;
using FamilyTree.Web.Tests.ServiceTests;
using Xunit;
using Xunit.Sdk;

namespace FamilyTree.Web.Tests.WritePath;

// B7 (Outreach readiness, 2026-10-25): a write-path fuzz harness. The layout fuzz tests
// (FamilyTreeLayoutFuzzTests) start from a finished family; this one builds families the
// way a casual user does, one save at a time through the real PersonService and
// RelationshipService, and checks every save:
//   - nothing crashes (the services log a caught exception and say "An error occurred",
//     which the user can't tell from a refusal, so the log is watched);
//   - an impossible save is refused, with a message, and changes nothing;
//   - a sound save is accepted, and stores exactly what the form said: no link invented,
//     dropped or changed elsewhere, and no date the user didn't give;
//   - the whole database stays sound (CasualUserSession.CheckDatabase, Tree.Problems);
//   - both layout engines draw everyone, once, at a finite position, and the prototype
//     draws every child below each parent wherever the family's dates allow it
//     (CasualUserSession.PlausibleFamilies; the archived engine is frozen, ADR 004).
// "Impossible" is Tree.Problems: a blank name, a date out of range or before birth, a loop
// of ancestors, a PlausibilityRules error (a grandparent born after the grandchild among
// them), someone who is both a parent and a spouse or sibling, two current marriages at once.
// The harness signs in as a family Admin and drives what the screens call; it doesn't cover
// Members' permissions, photos, stories, or the sign-up linking flow (AuthService).
//
// Same driver as the layout fuzz tests: iteration i uses PCG stream i from a pinned seed,
// and a failure is shrunk to a shorter session and printed as a transcript.
//   FAMILYTREE_WRITE_FUZZ_ITER=<n>      sessions (default 150)
//   FAMILYTREE_WRITE_FUZZ_SEED=<ulong>  base seed
//   FAMILYTREE_FUZZ_SOAK=1              the local soak (random seed, 2000 sessions, layout after every step)
//   FAMILYTREE_WRITE_FUZZ_SURVEY=1 and FAMILYTREE_WRITE_FUZZ_SURVEY_OUT=<file>  every failure, grouped (Survey_...)
public class WritePathFuzzTests
{
    private const ulong PinnedSeed = 20261025;
    private static int Iterations => int.TryParse(Environment.GetEnvironmentVariable("FAMILYTREE_WRITE_FUZZ_ITER"), out var n) ? n : 150;
    private static ulong BaseSeed => ulong.TryParse(Environment.GetEnvironmentVariable("FAMILYTREE_WRITE_FUZZ_SEED"), out var s) ? s : PinnedSeed;

    internal static readonly Gen<Step> GenStep =
        Gen.Int[0, 99].Select(Gen.Int[0, 9999].Array[8], (kind, args) => new Step(kind, args));

    internal static readonly Gen<Step[]> GenSession = GenStep.Array[1, 40];

    [Fact]
    public void CasualUserSessions_NeverCrashInventOrAcceptTheImpossible() => Run(GenSession, Iterations, BaseSeed);

    // The generator must really reach the cases worth testing, or the test above passes
    // vacuously: each kind of impossible input refused, and each kind of edit saved.
    [Fact]
    public void Generator_ReachesEveryKindOfEditAndRefusal()
    {
        var seen = new Dictionary<string, int>();
        for (var i = 0; i < Iterations; i++)
        {
            var session = new CasualUserSession();
            Task.Run(() => session.RunAsync(GenSession.Generate(new PCG((uint)i, BaseSeed), null, out _))).GetAwaiter().GetResult();
            foreach (var o in session.Outcomes) seen[o] = seen.GetValueOrDefault(o) + 1;
        }

        string[] required =
        [
            "add ok", "update ok", "delete ok", "restore ok", "link-sibling ok",
            "add refused [name]", "add refused [date-range]", "add refused [loop]", "add refused [impossible-parent]",
            "update refused [date-range]", "update refused [loop]", "update refused [impossible-parent]",
            "update refused [two-marriages]", "link-sibling refused [duplicate]",
            "add refused [impossible-ancestor]", "update refused [impossible-ancestor]",
            "add refused [relative-link]", "update refused [relative-link]", "link-sibling refused [relative-link]",
            "layout checked",
        ];
        var missing = required.Where(r => seen.GetValueOrDefault(r) == 0).ToList();
        // The layout check skips families whose dates can't all hold; most must still be checked.
        var skipped = seen.GetValueOrDefault("layout partly skipped (implausible dates)");
        if (skipped > seen.GetValueOrDefault("layout checked"))
            missing.Add($"a checked layout in most sessions ({skipped} were partly skipped)");
        if (missing.Count > 0)
            throw new XunitException("generator never produced: " + string.Join(", ", missing) +
                                     "\nseen: " + string.Join(", ", seen.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} x{kv.Value}")));
    }

    // Local tool, not a check: plays every session instead of stopping at the first failure,
    // groups the failures by their first violation (names, dates and numbers blanked), and
    // writes each group's count and shortest transcript to FAMILYTREE_WRITE_FUZZ_SURVEY_OUT.
    // How B7's findings were collected; use it to see everything a change breaks at once.
    [SurveyFact]
    public void Survey_GroupsEveryFailingSession()
    {
        var groups = new Dictionary<string, (int Count, string Example)>();
        for (var i = 0; i < Iterations; i++)
        {
            var s = Play(GenSession.Generate(new PCG((uint)i, BaseSeed), null, out _), false);
            if (s.Violations.Count == 0) continue;
            var key = System.Text.RegularExpressions.Regex.Replace(s.Violations[0], @"[A-Z][a-z]+#\d+|\d[\d/:-]*|step \d+|y=\d+", "_");
            key = key.Length > 140 ? key[..140] : key;
            groups[key] = groups.TryGetValue(key, out var g) ? (g.Count + 1, g.Example.Length < s.Report().Length ? g.Example : s.Report()) : (1, s.Report());
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("FAMILYTREE_WRITE_FUZZ_SURVEY_OUT")!,
            string.Join("\n\n========\n", groups.OrderByDescending(g => g.Value.Count).Select(g => $"[{g.Value.Count}] {g.Key}\n{g.Value.Example}")));
    }

    [SoakFact]
    public void Soak_CasualUserSessions()
    {
        var seed = (ulong)Random.Shared.NextInt64();
        try
        {
            Run(GenSession, int.TryParse(Environment.GetEnvironmentVariable("FAMILYTREE_WRITE_FUZZ_ITER"), out var n) ? n : 2000, seed,
                layoutEveryStep: true);
        }
        catch (Exception ex)
        {
            throw new XunitException($"Soak failed with FAMILYTREE_WRITE_FUZZ_SEED={seed}\n{ex.Message}");
        }
    }

    private static CasualUserSession Play(Step[] steps, bool layoutEveryStep)
    {
        var session = new CasualUserSession();
        Task.Run(() => session.RunAsync(steps, layoutEveryStep)).GetAwaiter().GetResult();
        return session;
    }

    private static void Run(Gen<Step[]> gen, int iterations, ulong seed, bool layoutEveryStep = false)
    {
        void Check(Step[] steps)
        {
            var session = Play(steps, layoutEveryStep);
            if (session.Violations.Count > 0) throw new XunitException(session.Report());
        }

        for (var i = 0; i < iterations; i++)
        {
            var pcg = new PCG((uint)i, seed);
            var state = pcg.Seed;
            var steps = gen.Generate(pcg, null, out _);
            var first = Play(steps, layoutEveryStep);
            if (first.Violations.Count == 0) continue;

            var replay = pcg.ToString(state);
            string shrunk;
            try
            {
                gen.Sample(Check, seed: replay, iter: 500, threads: 1, print: s => Play(s, layoutEveryStep).Report());
                shrunk = "(shrink pass did not reproduce the failure)";
            }
            catch (Exception ex)
            {
                shrunk = ex.Message;
            }
            throw new XunitException(
                $"Session {i} failed (base seed {seed}, CsCheck replay seed \"{replay}\").\n\n" +
                $"Shrunk:\n{shrunk}\n\nOriginal:\n{first.Report()}");
        }
    }
}

public sealed class SurveyFactAttribute : FactAttribute
{
    public SurveyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FAMILYTREE_WRITE_FUZZ_SURVEY") != "1" ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FAMILYTREE_WRITE_FUZZ_SURVEY_OUT")))
            Skip = "Local-only survey. Set FAMILYTREE_WRITE_FUZZ_SURVEY=1 and FAMILYTREE_WRITE_FUZZ_SURVEY_OUT=<file> " +
                   "(optionally FAMILYTREE_WRITE_FUZZ_ITER and _SEED) to run.";
    }
}
