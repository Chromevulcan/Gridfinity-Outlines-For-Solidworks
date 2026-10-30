using System;
using System.Collections.Generic;
using System.Linq;
using Gridfinity.Core;
using Xunit;

namespace Gridfinity.Core.Tests
{
    public class CoreTests
    {
        private static void AssertClosed(Loop l)
        {
            if (l.IsCircle) return;
            Assert.NotEmpty(l.Segments);
            for (int i = 0; i < l.Segments.Count; i++)
            {
                Segment a = l.Segments[i], b = l.Segments[(i + 1) % l.Segments.Count];
                Assert.True((a.End - b.Start).Length < 1e-6, "gap between segment " + i + " and the next");
                if (a.Kind == SegmentKind.Arc)
                    Assert.True(Math.Abs((a.Start - a.Center).Length - (a.End - a.Center).Length) < 1e-6, "arc radius mismatch");
            }
        }

        [Fact]
        public void Base_profile_matches_the_spec()
        {
            var plan = TrayModeler.CreatePlan(new TrayDefinition { UnitsX = 1, UnitsY = 1 });
            var lower = plan.Features[0];
            var vertical = plan.Features[1];
            var upper = plan.Features[2];

            Assert.Equal(4.75, lower.Depth + vertical.Depth + upper.Depth, 6);
            Assert.Equal(lower.StartZ + lower.Depth, vertical.StartZ, 6);
            Assert.Equal(vertical.StartZ + vertical.Depth, upper.StartZ, 6);

            Vec2 min, max;
            lower.Loops[0].GetBounds(out min, out max);
            Assert.Equal(35.6, max.X - min.X, 6);

            // Top of the upper chamfer = section width + 2 * height at 45 degrees = 41.5 mm.
            upper.Loops[0].GetBounds(out min, out max);
            Assert.Equal(41.5, (max.X - min.X) + 2 * upper.Depth, 6);
        }

        [Fact]
        public void Every_loop_the_modeler_emits_is_closed()
        {
            var t = new TrayDefinition
            {
                UnitsX = 3, UnitsY = 2, HeightUnits = 4, Style = TrayStyle.OpenTray,
                StackingLip = true, MagnetHoles = true, CompartmentsX = 2, AutoLayout = false
            };
            t.Pockets.Add(Pocket.Rect("r", 30, 12, 5, 2));
            t.Pockets.Add(Pocket.SlotPocket("s", 40, 8, 5));
            t.Pockets.Add(Pocket.Hex("h", 10, 5));
            t.Pockets[0].X = -20; t.Pockets[1].X = 20; t.Pockets[2].Y = 25;

            foreach (var f in TrayModeler.CreatePlan(t).Features)
                foreach (var l in f.Loops) AssertClosed(l);
        }

        [Fact]
        public void Oversized_corner_radius_is_clamped_not_broken()
        {
            AssertClosed(Shapes.RoundedRect(0, 0, 10, 4, 50));
        }

        [Fact]
        public void Rectangle_pocket_loop_has_the_clearance_applied()
        {
            var p = Pocket.Rect("x", 20, 10, 5);
            Vec2 min, max;
            p.BuildLoop(0.4).GetBounds(out min, out max);
            Assert.Equal(20.4, max.X - min.X, 6);
            Assert.Equal(10.4, max.Y - min.Y, 6);
        }

        [Fact]
        public void Auto_layout_places_everything_without_overlap()
        {
            var t = new TrayDefinition { UnitsX = 3, UnitsY = 2 };
            for (int i = 0; i < 6; i++) t.Pockets.Add(Pocket.Rect("p" + i, 40, 14, 6));

            var compiled = TrayCompiler.Compile(t);
            Assert.True(compiled.Success, compiled.Report.ToString());
            Assert.DoesNotContain(compiled.Report.Issues, i => i.Message.Contains("overlap"));
        }

        [Fact]
        public void Auto_grow_finds_a_grid_that_fits()
        {
            var t = new TrayDefinition { UnitsX = 1, UnitsY = 1, AutoGrowGrid = true };
            for (int i = 0; i < 5; i++) t.Pockets.Add(Pocket.Rect("p" + i, 60, 20, 6));

            var compiled = TrayCompiler.Compile(t);
            Assert.True(compiled.Success, compiled.Report.ToString());
            Assert.True(compiled.Resolved.UnitsX * compiled.Resolved.UnitsY > 1);
            Assert.Equal(1, t.UnitsX); // input untouched
        }

