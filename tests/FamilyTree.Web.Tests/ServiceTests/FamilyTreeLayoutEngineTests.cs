using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Services;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

// Regression tests for the layout-engine edge cases found while building self-service
// tree linking (2026-07-04): a sibling with no shared parent on the tree, a married
// sibling whose spouse has no other recorded relatives, and a 3+ mutually-sibling
// cluster. Each of these silently broke in a different way (disconnected component,
// wrong generation depth, spouse landing decades off on the timeline, overlapping
// nodes) before the underlying fixes — these tests exist so a future change to
// FamilyTreeLayoutEngine can't reintroduce any of them without a test failing first.
public class FamilyTreeLayoutEngineTests
{
    private readonly FamilyTreeLayoutEngine _engine = new();

    [Fact]
    public void UnrelatedCrossRootCouple_DoesNotDisplaceAnOrphanSiblingGroupPastItsRoot()
    {
        // Reproduces the exact bug: Marc (child of Bud+Florence) marries Ellen (child of
        // Ray+Rose) — a pre-existing cross-root couple the layout engine specifically
        // reorders root groups for, so the couple's midpoint lines up correctly. Bill, an
        // unrelated orphan sibling of Ray with his own child (Willa), naturally sits
        // right after Ray+Rose — that reorder must not push Bill to sit BEFORE Ray+Rose,
        // even though it's free to shift Bud+Florence's group around (that one's actually
        // involved in the cross-root pairing).
        var ray = Guid.NewGuid();
        var rose = Guid.NewGuid();
        var ellen = Guid.NewGuid();
        var sarah = Guid.NewGuid();
        var bill = Guid.NewGuid();
        var willa = Guid.NewGuid();
        var bud = Guid.NewGuid();
        var florence = Guid.NewGuid();
        var marc = Guid.NewGuid();

        var people = BuildFamily(ray, rose, ellen, sarah);
        people.First(p => p.Id == ray).SiblingIds = [bill];
        people.Add(new PersonDto
        {
            Id = bill, FirstName = "Bill", LastName = "Small",
            ParentIds = [], ChildIds = [willa], SpouseIds = [], SiblingIds = [ray]
        });
        people.Add(new PersonDto
        {
            Id = willa, FirstName = "Willa", LastName = "Kuh",
            ParentIds = [bill], ChildIds = [], SpouseIds = []
        });
        people.Add(new PersonDto
        {
            Id = bud, FirstName = "Bud", LastName = "Small",
            ParentIds = [], ChildIds = [marc], SpouseIds = [florence]
        });
        people.Add(new PersonDto
        {
            Id = florence, FirstName = "Florence", LastName = "Small",
            ParentIds = [], ChildIds = [marc], SpouseIds = [bud]
        });
        people.First(p => p.Id == ellen).SpouseIds = [marc];
        people.Add(new PersonDto
        {
            Id = marc, FirstName = "Marc", LastName = "Small",
            ParentIds = [bud, florence], ChildIds = [], SpouseIds = [ellen]
        });

        var layout = _engine.ComputeLayout(people, CoupleHelper.Derive(people), ray);

        var rayNode = layout.Nodes.Single(n => n.Person.Id == ray);
        var roseNode = layout.Nodes.Single(n => n.Person.Id == rose);
        var billNode = layout.Nodes.Single(n => n.Person.Id == bill);
        var willaNode = layout.Nodes.Single(n => n.Person.Id == willa);

        billNode.X.Should().BeGreaterThan(Math.Max(rayNode.X, roseNode.X),
            "Bill has nothing to do with the Marc/Ellen cross-root marriage and must stay to the right of Ray+Rose, not get shoved before them");
        willaNode.X.Should().Be(billNode.X,
            "Willa (Bill's only child) must render directly under Bill, not wedged into an unrelated couple's row");
    }

