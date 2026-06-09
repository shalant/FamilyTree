using System.Globalization;
using System.Text;
using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

public enum SvgStyle      { Classic, Minimal, Dark }
public enum SvgColorTheme { Forest, Ocean, Sepia, Mono }

public class SvgExportService(FamilyTreeLayoutEngine layoutEngine)
{
    // ── Public entry point ────────────────────────────────────────────────────

    public string GenerateSvg(
        List<PersonDto> people,
        PersonDto? focusPerson,
        string title,
        SvgStyle style = SvgStyle.Classic,
        SvgColorTheme theme = SvgColorTheme.Forest)
    {
        var palette = GetPalette(style, theme);
        var couples = CoupleHelper.Derive(people);
        var layout  = layoutEngine.ComputeLayout(people, couples, focusPerson?.Id);

        var showHeaderBar  = style != SvgStyle.Minimal;
        var showBands      = style == SvgStyle.Classic;
        var useGradients   = style != SvgStyle.Minimal;
        var showShadow     = style == SvgStyle.Classic;
        var showFocusRing  = style != SvgStyle.Minimal;

        const int TitleHeight  = 56;
        const int FooterHeight = 30;
        const int MinTitleH    = 44; // Minimal has a shorter title area

        var titleH = showHeaderBar ? TitleHeight : MinTitleH;
        var svgW   = layout.CanvasWidth;
        var svgH   = layout.CanvasHeight + titleH + FooterHeight;

        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{svgW}" height="{svgH}" viewBox="0 0 {svgW} {svgH}">""");

        // ── Defs ───────────────────────────────────────────────────────────
        AppendDefs(sb, palette, useGradients);

        // ── Background ────────────────────────────────────────────────────
        sb.AppendLine($"""<rect x="0" y="0" width="{svgW}" height="{svgH}" fill="{palette.CanvasBg}"/>""");

        // ── Title area ────────────────────────────────────────────────────
        if (showHeaderBar)
        {
            sb.AppendLine($"""<rect x="0" y="0" width="{svgW}" height="{titleH}" fill="{palette.TitleBg}"/>""");
            sb.AppendLine($"""<text x="{svgW / 2}" y="33" text-anchor="middle" font-family="Georgia,serif" font-size="20" font-weight="700" fill="{palette.TitleFg}" letter-spacing="0.5">{Escape(title)}</text>""");
            sb.AppendLine($"""<text x="{svgW / 2}" y="49" text-anchor="middle" font-family="Arial,sans-serif" font-size="11" fill="{palette.SubtitleFg}">{people.Count} people  &#x2022;  ArborKin</text>""");
        }
        else
        {
            // Minimal: centered title above the tree
            sb.AppendLine($"""<text x="{svgW / 2}" y="26" text-anchor="middle" font-family="Georgia,serif" font-size="18" font-weight="600" fill="{palette.NameText}">{Escape(title)}</text>""");
            sb.AppendLine($"""<text x="{svgW / 2}" y="40" text-anchor="middle" font-family="Arial,sans-serif" font-size="11" fill="{palette.YearText}">{people.Count} people</text>""");
        }

        // ── Decade bands ──────────────────────────────────────────────────
        if (showBands)
        {
            for (var i = 0; i < layout.Bands.Count; i++)
            {
                var band  = layout.Bands[i];
                var fill  = i % 2 == 0 ? "rgba(15,110,86,0.04)" : "rgba(0,0,0,0.025)";
                var bandY = band.Top + titleH;
                sb.AppendLine($"""<rect x="0" y="{bandY}" width="{svgW}" height="{band.Height}" fill="{fill}"/>""");
                sb.AppendLine($"""<text x="46" y="{bandY + 14}" font-family="Courier New,monospace" font-size="10" fill="{palette.YearText}" text-anchor="end">{band.Depth}</text>""");
            }
        }

