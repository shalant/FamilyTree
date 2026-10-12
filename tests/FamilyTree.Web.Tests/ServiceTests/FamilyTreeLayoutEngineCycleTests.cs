using FamilyTree.Core.Mappers;
using FamilyTree.Core.Models;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

// R0 (docs/TodoList.md): a Parent cycle already sitting in the database must not take the
// web process down. Before the fix PlaceGroup / MeasureGroup / MarkDescendants (and
// ReorderCrossEdgeChildren) recursed forever on one, and a StackOverflow can't be caught.
// The write paths now refuse to record a cycle; these tests cover the data that got in
// before they did.
//
// Families here go through PersonMapper + CoupleHelper like production, from named
// "parent -> child" edges. What the engine must do with a cycle: not crash or hang, draw
// every person exactly once at finite coordinates, keep the unrelated rest of the tree
// intact, and warn with the people involved. WHICH link it ignores to break the loop is
// deliberately not asserted.
public class FamilyTreeLayoutEngineCycleTests
{
    private sealed class CapturingLogger : ILogger<FamilyTreeLayoutEngine>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class Family
    {
        private readonly Dictionary<string, Guid> _ids = new();
        private readonly List<Relationship> _rels = [];
        public Guid this[string name] => _ids[name];

        public Family Person(params string[] names)
        {
            foreach (var n in names) _ids.TryAdd(n, Guid.NewGuid());
            return this;
        }
        public Family Parent(string parent, string child) => Add(RelationshipType.Parent, parent, child);
        public Family Spouse(string a, string b) => Add(RelationshipType.Spouse, a, b);

        private Family Add(RelationshipType type, string a, string b)
        {
            Person(a, b);
            var (x, y) = type == RelationshipType.Parent || _ids[a].CompareTo(_ids[b]) < 0 ? (a, b) : (b, a);
            _rels.Add(new Relationship { Id = Guid.NewGuid(), Type = type, PersonAId = _ids[x], PersonBId = _ids[y] });
            return this;
        }

        // The order people are handed to ComputeLayout decides which group the engine reaches
        // first, and whether a cycle is reachable from a root at all (confirmed 2026-10-11: the
        // Invariant7b shape overflows the stack in insertion order but not in name order). So
        // every test here runs under all of these orders rather than trusting one.
        public enum Order { Insertion, Reversed, ByName }

        public List<PersonDto> People(Order order = Order.Insertion)
        {
            var people = _ids
                .Select(kv => PersonMapper.MapPersonToDto(
                    new Person { Id = kv.Value, FirstName = kv.Key, LastName = "Test" }, _rels))
                .ToList();
            return order switch
            {
                Order.Reversed => [.. people.AsEnumerable().Reverse()],
                Order.ByName => [.. people.OrderBy(p => p.FirstName)],
                _ => people,
            };
        }

        public string NameOf(Guid id) => _ids.First(kv => kv.Value == id).Key;
    }

    private static readonly Family.Order[] AllOrders = Enum.GetValues<Family.Order>();

    private static FamilyTreeLayout Layout(
        Family f, string focus, Family.Order order = Family.Order.Insertion, ILogger<FamilyTreeLayoutEngine>? logger = null)
    {
        var people = f.People(order);
        return new FamilyTreeLayoutEngine(logger).ComputeLayout(people, CoupleHelper.Derive(people), f[focus]);
    }

    // Lays the family out once per list order and runs the same sanity checks (and any
    // extra assertions) on each.
    private static void AssertRendersInEveryOrder(Family f, string focus, Action<FamilyTreeLayout>? extra = null)
    {
        foreach (var order in AllOrders)
        {
            var layout = Layout(f, focus, order);
            var expected = f.People(order).Select(p => p.Id).ToList();
            layout.Nodes.Select(n => n.Person.Id).Should().BeEquivalentTo(expected,
                $"every person is drawn exactly once, cycle or not ({order} order)");
            layout.Nodes.Should().OnlyContain(n => n.X >= 0 && n.Y >= 0, $"({order} order)");
            layout.CanvasWidth.Should().BeGreaterThan(0);
            layout.CanvasHeight.Should().BeGreaterThan(0);
            extra?.Invoke(layout);
        }
    }