    [Fact]
    public void AddingNewSiblingDoesNotMoveExistingNodes()
    {
        var ray = Guid.NewGuid();
        var rose = Guid.NewGuid();
        var ellen = Guid.NewGuid();
        var sarah = Guid.NewGuid();
        var bill = Guid.NewGuid();

        var before = BuildFamily(ray, rose, ellen, sarah);
        var layoutBefore = _engine.ComputeLayout(before, CoupleHelper.Derive(before), ray);
        var positionsBefore = layoutBefore.Nodes.ToDictionary(n => n.Person.Id, n => (n.X, n.Y));

        // Bill joins later as an orphan sibling of Ray — no shared parent on the tree.
        var after = BuildFamily(ray, rose, ellen, sarah);
        after.Add(new PersonDto
        {
            Id = bill, FirstName = "Bill", LastName = "Small",
            ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [ray]
        });
        after.First(p => p.Id == ray).SiblingIds = [bill];

        var layoutAfter = _engine.ComputeLayout(after, CoupleHelper.Derive(after), ray);

        foreach (var (id, pos) in positionsBefore)
        {
            var node = layoutAfter.Nodes.Single(n => n.Person.Id == id);
            (node.X, node.Y).Should().Be(pos,
                $"adding a new sibling must never move a person who was already positioned");
        }
    }

    [Fact]
    public void GrowingAnOrphanSiblingsFamily_PushesTheNextSiblingRightWithoutOverlap()
    {
        // Ray, Bill, Morton are three orphan-sibling roots in a row. Bill has a child
        // (Willa). When Willa marries Tom, Bill's subtree needs more horizontal room
        // (a couple is wider than a single person) — Morton must get pushed further
        // right to make room, not end up overlapping or crowding Bill/Willa/Tom's column.
        var ray = Guid.NewGuid();
        var rose = Guid.NewGuid();
        var ellen = Guid.NewGuid();
        var sarah = Guid.NewGuid();
        var bill = Guid.NewGuid();
        var willa = Guid.NewGuid();
        var morton = Guid.NewGuid();
        var tom = Guid.NewGuid();

        List<PersonDto> BuildScenario(bool willaMarriesTom)
        {
            var people = BuildFamily(ray, rose, ellen, sarah);
            people.First(p => p.Id == ray).SiblingIds = [bill, morton];
            people.Add(new PersonDto
            {
                Id = bill, FirstName = "Bill", LastName = "Small",
                ParentIds = [], ChildIds = [willa], SpouseIds = [], SiblingIds = [ray, morton]
            });
            people.Add(new PersonDto
            {
                Id = morton, FirstName = "Morton", LastName = "Small",
                ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [ray, bill]
            });
            people.Add(new PersonDto
            {
                Id = willa, FirstName = "Willa", LastName = "Kuh",
                ParentIds = [bill], ChildIds = [], SpouseIds = willaMarriesTom ? [tom] : []
            });
            if (willaMarriesTom)
                people.Add(new PersonDto
                {
                    Id = tom, FirstName = "Tom", LastName = "Kuh",
                    ParentIds = [], ChildIds = [], SpouseIds = [willa]
                });

            return people;
        }

        var without = BuildScenario(willaMarriesTom: false);
        var layoutWithout = _engine.ComputeLayout(without, CoupleHelper.Derive(without), ray);
        var mortonBefore = layoutWithout.Nodes.Single(n => n.Person.Id == morton);

        var with = BuildScenario(willaMarriesTom: true);
        var layoutWith = _engine.ComputeLayout(with, CoupleHelper.Derive(with), ray);
        var mortonAfter = layoutWith.Nodes.Single(n => n.Person.Id == morton);
        var willaNode = layoutWith.Nodes.Single(n => n.Person.Id == willa);
        var tomNode = layoutWith.Nodes.Single(n => n.Person.Id == tom);
        var billNode = layoutWith.Nodes.Single(n => n.Person.Id == bill);

        mortonAfter.X.Should().BeGreaterThan(mortonBefore.X,
            "Bill's subtree needs more horizontal room once Willa marries Tom (a couple is wider than a single person) — Morton must be pushed right to make room");

        var billSubtreeRightEdge = Math.Max(willaNode.X, tomNode.X);
        mortonAfter.X.Should().BeGreaterThan(billSubtreeRightEdge,
            "Morton must not overlap or crowd Bill's now-wider subtree (Willa + Tom)");
    }

