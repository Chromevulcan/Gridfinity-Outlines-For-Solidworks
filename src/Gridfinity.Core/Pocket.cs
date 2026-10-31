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

    /// <summary>The underside of a traced part, used to shape its pocket (Outline pockets only).</summary>
    public enum PartBottom
    {
        /// <summary>A flat part (a wrench, a hex key): the pocket has a flat floor at the part's thickness.</summary>
        Flat,
        /// <summary>
        /// A round part lying on its side (a screwdriver, a socket, a pen): the cross-section is taken as round, with its
        /// diameter equal to the part's local width in the photo, so the pocket deepens where the part is wider.
        /// </summary>
        Round
    }

    public enum LabelSide { Below, Above }

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

        [Category("Depth"), Description("Pocket depth, mm. Outline pockets with a flat bottom: the part's thickness (the cut is this minus the Y offset). Ignored for round bottoms.")]
        public double Depth { get; set; } = 6;

        // ---- Traced parts (Outline pockets) -----------------------------------------------------

        [Category("Part"), Description("Flat: a flat floor at the part's thickness (Depth). Round: a round bottom whose depth follows the part's width along its length, as for a screwdriver or socket lying on its side.")]
        public PartBottom Bottom { get; set; } = PartBottom.Flat;

        [Category("Part"), Description("Round bottoms only: limit how thick the part is taken to be, mm. Use it for a part that is round in one place and flat in another. 0 = no limit.")]
        public double RoundMaxThickness { get; set; }

        [Category("Part"), Description("Height of the top of this part above the tray's top surface, mm (the Y offset). Only what lies below the surface is cut. Empty = the tray's PartTopOffset.")]
        public double? TopOffset { get; set; }

        [Category("Part"), Description("Name debossed beside the part, along its centreline. Empty = no name.")]
        public string LabelText { get; set; }

        [Category("Part"), Description("Character height of the name, mm.")]
        public double LabelHeight { get; set; } = 5;

        [Category("Part"), Description("Which side of the centreline the name goes on, before the part is turned by layout.")]
        public LabelSide LabelSide { get; set; } = LabelSide.Below;

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

            // Leave room for the name beside the part so layout never puts another part on top of it.
            var band = LabelBandLocal(defaultClearance);
            if (band != null)
            {
                foreach (var corner in band)
                {
                    var q = corner.Rotated(rot);
                    lo = new Vec2(Math.Min(lo.X, q.X), Math.Min(lo.Y, q.Y));
                    hi = new Vec2(Math.Max(hi.X, q.X), Math.Max(hi.Y, q.Y));
                }
            }
            min = lo;
            max = hi;
        }

        // ---- Centreline and name ----------------------------------------------------------------

        /// <summary>Gap between the edge of the pocket and the name beside it, mm.</summary>
        public const double LabelGap = 1.2;

        [Newtonsoft.Json.JsonIgnore, Browsable(false)]
        public bool HasLabel { get { return !string.IsNullOrWhiteSpace(LabelText) && LabelHeight > 0; } }

        /// <summary>
        /// The part's centreline: through the middle of its outline along its long axis (local X; the importer
        /// turns every traced part that way), from end to end, at the pocket's placement.
        /// </summary>
        public void GetCenterline(double defaultClearance, out Vec2 start, out Vec2 end)
        {
            Vec2 lo, hi;
            MakeOutline(defaultClearance).GetBounds(out lo, out hi);
            double y = (lo.Y + hi.Y) / 2, rot = RotationDeg * Math.PI / 180.0;
            var at = new Vec2(X, Y);
            start = new Vec2(lo.X, y).Rotated(rot) + at;
            end = new Vec2(hi.X, y).Rotated(rot) + at;
        }

        /// <summary>
        /// Where the name goes: a line parallel to the centreline, beside the pocket, that the text is centred on,
        /// running left to right as you read it (so the text is never upside down whichever way layout turned
        /// the part). Null when there is no name.
        /// </summary>
        public bool GetLabelLine(double defaultClearance, out Vec2 start, out Vec2 end)
        {
            start = end = new Vec2(0, 0);
            if (!HasLabel) return false;
            Vec2 lo, hi;
            MakeOutline(defaultClearance).GetBounds(out lo, out hi);
            double y = LabelSide == LabelSide.Below ? lo.Y - LabelGap - LabelHeight / 2 : hi.Y + LabelGap + LabelHeight / 2;
            double rot = RotationDeg * Math.PI / 180.0;
            var at = new Vec2(X, Y);
            start = new Vec2(lo.X, y).Rotated(rot) + at;
            end = new Vec2(hi.X, y).Rotated(rot) + at;

            // Read left to right; a line pointing straight down reads bottom to top.
            Vec2 d = end - start;
            if (d.X < -1e-9 || (Math.Abs(d.X) <= 1e-9 && d.Y < 0)) { var t = start; start = end; end = t; }
            return true;
        }

        /// <summary>The name's rectangle in local (unrotated) coordinates, or null.</summary>
        private List<Vec2> LabelBandLocal(double defaultClearance)
        {
            if (!HasLabel) return null;
            Vec2 lo, hi;
            MakeOutline(defaultClearance).GetBounds(out lo, out hi);
            double near = LabelSide == LabelSide.Below ? lo.Y - LabelGap : hi.Y + LabelGap;
            double far = LabelSide == LabelSide.Below ? near - LabelHeight : near + LabelHeight;
            return new List<Vec2> { new Vec2(lo.X, near), new Vec2(hi.X, near), new Vec2(hi.X, far), new Vec2(lo.X, far) };
        }

        /// <summary>The name's rectangle at the pocket's placement, or null.</summary>
        public List<Vec2> BuildLabelBand(double defaultClearance)
        {
            var band = LabelBandLocal(defaultClearance);
            if (band == null) return null;
            double rot = RotationDeg * Math.PI / 180.0;
            return band.Select(q => q.Rotated(rot) + new Vec2(X, Y)).ToList();
        }

        /// <summary>The outline (with clearance) in local coordinates: centred, unrotated.</summary>
        public Loop BuildLocalLoop(double defaultClearance) { return MakeOutline(defaultClearance); }

        /// <summary>Islands in local coordinates (see <see cref="BuildIslandLoops"/>).</summary>
        public List<List<Vec2>> BuildLocalIslands(double defaultClearance)
        {
            var list = new List<List<Vec2>>();
            if (Shape != PocketShape.Outline || !KeepIslands || Holes == null) return list;
            double c = Clearance ?? defaultClearance;
            foreach (var hole in Holes)
            {
                if (hole == null || hole.Count < 3) continue;
                foreach (var piece in OutlineGeometry.Offset(hole, -c / 2))
                {
                    Vec2 lo, hi;
                    OutlineGeometry.Bounds(piece, out lo, out hi);
                    if (Math.Min(hi.X - lo.X, hi.Y - lo.Y) >= MinIslandSize) list.Add(piece);
                }
            }
            return list;
        }

        // Height field cache for round bottoms (see PartCuts).
        [Newtonsoft.Json.JsonIgnore] internal object HeightCache;
        [Newtonsoft.Json.JsonIgnore] internal string HeightCacheKey;

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
