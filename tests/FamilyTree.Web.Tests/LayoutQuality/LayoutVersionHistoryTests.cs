using System.Globalization;
using FamilyTree.Web.Services;
using FamilyTree.Web.Tests.ServiceTests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FamilyTree.Web.Tests.LayoutQuality;

// Prints the score-history row for the best and newest Timeline versions (added 2026-10-25),
// in the column order of the table in docs/Testing/layout-versions.md. Cutting a version means
// pasting the newest row there. Scores, doesn't judge: the ratchet is what fails.
public class LayoutVersionHistoryTests(ITestOutputHelper output)
{
    private const int Cases = 500;

    [Fact]
    public void PrintHistoryRows()
    {
        output.WriteLine(Row($"best so far ({BestTimelineLayoutEngine.Version})", () => new BestTimelineLayoutEngine()));
        output.WriteLine(Row($"newest (since {TimelineLayoutEngine.Version})", () => new TimelineLayoutEngine()));
    }

    private static string Row(string label, Func<IFamilyTreeLayoutEngine> make)
    {
        var seed = FamilyTreeLayoutFuzzTests.DefaultSeed;
        var generated = LayoutRatchetTests.GeneratedScores(Enumerable.Range(0, Cases).Select(i =>
        {
            var f = FamilyTreeLayoutFuzzTests.Generated(i, seed);
            return LayoutQualityMetrics.Compute(make().ComputeLayout(f.People, f.Couples, f.Focus, f.Anchor), f.People, f.Couples);
        }).ToList());

        // Checked-in fixtures only, so the row is the same on every machine.
        var fixtures = Directory.GetFiles(LayoutFixture.FixturesDirectory, "*.json").Order().Select(LayoutFixture.Load).Select(fx =>
        {
            var (layout, people, couples) = fx.Layout(make());
            layout.Nodes.Should().HaveCount(people.Count, $"{label} must place everyone in {fx.Name}");
            return LayoutQualityMetrics.Compute(layout, people, couples);
        }).ToList();

        string N(double v) => Math.Round(v, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        return string.Join(" | ",
            $"| {label}", "date", "change",
            N(generated["familiesWithLooseCouple"]), N(generated["crossings"]), N(generated["linesThroughNodes"]),
            N(generated["linesThroughNames"]), N(generated["mergedLines"]), N(generated["familyOffCentreMean"]), N(generated["widthMean"]),
            N(fixtures.Sum(r => r.Crossings)), N(fixtures.Sum(r => r.LinesThroughNodes + r.LinesThroughLabels)),
            N(fixtures.Sum(r => r.NodeOverlaps)), N(fixtures.Sum(r => r.LooseCouples.Count())), N(fixtures.Sum(r => r.MaxFamilyOffCenter))) + " |";
    }
}