        [Fact]
        public void Validator_reports_overlapping_pockets()
        {
            var t = new TrayDefinition { UnitsX = 2, UnitsY = 2, AutoLayout = false };
            t.Pockets.Add(Pocket.Rect("a", 30, 30, 6));
            t.Pockets.Add(Pocket.Rect("b", 30, 30, 6));
            t.Pockets[1].X = 10;

            var report = TrayValidator.Validate(t);
            Assert.False(report.IsValid);
            Assert.Contains(report.Errors, e => e.Message.Contains("overlap"));
        }

        [Fact]
        public void Validator_rejects_pockets_that_leave_no_floor()
        {
            var t = new TrayDefinition { UnitsX = 2, UnitsY = 2, HeightUnits = 2, AutoLayout = false };
            t.Pockets.Add(Pocket.Round("deep", 10, 14)); // block is 14 mm tall: nothing left underneath

            Assert.False(TrayValidator.Validate(t).IsValid);
        }

        [Fact]
        public void Open_tray_with_lip_is_taller_by_the_lip_height()
        {
            var t = new TrayDefinition { Style = TrayStyle.OpenTray, StackingLip = true, HeightUnits = 3, AutoLayout = false };
            var plan = TrayModeler.CreatePlan(t);

            Assert.Equal(21 + 4.4, plan.ExpectedHeight, 6);
            Assert.Equal(4, plan.Features.Count(f => f.Name.StartsWith("Lip")));

            // The lip cuts are stacked with no gaps and end at the very top (ignoring the overshoot on the last one).
            var lip = plan.Features.Where(f => f.Name.StartsWith("Lip")).ToList();
            for (int i = 1; i < lip.Count; i++)
                Assert.Equal(lip[i - 1].StartZ + lip[i - 1].Depth, lip[i].StartZ, 6);
        }

        [Fact]
        public void Json_round_trip_keeps_the_definition()
        {
            var t = new TrayDefinition { Name = "RT", UnitsX = 4, Style = TrayStyle.OpenTray, MagnetHoles = true };
            t.Pockets.Add(new Pocket
            {
                Name = "poly", Shape = PocketShape.Polygon, Depth = 5,
                Points = new List<Vec2> { new Vec2(-5, -5), new Vec2(5, -5), new Vec2(0, 6) }
            });

            var back = TrayDefinition.FromJson(t.ToJson());
            Assert.Equal("RT", back.Name);
            Assert.Equal(4, back.UnitsX);
            Assert.Equal(TrayStyle.OpenTray, back.Style);
            Assert.Equal(3, back.Pockets[0].Points.Count);
            Assert.DoesNotContain("Length", t.ToJson());
        }

        [Fact]
        public void Magnet_pockets_sit_inside_the_base_footprint()
        {
            var plan = TrayModeler.CreatePlan(new TrayDefinition { UnitsX = 1, UnitsY = 1, MagnetHoles = true });
            var magnets = plan.Features.Single(f => f.Name == "Magnet pockets");
            Assert.Equal(4, magnets.Loops.Count);
            foreach (var l in magnets.Loops)
                Assert.True(Math.Abs(l.Center.X) + l.Radius < 35.6 / 2);
        }

        [Fact]
        public void Labels_become_one_optional_text_cut_at_the_right_depth()
        {
            var t = new TrayDefinition { UnitsX = 2, UnitsY = 2, HeightUnits = 3, AutoLayout = false, LabelDepth = 0.8 };
            t.Labels.Add(Label.At("3/8", -30, 30));
            t.Labels.Add(Label.At("7/16", -30, 20));

            var f = TrayModeler.CreatePlan(t).Features.Single(x => x.Name == "Labels");
            Assert.True(f.Optional);
            Assert.Equal(2, f.Texts.Count);
            Assert.Equal(21 - 0.8, f.StartZ, 6);          // starts 0.8 mm below the top face
            Assert.True(f.Depth >= 0.8);                   // and runs up through it
        }

        [Fact]
        public void Labels_survive_a_json_round_trip_without_the_width_estimate()
        {
            var t = new TrayDefinition();
            t.Labels.Add(Label.At("1 1/16", 5, -7, 6));
            string json = t.ToJson();

            Assert.DoesNotContain("EstimatedWidth", json);
            var back = TrayDefinition.FromJson(json);
            Assert.Equal("1 1/16", back.Labels[0].Text);
            Assert.Equal(-7, back.Labels[0].Y, 6);
        }

        [Fact]
        public void A_label_on_top_of_a_pocket_is_warned_about()
        {
            var t = new TrayDefinition { UnitsX = 2, UnitsY = 2, AutoLayout = false };
            t.Pockets.Add(Pocket.Rect("p", 40, 20, 6));
            t.Labels.Add(Label.At("OVER", -10, 0));

            var report = TrayValidator.Validate(t);
            Assert.Contains(report.Warnings, w => w.Message.Contains("overlap a pocket"));
        }

