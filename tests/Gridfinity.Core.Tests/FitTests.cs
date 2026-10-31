using System.Collections.Generic;
using System.Linq;
using Gridfinity.Core;
using Xunit;

namespace Gridfinity.Core.Tests
{
    /// <summary>Print allowance and scale references.</summary>
    public class FitTests
    {
        private static TrayDefinition Tray(double allowance, double clearance, params Pocket[] pockets)
        {
            var t = new TrayDefinition { UnitsX = 3, UnitsY = 2, HeightUnits = 4, AutoLayout = false, DefaultClearance = clearance, PrintAllowance = allowance };
            t.Pockets.AddRange(pockets);
            return t;
        }

        private static void Size(Loop l, out double w, out double h)
        {
            Vec2 lo, hi;
            l.GetBounds(out lo, out hi);
            w = hi.X - lo.X; h = hi.Y - lo.Y;
        }

        [Fact]
        public void The_print_allowance_widens_every_cutout_on_every_side()
        {
            var p = Pocket.Rect("block", 60, 20, 8);
            var t = Tray(0.3, 0.4, p);
            double w, h;
            Size(p.BuildLoop(t.GeometryClearance), out w, out h);
            Assert.Equal(60 + 0.4 + 0.6, w, 6);
            Assert.Equal(20 + 0.4 + 0.6, h, 6);

            t.PrintAllowance = 0;
            Size(p.BuildLoop(t.GeometryClearance), out w, out h);
            Assert.Equal(60.4, w, 6);
        }

        [Fact]
        public void The_print_allowance_also_applies_to_parts_with_their_own_clearance()
        {
            var part = new Pocket { Name = "part", Shape = PocketShape.Outline, Depth = 6, Clearance = 1.0,
                Points = new List<Vec2> { new Vec2(-30, -10), new Vec2(30, -10), new Vec2(30, 10), new Vec2(-30, 10) } };
            var t = Tray(0.2, 0.4, part);
            double w, h;
            Size(part.BuildLoop(t.GeometryClearance), out w, out h);
            Assert.Equal(60 + 1.0 + 0.4, w, 1);
        }

        [Fact]
        public void Too_tight_a_fit_is_flagged_and_the_default_is_not()
        {
            var t = Tray(0, 0.2, Pocket.Rect("snug", 40, 20, 6));
            var c = TrayCompiler.Compile(t);
            Assert.Contains(c.Report.Issues, i => i.Severity == Severity.Warning && i.Message.Contains("per side"));

            t.PrintAllowance = 0.2; // 0.1 + 0.2 = 0.3 mm per side
            c = TrayCompiler.Compile(t);
            Assert.DoesNotContain(c.Report.Issues, i => i.Message.Contains("per side between the part"));
        }

        [Fact]
        public void An_unrealistic_allowance_is_an_error()
        {
            var c = TrayCompiler.Compile(Tray(3, 0.4, Pocket.Rect("x", 40, 20, 6)));
            Assert.False(c.Success);
        }

        [Fact]
        public void Old_files_get_the_default_allowance_and_it_round_trips()
        {
            var t = TrayDefinition.FromJson("{\"Name\":\"old\",\"UnitsX\":2,\"UnitsY\":1,\"HeightUnits\":3}");
            Assert.Equal(0.2, t.PrintAllowance, 6);
            t.PrintAllowance = 0.35;
            Assert.Equal(0.35, TrayDefinition.FromJson(t.ToJson()).PrintAllowance, 6);
        }

        private static TracedShape Rect(string name, double w, double h, double angleDeg = 0)
        {
            double a = angleDeg * System.Math.PI / 180;
            var pts = new[] { new Vec2(-w / 2, -h / 2), new Vec2(w / 2, -h / 2), new Vec2(w / 2, h / 2), new Vec2(-w / 2, h / 2) }
                .Select(p => p.Rotated(a) + new Vec2(100, 50)).ToList();
            return new TracedShape { Name = name, Source = "test", Outline = pts };
        }

        [Fact]
        public void A_square_reference_rescales_the_other_shapes_and_is_removed()
        {
            const double k = 0.97; // the photo came out 3 % small
            var shapes = new List<TracedShape> { Rect("wrench", 150 * k, 22 * k, 10), Rect("ref", 50 * k, 50 * k, 25), Rect("bit", 40 * k, 8 * k) };
            var notes = new List<string>();
            double s = ScaleReferences.Apply(shapes, new OutlineImportOptions { ScaleReference = ScaleReferenceKind.Square, ReferenceWidth = 50 }, "photo", notes);
            Assert.Equal(1 / k, s, 3);
            Assert.Equal(new[] { "wrench", "bit" }, shapes.Select(x => x.Name).ToArray());
            double l, w, fill;
            ScaleReferences.MinRect(shapes[0].Outline, out l, out w, out fill);
            Assert.Equal(150, l, 2);
            Assert.Equal(22, w, 2);
        }

        [Fact]
        public void A_missing_reference_leaves_the_scale_alone_and_says_so()
        {
            var shapes = new List<TracedShape> { Rect("wrench", 150, 22), Rect("disc-ish", 30, 29) };
            var notes = new List<string>();
            double s = ScaleReferences.Apply(shapes, new OutlineImportOptions { ScaleReference = ScaleReferenceKind.CreditCard }, "photo", notes);
            Assert.Equal(1, s, 9);
            Assert.Equal(2, shapes.Count);
            Assert.Contains(notes, n => n.Contains("no credit card scale reference"));
        }

        [Fact]
        public void A_rectangle_reference_such_as_a_steel_rule_is_found_among_parts()
        {
            const double k = 1.02;
            var shapes = new List<TracedShape> { Rect("rule", 150 * k, 19 * k, 3), Rect("chisel", 140 * k, 25 * k) };
            var notes = new List<string>();
            double s = ScaleReferences.Apply(shapes, new OutlineImportOptions { ScaleReference = ScaleReferenceKind.Rectangle, ReferenceWidth = 150, ReferenceHeight = 19 }, "photo", notes);
            Assert.Equal(1 / k, s, 3);
            Assert.Single(shapes);
            Assert.Equal("chisel", shapes[0].Name);
        }
    }
}
