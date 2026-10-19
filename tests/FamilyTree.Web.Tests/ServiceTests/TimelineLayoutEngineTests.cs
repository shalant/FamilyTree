using FamilyTree.Shared.Enums;
using FamilyTree.Web.Services;
using FamilyTree.Web.Tests.LayoutQuality;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

// The ADR 004 prototype engine (option 1: exact birth-year timeline, generation layers for
// ordering only, positions solved together). These pin what the design promises; how good
// the result looks is scored in the gallery (docs/Testing/layout-quality-gallery.md).
public class TimelineLayoutEngineTests
{
    private const int SpouseSpacingX = 200;
    private const int NodeSpacingX = 120;
    private const int PaddingY = 10;
    private const double PxPerYear = 6.5;

    // A family as Person + Relationship rows, laid out through PersonMapper/CoupleHelper.
    private sealed class Family
    {
        private readonly List<LayoutFixture.FixturePerson> _people = [];
        private readonly List<LayoutFixture.FixtureRelationship> _rels = [];

        public Guid Add(string first, int? year = null)
        {
            var id = new Guid(_people.Count + 1, 0, 0, new byte[8]);
            _people.Add(new(id, first, "Test", year is { } y ? new DateOnly(y, 6, 1) : null));
            return id;
        }

        public Family Parent(Guid parent, params Guid[] children)
        {
            foreach (var c in children) _rels.Add(new(parent, c, RelationshipType.Parent, null));
            return this;
        }

        public Family Marry(Guid a, Guid b, DateOnly? ended = null)
        {
            if (b.CompareTo(a) < 0) (a, b) = (b, a);
            _rels.Add(new(a, b, RelationshipType.Spouse, ended));
            return this;
        }

        public Family Sibling(Guid a, Guid b)
        {
            if (b.CompareTo(a) < 0) (a, b) = (b, a);
            _rels.Add(new(a, b, RelationshipType.Sibling, null));
            return this;
        }

        public LayoutFixture Fixture(Guid? focus = null, Guid? anchor = null) =>
            new("test", focus ?? _people[0].Id, anchor, [.. _people], [.. _rels]);
    }

    private static Dictionary<Guid, FamilyTreeLayoutEngine.LayoutNode> Lay(LayoutFixture f) =>
        f.Layout(new TimelineLayoutEngine()).Layout.Nodes.ToDictionary(n => n.Person.Id);

    [Fact]
    public void DatedPeople_SitExactlyAtTheirBirthYear()
    {
        var t = new Family();
        var mom = t.Add("Mom", 1903);
        var dad = t.Add("Dad", 1898);
        var kid = t.Add("Kid", 1931);
        t.Marry(mom, dad).Parent(mom, kid).Parent(dad, kid);

        var nodes = Lay(t.Fixture());

        const int minYear = 1890; // the earliest year, snapped down to its decade
        foreach (var (id, year) in new[] { (mom, 1903), (dad, 1898), (kid, 1931) })
            nodes[id].Y.Should().Be(PaddingY + (int)((year - minYear) * PxPerYear));
    }

    [Fact]
    public void EveryCouple_IsDrawnAtPartnerSpacing_EvenAcrossFamilies()
    {
        var (layout, people, couples) = LayoutFixture.LoadNamed("two-bridges").Layout(new TimelineLayoutEngine());
        var x = layout.Nodes.ToDictionary(n => n.Person.Id, n => n.X);
        string Name(Guid id) => people.Single(p => p.Id == id).FirstName;

        foreach (var c in couples)
            Math.Abs(x[c.PersonAId] - x[c.PersonBId]).Should().Be(SpouseSpacingX,
                $"{Name(c.PersonAId)} + {Name(c.PersonBId)} are partners (Ellen + Marc come from two different families)");
    }