        [Fact]
        public void Baseplate_profile_sits_on_the_base_layer_and_cells_do_not_touch()
        {
            var b = new BaseplateDefinition { UnitsX = 2, UnitsY = 2 };
            var plan = BaseplateModeler.CreatePlan(b);

            var plate = plan.Features.Single(f => f.Name == "Plate");
            Assert.Equal(3.0 + 4.65, plate.Depth, 6);
            Assert.Equal(84, plan.ExpectedWidth, 6);

            var cuts = plan.Features.Where(f => f.Name.StartsWith("Cells - ") && f.Name != "Cells - base layer").ToList();
            Assert.Equal(3, cuts.Count);
            Assert.Equal(4.65, cuts.Sum(f => f.Depth), 6);
            Assert.Equal(3.0, cuts[0].StartZ, 6);
            for (int i = 1; i < cuts.Count; i++)
                Assert.Equal(cuts[i - 1].StartZ + cuts[i - 1].Depth, cuts[i].StartZ, 6);

            // Every cell opening, including the draft growth to the top, stays inside its own 42 mm cell.
            Vec2 min, max;
            cuts.Last().Loops[0].GetBounds(out min, out max);
            double topWidth = (max.X - min.X) + 2 * cuts.Last().Depth;
            Assert.True(topWidth < 42.0);
            Assert.Equal(42.0 - b.WebAtTop, topWidth, 6);
            foreach (var f in plan.Features) foreach (var l in f.Loops) AssertClosed(l);
        }

        [Fact]
        public void Baseplate_base_layer_opening_matches_the_measured_site_plate()
        {
            // From the exported 2 x 2 plate: arms 18 wide, boss radius 4.5 centred 13.5 from the cell centre.
            var plan = BaseplateModeler.CreatePlan(new BaseplateDefinition { UnitsX = 1, UnitsY = 1, WebAtTop = 0.0 + 0.05 });
            var layer = plan.Features.Single(f => f.Name == "Cells - base layer");
            Assert.Equal(0, layer.StartZ, 6);
            Assert.Equal(3.0, layer.Depth, 6);

            var arcs = layer.Loops[0].Segments.Where(s => s.Kind == SegmentKind.Arc).ToList();
            Assert.Equal(4, arcs.Count);
            foreach (var a in arcs)
            {
                Assert.Equal(4.5, (a.Start - a.Center).Length, 6);
                Assert.Equal(13.5, Math.Abs(a.Center.X), 6);
                Assert.Equal(13.5, Math.Abs(a.Center.Y), 6);
            }
        }

        [Fact]
        public void Baseplate_magnets_screws_and_slots_are_optional_and_sized_by_the_settings()
        {
            var off = BaseplateModeler.CreatePlan(new BaseplateDefinition { UnitsX = 2, UnitsY = 2 });
            Assert.DoesNotContain(off.Features, f => f.Name == "Magnet pockets" || f.Name == "Screw holes" || f.Name.StartsWith("Slot"));

            var b = new BaseplateDefinition
            {
                UnitsX = 2, UnitsY = 2, MagnetHoles = true, MagnetDiameter = 8, MagnetThickness = 3, ScrewHoles = true, ConnectionSlots = true
            };
            Assert.Equal(0.9 + 3.0, b.EffectiveBase, 6);                    // the base layer grows to hold a 3 mm magnet
            var plan = BaseplateModeler.CreatePlan(b);

            var pockets = plan.Features.Single(f => f.Name == "Magnet pockets");
            Assert.Equal(16, pockets.Loops.Count);
            Assert.Equal(4.0, pockets.Loops[0].Radius, 6);
            Assert.Equal(0.9, pockets.StartZ, 6);
            Assert.Equal(16, plan.Features.Single(f => f.Name == "Screw holes").Loops.Count);

            // 2 slots on each of the 4 edges of a 2 x 2 plate, in every one of the four bands.
            var slots = plan.Features.Where(f => f.Name.StartsWith("Slot")).ToList();
            Assert.Equal(4, slots.Count);
            Assert.All(slots, f => Assert.Equal(8, f.Loops.Count));
            foreach (var f in slots) foreach (var l in f.Loops) AssertClosed(l);
            Assert.Equal(b.TotalHeight, slots.Last().StartZ + slots.Last().Depth - 0.3, 6);
        }