    // The exact shape from Invariant7b: Root+Root2 are A's parents, A is B's parent, and B is
    // ALSO recorded as A's parent (A has three parents). Two parents alone leave the cycle
    // unreachable from a root, which is why it took a third to find this.
    [Fact]
    [Trait("Category", "Regression")]
    public void ThreeParentCycle_BelowACouple_RendersWithoutCrashing()
    {
        var f = new Family().Spouse("Root", "Root2")
            .Parent("Root", "A").Parent("Root2", "A").Parent("A", "B").Parent("B", "A");

        AssertRendersInEveryOrder(f, "Root", layout =>
        {
            var y = layout.Nodes.ToDictionary(n => f.NameOf(n.Person.Id), n => n.Y);
            y["Root"].Should().BeLessThan(y["A"], "the part of the tree that is not in the loop still renders in order");
        });
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void CycleThreeGenerationsDeep_RendersWithoutCrashing()
    {
        // Root+Root2 -> A -> B -> C, and C is also recorded as one of A's parents.
        var f = new Family().Spouse("Root", "Root2")
            .Parent("Root", "A").Parent("Root2", "A")
            .Parent("A", "B").Parent("B", "C").Parent("C", "A");

        AssertRendersInEveryOrder(f, "Root");
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void PersonRecordedAsTheirOwnParent_RendersWithoutCrashing()
    {
        var f = new Family().Spouse("Root", "Root2").Parent("Root", "A").Parent("Root2", "A")
            .Parent("A", "A").Parent("A", "B");

        AssertRendersInEveryOrder(f, "A");
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void TwoPeopleEachOthersParent_WithNobodyAboveThem_RendersWithoutCrashing()
    {
        // No root group at all: everyone is inside the loop, plus an unrelated healthy family.
        var f = new Family().Parent("A", "B").Parent("B", "A")
            .Spouse("Mom", "Dad").Parent("Mom", "Kid").Parent("Dad", "Kid");

        AssertRendersInEveryOrder(f, "Kid", layout =>
        {
            var node = layout.Nodes.ToDictionary(n => f.NameOf(n.Person.Id));
            node["Mom"].Y.Should().BeLessThan(node["Kid"].Y, "the healthy family beside the loop is laid out normally");
            Math.Abs(node["Mom"].X - node["Dad"].X).Should().BeLessOrEqualTo(200, "the healthy couple still sits together");
        });
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void CycleInsideABranchThatMarriesAcrossRootGroups_RendersWithoutCrashing()
    {
        // Two root couples whose children (X and Y) marry: the cross-root-couple path, which
        // re-walks the group chain in ReorderCrossEdgeChildren. X's sibling Z is also recorded
        // as W's child while W is Z's child, a loop that is NOT through X or Y, so the walk
        // reaches it (the couple's own members are cut out of the chain before it runs).
        var f = new Family().Spouse("R1", "R2").Spouse("R3", "R4")
            .Parent("R1", "X").Parent("R2", "X").Parent("R1", "Z").Parent("R2", "Z")
            .Parent("R3", "Y").Parent("R4", "Y")
            .Spouse("X", "Y")
            .Parent("X", "KidXY").Parent("Y", "KidXY")
            .Parent("Z", "W").Parent("W", "Z");

        AssertRendersInEveryOrder(f, "X");
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void CycleThroughACouplesChildren_RendersWithoutCrashing()
    {
        // A and S are married and have child K; K is also recorded as a parent of A.
        var f = new Family().Spouse("A", "S").Parent("A", "K").Parent("S", "K").Parent("K", "A");

        AssertRendersInEveryOrder(f, "A");
    }

    [Fact]
    public void Cycle_IsLoggedAsAWarning_NamingThePeopleInvolved()
    {
        var f = new Family().Spouse("Root", "Root2")
            .Parent("Root", "A").Parent("Root2", "A").Parent("A", "B").Parent("B", "A");
        var logger = new CapturingLogger();

        Layout(f, "Root", logger: logger);

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        warnings.Should().NotBeEmpty("a cycle in the data must leave a trace in the logs");
        warnings.Should().Contain(m => m.Contains(f["A"].ToString()) && m.Contains(f["B"].ToString()),
            "the warning names the people caught in the loop by id");
    }

    [Fact]
    public void NoCycle_LogsNothing()
    {
        var f = new Family().Spouse("Root", "Root2").Parent("Root", "A").Parent("Root2", "A").Parent("A", "B");
        var logger = new CapturingLogger();

        Layout(f, "Root", logger: logger);

        logger.Entries.Should().BeEmpty();
    }

    // Guard for "layout for trees WITHOUT a cycle must not change at all": a diamond (the same
    // grandchild reachable from two parent groups) is legal data and must keep rendering
    // exactly as before. A global visited-set in the recursions would have changed this; the
    // in-progress-path guard must not.
    [Fact]
    public void Diamond_NotACycle_RendersAsBeforeAndLogsNothing()
    {
        var f = new Family().Spouse("G1", "G2").Parent("G1", "P").Parent("G2", "P")
            .Parent("P", "K").Parent("G1", "K");
        var logger = new CapturingLogger();

        foreach (var order in AllOrders)
            Layout(f, "P", order, logger);

        logger.Entries.Should().BeEmpty("a diamond is not a loop");
        AssertRendersInEveryOrder(f, "P");
    }
}
