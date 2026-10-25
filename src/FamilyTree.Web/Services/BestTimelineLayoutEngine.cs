using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;
using static FamilyTree.Web.Services.FamilyTreeLayoutEngine;
using Line = (double X1, double Y1, double X2, double Y2);

namespace FamilyTree.Web.Services;

/// <summary>
/// PROTOTYPE replacement for <see cref="FamilyTreeLayoutEngine"/> (ADR 004, option 1, started
/// 2026-10-17). Keeps the exact birth-year timeline on the Y axis, but places people the way
/// layered graph drawing does instead of with per-case rules:
///
///   1. Generation layers, solved from two rules: partners share a layer, and a child sits at
///      least one layer below each parent. A family with no recorded parents floats down to
///      just above its children, so families don't need aligned generations. Layers decide
///      left-to-right order only; they are never drawn.
///   2. Everyone married to each other forms one rigid block, partners SpouseSpacingX apart,
///      so a couple from two different families (Ellen + Marc) can't be pulled apart.
///   3. Blocks are ordered within each layer to reduce crossings (DFS start, then barycentre
///      sweeps, keeping the best order seen). An orphan sibling (no parents on the tree)
///      shares their sibling's layer and stays beside them in every order tried. The anchor
///      (the signed-in user's own person) puts their first parent's side on the left, as the
///      archived engine does, by mirroring the finished order.
///   4. Y is the birth year. A dateless person's year comes from relatives, held between
///      their parents' and children's years with MinParentChildGap either side; dateless
///      partners share one year.
///   5. X positions are solved together: children are pulled under their parents and parents
///      over their children. Two blocks must keep NodeSpacingX apart only if they share a
///      layer or actually overlap in height, so a family can use empty space at a different
///      height.
///
/// Connectors are built by the archived engine's own connector builder, so the canvas draws
/// both engines identically. TimelineEngineSuiteTests reruns the fuzz invariants and the
/// transition pairs against this engine (ADR 004 step 4). Known gaps are listed in
/// docs/architecture-decisions/004-layout-engine-direction.md.
/// </summary>
// FROZEN: TimelineLayoutEngine as cut at v1 (git tag layout-v1), drawn by the picker's
// "Best so far" option. Never edit it by hand: it is replaced whole by
// scripts/cut-layout-version.ps1 -Best. Shared code it calls (FamilyTreeLayoutEngine's connector
// builder and records) is not frozen; change that only through new parameters, so this keeps
// drawing as it did. docs/Testing/layout-versions.md.
public class BestTimelineLayoutEngine : IFamilyTreeLayoutEngine
{
    // The last version cut from this code (docs/Testing/layout-versions.md). Anything changed
    // since is unreleased; the picker shows this engine as "Newest". Set by
    // scripts/cut-layout-version.ps1, never by hand.
    public const string Version = "v1";

    // Same values as FamilyTreeLayoutEngine, so the two can be compared like for like.
    private const int NodeSpacingX = 120;
    private const int SpouseSpacingX = 200;
    private const int PaddingX = 90;
    private const int PaddingY = 10;
    private const int ComponentGapX = 320;
    private const int FocusSize = 80;
    private const int Gen1Size = 70;
    private const int DefaultSize = 60;
    private const double PxPerYear = 6.5;

    private const int GenerationYears = 25;   // assumed parent-to-child gap when inferring a year
    private const int MinParentChildGap = 20; // an inferred year never puts a child closer than this
    private const int LabelHeight = 40;       // name + year drawn under a node
    private const int VerticalMargin = 10;    // extra clearance when deciding two blocks overlap in height

    private const int OrderingIterations = 10;
    private const int RelaxIterations = 60;
    private const int PolishIterations = 150;

    public FamilyTreeLayout ComputeLayout(
        List<PersonDto> people,
        List<CoupleDto> couples,
        Guid? focusPersonId,
        Guid? anchorPersonId = null)
    {
        if (people.Count == 0)
            return FamilyTreeLayout.Empty;

        // Canonical order first, so nothing depends on how the caller sorted (the R5 lesson).
        people = [.. people.OrderBy(p => p.LastName, StringComparer.Ordinal)
                           .ThenBy(p => p.FirstName, StringComparer.Ordinal)
                           .ThenBy(p => p.Id)];
        var g = new Graph(people, couples);

        var layer = AssignLayers(g);
        var years = AssignYears(g, layer);

        var minYear = years.Min() / 10 * 10;
        var maxYear = (years.Max() + 9) / 10 * 10;
        var canvasHeight = (int)((maxYear - minYear + 1) * PxPerYear + PaddingY * 2);

        var components = g.Components();
        var componentOf = new int[g.N];
        for (var c = 0; c < components.Count; c++)
            foreach (var i in components[c]) componentOf[i] = c;

        var y = years.Select(yr => PaddingY + (int)((yr - minYear) * PxPerYear)).ToArray();

        // Depth reported to the canvas: the layer, counted from the top of each family tree.
        var depth = new int[g.N];
        foreach (var comp in components)
        {
            var top = comp.Min(i => layer[i]);
            foreach (var i in comp) depth[i] = layer[i] - top;
        }

        var focus = focusPersonId is { } fid && g.Index.TryGetValue(fid, out var fi) ? fi : -1;
        var size = new int[g.N];
        for (var i = 0; i < g.N; i++)
            size[i] = i == focus ? FocusSize
                : focus >= 0 && componentOf[i] == componentOf[focus] && Math.Abs(layer[i] - layer[focus]) <= 1 ? Gen1Size
                : DefaultSize;

        var side = LineageSides(g, anchorPersonId);

        // Each family tree is solved on its own, then they sit side by side.
        var x = new double[g.N];
        var dividers = new List<ComponentDivider>();
        double cursor = PaddingX;
        double? previousEnd = null;
        foreach (var comp in components)
        {
            var local = PlaceComponent(g, comp, layer, years, y, size, side);
            var minX = comp.Min(i => local[i]);
            if (previousEnd is { } end)
                dividers.Add(new ComponentDivider((end + cursor) / 2, 0, canvasHeight));
            foreach (var i in comp) x[i] = local[i] - minX + cursor;
            previousEnd = comp.Max(i => x[i]);
            cursor = previousEnd.Value + ComponentGapX;
        }

        var nodeMap = new Dictionary<Guid, LayoutNode>();
        for (var i = 0; i < g.N; i++)
            nodeMap[g.People[i].Id] = new LayoutNode(g.People[i], (int)Math.Round(x[i]), y[i], depth[i], i == focus, size[i]);

        var connectors = BuildConnectors(people, g.Couples, nodeMap, clearLabels: true);
        var drawn = connectors.SelectMany(LinesOf).ToList();
        var siblingLinks = RouteSiblingLinks(g, nodeMap, drawn);

        var bandHeight = Math.Max((int)(PxPerYear * 10), 80);
        var bands = new List<GenerationBand>();
        for (var year = minYear; year <= maxYear; year += 10)
            bands.Add(new GenerationBand(year, PaddingY + (int)((year - minYear) * PxPerYear), bandHeight));

        return new FamilyTreeLayout(
            [.. nodeMap.Values],
            bands,
            connectors,
            siblingLinks,
            dividers,
            (int)Math.Ceiling(previousEnd ?? 0) + PaddingX,
            canvasHeight,
            focus >= 0 ? depth[focus] : 0);
    }