    [Fact]
    public void MarryingAnOrphanSiblingRoot_DoesNotJumpTheirGroupPastUnrelatedRootsByAlphabeticalAccident()
    {
        // Reproduces a real bug (2026-07-06): marrying "Bill" (an orphan-sibling root
        // individual) to a new spouse sent his ENTIRE nuclear group to the far left of
        // the tree, past Bud+Florence — instead of Morton (Bill's actual neighbor)
        // simply being pushed right. Root cause: root-group order used to come from
        // CoupleHelper.Derive's dictionary insertion order, which is triggered by
        // whichever CHILD's shared-parent check runs first — Willa's last name ("Kuh")
        // sorts alphabetically before "Rosenberg"/"Small", so the instant she gained a
        // second parent (Gish), Bill+Gish's couple jumped to the front of the ordering.
        // Nothing genealogical about it — purely an artifact of dictionary iteration order.
        var ray = Guid.NewGuid(); var rose = Guid.NewGuid();
        var ellen = Guid.NewGuid(); var sarah = Guid.NewGuid();
        var bill = Guid.NewGuid(); var willa = Guid.NewGuid(); var gish = Guid.NewGuid();
        var morton = Guid.NewGuid();
        var bud = Guid.NewGuid(); var florence = Guid.NewGuid(); var marc = Guid.NewGuid();

        List<PersonDto> BuildScenario(bool billMarriesGish)
        {
            var people = BuildFamily(ray, rose, ellen, sarah);
            people.First(p => p.Id == ray).SiblingIds = [bill, morton];
            people.Add(new PersonDto
            {
                Id = bill, FirstName = "Bill", LastName = "Small",
                ParentIds = [], ChildIds = [willa], SpouseIds = billMarriesGish ? [gish] : [],
                SiblingIds = [ray, morton]
            });
            people.Add(new PersonDto
            {
                Id = morton, FirstName = "Morton", LastName = "Small",
                ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [ray, bill]
            });
            people.Add(new PersonDto
            {
                Id = willa, FirstName = "Willa", LastName = "Kuh",
                ParentIds = billMarriesGish ? [bill, gish] : [bill], ChildIds = [], SpouseIds = []
            });
            if (billMarriesGish)
                people.Add(new PersonDto
                {
                    Id = gish, FirstName = "Gish", LastName = "Small",
                    ParentIds = [], ChildIds = [willa], SpouseIds = [bill]
                });
            people.Add(new PersonDto
            {
                Id = bud, FirstName = "Bud", LastName = "Rosenberg",
                ParentIds = [], ChildIds = [marc], SpouseIds = [florence]
            });
            people.Add(new PersonDto
            {
                Id = florence, FirstName = "Florence", LastName = "Rosenberg",
                ParentIds = [], ChildIds = [marc], SpouseIds = [bud]
            });
            people.First(p => p.Id == ellen).SpouseIds = [marc];
            people.Add(new PersonDto
            {
                Id = marc, FirstName = "Marc", LastName = "Rosenberg",
                ParentIds = [bud, florence], ChildIds = [], SpouseIds = [ellen]
            });

            // Production (PersonService.GetAllAsync) always orders people alphabetically
            // by (LastName, FirstName) before the layout engine ever sees them — the bug
            // this test reproduces depends entirely on that ordering (Willa's last name
            // "Kuh" sorting before "Rosenberg"/"Small"), so the test must match it exactly
            // rather than relying on incidental .Add() call order.
            return people.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToList();
        }

        var before = BuildScenario(billMarriesGish: false);
        var layoutBefore = _engine.ComputeLayout(before, CoupleHelper.Derive(before), ray);
        var budBefore = layoutBefore.Nodes.Single(n => n.Person.Id == bud);
        var florenceBefore = layoutBefore.Nodes.Single(n => n.Person.Id == florence);
        var rayBefore = layoutBefore.Nodes.Single(n => n.Person.Id == ray);
        var roseBefore = layoutBefore.Nodes.Single(n => n.Person.Id == rose);
        var mortonBefore = layoutBefore.Nodes.Single(n => n.Person.Id == morton);

        var after = BuildScenario(billMarriesGish: true);
        var layoutAfter = _engine.ComputeLayout(after, CoupleHelper.Derive(after), ray);
        var budAfter = layoutAfter.Nodes.Single(n => n.Person.Id == bud);
        var florenceAfter = layoutAfter.Nodes.Single(n => n.Person.Id == florence);
        var rayAfter = layoutAfter.Nodes.Single(n => n.Person.Id == ray);
        var roseAfter = layoutAfter.Nodes.Single(n => n.Person.Id == rose);
        var billAfter = layoutAfter.Nodes.Single(n => n.Person.Id == bill);
        var mortonAfter = layoutAfter.Nodes.Single(n => n.Person.Id == morton);

        (budAfter.X, budAfter.Y).Should().Be((budBefore.X, budBefore.Y),
            "marrying Bill must not move Bud+Florence's already-placed group");
        (florenceAfter.X, florenceAfter.Y).Should().Be((florenceBefore.X, florenceBefore.Y));
        (rayAfter.X, rayAfter.Y).Should().Be((rayBefore.X, rayBefore.Y),
            "marrying Bill must not move Ray+Rose's already-placed group");
        (roseAfter.X, roseAfter.Y).Should().Be((roseBefore.X, roseBefore.Y));

        billAfter.X.Should().BeGreaterThan(
            Math.Max(budAfter.X, Math.Max(florenceAfter.X, Math.Max(rayAfter.X, roseAfter.X))),
            "Bill's own group must still land after Bud+Florence and Ray+Rose, not jump to the front of the tree");
        mortonAfter.X.Should().BeGreaterThan(mortonBefore.X,
            "Bill's subtree is wider now that he has a spouse — Morton (his actual neighbor) should get pushed right");
    }

