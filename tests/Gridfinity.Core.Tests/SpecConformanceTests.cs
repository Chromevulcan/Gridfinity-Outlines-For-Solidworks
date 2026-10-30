using System;
using System.Linq;
using Gridfinity.Core;
using Xunit;

namespace Gridfinity.Core.Tests
{
    /// <summary>
    /// Pins the dimensions to the Gridfinity Design Reference v5 (willtree8), the drawing published at
    /// https://gridfinity.xyz/specification/ (all values in millimetres). If one of these fails, a change has moved the
    /// geometry away from the specification: fix the change, or update the test only if the specification changed.
    /// </summary>
    public class SpecConformanceTests
    {
        private const double Tol = 1e-9;

        [Fact]
        public void Grid_pitch_is_42_and_a_bin_is_41_5_with_0_5_tolerance()
        {
            Assert.Equal(42.0, GridfinitySpec.Pitch, 9);
            Assert.Equal(41.5, GridfinitySpec.BinSize(1), 9);
            for (int n = 1; n <= 8; n++) Assert.Equal(42.0 * n - 0.5, GridfinitySpec.BinSize(n), 9);
        }

        [Fact]
        public void Base_profile_is_0_8_chamfer_1_8_vertical_2_15_chamfer_4_75_tall()
        {
            Assert.Equal(0.8, GridfinitySpec.BaseChamferLower, 9);
            Assert.Equal(1.8, GridfinitySpec.BaseVertical, 9);
            Assert.Equal(2.15, GridfinitySpec.BaseChamferUpper, 9);
            Assert.Equal(4.75, GridfinitySpec.BaseHeight, 9);
        }

        [Fact]
        public void Base_foot_is_35_6_at_the_bottom_and_41_5_at_the_top()
        {
            var plan = TrayModeler.CreatePlan(new TrayDefinition { UnitsX = 1, UnitsY = 1 });
            Vec2 lo, hi;
            plan.Features[0].Loops[0].GetBounds(out lo, out hi);
            Assert.Equal(35.6, hi.X - lo.X, 6);

            // 45-degree chamfers grow the section by 2 x their height.
            double top = 35.6 + 2 * GridfinitySpec.BaseChamferLower + 2 * GridfinitySpec.BaseChamferUpper;
            Assert.Equal(41.5, top, 6);
        }

        [Fact]
        public void Corner_diameters_are_7_5_outer_3_2_middle_1_6_bottom()
        {
            Assert.Equal(7.5, 2 * GridfinitySpec.BinCornerRadius, 9);
            Assert.Equal(1.6, 2 * GridfinitySpec.BaseBottomCornerRadius, 9);
            Assert.Equal(3.2, 2 * (GridfinitySpec.BaseBottomCornerRadius + GridfinitySpec.BaseChamferLower), 9);

            // The outer corner of the 41.5 mm bin: 3.2 mm (middle) + 2 x 2.15 mm chamfer = 7.5 mm.
            Assert.Equal(7.5, 3.2 + 2 * GridfinitySpec.BaseChamferUpper, 9);
        }

        [Fact]
        public void Magnet_holes_are_6_5_diameter_7_75_in_from_the_bin_edge()
        {
            Assert.Equal(6.5, GridfinitySpec.MagnetDiameter, 9);
            // Spec plan view: 2.15 + 0.8 + 4.8 = 7.75 mm from the outer edge of a 41.5 mm bin, so 13 mm from the centre.
            Assert.Equal(13.0, GridfinitySpec.MagnetOffset, 9);
            Assert.Equal(13.0, GridfinitySpec.BinSize(1) / 2 - (2.15 + 0.8 + 4.8), 9);
            // Holds 6 x 2 mm magnets.
            Assert.True(GridfinitySpec.MagnetDepth >= 2.0);
        }

        [Fact]
        public void Magnet_pockets_sit_in_all_four_corners_of_every_cell()
        {
            var plan = TrayModeler.CreatePlan(new TrayDefinition { UnitsX = 2, UnitsY = 3, MagnetHoles = true });
            var magnets = plan.Features.Single(f => f.Name == "Magnet pockets");
            Assert.Equal(2 * 3 * 4, magnets.Loops.Count);
            Assert.All(magnets.Loops, l => Assert.Equal(3.25, l.Radius, 9));
            Assert.Equal(0, magnets.StartZ, 9);
        }

        [Fact]
        public void Height_unit_is_7_and_the_lip_adds_4_4()
        {
            Assert.Equal(7.0, GridfinitySpec.HeightUnit, 9);
            Assert.Equal(0.7, GridfinitySpec.LipLowerChamfer, 9);
            Assert.Equal(1.8, GridfinitySpec.LipVertical, 9);
            Assert.Equal(1.9, GridfinitySpec.LipUpperChamfer, 9);
            Assert.Equal(4.4, GridfinitySpec.LipHeight, 9);

            // Spec: a 2u bin with its stacking lip is 18.4 mm tall.
            var t = new TrayDefinition { HeightUnits = 2, Style = TrayStyle.OpenTray, StackingLip = true, FloorHeight = 1.2 };
            Assert.Equal(14.0, t.BodyTop, 9);
            Assert.Equal(18.4, t.TotalHeight, 9);
        }

        [Fact]
        public void Baseplate_is_a_42_mm_grid_with_0_7_1_8_2_15_profile_and_8_mm_corners()
        {
            Assert.Equal(0.7, BaseplateSpec.LowerChamfer, 9);
            Assert.Equal(1.8, BaseplateSpec.Vertical, 9);
            Assert.Equal(2.15, BaseplateSpec.UpperChamfer, 9);
            Assert.Equal(8.0, 2 * BaseplateSpec.OuterCornerRadius, 9);
        }

        [Fact]
        public void Outline_pockets_never_change_the_bin_geometry()
        {
            // The base, body and lip of a tray with traced pockets are identical to one without.
            var plain = new TrayDefinition { UnitsX = 3, UnitsY = 2, HeightUnits = 3, MagnetHoles = true, AutoLayout = false };
            var traced = plain.Clone();
            traced.Pockets.Add(new Pocket
            {
                Name = "L", Shape = PocketShape.Outline, Depth = 8,
                Points = new[] { new Vec2(-30, -10), new Vec2(30, -10), new Vec2(30, 0), new Vec2(-20, 0), new Vec2(-20, 10), new Vec2(-30, 10) }.ToList()
            });

            var a = TrayModeler.CreatePlan(plain).Features.Where(f => !f.Name.StartsWith("Pockets")).ToList();
            var b = TrayModeler.CreatePlan(traced).Features.Where(f => !f.Name.StartsWith("Pockets")).ToList();
            Assert.Equal(a.Select(f => f.Name), b.Select(f => f.Name));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].StartZ, b[i].StartZ, 9);
                Assert.Equal(a[i].Depth, b[i].Depth, 9);
                Assert.Equal(a[i].DraftDegrees, b[i].DraftDegrees, 9);
                Assert.Equal(a[i].Loops.Count, b[i].Loops.Count);
            }
        }
    }
}
