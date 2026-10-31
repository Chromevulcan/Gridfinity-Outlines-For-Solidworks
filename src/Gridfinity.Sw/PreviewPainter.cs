using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Gridfinity.Core;

namespace Gridfinity.Sw
{
    /// <summary>
    /// Draws a to-scale top view of a tray: the bin and its cells, magnets, pockets (round bottoms shaded by depth),
    /// islands, notches, part centrelines, names as they will be debossed, and the seams where it is split to fit the
    /// printer. Shared by the import window and the Parts tab.
    /// </summary>
    internal static class PreviewPainter
    {
        public static void Paint(Graphics g, Control surface, TrayDefinition t, SplitResult split, Pocket selected, string emptyText)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (t == null)
            {
                g.DrawString(emptyText ?? "Nothing to show yet.", surface.Font, Brushes.Gray, 10, 10);
                return;
            }

            double W = t.OuterWidth, D = t.OuterDepth, pad = 10;
            float scale = (float)Math.Min((surface.Width - 2 * pad) / W, (surface.Height - 2 * pad) / D);
            if (scale <= 0) return;

            // Tray coordinates are centred with Y up; the panel's Y runs down.
            var state = g.Save();
            g.TranslateTransform(surface.Width / 2f, surface.Height / 2f);
            g.ScaleTransform(scale, -scale);

            double clr = t.DefaultClearance;
            using (var outline = new Pen(Color.FromArgb(68, 85, 102), 1.2f / scale))
            using (var thin = new Pen(Color.FromArgb(153, 170, 187), 0.8f / scale) { DashStyle = DashStyle.Dash })
            using (var magnet = new Pen(Color.FromArgb(187, 136, 136), 0.8f / scale))
            using (var edge = new Pen(Color.FromArgb(17, 34, 51), 0.8f / scale))
            using (var highlight = new Pen(Color.FromArgb(230, 120, 0), 2.2f / scale))
            using (var centreline = new Pen(Color.FromArgb(224, 138, 0), 1.0f / scale) { DashPattern = new[] { 8f, 3f, 2f, 3f } })
            using (var seam = new Pen(Color.FromArgb(198, 40, 40), 1.6f / scale) { DashStyle = DashStyle.Dash })
            using (var binFill = new SolidBrush(Color.FromArgb(233, 238, 243)))
            using (var pocketFill = new SolidBrush(Color.FromArgb(61, 110, 153)))
            using (var roundFill = new SolidBrush(Color.FromArgb(150, 61, 110, 153)))
            using (var notchFill = new SolidBrush(Color.FromArgb(91, 143, 189)))
            {
                var bin = new RectangleF((float)(-W / 2), (float)(-D / 2), (float)W, (float)D);
                g.FillRectangle(binFill, bin);
                g.DrawRectangle(outline, bin.X, bin.Y, bin.Width, bin.Height);

                foreach (var c in TrayModeler.CellCenters(t))
                {
                    g.DrawRectangle(thin, (float)(c.X - 18.6), (float)(c.Y - 18.6), 37.2f, 37.2f);
                    if (!t.MagnetHoles) continue;
                    float r = (float)(GridfinitySpec.MagnetDiameter / 2);
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sy in new[] { -1, 1 })
                            g.DrawEllipse(magnet, (float)(c.X + sx * GridfinitySpec.MagnetOffset) - r, (float)(c.Y + sy * GridfinitySpec.MagnetOffset) - r, 2 * r, 2 * r);
                }

                // A split piece shows only its share of parts that cross a seam.
                if (t.Piece != null)
                {
                    float o = (float)SplitPieceInfo.SeamOvercut;
                    g.SetClip(new RectangleF(bin.X - o, bin.Y - o, bin.Width + 2 * o, bin.Height + 2 * o));
                }

                int mostSteps = 1;
                foreach (var p in t.Pockets.Where(q => PartCuts.IsPart(q) && q.Bottom == PartBottom.Round))
                    mostSteps = Math.Max(mostSteps, SafeLayers(p, t).Length);