    [Fact]
    public void OrphanSiblingConnector_IsDrawnBetweenActualPositions()
    {
        var ray = Guid.NewGuid();
        var rose = Guid.NewGuid();
        var ellen = Guid.NewGuid();
        var sarah = Guid.NewGuid();
        var bill = Guid.NewGuid();

        var people = BuildFamily(ray, rose, ellen, sarah);
        people.First(p => p.Id == ray).SiblingIds = [bill];
        people.Add(new PersonDto
        {
            Id = bill, FirstName = "Bill", LastName = "Small",
            ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [ray]
        });

        var layout = _engine.ComputeLayout(people, CoupleHelper.Derive(people), ray);

        var rayNode = layout.Nodes.Single(n => n.Person.Id == ray);
        var billNode = layout.Nodes.Single(n => n.Person.Id == bill);

        layout.SiblingLinks.Should().ContainSingle(
            "an explicit sibling relationship with no shared parent has no couple/parent connector to hang off of, so it needs its own line");
        var link = layout.SiblingLinks[0];
        var drawnPoints = new[] { (link.AX, link.AY), (link.BX, link.BY) };
        drawnPoints.Should().Contain((rayNode.X, rayNode.Y));
        drawnPoints.Should().Contain((billNode.X, billNode.Y));
    }

    [Fact]
    public void AddingThirdMutualSibling_DoesNotMoveAnyPreviouslyPlacedPerson()
    {
        var ray = Guid.NewGuid();
        var rose = Guid.NewGuid();
        var ellen = Guid.NewGuid();
        var sarah = Guid.NewGuid();
        var bill = Guid.NewGuid();
        var morton = Guid.NewGuid();

        // Ray, Bill already on the tree and positioned.
        var before = BuildFamily(ray, rose, ellen, sarah);
        before.First(p => p.Id == ray).SiblingIds = [bill];
        before.Add(new PersonDto
        {
            Id = bill, FirstName = "Bill", LastName = "Small",
            ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [ray]
        });
        var layoutBefore = _engine.ComputeLayout(before, CoupleHelper.Derive(before), ray);
        var positionsBefore = layoutBefore.Nodes.ToDictionary(n => n.Person.Id, n => (n.X, n.Y));

        // Morton joins later as a third mutual sibling of both Ray and Bill — this is
        // the exact case that broke the earlier pairwise-reorder approach (moving Bill
        // next to Morton undid Bill's adjacency to Ray).
        var after = BuildFamily(ray, rose, ellen, sarah);
        after.First(p => p.Id == ray).SiblingIds = [bill, morton];
        after.Add(new PersonDto
        {
            Id = bill, FirstName = "Bill", LastName = "Small",
            ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [ray, morton]
        });
        after.Add(new PersonDto
        {
            Id = morton, FirstName = "Morton", LastName = "Small",
            ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [ray, bill]
        });

        var layoutAfter = _engine.ComputeLayout(after, CoupleHelper.Derive(after), ray);

        foreach (var (id, pos) in positionsBefore)
        {
            var node = layoutAfter.Nodes.Single(n => n.Person.Id == id);
            (node.X, node.Y).Should().Be(pos,
                "adding a third mutual sibling must never move anyone who was already positioned");
        }
    }

