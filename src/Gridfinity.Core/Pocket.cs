using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Gridfinity.Core
{
    public enum PocketShape
    {
        /// <summary>Width x Height rectangle, optional corner radius.</summary>
        Rectangle,
        /// <summary>Round hole; Width is the diameter.</summary>
        Circle,
        /// <summary>Obround slot; Width is overall length, Height is slot width.</summary>
        Slot,
        /// <summary>Hexagon; Width is across flats.</summary>
        Hex,
        /// <summary>Convex outline from <see cref="Pocket.Points"/> (JSON only).</summary>
        Polygon,
        /// <summary>
        /// A round-bottomed trough for a cylindrical part lying on its side (a socket). Width is the part's diameter
        /// (X), Height its length (Y). Clearance is added to both. Depth is measured to the lowest point of the trough.
        /// The sides run straight up from the centre line, so the part drops in and lifts out freely. RotationDeg must be 0.
        /// </summary>
        Cradle,
        /// <summary>
        /// Any traced shape, convex or not, such as an outline exported from outline-app (DXF or SVG).
        /// <see cref="Pocket.Points"/> is the outer edge relative to the pocket centre; <see cref="Pocket.Holes"/>
        /// are optional holes inside it. Clearance grows the edge by half its value all round (and shrinks holes),
        /// rounding the corners. Width and Height are informational (set to the size on import).
        /// </summary>
        Outline
    }

    /// <summary>One cut-out in a tray or holder block: a socket, wrench, bit, screwdriver, and so on.</summary>
    public class Pocket
    {
        [Category("Identity"), Description("Label for your own reference.")]
        public string Name { get; set; } = "Pocket";

        [Category("Shape")]
        public PocketShape Shape { get; set; } = PocketShape.Rectangle;

        [Category("Shape"), Description("Size along the pocket's local X axis (length, diameter, or across flats), mm.")]
        public double Width { get; set; } = 20;

        [Category("Shape"), Description("Size along local Y (rectangle height or slot width), mm. Ignored for Circle and Hex.")]
        public double Height { get; set; } = 10;

        [Category("Shape"), Description("Corner radius for Rectangle, Hex and Polygon, mm.")]
        public double CornerRadius { get; set; }

        [Browsable(false), Description("Polygon: convex outline. Outline: the traced outer edge (any shape). mm, relative to the pocket centre.")]
        public List<Vec2> Points { get; set; }

        [Browsable(false), Description("Outline only: holes inside the traced shape (for example the ring of a box-end wrench).")]
        public List<List<Vec2>> Holes { get; set; }

        [Category("Shape"), Description("Outline only: leave material standing inside the traced holes (an island the part sits over). Off cuts the holes away with the rest of the pocket.")]
        public bool KeepIslands { get; set; }

        [Category("Depth"), Description("Pocket depth, mm.")]
        public double Depth { get; set; } = 6;

        [Category("Fit"), Description("Total clearance added to the nominal size, mm. Empty = use the tray default.")]
        public double? Clearance { get; set; }

        [Category("Placement"), Description("Centre X relative to the bin centre, mm. Overwritten when the tray has AutoLayout on.")]
        public double X { get; set; }

        [Category("Placement"), Description("Centre Y relative to the bin centre, mm. Overwritten when the tray has AutoLayout on.")]
        public double Y { get; set; }

        [Category("Placement"), Description("Rotation about the pocket centre, degrees CCW.")]
        public double RotationDeg { get; set; }

        [Category("Placement"), Description("Let AutoLayout turn this pocket by 90 degrees to pack better.")]
        public bool AllowRotate { get; set; } = true;

        [Category("Finger notch"), Description("Diameter of an optional round finger notch cut at the same depth, mm. 0 = none.")]
        public double NotchDiameter { get; set; }

        [Category("Finger notch"), Description("Notch centre along the pocket's local X from its centre, mm.")]
        public double NotchOffset { get; set; }

        public override string ToString() { return Name + " (" + Shape + ")"; }

        // ---- Convenience constructors ---------------------------------------------------------

        public static Pocket Rect(string name, double length, double width, double depth, double cornerRadius = 0)
        {
            return new Pocket { Name = name, Shape = PocketShape.Rectangle, Width = length, Height = width, Depth = depth, CornerRadius = cornerRadius };
        }

        public static Pocket CradlePocket(string name, double diameter, double length, double depth)
        {
            return new Pocket { Name = name, Shape = PocketShape.Cradle, Width = diameter, Height = length, Depth = depth, AllowRotate = false };
        }

        public static Pocket Round(string name, double diameter, double depth)
        {
            return new Pocket { Name = name, Shape = PocketShape.Circle, Width = diameter, Height = diameter, Depth = depth };
        }

        public static Pocket SlotPocket(string name, double length, double width, double depth)
        {
            return new Pocket { Name = name, Shape = PocketShape.Slot, Width = length, Height = width, Depth = depth };
        }

        public static Pocket Hex(string name, double acrossFlats, double depth)
        {
            return new Pocket { Name = name, Shape = PocketShape.Hex, Width = acrossFlats, Height = acrossFlats, Depth = depth };
        }

        // ---- Geometry ------------------------------------------------------------------------

        /// <summary>
        /// Cross-section of a <see cref="PocketShape.Cradle"/> in (X, Z): straight sides, a half-round bottom whose lowest
        /// point is at <paramref name="bottomZ"/>, open at <paramref name="topZ"/>. Counter-clockwise.
        /// </summary>
        public Loop BuildCradleSection(double defaultClearance, double bottomZ, double topZ)
        {
            double c = Clearance ?? defaultClearance;
            double r = (Width + c) / 2;
            double zc = bottomZ + r;
            if (topZ < zc) topZ = zc + 0.01;
            var left = new Vec2(X - r, zc);
            var right = new Vec2(X + r, zc);
            var l = new Loop();
            l.Segments.Add(Segment.Line(new Vec2(X - r, topZ), left));
            l.Segments.Add(Segment.Arc(new Vec2(X, zc), left, right));
            l.Segments.Add(Segment.Line(right, new Vec2(X + r, topZ)));
            l.Segments.Add(Segment.Line(new Vec2(X + r, topZ), new Vec2(X - r, topZ)));
            return l;
        }

        /// <summary>Y extent (low, high) of a cradle, clearance included.</summary>
        public void GetCradleYRange(double defaultClearance, out double yLow, out double yHigh)
        {
            double len = Height + (Clearance ?? defaultClearance);
            yLow = Y - len / 2;
            yHigh = Y + len / 2;
        }

        /// <summary>The pocket outline at its X/Y/rotation, with clearance applied.</summary>
        public Loop BuildLoop(double defaultClearance)
        {
            return MakeOutline(defaultClearance).Transformed(RotationDeg * Math.PI / 180.0, new Vec2(X, Y));
        }

        /// <summary>The finger-notch circle at the pocket's placement, or null when there is none.</summary>
        public Loop BuildNotchLoop(double defaultClearance)
        {
            if (NotchDiameter <= 0) return null;
            return Loop.Circle(new Vec2(NotchOffset, 0), NotchDiameter / 2)
                       .Transformed(RotationDeg * Math.PI / 180.0, new Vec2(X, Y));
        }

        /// <summary>Bounding box (outline plus notch) relative to the pocket centre for a given rotation.</summary>
        public void GetLocalBounds(double defaultClearance, double rotationDeg, out Vec2 min, out Vec2 max)
        {
            double rot = rotationDeg * Math.PI / 180.0;
            Vec2 lo, hi;
            MakeOutline(defaultClearance).Transformed(rot, new Vec2(0, 0)).GetBounds(out lo, out hi);

            if (NotchDiameter > 0)
            {
                Vec2 nlo, nhi;
                Loop.Circle(new Vec2(NotchOffset, 0), NotchDiameter / 2)
                    .Transformed(rot, new Vec2(0, 0)).GetBounds(out nlo, out nhi);
                lo = new Vec2(Math.Min(lo.X, nlo.X), Math.Min(lo.Y, nlo.Y));
                hi = new Vec2(Math.Max(hi.X, nhi.X), Math.Max(hi.Y, nhi.Y));
            }
            min = lo;
            max = hi;
        }

        private Loop MakeOutline(double defaultClearance)
        {
            double c = Clearance ?? defaultClearance;
            switch (Shape)
            {
                case PocketShape.Circle:
                    return Loop.Circle(new Vec2(0, 0), (Width + c) / 2);

                case PocketShape.Slot:
                    {
                        double length = Width + c, slotWidth = Height + c;
                        double half = Math.Max(0, (length - slotWidth) / 2);
                        return Shapes.Slot(new Vec2(-half, 0), new Vec2(half, 0), slotWidth);
                    }

                case PocketShape.Hex:
                    return Shapes.RoundedPolygon(Shapes.RegularPolygon(6, Width + c), Grow(CornerRadius, c));

                case PocketShape.Polygon:
                    if (Points == null || Points.Count < 3)
                        throw new InvalidOperationException("Pocket '" + Name + "': Polygon needs at least 3 Points.");
                    return Shapes.RoundedPolygon(Points, CornerRadius);

                case PocketShape.Outline:
                    return OutlineFootprint(c);

                case PocketShape.Cradle:
                    // The footprint seen from above (used for layout and overlap checks).
                    return Shapes.RoundedRect(0, 0, Width + c, Height + c, 0);

                default:
                    return Shapes.RoundedRect(0, 0, Width + c, Height + c, Grow(CornerRadius, c));
            }
        }

        /// <summary>
        /// Outline pockets only: the islands left standing inside traced holes, at the pocket's placement, shrunk by
        /// half the clearance. Empty unless <see cref="KeepIslands"/> is on. Tiny pieces (under 2 mm across) are dropped.
        /// </summary>
        public List<Loop> BuildIslandLoops(double defaultClearance)
        {
            var loops = new List<Loop>();
            if (Shape != PocketShape.Outline || !KeepIslands || Holes == null) return loops;

            double c = Clearance ?? defaultClearance;
            double rot = RotationDeg * Math.PI / 180.0;
            foreach (var hole in Holes)
            {
                if (hole == null || hole.Count < 3) continue;
                foreach (var piece in OutlineGeometry.Offset(hole, -c / 2))
                {
                    Vec2 lo, hi;
                    OutlineGeometry.Bounds(piece, out lo, out hi);
                    if (Math.Min(hi.X - lo.X, hi.Y - lo.Y) < MinIslandSize) continue;
                    loops.Add(OutlineGeometry.ToLoop(piece).Transformed(rot, new Vec2(X, Y)));
                }
            }
            return loops;
        }

        /// <summary>Islands narrower than this are left out: they would not print or would snap off.</summary>
        public const double MinIslandSize = 2.0;

        // Offsetting a traced outline is the slow part of layout (it runs for every grid size tried), so the
        // result is cached against the clearance and the point list it was made from.
        [Newtonsoft.Json.JsonIgnore] private Loop _footprint;
        [Newtonsoft.Json.JsonIgnore] private double _footprintClearance = double.NaN;
        [Newtonsoft.Json.JsonIgnore] private List<Vec2> _footprintSource;
        [Newtonsoft.Json.JsonIgnore] private int _footprintCount;

        private Loop OutlineFootprint(double clearance)
        {
            if (Points == null || Points.Count < 3)
                throw new InvalidOperationException("Pocket '" + Name + "': Outline needs at least 3 Points.");

            if (_footprint != null && ReferenceEquals(_footprintSource, Points) && _footprintCount == Points.Count &&
                Math.Abs(_footprintClearance - clearance) < 1e-12)
                return _footprint;

            var grown = OutlineGeometry.OffsetLargest(Points, clearance / 2);
            if (grown == null)
                throw new InvalidOperationException("Pocket '" + Name + "': the outline has no area.");

            _footprint = OutlineGeometry.ToLoop(grown);
            _footprintClearance = clearance;
            _footprintSource = Points;
            _footprintCount = Points.Count;
            return _footprint;
        }

        // A nominal radius grows by half the clearance; a sharp corner stays sharp.
        private static double Grow(double radius, double clearance)
        {
            return radius > 0 ? radius + clearance / 2 : 0;
        }
    }
}