    [Fact]
    public void ParentlessInLaws_FloatDownToJustAboveTheirChild()
    {
        var (layout, people, _) = LayoutFixture.LoadNamed("two-bridges").Layout(new TimelineLayoutEngine());
        int Depth(string name) => layout.Nodes.Single(n => n.Person.FirstName == name).Depth;

        Depth("Marc").Should().Be(Depth("Ellen"), "partners share a generation");
        Depth("Bud").Should().Be(Depth("Marc") - 1, "Bud has no recorded parents, so nothing pins him to the top row");
        Depth("Gimpel").Should().Be(0);
    }

    [Fact]
    public void ChildrenAreCentredUnderTheirParents()
    {
        var t = new Family();
        var mom = t.Add("Mom", 1900);
        var dad = t.Add("Dad", 1900);
        var kids = new[] { t.Add("A", 1925), t.Add("B", 1927), t.Add("C", 1929) };
        t.Marry(mom, dad).Parent(mom, kids).Parent(dad, kids);

        var nodes = Lay(t.Fixture());

        kids.Average(k => nodes[k].X).Should().BeApproximately((nodes[mom].X + nodes[dad].X) / 2.0, 1);
        var xs = kids.Select(k => nodes[k].X).Order().ToList();
        xs.Zip(xs.Skip(1)).Should().OnlyContain(p => p.Second - p.First >= NodeSpacingX - 1);
    }

    [Fact]
    public void DatelessPerson_TakesTheirYearFromRelatives_AndKeepsTheParentGap()
    {
        var t = new Family();
        var grandma = t.Add("Grandma", 1900);
        var mom = t.Add("Mom");
        var kid = t.Add("Kid", 1950);
        var lateKid = t.Add("Late");
        var parentOfLate = t.Add("ParentOfLate", 1990);
        t.Parent(grandma, mom).Parent(mom, kid).Parent(parentOfLate, lateKid);

        var nodes = Lay(t.Fixture());
        int Year(Guid id) => (int)Math.Round((nodes[id].Y - PaddingY) / PxPerYear) + 1900;

        Year(mom).Should().BeInRange(1924, 1926, "halfway between a 1900 mother and a 1950 son");
        Year(lateKid).Should().BeGreaterThanOrEqualTo(2010, "an inferred child is never closer than 20 years to a parent");
    }

    [Fact]
    public void SameFamily_GivesTheSameLayout_WhateverOrderItArrivesIn()
    {
        var fixture = LayoutFixture.LoadNamed("two-bridges");
        var people = fixture.MapPeople();
        var couples = FamilyTree.Web.Services.CoupleHelper.Derive(people);
        var engine = new TimelineLayoutEngine();

        var a = engine.ComputeLayout(people, couples, fixture.Focus);
        var shuffled = people.OrderByDescending(p => p.Id).ToList();
        var b = engine.ComputeLayout(shuffled, Enumerable.Reverse(couples).ToList(), fixture.Focus);

        b.Nodes.Select(n => (n.Person.Id, n.X, n.Y)).Should().BeEquivalentTo(a.Nodes.Select(n => (n.Person.Id, n.X, n.Y)));
    }

    [Fact]
    public void ParentCycle_StillLaysOutEveryone()
    {
        var t = new Family();
        var a = t.Add("A", 1900);
        var b = t.Add("B", 1930);
        var c = t.Add("C");
        t.Parent(a, b).Parent(b, c).Parent(c, a);

        Lay(t.Fixture()).Should().HaveCount(3);
    }

    // The anchor's first parent (canonical order: here the one whose first name sorts first)
    // gets the left, whichever of the two that is, as in the archived engine.
    [Theory, InlineData("Ellen", "Marc"), InlineData("Zoe", "Adam")]
    public void Anchor_PutsTheirFirstParentsFamilyOnTheLeft(string mother, string father)
    {
        var t = new Family();
        var ray = t.Add("Ray", 1922);
        var rose = t.Add("Rose", 1923);
        var bud = t.Add("Bud", 1920);
        var florence = t.Add("Florence", 1921);
        var mom = t.Add(mother, 1950);
        var dad = t.Add(father, 1948);
        var kid = t.Add("Kid", 1978);
        t.Marry(ray, rose).Parent(ray, mom).Parent(rose, mom)
         .Marry(bud, florence).Parent(bud, dad).Parent(florence, dad)
         .Marry(mom, dad).Parent(mom, kid).Parent(dad, kid);

        var nodes = Lay(t.Fixture(kid, anchor: kid));

        var (first, second) = string.CompareOrdinal(mother, father) < 0
            ? ((ray, rose), (bud, florence)) : ((bud, florence), (ray, rose));
        Math.Max(nodes[first.Item1].X, nodes[first.Item2].X).Should().BeLessThan(
            Math.Min(nodes[second.Item1].X, nodes[second.Item2].X), "the anchor's first parent's parents sit left");
    }

