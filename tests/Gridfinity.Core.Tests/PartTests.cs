using System;
using System.Collections.Generic;
using System.Linq;
using Gridfinity.Core;
using Xunit;

namespace Gridfinity.Core.Tests
{
    public class PartTests
    {
        private static List<Vec2> Rect(double w, double h)
        {
            return new List<Vec2> { new Vec2(-w / 2, -h / 2), new Vec2(w / 2, -h / 2), new Vec2(w / 2, h / 2), new Vec2(-w / 2, h / 2) };
        }

        private static TrayDefinition Tray(params Pocket[] pockets)
        {
            var t = new TrayDefinition { UnitsX = 3, UnitsY = 2, HeightUnits = 6, AutoLayout = false, DefaultClearance = 0, PartCenterlines = true };
            t.Pockets.AddRange(pockets);
            return t;
        }

        private static Pocket Part(List<Vec2> pts, PartBottom bottom = PartBottom.Flat, double depth = 8)
        {
            return new Pocket { Name = "part", Shape = PocketShape.Outline, Points = pts, Depth = depth, Bottom = bottom };
        }

        // ---- Y offset (flat parts) ---------------------------------------------------------------

        [Fact]
        public void Flat_part_is_cut_its_thickness_less_the_y_offset()
        {
            var t = Tray(Part(Rect(60, 20), PartBottom.Flat, 8));
            t.PartTopOffset = 3;
            Assert.Equal(5.0, PartCuts.MaxCutDepth(t.Pockets[0], t), 6);
            var cut = TrayModeler.CreatePlan(t).Features.Single(f => f.Name.StartsWith("Pockets"));
            Assert.Equal(t.PocketTopZ - 5.0, cut.StartZ, 6);
        }

        [Fact]
        public void A_part_override_beats_the_tray_offset_and_a_part_above_the_surface_cuts_nothing()
        {
            var p = Part(Rect(60, 20), PartBottom.Flat, 4);
            p.TopOffset = 6;
            var t = Tray(p);
            t.PartTopOffset = 1;
            Assert.True(PartCuts.MaxCutDepth(p, t) < 0);
            Assert.DoesNotContain(TrayModeler.CreatePlan(t).Features, f => f.Name.StartsWith("Pockets"));
            Assert.Contains(TrayValidator.Validate(t).Warnings, i => i.Message.Contains("nothing is cut"));
        }

        [Fact]
        public void Negative_offset_sinks_the_part()
        {
            var t = Tray(Part(Rect(60, 20), PartBottom.Flat, 6));
            t.PartTopOffset = -2;
            Assert.Equal(8.0, PartCuts.MaxCutDepth(t.Pockets[0], t), 6);
        }

        // ---- Round bottoms -------------------------------------------------------------------------

        [Fact]
        public void A_round_bar_is_as_thick_as_it_is_wide()
        {
            var p = Part(Rect(100, 12), PartBottom.Round);
            var t = Tray(p);
            var f = PartCuts.Field(p, t);
            Assert.InRange(f.Max, 11.4, 12.1);
        }

        [Fact]
        public void A_tapered_part_is_deeper_where_it_is_wider()
        {
            // A screwdriver: 24 mm handle, 6 mm shaft.
            var pts = new List<Vec2> { new Vec2(-60, -12), new Vec2(0, -12), new Vec2(0, -3), new Vec2(60, -3), new Vec2(60, 3), new Vec2(0, 3), new Vec2(0, 12), new Vec2(-60, 12) };
            var p = Part(pts, PartBottom.Round);
            var t = Tray(p);
            t.HeightUnits = 7;
            var layers = PartCuts.Layers(p, t);
            double deepest = layers.Max(l => l.Depth);
            Assert.InRange(deepest, 23.0, 24.5);

            // Under the shaft (x = 40) only the layers down to about 6 mm are present.
            var shaft = new Vec2(40, 0);
            double shaftDepth = layers.Where(l => l.Loops.Any(lp => GeometryUtil.PointInPolygon(shaft, lp.ToPolyline()))).Max(l => l.Depth);
            Assert.InRange(shaftDepth, 5.6, 6.8);
        }

