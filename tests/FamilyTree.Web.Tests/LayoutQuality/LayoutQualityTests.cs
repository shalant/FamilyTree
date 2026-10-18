using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Services;
using FamilyTree.Web.Tests.ServiceTests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static FamilyTree.Web.Services.FamilyTreeLayoutEngine;

namespace FamilyTree.Web.Tests.LayoutQuality;

// The scoring itself (ADR 004, step 1). Hand-built layouts with a known answer, so a
// metric that silently miscounts can't pass as a baseline.
public class LayoutQualityMetricsTests
{
    private static PersonDto P(string name) => new() { Id = Guid.NewGuid(), FirstName = name, LastName = "Test" };
    private static LayoutNode Node(PersonDto p, int x, int y) => new(p, x, y, 0, false, 60);
    private static FamilyUnit Stem(double x, double top, double bot) => new(null, new StemLine(x, top, bot), null, []);
    private static FamilyUnit Span(double left, double right, double y) =>
        new(null, new StemLine(left, y, y), new SiblingSpan(left, right, y), []);

    private static FamilyTreeLayout Layout(List<LayoutNode> nodes, List<FamilyUnit> connectors, List<SiblingLink>? links = null) =>
        new(nodes, [], connectors, links ?? [], [], 1000, 1000, 0);

    private static LayoutQualityReport Score(FamilyTreeLayout layout, List<PersonDto>? people = null, List<CoupleDto>? couples = null) =>
        LayoutQualityMetrics.Compute(layout, people ?? layout.Nodes.Select(n => n.Person).ToList(), couples ?? []);

    [Fact]
    public void LinesFromTwoFamiliesThatCross_CountOnce()
    {
        var report = Score(Layout([], [Stem(100, 0, 200), Span(0, 200, 100)]));

        report.Crossings.Should().Be(1);
    }

    [Fact]
    public void LinesThatOnlyMeetAtAnEnd_DontCount()
    {
        var report = Score(Layout([], [Stem(100, 0, 200), Span(100, 300, 100)]));

        report.Crossings.Should().Be(0);
    }

    [Fact]
    public void LinesWithinOneFamily_DontCount()
    {
        var family = new FamilyUnit(null, new StemLine(100, 0, 200), new SiblingSpan(0, 200, 100), []);

        Score(Layout([], [family])).Crossings.Should().Be(0);
    }

    [Fact]
    public void LineRunningThroughAnUnrelatedNode_Counts_AndOneEndingAtItsRim_DoesNot()
    {
        var bystander = Node(P("Bystander"), 150, 100);
        var through = new SiblingLink(0, 100, 300, 100);
        var endsAtRim = new SiblingLink(150, 300, 150, 131);

        Score(Layout([bystander], [], [through])).LinesThroughNodes.Should().Be(1);
        Score(Layout([bystander], [], [endsAtRim])).LinesThroughNodes.Should().Be(0);
    }

    [Fact]
    public void PartnerGap_IsTheHorizontalDistance_AndWideCouplesAreLoose()
    {
        var (a, b, c, d) = (P("Ellen"), P("Marc"), P("Rose"), P("Ray"));
        var layout = Layout([Node(a, 0, 100), Node(b, 830, 140), Node(c, 0, 400), Node(d, 200, 400)], []);
        var couples = new List<CoupleDto>
        {
            new() { PersonAId = a.Id, PersonBId = b.Id },
            new() { PersonAId = c.Id, PersonBId = d.Id },
        };

        var report = Score(layout, couples: couples);

        report.MaxPartnerGap.Should().Be(830);
        report.LooseCouples.Select(g => (g.A, g.B)).Should().Equal(("Ellen", "Marc"));
    }

    [Fact]
    public void FamilyOffCentre_ComparesTheChildrensCentreWithTheParentsCentre()
    {
        var (mom, dad, kid) = (P("Mom"), P("Dad"), P("Kid"));
        kid.ParentIds = [mom.Id, dad.Id];
        var layout = Layout([Node(mom, 0, 0), Node(dad, 200, 0), Node(kid, 400, 200)], []);

        var report = Score(layout);

        report.MaxFamilyOffCenter.Should().Be(300);
    }

    [Fact]
    public void OverlappingNodes_Count()
    {
        var layout = Layout([Node(P("Louis"), 459, 197), Node(P("Tf"), 455, 197), Node(P("Far"), 900, 197)], []);

        Score(layout).NodeOverlaps.Should().Be(1);
    }
}

// Checked-in and private fixtures (ADR 004, step 1). These tests score, they don't judge:
// the numbers go to the test output as a baseline. They fail only if a fixture can't be
// laid out at all.
public class LayoutFixtureTests(ITestOutputHelper output)
{
    [Fact]
    public void TwoBridges_LoadsThroughTheProductionMappingAndPlacesEveryone()
    {
        var fixture = LayoutFixture.LoadNamed("two-bridges");
        var (layout, people, couples) = fixture.Layout();

        layout.Nodes.Select(n => n.Person.Id).Should().BeEquivalentTo(fixture.People.Select(p => p.Id));
        var marc = people.Single(p => p.FirstName == "Marc");
        marc.SpouseIds.Should().ContainSingle(id => people.Single(p => p.Id == id).FirstName == "Ellen",
            "the fixture's relationships must map like PersonMapper maps the database's");

        output.WriteLine(LayoutQualityMetrics.Compute(layout, people, couples).Summary());
    }

