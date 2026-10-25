using System.Reflection;
using System.Runtime.ExceptionServices;
using FamilyTree.Web.Services;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

// ADR 004 step 4: every layout test in FamilyTreeLayoutFuzzTests and
// FamilyTreeLayoutTransitionTests, rerun unchanged against TimelineLayoutEngine (via
// LayoutEngineOverride). Skipped full-domain invariants run here too: they're skipped for
// known-open bugs in the archived engine, which the prototype may not share.
//
// Tests the prototype fails go in KnownOpen with the reason, and are checked to still
// fail, so fixing one turns its KnownOpen_StillFails case red: delete the entry then.
// A test that only fails because it pins archived-engine behaviour rather than a property
// any good layout has is listed in NotApplicable instead, with why.
public class TimelineEngineSuiteTests
{
    private static readonly Type[] Suites = [typeof(FamilyTreeLayoutFuzzTests), typeof(FamilyTreeLayoutTransitionTests)];

    // Harness self-checks that don't lay anything out.
    private static readonly HashSet<string> NoLayout =
    [
        nameof(FamilyTreeLayoutFuzzTests.GeneratorCyclic_ActuallyProducesParentCycles),
        nameof(FamilyTreeLayoutFuzzTests.Soak_AllActiveInvariants),
        nameof(FamilyTreeLayoutFuzzTests.Generator_CoversTheInterestingShapes),
        nameof(FamilyTreeLayoutFuzzTests.ExcludingKnownOpenDomains_StillProduceRealTrees),
    ];

    private static readonly Dictionary<string, string> KnownOpen = new()
    {
        // Same full-domain invariant the archived engine skips (R2, R6, R7); its
        // _ExcludingKnownOpen twin passes here. What's left can't be fixed by placement: a
        // person with three or more partners can't sit within SpouseSpacingX of all of them,
        // and a parent and child who are co-parents (generator data) can't share a row while
        // the child stays below the parent, which Invariant 3 requires.
        ["Fuzz.Invariant4_CouplesSitTogether"] = "3+ partners; parent and child as co-parents",
        // Ordering only minimises crossings. When Pat's children by nobody else need room on
        // Pat's side, Pat's childless brother Sam isn't moved to Alex's side, so Kat lands
        // about 120 past Sam (at a different height, nothing overlaps).
        ["Transition.ChildrenWithNoOtherRecordedParent_SitUnderThatParentBesideTheirMarriage(False)"] = "ordering ignores free space",
        ["Transition.ChildrenWithNoOtherRecordedParent_SitUnderThatParentBesideTheirMarriage(True)"] = "ordering ignores free space",
    };

    private static readonly Dictionary<string, string> NotApplicable = new()
    {
        // Asserts each family keeps its internal left-to-right order when Ray and Bill are
        // linked as brothers. The prototype turns Bill + Gish so the brothers sit side by side
        // (Gish, Bill | Ray, Rose), which is what the link is for; the archived engine leaves
        // Gish between them.
        ["Transition.SiblingLinkBetweenTwoMarriedParents_BothFamiliesKeepTheirShape(False)"] = "turns a couple to seat linked brothers together",
        ["Transition.SiblingLinkBetweenTwoMarriedParents_BothFamiliesKeepTheirShape(True)"] = "turns a couple to seat linked brothers together",
    };

    public static IEnumerable<object[]> Passing() => Cases().Where(c => !KnownOpen.ContainsKey(c) && !NotApplicable.ContainsKey(c)).Select(c => new object[] { c });
    public static IEnumerable<object[]> Open() => Cases().Where(KnownOpen.ContainsKey).Select(c => new object[] { c });

    [Theory, MemberData(nameof(Passing))]
    public void PassesUnderTimelineEngine(string test) => Invoke(test);

    [Theory, MemberData(nameof(Open))]
    public void KnownOpen_StillFails(string test) =>
        FluentActions.Invoking(() => Invoke(test)).Should().Throw<Exception>(
            $"{test} is listed as known-open for the timeline engine ({KnownOpen[test]}); if it passes now, delete the entry");

    [Fact]
    public void EveryListedCaseExists()
    {
        var all = Cases().ToHashSet();
        KnownOpen.Keys.Concat(NotApplicable.Keys).Where(k => !all.Contains(k)).Should().BeEmpty();
    }

    // "Suite.Method" for facts, "Suite.Method(arg)" for each InlineData row.
    private static IEnumerable<string> Cases()
    {
        foreach (var suite in Suites)
            foreach (var m in suite.GetMethods(BindingFlags.Public | BindingFlags.Instance).OrderBy(m => m.MetadataToken))
            {
                if (NoLayout.Contains(m.Name)) continue;
                if (m.GetCustomAttribute<TheoryAttribute>() is not null)
                    foreach (var row in m.GetCustomAttributes<InlineDataAttribute>())
                        foreach (var args in row.GetData(m))
                            yield return $"{Short(suite)}.{m.Name}({string.Join(",", args)})";
                else if (m.GetCustomAttribute<FactAttribute>() is not null)
                    yield return $"{Short(suite)}.{m.Name}";
            }
    }

    private static string Short(Type suite) => suite.Name.Replace("FamilyTreeLayout", "").Replace("Tests", "");

    private static void Invoke(string test)
    {
        var dot = test.IndexOf('.');
        var suite = Suites.Single(s => Short(s) == test[..dot]);
        var rest = test[(dot + 1)..];
        var paren = rest.IndexOf('(');
        var name = paren < 0 ? rest : rest[..paren];
        var method = suite.GetMethod(name)!;
        var args = paren < 0 ? [] : method.GetParameters()
            .Zip(rest[(paren + 1)..^1].Split(','), (p, s) => Convert.ChangeType(s, p.ParameterType))
            .ToArray();

        using var _ = LayoutEngineOverride.Use(() => new TimelineLayoutEngine());
        try
        {
            method.Invoke(Activator.CreateInstance(suite), args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }
}
