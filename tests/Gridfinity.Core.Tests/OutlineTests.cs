using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gridfinity.Core;
using Xunit;

namespace Gridfinity.Core.Tests
{
    public class OutlineTests
    {
        private static string Sample(string name)
        {
            return Path.Combine(AppContext.BaseDirectory, "outlines", name);
        }

        private static List<Vec2> LShape()
        {
            // 60 x 20 L: concave, so the old convex-only Polygon could not hold it.
            return new List<Vec2> { new Vec2(-30, -10), new Vec2(30, -10), new Vec2(30, 0), new Vec2(-20, 0), new Vec2(-20, 10), new Vec2(-30, 10) };
        }

        private static Pocket OutlinePocket(List<Vec2> pts, double depth = 8)
        {
            return new Pocket { Name = "shape", Shape = PocketShape.Outline, Points = pts, Depth = depth };
        }

        // ---- Geometry ---------------------------------------------------------------------------------

        [Fact]
        public void Clearance_grows_an_outline_by_half_on_each_side()
        {
            var p = OutlinePocket(LShape());
            p.Clearance = 1.0;
            Vec2 lo, hi;
            p.BuildLoop(0).GetBounds(out lo, out hi);
            Assert.Equal(61.0, hi.X - lo.X, 2);
            Assert.Equal(21.0, hi.Y - lo.Y, 2);
        }

        [Fact]
        public void A_concave_outline_stays_concave_after_offsetting()
        {
            var grown = OutlineGeometry.OffsetLargest(LShape(), 0.5);
            double area = Math.Abs(GeometryUtil.SignedArea(grown));
            double hull = Math.Abs(GeometryUtil.SignedArea(OutlineGeometry.ConvexHull(grown)));
            // The L is 700 mm²; grown by 0.5 mm its perimeter of 160 mm adds about 80 mm². Its hull is far larger.
            Assert.InRange(area, 770, 790);
            Assert.True(hull > area + 150, "hull " + hull + " vs " + area); // the L's hull adds a 250 mm² triangle
            // The inside corner of the L is still a notch: (−10, 5) lies outside the grown shape.
            Assert.False(GeometryUtil.PointInPolygon(new Vec2(-10, 5), grown));
        }

        [Fact]
        public void Offsetting_cleans_a_self_crossing_trace()
        {
            // A bow tie: tracing glitches can produce this. Union keeps both lobes' outline without crossing edges.
            var bow = new List<Vec2> { new Vec2(0, 0), new Vec2(10, 10), new Vec2(10, 0), new Vec2(0, 10) };
            Assert.NotNull(OutlineGeometry.Clean(bow));
        }

        [Fact]
        public void Align_turns_a_diagonal_bar_onto_the_x_axis()
        {
            var bar = new List<Vec2> { new Vec2(-40, -2), new Vec2(40, -2), new Vec2(40, 2), new Vec2(-40, 2) }
                .Select(q => q.Rotated(35 * Math.PI / 180)).ToList();
            double a = OutlineGeometry.AxisAlignAngle(bar);
            Vec2 lo, hi;
            OutlineGeometry.Bounds(bar.Select(q => q.Rotated(a)), out lo, out hi);
            Assert.Equal(80, hi.X - lo.X, 6);
            Assert.Equal(4, hi.Y - lo.Y, 6);
        }

        [Fact]
        public void Simplify_drops_jitter_but_keeps_corners()
        {
            var rnd = new Random(1);
            var pts = new List<Vec2>();
            for (int i = 0; i < 100; i++) pts.Add(new Vec2(i * 0.5, rnd.NextDouble() * 0.05));
            pts.Add(new Vec2(50, 20));
            pts.Add(new Vec2(0, 20));
            var s = OutlineGeometry.Simplify(pts, 0.1);
            Assert.True(s.Count < 10, "kept " + s.Count + " points");
            Vec2 lo, hi;
            OutlineGeometry.Bounds(s, out lo, out hi);
            Assert.Equal(20, hi.Y - lo.Y, 1);
        }

        // ---- Islands ----------------------------------------------------------------------------------

        [Fact]
        public void KeepIslands_puts_the_shrunk_hole_inside_the_pocket_cut()
        {
            var ring = Loop.Circle(new Vec2(0, 0), 15).ToPolyline(3);
            var hole = Loop.Circle(new Vec2(0, 0), 8).ToPolyline(3);
            var t = new TrayDefinition { UnitsX = 1, UnitsY = 1, HeightUnits = 3, AutoLayout = false, DefaultClearance = 1.0, PrintAllowance = 0 };
            var p = OutlinePocket(ring);
            p.Holes = new List<List<Vec2>> { hole };
            p.KeepIslands = true;
            t.Pockets.Add(p);

            var cut = TrayModeler.CreatePlan(t).Features.Single(f => f.Name.StartsWith("Pockets"));
            Assert.Equal(2, cut.Loops.Count);
            Vec2 lo, hi;
            cut.Loops[1].GetBounds(out lo, out hi);
            Assert.Equal(15.0, hi.X - lo.X, 1); // 16 mm hole minus 0.5 mm each side

            p.KeepIslands = false;
            Assert.Single(TrayModeler.CreatePlan(t).Features.Single(f => f.Name.StartsWith("Pockets")).Loops);
        }

