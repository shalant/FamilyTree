using System.Globalization;
using System.Net;
using System.Text;
using FamilyTree.Web.Services;

namespace FamilyTree.Web.Tests.LayoutQuality;

// Renders layouts to one static HTML page of SVGs, drawn from the same FamilyTreeLayout
// connectors FamilyTreeCanvas draws, so "does it look refined" can be judged at a glance
// (ADR 004, step 1). Couples wider than the engine's partner spacing get their distance
// written in red on the couple line; overlapping nodes get a red ring.
public static class LayoutGallery
{
    public sealed record Entry(string Title, string Note, FamilyTreeLayout Layout, LayoutQualityReport Report);

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string H(string s) => WebUtility.HtmlEncode(s);

    public static void Write(string path, IEnumerable<Entry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Layout gallery</title>
            <style>
              body { font-family: system-ui, sans-serif; margin: 16px; background: #f4f7f4; color: #1d2b1f; }
              section { margin: 0 0 40px; }
              h2 { font-size: 1.1rem; margin: 0 0 4px; }
              p.note { margin: 0 0 6px; color: #556; font-size: .85rem; }
              pre { background: #fff; border: 1px solid #ccd; padding: 8px; font-size: .8rem; overflow-x: auto; }
              .canvas { background: #e6f2e8; border: 1px solid #b9d3bf; overflow: auto; max-height: 80vh; }
              svg text { font-family: system-ui, sans-serif; }
            </style></head><body>
            <h1>Layout gallery</h1>
            """);
        sb.Append($"<p class=\"note\">Generated {DateTime.Now:yyyy-MM-dd HH:mm} by LayoutGalleryTests. See docs/Testing/layout-quality-gallery.md.</p>\n");

        foreach (var e in entries)
        {
            sb.Append($"<section><h2>{H(e.Title)}</h2>");
            if (e.Note.Length > 0) sb.Append($"<p class=\"note\">{H(e.Note)}</p>");
            sb.Append($"<pre>{H(e.Report.Summary())}</pre><div class=\"canvas\">");
            sb.Append(Svg(e.Layout));
            sb.Append("</div></section>\n");
        }
        sb.Append("</body></html>\n");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
    }

    public static string Svg(FamilyTreeLayout layout)
    {
        if (layout.Nodes.Count == 0) return "<p>(empty)</p>";
        const int pad = 60;
        var minX = layout.Nodes.Min(n => n.X - n.Size) - pad;
        var maxX = layout.Nodes.Max(n => n.X + n.Size) + pad;
        var minY = layout.Nodes.Min(n => n.Y - n.Size) - pad;
        var maxY = layout.Nodes.Max(n => n.Y + n.Size) + pad;
        var (w, h) = (maxX - minX, maxY - minY);

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" viewBox=\"{minX} {minY} {w} {h}\">");

        void Line(double x1, double y1, double x2, double y2, string stroke, bool dashed) =>
            sb.Append($"<path d=\"M {F(x1)} {F(y1)} L {F(x2)} {F(y2)}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"1.4\"{(dashed ? " stroke-dasharray=\"4 3\"" : "")}/>");

        foreach (var d in layout.ComponentDividers)
            sb.Append($"<path d=\"M {F(d.X)} {d.TopY} L {F(d.X)} {d.BotY}\" stroke=\"#9aa\" stroke-width=\"1\" stroke-dasharray=\"2 6\"/>");

        foreach (var f in layout.Connectors)
        {
            if (f.Couple is { } c)
            {
                Line(c.PartnerAX, c.PartnerAY, c.PartnerBX, c.PartnerBY, c.IsFormer ? "#8a9a8f" : "#b0915a", c.IsFormer);
                var gap = Math.Abs(c.PartnerBX - c.PartnerAX);
                if (gap > LayoutQualityMetrics.SpouseSpacingX)
                    sb.Append($"<text x=\"{F(c.MidX)}\" y=\"{F((c.PartnerAY + c.PartnerBY) / 2 - 6)}\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"700\" fill=\"#c0392b\">{F(gap)}</text>");
            }
            if (f.Stem.TopY < f.Stem.BotY) Line(f.Stem.X, f.Stem.TopY, f.Stem.X, f.Stem.BotY, "#b0915a", false);
            if (f.Span is { } s) Line(s.LeftX, s.Y, s.RightX, s.Y, "#b0915a", false);
            foreach (var d in f.Drops) Line(d.CenterX, f.Stem.BotY, d.CenterX, d.BotY, "#b0915a", false);
        }
        foreach (var l in layout.SiblingLinks) Line(l.AX, l.AY, l.BX, l.BY, "#8a9a8f", true);

        var overlapping = new HashSet<Guid>();
        for (var i = 0; i < layout.Nodes.Count; i++)
            for (var j = i + 1; j < layout.Nodes.Count; j++)
            {
                var (a, b) = (layout.Nodes[i], layout.Nodes[j]);
                if (Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)) < (a.Size + b.Size) / 2.0)
                    overlapping.UnionWith([a.Person.Id, b.Person.Id]);
            }

        foreach (var n in layout.Nodes)
        {
            var r = n.Size / 2.0;
            var p = n.Person;
            var initials = $"{p.FirstName.FirstOrDefault()}{p.LastName.FirstOrDefault()}".Trim('\0');
            var ring = overlapping.Contains(p.Id) ? "#c0392b" : n.IsFocus ? "#2e7d4f" : "#a9c9b2";
            sb.Append($"<circle cx=\"{n.X}\" cy=\"{n.Y}\" r=\"{F(r)}\" fill=\"#f3faf4\" stroke=\"{ring}\" stroke-width=\"{(n.IsFocus || overlapping.Contains(p.Id) ? 3 : 1.5)}\"/>");
            sb.Append($"<text x=\"{n.X}\" y=\"{n.Y + 5}\" text-anchor=\"middle\" font-size=\"14\" fill=\"#33503b\">{H(initials)}</text>");
            sb.Append($"<text x=\"{n.X}\" y=\"{F(n.Y + r + 16)}\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"600\" fill=\"#1d2b1f\">{H(p.FirstName)}</text>");
            if (p.BirthDate is { } bd)
                sb.Append($"<text x=\"{n.X}\" y=\"{F(n.Y + r + 30)}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#7a8a7f\">{bd.Year}</text>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
