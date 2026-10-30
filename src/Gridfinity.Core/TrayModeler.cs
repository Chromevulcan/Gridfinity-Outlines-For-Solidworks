using System;
using System.Collections.Generic;
using System.Linq;

namespace Gridfinity.Core
{
    /// <summary>Turns a (resolved, validated) <see cref="TrayDefinition"/> into a <see cref="BuildPlan"/>.</summary>
    public static class TrayModeler
    {
        private const double Overshoot = 0.2; // cuts run slightly past open faces to avoid coplanar faces

        public static List<Vec2> CellCenters(TrayDefinition t)
        {
            var list = new List<Vec2>();
            for (int i = 0; i < t.UnitsX; i++)
                for (int j = 0; j < t.UnitsY; j++)
                    list.Add(new Vec2((i - (t.UnitsX - 1) / 2.0) * GridfinitySpec.Pitch,
                                      (j - (t.UnitsY - 1) / 2.0) * GridfinitySpec.Pitch));
            return list;
        }

        public static BuildPlan CreatePlan(TrayDefinition t)
        {
            var plan = new BuildPlan
            {
                Name = t.Name,
                ExpectedWidth = t.OuterWidth,
                ExpectedDepth = t.OuterDepth,
                ExpectedHeight = t.TotalHeight
            };

            double W = t.OuterWidth, D = t.OuterDepth;
            var cells = CellCenters(t);

            // 1-3. Base profile under every cell: three drafted/straight extrudes.
            double bottomSize = GridfinitySpec.CellSize - 2 * (GridfinitySpec.BaseChamferLower + GridfinitySpec.BaseChamferUpper); // 35.6
            double midSize = bottomSize + 2 * GridfinitySpec.BaseChamferLower;                                                 // 37.2
            double bottomR = GridfinitySpec.BaseBottomCornerRadius;
            double midR = bottomR + GridfinitySpec.BaseChamferLower;

            plan.Features.Add(Boss("Base - lower chamfer", 0, GridfinitySpec.BaseChamferLower, 45,
                cells.Select(c => Shapes.RoundedRect(c.X, c.Y, bottomSize, bottomSize, bottomR))));
            plan.Features.Add(Boss("Base - vertical", GridfinitySpec.BaseChamferLower, GridfinitySpec.BaseVertical, 0,
                cells.Select(c => Shapes.RoundedRect(c.X, c.Y, midSize, midSize, midR))));
            plan.Features.Add(Boss("Base - upper chamfer", GridfinitySpec.BaseChamferLower + GridfinitySpec.BaseVertical,
                GridfinitySpec.BaseChamferUpper, 45,
                cells.Select(c => Shapes.RoundedRect(c.X, c.Y, midSize, midSize, midR))));

            // 4. Body block from the top of the base to the top of the walls (+ lip).
            plan.Features.Add(Boss("Body", GridfinitySpec.BaseHeight, t.TotalHeight - GridfinitySpec.BaseHeight, 0,
                new[] { Shapes.RoundedRect(0, 0, W, D, GridfinitySpec.BinCornerRadius) }));

            // 5. Cavity (open tray only), with optional dividers.
            if (t.Style == TrayStyle.OpenTray)
            {
                var cavity = CavityLoops(t);
                double depth = t.BodyTop - t.FloorHeight + (t.HasLip ? 0 : Overshoot);
                plan.Features.Add(Cut("Cavity", t.FloorHeight, depth, 0, cavity));
            }

            // 6. Pockets, grouped by depth so every group is one sketch with no overlapping contours.
            double clr = t.DefaultClearance;
            foreach (var g in t.Pockets.Where(p => p.Shape != PocketShape.Cradle).GroupBy(p => Math.Round(p.Depth, 2)).OrderByDescending(grp => grp.Key))
            {
                double depth = g.Key;
                string label = depth.ToString("0.##") + " mm";
                // Islands (Outline pockets with KeepIslands) are drawn inside their pocket in the same sketch, so
                // the cut goes around them and leaves them standing.
                plan.Features.Add(Cut("Pockets " + label, t.PocketTopZ - depth, depth + Overshoot, 0,
                    g.SelectMany(p => new[] { p.BuildLoop(clr) }.Concat(p.BuildIslandLoops(clr)))));

                var notches = g.Select(p => p.BuildNotchLoop(clr)).Where(l => l != null).ToList();
                if (notches.Count > 0)
                    plan.Features.Add(Cut("Finger notches " + label, t.PocketTopZ - depth, depth + Overshoot, 0, notches));
            }

            // 6a. Round-bottomed cradles: sketched on the Top plane and cut along Y. Cradles with the same Y span share a sketch.
            AddCradles(t, plan, clr);

            // 6b. Debossed labels, one text cut. Optional: if SolidWorks cannot make the text, the tray is still built.
            if (t.Labels.Count > 0)
            {
                var labels = new FeaturePlan
                {
                    Name = "Labels",
                    Op = FeatureOp.Cut,
                    StartZ = t.PocketTopZ - t.LabelDepth,
                    Depth = t.LabelDepth + Overshoot,
                    Optional = true
                };
                foreach (var l in t.Labels)
                    labels.Texts.Add(new TextPlan { Text = l.Text, X = l.X, Y = l.Y, HeightMm = l.HeightMm, Font = t.LabelFont, Bold = t.LabelBold });
                plan.Features.Add(labels);
            }

            // 7. Stacking lip: four cuts mirroring the base profile of a bin that would sit on top.
            if (t.HasLip)
            {
                double top = Math.Max(0.4, Math.Min(GridfinitySpec.LipUpperChamfer, t.LipTopInset));
                double z = t.BodyTop;
                var steps = new[]
                {
                    new { Name = "Lip - lower chamfer", Height = GridfinitySpec.LipLowerChamfer, Inset = GridfinitySpec.LipBottomInset, Draft = 45.0 },
                    new { Name = "Lip - vertical",      Height = GridfinitySpec.LipVertical,     Inset = GridfinitySpec.LipMidInset,    Draft = 0.0 },
                    new { Name = "Lip - upper chamfer", Height = GridfinitySpec.LipUpperChamfer - top, Inset = GridfinitySpec.LipMidInset, Draft = 45.0 },
                    new { Name = "Lip - top land",      Height = top,                            Inset = top,                           Draft = 0.0 }
                };

                for (int i = 0; i < steps.Length; i++)
                {
                    var s = steps[i];
                    if (s.Height < 1e-6) continue;
                    double r = Math.Max(0.4, GridfinitySpec.BinCornerRadius - s.Inset);
                    var loop = Shapes.RoundedRect(0, 0, W - 2 * s.Inset, D - 2 * s.Inset, r);
                    bool last = i == steps.Length - 1;
                    plan.Features.Add(Cut(s.Name, z, s.Height + (last ? Overshoot : 0), s.Draft, new[] { loop }));
                    z += s.Height;
                }
            }

            // 8. Magnet pockets in the underside of every cell.
            if (t.MagnetHoles)
            {
                var holes = new List<Loop>();
                double o = GridfinitySpec.MagnetOffset;
                foreach (var c in cells)
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sy in new[] { -1, 1 })
                            holes.Add(Loop.Circle(new Vec2(c.X + sx * o, c.Y + sy * o), GridfinitySpec.MagnetDiameter / 2));
                plan.Features.Add(Cut("Magnet pockets", 0, GridfinitySpec.MagnetDepth, 0, holes));
            }