        [Fact]
        public void Steps_are_never_shallower_than_the_part()
        {
            var p = Part(Rect(80, 16), PartBottom.Round);
            var t = Tray(p);
            t.RoundStep = 0.5;
            var f = PartCuts.Field(p, t);
            var layers = PartCuts.Layers(p, t);
            // Sample points across the bar: the cut at each point must reach the part's bottom.
            for (double y = -7.9; y <= 7.9; y += 0.1)
            {
                var q = new Vec2(0, y);
                double cut = layers.Where(l => l.Loops.Any(lp => GeometryUtil.PointInPolygon(q, lp.ToPolyline()))).Select(l => l.Depth).DefaultIfEmpty(0).Max();
                double r = 8, need = r + Math.Sqrt(Math.Max(0, r * r - y * y));
                Assert.True(cut >= need - 0.05, "at y=" + y + " cut " + cut + " < " + need);
            }
            Assert.True(f.Max <= 16.1);
        }

        [Fact]
        public void Round_thickness_limit_caps_the_depth()
        {
            var p = Part(Rect(100, 20), PartBottom.Round);
            p.RoundMaxThickness = 8;
            var t = Tray(p);
            Assert.InRange(PartCuts.MaxCutDepth(p, t), 8.0, 8.4);
        }

        [Fact]
        public void Round_part_steps_respect_the_y_offset()
        {
            var p = Part(Rect(100, 12), PartBottom.Round);
            var t = Tray(p);
            t.PartTopOffset = 4;
            double max = PartCuts.MaxCutDepth(p, t);
            Assert.InRange(max, 7.2, 8.4);
            var layers = PartCuts.Layers(p, t);
            Assert.Equal(max, layers.Max(l => l.Depth), 6);
            Assert.True(TrayValidator.Validate(t).IsValid, TrayValidator.Validate(t).ToString());
        }

        [Fact]
        public void Each_step_depth_is_one_cut_feature()
        {
            var a = Part(Rect(80, 10), PartBottom.Round); a.Y = -20;
            var b = Part(Rect(80, 10), PartBottom.Round); b.Y = 20; b.Name = "b";
            var t = Tray(a, b);
            var cuts = TrayModeler.CreatePlan(t).Features.Where(f => f.Name.StartsWith("Pockets")).ToList();
            Assert.Equal(cuts.Count, cuts.Select(c => c.Name).Distinct().Count());
            Assert.All(cuts, c => Assert.True(c.Loops.Count >= 2));
        }

        // ---- Centrelines and names ---------------------------------------------------------------

        [Fact]
        public void Centreline_runs_end_to_end_through_the_middle_and_turns_with_the_part()
        {
            var p = Part(Rect(60, 20));
            p.X = 10; p.Y = 5; p.RotationDeg = 90;
            var t = Tray(p);
            Vec2 a, b;
            p.GetCenterline(0, out a, out b);
            Assert.Equal(10, a.X, 6); Assert.Equal(10, b.X, 6);
            Assert.Equal(60, Math.Abs(b.Y - a.Y), 6);
            var sketch = TrayModeler.CreatePlan(t).Features.Single(f => f.Op == FeatureOp.Sketch);
            Assert.Single(sketch.Lines);
            Assert.Equal(t.PocketTopZ + t.PartTopOffset, sketch.SketchZ, 6);
        }

        [Fact]
        public void Name_sits_beside_the_part_and_reads_left_to_right()
        {
            var p = Part(Rect(60, 20));
            p.LabelText = "10 mm";
            p.RotationDeg = 180; // upside down: the text must still read left to right
            var t = Tray(p);
            Vec2 a, b;
            Assert.True(p.GetLabelLine(0, out a, out b));
            Assert.True(b.X > a.X);
            // Below the part before turning = above it after 180 degrees: 10 + gap + half the text.
            Assert.Equal(10 + Pocket.LabelGap + p.LabelHeight / 2, a.Y, 6);

            var text = TrayModeler.PartLabels(t).Single();
            Assert.Equal(0, text.AngleDeg, 6);
            Assert.True(TrayModeler.CreatePlan(t).Features.Any(f => f.Name == "Labels" && f.Texts.Count == 1));

            // Layout leaves room for the name.
            Vec2 lo, hi;
            p.GetLocalBounds(0, 0, out lo, out hi);
            Assert.True(hi.Y - lo.Y >= 20 + Pocket.LabelGap + p.LabelHeight - 1e-6);
        }

