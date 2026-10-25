using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Services;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Layout.Layered;
using static FamilyTree.Web.Services.FamilyTreeLayoutEngine;

namespace FamilyTree.Web.Tests.LayoutQuality;

// The common-practice baseline (ADR 004, added 2026-10-23): an off-the-shelf layered
// (Sugiyama) layout, Microsoft's MSAGL, standing in for what dagre or ELK would draw. It
// answers "is the prototype at least as good as the standard algorithm?", so it lives in the
// tests and never ships.
//
// Everyone married to each other is one box (partners SpouseSpacingX apart, like the
// prototype's blocks), with an edge from each parent's box to each child's box. MSAGL
// assigns layers, orders them and assigns X. Inside a box, partners are turned so each faces
// their own parents, the only family-specific step. Node sizes, depth and the birth-year Y
// come from TimelineLayoutEngine, so only the placement differs:
//
//   birthYearY = false: Y is MSAGL's layer, the pure-generation drawing common practice gives.
//   birthYearY = true:  MSAGL's X with the prototype's birth-year Y, which shows what the
//                       standard algorithm does with the timeline axis it wasn't built for.
//
// Not modelled: the anchor (no lineage sides) and orphan siblings (a Sibling link isn't an
// edge, so siblings with no parents on the tree are laid out as separate trees).
public sealed class LayeredBaselineEngine(bool birthYearY) : IFamilyTreeLayoutEngine
{
    private const int NodeSpacingX = 120;
    private const int SpouseSpacingX = 200;
    private const int PaddingX = 90;
    private const int PaddingY = 10;
    private const int ComponentGapX = 320;
    private const int LabelHeight = 40;
    private const int LayerGap = 80;

