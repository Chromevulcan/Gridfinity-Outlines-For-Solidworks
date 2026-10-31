using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Gridfinity.Core
{
    /// <summary>A printer bed, mm.</summary>
    public sealed class PrinterProfile
    {
        public string Name;
        public double BedWidth;
        public double BedDepth;
        public PrinterProfile(string name, double w, double d) { Name = name; BedWidth = w; BedDepth = d; }
        public override string ToString() { return Name; }
    }

    /// <summary>Common printers and their bed sizes (manufacturers' published build volumes; see the README).</summary>
    public static class PrinterProfiles
    {
        public const string CustomName = "Custom";
        public const string DefaultName = "Bambu Lab P1S / X1C / A1 (256 x 256)";

        public static readonly PrinterProfile[] All =
        {
            new PrinterProfile("Bambu Lab A1 mini (180 x 180)", 180, 180),
            new PrinterProfile("Prusa MINI+ (180 x 180)", 180, 180),
            new PrinterProfile("Creality Ender-3 V3 SE / K1 / K1C (220 x 220)", 220, 220),
            new PrinterProfile("Elegoo Neptune 4 Pro (225 x 225)", 225, 225),
            new PrinterProfile("Prusa MK4S (250 x 210)", 250, 210),
            new PrinterProfile("Prusa CORE One (250 x 220)", 250, 220),
            new PrinterProfile(DefaultName, 256, 256),
            new PrinterProfile("Creality K1 Max (300 x 300)", 300, 300),
            new PrinterProfile("Prusa CORE One L (300 x 300)", 300, 300),
            new PrinterProfile("Bambu Lab H2D, one nozzle (325 x 320)", 325, 320),
            new PrinterProfile("Example 350 x 350 (Voron-class)", 350, 350),
        };

        public static IEnumerable<string> Names { get { return All.Select(p => p.Name).Concat(new[] { CustomName }); } }

        /// <summary>The bed for a tray's printer setting (Custom, or an unknown name, uses the custom size).</summary>
        public static void BedFor(TrayDefinition t, out double width, out double depth)
        {
            var p = All.FirstOrDefault(x => string.Equals(x.Name, t.Printer, StringComparison.OrdinalIgnoreCase));
            if (p == null)
            {
                // Accept a short form such as "A1 mini" too.
                p = All.FirstOrDefault(x => !string.IsNullOrWhiteSpace(t.Printer) &&
                                            x.Name.IndexOf(t.Printer.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            }
            width = p != null ? p.BedWidth : t.CustomBedWidth;
            depth = p != null ? p.BedDepth : t.CustomBedDepth;
        }

        /// <summary>Most whole Gridfinity units that fit along a bed edge, leaving the margin at each end.</summary>
        public static int UnitsThatFit(double bedLength, double margin)
        {
            return Math.Max(0, (int)Math.Floor((bedLength - 2 * margin + GridfinitySpec.FitClearance) / GridfinitySpec.Pitch + 1e-9));
        }
    }

    /// <summary>Drop-down list of printers in the property grid; any other name is kept as typed.</summary>
    public sealed class PrinterNameConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return false; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(PrinterProfiles.Names.ToList());
        }
    }

    /// <summary>Where a split piece came from.</summary>
    public sealed class SplitPieceInfo
    {
        /// <summary>How far past a seam the cuts run, mm, so a part that crosses it is cut cleanly through the side.</summary>
        public const double SeamOvercut = 2.0;

        public string Parent;
        public int Column;        // 0 = left (-X)
        public int Row;           // 0 = front (-Y)
        public int Columns;
        public int Rows;
        /// <summary>Centre of this piece in the coordinates of the whole tray, mm.</summary>
        public double OffsetX;
        public double OffsetY;
        public bool SeamLeft, SeamRight, SeamFront, SeamBack;
        /// <summary>Half the grid span of the piece (units x 21 mm), for deciding which piece owns a point.</summary>
        public double HalfSpanX, HalfSpanY;

        /// <summary>True when a point (piece coordinates) belongs to this piece; points on a seam go to the piece on its +X/+Y side.</summary>
        public bool Contains(Vec2 q)
        {
            bool inX = (q.X >= -HalfSpanX || !SeamLeft) && (q.X < HalfSpanX || !SeamRight);
            bool inY = (q.Y >= -HalfSpanY || !SeamFront) && (q.Y < HalfSpanY || !SeamBack);
            return inX && inY;
        }

        public string Label { get { return ((char)('A' + Column)).ToString() + (Row + 1); } }
    }

    public sealed class SplitResult
    {
        public List<TrayDefinition> Pieces = new List<TrayDefinition>();
        public List<string> Notes = new List<string>();
        /// <summary>Seam positions in the whole tray's coordinates, mm (for previews).</summary>
        public List<double> SeamsX = new List<double>();
        public List<double> SeamsY = new List<double>();
        public bool WasSplit { get { return Pieces.Count > 1; } }
    }

    /// <summary>
    /// Splits a laid-out tray that is larger than the printer bed into Gridfinity bins that each fit. Seams run along
    /// cell lines, so every piece is a whole number of units and sits in a baseplate like any bin. Seams are placed
    /// to cross as few parts as possible; a part that has to cross one continues in the next piece.
    /// </summary>
    public static class TraySplitter
    {
        public static SplitResult Split(TrayDefinition t)
        {
            var result = new SplitResult();
            if (t.Piece != null || !t.SplitToFitPrinter) { result.Pieces.Add(t); return result; }

            double bedW, bedD;
            PrinterProfiles.BedFor(t, out bedW, out bedD);
            int fitW = PrinterProfiles.UnitsThatFit(bedW, t.BedMargin);
            int fitD = PrinterProfiles.UnitsThatFit(bedD, t.BedMargin);
            if (fitW < 1 || fitD < 1)
            {
                result.Notes.Add("The printer bed (" + bedW + " x " + bedD + " mm, " + t.BedMargin + " mm margin) is smaller than one Gridfinity unit; the tray was not split.");
                result.Pieces.Add(t);
                return result;
            }

            // A piece may be turned on the bed, so try both ways round and keep the one with fewer pieces.
            int ax = t.UnitsX, ay = t.UnitsY;
            int kx1 = Ceil(ax, fitW), ky1 = Ceil(ay, fitD);
            int kx2 = Ceil(ax, fitD), ky2 = Ceil(ay, fitW);
            int maxX, maxY;
            if (kx2 * ky2 < kx1 * ky1) { maxX = fitD; maxY = fitW; }
            else { maxX = fitW; maxY = fitD; }
            int kx = Ceil(ax, maxX), ky = Ceil(ay, maxY);

            if (kx * ky == 1) { result.Pieces.Add(t); return result; }

            var xs = ChooseCuts(t, true, ax, kx, maxX);
            var ys = ChooseCuts(t, false, ay, ky, maxY);
            double pitch = GridfinitySpec.Pitch;
            double x0 = -ax * pitch / 2, y0 = -ay * pitch / 2;
            foreach (int c in xs.Skip(1).Take(xs.Count - 2)) result.SeamsX.Add(x0 + c * pitch);
            foreach (int c in ys.Skip(1).Take(ys.Count - 2)) result.SeamsY.Add(y0 + c * pitch);

            result.Notes.Add(string.Format("{0} x {1} units is larger than the {2} bed ({3} x {4} mm with a {5} mm margin fits {6} x {7} units); split into {8} pieces ({9} x {10}).",
                ax, ay, t.Printer, bedW, bedD, t.BedMargin, fitW, fitD, kx * ky, kx, ky));

            double clr = t.DefaultClearance;
            for (int i = 0; i < kx; i++)
            for (int j = 0; j < ky; j++)
            {
                int ux = xs[i + 1] - xs[i], uy = ys[j + 1] - ys[j];
                double cx = x0 + (xs[i] + xs[i + 1]) / 2.0 * pitch;
                double cy = y0 + (ys[j] + ys[j + 1]) / 2.0 * pitch;
                var info = new SplitPieceInfo
                {
                    Parent = t.Name, Column = i, Row = j, Columns = kx, Rows = ky, OffsetX = cx, OffsetY = cy,
                    SeamLeft = i > 0, SeamRight = i < kx - 1, SeamFront = j > 0, SeamBack = j < ky - 1,
                    HalfSpanX = ux * pitch / 2, HalfSpanY = uy * pitch / 2
                };

                var piece = t.Clone();
                piece.Name = t.Name + "_" + info.Label;
                piece.UnitsX = ux;
                piece.UnitsY = uy;
                piece.AutoLayout = false;
                piece.AutoGrowGrid = false;
                piece.Piece = info;
                piece.Pockets.Clear();
                piece.Labels.Clear();

                double hx = piece.OuterWidth / 2 + SplitPieceInfo.SeamOvercut, hy = piece.OuterDepth / 2 + SplitPieceInfo.SeamOvercut;
                for (int k = 0; k < t.Pockets.Count; k++)
                {
                    var src = t.Pockets[k];
                    Vec2 lo, hi;
                    PocketBounds(src, clr, out lo, out hi);
                    if (hi.X - cx <= -hx || lo.X - cx >= hx || hi.Y - cy <= -hy || lo.Y - cy >= hy) continue;
                    var p = ClonePocket(t, k);
                    p.X -= cx;
                    p.Y -= cy;
                    piece.Pockets.Add(p);
                }
                foreach (var l in t.Labels)
                {
                    var q = new Vec2(l.X + l.EstimatedWidth / 2 - cx, l.Y - cy);
                    if (!info.Contains(q)) continue;
                    piece.Labels.Add(new Label { Text = l.Text, X = l.X - cx, Y = l.Y - cy, HeightMm = l.HeightMm });
                }
                result.Pieces.Add(piece);
            }

            // Report the parts that cross a seam.
            foreach (var p in t.Pockets)
            {
                Vec2 lo, hi;
                PocketBounds(p, clr, out lo, out hi);
                bool crosses = result.SeamsX.Any(s => lo.X < s && hi.X > s) || result.SeamsY.Any(s => lo.Y < s && hi.Y > s);
                if (!crosses) continue;
                if (p.Shape == PocketShape.Cradle)
                    result.Notes.Add("Cradle '" + p.Name + "' crosses a seam; cradles cannot be split. Move it or choose a larger printer.");
                else if (t.Style == TrayStyle.OpenTray)
                    result.Notes.Add("'" + p.Name + "' crosses a seam in an open tray; the wall of each piece will stand in its way. Move it, or use a solid block.");
                else
                    result.Notes.Add("'" + p.Name + "' crosses a seam and continues in the next piece.");
            }
            return result;
        }

        private static Pocket ClonePocket(TrayDefinition t, int index)
        {
            // A JSON round trip, the same way TrayDefinition.Clone copies everything.
            var holder = new TrayDefinition();
            holder.Pockets.Add(t.Pockets[index]);
            return holder.Clone().Pockets[0];
        }

        private static int Ceil(int a, int b) { return (a + b - 1) / b; }

        /// <summary>The pocket's footprint plus its notch and its name, placed.</summary>
        public static void PocketBounds(Pocket p, double clr, out Vec2 lo, out Vec2 hi)
        {
            p.BuildLoop(clr).GetBounds(out lo, out hi);
            var extra = new List<Vec2>();
            var notch = p.BuildNotchLoop(clr);
            if (notch != null) { Vec2 a, b; notch.GetBounds(out a, out b); extra.Add(a); extra.Add(b); }
            var band = p.BuildLabelBand(clr);
            if (band != null) extra.AddRange(band);
            foreach (var q in extra)
            {
                lo = new Vec2(Math.Min(lo.X, q.X), Math.Min(lo.Y, q.Y));
                hi = new Vec2(Math.Max(hi.X, q.X), Math.Max(hi.Y, q.Y));
            }
        }

        /// <summary>
        /// Cell indices of the cuts along one axis (including 0 and the end): k segments, none longer than max,
        /// crossing as few parts as possible, and as even as possible otherwise.
        /// </summary>
        private static List<int> ChooseCuts(TrayDefinition t, bool alongX, int units, int k, int max)
        {
            double pitch = GridfinitySpec.Pitch, origin = -units * pitch / 2, clr = t.DefaultClearance;
            var spans = t.Pockets.Select(p =>
            {
                Vec2 lo, hi;
                PocketBounds(p, clr, out lo, out hi);
                return new { Lo = alongX ? lo.X : lo.Y, Hi = alongX ? hi.X : hi.Y, Weight = p.Shape == PocketShape.Cradle ? 1000.0 : 1.0 };
            }).ToList();

            // Cost of a seam at each inner cell line.
            var cost = new double[units + 1];
            for (int c = 1; c < units; c++)
            {
                double s = origin + c * pitch;
                cost[c] = spans.Where(x => x.Lo < s && x.Hi > s).Sum(x => x.Weight);
            }

            double ideal = units / (double)k;
            const double INF = double.PositiveInfinity;
            var best = new double[k + 1, units + 1];
            var from = new int[k + 1, units + 1];
            for (int a = 0; a <= k; a++) for (int b = 0; b <= units; b++) best[a, b] = INF;
            best[0, 0] = 0;
            for (int seg = 1; seg <= k; seg++)
            for (int end = 1; end <= units; end++)
            for (int len = 1; len <= Math.Min(max, end); len++)
            {
                int start = end - len;
                if (double.IsInfinity(best[seg - 1, start])) continue;
                double v = best[seg - 1, start] + (end < units ? cost[end] : 0) + 0.01 * (len - ideal) * (len - ideal);
                if (v < best[seg, end]) { best[seg, end] = v; from[seg, end] = start; }
            }

            var cuts = new List<int> { units };
            int e = units;
            for (int seg = k; seg >= 1; seg--) { e = from[seg, e]; cuts.Add(e); }
            cuts.Reverse();
            return cuts;
        }
    }
}