    // Bill has no parents on the tree, only a Sibling link to Ray. He sits beside Ray, on the
    // far side from Ray's wife, and his marriage grows outward without moving Ray's family.
    [Fact]
    public void OrphanSibling_SitsBesideTheirSibling_AndTheirMarriageDoesNotMoveTheSiblingsFamily()
    {
        var t = new Family();
        var ray = t.Add("Ray", 1922);
        var rose = t.Add("Rose", 1924);
        var ellen = t.Add("Ellen", 1950);
        var bill = t.Add("Bill", 1925);
        t.Marry(ray, rose).Parent(ray, ellen).Parent(rose, ellen).Sibling(ray, bill);
        var before = Lay(t.Fixture(ray));

        var gish = t.Add("Gish", 1926);
        t.Marry(bill, gish);
        var after = Lay(t.Fixture(ray));

        foreach (var nodes in new[] { before, after })
        {
            Math.Abs(nodes[bill].X - nodes[ray].X).Should().Be(NodeSpacingX, "brothers sit side by side");
            Math.Sign(nodes[bill].X - nodes[ray].X).Should().Be(Math.Sign(nodes[ray].X - nodes[rose].X), "Bill takes Ray's free side");
        }
        foreach (var id in new[] { ray, rose, ellen })
            after[id].X.Should().Be(before[id].X, "Bill's marriage must not move Ray's family");
    }

    // Two dateless partners share a row even when their evidence differs, and an inferred
    // year never comes within 20 years of a parent or child, following a chain of dateless
    // relatives to the nearest recorded year (R3's shape in the archived engine).
    [Fact]
    public void DatelessPartners_ShareARow_InsideTheirRelativesBounds()
    {
        var t = new Family();
        var marc = t.Add("Marc");
        var wife = t.Add("Wife");
        var willa = t.Add("Willa");
        var kim = t.Add("Kim", 1968);
        var zed = t.Add("Zed", 1970);
        t.Marry(marc, wife).Parent(marc, willa, kim).Parent(wife, willa).Parent(willa, zed);

        var nodes = Lay(t.Fixture(kim));

        nodes[marc].Y.Should().Be(nodes[wife].Y);
        (nodes[willa].Y - nodes[marc].Y).Should().BeGreaterThanOrEqualTo((int)(20 * PxPerYear));
        (nodes[zed].Y - nodes[willa].Y).Should().BeGreaterThanOrEqualTo((int)(20 * PxPerYear));
    }

    // Safety net over the fuzz harness's random families: everyone placed, nobody drawn on
    // top of anyone, and partners always at partner spacing.
    [Fact]
    public void GeneratedFamilies_PlaceEveryone_WithNoOverlaps()
    {
        var engine = new TimelineLayoutEngine();
        for (var i = 0; i < 200; i++)
        {
            var f = FamilyTreeLayoutFuzzTests.Generated(i, FamilyTreeLayoutFuzzTests.DefaultSeed);
            var layout = engine.ComputeLayout(f.People, f.Couples, f.Focus, f.Anchor);
            var report = LayoutQualityMetrics.Compute(layout, f.People, f.Couples);

            layout.Nodes.Should().HaveCount(f.People.Count, $"case {i}");
            report.NodeOverlaps.Should().Be(0, $"case {i}");
            layout.Nodes.Should().OnlyContain(n => n.X >= 0 && n.X <= layout.CanvasWidth && n.Y >= 0 && n.Y <= layout.CanvasHeight, $"case {i}");
        }
    }
}
