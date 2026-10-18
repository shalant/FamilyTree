using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;
using static FamilyTree.Web.Services.FamilyTreeLayoutEngine;

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
///      sweeps, keeping the best order seen).
///   4. Y is the birth year. A dateless person's year comes from relatives, else from their
///      layer.
///   5. X positions are solved together: children are pulled under their parents and parents
///      over their children. Two blocks must keep NodeSpacingX apart only if they share a
///      layer or actually overlap in height, so a family can use empty space at a different
///      height.
///
/// Connectors are built by the archived engine's own connector builder, so the canvas draws
/// both engines identically. The anchor (logged-in user's person) isn't used yet.
/// Known gaps are listed in docs/architecture-decisions/004-layout-engine-direction.md.
/// </summary>
public class TimelineLayoutEngine : IFamilyTreeLayoutEngine
{
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
        var y = years.Select(yr => PaddingY + (int)((yr - minYear) * PxPerYear)).ToArray();

        var components = g.Components();
        var componentOf = new int[g.N];
        for (var c = 0; c < components.Count; c++)
            foreach (var i in components[c]) componentOf[i] = c;

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

        // Each family tree is solved on its own, then they sit side by side.
        var x = new double[g.N];
        var dividers = new List<ComponentDivider>();
        double cursor = PaddingX;
        double? previousEnd = null;
        foreach (var comp in components)
        {
            var local = PlaceComponent(g, comp, layer, years, y, size);
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

        var siblingLinks = new List<SiblingLink>();
        for (var i = 0; i < g.N; i++)
            foreach (var s in g.Siblings[i].Where(s => s > i && !g.Parents[i].Intersect(g.Parents[s]).Any()))
            {
                var (a, b) = (nodeMap[g.People[i].Id], nodeMap[g.People[s].Id]);
                siblingLinks.Add(new SiblingLink(a.X, a.Y, b.X, b.Y));
            }

        var bandHeight = Math.Max((int)(PxPerYear * 10), 80);
        var bands = new List<GenerationBand>();
        for (var year = minYear; year <= maxYear; year += 10)
            bands.Add(new GenerationBand(year, PaddingY + (int)((year - minYear) * PxPerYear), bandHeight));

        return new FamilyTreeLayout(
            [.. nodeMap.Values],
            bands,
            BuildConnectors(people, g.Couples, nodeMap),
            siblingLinks,
            dividers,
            (int)Math.Ceiling(previousEnd ?? 0) + PaddingX,
            canvasHeight,
            focus >= 0 ? depth[focus] : 0);
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
        foreach (var k in order.Where(Leaf))
        {
            var parents = inE[k].Where(u => Kept(u, k)).ToList();
            if (parents.Count > 0) level[k] = parents.Max(u => level[u]) + 1;
        }

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
            for (var i = 0; i < g.N; i++) year[i] ??= offset + GenerationYears * layer[i];
        }

        var result = year.Select(v => (int)Math.Round(v!.Value)).ToArray();
        for (var pass = 0; pass < 20; pass++)
        {
            var moved = false;
            for (var c = 0; c < g.N; c++)
                foreach (var p in g.Parents[c])
                {
                    if (result[c] - result[p] >= MinParentChildGap) continue;
                    if (!known[c]) { result[c] = result[p] + MinParentChildGap; moved = true; }
                    else if (!known[p]) { result[p] = result[c] - MinParentChildGap; moved = true; }
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
        public double Top, Bottom; // vertical extent incl. labels
    }

    private sealed record Family(List<int> Parents, List<int> Children);

    private static double[] PlaceComponent(Graph g, List<int> comp, int[] layer, int[] years, int[] y, int[] size)
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
            bl.Bottom = bl.Members.Max(m => y[m] + size[m] / 2.0 + LabelHeight) + VerticalMargin / 2.0;
        }

        var families = comp.Where(i => g.Parents[i].Count > 0)
            .GroupBy(i => string.Join(",", g.Parents[i]))
            .Select(gr => new Family(g.Parents[gr.First()], [.. gr]))
            .ToList();

        var rows = OrderLayers(g, comp, blocks, blockOf, years);
        return SolveX(g, blocks, blockOf, rows, families);
    }

    // Partners side by side. With more than two (second marriages), the person with the most
    // partners goes in the middle, their partners alternating right and left, anyone else
    // beside their own partner on the outside.
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
    private static Dictionary<int, List<int>> OrderLayers(Graph g, List<int> comp, List<Block> blocks, Dictionary<int, int> blockOf, int[] years)
    {
        var inComp = new HashSet<int>(comp);
        var rows = blocks.Select(b => b.Layer).Distinct().ToDictionary(l => l, _ => new List<int>());
        var layers = rows.Keys.Order().ToList();

        var childBlocks = blocks.Select(bl => bl.Members.SelectMany(m => g.Children[m]).Where(inComp.Contains)
            .OrderBy(ch => years[ch]).ThenBy(ch => ch).Select(ch => blockOf[ch]).Distinct().ToList()).ToList();
        var hasParent = blocks.Select(bl => bl.Members.Any(m => g.Parents[m].Any(inComp.Contains))).ToList();
        var visited = new HashSet<int>();
        void Visit(int b)
        {
            if (!visited.Add(b)) return;
            rows[blocks[b].Layer].Add(b);
            foreach (var cb in childBlocks[b]) Visit(cb);
        }
        for (var b = 0; b < blocks.Count; b++) if (!hasParent[b]) Visit(b);
        for (var b = 0; b < blocks.Count; b++) Visit(b);

        var rowIndex = new int[blocks.Count];
        void Reindex() { foreach (var row in rows.Values) for (var k = 0; k < row.Count; k++) rowIndex[row[k]] = k; }
        Reindex();

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
                    var side = k - (mem.Count - 1) / 2.0;
                    var ps = g.Parents[m].Where(inComp.Contains).ToList();
                    if (ps.Count > 0) score += (ps.Average(MPos) - Pos(b)) * side;
                    // Children this partner has with nobody else in the block (a first marriage's).
                    var own = g.Children[m].Where(ch => inComp.Contains(ch) && !g.Parents[ch].Any(p => p != m && mem.Contains(p))).ToList();
                    if (own.Count > 0) score += (own.Average(MPos) - Pos(b)) * side;
                }
                if (score < -1e-9) mem.Reverse();
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
                rows[l] = [.. rows[l].OrderBy(b => Bary(b, up: true)).ThenBy(b => rowIndex[b])];
                Reindex();
            }
            Orient();
            Keep();
            foreach (var l in Enumerable.Reverse(layers).Skip(1))
            {
                rows[l] = [.. rows[l].OrderBy(b => Bary(b, up: false)).ThenBy(b => rowIndex[b])];
                Reindex();
            }
            Orient();
            Keep();
        }

        for (var b = 0; b < blocks.Count; b++) blocks[b].Members = best.Members[b];
        return best.Rows;
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
        var siblingPairs = Enumerable.Range(0, nb).Select(b => blocks[b].Members
            .SelectMany(m => g.Siblings[m].Where(blockOf.ContainsKey).Select(s => (Me: m, Sib: s)))
            .Where(t => blockOf[t.Sib] != b).ToList()).ToArray();

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
        var center = Enumerable.Range(0, nb).Select(b => bx[b] + blocks[b].Width / 2).ToArray();
        for (var i = 0; i < nb; i++)
            for (var j = i + 1; j < nb; j++)
            {
                if (blocks[i].Layer == blocks[j].Layer) continue;
                if (blocks[i].Bottom <= blocks[j].Top || blocks[j].Bottom <= blocks[i].Top) continue;
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