        // ---- DXF / SVG (files made with outline-app's own exporters) ------------------------------------

        [Fact]
        public void Reads_an_outline_app_dxf_with_its_hole()
        {
            var notes = new List<string>();
            var shapes = OutlineImporter.ReadFile(Sample("wrench-13mm.dxf"), notes);
            var s = Assert.Single(shapes);
            Assert.Single(s.Holes);
            Assert.True(s.Outline.Count > 1000);
            Assert.Empty(notes);
        }

        [Fact]
        public void Dxf_and_svg_exports_of_the_same_part_agree()
        {
            var notes = new List<string>();
            var o = new OutlineImportOptions();
            var fromDxf = OutlineImporter.ToPockets(OutlineImporter.ReadFile(Sample("wrench-13mm.dxf"), notes), o, notes).Single();
            var fromSvg = OutlineImporter.ToPockets(OutlineImporter.ReadFile(Sample("wrench-13mm.svg"), notes), o, notes).Single();

            Assert.Equal(fromDxf.Width, fromSvg.Width, 1);
            Assert.Equal(fromDxf.Height, fromSvg.Height, 1);
            // Same handedness: a mirrored SVG would give the same size but a different shape.
            Vec2 a0, a1, b0, b1;
            fromDxf.BuildLoop(0).GetBounds(out a0, out a1);
            fromSvg.BuildLoop(0).GetBounds(out b0, out b1);
            double overlapArea = Math.Abs(GeometryUtil.SignedArea(fromDxf.Points));
            Assert.Equal(overlapArea, Math.Abs(GeometryUtil.SignedArea(fromSvg.Points)), 0);
            Assert.False(GeometryUtil.MinDistance(fromDxf.Points, fromSvg.Points) > 0.5);
        }

        [Fact]
        public void Reads_every_dxf_in_an_outline_app_zip()
        {
            var notes = new List<string>();
            var shapes = OutlineImporter.ReadFile(Sample("toolset_dxf.zip"), notes);
            Assert.Equal(3, shapes.Count);
        }

        [Fact]
        public void Wrench_measures_170_by_30_after_alignment()
        {
            var notes = new List<string>();
            var p = OutlineImporter.ToPockets(OutlineImporter.ReadFile(Sample("wrench-13mm.dxf"), notes), new OutlineImportOptions(), notes).Single();
            Assert.InRange(p.Width, 169.5, 171);
            Assert.InRange(p.Height, 29.5, 31);
            Assert.True(p.Points.Count < 700, "simplified to " + p.Points.Count);
        }

        [Fact]
        public void Dxf_bulge_arcs_and_inch_units()
        {
            // A 1 x 1 inch square whose top edge is a half circle bulging outwards (bulge 1 = 180 degrees).
            string dxf = string.Join("\n", new[]
            {
                "0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", "1", "0", "ENDSEC",
                "0", "SECTION", "2", "ENTITIES",
                "0", "LWPOLYLINE", "8", "0", "90", "4", "70", "1",
                "10", "0", "20", "0",
                "10", "1", "20", "0",
                "10", "1", "20", "1", "42", "1",
                "10", "0", "20", "1",
                "0", "ENDSEC", "0", "EOF"
            });
            var notes = new List<string>();
            var s = OutlineImporter.ReadDxf(dxf, "arc", notes).Single();
            double expected = 25.4 * 25.4 + Math.PI * 12.7 * 12.7 / 2;
            Assert.InRange(s.Area, expected * 0.998, expected * 1.0005); // arcs are flattened to 3-degree steps
            Vec2 lo, hi;
            OutlineGeometry.Bounds(s.Outline, out lo, out hi);
            Assert.Equal(25.4 + 12.7, hi.Y - lo.Y, 1);
        }

        [Fact]
        public void Svg_units_transforms_and_arcs()
        {
            // 2 in wide drawing, viewBox in inches x 100: a 100-unit circle (made of two arcs) = 1 in = 25.4 mm across.
            string svg = @"<svg xmlns='http://www.w3.org/2000/svg' width='2in' height='2in' viewBox='0 0 200 200'>
                <g transform='translate(100,100)'>
                  <path d='M -50 0 A 50 50 0 0 1 50 0 A 50 50 0 0 1 -50 0 Z'/>
                </g>
                <rect x='10' y='10' width='20' height='10' transform='rotate(90 20 15)'/>
              </svg>";
            var notes = new List<string>();
            var shapes = OutlineImporter.ReadSvg(svg, "t", notes);
            Assert.Equal(2, shapes.Count);
            var circle = shapes.OrderByDescending(s => s.Area).First();
            Assert.InRange(circle.Area, Math.PI * 12.7 * 12.7 * 0.998, Math.PI * 12.7 * 12.7 * 1.0005);
            var rect = shapes.OrderBy(s => s.Area).First();
            Vec2 lo, hi;
            OutlineGeometry.Bounds(rect.Outline, out lo, out hi);
            Assert.Equal(10 * 0.254, hi.X - lo.X, 2); // rotated: 10 units wide
            Assert.Equal(20 * 0.254, hi.Y - lo.Y, 2);
        }

