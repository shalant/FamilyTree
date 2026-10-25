using System.Text;
using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Services;

namespace FamilyTree.Web.Tests.LayoutQuality;

// How a layout looks, as numbers (ADR 004, step 1). The fuzz invariants check that a layout
// is correct; these score how loose or tangled a correct layout is, so a replacement engine
// has a baseline to beat. LayoutRatchetTests holds TimelineLayoutEngine to them (2026-10-23).
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
        // A sibling bar's gap (a line jump) is where another line crosses it: the gap only stops
        // it looking like a junction, so it still counts as a crossing.
        crossings += layout.SiblingLinks.Sum(l => l.Gaps?.Count ?? 0);

        // A line through a node it doesn't start or end at. Touching the rim doesn't count.
        var throughNodes = new List<string>();
        foreach (var s in segments)
            foreach (var n in layout.Nodes)
            {
                var r = n.Size / 2.0;
                if (Dist(n.X, n.Y, s.X1, s.Y1) <= r + 6 || Dist(n.X, n.Y, s.X2, s.Y2) <= r + 6) continue;
                if (DistToSegment(n.X, n.Y, s) < r - 2) throughNodes.Add($"{s.Kind} through {n.Person.FirstName}");
            }

        // A line through someone's name or birth year under their circle (found 2026-10-25 on
        // the live tree: a couple line drawn at the lower partner's height ran through the
        // higher partner's name, and a single parent's stem through their own).
        var throughLabels = new List<string>();
        foreach (var s in segments)
            foreach (var n in layout.Nodes)
                if (CrossesRect(s, LabelBox(n))) throughLabels.Add($"{s.Kind} through {n.Person.FirstName}'s name");

        // Lines from two families drawn on top of each other read as one family, which states a
        // relationship that isn't there: on the live tree (found 2026-10-23) Ray's line from
        // Louis + Lillian ran along Morry and Isador's line from their mother, so the three looked
        // like brothers. Crossings and lines through nodes both miss it. Two sibling links that
        // share a person may merge, since everyone on them is a sibling (Louis, Tilly, David).
        var linkEnds = layout.SiblingLinks.Select(l => new[] { (l.AX, l.AY), (l.BX, l.BY) }).ToList();
        bool SiblingsOnly(int a, int b)
        {
            var (ka, kb) = (a - layout.Connectors.Count, b - layout.Connectors.Count);
            return ka >= 0 && kb >= 0 && linkEnds[ka].Intersect(linkEnds[kb]).Any();
        }
        string At(double x, double y) => layout.Nodes.OrderBy(n => Dist(n.X, n.Y, x, y)).FirstOrDefault()?.Person.FirstName ?? "?";
        string OwnerName(int o)
        {
            if (o >= layout.Connectors.Count)
            {
                var l = layout.SiblingLinks[o - layout.Connectors.Count];
                return $"the {At(l.AX, l.AY)}–{At(l.BX, l.BY)} sibling link";
            }
            var f = layout.Connectors[o];
            if (f.Drops.Count > 0) return $"{string.Join(", ", f.Drops.Select(d => At(d.CenterX, d.BotY)))}'s line";
            return f.Couple is { } c ? $"{At(c.PartnerAX, c.PartnerAY)} + {At(c.PartnerBX, c.PartnerBY)}" : "a line";
        }
        var merged = new List<string>();
        for (var i = 0; i < segments.Count; i++)
            for (var j = i + 1; j < segments.Count; j++)
            {
                var (a, b) = (segments[i], segments[j]);
                if (a.Owner == b.Owner || SiblingsOnly(a.Owner, b.Owner)) continue;
                if (Overlap(a, b) > MergeMinLength)
                    merged.Add($"{OwnerName(a.Owner)} on {OwnerName(b.Owner)}");
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
            offCenter,
            merged,
            throughLabels);
    }

    // Where PersonNode draws the name (11px, top at rim + 5) and birth year (10px, rim + 20),
    // centred under the circle and cut off at 88 wide. Text width is estimated per character.
    public static (double Left, double Top, double Right, double Bottom) LabelBox(FamilyTreeLayoutEngine.LayoutNode n)
    {
        const double labelWidth = 88, nameCharWidth = 6.2, yearWidth = 24;
        var width = Math.Min(labelWidth, Math.Max(n.Person.FirstName.Length * nameCharWidth, n.Person.BirthDate.HasValue ? yearWidth : 0));
        var top = n.Y + n.Size / 2.0 + 5;
        var bottom = top + (n.Person.BirthDate.HasValue ? 28 : 14);
        return (n.X - width / 2, top, n.X + width / 2, bottom);
    }

    // Whether a segment passes through the inside of a box (Liang-Barsky clipping); a line that
    // only grazes an edge doesn't count.
    private static bool CrossesRect(Segment s, (double Left, double Top, double Right, double Bottom) box)
    {
        const double inset = 1;
        var (l, t, r, b) = (box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
        var (dx, dy) = (s.X2 - s.X1, s.Y2 - s.Y1);
        double t0 = 0, t1 = 1;
        foreach (var (pk, qk) in new[] { (-dx, s.X1 - l), (dx, r - s.X1), (-dy, s.Y1 - t), (dy, b - s.Y1) })
        {
            if (Math.Abs(pk) < 1e-9) { if (qk < 0) return false; continue; }
            var u = qk / pk;
            if (pk < 0) t0 = Math.Max(t0, u); else t1 = Math.Min(t1, u);
            if (t0 > t1) return false;
        }
        return t1 - t0 > 1e-6;
    }

    public sealed record Segment(int Owner, double X1, double Y1, double X2, double Y2, string Kind = "line");

    // Each FamilyUnit's lines share one owner (they meet by design); each sibling link is its own.
    public static List<Segment> Segments(FamilyTreeLayout layout)
    {
        var list = new List<Segment>();
        var owner = 0;
        foreach (var f in layout.Connectors)
        {
            if (f.Couple is { } c)
                foreach (var (x1, y1, x2, y2) in c.Segments) list.Add(new(owner, x1, y1, x2, y2, "couple line"));
            if (f.Stem.TopY < f.Stem.BotY) list.Add(new(owner, f.Stem.X, f.Stem.TopY, f.Stem.X, f.Stem.BotY, "stem"));
            if (f.Span is { } s) list.Add(new(owner, s.LeftX, s.Y, s.RightX, s.Y, "sibling span"));
            foreach (var d in f.Drops) list.Add(new(owner, d.CenterX, f.Stem.BotY, d.CenterX, d.BotY, "child drop"));
            owner++;
        }
        foreach (var l in layout.SiblingLinks)
        {
            foreach (var (x1, y1, x2, y2) in l.Segments) list.Add(new(owner, x1, y1, x2, y2, "sibling link"));
            owner++;
        }
        return list.Where(s => Dist(s.X1, s.Y1, s.X2, s.Y2) > 0.5).ToList();
    }

    // Two lines read as one when both run the same way within MergeDistance of each other
    // (the canvas draws 1.2 wide); returns how far they run together, 0 if they don't.
    private const double MergeDistance = 3;
    private const double MergeMinLength = 2;

    private static double Overlap(Segment a, Segment b)
    {
        static bool Flat(Segment s) => Math.Abs(s.Y1 - s.Y2) < 0.5;
        static bool Upright(Segment s) => Math.Abs(s.X1 - s.X2) < 0.5;
        static double Shared(double a1, double a2, double b1, double b2) =>
            Math.Min(Math.Max(a1, a2), Math.Max(b1, b2)) - Math.Max(Math.Min(a1, a2), Math.Min(b1, b2));
        if (Flat(a) && Flat(b) && Math.Abs(a.Y1 - b.Y1) < MergeDistance) return Math.Max(0, Shared(a.X1, a.X2, b.X1, b.X2));
        if (Upright(a) && Upright(b) && Math.Abs(a.X1 - b.X1) < MergeDistance) return Math.Max(0, Shared(a.Y1, a.Y2, b.Y1, b.Y2));
        return 0;
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
    IReadOnlyList<FamilyOffset> FamilyOffsets,  // every set of children sharing parents, most off-centre first
    IReadOnlyList<string> MergedLineDetails,    // "Ray's line on Morry, Isador's line", in segment order
    IReadOnlyList<string> LinesThroughLabelDetails)  // "couple line through Joseph's name", in segment order
{
    public int LinesThroughLabels => LinesThroughLabelDetails.Count;

    // Pieces of two different families' lines drawn on top of each other.
    public int MergedLines => MergedLineDetails.Count;

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
        sb.AppendLine($"lines through names: {LinesThroughLabels}");
        foreach (var t in LinesThroughLabelDetails.Take(5))
            sb.AppendLine($"  {t}");
        sb.AppendLine($"lines on top of another family's: {MergedLines}");
        foreach (var m in MergedLineDetails.Take(5))
            sb.AppendLine($"  {m}");
        sb.AppendLine($"connector length: {ConnectorLength:0}, family off-centre: max {MaxFamilyOffCenter:0}, mean {MeanFamilyOffCenter:0}");
        foreach (var f in FamilyOffsets.Where(f => f.Offset > LayoutQualityMetrics.SpouseSpacingX).Take(3))
            sb.AppendLine($"  {f.Children} under {f.Parents}: {f.Offset:0}");
        return sb.ToString();
    }
}