    [Fact]
    public void MarriedOrphanSibling_SpouseYearAlignsWithPartner()
    {
        var ray = Guid.NewGuid();
        var rose = Guid.NewGuid();
        var ellen = Guid.NewGuid();
        var sarah = Guid.NewGuid();
        var bill = Guid.NewGuid();
        var willa = Guid.NewGuid();
        var tom = Guid.NewGuid();

        var people = BuildFamily(ray, rose, ellen, sarah);
        people.Add(new PersonDto
        {
            Id = bill, FirstName = "Bill", LastName = "Small",
            ParentIds = [], ChildIds = [willa], SpouseIds = [], SiblingIds = [ray]
        });
        people.First(p => p.Id == ray).SiblingIds = [bill];
        people.Add(new PersonDto
        {
            Id = willa, FirstName = "Willa", LastName = "Kuh",
            ParentIds = [bill], ChildIds = [], SpouseIds = [tom]
        });
        // Tom has no parent/child/sibling of his own on the tree — spouse-year
        // inference is the only way he can ever land near Willa on the timeline.
        people.Add(new PersonDto
        {
            Id = tom, FirstName = "Tom", LastName = "Kuh",
            ParentIds = [], ChildIds = [], SpouseIds = [willa]
        });

        var layout = _engine.ComputeLayout(people, CoupleHelper.Derive(people), ray);

        var willaNode = layout.Nodes.Single(n => n.Person.Id == willa);
        var tomNode = layout.Nodes.Single(n => n.Person.Id == tom);

        Math.Abs(tomNode.Y - willaNode.Y).Should().BeLessOrEqualTo(1,
            "a spouse with no other recorded relatives should still land on the same generation as their partner, not fall back to the tree-wide median year");
    }

    [Fact]
    public void ThreeWaySiblingCluster_AllLandOnTheSameGeneration()
    {
        var ray = Guid.NewGuid();
        var rose = Guid.NewGuid();
        var ellen = Guid.NewGuid();
        var sarah = Guid.NewGuid();
        var bill = Guid.NewGuid();
        var morton = Guid.NewGuid();

        var people = BuildFamily(ray, rose, ellen, sarah);
        people.First(p => p.Id == ray).SiblingIds = [bill, morton];
        people.Add(new PersonDto
        {
            Id = bill, FirstName = "Bill", LastName = "Small",
            ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [ray, morton]
        });
        people.Add(new PersonDto
        {
            Id = morton, FirstName = "Morton", LastName = "Small",
            ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [ray, bill]
        });

        var layout = _engine.ComputeLayout(people, CoupleHelper.Derive(people), ray);

        var rayDepth = layout.Nodes.Single(n => n.Person.Id == ray).Depth;
        var billDepth = layout.Nodes.Single(n => n.Person.Id == bill).Depth;
        var mortonDepth = layout.Nodes.Single(n => n.Person.Id == morton).Depth;

        billDepth.Should().Be(rayDepth, "an explicit sibling with no shared parent must share their sibling's generation");
        mortonDepth.Should().Be(rayDepth, "an explicit sibling with no shared parent must share their sibling's generation");
    }

    [Fact]
    public void FullSiblingsSharingAParent_DoNotGetARedundantSiblingConnector()
    {
        var ray = Guid.NewGuid();
        var rose = Guid.NewGuid();
        var ellen = Guid.NewGuid();
        var sarah = Guid.NewGuid();

        var people = BuildFamily(ray, rose, ellen, sarah);

        var layout = _engine.ComputeLayout(people, CoupleHelper.Derive(people), ray);

        layout.SiblingLinks.Should().BeEmpty(
            "Ellen and Sarah already share a parent and get a normal stem/T-bar/drop connector — " +
            "PersonDto.SiblingIds includes them too (inferred from the shared parent), but drawing " +
            "a second dashed line directly between them would be a redundant, confusing extra connector");
    }

