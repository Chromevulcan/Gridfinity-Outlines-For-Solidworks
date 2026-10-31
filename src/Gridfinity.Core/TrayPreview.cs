using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Gridfinity.Core
{
    /// <summary>
    /// A to-scale top view of a resolved tray as SVG (1 unit = 1 mm): bin outline, grid cells, pockets with their
    /// clearance, islands, finger notches, magnets and labels. Lets you check a layout without opening SolidWorks.
    /// </summary>
    public static class TrayPreview
    {
        public static string ToSvg(TrayDefinition t) { return ToSvg(t, null); }

        /// <summary>With <paramref name="split"/>, the seams where the tray will be cut into printable pieces are drawn too.</summary>
        public static string ToSvg(TrayDefinition t, SplitResult split)
        {
            var inv = CultureInfo.InvariantCulture;
            double W = t.OuterWidth, D = t.OuterDepth, pad = 6;
            double titleSize = Math.Max(2.5, Math.Min(4.0, (W + 2 * pad) / 40));
            double clr = t.GeometryClearance;
            var sb = new StringBuilder();
            Func<double, string> f = v => v.ToString("0.###", inv);

            sb.AppendFormat(inv, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}mm\" height=\"{1}mm\" viewBox=\"{2} {3} {0} {1}\">\n",
                f(W + 2 * pad), f(D + 2 * pad + 8), f(-W / 2 - pad), f(-D / 2 - pad - 8));
            sb.AppendLine("<style>text{font-family:Arial,sans-serif}</style>");
            sb.AppendFormat(inv, "<text x=\"{0}\" y=\"{1}\" font-size=\"{7}\" fill=\"#333\">{2}: {3} x {4} cells, {5} mm tall, {6} pocket(s){8}</text>\n",
                f(-W / 2), f(-D / 2 - 4), Escape(t.Name), t.UnitsX, t.UnitsY, f(t.TotalHeight), t.Pockets.Count, f(titleSize),
                split != null && split.WasSplit ? ", prints as " + split.Pieces.Count + " pieces" : "");

            // SVG Y runs down, the tray's Y runs up: flip everything inside this group.
            sb.AppendLine("<g transform=\"scale(1,-1)\">");
            sb.AppendFormat(inv, "<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" rx=\"{4}\" fill=\"#e9eef3\" stroke=\"#456\" stroke-width=\"0.4\"/>\n",
                f(-W / 2), f(-D / 2), f(W), f(D), f(GridfinitySpec.BinCornerRadius));

            foreach (var c in TrayModeler.CellCenters(t))
                sb.AppendFormat(inv, "<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{2}\" rx=\"1.6\" fill=\"none\" stroke=\"#9ab\" stroke-width=\"0.2\" stroke-dasharray=\"1 1\"/>\n",
                    f(c.X - 37.2 / 2), f(c.Y - 37.2 / 2), f(37.2));

            if (t.MagnetHoles)
                foreach (var c in TrayModeler.CellCenters(t))
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sy in new[] { -1, 1 })
                            sb.AppendFormat(inv, "<circle cx=\"{0}\" cy=\"{1}\" r=\"{2}\" fill=\"none\" stroke=\"#b88\" stroke-width=\"0.2\"/>\n",
                                f(c.X + sx * GridfinitySpec.MagnetOffset), f(c.Y + sy * GridfinitySpec.MagnetOffset), f(GridfinitySpec.MagnetDiameter / 2));

            // A split piece shows only its share of the parts that cross its seams.
            if (t.Piece != null)
            {
                double ox = W / 2 + SplitPieceInfo.SeamOvercut, oy = D / 2 + SplitPieceInfo.SeamOvercut;
                sb.AppendFormat(inv, "<clipPath id=\"piece\"><rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\"/></clipPath>\n", f(-ox), f(-oy), f(2 * ox), f(2 * oy));
                sb.AppendLine("<g clip-path=\"url(#piece)\">");
            }
            else sb.AppendLine("<g>");

            int mostSteps = t.Pockets.Where(q => PartCuts.IsPart(q) && q.Bottom == PartBottom.Round)
                                     .Select(q => PartCuts.Layers(q, t).Count).DefaultIfEmpty(1).Max();
            foreach (var p in t.Pockets)
            {
                bool round = PartCuts.IsPart(p) && p.Bottom == PartBottom.Round;
                sb.AppendFormat(inv, "<path d=\"{0}\" fill=\"#3d6e99\" fill-opacity=\"{1}\" stroke=\"#123\" stroke-width=\"0.25\"/>\n",
                    PathOf(p.BuildLoop(clr), f), round ? "0.45" : "0.85");
                if (round)
                {
                    // Round bottoms: each step darkens the pocket a little, so the deepest part shows darkest.
                    var layers = PartCuts.Layers(p, t);
                    double op = Math.Min(0.25, 0.9 / Math.Max(1, mostSteps));
                    foreach (var layer in layers.Skip(1))
                        sb.AppendFormat(inv, "<path d=\"{0}\" fill=\"#0b2238\" fill-opacity=\"{1}\" fill-rule=\"evenodd\" stroke=\"#123\" stroke-opacity=\"0.25\" stroke-width=\"0.1\"/>\n",
                            string.Join(" ", layer.Loops.Select(l => PathOf(l, f))), f(op));
                }
                foreach (var island in p.BuildIslandLoops(clr))
                    sb.AppendFormat(inv, "<path d=\"{0}\" fill=\"#e9eef3\" stroke=\"#123\" stroke-width=\"0.25\"/>\n", PathOf(island, f));
                var notch = p.BuildNotchLoop(clr);
                if (notch != null)
                    sb.AppendFormat(inv, "<path d=\"{0}\" fill=\"#5b8fbd\" stroke=\"#123\" stroke-width=\"0.25\"/>\n", PathOf(notch, f));
            }

            // Centrelines (dashed) through every traced part.
            if (t.PartCenterlines)
                foreach (var p in t.Pockets.Where(PartCuts.IsPart))
                {
                    Vec2 a, b;
                    p.GetCenterline(clr, out a, out b);
                    sb.AppendFormat(inv, "<line x1=\"{0}\" y1=\"{1}\" x2=\"{2}\" y2=\"{3}\" stroke=\"#e08a00\" stroke-width=\"0.3\" stroke-dasharray=\"3 1 0.6 1\"/>\n",
                        f(a.X), f(a.Y), f(b.X), f(b.Y));
                }

            sb.AppendLine("</g>");

            // Seams where the tray is split for the printer.
            if (split != null)
            {
                foreach (double x in split.SeamsX)
                    sb.AppendFormat(inv, "<line x1=\"{0}\" y1=\"{1}\" x2=\"{0}\" y2=\"{2}\" stroke=\"#c62828\" stroke-width=\"0.6\" stroke-dasharray=\"4 2\"/>\n", f(x), f(-D / 2 - 2), f(D / 2 + 2));
                foreach (double y in split.SeamsY)
                    sb.AppendFormat(inv, "<line x1=\"{0}\" y1=\"{2}\" x2=\"{1}\" y2=\"{2}\" stroke=\"#c62828\" stroke-width=\"0.6\" stroke-dasharray=\"4 2\"/>\n", f(-W / 2 - 2), f(W / 2 + 2), f(y));
            }
            sb.AppendLine("</g>");

            // Part names as they will be debossed: beside the part, along its centreline.
            foreach (var tp in TrayModeler.PartLabels(t))
                sb.AppendFormat(inv, "<text x=\"{0}\" y=\"{1}\" font-size=\"{2}\" font-weight=\"bold\" fill=\"#a33\" dominant-baseline=\"middle\" transform=\"rotate({3} {0} {1})\">{4}</text>\n",
                    f(tp.X), f(-tp.Y), f(tp.HeightMm), f(-tp.AngleDeg), Escape(tp.Text));

            // Names (unflipped so they read normally) at each pocket centre.
            // A dark box behind each name keeps it readable where it runs past the pocket.
            foreach (var p in t.Pockets.Where(q => !q.HasLabel))
            {
                double w = 0.56 * 3 * (p.Name ?? "").Length + 1.6;
                sb.AppendFormat(inv, "<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"4.2\" rx=\"0.8\" fill=\"#123\" fill-opacity=\"0.85\"/>\n",
                    f(p.X - w / 2), f(-p.Y - 2.1), f(w));
                sb.AppendFormat(inv, "<text x=\"{0}\" y=\"{1}\" font-size=\"3\" fill=\"#fff\" text-anchor=\"middle\" dominant-baseline=\"middle\">{2}</text>\n",
                    f(p.X), f(-p.Y), Escape(p.Name));
            }
            foreach (var l in t.Labels)
                sb.AppendFormat(inv, "<text x=\"{0}\" y=\"{1}\" font-size=\"{2}\" fill=\"#a33\" dominant-baseline=\"middle\">{3}</text>\n",
                    f(l.X), f(-l.Y), f(l.HeightMm), Escape(l.Text));

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private static string PathOf(Loop loop, Func<double, string> f)
        {
            var pts = loop.ToPolyline(4.0);
            var sb = new StringBuilder();
            for (int i = 0; i < pts.Count; i++)
                sb.Append(i == 0 ? "M" : "L").Append(f(pts[i].X)).Append(' ').Append(f(pts[i].Y)).Append(' ');
            return sb.Append('Z').ToString();
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