        [Fact]
        public void Baseplate_rejects_unusable_magnet_and_clip_settings()
        {
            Assert.False(BaseplateModeler.Validate(new BaseplateDefinition { MagnetHoles = true, MagnetDiameter = 12 }).IsValid);
            Assert.False(BaseplateModeler.Validate(new BaseplateDefinition { MagnetHoles = true, ScrewHoles = true, ScrewDiameter = 7 }).IsValid);
            Assert.False(new ClipDefinition { ClipCount = 0 }.Validate().IsValid);
            Assert.True(new ClipDefinition { ClipCount = 6 }.Validate().IsValid);
            Assert.True(BaseplateModeler.Validate(new BaseplateDefinition { MagnetHoles = true, ScrewHoles = true, ConnectionSlots = true }).IsValid);
        }

        [Fact]
        public void Clip_plan_is_one_closed_section_per_clip()
        {
            var plan = BaseplateModeler.CreateClipPlan("c", 3, 19.6);
            var f = plan.Features.Single();
            Assert.Equal(3, f.Loops.Count);
            Assert.Equal(19.6, f.Depth, 6);
            Vec2 min, max;
            f.Loops[0].GetBounds(out min, out max);
            Assert.Equal(4.3, max.X - min.X, 6);
            foreach (var l in f.Loops) AssertClosed(l);
        }

        [Fact]
        public void Clip_json_is_recognised_and_round_trips()
        {
            string json = new ClipDefinition { Name = "C", ClipCount = 9 }.ToJson();
            Assert.True(ClipDefinition.LooksLikeClips(json));
            Assert.False(BaseplateDefinition.LooksLikeBaseplate(json));
            Assert.False(ClipDefinition.LooksLikeClips(new BaseplateDefinition().ToJson()));
            Assert.Equal(9, ClipDefinition.FromJson(json).ClipCount);
        }

        [Fact]
        public void Baseplate_json_is_recognised_and_round_trips()
        {
            var b = new BaseplateDefinition { Name = "BP", UnitsX = 5, MagnetHoles = true, MagnetDiameter = 6.0, ConnectionSlots = true };
            string json = b.ToJson();

            Assert.True(BaseplateDefinition.LooksLikeBaseplate(json));
            Assert.False(BaseplateDefinition.LooksLikeBaseplate(new TrayDefinition().ToJson()));
            Assert.False(BaseplateDefinition.LooksLikeBaseplate("not json"));

            var back = BaseplateDefinition.FromJson(json);
            Assert.Equal(5, back.UnitsX);
            Assert.Equal(6.0, back.MagnetDiameter, 6);
            Assert.True(back.ConnectionSlots);

            // Older files that still carry Style / FloorThickness load without complaint.
            Assert.Equal(2, BaseplateDefinition.FromJson("{\"Type\":\"Baseplate\",\"UnitsX\":2,\"Style\":\"Solid\",\"FloorThickness\":1.0}").UnitsX);
        }

        [Fact]
        public void Cradle_section_is_closed_round_bottomed_and_sized_to_the_diameter()
        {
            var p = Pocket.CradlePocket("s", 20, 40, 18);
            p.X = 10;
            Loop l = p.BuildCradleSection(0.4, 12, 30);
            AssertClosed(l);

            Vec2 min, max;
            l.GetBounds(out min, out max);
            Assert.Equal(20.4, max.X - min.X, 3);
            Assert.Equal(12, min.Y, 3);      // lowest point of the round bottom
            Assert.Equal(30, max.Y, 3);
        }

        [Fact]
        public void Cradles_become_top_plane_cuts_split_at_the_centre_line()
        {
            var t = new TrayDefinition { UnitsX = 2, UnitsY = 2, HeightUnits = 6, AutoLayout = false, DefaultClearance = 0.4 };
            var a = Pocket.CradlePocket("straddles", 20, 40, 18); a.X = -15; a.Y = 0;
            var b = Pocket.CradlePocket("front", 20, 20, 18); b.X = 15; b.Y = -25;
            t.Pockets.Add(a); t.Pockets.Add(b);

            var cuts = TrayModeler.CreatePlan(t).Features.Where(f => f.Plane == SketchPlane.Top).ToList();
            Assert.Equal(3, cuts.Count);                                   // straddler = 2 cuts, the other = 1
            Assert.Equal(2, cuts.Count(f => f.NegativeSide));
            Assert.All(cuts, f => Assert.True(f.Depth > 0 && f.StartZ >= 0));
        }

        [Fact]
        public void Pocket_into_the_feet_of_a_solid_block_is_rejected()
        {
            var t = new TrayDefinition { UnitsX = 2, UnitsY = 2, HeightUnits = 6, AutoLayout = false };
            t.Pockets.Add(Pocket.CradlePocket("deep", 20, 40, 42 - 2.5));   // bottom 2.5 mm up: inside the 4.75 mm feet
            Assert.False(TrayValidator.Validate(t).IsValid);

            t.Pockets[0].Depth = 42 - 6;
            Assert.True(TrayValidator.Validate(t).IsValid);
        }
    }
}