    [Fact]
    public void NoTwoPeopleOccupyTheSameCoordinates()
    {
        var ray = Guid.NewGuid();
        var rose = Guid.NewGuid();
        var ellen = Guid.NewGuid();
        var sarah = Guid.NewGuid();
        var bill = Guid.NewGuid();
        var willa = Guid.NewGuid();

        var people = BuildFamily(ray, rose, ellen, sarah);
        people.First(p => p.Id == ray).SiblingIds = [bill];
        people.Add(new PersonDto
        {
            Id = bill, FirstName = "Bill", LastName = "Small",
            ParentIds = [], ChildIds = [willa], SpouseIds = [], SiblingIds = [ray]
        });
        people.Add(new PersonDto
        {
            Id = willa, FirstName = "Willa", LastName = "Kuh",
            ParentIds = [bill], ChildIds = [], SpouseIds = []
        });

        var layout = _engine.ComputeLayout(people, CoupleHelper.Derive(people), ray);

        var duplicateCoordinates = layout.Nodes
            .GroupBy(n => (n.X, n.Y))
            .Where(g => g.Count() > 1)
            .ToList();

        duplicateCoordinates.Should().BeEmpty(
            "two distinct people rendered at the exact same coordinates look like one person merged with themselves");
    }

    [Fact]
    public void SiblingWithNoParentLink_LandsNearTheirSiblings_NotFarAcrossTheWholeSubtree()
    {
        // Reproduces a real bug (2026-07-06): "test rosenberg" had explicit Sibling
        // relationships to Douglas and Lauren — both several generations deep inside an
        // existing Marc+Ellen tree, not roots themselves — but no ParentIds of his own.
        // The "root individual" fallback (people in no NuclearGroup at all) doesn't
        // consult SiblingIds — it just appends after every root group's ENTIRE subtree
        // has been placed, landing him past Lauren's spouse and child, well outside the
        // visual "umbrella" of the family he's actually linked into.
        var (marc, ellen, douglas, lauren, omar, amelia, newby) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var people = BuildNestedSiblingFamily(marc, ellen, douglas, lauren, omar, amelia);
        people.First(p => p.Id == douglas).SiblingIds = [lauren, newby];
        people.First(p => p.Id == lauren).SiblingIds = [douglas, newby];
        people.Add(new PersonDto
        {
            Id = newby, FirstName = "Newby", LastName = "Rosenberg",
            ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [douglas, lauren]
        });

        var layout = _engine.ComputeLayout(people, CoupleHelper.Derive(people), marc);

        var douglasNode = layout.Nodes.Single(n => n.Person.Id == douglas);
        var laurenNode = layout.Nodes.Single(n => n.Person.Id == lauren);
        var omarNode = layout.Nodes.Single(n => n.Person.Id == omar);
        var newbyNode = layout.Nodes.Single(n => n.Person.Id == newby);

        var siblingGroupRightEdge = Math.Max(douglasNode.X, Math.Max(laurenNode.X, omarNode.X));

        (newbyNode.X - siblingGroupRightEdge).Should().BeLessOrEqualTo(150,
            "a person whose only connection is a Sibling relationship to people deep inside an " +
            "existing nuclear family should land right next to them, not far across the whole subtree");
    }