    [Fact]
    public void EveryFixture_LaysOut()
    {
        foreach (var fixture in LayoutFixture.LoadAll())
        {
            var (layout, people, couples) = fixture.Layout();
            layout.Nodes.Should().HaveCount(people.Count, $"{fixture.Name} must place everyone");
            output.WriteLine($"== {fixture.Name}");
            output.WriteLine(LayoutQualityMetrics.Compute(layout, people, couples).Summary());
        }
    }
}

// Local-only: writes artifacts/layout-gallery/index.html (ADR 004, step 1; how to run it is
// in docs/Testing/layout-quality-gallery.md). Every fixture, then the worst generated
// families by each score, plus aggregate scores over all the generated ones as a baseline.
public class LayoutGalleryTests(ITestOutputHelper output)
{
    private const int WorstPerScore = 4;

    [GalleryFact]
    public void WriteGallery()
    {
        var entries = new List<LayoutGallery.Entry>();
        foreach (var fixture in LayoutFixture.LoadAll())
        {
            var (layout, people, couples) = fixture.Layout();
            entries.Add(new(fixture.Name!, "fixture", layout, LayoutQualityMetrics.Compute(layout, people, couples)));
        }

        var cases = int.TryParse(Environment.GetEnvironmentVariable("FAMILYTREE_LAYOUT_GALLERY_CASES"), out var n) ? n : 500;
        var seed = FamilyTreeLayoutFuzzTests.DefaultSeed;
        var generated = Enumerable.Range(0, cases).Select(i =>
        {
            var f = FamilyTreeLayoutFuzzTests.Generated(i, seed);
            var layout = f.Layout();
            return (Index: i, Layout: layout, Report: LayoutQualityMetrics.Compute(layout, f.People, f.Couples));
        }).ToList();

        var picked = new HashSet<int>();
        void Worst(string score, Func<LayoutQualityReport, double> by)
        {
            foreach (var g in generated.Where(g => by(g.Report) > 0).OrderByDescending(g => by(g.Report)).ThenBy(g => g.Index)
                         .Where(g => !picked.Contains(g.Index)).Take(WorstPerScore))
            {
                picked.Add(g.Index);
                entries.Add(new($"generated #{g.Index} (seed {seed})", $"among the worst by {score}", g.Layout, g.Report));
            }
        }
        Worst("partner gap", r => r.MaxPartnerGap > LayoutQualityMetrics.SpouseSpacingX ? r.MaxPartnerGap : 0);
        Worst("crossings", r => r.Crossings);
        Worst("lines through nodes", r => r.LinesThroughNodes);
        Worst("node overlaps", r => r.NodeOverlaps);
        Worst("family off-centre", r => r.MaxFamilyOffCenter);

        var path = Environment.GetEnvironmentVariable("FAMILYTREE_LAYOUT_GALLERY_OUT")
            ?? Path.Combine(RepoRoot(), "artifacts", "layout-gallery", "index.html");
        LayoutGallery.Write(path, entries);

        var reports = generated.Select(g => g.Report).ToList();
        output.WriteLine($"Wrote {path}");
        output.WriteLine($"Baseline over {cases} generated families (seed {seed}):");
        output.WriteLine($"  with a loose couple (> {LayoutQualityMetrics.SpouseSpacingX}): {reports.Count(r => r.LooseCouples.Any())}");
        output.WriteLine($"  max partner gap: mean {reports.Average(r => r.MaxPartnerGap):0}, worst {reports.Max(r => r.MaxPartnerGap):0}");
        output.WriteLine($"  crossings: total {reports.Sum(r => r.Crossings)}, families with any {reports.Count(r => r.Crossings > 0)}");
        output.WriteLine($"  lines through nodes: total {reports.Sum(r => r.LinesThroughNodes)}, families with any {reports.Count(r => r.LinesThroughNodes > 0)}");
        output.WriteLine($"  node overlaps: total {reports.Sum(r => r.NodeOverlaps)}, families with any {reports.Count(r => r.NodeOverlaps > 0)}");
        output.WriteLine($"  family off-centre: mean of max {reports.Average(r => r.MaxFamilyOffCenter):0}");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "FamilyTree.sln"))) return dir.FullName;
        throw new InvalidOperationException("FamilyTree.sln not found above " + AppContext.BaseDirectory);
    }
}

// Skipped unless FAMILYTREE_LAYOUT_GALLERY=1, so `dotnet test` shows an explicit skip.
public sealed class GalleryFactAttribute : FactAttribute
{
    public GalleryFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FAMILYTREE_LAYOUT_GALLERY") != "1")
            Skip = "Local-only. Set FAMILYTREE_LAYOUT_GALLERY=1 to write artifacts/layout-gallery/index.html.";
    }
}