        [Fact]
        public void Name_along_a_vertical_part_reads_bottom_to_top()
        {
            var p = Part(Rect(60, 20));
            p.LabelText = "Hex";
            p.RotationDeg = 270;
            var text = TrayModeler.PartLabels(Tray(p)).Single();
            Assert.Equal(90, text.AngleDeg, 6);
        }

        // ---- Splitting -------------------------------------------------------------------------

        [Fact]
        public void Units_that_fit_a_bed()
        {
            Assert.Equal(6, PrinterProfiles.UnitsThatFit(256, 2));
            Assert.Equal(5, PrinterProfiles.UnitsThatFit(256, 3)); // 6 units are 251.5 mm
            Assert.Equal(4, PrinterProfiles.UnitsThatFit(180, 2));
            Assert.Equal(5, PrinterProfiles.UnitsThatFit(220, 2));
        }

        [Fact]
        public void A_small_tray_is_not_split()
        {
            var t = Tray(Part(Rect(60, 20)));
            Assert.False(TraySplitter.Split(t).WasSplit);
        }

        [Fact]
        public void An_oversized_tray_splits_on_cell_lines_into_pieces_that_fit_and_keep_every_part()
        {
            var t = new TrayDefinition { UnitsX = 10, UnitsY = 3, HeightUnits = 5, AutoLayout = false, DefaultClearance = 0, Printer = "Bambu Lab A1 mini (180 x 180)" };
            // A part straddling x = 0 (the 5-unit line) and one clear of every line.
            var a = Part(Rect(30, 10)); a.Name = "a"; a.X = 0; a.Y = 0;
            var b = Part(Rect(20, 10)); b.Name = "b"; b.X = -147; b.Y = 0;
            t.Pockets.Add(a); t.Pockets.Add(b);

            var r = TraySplitter.Split(t);
            Assert.True(r.WasSplit);
            Assert.All(r.Pieces, p => Assert.True(p.UnitsX <= 4 && p.UnitsY <= 4));
            Assert.Equal(10, r.Pieces.Where(p => p.Piece.Row == 0).Sum(p => p.UnitsX));
            Assert.Equal(3, r.Pieces.Count); // 4 + 3 + 3 (or similar) x 3 rows of one
            // The seam avoids the part at x = 0 (a line at 0 would cross it).
            Assert.DoesNotContain(r.SeamsX, s => Math.Abs(s) < 1);
            // Pieces are valid bins and get names A1, B1, C1.
            Assert.Equal(new[] { "_A1", "_B1", "_C1" }, r.Pieces.Select(p => p.Name.Substring(t.Name.Length)));
            foreach (var piece in r.Pieces)
                Assert.True(TrayValidator.Validate(piece).IsValid, TrayValidator.Validate(piece).ToString());
            Assert.Equal(2, r.Pieces.Sum(p => p.Pockets.Count));
        }

        [Fact]
        public void A_part_that_must_cross_a_seam_is_cut_in_both_pieces()
        {
            var t = new TrayDefinition { UnitsX = 8, UnitsY = 1, HeightUnits = 5, AutoLayout = false, DefaultClearance = 0, Printer = "Bambu Lab A1 mini (180 x 180)" };
            var bar = Part(Rect(300, 10)); bar.Name = "bar";
            t.Pockets.Add(bar);
            var r = TraySplitter.Split(t);
            Assert.Equal(2, r.Pieces.Count);
            foreach (var piece in r.Pieces)
            {
                Assert.Single(piece.Pockets);
                var cut = TrayModeler.CreatePlan(piece).Features.Single(f => f.Name.StartsWith("Pockets"));
                Vec2 lo, hi;
                cut.Loops[0].GetBounds(out lo, out hi);
                Assert.True(hi.X <= piece.OuterWidth / 2 + SplitPieceInfo.SeamOvercut + 1e-6);
                Assert.True(lo.X >= -piece.OuterWidth / 2 - SplitPieceInfo.SeamOvercut - 1e-6);
            }
            Assert.Contains(r.Notes, n => n.Contains("continues in the next piece"));
        }
    }
}