                foreach (var p in t.Pockets)
                {
                    bool round = PartCuts.IsPart(p) && p.Bottom == PartBottom.Round;
                    FillLoop(g, p.BuildLoop(clr), round ? (Brush)roundFill : pocketFill, edge);
                    if (round)
                    {
                        int alpha = Math.Max(4, Math.Min(60, 230 / mostSteps));
                        using (var step = new SolidBrush(Color.FromArgb(alpha, 11, 34, 56)))
                            foreach (var layer in SafeLayers(p, t).Skip(1))
                            {
                                using (var path = new GraphicsPath(FillMode.Alternate))
                                {
                                    foreach (var l in layer.Loops) AddLoop(path, l);
                                    g.FillPath(step, path);
                                }
                            }
                    }
                    foreach (var island in p.BuildIslandLoops(clr)) FillLoop(g, island, binFill, edge);
                    var notch = p.BuildNotchLoop(clr);
                    if (notch != null) FillLoop(g, notch, notchFill, edge);
                    if (ReferenceEquals(p, selected)) DrawLoop(g, p.BuildLoop(clr), highlight);

                    if (t.PartCenterlines && PartCuts.IsPart(p))
                    {
                        Vec2 a, b;
                        p.GetCenterline(clr, out a, out b);
                        g.DrawLine(centreline, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y);
                    }
                }
                g.ResetClip();

                if (split != null)
                {
                    foreach (double x in split.SeamsX) g.DrawLine(seam, (float)x, (float)(-D / 2 - 3), (float)x, (float)(D / 2 + 3));
                    foreach (double y in split.SeamsY) g.DrawLine(seam, (float)(-W / 2 - 3), (float)y, (float)(W / 2 + 3), (float)y);
                }
            }
            g.Restore(state);

            // Text, drawn unflipped: names as debossed (along each part), pocket names for the rest, and tray labels.
            Func<double, double, PointF> toScreen = (x, y) => new PointF(surface.Width / 2f + (float)x * scale, surface.Height / 2f - (float)y * scale);
            using (var small = new Font(surface.Font.FontFamily, 8f))
            using (var label = new SolidBrush(Color.FromArgb(170, 40, 40)))
            {
                foreach (TextPlan tp in TrayModeler.PartLabels(t))
                {
                    float px = Math.Max(5f, (float)tp.HeightMm * scale);
                    using (var f = new Font(surface.Font.FontFamily, px, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        var s = g.Save();
                        PointF at = toScreen(tp.X, tp.Y);
                        g.TranslateTransform(at.X, at.Y);
                        g.RotateTransform((float)-tp.AngleDeg);
                        g.DrawString(tp.Text, f, label, 0, -px * 0.6f);
                        g.Restore(s);
                    }
                }
                var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                foreach (var p in t.Pockets.Where(q => !q.HasLabel)) g.DrawString(p.Name, small, Brushes.White, toScreen(p.X, p.Y), centre);
                foreach (var l in t.Labels) g.DrawString(l.Text, small, Brushes.DarkRed, toScreen(l.X, l.Y + l.HeightMm / 2));
            }
        }

        private static CutLayer[] SafeLayers(Pocket p, TrayDefinition t)
        {
            try { return PartCuts.Layers(p, t).ToArray(); }
            catch { return new CutLayer[0]; }
        }

        private static void AddLoop(GraphicsPath path, Loop loop)
        {
            var pts = loop.ToPolyline(4.0).Select(v => new PointF((float)v.X, (float)v.Y)).ToArray();
            if (pts.Length >= 3) path.AddPolygon(pts);
        }

        public static void FillLoop(Graphics g, Loop loop, Brush fill, Pen edge)
        {
            var pts = loop.ToPolyline(4.0).Select(v => new PointF((float)v.X, (float)v.Y)).ToArray();
            if (pts.Length < 3) return;
            g.FillPolygon(fill, pts);
            g.DrawPolygon(edge, pts);
        }

        private static void DrawLoop(Graphics g, Loop loop, Pen pen)
        {
            var pts = loop.ToPolyline(4.0).Select(v => new PointF((float)v.X, (float)v.Y)).ToArray();
            if (pts.Length >= 3) g.DrawPolygon(pen, pts);
        }
    }

    internal sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }
}