        // ── Connectors ────────────────────────────────────────────────────
        foreach (var family in layout.Connectors)
        {
            if (family.Couple is { } arc)
            {
                var ay    = arc.PartnerAY + titleH;
                var by    = arc.PartnerBY + titleH;
                var hy    = arc.HeartY    + titleH;
                var color = arc.IsFormer  ? palette.DeadTop : palette.Couple;
                var dash  = arc.IsFormer  ? """ stroke-dasharray="5 3" """ : " ";
                sb.AppendLine($"""<line x1="{F(arc.PartnerAX)}" y1="{F(ay)}" x2="{F(arc.PartnerBX)}" y2="{F(by)}" stroke="{color}" stroke-width="1.5"{dash}/>""");
                if (!arc.IsFormer)
                    sb.AppendLine($"""<text x="{F(arc.MidX)}" y="{F(hy - 3)}" text-anchor="middle" font-size="9" fill="{palette.Couple}">&#x2665;</text>""");
            }

            var stem = family.Stem;
            if (stem.TopY < stem.BotY)
                sb.AppendLine($"""<line x1="{F(stem.X)}" y1="{F(stem.TopY + titleH)}" x2="{F(stem.X)}" y2="{F(stem.BotY + titleH)}" stroke="{palette.Parent}" stroke-width="1.2"/>""");

            if (family.Span is { } span)
                sb.AppendLine($"""<line x1="{F(span.LeftX)}" y1="{F(span.Y + titleH)}" x2="{F(span.RightX)}" y2="{F(span.Y + titleH)}" stroke="{palette.Parent}" stroke-width="1.2"/>""");

            var dropTop = stem.BotY + titleH;
            foreach (var drop in family.Drops)
                sb.AppendLine($"""<line x1="{F(drop.CenterX)}" y1="{F(dropTop)}" x2="{F(drop.CenterX)}" y2="{F(drop.BotY + titleH)}" stroke="{palette.Parent}" stroke-width="1.2"/>""");
        }

        // ── Person nodes ──────────────────────────────────────────────────
        foreach (var node in layout.Nodes)
        {
            var p      = node.Person;
            var r      = node.Size / 2;
            var cx     = node.X;
            var cy     = node.Y + titleH;
            var relDep = node.Depth - layout.FocusDepth;

            string fillRef;
            if (useGradients)
            {
                fillRef = node.IsFocus           ? "url(#gFocus)"
                        : p.IsDeceased           ? "url(#gDead)"
                        : Math.Abs(relDep) <= 1  ? "url(#gGen1)"
                        : "url(#gOther)";
            }
            else
            {
                // Minimal: flat solid fill + border
                var flatFill = node.IsFocus           ? palette.FocusTop
                             : p.IsDeceased           ? palette.DeadTop
                             : Math.Abs(relDep) <= 1  ? palette.Gen1Top
                             : palette.OtherTop;
                fillRef = flatFill;
            }

            if (showShadow)
                sb.AppendLine($"""<circle cx="{cx}" cy="{cy + 2}" r="{r}" fill="rgba(0,0,0,0.10)"/>""");

            sb.AppendLine($"""<circle cx="{cx}" cy="{cy}" r="{r}" fill="{fillRef}"/>""");

            if (!useGradients)
            {
                // Minimal: add a subtle border
                var borderColor = node.IsFocus ? palette.FocusBg : palette.OtherBg;
                sb.AppendLine($"""<circle cx="{cx}" cy="{cy}" r="{r}" fill="none" stroke="{borderColor}" stroke-width="1.5"/>""");
            }

            if (showFocusRing && node.IsFocus)
                sb.AppendLine($"""<circle cx="{cx}" cy="{cy}" r="{r + 4}" fill="none" stroke="{palette.FocusTop}" stroke-width="2" stroke-dasharray="4 2" opacity="0.55"/>""");

            var initials = $"{p.FirstName?.FirstOrDefault()}{p.LastName?.FirstOrDefault()}".ToUpperInvariant().Trim();
            var fSize    = node.IsFocus ? 16 : Math.Abs(relDep) <= 1 ? 14 : 12;
            var textFill = (style == SvgStyle.Minimal && !node.IsFocus) ? palette.NameText : palette.CircleText;
            sb.AppendLine($"""<text x="{cx}" y="{cy + (int)(fSize * 0.38)}" text-anchor="middle" dominant-baseline="middle" font-family="Arial,sans-serif" font-size="{fSize}" font-weight="600" fill="{textFill}">{Escape(initials)}</text>""");

            var nameLabel = Trim14(p.FirstName ?? "");
            sb.AppendLine($"""<text x="{cx}" y="{cy + r + 15}" text-anchor="middle" font-family="Arial,sans-serif" font-size="11" font-weight="500" fill="{palette.NameText}">{Escape(nameLabel)}</text>""");

            var yr = p.BirthDate?.Year;
            if (yr.HasValue)
            {
                var yrStr = p.IsDeceased && p.DeathDate.HasValue
                    ? $"{yr}–{p.DeathDate.Value.Year}"
                    : $"{yr}";
                sb.AppendLine($"""<text x="{cx}" y="{cy + r + 27}" text-anchor="middle" font-family="Arial,sans-serif" font-size="10" fill="{palette.YearText}">{Escape(yrStr)}</text>""");
            }
        }

