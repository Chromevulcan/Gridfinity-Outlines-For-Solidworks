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
        public static string ToSvg(TrayDefinition t)
        {
            var inv = CultureInfo.InvariantCulture;
            double W = t.OuterWidth, D = t.OuterDepth, pad = 6;
            double titleSize = Math.Max(2.5, Math.Min(4.0, (W + 2 * pad) / 40));
            double clr = t.DefaultClearance;
            var sb = new StringBuilder();
            Func<double, string> f = v => v.ToString("0.###", inv);

            sb.AppendFormat(inv, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}mm\" height=\"{1}mm\" viewBox=\"{2} {3} {0} {1}\">\n",
                f(W + 2 * pad), f(D + 2 * pad + 8), f(-W / 2 - pad), f(-D / 2 - pad - 8));
            sb.AppendLine("<style>text{font-family:Arial,sans-serif}</style>");
            sb.AppendFormat(inv, "<text x=\"{0}\" y=\"{1}\" font-size=\"{7}\" fill=\"#333\">{2}: {3} x {4} cells, {5} mm tall, {6} pocket(s)</text>\n",
                f(-W / 2), f(-D / 2 - 4), Escape(t.Name), t.UnitsX, t.UnitsY, f(t.TotalHeight), t.Pockets.Count, f(titleSize));

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

            foreach (var p in t.Pockets)
            {
                sb.AppendFormat(inv, "<path d=\"{0}\" fill=\"#3d6e99\" fill-opacity=\"0.85\" stroke=\"#123\" stroke-width=\"0.25\"/>\n", PathOf(p.BuildLoop(clr), f));
                foreach (var island in p.BuildIslandLoops(clr))
                    sb.AppendFormat(inv, "<path d=\"{0}\" fill=\"#e9eef3\" stroke=\"#123\" stroke-width=\"0.25\"/>\n", PathOf(island, f));
                var notch = p.BuildNotchLoop(clr);
                if (notch != null)
                    sb.AppendFormat(inv, "<path d=\"{0}\" fill=\"#5b8fbd\" stroke=\"#123\" stroke-width=\"0.25\"/>\n", PathOf(notch, f));
            }
            sb.AppendLine("</g>");

            // Names (unflipped so they read normally) at each pocket centre.
            foreach (var p in t.Pockets)
                sb.AppendFormat(inv, "<text x=\"{0}\" y=\"{1}\" font-size=\"3\" fill=\"#fff\" text-anchor=\"middle\" dominant-baseline=\"middle\">{2}</text>\n",
                    f(p.X), f(-p.Y), Escape(p.Name));
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