            plan.Properties["Description"] = string.Format("Gridfinity {0}x{1}x{2} {3}", t.UnitsX, t.UnitsY, t.HeightUnits, t.Style);
            plan.Properties["PocketCount"] = t.Pockets.Count.ToString();
            return plan;
        }

        private static void AddCradles(TrayDefinition t, BuildPlan plan, double clr)
        {
            var cradles = t.Pockets.Where(p => p.Shape == PocketShape.Cradle).ToList();
            if (cradles.Count == 0) return;

            double topZ = t.PocketTopZ + Overshoot;
            var groups = cradles.GroupBy(p =>
            {
                double lo, hi;
                p.GetCradleYRange(clr, out lo, out hi);
                return Tuple.Create(Math.Round(lo, 2), Math.Round(hi, 2));
            }).OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2);

            foreach (var g in groups)
            {
                double lo = g.Key.Item1, hi = g.Key.Item2;
                var loops = g.Select(p => p.BuildCradleSection(clr, t.PocketTopZ - p.Depth, topZ)).ToList();
                string span = "Y " + lo.ToString("0.#") + " to " + hi.ToString("0.#");

                // The cut distance is measured from the Top plane (Y = 0) in either direction, so a cradle that
                // straddles Y = 0 is made of one cut each way.
                if (hi > 0)
                    plan.Features.Add(TopCut("Cradles " + span + " (+)", Math.Max(0, lo), hi - Math.Max(0, lo), false, loops));
                if (lo < 0)
                    plan.Features.Add(TopCut("Cradles " + span + " (-)", Math.Max(0, -hi), -lo - Math.Max(0, -hi), true, loops));
            }
        }

        private static FeaturePlan TopCut(string name, double start, double depth, bool negative, List<Loop> loops)
        {
            return new FeaturePlan { Name = name, Op = FeatureOp.Cut, Plane = SketchPlane.Top, NegativeSide = negative, StartZ = start, Depth = depth, Loops = loops };
        }

        private static List<Loop> CavityLoops(TrayDefinition t)
        {
            double innerW = t.OuterWidth - 2 * t.WallThickness;
            double innerD = t.OuterDepth - 2 * t.WallThickness;
            int nx = Math.Max(1, t.CompartmentsX), ny = Math.Max(1, t.CompartmentsY);
            double cw = (innerW - (nx - 1) * t.DividerThickness) / nx;
            double cd = (innerD - (ny - 1) * t.DividerThickness) / ny;
            double r = Math.Max(0.4, GridfinitySpec.BinCornerRadius - t.WallThickness);

            var loops = new List<Loop>();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    double cx = -innerW / 2 + cw / 2 + i * (cw + t.DividerThickness);
                    double cy = -innerD / 2 + cd / 2 + j * (cd + t.DividerThickness);
                    loops.Add(Shapes.RoundedRect(cx, cy, cw, cd, r));
                }
            return loops;
        }

        private static FeaturePlan Boss(string name, double z, double depth, double draft, IEnumerable<Loop> loops)
        {
            return new FeaturePlan { Name = name, Op = FeatureOp.Boss, StartZ = z, Depth = depth, DraftDegrees = draft, Loops = loops.ToList() };
        }

        private static FeaturePlan Cut(string name, double z, double depth, double draft, IEnumerable<Loop> loops)
        {
            return new FeaturePlan { Name = name, Op = FeatureOp.Cut, StartZ = z, Depth = depth, DraftDegrees = draft, Loops = loops.ToList() };
        }
    }
}