        // ── Footer ────────────────────────────────────────────────────────
        var footerY = svgH - FooterHeight;
        sb.AppendLine($"""<line x1="50" y1="{footerY}" x2="{svgW - 50}" y2="{footerY}" stroke="{palette.FooterFg}" stroke-width="0.5" opacity="0.5"/>""");
        sb.AppendLine($"""<text x="{svgW / 2}" y="{footerY + 16}" text-anchor="middle" font-family="Arial,sans-serif" font-size="10" fill="{palette.FooterFg}">Generated by ArborKin</text>""");

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    // ── Defs (gradient definitions) ───────────────────────────────────────────

    private static void AppendDefs(StringBuilder sb, SvgPalette p, bool useGradients)
    {
        if (!useGradients) return;
        sb.AppendLine("<defs>");
        AppendGrad(sb, "gFocus", p.FocusTop, p.FocusBg);
        AppendGrad(sb, "gGen1",  p.Gen1Top,  p.Gen1Bg);
        AppendGrad(sb, "gOther", p.OtherTop, p.OtherBg);
        AppendGrad(sb, "gDead",  p.DeadTop,  p.DeadBg);
        sb.AppendLine("</defs>");
    }

    private static void AppendGrad(StringBuilder sb, string id, string top, string bot) =>
        sb.AppendLine($"""  <radialGradient id="{id}" cx="0.38" cy="0.32" r="0.72" gradientUnits="objectBoundingBox"><stop offset="0%" stop-color="{top}"/><stop offset="100%" stop-color="{bot}"/></radialGradient>""");

    // ── Palette factory ───────────────────────────────────────────────────────

