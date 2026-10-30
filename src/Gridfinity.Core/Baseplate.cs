using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Gridfinity.Core
{
    /// <summary>
    /// A Gridfinity baseplate: always an open frame (no floor), one bin-shaped opening per 42 mm cell, with optional
    /// magnet pockets, screw holes and connection slots for the clips. Serialises to/from JSON. The geometry was
    /// measured from a connectable 2 x 2 baseplate exported from gridfinitygenerator.com.
    /// </summary>
    public class BaseplateDefinition
    {
        /// <summary>Marks a JSON file as a baseplate so the add-in can tell it from a box.</summary>
        [Browsable(false)]
        public string Type { get; set; } = "Baseplate";

        [Category("Grid"), Description("Part name, also used as the file name stem.")]
        public string Name { get; set; } = "GridfinityBaseplate";

        [Category("Grid"), Description("Number of 42 mm cells along X.")]
        public int UnitsX { get; set; } = 4;

        [Category("Grid"), Description("Number of 42 mm cells along Y.")]
        public int UnitsY { get; set; } = 3;

        [Category("Frame"), Description("Thickness of the lower layer that holds the magnets and screws, mm. Grows automatically if the magnets need more.")]
        public double BaseThickness { get; set; } = 3.0;

        [Category("Frame"), Description("Material left between neighbouring cells at the top surface, mm. The cell opening is 42 minus this.")]
        public double WebAtTop { get; set; } = 0.2;

        // ---- Magnets --------------------------------------------------------------------------
        [Category("Magnets"), Description("A magnet pocket in each cell corner, opening upward so the magnet drops in from the top.")]
        public bool MagnetHoles { get; set; }

        [Category("Magnets"), Description("Magnet diameter, mm (6.2 for a 6 mm magnet). Up to 8 mm.")]
        public double MagnetDiameter { get; set; } = 6.2;

        [Category("Magnets"), Description("Magnet thickness, mm (the pocket depth).")]
        public double MagnetThickness { get; set; } = 2.1;

        [Category("Magnets"), Description("Material left under the magnet, mm (it is also where a screw hole goes through).")]
        public double MagnetFloor { get; set; } = 0.9;

        // ---- Screws ---------------------------------------------------------------------------
        [Category("Screws"), Description("A screw hole in each cell corner, through the lower layer (under the magnet pocket when magnets are on).")]
        public bool ScrewHoles { get; set; }

        [Category("Screws"), Description("Screw hole diameter, mm.")]
        public double ScrewDiameter { get; set; } = 3.5;

        // ---- Connection -----------------------------------------------------------------------
        [Category("Connection"), Description("Cut slots in the outer walls so plates can be joined with clips. Slots sit on the 42 mm grid, so plates of different sizes line up.")]
        public bool ConnectionSlots { get; set; }

        [Category("Connection"), Description("Slots on the left (-X) edge.")]
        public bool SlotsLeft { get; set; } = true;

        [Category("Connection"), Description("Slots on the right (+X) edge.")]
        public bool SlotsRight { get; set; } = true;

        [Category("Connection"), Description("Slots on the front (-Y) edge.")]
        public bool SlotsFront { get; set; } = true;

        [Category("Connection"), Description("Slots on the back (+Y) edge.")]
        public bool SlotsBack { get; set; } = true;

        [Category("Connection"), Description("Slot length minus clip length, mm (0.4 gives the 20 mm slot the site uses for its 19.6 mm clip).")]
        public double SlotClearance { get; set; } = 0.4;

        [Category("Connection"), Description("Length of the clip the slots are made for, mm. The slot is this plus SlotClearance. Build the clips themselves on the Clips tab.")]
        public double ClipLength { get; set; } = BaseplateSpec.ClipLength;

        // ---- Derived (read-only) --------------------------------------------------------------
        [JsonIgnore, Category("Derived")]
        public double OuterWidth { get { return UnitsX * GridfinitySpec.Pitch; } }

        [JsonIgnore, Category("Derived")]
        public double OuterDepth { get { return UnitsY * GridfinitySpec.Pitch; } }

        /// <summary>Thickness of the lower layer: BaseThickness, or more when the magnet pockets need it.</summary>
        [JsonIgnore, Category("Derived")]
        public double EffectiveBase { get { return MagnetHoles ? Math.Max(BaseThickness, MagnetFloor + MagnetThickness) : BaseThickness; } }

        [JsonIgnore, Category("Derived")]
        public double TotalHeight { get { return EffectiveBase + BaseplateSpec.PocketDepth; } }

        [JsonIgnore, Category("Derived")]
        public double SlotLength { get { return ClipLength + SlotClearance; } }

        // ---- JSON -----------------------------------------------------------------------------
        private static JsonSerializerSettings Settings()
        {
            var s = new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore };
            s.Converters.Add(new StringEnumConverter());
            return s;
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Settings()); }

        public static BaseplateDefinition FromJson(string json)
        {
            return JsonConvert.DeserializeObject<BaseplateDefinition>(json, Settings());
        }

        /// <summary>True when the JSON text carries "Type": "Baseplate".</summary>
        public static bool LooksLikeBaseplate(string json)
        {
            try
            {
                JToken type = JObject.Parse(json)["Type"];
                return type != null && string.Equals(type.ToString(), "Baseplate", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>The connection clips as a part of their own: N clips side by side. Serialises to/from JSON.</summary>
    public class ClipDefinition
    {
        [Browsable(false)]
        public string Type { get; set; } = "Clips";

        [Category("Clips"), Description("Part name.")]
        public string Name { get; set; } = "GridfinityClips";

        [Category("Clips"), Description("How many clips to model, side by side.")]
        public int ClipCount { get; set; } = 6;

        [Category("Clips"), Description("Clip length, mm. The site's clip is 19.6 mm.")]
        public double ClipLength { get; set; } = BaseplateSpec.ClipLength;

        public ValidationReport Validate()
        {
            var r = new ValidationReport();
            if (string.IsNullOrWhiteSpace(Name)) r.Error("Name is empty.");
            if (ClipCount < 1 || ClipCount > 200) r.Error("ClipCount must be 1 to 200.");
            if (ClipLength < 5 || ClipLength > 30) r.Error("ClipLength must be 5 to 30 mm.");
            return r;
        }

        public string ToJson()
        {
            var s = new JsonSerializerSettings { Formatting = Formatting.Indented };
            return JsonConvert.SerializeObject(this, s);
        }

        public static ClipDefinition FromJson(string json) { return JsonConvert.DeserializeObject<ClipDefinition>(json); }

        public static bool LooksLikeClips(string json)
        {
            try
            {
                JToken type = JObject.Parse(json)["Type"];
                return type != null && string.Equals(type.ToString(), "Clips", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Baseplate pocket profile, top to bottom: 2.15 mm 45-degree chamfer, 1.8 mm vertical, 0.7 mm 45-degree chamfer
    /// (4.65 mm in all). The cell opening is a little larger than a bin foot so bins drop in. These follow the commonly
    /// published Gridfinity baseplate profile; print one cell and check the fit before printing a large plate.
    /// </summary>
    public static class BaseplateSpec
    {
        public const double UpperChamfer = 2.15;
        public const double Vertical = 1.8;
        public const double LowerChamfer = 0.7;
        public const double PocketDepth = UpperChamfer + Vertical + LowerChamfer; // 4.65
        public const double OuterCornerRadius = 4.0;

        /// <summary>Magnet and screw positions from each cell centre, on both axes (measured from the site's plate).</summary>
        public const double HoleOffset = 13.5;

        /// <summary>Wall kept around a magnet pocket: the boss radius is the magnet radius plus this.</summary>
        public const double BossWall = 1.4;

        public const double MaxMagnetDiameter = 8.0;

        // Connection slot. The wall hook that traps the clip, measured from the outer edge of the plate (d) and from
        // the top of the lower layer (z). The clip itself is 19.6 long with a 4.3 x 3.7 mm section; half of it sits in each plate.
        public const double ClipLength = 19.6;
        public const double SlotFloor = 0.7;      // groove floor above the lower layer
        public const double HookTop = 2.0;        // above this the wall is cut away completely

        /// <summary>Groove bands above the lower layer: start, height, and how far in from the edge the hook face is (d).</summary>
        public static readonly double[][] GrooveBands =
        {
            new[] { 0.7, 0.6, 1.0 },   // vertical hook face, 3.7 to 4.3 mm
            new[] { 1.3, 0.2, 1.2 },   // sloping face (stepped), 4.3 to 4.5 mm
            new[] { 1.5, 0.5, 1.4 }    // wider hook head, 4.5 to 5.0 mm
        };
    }

    public sealed class BaseplateCompileResult
    {
        public ValidationReport Report = new ValidationReport();

        /// <summary>Null when validation found errors.</summary>
        public BuildPlan Plan;

        public bool Success { get { return Plan != null && Report.IsValid; } }
    }

    public static class BaseplateModeler
    {
        public static BaseplateCompileResult Compile(BaseplateDefinition b)
        {
            var result = new BaseplateCompileResult();
            result.Report = Validate(b);
            if (result.Report.IsValid) result.Plan = CreatePlan(b);
            return result;
        }

        public static ValidationReport Validate(BaseplateDefinition b)
        {
            var r = new ValidationReport();
            if (string.IsNullOrWhiteSpace(b.Name)) r.Error("Name is empty.");
            if (b.UnitsX < 1 || b.UnitsX > 40) r.Error("UnitsX must be 1 to 40.");
            if (b.UnitsY < 1 || b.UnitsY > 40) r.Error("UnitsY must be 1 to 40.");
            if (b.WebAtTop < 0.05 || b.WebAtTop > 2.0) r.Error("WebAtTop must be 0.05 to 2 mm.");
            if (b.BaseThickness < 1.0) r.Error("BaseThickness must be at least 1 mm.");

            if (b.MagnetHoles)
            {
                if (b.MagnetDiameter < 2 || b.MagnetDiameter > BaseplateSpec.MaxMagnetDiameter)
                    r.Error("MagnetDiameter must be 2 to " + BaseplateSpec.MaxMagnetDiameter + " mm.");
                if (b.MagnetThickness < 0.5) r.Error("MagnetThickness must be at least 0.5 mm.");
                if (b.MagnetFloor < 0.4) r.Error("MagnetFloor must be at least 0.4 mm of material under the magnet.");
                if (b.ScrewHoles && b.ScrewDiameter >= b.MagnetDiameter) r.Error("ScrewDiameter must be smaller than MagnetDiameter.");
            }
            if (b.ScrewHoles && (b.ScrewDiameter < 1 || b.ScrewDiameter > 6)) r.Error("ScrewDiameter must be 1 to 6 mm.");

            if (b.ConnectionSlots)
            {
                if (!(b.SlotsLeft || b.SlotsRight || b.SlotsFront || b.SlotsBack)) r.Warn("ConnectionSlots is on but every side is off, so no slots will be cut.");
                if (b.ClipLength < 5 || b.ClipLength > 30) r.Error("ClipLength must be 5 to 30 mm.");
                if (b.SlotClearance < 0 || b.SlotClearance > 2) r.Error("SlotClearance must be 0 to 2 mm.");
                if (b.SlotLength > GridfinitySpec.Pitch - 12) r.Error("The slot is too long for a 42 mm cell.");
            }
            return r;
        }

        public static List<Vec2> CellCenters(BaseplateDefinition b)
        {
            var list = new List<Vec2>();
            for (int i = 0; i < b.UnitsX; i++)
                for (int j = 0; j < b.UnitsY; j++)
                    list.Add(new Vec2((i - (b.UnitsX - 1) / 2.0) * GridfinitySpec.Pitch,
                                      (j - (b.UnitsY - 1) / 2.0) * GridfinitySpec.Pitch));
            return list;
        }

        /// <summary>
        /// The opening of one cell in the lower layer: a square with a rounded boss left in each corner to hold the
        /// magnet and screw. Centred on (cx, cy). Traversed so every arc is counter-clockwise.
        /// h = half the opening, a = half-width of the plus-shaped arms, c = boss centre offset, R = boss radius.
        /// </summary>
        public static Loop BossedOpening(double cx, double cy, double h, double a, double c)
        {
            Func<double, double, Vec2> P = (x, y) => new Vec2(cx + x, cy + y);
            var l = new Loop();
            l.Segments.Add(Segment.Line(P(-a, -h), P(-a, -c)));
            l.Segments.Add(Segment.Arc(P(-c, -c), P(-a, -c), P(-c, -a)));
            l.Segments.Add(Segment.Line(P(-c, -a), P(-h, -a)));
            l.Segments.Add(Segment.Line(P(-h, -a), P(-h, a)));
            l.Segments.Add(Segment.Line(P(-h, a), P(-c, a)));
            l.Segments.Add(Segment.Arc(P(-c, c), P(-c, a), P(-a, c)));
            l.Segments.Add(Segment.Line(P(-a, c), P(-a, h)));
            l.Segments.Add(Segment.Line(P(-a, h), P(a, h)));
            l.Segments.Add(Segment.Line(P(a, h), P(a, c)));
            l.Segments.Add(Segment.Arc(P(c, c), P(a, c), P(c, a)));
            l.Segments.Add(Segment.Line(P(c, a), P(h, a)));
            l.Segments.Add(Segment.Line(P(h, a), P(h, -a)));
            l.Segments.Add(Segment.Line(P(h, -a), P(c, -a)));
            l.Segments.Add(Segment.Arc(P(c, -c), P(c, -a), P(a, -c)));
            l.Segments.Add(Segment.Line(P(a, -c), P(a, -h)));
            l.Segments.Add(Segment.Line(P(a, -h), P(-a, -h)));
            return l;
        }

        /// <summary>
        /// Plate block; the lower layer's cell openings (with the corner bosses); three profile cuts per cell (the drafted
        /// ones mirror the bin foot); then magnet pockets, screw holes and connection slots.
        /// </summary>
        public static BuildPlan CreatePlan(BaseplateDefinition b)
        {
            double floor = b.EffectiveBase;
            var plan = new BuildPlan
            {
                Name = b.Name,
                ExpectedWidth = b.OuterWidth,
                ExpectedDepth = b.OuterDepth,
                ExpectedHeight = b.TotalHeight
            };
            var cells = CellCenters(b);

            double top = GridfinitySpec.Pitch - b.WebAtTop;                                   // cell opening at the top surface
            double mid = top - 2 * BaseplateSpec.UpperChamfer;                                // vertical section
            double bottom = mid - 2 * BaseplateSpec.LowerChamfer;                             // bottom of the profile
            double topR = GridfinitySpec.BinCornerRadius + (top - GridfinitySpec.CellSize) / 2;
            double midR = topR - BaseplateSpec.UpperChamfer;
            double bottomR = Math.Max(0.3, midR - BaseplateSpec.LowerChamfer);

            plan.Features.Add(new FeaturePlan
            {
                Name = "Plate",
                Op = FeatureOp.Boss,
                StartZ = 0,
                Depth = b.TotalHeight,
                Loops = { Shapes.RoundedRect(0, 0, b.OuterWidth, b.OuterDepth, BaseplateSpec.OuterCornerRadius) }
            });

            // Lower layer: every cell open all the way through, except a boss in each corner.
            double c = BaseplateSpec.HoleOffset;
            double bossR = Math.Max(b.MagnetDiameter / 2, 3.1) + BaseplateSpec.BossWall;
            double arm = c - bossR;
            plan.Features.Add(Cut("Cells - base layer", 0, floor, 0,
                cells.Select(cc => BossedOpening(cc.X, cc.Y, bottom / 2, arm, c))));

            // The profile cut depths stop exactly at each section's top (no overshoot): the drafted cuts keep growing, and
            // neighbouring cells would start to overlap above the top surface.
            plan.Features.Add(Cut("Cells - lower chamfer", floor, BaseplateSpec.LowerChamfer, 45,
                cells.Select(cc => Shapes.RoundedRect(cc.X, cc.Y, bottom, bottom, bottomR))));
            plan.Features.Add(Cut("Cells - vertical", floor + BaseplateSpec.LowerChamfer, BaseplateSpec.Vertical, 0,
                cells.Select(cc => Shapes.RoundedRect(cc.X, cc.Y, mid, mid, midR))));
            plan.Features.Add(Cut("Cells - upper chamfer", floor + BaseplateSpec.LowerChamfer + BaseplateSpec.Vertical,
                BaseplateSpec.UpperChamfer, 45,
                cells.Select(cc => Shapes.RoundedRect(cc.X, cc.Y, mid, mid, midR))));

            // Magnet pockets open upward from MagnetFloor, so the magnet drops in from the top.
            if (b.MagnetHoles)
            {
                var pockets = new List<Loop>();
                foreach (var cc in cells)
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sy in new[] { -1, 1 })
                            pockets.Add(Loop.Circle(new Vec2(cc.X + sx * c, cc.Y + sy * c), b.MagnetDiameter / 2));
                plan.Features.Add(Cut("Magnet pockets", b.MagnetFloor, floor - b.MagnetFloor + 0.2, 0, pockets));
            }

            // Screw holes go through the floor under the magnet, or the whole lower layer when there is no magnet pocket.
            if (b.ScrewHoles)
            {
                var holes = new List<Loop>();
                foreach (var cc in cells)
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sy in new[] { -1, 1 })
                            holes.Add(Loop.Circle(new Vec2(cc.X + sx * c, cc.Y + sy * c), b.ScrewDiameter / 2));
                double through = b.MagnetHoles ? b.MagnetFloor + 0.1 : floor + 0.2;
                plan.Features.Add(Cut("Screw holes", 0, through, 0, holes));
            }

            if (b.ConnectionSlots) AddSlots(b, plan, floor, cells);

            plan.Properties["Description"] = string.Format("Gridfinity baseplate {0}x{1}", b.UnitsX, b.UnitsY);
            return plan;
        }

        // A slot is one stepped notch in the outer wall on a cell edge, as long as the clip plus clearance. It is cut as
        // four bands from the bottom up so each band can stay a simple sketch on the Front Plane.
        private static void AddSlots(BaseplateDefinition b, BuildPlan plan, double floor, List<Vec2> cells)
        {
            double halfLen = b.SlotLength / 2;
            double W = b.OuterWidth / 2, D = b.OuterDepth / 2;
            double inner = BaseplateSpec.UpperChamfer + 0.8;           // reaches into the open cell, so no wall is left behind
            var xs = cells.Select(cc => cc.X).Distinct().OrderBy(v => v).ToList();
            var ys = cells.Select(cc => cc.Y).Distinct().OrderBy(v => v).ToList();

            Func<double, List<Loop>> rects = d0 =>
            {
                var list = new List<Loop>();
                if (b.SlotsLeft) foreach (double y in ys) list.Add(Rect(-W + d0, -W + inner, y - halfLen, y + halfLen));
                if (b.SlotsRight) foreach (double y in ys) list.Add(Rect(W - inner, W - d0, y - halfLen, y + halfLen));
                if (b.SlotsFront) foreach (double x in xs) list.Add(Rect(x - halfLen, x + halfLen, -D + d0, -D + inner));
                if (b.SlotsBack) foreach (double x in xs) list.Add(Rect(x - halfLen, x + halfLen, D - inner, D - d0));
                return list;
            };
            if (!(b.SlotsLeft || b.SlotsRight || b.SlotsFront || b.SlotsBack)) return;

            foreach (double[] band in BaseplateSpec.GrooveBands)
                plan.Features.Add(Cut("Slot groove " + band[0].ToString("0.0"), floor + band[0], band[1], 0, rects(band[2])));

            double topStart = floor + BaseplateSpec.HookTop;
            plan.Features.Add(Cut("Slot opening", topStart, b.TotalHeight - topStart + 0.3, 0, rects(-0.3)));
        }

        private static Loop Rect(double x0, double x1, double y0, double y1)
        {
            return Shapes.RoundedRect((x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0, 0);
        }

        /// <summary>
        /// The clip part: <paramref name="count"/> clips side by side, each the profile of the site's Connection Clip
        /// (4.3 mm wide, 3.6 mm tall, with a hollow underside) extruded <paramref name="length"/> mm. The 0.1 mm tip
        /// rounding of the original is replaced by a small flat.
        /// </summary>
        public static BuildPlan CreateClipPlan(string name, int count, double length)
        {
            const double spacing = 7.0;
            double[][] outline =
            {
                new[] { -2.15, -3.8 }, new[] { -1.05, -3.8 }, new[] { -1.05, -3.55 }, new[] { -1.45, -3.017 },
                new[] { -1.45, -2.45 }, new[] { 1.45, -2.45 }, new[] { 1.45, -3.017 }, new[] { 1.05, -3.55 },
                new[] { 1.05, -3.8 }, new[] { 2.15, -3.8 }, new[] { 2.15, -2.15 }, new[] { 0.212, -0.212 },
                new[] { -0.212, -0.212 }, new[] { -2.15, -2.15 }
            };
            double yShift = 2.006; // centre the section on Y = 0

            var plan = new BuildPlan
            {
                Name = name,
                ExpectedWidth = (count - 1) * spacing + 4.3,
                ExpectedDepth = 3.588,
                ExpectedHeight = length
            };
            var f = new FeaturePlan { Name = "Clips", Op = FeatureOp.Boss, StartZ = 0, Depth = length };
            for (int i = 0; i < count; i++)
            {
                double ox = (i - (count - 1) / 2.0) * spacing;
                var loop = new Loop();
                for (int k = 0; k < outline.Length; k++)
                {
                    double[] p = outline[k], q = outline[(k + 1) % outline.Length];
                    loop.Segments.Add(Segment.Line(new Vec2(ox + p[0], p[1] + yShift), new Vec2(ox + q[0], q[1] + yShift)));
                }
                f.Loops.Add(loop);
            }
            plan.Features.Add(f);
            plan.Properties["Description"] = count + " Gridfinity connection clip(s)";
            return plan;
        }

        private static FeaturePlan Cut(string name, double z, double depth, double draft, IEnumerable<Loop> loops)
        {
            return new FeaturePlan { Name = name, Op = FeatureOp.Cut, StartZ = z, Depth = depth, DraftDegrees = draft, Loops = loops.ToList() };
        }
    }
}