        [Fact]
        public void Nested_loops_become_shapes_holes_and_shapes_again()
        {
            Func<double, List<Vec2>> sq = r => new List<Vec2> { new Vec2(-r, -r), new Vec2(r, -r), new Vec2(r, r), new Vec2(-r, r) };
            var shapes = OutlineImporter.Group(new[] { sq(40), sq(30), sq(10) }, "n", "test");
            Assert.Equal(2, shapes.Count);
            Assert.Single(shapes[0].Holes);
            Assert.Empty(shapes[1].Holes);
        }

        [Fact]
        public void Tiny_noise_shapes_are_skipped()
        {
            var notes = new List<string>();
            var dust = new TracedShape { Name = "dust", Outline = new List<Vec2> { new Vec2(0, 0), new Vec2(2, 0), new Vec2(2, 2) } };
            Assert.Empty(OutlineImporter.ToPockets(new[] { dust }, new OutlineImportOptions(), notes));
            Assert.Contains(notes, n => n.Contains("skipped"));
        }

        // ---- Whole trays ------------------------------------------------------------------------------

        [Fact]
        public void Toolset_builds_a_valid_tray_within_the_print_bed_limit()
        {
            var notes = new List<string>();
            var shapes = OutlineImporter.ReadFile(Sample("toolset_dxf.zip"), notes);
            var tray = OutlineImporter.CreateTray("Toolset", shapes, new OutlineImportOptions { Depth = 12 }, notes);
            tray.MagnetHoles = true;

            CompileResult c = TrayCompiler.Compile(tray);
            Assert.True(c.Success, c.Report.ToString());
            Assert.True(c.Resolved.UnitsX <= 5 && c.Resolved.UnitsY <= 5);
            Assert.True(c.Resolved.PocketTopZ - 12 >= GridfinitySpec.BaseHeight + tray.MinFloorBelowPocket);

            foreach (var f in c.Plan.Features)
                foreach (var l in f.Loops)
                    if (!l.IsCircle)
                        for (int i = 0; i < l.Segments.Count; i++)
                            Assert.True((l.Segments[i].End - l.Segments[(i + 1) % l.Segments.Count].Start).Length < 1e-6);
        }

        [Fact]
        public void Overlapping_outlines_are_reported()
        {
            var t = new TrayDefinition { UnitsX = 3, UnitsY = 2, HeightUnits = 3, AutoLayout = false };
            t.Pockets.Add(OutlinePocket(LShape()));
            var second = OutlinePocket(LShape());
            second.Name = "other";
            second.X = 5;
            t.Pockets.Add(second);
            Assert.Contains(TrayValidator.Validate(t).Errors, e => e.Message.Contains("overlap"));
        }

        [Fact]
        public void Outline_pockets_survive_a_json_round_trip()
        {
            var t = new TrayDefinition();
            var p = OutlinePocket(LShape());
            p.Holes = new List<List<Vec2>> { new List<Vec2> { new Vec2(-28, -8), new Vec2(-25, -8), new Vec2(-25, -5) } };
            p.KeepIslands = true;
            t.Pockets.Add(p);

            string json = t.ToJson();
            Assert.Contains("\"Outline\"", json);
            var back = TrayDefinition.FromJson(json).Pockets.Single();
            Assert.Equal(PocketShape.Outline, back.Shape);
            Assert.Equal(6, back.Points.Count);
            Assert.Single(back.Holes);
            Assert.True(back.KeepIslands);
        }

        [Fact]
        public void MaxUnits_caps_auto_grow()
        {
            var t = new TrayDefinition { AutoGrowGrid = true, MaxUnitsX = 2, MaxUnitsY = 2 };
            t.Pockets.Add(Pocket.Rect("long", 150, 10, 5));
            var c = TrayCompiler.Compile(t);
            Assert.False(c.Success);
            Assert.Contains(c.Report.Errors, e => e.Message.Contains("2 x 2"));
        }

        [Fact]
        public void Preview_svg_is_well_formed()
        {
            var notes = new List<string>();
            var tray = OutlineImporter.CreateTray("P", OutlineImporter.ReadFile(Sample("hex-key-5mm.svg"), notes), new OutlineImportOptions(), notes);
            var c = TrayCompiler.Compile(tray);
            var doc = System.Xml.Linq.XDocument.Parse(TrayPreview.ToSvg(c.Resolved));
            Assert.Equal("svg", doc.Root.Name.LocalName);
        }
    }
}