    private static SvgPalette GetPalette(SvgStyle style, SvgColorTheme theme) =>
        (style, theme) switch
        {
            // ── Classic ──────────────────────────────────────────────────────
            (SvgStyle.Classic, SvgColorTheme.Forest) => new(
                "#f7f5f0", "#085041", "#ffffff", "rgba(255,255,255,0.55)",
                "#7B73D3", "#3B3499",   // focus
                "#1D9E75", "#085041",   // gen1
                "#5DCAA5", "#0F6E56",   // other
                "#AAA9A0", "#656560",   // deceased
                "#ffffff", "#9FE1CB", "#5DCAA5",
                "#085041", "#B4B2A9", "#C8C4BA"),

            (SvgStyle.Classic, SvgColorTheme.Ocean) => new(
                "#f0f5fb", "#1565c0", "#ffffff", "rgba(255,255,255,0.55)",
                "#5c6bc0", "#283593",
                "#2196f3", "#1565c0",
                "#64b5f6", "#1976d2",
                "#90a4ae", "#546e7a",
                "#ffffff", "#90caf9", "#64b5f6",
                "#1565c0", "#90a4ae", "#b0bec5"),

            (SvgStyle.Classic, SvgColorTheme.Sepia) => new(
                "#fdf6ec", "#5d4037", "#ffffff", "rgba(255,255,255,0.55)",
                "#8d6e63", "#4e342e",
                "#a1887f", "#5d4037",
                "#d4a574", "#a0522d",
                "#bcaaa4", "#8d6e63",
                "#ffffff", "#d4a574", "#a1887f",
                "#4e342e", "#bcaaa4", "#c8b9a6"),

            (SvgStyle.Classic, SvgColorTheme.Mono) => new(
                "#ffffff", "#212121", "#ffffff", "rgba(255,255,255,0.55)",
                "#424242", "#212121",
                "#616161", "#424242",
                "#9e9e9e", "#616161",
                "#bdbdbd", "#9e9e9e",
                "#ffffff", "#bdbdbd", "#9e9e9e",
                "#212121", "#9e9e9e", "#bdbdbd"),

            // ── Minimal ──────────────────────────────────────────────────────
            (SvgStyle.Minimal, SvgColorTheme.Forest) => new(
                "#ffffff", "#085041", "#085041", "#5DCAA5",
                "#534AB7", "#3B3499",
                "#0F6E56", "#085041",
                "#5DCAA5", "#0F6E56",
                "#9e9e9e", "#757575",
                "#085041", "#9FE1CB", "#5DCAA5",
                "#085041", "#9e9e9e", "#bdbdbd"),

            (SvgStyle.Minimal, SvgColorTheme.Ocean) => new(
                "#ffffff", "#1565c0", "#1565c0", "#64b5f6",
                "#283593", "#1a237e",
                "#1976d2", "#1565c0",
                "#64b5f6", "#42a5f5",
                "#9e9e9e", "#757575",
                "#1565c0", "#90caf9", "#64b5f6",
                "#1565c0", "#90a4ae", "#bdbdbd"),

            (SvgStyle.Minimal, SvgColorTheme.Sepia) => new(
                "#fdf6ec", "#4e342e", "#4e342e", "#d4a574",
                "#4e342e", "#3e2723",
                "#6d4c41", "#4e342e",
                "#a0522d", "#8d6e63",
                "#9e9e9e", "#757575",
                "#4e342e", "#d4a574", "#a1887f",
                "#4e342e", "#bcaaa4", "#d4c4bc"),

            (SvgStyle.Minimal, SvgColorTheme.Mono) => new(
                "#ffffff", "#212121", "#212121", "#9e9e9e",
                "#212121", "#424242",
                "#424242", "#616161",
                "#757575", "#9e9e9e",
                "#bdbdbd", "#9e9e9e",
                "#212121", "#bdbdbd", "#9e9e9e",
                "#212121", "#9e9e9e", "#bdbdbd"),

            // ── Dark ─────────────────────────────────────────────────────────
            (SvgStyle.Dark, SvgColorTheme.Forest) => new(
                "#1a1d21", "#0d1014", "#f0ede8", "rgba(240,237,232,0.5)",
                "#9C94E0", "#534AB7",
                "#4EC99A", "#0F6E56",
                "#9FE1CB", "#1D9E75",
                "#78746E", "#4A4741",
                "#ffffff", "#9FE1CB", "#5DCAA5",
                "#E0DDD5", "#787570", "#555249"),

            (SvgStyle.Dark, SvgColorTheme.Ocean) => new(
                "#0d1420", "#070e18", "#e8f0ff", "rgba(232,240,255,0.5)",
                "#7986cb", "#3949ab",
                "#64b5f6", "#1976d2",
                "#90caf9", "#42a5f5",
                "#607d8b", "#455a64",
                "#ffffff", "#90caf9", "#64b5f6",
                "#e8f0ff", "#607d8b", "#455a64"),

            (SvgStyle.Dark, SvgColorTheme.Sepia) => new(
                "#1a1410", "#0e0b08", "#f5e8d0", "rgba(245,232,208,0.5)",
                "#c49a82", "#6d4c41",
                "#d4a574", "#a0522d",
                "#e8c49a", "#c4913a",
                "#7d6d63", "#5d4e45",
                "#ffffff", "#d4a574", "#a1887f",
                "#f5e8d0", "#8d7d73", "#5d4e45"),

            (SvgStyle.Dark, SvgColorTheme.Mono) => new(
                "#1a1a1a", "#0d0d0d", "#f5f5f5", "rgba(245,245,245,0.5)",
                "#e0e0e0", "#9e9e9e",
                "#bdbdbd", "#757575",
                "#9e9e9e", "#616161",
                "#616161", "#424242",
                "#1a1a1a", "#9e9e9e", "#757575",
                "#f5f5f5", "#757575", "#424242"),

            _ => GetPalette(SvgStyle.Classic, SvgColorTheme.Forest)
        };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Escape(string? s)
    {
        if (s is null) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
    }

    private static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

    private static string Trim14(string name) =>
        name.Length > 14 ? name[..14] + "…" : name;

    // ── Palette record ────────────────────────────────────────────────────────

    private sealed record SvgPalette(
        string CanvasBg,
        string TitleBg,
        string TitleFg,
        string SubtitleFg,
        string FocusTop, string FocusBg,
        string Gen1Top,  string Gen1Bg,
        string OtherTop, string OtherBg,
        string DeadTop,  string DeadBg,
        string CircleText,
        string Couple,
        string Parent,
        string NameText,
        string YearText,
        string FooterFg);
}
