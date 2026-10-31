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

            // 6. Pockets and parts, as cut layers grouped by depth so every group is one sketch with no overlapping
            // contours. A flat pocket is one layer; a round-bottomed part is a stack of steps (see PartCuts).
            double clr = t.DefaultClearance;
            var layers = t.Pockets.Where(p => p.Shape != PocketShape.Cradle).SelectMany(p => PartCuts.Layers(p, t)).ToList();
            foreach (var g in layers.GroupBy(l => Math.Round(l.Depth, 2)).OrderByDescending(grp => grp.Key))
            {
                double depth = g.Key;
                string label = depth.ToString("0.##") + " mm";
                // Islands (Outline pockets with KeepIslands) are drawn inside their pocket in the same sketch, so
                // the cut goes around them and leaves them standing.
                var loops = Clip(t, g.SelectMany(l => l.Loops));
                if (loops.Count > 0)
                    plan.Features.Add(Cut("Pockets " + label, t.PocketTopZ - depth, depth + Overshoot, 0, loops));
            }

            // Finger notches go as deep as the deepest point of their pocket.
            foreach (var g in t.Pockets.Where(p => p.Shape != PocketShape.Cradle && p.NotchDiameter > 0)
                                       .Select(p => new { P = p, D = PartCuts.MaxCutDepth(p, t) })
                                       .Where(x => x.D > 1e-6)
                                       .GroupBy(x => Math.Round(x.D, 2)).OrderByDescending(grp => grp.Key))
            {
                var notches = Clip(t, g.Select(x => x.P.BuildNotchLoop(clr)).Where(l => l != null));
                if (notches.Count > 0)
                    plan.Features.Add(Cut("Finger notches " + g.Key.ToString("0.##") + " mm", t.PocketTopZ - g.Key, g.Key + Overshoot, 0, notches));
            }

            // 6a. Round-bottomed cradles: sketched on the Top plane and cut along Y. Cradles with the same Y span share a sketch.
            AddCradles(t, plan, clr);

            // 6b. Debossed labels and part names, one text cut. Optional: if SolidWorks cannot make the text, the tray is still built.
            var partLabels = PartLabels(t);
            if (t.Labels.Count > 0 || partLabels.Count > 0)
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
                labels.Texts.AddRange(partLabels);
                plan.Features.Add(labels);
            }

            // 6c. Reference sketch: every part's centreline, the line its name sits on and its outline, on a plane at
            // the part tops. Nothing is cut; it is there for lining up your own text or features.
            if (t.PartCenterlines)
            {
                var parts = t.Pockets.Where(PartCuts.IsPart).ToList();
                if (parts.Count > 0)
                {
                    var sketch = new FeaturePlan
                    {
                        Name = "Part centerlines",
                        Op = FeatureOp.Sketch,
                        SketchZ = t.PocketTopZ + t.PartTopOffset,
                        LoopsAsConstruction = true,
                        Optional = true
                    };
                    foreach (var p in parts)
                    {
                        Vec2 a, b;
                        p.GetCenterline(clr, out a, out b);
                        if (InClip(t, a, b)) sketch.Lines.Add(new LinePlan { Start = a, End = b, Name = p.Name });
                        if (p.GetLabelLine(clr, out a, out b) && InClip(t, a, b))
                            sketch.Lines.Add(new LinePlan { Start = a, End = b, Name = p.Name + " text" });
                    }
                    sketch.Loops = Clip(t, parts.Select(p => p.BuildLoop(clr)));
                    if (sketch.Lines.Count > 0) plan.Features.Add(sketch);
                }
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

        /// <summary>
        /// Names of the parts as sketch text: centred on the line beside each part, running along it and reading left
        /// to right. Character height is reduced if the name would be longer than the part.
        /// </summary>
        public static List<TextPlan> PartLabels(TrayDefinition t)
        {
            var list = new List<TextPlan>();
            foreach (var p in t.Pockets.Where(q => q.HasLabel))
            {
                Vec2 a, b;
                if (!p.GetLabelLine(t.DefaultClearance, out a, out b)) continue;
                Vec2 mid = (a + b) * 0.5;
                if (t.Piece != null && !t.Piece.Contains(mid)) continue; // a split tray puts each name on one piece
                double len = (b - a).Length;
                string text = p.LabelText.Trim();
                double h = p.LabelHeight;
                double width = EstimateTextWidth(text, h);
                if (width > len && len > 1) { h = Math.Max(2.5, h * len / width); width = EstimateTextWidth(text, h); }
                Vec2 dir = len > 1e-9 ? (b - a) * (1.0 / len) : new Vec2(1, 0);
                Vec2 start = mid - dir * (width / 2);
                list.Add(new TextPlan
                {
                    Text = text,
                    X = start.X,
                    Y = start.Y,
                    AngleDeg = Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI,
                    HeightMm = h,
                    Font = t.LabelFont,
                    Bold = t.LabelBold
                });
            }
            return list;
        }

        /// <summary>Same generous width estimate as <see cref="Label.EstimatedWidth"/>.</summary>
        public static double EstimateTextWidth(string text, double height) { return (text ?? "").Length * height * 0.7; }

        // Split trays: keep only what lies within this piece (plus a little past its seams, so cuts run out cleanly).
        private static List<Loop> Clip(TrayDefinition t, IEnumerable<Loop> loops)
        {
            if (t.Piece == null) return loops.ToList();
            var result = new List<Loop>();
            double hx = t.OuterWidth / 2 + SplitPieceInfo.SeamOvercut, hy = t.OuterDepth / 2 + SplitPieceInfo.SeamOvercut;
            foreach (var loop in loops)
            {
                Vec2 lo, hi;
                loop.GetBounds(out lo, out hi);
                if (lo.X >= -hx && hi.X <= hx && lo.Y >= -hy && hi.Y <= hy) { result.Add(loop); continue; }
                if (hi.X <= -hx || lo.X >= hx || hi.Y <= -hy || lo.Y >= hy) continue;
                foreach (var piece in OutlineGeometry.ClipToRect(loop.ToPolyline(3.0), -hx, -hy, hx, hy))
                    result.Add(OutlineGeometry.ToLoop(piece));
            }
            return result;
        }

        private static bool InClip(TrayDefinition t, Vec2 a, Vec2 b)
        {
            return t.Piece == null || t.Piece.Contains((a + b) * 0.5);
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