    public FamilyTreeLayout ComputeLayout(List<PersonDto> people, List<CoupleDto> couples, Guid? focusPersonId, Guid? anchorPersonId = null)
    {
        var timeline = new TimelineLayoutEngine().ComputeLayout(people, couples, focusPersonId, anchorPersonId);
        if (timeline.Nodes.Count == 0) return timeline;
        var given = timeline.Nodes.ToDictionary(n => n.Person.Id);

        people = [.. people.OrderBy(p => p.LastName, StringComparer.Ordinal).ThenBy(p => p.FirstName, StringComparer.Ordinal).ThenBy(p => p.Id)];
        var n = people.Count;
        var index = people.Select((p, i) => (p.Id, i)).ToDictionary(t => t.Id, t => t.i);
        List<int> Ids(IEnumerable<Guid>? ids, int self) =>
            [.. (ids ?? []).Where(index.ContainsKey).Select(id => index[id]).Where(j => j != self).Distinct().Order()];
        var parents = Enumerable.Range(0, n).Select(i => Ids(people[i].ParentIds, i)).ToArray();
        var partners = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        foreach (var c in couples.Where(c => index.ContainsKey(c.PersonAId) && index.ContainsKey(c.PersonBId) && c.PersonAId != c.PersonBId))
        {
            var (a, b) = (index[c.PersonAId], index[c.PersonBId]);
            if (!partners[a].Contains(b)) partners[a].Add(b);
            if (!partners[b].Contains(a)) partners[b].Add(a);
        }

        // Boxes: partner clusters, each laid out as a chain of marriages.
        var root = Enumerable.Range(0, n).ToArray();
        int Find(int i) { while (root[i] != i) i = root[i] = root[root[i]]; return i; }
        for (var i = 0; i < n; i++)
            foreach (var p in partners[i]) { var (ra, rb) = (Find(i), Find(p)); if (ra != rb) root[Math.Max(ra, rb)] = Math.Min(ra, rb); }
        var boxes = Enumerable.Range(0, n).GroupBy(Find).OrderBy(g => g.Key).Select(g => Chain([.. g], partners)).ToList();
        var boxOf = new int[n];
        for (var b = 0; b < boxes.Count; b++) foreach (var m in boxes[b]) boxOf[m] = b;
        int Size(int i) => given[people[i].Id].Size;
        double BoxWidth(int b) => (boxes[b].Count - 1) * SpouseSpacingX + boxes[b].Max(Size);

        var edges = Enumerable.Range(0, n).SelectMany(ch => parents[ch].Select(p => (P: boxOf[p], C: boxOf[ch])))
            .Where(e => e.P != e.C).Distinct().ToList();

        // Each family tree (boxes joined by parent edges) is laid out on its own and the trees
        // set side by side, as the prototype does, so the birth-year variant can't stack two
        // trees on top of each other.
        var comp = Enumerable.Range(0, boxes.Count).ToArray();
        int FindC(int i) { while (comp[i] != i) i = comp[i] = comp[comp[i]]; return i; }
        foreach (var (p, c) in edges) { var (ra, rb) = (FindC(p), FindC(c)); if (ra != rb) comp[Math.Max(ra, rb)] = Math.Min(ra, rb); }

        var centerX = new double[boxes.Count];
        var layerY = new double[boxes.Count];
        double cursor = PaddingX;
        foreach (var group in Enumerable.Range(0, boxes.Count).GroupBy(FindC).OrderBy(g => g.Key))
        {
            var members = group.ToList();
            var graph = new GeometryGraph();
            var nodes = new Dictionary<int, Node>();
            foreach (var b in members)
            {
                var h = boxes[b].Max(Size) + LabelHeight;
                graph.Nodes.Add(nodes[b] = new Node(CurveFactory.CreateRectangle(BoxWidth(b), h, new Point()), b));
            }
            foreach (var (p, c) in edges.Where(e => nodes.ContainsKey(e.P)))
                graph.Edges.Add(new Edge(nodes[p], nodes[c]));
            if (members.Count > 1)
                new LayeredLayout(graph, new SugiyamaLayoutSettings
                {
                    NodeSeparation = NodeSpacingX - boxes.SelectMany(x => x).Max(Size),
                    LayerSeparation = LayerGap,
                }).Run();

            var left = members.Min(b => nodes[b].Center.X - BoxWidth(b) / 2);
            foreach (var b in members)
            {
                centerX[b] = nodes[b].Center.X - left + cursor;
                layerY[b] = -nodes[b].Center.Y; // MSAGL's Y points up
            }
            cursor = members.Max(b => centerX[b] + BoxWidth(b) / 2) + ComponentGapX;
        }

        // Turn each box so its partners face their own parents.
        var x = new double[n];
        void Place(int b)
        {
            var mem = boxes[b];
            var start = centerX[b] - (mem.Count - 1) * SpouseSpacingX / 2.0;
            for (var k = 0; k < mem.Count; k++) x[mem[k]] = start + k * SpouseSpacingX;
        }
        for (var b = 0; b < boxes.Count; b++)
        {
            Place(b);
            var mem = boxes[b];
            var score = mem.Select((m, k) => parents[m].Count == 0 ? 0
                : (parents[m].Average(p => centerX[boxOf[p]]) - centerX[b]) * (k - (mem.Count - 1) / 2.0)).Sum();
            if (score < 0) { mem.Reverse(); Place(b); }
        }

        int[] y;
        if (birthYearY)
            y = [.. Enumerable.Range(0, n).Select(i => given[people[i].Id].Y)];
        else
        {
            var top = Enumerable.Range(0, boxes.Count).Min(b => layerY[b]);
            y = [.. Enumerable.Range(0, n).Select(i => (int)Math.Round(layerY[boxOf[i]] - top) + PaddingY + Size(i) / 2)];
        }

        var minX = x.Min() - PaddingX;
        var nodeMap = new Dictionary<Guid, LayoutNode>();
        for (var i = 0; i < n; i++)
        {
            var g = given[people[i].Id];
            nodeMap[people[i].Id] = g with { X = (int)Math.Round(x[i] - minX), Y = y[i] };
        }

        var siblingLinks = new List<SiblingLink>();
        for (var i = 0; i < n; i++)
            foreach (var s in Ids(people[i].SiblingIds, i).Where(s => s > i && !parents[i].Intersect(parents[s]).Any()))
            {
                var (a, b) = (nodeMap[people[i].Id], nodeMap[people[s].Id]);
                siblingLinks.Add(new SiblingLink(a.X, a.Y, b.X, b.Y));
            }

        return new FamilyTreeLayout(
            [.. nodeMap.Values],
            birthYearY ? timeline.Bands : [],
            BuildConnectors(people, couples, nodeMap),
            siblingLinks,
            [],
            nodeMap.Values.Max(v => v.X) + PaddingX,
            birthYearY ? timeline.CanvasHeight : nodeMap.Values.Max(v => v.Y + v.Size / 2 + LabelHeight) + PaddingY,
            timeline.FocusDepth);
    }

    // A chain of marriages in order: the person with the most partners in the middle, their
    // partners alternating right and left, anyone else beside their own partner.
    private static List<int> Chain(List<int> members, List<int>[] partners)
    {
        members.Sort();
        if (members.Count <= 2) return members;
        var set = members.ToHashSet();
        var hub = members.OrderByDescending(m => partners[m].Count(set.Contains)).ThenBy(m => m).First();
        var order = new List<int> { hub };
        var k = 0;
        foreach (var p in partners[hub].Where(set.Contains).Order())
            if (k++ % 2 == 0) order.Add(p); else order.Insert(0, p);
        while (order.Count < members.Count)
        {
            var m = members.FirstOrDefault(m => !order.Contains(m) && partners[m].Any(order.Contains), -1);
            if (m < 0) { order.AddRange(members.Where(x => !order.Contains(x))); break; }
            if (order.IndexOf(partners[m].First(order.Contains)) >= order.Count / 2.0) order.Add(m); else order.Insert(0, m);
        }
        return order;
    }
}