    // Sibling links (siblings with no shared parent on the tree) treat people and the lines
    // already drawn as obstacles. Straight lines ran through whoever sat in between (Lillian
    // to her sister Libby through their husband Louis; Louis to David through Libby and
    // Tilly, on top of Louis to Tilly), and on the live tree four such lines around Louis
    // couldn't be told apart (2026-10-24). So the links of one set of siblings (Louis, Tilly,
    // David) share one route: all straight, or all brackets to one bar over them, the shape a
    // family's children hang from. Each set picks the route that lies along no other line
    // first (what sank the first bracket attempt on 2026-10-23: merged lines 646 to 827), then
    // the one through the fewest other people, then the fewest crossings, then the shortest,
    // so siblings with nothing between them keep a straight line. Narrowest sets first, so a
    // wider bar goes over the narrower ones.
    private const int SiblingBarGap = 18;  // the connector builder's span sits this far above a child
    private const int SiblingBarStep = 12; // further bars, each clear of the last (MergeDistance 3)
    private const int SiblingBarTries = 8;

    private static List<SiblingLink> RouteSiblingLinks(Graph g, Dictionary<Guid, LayoutNode> nodeMap, List<Line> drawn)
    {
        var pairs = Enumerable.Range(0, g.N)
            .SelectMany(i => g.Siblings[i].Where(s => s > i && !g.Parents[i].Intersect(g.Parents[s]).Any()).Select(s => (i, s)))
            .ToList();

        // Sets of siblings joined by these links.
        var root = Enumerable.Range(0, g.N).ToArray();
        int Find(int v) => root[v] == v ? v : root[v] = Find(root[v]);
        foreach (var (i, s) in pairs) root[Find(i)] = Find(s);
        LayoutNode Node(int i) => nodeMap[g.People[i].Id];
        var sets = pairs.GroupBy(p => Find(p.i))
            .Select(gr => gr.ToList())
            .OrderBy(set => set.SelectMany(p => new[] { Node(p.i).X, Node(p.s).X }).Max()
                          - set.SelectMany(p => new[] { Node(p.i).X, Node(p.s).X }).Min())
            .ToList();

        var links = new List<SiblingLink>();
        foreach (var set in sets)
        {
            var members = set.SelectMany(p => new[] { p.i, p.s }).Distinct().Select(Node).ToList();
            var memberIds = members.Select(m => m.Person.Id).ToHashSet();
            var others = nodeMap.Values.Where(n => !memberIds.Contains(n.Person.Id)).ToList();
            var top = members.Min(m => m.Y - m.Size / 2.0) - SiblingBarGap;
            var routes = new List<double?> { null }
                .Concat(Enumerable.Range(0, SiblingBarTries).Select(k => (double?)(top - k * SiblingBarStep)));
            // Drawn as a chain left to right (Louis to Tilly, Tilly to David), not the recorded
            // pairs (Louis to Tilly, Louis to David): one bar with one line to each sibling,
            // instead of two lines up from Louis on top of each other.
            var chain = members.OrderBy(m => m.X).ThenBy(m => m.Person.Id).ToList();
            var best = routes
                .Select(bar => chain.Zip(chain.Skip(1), (a, b) => new SiblingLink(a.X, a.Y, b.X, b.Y, bar)).ToList())
                .MinBy(route =>
                {
                    var segs = route.SelectMany(l => l.Segments).ToList();
                    var along = segs.Sum(s => drawn.Count(d => Along(s, d)));
                    var hits = segs.Sum(s => others.Count(n => DistanceToSegment(n.X, n.Y, s) < n.Size / 2.0));
                    var crossings = segs.Sum(s => drawn.Count(d => Cross(s, d)));
                    var length = segs.Sum(s => Math.Abs(s.X2 - s.X1) + Math.Abs(s.Y2 - s.Y1));
                    return (along, hits, crossings, length);
                })!;
            links.AddRange(best);
            drawn.AddRange(best.SelectMany(l => l.Segments));
        }

        // A line crossing a bar would read as hanging from it, so the bar gets a gap there
        // (after every set is routed, so later sets' lines count too). Only lines passing right
        // through: a sibling's own line ends at the bar, and a crossing within a gap of the
        // bar's end isn't given one.
        var upright = drawn.Where(d => Math.Abs(d.X1 - d.X2) < 0.5).ToList();
        return [.. links.Select(l =>
        {
            if (l.BarY is not { } bar) return l;
            var (left, right) = (Math.Min(l.AX, l.BX), Math.Max(l.AX, l.BX));
            var gaps = upright
                .Where(v => v.X1 > left + SiblingLink.JumpGap && v.X1 < right - SiblingLink.JumpGap
                         && Math.Min(v.Y1, v.Y2) < bar - 0.5 && Math.Max(v.Y1, v.Y2) > bar + 0.5)
                .Select(v => v.X1).Distinct().Order().ToList();
            return gaps.Count > 0 ? l with { Gaps = gaps } : l;
        })];
    }

    // Two level (or two upright) lines closer than SiblingLineClearance and sharing more than
    // 2 of their length. LayoutQualityMetrics counts lines within 3 as merged; a router held
    // to that put the sisters' bar 7 from Louis's siblings' bar on the live tree, which still
    // read as a double line (2026-10-24), so the router keeps further apart than the score.
    private const double SiblingLineClearance = 10;

    private static bool Along(Line a, Line b)
    {
        static bool Flat(Line s) => Math.Abs(s.Y1 - s.Y2) < 0.5;
        static bool Upright(Line s) => Math.Abs(s.X1 - s.X2) < 0.5;
        static double Shared(double a1, double a2, double b1, double b2) =>
            Math.Min(Math.Max(a1, a2), Math.Max(b1, b2)) - Math.Max(Math.Min(a1, a2), Math.Min(b1, b2));
        if (Flat(a) && Flat(b) && Math.Abs(a.Y1 - b.Y1) < SiblingLineClearance) return Shared(a.X1, a.X2, b.X1, b.X2) > 2;
        if (Upright(a) && Upright(b) && Math.Abs(a.X1 - b.X1) < SiblingLineClearance) return Shared(a.Y1, a.Y2, b.Y1, b.Y2) > 2;
        return false;
    }

