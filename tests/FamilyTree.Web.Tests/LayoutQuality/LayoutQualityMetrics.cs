using System.Text;
using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Services;

namespace FamilyTree.Web.Tests.LayoutQuality;

// How a layout looks, as numbers (ADR 004, step 1). The fuzz invariants check that a layout
// is correct; these score how loose or tangled a correct layout is, so a replacement engine
// has a baseline to beat. Nothing fails on them yet.
//
// All distances are in layout units (the coordinates FamilyTreeCanvas draws at zoom 1).
// Lines are the same segments FamilyTreeCanvas draws: couple lines, stems, sibling spans,
// child drops and sibling links (component dividers are background, not connectors).
public static class LayoutQualityMetrics
{
    // Mirror of the private constant in FamilyTreeLayoutEngine.
    public const int SpouseSpacingX = 200;

    public static LayoutQualityReport Compute(FamilyTreeLayout layout, IReadOnlyList<PersonDto> people, IReadOnlyList<CoupleDto> couples)
    {
        var nodes = layout.Nodes.ToDictionary(n => n.Person.Id);
        string Name(Guid id) => nodes.TryGetValue(id, out var n) ? n.Person.FirstName : id.ToString()[..8];

        var gaps = couples
            .Where(c => nodes.ContainsKey(c.PersonAId) && nodes.ContainsKey(c.PersonBId))
            .Select(c => new PartnerGap(Name(c.PersonAId), Name(c.PersonBId),
                Math.Abs(nodes[c.PersonAId].X - nodes[c.PersonBId].X), c.IsFormer))
            .OrderByDescending(g => g.Distance).ThenBy(g => g.A).ThenBy(g => g.B)
            .ToList();

        var segments = Segments(layout);

        var crossings = 0;
        for (var i = 0; i < segments.Count; i++)
            for (var j = i + 1; j < segments.Count; j++)
                if (segments[i].Owner != segments[j].Owner && ProperlyCross(segments[i], segments[j]))
                    crossings++;

        // A line through a node it doesn't start or end at. Touching the rim doesn't count.
        var throughNodes = new List<string>();
        foreach (var s in segments)
            foreach (var n in layout.Nodes)
            {
                var r = n.Size / 2.0;
                if (Dist(n.X, n.Y, s.X1, s.Y1) <= r + 6 || Dist(n.X, n.Y, s.X2, s.Y2) <= r + 6) continue;
                if (DistToSegment(n.X, n.Y, s) < r - 2) throughNodes.Add($"{s.Kind} through {n.Person.FirstName}");
            }

        var overlaps = 0;
        for (var i = 0; i < layout.Nodes.Count; i++)
            for (var j = i + 1; j < layout.Nodes.Count; j++)
            {
                var (a, b) = (layout.Nodes[i], layout.Nodes[j]);
                if (Dist(a.X, a.Y, b.X, b.Y) < (a.Size + b.Size) / 2.0) overlaps++;
            }

        // Each set of children sharing the same placed parents, against those parents:
        // how far the children's centre sits from the parents' centre.
        var offCenter = people
            .Where(p => nodes.ContainsKey(p.Id))
            .Select(p => (Child: p.Id, Parents: (p.ParentIds ?? []).Where(nodes.ContainsKey).Order().ToList()))
            .Where(x => x.Parents.Count > 0)
            .GroupBy(x => string.Join(",", x.Parents))
            .Select(g => new FamilyOffset(
                string.Join(" + ", g.First().Parents.Select(Name)),
                string.Join(", ", g.Select(x => Name(x.Child))),
                Math.Abs(g.Average(x => nodes[x.Child].X) - g.First().Parents.Average(pid => nodes[pid].X))))
            .OrderByDescending(f => f.Offset).ThenBy(f => f.Parents)
            .ToList();

        var xs = layout.Nodes.Select(n => n.X).DefaultIfEmpty().ToList();
        var ys = layout.Nodes.Select(n => n.Y).DefaultIfEmpty().ToList();

        return new LayoutQualityReport(
            layout.Nodes.Count,
            xs.Max() - xs.Min(),
            ys.Max() - ys.Min(),
            gaps,
            crossings,
            throughNodes.Count,
            throughNodes,
            overlaps,
            segments.Sum(s => Dist(s.X1, s.Y1, s.X2, s.Y2)),
            offCenter);
    }

    public sealed record Segment(int Owner, double X1, double Y1, double X2, double Y2, string Kind = "line");

