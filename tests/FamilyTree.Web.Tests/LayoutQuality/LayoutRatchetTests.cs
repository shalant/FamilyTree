using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyTree.Web.Services;
using FamilyTree.Web.Tests.ServiceTests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FamilyTree.Web.Tests.LayoutQuality;

// The quality ratchet (ADR 004, added 2026-10-23). TimelineLayoutEngine's scores may only get
// better: every score below is "lower is better", and the test fails if any is worse than its
// limit in LayoutQuality/ratchet.json. That stops a fix for one family quietly costing
// another, the pattern that got the archived engine frozen.
//
// When a change improves a score, the test passes and prints the better value. Run with
// FAMILYTREE_RATCHET_UPDATE=1 to write it back; that only ever tightens limits. Loosening one
// is a deliberate trade-off: edit ratchet.json by hand and say why in the commit message.
//
// Scored: 500 generated families (the fuzz harness's families and seed), plus every fixture.
// Private fixtures (live exports) are checked only where they exist, so locally; their limits
// are plain numbers and hold no names.
public class LayoutRatchetTests(ITestOutputHelper output)
{
    private const int Cases = 500;

    private static string RatchetPath => Path.Combine(LayoutGalleryTests.RepoRoot(), "tests", "FamilyTree.Web.Tests", "LayoutQuality", "ratchet.json");

    // Compared after rounding to a whole layout unit, so an average can't fail on a fraction.
    private static Dictionary<string, double> GeneratedScores(IReadOnlyList<LayoutQualityReport> r) => new()
    {
        ["familiesWithLooseCouple"] = r.Count(x => x.LooseCouples.Any()),
        ["partnerGapWorst"] = r.Max(x => x.MaxPartnerGap),
        ["partnerGapMean"] = r.Average(x => x.MaxPartnerGap),
        ["crossings"] = r.Sum(x => x.Crossings),
        ["linesThroughNodes"] = r.Sum(x => x.LinesThroughNodes),
        ["nodeOverlaps"] = r.Sum(x => x.NodeOverlaps),
        ["mergedLines"] = r.Sum(x => x.MergedLines),
        ["familyOffCentreMean"] = r.Average(x => x.MaxFamilyOffCenter),
        ["widthMean"] = r.Average(x => x.Width),
        ["connectorLengthMean"] = r.Average(x => x.ConnectorLength),
    };

    private static Dictionary<string, double> FixtureScores(LayoutQualityReport r) => new()
    {
        ["partnerGapMax"] = r.MaxPartnerGap,
        ["crossings"] = r.Crossings,
        ["linesThroughNodes"] = r.LinesThroughNodes,
        ["nodeOverlaps"] = r.NodeOverlaps,
        ["mergedLines"] = r.MergedLines,
        ["familyOffCentreMax"] = r.MaxFamilyOffCenter,
        ["width"] = r.Width,
        ["connectorLength"] = r.ConnectorLength,
    };

    [Fact]
    public void TimelineEngine_ScoresNoWorseThanTheRatchet()
    {
        var seed = FamilyTreeLayoutFuzzTests.DefaultSeed;
        var reports = Enumerable.Range(0, Cases).Select(i =>
        {
            var f = FamilyTreeLayoutFuzzTests.Generated(i, seed);
            var layout = new TimelineLayoutEngine().ComputeLayout(f.People, f.Couples, f.Focus, f.Anchor);
            return LayoutQualityMetrics.Compute(layout, f.People, f.Couples);
        }).ToList();

        var current = new Dictionary<string, Dictionary<string, double>>
        {
            [$"generated (seed {seed}, {Cases} families)"] = GeneratedScores(reports),
        };
        foreach (var fixture in LayoutFixture.LoadAll())
        {
            var (layout, people, couples) = fixture.Layout(new TimelineLayoutEngine());
            current[$"fixture {fixture.Name}"] = FixtureScores(LayoutQualityMetrics.Compute(layout, people, couples));
        }

        var limits = File.Exists(RatchetPath)
            ? JsonNode.Parse(File.ReadAllText(RatchetPath))!.AsObject()
            : new JsonObject();

        var worse = new List<string>();
        var better = new List<string>();
        foreach (var (set, scores) in current)
        {
            if (limits[set] is not JsonObject setLimits)
            {
                output.WriteLine($"{set}: no limits yet{(Update ? ", recorded" : "; run with FAMILYTREE_RATCHET_UPDATE=1 to record them")}");
                if (Update) limits[set] = new JsonObject(scores.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)Round(kv.Value))));
                continue;
            }
            foreach (var (score, value) in scores)
            {
                if (setLimits[score] is not JsonValue v)
                {
                    output.WriteLine($"{set} / {score}: no limit yet ({Round(value)})");
                    if (Update) setLimits[score] = Round(value);
                    continue;
                }
                var limit = v.GetValue<double>();
                if (Round(value) > limit)
                    worse.Add($"{set} / {score}: {Round(value)}, limit {limit}");
                else if (Round(value) < limit)
                {
                    better.Add($"{set} / {score}: {Round(value)}, limit {limit}");
                    if (Update) setLimits[score] = Round(value);
                }
            }
        }

        foreach (var w in worse) output.WriteLine($"WORSE: {w}");
        foreach (var b in better) output.WriteLine($"better: {b}");
        if (better.Count > 0 && !Update) output.WriteLine("Run with FAMILYTREE_RATCHET_UPDATE=1 to tighten these limits.");
        if (Update)
        {
            File.WriteAllText(RatchetPath, limits.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            output.WriteLine($"Wrote {RatchetPath}");
        }

        limits.ContainsKey(current.Keys.First()).Should().BeTrue("the ratchet must have limits to check, or it passes vacuously");
        worse.Should().BeEmpty("TimelineLayoutEngine's quality scores may only improve (LayoutQuality/ratchet.json). " +
                               "If this trade-off is deliberate, loosen the limit by hand and say why in the commit message");
    }

    private static bool Update => Environment.GetEnvironmentVariable("FAMILYTREE_RATCHET_UPDATE") == "1";

    private static double Round(double v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
}