    private static IEnumerable<Line> LinesOf(FamilyUnit f)
    {
        if (f.Couple is { } c)
            foreach (var piece in c.Segments) yield return piece;
        if (f.Stem.TopY < f.Stem.BotY) yield return (f.Stem.X, f.Stem.TopY, f.Stem.X, f.Stem.BotY);
        if (f.Span is { } s) yield return (s.LeftX, s.Y, s.RightX, s.Y);
        foreach (var d in f.Drops) yield return (d.CenterX, f.Stem.BotY, d.CenterX, d.BotY);
    }

    // Strict crossing: each line's ends lie on opposite sides of the other. Lines that only
    // touch or meet at an end (a sibling's link and their own couple line) don't count.
    private static bool Cross(Line a, Line b)
    {
        static double Side(Line l, double x, double y) => (l.X2 - l.X1) * (y - l.Y1) - (l.Y2 - l.Y1) * (x - l.X1);
        const double eps = 1e-6;
        static bool Apart(double p, double q) => (p > eps && q < -eps) || (p < -eps && q > eps);
        return Apart(Side(a, b.X1, b.Y1), Side(a, b.X2, b.Y2)) && Apart(Side(b, a.X1, a.Y1), Side(b, a.X2, a.Y2));
    }

    private static double DistanceToSegment(double px, double py, Line s)
    {
        var (dx, dy) = (s.X2 - s.X1, s.Y2 - s.Y1);
        var len = dx * dx + dy * dy;
        var t = len == 0 ? 0 : Math.Clamp(((px - s.X1) * dx + (py - s.Y1) * dy) / len, 0, 1);
        var (cx, cy) = (s.X1 + t * dx - px, s.Y1 + t * dy - py);
        return Math.Sqrt(cx * cx + cy * cy);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GRAPH
    // ─────────────────────────────────────────────────────────────────────────

    // People as indices 0..N-1 in canonical order, with every relation limited to people
    // actually in the list. Partner clusters: everyone joined by any chain of couples.
    private sealed class Graph
    {
        public readonly List<PersonDto> People;
        public readonly List<CoupleDto> Couples;
        public readonly Dictionary<Guid, int> Index = [];
        public readonly int N;
        public readonly List<int>[] Parents, Children, Partners, Siblings;
        public readonly int[] Cluster;

        public Graph(List<PersonDto> people, List<CoupleDto> couples)
        {
            People = people;
            N = people.Count;
            for (var i = 0; i < N; i++) Index[people[i].Id] = i;

            List<int> Ids(IEnumerable<Guid>? ids, int self) =>
                [.. (ids ?? []).Where(Index.ContainsKey).Select(id => Index[id]).Where(j => j != self).Distinct().Order()];

            Parents = new List<int>[N];
            Siblings = new List<int>[N];
            Children = new List<int>[N];
            Partners = new List<int>[N];
            for (var i = 0; i < N; i++)
            {
                Parents[i] = Ids(people[i].ParentIds, i);
                Siblings[i] = Ids(people[i].SiblingIds, i);
                Children[i] = [];
                Partners[i] = [];
            }
            for (var i = 0; i < N; i++)
                foreach (var p in Parents[i]) Children[p].Add(i);

            Couples = [.. couples
                .Where(c => c.PersonAId != c.PersonBId && Index.ContainsKey(c.PersonAId) && Index.ContainsKey(c.PersonBId))
                .OrderBy(c => Index[c.PersonAId]).ThenBy(c => Index[c.PersonBId])];
            foreach (var c in Couples)
            {
                var (a, b) = (Index[c.PersonAId], Index[c.PersonBId]);
                if (!Partners[a].Contains(b)) Partners[a].Add(b);
                if (!Partners[b].Contains(a)) Partners[b].Add(a);
            }
            foreach (var list in Partners) list.Sort();
            // Sibling links are symmetric in the mapper, but don't rely on it.
            for (var i = 0; i < N; i++)
                foreach (var s in Siblings[i].ToList())
                    if (!Siblings[s].Contains(i)) { Siblings[s].Add(i); Siblings[s].Sort(); }

            var root = Enumerable.Range(0, N).ToArray();
            int Find(int i) { while (root[i] != i) i = root[i] = root[root[i]]; return i; }
            for (var i = 0; i < N; i++)
                foreach (var p in Partners[i])
                {
                    var (ra, rb) = (Find(i), Find(p));
                    if (ra != rb) root[Math.Max(ra, rb)] = Math.Min(ra, rb);
                }
            Cluster = [.. Enumerable.Range(0, N).Select(Find)];
        }

        // Everyone connected by any relation, each list in canonical order, ordered by first member.
        public List<List<int>> Components()
        {
            var seen = new bool[N];
            var result = new List<List<int>>();
            for (var s = 0; s < N; s++)
            {
                if (seen[s]) continue;
                var comp = new List<int>();
                var queue = new Queue<int>([s]);
                seen[s] = true;
                while (queue.Count > 0)
                {
                    var i = queue.Dequeue();
                    comp.Add(i);
                    foreach (var j in Parents[i].Concat(Children[i]).Concat(Partners[i]).Concat(Siblings[i]))
                        if (!seen[j]) { seen[j] = true; queue.Enqueue(j); }
                }
                comp.Sort();
                result.Add(comp);
            }
            return result;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LAYERS
    // ─────────────────────────────────────────────────────────────────────────

    // Longest path from the top over partner clusters, then every cluster with children is
    // pulled down to just above its highest child (so parentless in-laws float to their
    // children's level instead of sitting at the top of the tree). Pulling down only ever
    // moves a cluster away from its own parents, so no rule breaks. A cycle (bad data, or
    // someone married to their own descendant) is broken by dropping the edges into one
    // cluster, chosen by canonical order so the result is deterministic.
    private static int[] AssignLayers(Graph g)
    {
        var clusters = g.Cluster.Distinct().Order().ToList();
        var ci = clusters.Select((c, k) => (c, k)).ToDictionary(t => t.c, t => t.k);
        var cl = g.Cluster.Select(c => ci[c]).ToArray();
        var c = clusters.Count;

        var outE = Enumerable.Range(0, c).Select(_ => new SortedSet<int>()).ToArray();
        var inE = Enumerable.Range(0, c).Select(_ => new SortedSet<int>()).ToArray();
        for (var i = 0; i < g.N; i++)
            foreach (var p in g.Parents[i])
                if (cl[p] != cl[i]) { outE[cl[p]].Add(cl[i]); inE[cl[i]].Add(cl[p]); }

        var indeg = inE.Select(s => s.Count).ToArray();
        var done = new bool[c];
        var dropped = new HashSet<(int, int)>();
        var ready = new SortedSet<int>(Enumerable.Range(0, c).Where(k => indeg[k] == 0));
        var order = new List<int>();
        while (order.Count < c)
        {
            if (ready.Count == 0)
            {
                var forced = Enumerable.Range(0, c).First(k => !done[k]);
                foreach (var u in inE[forced].Where(u => !done[u])) dropped.Add((u, forced));
                indeg[forced] = 0;
                ready.Add(forced);
            }
            var k0 = ready.Min;
            ready.Remove(k0);
            done[k0] = true;
            order.Add(k0);
            foreach (var o in outE[k0])
                if (!done[o] && !dropped.Contains((k0, o)) && --indeg[o] == 0) ready.Add(o);
        }

        bool Kept(int u, int v) => !dropped.Contains((u, v));
        var level = new int[c];
        foreach (var k in order)
            foreach (var o in outE[k].Where(o => Kept(k, o)))
                level[o] = Math.Max(level[o], level[k] + 1);
        // Childless clusters don't hold their parents up: Bud's son Elliot has no children,
        // and counting him would pin Bud to the top row although his other son Marc is three
        // generations down. They are placed afterwards, one layer below their parents.
        bool Leaf(int k) => !outE[k].Any(o => Kept(k, o));
        for (var idx = order.Count - 1; idx >= 0; idx--)
        {
            var k = order[idx];
            var kids = outE[k].Where(o => Kept(k, o) && !Leaf(o)).ToList();
            if (kids.Count > 0) level[k] = Math.Max(level[k], kids.Min(o => level[o]) - 1);
        }
        void PlaceLeaves()
        {
            foreach (var k in order.Where(Leaf))
            {
                var parents = inE[k].Where(u => Kept(u, k)).ToList();
                if (parents.Count > 0) level[k] = parents.Max(u => level[u]) + 1;
            }
        }

        // A family with no recorded parents but a Sibling link (an orphan sibling, like Bill
        // beside his brother Ray) takes their sibling's layer, as far as its own children
        // allow, so ordering can put the two side by side. Childless children don't limit it
        // (they're placed below their parents afterwards), so an orphan sibling with a
        // childless daughter still joins their sibling's layer.
        var siblingClusters = Enumerable.Range(0, c).Select(_ => new SortedSet<int>()).ToArray();
        for (var i = 0; i < g.N; i++)
            foreach (var s in g.Siblings[i].Where(s => cl[s] != cl[i])) siblingClusters[cl[i]].Add(cl[s]);
        for (var round = 0; round < 2; round++)
        {
            PlaceLeaves();
            foreach (var k in order.Where(k => siblingClusters[k].Count > 0 && !inE[k].Any(u => Kept(u, k))))
            {
                var target = siblingClusters[k].Max(s => level[s]);
                var kids = outE[k].Where(o => Kept(k, o) && !Leaf(o)).ToList();
                if (kids.Count > 0) target = Math.Min(target, kids.Min(o => level[o]) - 1);
                level[k] = target;
            }
        }
        PlaceLeaves();

        return [.. Enumerable.Range(0, g.N).Select(i => level[cl[i]])];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // YEARS
    // ─────────────────────────────────────────────────────────────────────────

    // Recorded birth years are never changed. A dateless person takes the average of what
    // their dated (or already inferred) relatives suggest: a partner's year, a sibling's
    // year, a parent's year plus GenerationYears per layer, a child's minus. Repeated until
    // nothing new can be inferred. A family tree with no dates at all is placed by layer from
    // the dated trees' typical year for that layer (or from today). Last, an inferred year
    // is nudged so no child is closer than MinParentChildGap to a parent.
    private static int[] AssignYears(Graph g, int[] layer)
    {
        var known = g.People.Select(p => p.BirthDate is not null).ToArray();
        var year = g.People.Select(p => p.BirthDate is { } d ? (double?)d.Year : null).ToArray();

        bool progress;
        do
        {
            progress = false;
            var next = (double?[])year.Clone();
            for (var i = 0; i < g.N; i++)
            {
                if (year[i] is not null) continue;
                double sum = 0, weight = 0;
                void Add(double v, double w) { sum += v * w; weight += w; }
                foreach (var p in g.Partners[i]) if (year[p] is { } v) Add(v, 2);
                foreach (var p in g.Parents[i]) if (year[p] is { } v) Add(v + GenerationYears * Math.Max(1, layer[i] - layer[p]), 1);
                foreach (var ch in g.Children[i]) if (year[ch] is { } v) Add(v - GenerationYears * Math.Max(1, layer[ch] - layer[i]), 1);
                foreach (var s in g.Siblings[i].Concat(g.Parents[i].SelectMany(p => g.Children[p])).Where(s => s != i).Distinct())
                    if (year[s] is { } v) Add(v, 1);
                if (weight > 0) { next[i] = sum / weight; progress = true; }
            }
            year = next;
        } while (progress);

        if (year.Any(v => v is null))
        {
            var dated = Enumerable.Range(0, g.N).Where(i => known[i]).Select(i => year[i]!.Value - GenerationYears * layer[i]).Order().ToList();
            var offset = dated.Count > 0
                ? dated[dated.Count / 2]
                : DateTime.Today.Year - 20 - GenerationYears * layer.Max();
            // Even, so when a dateless tree shifts with the others' dates it moves a whole
            // number of pixels (2 years = 13px) and nobody in it moves a pixel more than the rest.
            offset = Math.Floor(offset / 2) * 2;
            for (var i = 0; i < g.N; i++) year[i] ??= offset + GenerationYears * layer[i];
        }

        // The dateless members of one partner cluster share a year (the invariant that dateless
        // couples share a row), and every dateless group is then held inside hard bounds: at
        // least MinParentChildGap after every parent and before every child, following chains
        // of dateless relatives to the nearest recorded year. Nudging one edge at a time
        // instead oscillated between a dateless person's parent and their dated child.
        // A cluster that is its own ancestor through its children (a parent and their own child
        // who are co-parents, or a partner who is also a grandchild of the other partner) can't
        // share a year: everyone must stay below their parents, so its dateless members are
        // bounded one by one instead.
        var clusterKids = new Dictionary<int, HashSet<int>>();
        for (var i = 0; i < g.N; i++)
            foreach (var p in g.Parents[i])
                (clusterKids.TryGetValue(g.Cluster[p], out var set) ? set : clusterKids[g.Cluster[p]] = []).Add(g.Cluster[i]);
        bool OnCycle(int k)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>(clusterKids.GetValueOrDefault(k) ?? []);
            while (stack.Count > 0)
            {
                var x = stack.Pop();
                if (x == k) return true;
                if (seen.Add(x)) foreach (var y in clusterKids.GetValueOrDefault(x) ?? []) stack.Push(y);
            }
            return false;
        }
        var cyclic = g.Cluster.Distinct().Where(OnCycle).ToHashSet();
        var unit = new int[g.N];
        for (var i = 0; i < g.N; i++)
            unit[i] = known[i] ? -1 : cyclic.Contains(g.Cluster[i]) ? g.N + i : g.Cluster[i];
        var units = Enumerable.Range(0, g.N).Where(i => !known[i]).GroupBy(i => unit[i])
            .ToDictionary(gr => gr.Key, gr => gr.ToList());
        var lo = units.Keys.ToDictionary(u => u, _ => double.NegativeInfinity);
        var hi = units.Keys.ToDictionary(u => u, _ => double.PositiveInfinity);
        double Lo(int i) => known[i] ? year[i]!.Value : lo[unit[i]];
        double Hi(int i) => known[i] ? year[i]!.Value : hi[unit[i]];
        for (var pass = 0; pass < g.N + 1; pass++)
        {
            var changed = false;
            foreach (var (u, members) in units)
            {
                var l = members.SelectMany(m => g.Parents[m]).Where(p => unit[p] != u)
                    .Select(p => Lo(p) + MinParentChildGap).DefaultIfEmpty(double.NegativeInfinity).Max();
                var h = members.SelectMany(m => g.Children[m]).Where(c => unit[c] != u)
                    .Select(c => Hi(c) - MinParentChildGap).DefaultIfEmpty(double.PositiveInfinity).Min();
                if (l > lo[u]) { lo[u] = l; changed = true; }
                if (h < hi[u]) { hi[u] = h; changed = true; }
            }
            if (!changed) break; // a parent cycle (bad data) never settles; the cap ends it
        }

        var result = year.Select(v => (int)Math.Round(v!.Value)).ToArray();
        foreach (var (u, members) in units)
        {
            var v = members.Average(m => year[m]!.Value);
            v = lo[u] <= hi[u] ? Math.Clamp(v, lo[u], hi[u])
                : double.IsFinite(hi[u]) ? hi[u] : lo[u]; // contradictory recorded dates: stay above the children
            foreach (var m in members) result[m] = (int)Math.Round(v);
        }

        // Dateless relatives with no recorded year anywhere along the chain have no bounds
        // above; this keeps them in generation order too (a parent and child who are also
        // co-parents share a layer, so their inferred years can tie).
        void Set(int i, int v) { foreach (var m in units[unit[i]]) result[m] = v; }
        for (var pass = 0; pass < 20; pass++)
        {
            var moved = false;
            for (var ch = 0; ch < g.N; ch++)
                foreach (var p in g.Parents[ch])
                {
                    if (result[ch] - result[p] >= MinParentChildGap || unit[ch] == unit[p] && unit[ch] >= 0) continue;
                    if (!known[ch] && result[p] + MinParentChildGap <= hi[unit[ch]]) { Set(ch, result[p] + MinParentChildGap); moved = true; }
                    else if (!known[p]) { Set(p, result[ch] - MinParentChildGap); moved = true; }
                }
            if (!moved) break;
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ONE FAMILY TREE: blocks, order, then X
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class Block(List<int> members, int layer)
    {
        public List<int> Members = members;
        public readonly int Layer = layer;
        public double Width => (Members.Count - 1) * SpouseSpacingX;
        public double Top, Bottom; // vertical extent incl. labels, Bottom also its line zone
        public double NodeBottom;  // vertical extent of the people alone
    }

    private sealed record Family(List<int> Parents, List<int> Children);

    private static double[] PlaceComponent(Graph g, List<int> comp, int[] layer, int[] years, int[] y, int[] size, int[] side)
    {
        var inComp = new HashSet<int>(comp);

        var blocks = comp.GroupBy(i => g.Cluster[i]).OrderBy(gr => gr.Min())
            .Select(gr => new Block(BlockOrder([.. gr], g), layer[gr.First()]))
            .ToList();
        var blockOf = new Dictionary<int, int>();
        for (var b = 0; b < blocks.Count; b++)
            foreach (var m in blocks[b].Members) blockOf[m] = b;
        foreach (var bl in blocks)
        {
            bl.Top = bl.Members.Min(m => y[m] - size[m] / 2.0) - VerticalMargin / 2.0;
            bl.Bottom = bl.NodeBottom = bl.Members.Max(m => y[m] + size[m] / 2.0 + LabelHeight) + VerticalMargin / 2.0;
        }

        var families = comp.Where(i => g.Parents[i].Count > 0)
            .GroupBy(i => string.Join(",", g.Parents[i]))
            .Select(gr => new Family(g.Parents[gr.First()], [.. gr]))
            .ToList();

        // A family's line runs from its parents straight down to just above the highest child
        // (the connector builder's span sits 18 above the child's circle). The parents' block
        // counts as reaching down that far when deciding which blocks overlap in height, so
        // nobody from another family is packed into the path of that line.
        foreach (var f in families)
        {
            var spanY = f.Children.Min(c => y[c] - size[c] / 2.0) - 18;
            foreach (var b in f.Parents.Where(inComp.Contains).Select(p => blockOf[p]).Distinct())
                blocks[b].Bottom = Math.Max(blocks[b].Bottom, spanY);
        }

        var rows = OrderLayers(g, comp, blocks, blockOf, years, side);
        return SolveX(g, blocks, blockOf, rows, families);
    }

    // Partners side by side. With more than two (second marriages), the person with the most
    // partners goes in the middle, their partners alternating right and left, anyone else
    // beside their own partner on the outside, so a chain of marriages lays out as a chain.
    // A person with three or more partners can't have them all beside them.
    private static List<int> BlockOrder(List<int> members, Graph g)
    {
        members.Sort();
        if (members.Count <= 2) return members;
        var set = new HashSet<int>(members);
        var hub = members.OrderByDescending(m => g.Partners[m].Count(set.Contains)).ThenBy(m => m).First();
        var order = new List<int> { hub };
        var k = 0;
        foreach (var p in g.Partners[hub].Where(set.Contains))
            if (k++ % 2 == 0) order.Add(p); else order.Insert(0, p);
        while (order.Count < members.Count)
        {
            var m = members.FirstOrDefault(m => !order.Contains(m) && g.Partners[m].Any(order.Contains), -1);
            if (m < 0) { order.AddRange(members.Where(x => !order.Contains(x))); break; }
            var at = order.IndexOf(g.Partners[m].First(order.Contains));
            if (at >= order.Count / 2.0) order.Add(m); else order.Insert(0, m);
        }
        return order;
    }

    // Left-to-right order of blocks in each layer. Start from a depth-first walk down from
    // the oldest families (keeps each family's descendants together), then alternate
    // barycentre sweeps down and up, turning couples so each partner faces their own parents,
    // and keep whichever order had the fewest crossings.
    private static Dictionary<int, List<int>> OrderLayers(Graph g, List<int> comp, List<Block> blocks, Dictionary<int, int> blockOf, int[] years, int[] side)
    {
        var inComp = new HashSet<int>(comp);
        var rows = blocks.Select(b => b.Layer).Distinct().ToDictionary(l => l, _ => new List<int>());
        var layers = rows.Keys.Order().ToList();

        var childBlocks = blocks.Select(bl => bl.Members.SelectMany(m => g.Children[m]).Where(inComp.Contains)
            .OrderBy(ch => years[ch]).ThenBy(ch => ch).Select(ch => blockOf[ch]).Distinct().ToList()).ToList();
        var hasParent = blocks.Select(bl => bl.Members.Any(m => g.Parents[m].Any(inComp.Contains))).ToList();

        // An orphan sibling (no parents on the tree, only a Sibling link) follows the sibling
        // with the stronger place in the tree: one with parents, else one with children, else
        // the first in canonical order. Followers sit right after their leader in every order
        // tried, so Bill marrying can't land his couple on the far side of Ray's family.
        var sibBlocks = blocks.Select((bl, b) => bl.Members.SelectMany(m => g.Siblings[m]).Where(inComp.Contains)
            .Select(s => blockOf[s]).Where(sb => sb != b).Distinct().Order().ToList()).ToList();
        (int, int, int) Strength(int b) => (hasParent[b] ? 1 : 0, childBlocks[b].Count > 0 ? 1 : 0, -b);
        var leader = Enumerable.Range(0, blocks.Count).Select(b => hasParent[b] ? -1
            : sibBlocks[b].Where(s => Strength(s).CompareTo(Strength(b)) > 0)
                .OrderByDescending(Strength).Select(s => (int?)s).FirstOrDefault() ?? -1).ToArray();
        var followers = Enumerable.Range(0, blocks.Count).Select(b => Enumerable.Range(0, blocks.Count)
            .Where(f => leader[f] == b).ToList()).ToList();

        var visited = new HashSet<int>();
        void Visit(int b)
        {
            if (!visited.Add(b)) return;
            rows[blocks[b].Layer].Add(b);
            foreach (var f in followers[b]) Visit(f);
            foreach (var cb in childBlocks[b]) Visit(cb);
        }
        for (var b = 0; b < blocks.Count; b++) if (!hasParent[b] && leader[b] < 0) Visit(b);
        for (var b = 0; b < blocks.Count; b++) Visit(b);

        var rowIndex = new int[blocks.Count];
        void Reindex() { foreach (var row in rows.Values) for (var k = 0; k < row.Count; k++) rowIndex[row[k]] = k; }

        // Puts each follower back beside its leader when the two share a layer: on the side
        // of the leader's block where their sibling sits (Morton beside his brother Ray, not
        // past Ray's wife Rose), else on the right.
        // A sibling in the middle of a block (Louis between his two wives) has no free side;
        // the follower takes the quieter end, the one whose descendants (and their partners)
        // reach less of the tree, so his sister doesn't land between Lillian and her son Ray's line.
        var reach = new Dictionary<int, int>();
        int Reach(int m)
        {
            if (reach.TryGetValue(m, out var r)) return r;
            var seen = new HashSet<int>();
            var stack = new Stack<int>(g.Children[m].Where(inComp.Contains));
            while (stack.Count > 0)
            {
                var x = stack.Pop();
                if (!seen.Add(x)) continue;
                foreach (var p in g.Partners[x].Where(inComp.Contains)) seen.Add(p);
                foreach (var ch in g.Children[x].Where(inComp.Contains)) stack.Push(ch);
            }
            return reach[m] = seen.Count;
        }
        bool OnLeft(int f)
        {
            var mem = blocks[leader[f]].Members;
            var sib = mem.FindIndex(m => blocks[f].Members.Any(fm => g.Siblings[fm].Contains(m)));
            if (sib < 0) return false;
            if (mem.Count > 2 && sib * 2 == mem.Count - 1) return Reach(mem[0]) < Reach(mem[^1]);
            return sib < (mem.Count - 1) / 2.0;
        }
        List<int> Glue(List<int> row)
        {
            var inRow = row.ToHashSet();
            bool Glued(int b) => leader[b] >= 0 && inRow.Contains(leader[b]);
            var result = new List<int>();
            void Put(int b)
            {
                var mine = followers[b].Where(inRow.Contains).ToList();
                foreach (var f in Enumerable.Reverse(mine).Where(OnLeft)) Put(f);
                result.Add(b);
                foreach (var f in mine.Where(f => !OnLeft(f))) Put(f);
            }
            foreach (var b in row.Where(b => !Glued(b))) Put(b);
            return result;
        }
        void GlueAll()
        {
            foreach (var l in layers) rows[l] = Glue(rows[l]);
            Reindex();
        }
        GlueAll();

        double Pos(int b) => (rowIndex[b] + 0.5) / rows[blocks[b].Layer].Count;
        double MPos(int m)
        {
            var bl = blocks[blockOf[m]];
            return Pos(blockOf[m]) + (bl.Members.IndexOf(m) - (bl.Members.Count - 1) / 2.0) * 0.001;
        }
        double Bary(int b, bool up)
        {
            var rel = blocks[b].Members.SelectMany(m => up ? g.Parents[m] : g.Children[m]).Where(inComp.Contains).ToList();
            return rel.Count > 0 ? rel.Average(MPos) : Pos(b);
        }

        void Orient()
        {
            for (var b = 0; b < blocks.Count; b++)
            {
                var mem = blocks[b].Members;
                if (mem.Count < 2) continue;
                double score = 0;
                for (var k = 0; k < mem.Count; k++)
                {
                    var m = mem[k];
                    var half = k - (mem.Count - 1) / 2.0;
                    var ps = g.Parents[m].Where(inComp.Contains).ToList();
                    if (ps.Count > 0) score += (ps.Average(MPos) - Pos(b)) * half;
                    // Each child pulls their own parents' end of the block towards them: Ray, son
                    // of Louis + Lillian in [Libby, Louis, Lillian], turns the block so Lillian
                    // faces Ray. A child of the whole couple pulls neither way.
                    foreach (var ch in g.Children[m].Where(inComp.Contains))
                    {
                        var inBlock = g.Parents[ch].Where(mem.Contains).ToList();
                        var centre = inBlock.Average(p => mem.IndexOf(p) - (mem.Count - 1) / 2.0);
                        score += (MPos(ch) - Pos(b)) * centre / inBlock.Count;
                    }
                    // A partner with an orphan sibling following this block faces right, so the
                    // sibling (and later their own marriage) grows outward instead of pushing
                    // the couple over. Small, so parents and children decide first.
                    if (followers[b].Any(f => blocks[f].Members.Any(fm => g.Siblings[fm].Contains(m)))) score += 1e-6 * half;
                }
                if (score < -1e-9) mem.Reverse();

                // A partner with no other relatives here who can't sit beside their spouse
                // anyway (Pat's third wife Casey, past Alex) goes on the right end, so turning
                // the block for the children never moves them to the left and pushes the
                // whole tree over.
                if (mem.Count > 2)
                {
                    bool Loose(int m) => !g.Parents[m].Concat(g.Children[m]).Concat(g.Siblings[m]).Any(inComp.Contains);
                    bool BesidePartner(int k) =>
                        (k > 0 && g.Partners[mem[k]].Contains(mem[k - 1])) || (k + 1 < mem.Count && g.Partners[mem[k]].Contains(mem[k + 1]));
                    var moving = mem.Where((m, k) => k < mem.Count - 1 && Loose(m) && !BesidePartner(k)).ToList();
                    foreach (var m in moving) { mem.Remove(m); mem.Add(m); }
                }
            }
        }

        var edges = comp.SelectMany(ch => g.Parents[ch].Where(inComp.Contains).Select(p => (P: p, C: ch))).ToList();
        int Crossings()
        {
            var count = 0;
            for (var a = 0; a < edges.Count; a++)
                for (var b = a + 1; b < edges.Count; b++)
                {
                    var (e, f) = (edges[a], edges[b]);
                    if (e.P == f.P || e.C == f.C) continue;
                    if (blocks[blockOf[e.P]].Layer != blocks[blockOf[f.P]].Layer ||
                        blocks[blockOf[e.C]].Layer != blocks[blockOf[f.C]].Layer) continue;
                    if ((MPos(e.P) - MPos(f.P)) * (MPos(e.C) - MPos(f.C)) < 0) count++;
                }
            return count;
        }

        Orient();
        GlueAll(); // a couple that turned moves its follower to the other side; score what's drawn
        var best = (Score: Crossings(), Rows: rows.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
                    Members: blocks.Select(bl => bl.Members.ToList()).ToList());
        void Keep()
        {
            var score = Crossings();
            if (score < best.Score)
                best = (score, rows.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()), blocks.Select(bl => bl.Members.ToList()).ToList());
        }

        for (var it = 0; it < OrderingIterations && best.Score > 0; it++)
        {
            foreach (var l in layers.Skip(1))
            {
                rows[l] = Glue([.. rows[l].OrderBy(b => Bary(b, up: true)).ThenBy(b => rowIndex[b])]);
                Reindex();
            }
            Orient();
            GlueAll();
            Keep();
            foreach (var l in Enumerable.Reverse(layers).Skip(1))
            {
                rows[l] = Glue([.. rows[l].OrderBy(b => Bary(b, up: false)).ThenBy(b => rowIndex[b])]);
                Reindex();
            }
            Orient();
            GlueAll();
            Keep();
        }

        // A couple with an orphan sibling glued to it decides which side the sibling takes, and
        // nothing above turns it against Orient: a partner's lone parent always counts as the
        // middle of their row, so Rebecca faced inward and her sister landed between two
        // families, the sister's sons past Ray with her line drawn along his (found live
        // 2026-10-24). Try each such couple the other way round, re-sort the rows by their
        // parents, and keep it if that crosses less. Only these blocks: counting crossings is
        // quadratic in the number of lines.
        void Restore()
        {
            for (var b = 0; b < blocks.Count; b++) blocks[b].Members = [.. best.Members[b]];
            rows = best.Rows.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
            Reindex();
        }
        var turnable = Enumerable.Range(0, blocks.Count)
            .Where(b => blocks[b].Members.Count >= 2 && followers[b].Count > 0).ToList();
        foreach (var b in turnable.Where(_ => best.Score > 0))
        {
            Restore();
            blocks[b].Members.Reverse();
            GlueAll();
            foreach (var l in layers.Skip(1))
            {
                rows[l] = Glue([.. rows[l].OrderBy(x => Bary(x, up: true)).ThenBy(x => rowIndex[x])]);
                Reindex();
            }
            Keep();
        }

        for (var b = 0; b < blocks.Count; b++) blocks[b].Members = best.Members[b];
        rows = best.Rows;

        // The anchor's first parent's side goes on the left, as in the archived engine
        // (Doug's mother's family left, his father's right). Mirroring keeps every crossing.
        Reindex();
        var lean = comp.Where(m => side[m] != 0).Sum(m => side[m] * (MPos(m) - 0.5));
        if (lean < -1e-9)
        {
            foreach (var row in rows.Values) row.Reverse();
            foreach (var bl in blocks) bl.Members.Reverse();
        }
        return rows;
    }

    // Which side of the anchor's family each person belongs to: -1 nearer the anchor's first
    // parent (canonical order), +1 nearer the second, 0 for a tie, no anchor, or an anchor
    // with fewer than two parents. Hop distances over every relation, never through the
    // anchor or the two parents' own partnership, as in the archived engine's
    // ComputeLineageSideDistances.
    private static int[] LineageSides(Graph g, Guid? anchorPersonId)
    {
        var side = new int[g.N];
        if (anchorPersonId is not { } aid || !g.Index.TryGetValue(aid, out var anchor) || g.Parents[anchor].Count < 2)
            return side;
        var (p1, p2) = (g.Parents[anchor][0], g.Parents[anchor][1]);
        int[] Hops(int from)
        {
            var dist = Enumerable.Repeat(int.MaxValue, g.N).ToArray();
            dist[from] = 0;
            var queue = new Queue<int>([from]);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                foreach (var j in g.Parents[i].Concat(g.Children[i]).Concat(g.Partners[i]).Concat(g.Siblings[i]))
                {
                    if (j == anchor || dist[j] != int.MaxValue) continue;
                    if ((i == p1 && j == p2) || (i == p2 && j == p1)) continue;
                    dist[j] = dist[i] + 1;
                    queue.Enqueue(j);
                }
            }
            return dist;
        }
        var (d1, d2) = (Hops(p1), Hops(p2));
        for (var i = 0; i < g.N; i++) side[i] = d1[i] < d2[i] ? -1 : d2[i] < d1[i] ? 1 : 0;
        return side;
    }

    // Block left edges (bx) solved for: each child as close as possible to under the centre
    // of its parents, each set of parents over the centre of their children, subject to
    // "the block on the right starts at least (left block's width + NodeSpacingX) further
    // right". That constraint holds between neighbours in a layer and, in the second phase,
    // between any two blocks in different layers that overlap in height (their order taken
    // from the first phase). Blocks in different layers that don't overlap in height are
    // free to share the same X range: that is the packing the archived engine couldn't do.
    //
    // Each phase: relax towards the targets with constraints projected after each step,
    // then a forward pass in constraint order that makes every constraint hold, then
    // coordinate descent that only moves a block within the room its neighbours leave, so
    // the result always satisfies every constraint.
    private static double[] SolveX(Graph g, List<Block> blocks, Dictionary<int, int> blockOf, Dictionary<int, List<int>> rows, List<Family> families)
    {
        var nb = blocks.Count;
        var off = new Dictionary<int, double>();
        foreach (var bl in blocks)
            for (var k = 0; k < bl.Members.Count; k++) off[bl.Members[k]] = k * SpouseSpacingX;

        var bx = new double[nb];
        foreach (var row in rows.Values)
        {
            double cur = 0;
            foreach (var b in row) { bx[b] = cur; cur += blocks[b].Width + NodeSpacingX; }
            var shift = (cur - NodeSpacingX) / 2;
            foreach (var b in row) bx[b] -= shift;
        }
        double X(int m) => bx[blockOf[m]] + off[m];

        var asChild = Enumerable.Range(0, nb).Select(_ => new List<(Family F, int Child)>()).ToArray();
        var asParent = Enumerable.Range(0, nb).Select(_ => new List<Family>()).ToArray();
        foreach (var f in families)
        {
            foreach (var ch in f.Children) asChild[blockOf[ch]].Add((f, ch));
            foreach (var b in f.Parents.Select(p => blockOf[p]).Distinct()) asParent[b].Add(f);
        }
        // Only siblings with no shared parent on the tree pull on each other: children of the
        // same parents are already held together under them, and pulling half-siblings
        // together moved a first marriage's child when a second marriage's child arrived.
        var siblingPairs = Enumerable.Range(0, nb).Select(b => blocks[b].Members
            .SelectMany(m => g.Siblings[m].Where(blockOf.ContainsKey).Select(s => (Me: m, Sib: s)))
            .Where(t => blockOf[t.Sib] != b && !g.Parents[t.Me].Intersect(g.Parents[t.Sib]).Any()).ToList()).ToArray();

        double Target(int b)
        {
            double sum = 0, weight = 0;
            foreach (var (f, ch) in asChild[b])
            {
                if (f.Parents.Any(p => blockOf[p] == b)) continue;
                sum += f.Parents.Average(X) - off[ch];
                weight += 1;
            }
            foreach (var f in asParent[b])
            {
                if (f.Children.Any(ch => blockOf[ch] == b)) continue;
                var share = f.Parents.Count(p => blockOf[p] == b) / (double)f.Parents.Count;
                var shiftNeeded = (f.Children.Average(X) - f.Parents.Average(X)) / share;
                sum += (bx[b] + shiftNeeded) * f.Children.Count;
                weight += f.Children.Count;
            }
            foreach (var (me, sib) in siblingPairs[b])
            {
                sum += 0.3 * (X(sib) - off[me]);
                weight += 0.3;
            }
            return weight > 0 ? sum / weight : bx[b];
        }

        var cons = new List<(int L, int R, double D)>();
        foreach (var row in rows.Values)
            for (var k = 0; k + 1 < row.Count; k++)
                cons.Add((row[k], row[k + 1], blocks[row[k]].Width + NodeSpacingX));

        void Project()
        {
            foreach (var (l, r, d) in cons)
            {
                var v = bx[l] + d - bx[r];
                if (v > 0) { bx[l] -= v / 2; bx[r] += v / 2; }
            }
        }

        void Relax()
        {
            for (var it = 0; it < RelaxIterations; it++)
            {
                var t = Enumerable.Range(0, nb).Select(Target).ToArray();
                for (var b = 0; b < nb; b++) bx[b] += 0.5 * (t[b] - bx[b]);
                for (var p = 0; p < 4; p++) Project();
            }
        }

        // Every constraint L→R has L before R in `order`, so one pass makes them all hold.
        void Enforce(IEnumerable<int> order)
        {
            var preds = Enumerable.Range(0, nb).Select(_ => new List<(int, double)>()).ToArray();
            foreach (var (l, r, d) in cons) preds[r].Add((l, d));
            foreach (var b in order)
                foreach (var (l, d) in preds[b])
                    bx[b] = Math.Max(bx[b], bx[l] + d);
        }

        void Polish()
        {
            var preds = Enumerable.Range(0, nb).Select(_ => new List<(int, double)>()).ToArray();
            var succs = Enumerable.Range(0, nb).Select(_ => new List<(int, double)>()).ToArray();
            foreach (var (l, r, d) in cons) { preds[r].Add((l, d)); succs[l].Add((r, d)); }
            for (var it = 0; it < PolishIterations; it++)
                for (var b = 0; b < nb; b++)
                {
                    var lo = preds[b].Count > 0 ? preds[b].Max(p => bx[p.Item1] + p.Item2) : double.NegativeInfinity;
                    var hi = succs[b].Count > 0 ? succs[b].Min(s => bx[s.Item1] - s.Item2) : double.PositiveInfinity;
                    if (lo <= hi) bx[b] = Math.Clamp(Target(b), lo, hi);
                }
        }

        // Phase 1: layers only, like ordinary layered drawing.
        Relax();
        Enforce(rows.Keys.Order().SelectMany(l => rows[l]));
        Polish();

        // Phase 2: blocks in different layers that overlap in height must not collide either.
        var parentOf = families.SelectMany(f => f.Parents.Where(blockOf.ContainsKey).Select(p => blockOf[p]).Distinct()
            .SelectMany(pb => f.Children.Select(ch => (pb, blockOf[ch])))).ToHashSet();
        var center = Enumerable.Range(0, nb).Select(b => bx[b] + blocks[b].Width / 2).ToArray();
        for (var i = 0; i < nb; i++)
            for (var j = i + 1; j < nb; j++)
            {
                if (blocks[i].Layer == blocks[j].Layer) continue;
                // A block's line zone (see PlaceComponent) is only kept clear of other
                // families: its own children, from any of its marriages, hang from it, so
                // between parent and child only the people themselves must not collide.
                var related = parentOf.Contains((i, j)) || parentOf.Contains((j, i));
                var (bi, bj) = related ? (blocks[i].NodeBottom, blocks[j].NodeBottom) : (blocks[i].Bottom, blocks[j].Bottom);
                if (bi <= blocks[j].Top || bj <= blocks[i].Top) continue;
                var (l, r) = center[i] < center[j] || (center[i] == center[j] && i < j) ? (i, j) : (j, i);
                cons.Add((l, r, blocks[l].Width + NodeSpacingX));
            }
        Enforce(Enumerable.Range(0, nb).OrderBy(b => center[b]).ThenBy(b => b));
        Polish();

        var x = new double[g.N];
        foreach (var bl in blocks)
            foreach (var m in bl.Members) x[m] = X(m);
        return x;
    }
}