    [Fact]
    public void AddingASiblingWithNoParentLink_DoesNotMoveAnyPreviouslyPlacedPerson()
    {
        var (marc, ellen, douglas, lauren, omar, amelia, newby) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var before = BuildNestedSiblingFamily(marc, ellen, douglas, lauren, omar, amelia);
        var layoutBefore = _engine.ComputeLayout(before, CoupleHelper.Derive(before), marc);
        var positionsBefore = layoutBefore.Nodes.ToDictionary(n => n.Person.Id, n => (n.X, n.Y));

        var after = BuildNestedSiblingFamily(marc, ellen, douglas, lauren, omar, amelia);
        after.First(p => p.Id == douglas).SiblingIds = [lauren, newby];
        after.First(p => p.Id == lauren).SiblingIds = [douglas, newby];
        after.Add(new PersonDto
        {
            Id = newby, FirstName = "Newby", LastName = "Rosenberg",
            ParentIds = [], ChildIds = [], SpouseIds = [], SiblingIds = [douglas, lauren]
        });

        var layoutAfter = _engine.ComputeLayout(after, CoupleHelper.Derive(after), marc);

        foreach (var (id, pos) in positionsBefore)
        {
            var node = layoutAfter.Nodes.Single(n => n.Person.Id == id);
            (node.X, node.Y).Should().Be(pos,
                "adding a parentless sibling must never move anyone who was already positioned");
        }
    }

    // Marc+Ellen (root couple) → Douglas (full sibling, no spouse) and Lauren (full
    // sibling, married to Omar with child Amelia) — mirrors the real family shape:
    // a nested (non-root) sibling pair, one of them with their own nested couple+child.
    private static List<PersonDto> BuildNestedSiblingFamily(
        Guid marc, Guid ellen, Guid douglas, Guid lauren, Guid omar, Guid amelia) =>
    [
        new() { Id = marc, FirstName = "Marc", LastName = "Rosenberg",
            ParentIds = [], ChildIds = [douglas, lauren], SpouseIds = [ellen], SiblingIds = [],
            BirthDate = new DateOnly(1948, 1, 1) },
        new() { Id = ellen, FirstName = "Ellen", LastName = "Rosenberg",
            ParentIds = [], ChildIds = [douglas, lauren], SpouseIds = [marc], SiblingIds = [],
            BirthDate = new DateOnly(1950, 1, 1) },
        new() { Id = douglas, FirstName = "Douglas", LastName = "Rosenberg",
            ParentIds = [marc, ellen], ChildIds = [], SpouseIds = [], SiblingIds = [lauren],
            BirthDate = new DateOnly(1978, 1, 1) },
        new() { Id = lauren, FirstName = "Lauren", LastName = "Rosenberg",
            ParentIds = [marc, ellen], ChildIds = [amelia], SpouseIds = [omar], SiblingIds = [douglas],
            BirthDate = new DateOnly(1982, 1, 1) },
        new() { Id = omar, FirstName = "Omar", LastName = "Anbari",
            ParentIds = [], ChildIds = [amelia], SpouseIds = [lauren], SiblingIds = [],
            BirthDate = new DateOnly(1981, 1, 1) },
        new() { Id = amelia, FirstName = "Amelia", LastName = "Anbari",
            ParentIds = [lauren, omar], ChildIds = [], SpouseIds = [], SiblingIds = [] },
    ];

    private static List<PersonDto> BuildFamily(Guid ray, Guid rose, Guid ellen, Guid sarah) =>
    [
        new() { Id = ray, FirstName = "Ray", LastName = "Small",
            ParentIds = [], ChildIds = [ellen, sarah], SpouseIds = [rose], SiblingIds = [],
            BirthDate = new DateOnly(1945, 1, 1) },
        new() { Id = rose, FirstName = "Rose", LastName = "Small",
            ParentIds = [], ChildIds = [ellen, sarah], SpouseIds = [ray], SiblingIds = [],
            BirthDate = new DateOnly(1948, 1, 1) },
        // SiblingIds mirrors what PersonMapper actually produces in production —
        // it merges explicit Sibling relationships with siblings inferred from a
        // shared parent, so a hand-built PersonDto needs to set it too, or a test
        // could miss a bug that only shows up when SiblingIds includes inferred
        // entries (see RedundantSiblingConnector regression below).
        new() { Id = ellen, FirstName = "Ellen", LastName = "Small",
            ParentIds = [ray, rose], ChildIds = [], SpouseIds = [], SiblingIds = [sarah],
            BirthDate = new DateOnly(1970, 1, 1) },
        new() { Id = sarah, FirstName = "Sarah", LastName = "Small",
            ParentIds = [ray, rose], ChildIds = [], SpouseIds = [], SiblingIds = [ellen],
            BirthDate = new DateOnly(1972, 1, 1) },
    ];
}
