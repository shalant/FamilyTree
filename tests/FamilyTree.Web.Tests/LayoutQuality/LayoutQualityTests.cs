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
    public void LineThroughAName_Counts_AndOneBelowTheBirthYear_DoesNot()
    {
        // Joseph's circle spans 70..130; his name and year sit from 135 to 163.
        var joseph = Node(new PersonDto { Id = Guid.NewGuid(), FirstName = "Joseph", LastName = "Test", BirthDate = new DateOnly(1895, 1, 1) }, 150, 100);
        var throughName = new SiblingLink(0, 145, 300, 145);
        var belowYear = new SiblingLink(0, 170, 300, 170);

        Score(Layout([joseph], [], [throughName])).LinesThroughLabels.Should().Be(1);
        Score(Layout([joseph], [], [belowYear])).LinesThroughLabels.Should().Be(0);
    }

    [Fact]
    public void StemStartingAtTheParentsRim_RunsThroughTheirOwnName()
    {
        // David, found 2026-10-25: a single parent's stem started at the bottom of his circle.
        var david = Node(P("David"), 150, 100);

        Score(Layout([david], [Stem(150, 130, 300)])).LinesThroughLabels.Should().Be(1);
        Score(Layout([david], [Stem(150, 160, 300)])).LinesThroughLabels.Should().Be(0);
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

    // Ray's line from Louis + Lillian ran along Morry and Isador's line from their mother on the
    // live tree (2026-10-23), so the three looked like brothers.
    [Fact]
    [Trait("Category", "Regression")]
    public void TwoFamiliesLinesOnTopOfEachOther_CountAsMerged()
    {
        var report = Score(Layout([], [Span(530, 656, 300), Span(436, 770, 300)]));

        report.MergedLines.Should().Be(1);
    }

    [Theory]
    [InlineData(100, 200, 300, "bars that only meet at an end")]
    [InlineData(50, 150, 310, "bars 10 apart in height")]
    public void LinesThatDontRunTogether_AreNotMerged(double left, double right, double y, string why)
    {
        var report = Score(Layout([], [Span(0, 100, 300), Span(left, right, y)]));

        report.MergedLines.Should().Be(0, why);
    }

    [Fact]
    public void OneFamilysOwnLines_AreNotMerged()
    {
        var family = new FamilyUnit(null, new StemLine(100, 0, 200), new SiblingSpan(0, 200, 200), [new ChildDrop(100, 260)]);

        Score(Layout([], [family])).MergedLines.Should().Be(0);
    }

    [Fact]
    public void SiblingLinksSharingAPerson_MayMerge_ButUnrelatedOnesMayNot()
    {
        var shared = new List<SiblingLink> { new(756, 211, 1076, 211), new(756, 211, 1330, 211) }; // Louis to Tilly, Louis to David
        var unrelated = new List<SiblingLink> { new(0, 211, 400, 211), new(200, 211, 600, 211) };

        Score(Layout([], [], shared)).MergedLines.Should().Be(0, "Louis, Tilly and David are all siblings");
        Score(Layout([], [], unrelated)).MergedLines.Should().Be(1);
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

    // Every fixture under every engine, one table per fixture (B7 step 0, 2026-10-25): the
    // neutral test families exist to compare engines on families that weren't the ones each
    // engine was tuned on. Scores, doesn't judge; results are in docs/Testing/layout-quality-gallery.md.
    [Fact]
    public void EveryFixture_ScoredByEveryEngine()
    {
        foreach (var fixture in LayoutFixture.LoadAll())
        {
            output.WriteLine($"== {fixture.Name} ({fixture.People.Count} people)");
            output.WriteLine($"{"engine",-32}{"loose",6}{"gapMax",7}{"cross",6}{"thruNode",9}{"thruName",9}{"overlap",8}{"merged",7}{"offCtr",7}{"width",7}{"length",8}");
            foreach (var (name, make) in LayoutGalleryTests.Engines)
            {
                var (layout, people, couples) = fixture.Layout(make());
                layout.Nodes.Should().HaveCount(people.Count, $"{name} must place everyone in {fixture.Name}");
                var r = LayoutQualityMetrics.Compute(layout, people, couples);
                output.WriteLine($"{name,-32}{r.LooseCouples.Count(),6}{r.MaxPartnerGap,7:0}{r.Crossings,6}{r.LinesThroughNodes,9}{r.LinesThroughLabels,9}{r.NodeOverlaps,8}{r.MergedLines,7}{r.MaxFamilyOffCenter,7:0}{r.Width,7:0}{r.ConnectorLength,8:0}");
            }
        }
    }

    // The common-practice baseline (LayeredBaselineEngine) only means something if it lays
    // out the same families: everyone placed, nobody twice, partners SpouseSpacingX apart.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayeredBaseline_PlacesEveryoneWithPartnersSideBySide(bool birthYearY)
    {
        var engine = new LayeredBaselineEngine(birthYearY);
        var families = LayoutFixture.LoadAll().Select(f => (f.Name, Data: f.Layout(engine)))
            .Concat(Enumerable.Range(0, 50).Select(i =>
            {
                var g = FamilyTreeLayoutFuzzTests.Generated(i, FamilyTreeLayoutFuzzTests.DefaultSeed);
                return ((string?)$"generated #{i}", Data: (engine.ComputeLayout(g.People, g.Couples, g.Focus, g.Anchor), g.People, g.Couples));
            }));

        foreach (var (name, (layout, people, couples)) in families)
        {
            layout.Nodes.Select(n => n.Person.Id).Should().BeEquivalentTo(people.Select(p => p.Id), $"{name} must place everyone once");
            var at = layout.Nodes.ToDictionary(n => n.Person.Id);
            foreach (var c in couples.Where(c => c.PersonAId != c.PersonBId))
                Math.Abs(at[c.PersonAId].X - at[c.PersonBId].X).Should().BeGreaterThanOrEqualTo(LayoutQualityMetrics.SpouseSpacingX, $"{name}: partners are never stacked");
        }
    }
}

// Local-only: writes artifacts/layout-gallery/index.html (ADR 004, step 1; how to run it is
// in docs/Testing/layout-quality-gallery.md). Every fixture, then the worst generated
// families by each score under the archived engine, each drawn by both engines one above
// the other, plus aggregate scores for both engines over all the generated families.
public class LayoutGalleryTests(ITestOutputHelper output)
{
    private const int WorstPerScore = 4;

    internal static readonly (string Name, Func<IFamilyTreeLayoutEngine> Make)[] Engines =
        [("archived", () => new FamilyTreeLayoutEngine()), ("timeline prototype", () => new TimelineLayoutEngine()),
         ("layered baseline, generations", () => new LayeredBaselineEngine(birthYearY: false)),
         ("layered baseline, birth years", () => new LayeredBaselineEngine(birthYearY: true))];

    [GalleryFact]
    public void WriteGallery()
    {
        var entries = new List<LayoutGallery.Entry>();
        foreach (var fixture in LayoutFixture.LoadAll())
            foreach (var (name, make) in Engines)
            {
                var (layout, people, couples) = fixture.Layout(make());
                entries.Add(new($"{fixture.Name} — {name}", "fixture", layout, LayoutQualityMetrics.Compute(layout, people, couples)));
            }

        var cases = int.TryParse(Environment.GetEnvironmentVariable("FAMILYTREE_LAYOUT_GALLERY_CASES"), out var n) ? n : 500;
        var seed = FamilyTreeLayoutFuzzTests.DefaultSeed;
        var generated = Enumerable.Range(0, cases).Select(i =>
        {
            var f = FamilyTreeLayoutFuzzTests.Generated(i, seed);
            var focus = f.Focus;
            var reports = Engines.Select(e =>
            {
                var layout = e.Make().ComputeLayout(f.People, f.Couples, focus, f.Anchor);
                return (Layout: layout, Report: LayoutQualityMetrics.Compute(layout, f.People, f.Couples));
            }).ToArray();
            return (Index: i, Results: reports);
        }).ToList();

        var picked = new HashSet<int>();
        void Worst(string score, Func<LayoutQualityReport, double> by)
        {
            foreach (var g in generated.Where(g => by(g.Results[0].Report) > 0)
                         .OrderByDescending(g => by(g.Results[0].Report)).ThenBy(g => g.Index)
                         .Where(g => !picked.Contains(g.Index)).Take(WorstPerScore))
            {
                picked.Add(g.Index);
                for (var e = 0; e < Engines.Length; e++)
                    entries.Add(new($"generated #{g.Index} (seed {seed}) — {Engines[e].Name}",
                        $"among the worst by {score} under the archived engine", g.Results[e].Layout, g.Results[e].Report));
            }
        }
        Worst("partner gap", r => r.MaxPartnerGap > LayoutQualityMetrics.SpouseSpacingX ? r.MaxPartnerGap : 0);
        Worst("crossings", r => r.Crossings);
        Worst("lines through nodes", r => r.LinesThroughNodes);
        Worst("node overlaps", r => r.NodeOverlaps);
        Worst("lines on top of another family's", r => r.MergedLines);
        Worst("family off-centre", r => r.MaxFamilyOffCenter);

        var path = Environment.GetEnvironmentVariable("FAMILYTREE_LAYOUT_GALLERY_OUT")
            ?? Path.Combine(RepoRoot(), "artifacts", "layout-gallery", "index.html");
        LayoutGallery.Write(path, entries);

        output.WriteLine($"Wrote {path}");
        for (var e = 0; e < Engines.Length; e++)
        {
            var reports = generated.Select(g => g.Results[e].Report).ToList();
            output.WriteLine($"{Engines[e].Name}, over {cases} generated families (seed {seed}):");
            output.WriteLine($"  with a loose couple (> {LayoutQualityMetrics.SpouseSpacingX}): {reports.Count(r => r.LooseCouples.Any())}");
            output.WriteLine($"  max partner gap: mean {reports.Average(r => r.MaxPartnerGap):0}, worst {reports.Max(r => r.MaxPartnerGap):0}");
            output.WriteLine($"  crossings: total {reports.Sum(r => r.Crossings)}, families with any {reports.Count(r => r.Crossings > 0)}");
            output.WriteLine($"  lines through nodes: total {reports.Sum(r => r.LinesThroughNodes)}, families with any {reports.Count(r => r.LinesThroughNodes > 0)}");
            output.WriteLine($"  node overlaps: total {reports.Sum(r => r.NodeOverlaps)}, families with any {reports.Count(r => r.NodeOverlaps > 0)}");
            output.WriteLine($"  lines on top of another family's: total {reports.Sum(r => r.MergedLines)}, families with any {reports.Count(r => r.MergedLines > 0)}");
            output.WriteLine($"  family off-centre: mean of max {reports.Average(r => r.MaxFamilyOffCenter):0}");
            output.WriteLine($"  width: mean {reports.Average(r => r.Width):0}; connector length: mean {reports.Average(r => r.ConnectorLength):0}");
        }
    }

    internal static string RepoRoot()
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