    // Each FamilyUnit's lines share one owner (they meet by design); each sibling link is its own.
    public static List<Segment> Segments(FamilyTreeLayout layout)
    {
        var list = new List<Segment>();
        var owner = 0;
        foreach (var f in layout.Connectors)
        {
            if (f.Couple is { } c) list.Add(new(owner, c.PartnerAX, c.PartnerAY, c.PartnerBX, c.PartnerBY, "couple line"));
            if (f.Stem.TopY < f.Stem.BotY) list.Add(new(owner, f.Stem.X, f.Stem.TopY, f.Stem.X, f.Stem.BotY, "stem"));
            if (f.Span is { } s) list.Add(new(owner, s.LeftX, s.Y, s.RightX, s.Y, "sibling span"));
            foreach (var d in f.Drops) list.Add(new(owner, d.CenterX, f.Stem.BotY, d.CenterX, d.BotY, "child drop"));
            owner++;
        }
        foreach (var l in layout.SiblingLinks) list.Add(new(owner++, l.AX, l.AY, l.BX, l.BY, "sibling link"));
        return list.Where(s => Dist(s.X1, s.Y1, s.X2, s.Y2) > 0.5).ToList();
    }

    // Strict crossing: each segment's ends lie strictly on opposite sides of the other.
    // Lines that only touch, meet at an end, or run along each other don't count.
    private static bool ProperlyCross(Segment a, Segment b)
    {
        static double Orient(double ax, double ay, double bx, double by, double cx, double cy) =>
            (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        var d1 = Orient(a.X1, a.Y1, a.X2, a.Y2, b.X1, b.Y1);
        var d2 = Orient(a.X1, a.Y1, a.X2, a.Y2, b.X2, b.Y2);
        var d3 = Orient(b.X1, b.Y1, b.X2, b.Y2, a.X1, a.Y1);
        var d4 = Orient(b.X1, b.Y1, b.X2, b.Y2, a.X2, a.Y2);
        const double eps = 1e-6;
        return ((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps)) &&
               ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps));
    }

    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    private static double DistToSegment(double px, double py, Segment s)
    {
        var (dx, dy) = (s.X2 - s.X1, s.Y2 - s.Y1);
        var t = Math.Clamp(((px - s.X1) * dx + (py - s.Y1) * dy) / (dx * dx + dy * dy), 0, 1);
        return Dist(px, py, s.X1 + t * dx, s.Y1 + t * dy);
    }
}

public sealed record PartnerGap(string A, string B, double Distance, bool IsFormer);

public sealed record FamilyOffset(string Parents, string Children, double Offset);

public sealed record LayoutQualityReport(
    int NodeCount,
    int Width,
    int Height,
    IReadOnlyList<PartnerGap> PartnerGaps,  // every drawn couple, widest first
    int Crossings,
    int LinesThroughNodes,
    IReadOnlyList<string> LinesThroughNodeDetails,  // "stem through Ray", in segment order
    int NodeOverlaps,
    double ConnectorLength,
    IReadOnlyList<FamilyOffset> FamilyOffsets)  // every set of children sharing parents, most off-centre first
{
    public double MaxFamilyOffCenter => FamilyOffsets.Count == 0 ? 0 : FamilyOffsets[0].Offset;
    public double MeanFamilyOffCenter => FamilyOffsets.Count == 0 ? 0 : FamilyOffsets.Average(f => f.Offset);

    public double MaxPartnerGap => PartnerGaps.Count == 0 ? 0 : PartnerGaps[0].Distance;

    // Couples drawn further apart than the engine's own partner spacing.
    public IEnumerable<PartnerGap> LooseCouples => PartnerGaps.Where(g => g.Distance > LayoutQualityMetrics.SpouseSpacingX);

    public string Summary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{NodeCount} people, {Width} x {Height}");
        sb.AppendLine($"partner gap: max {MaxPartnerGap:0}, {LooseCouples.Count()} of {PartnerGaps.Count} couples over {LayoutQualityMetrics.SpouseSpacingX}");
        foreach (var g in LooseCouples.Take(5))
            sb.AppendLine($"  {g.A} + {g.B}: {g.Distance:0}{(g.IsFormer ? " (former)" : "")}");
        sb.AppendLine($"crossings: {Crossings}, lines through nodes: {LinesThroughNodes}, node overlaps: {NodeOverlaps}");
        foreach (var t in LinesThroughNodeDetails.Take(5))
            sb.AppendLine($"  {t}");
        sb.AppendLine($"connector length: {ConnectorLength:0}, family off-centre: max {MaxFamilyOffCenter:0}, mean {MeanFamilyOffCenter:0}");
        foreach (var f in FamilyOffsets.Where(f => f.Offset > LayoutQualityMetrics.SpouseSpacingX).Take(3))
            sb.AppendLine($"  {f.Children} under {f.Parents}: {f.Offset:0}");
        return sb.ToString();
    }
}
