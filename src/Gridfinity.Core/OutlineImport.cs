using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Gridfinity.Core
{
    /// <summary>One traced object: an outer edge and any holes inside it, in millimetres with Y up.</summary>
    public sealed class TracedShape
    {
        public string Name;
        public string Source;
        public List<Vec2> Outline = new List<Vec2>();
        public List<List<Vec2>> Holes = new List<List<Vec2>>();

        public double Area { get { return Math.Abs(GeometryUtil.SignedArea(Outline)); } }

        public override string ToString() { return Name + " (" + Outline.Count + " points, " + Holes.Count + " holes)"; }
    }

    public enum PaperSize { Letter, A4, Legal, A3, Tabloid, Custom }

    public enum PhotoMode { PaperPhoto, Scan }

    public static class PaperSizes
    {
        /// <summary>Paper size in mm, portrait (width less than height).</summary>
        public static void Size(OutlineImportOptions o, out double w, out double h)
        {
            switch (o.Paper)
            {
                case PaperSize.A4: w = 210; h = 297; break;
                case PaperSize.Legal: w = 215.9; h = 355.6; break;
                case PaperSize.A3: w = 297; h = 420; break;
                case PaperSize.Tabloid: w = 279.4; h = 431.8; break;
                case PaperSize.Custom: w = Math.Min(o.PaperWidth, o.PaperHeight); h = Math.Max(o.PaperWidth, o.PaperHeight); break;
                default: w = 215.9; h = 279.4; break;
            }
        }
    }

    /// <summary>How imported outlines become pockets. Shown in the designer's import window.</summary>
    public sealed class OutlineImportOptions
    {
        [Category("Pocket"), Description("Flat parts: the part's thickness, mm. The cut is this less the Y offset (TopOffset). Round parts ignore it; their depth comes from their width in the outline.")]
        public double Depth { get; set; } = 12;

        [Category("Pocket"), Description("Flat: a flat floor at Depth. Round: the part is taken as round in section, as wide as it is deep at every point (screwdrivers, sockets, pens lying on their side), and the pocket follows that shape in steps.")]
        public PartBottom Bottom { get; set; } = PartBottom.Flat;

        [Category("Pocket"), Description("Round bottoms only: the most the part is taken to be thick, mm. 0 = no limit.")]
        public double RoundMaxThickness { get; set; }

        [Category("Pocket"), Description("Y offset: how far the top of each part sits above the tray's top surface, mm. 0 = flush; 3 leaves 3 mm standing proud to grab. Only what lies below the surface is cut.")]
        public double TopOffset { get; set; }

        [Category("Pocket"), Description("Total clearance added to each outline, mm (half on every side). Empty = use the tray's DefaultClearance (0.4 mm). Photo tracing is less exact than measuring, so 1.0 mm is a good start.")]
        public double? Clearance { get; set; } = 1.0;

        [Category("Pocket"), Description("Leave material standing inside holes in an outline (for example the ring of a box-end wrench). Off cuts them away.")]
        public bool KeepIslands { get; set; }

        [Category("Pocket"), Description("Diameter of a round finger notch at the centre of each pocket, mm. 0 = none.")]
        public double NotchDiameter { get; set; }

        [Category("Pocket"), Description("Let auto-layout turn the pockets by 90 degrees to pack better.")]
        public bool AllowRotate { get; set; } = true;

        [Category("Tray"), Description("SolidBlock: pockets cut down from the top of a solid bin (best for tools). OpenTray: a walled bin with the pockets cut into a thickened floor.")]
        public TrayStyle Style { get; set; } = TrayStyle.SolidBlock;

        [Category("Tray"), Description("Magnet pockets (6.5 mm, for 6 x 2 mm magnets) in all four corners of every cell, as in the Gridfinity specification.")]
        public bool MagnetHoles { get; set; }

        [Category("Tray"), Description("OpenTray only: stacking lip on top of the walls.")]
        public bool StackingLip { get; set; }

        [Category("Tray"), Description("Deboss each part's name (the file or shape name) beside it, along its centreline. You can change or clear each name on the Parts tab.")]
        public bool NameLabels { get; set; }

        [Category("Tray"), Description("Printer the tray must fit. A tray larger than its bed is split along cell lines into bins that fit."), TypeConverter(typeof(PrinterNameConverter))]
        public string Printer { get; set; } = PrinterProfiles.DefaultName;

        [Category("Tray"), Description("Print allowance: extra gap on every side of every cutout, mm, on top of the clearance, because printed pockets come out smaller than drawn. 0.2 suits most FDM printers; raise it if parts fit too tightly.")]
        public double PrintAllowance { get; set; } = 0.2;

        [Category("Tray"), Description("Largest grid the tray may grow to along X, cells. A tray larger than the printer is split into pieces when it is built.")]
        public int MaxUnitsX { get; set; } = 5;

        [Category("Tray"), Description("Largest grid the tray may grow to along Y, cells.")]
        public int MaxUnitsY { get; set; } = 5;

        [Category("Shape"), Description("Points closer than this to a straight line are dropped, mm. 0.1 keeps the shape and makes the sketch far lighter. 0 keeps every traced point.")]
        public double SimplifyTolerance { get; set; } = 0.1;

        [Category("Shape"), Description("Turn each outline so its smallest enclosing rectangle lines up with the grid (long side along X). Packs much better than the angle the part had in the photo.")]
        public bool AlignToAxes { get; set; } = true;

        [Category("Shape"), Description("Shapes smaller than this are ignored as noise from the tracing, mm².")]
        public double MinArea { get; set; } = 20;

        [Category("Shape"), Description("Extra scale factor applied after the file's own units, for example 25.4 for a unitless file drawn in inches.")]
        public double Scale { get; set; } = 1.0;

        [Category("Shape"), Description("Mirror the outlines left to right, for a part traced from the wrong side.")]
        public bool Mirror { get; set; }

        // ---- Photos (the built-in tracer) ----------------------------------------------------------

        [Category("Photo"), Description("Size of the sheet of paper the parts lie on in the photo. Its corners set the scale and remove the camera's perspective.")]
        public PaperSize Paper { get; set; } = PaperSize.Letter;

        [Category("Photo"), Description("Custom paper width, mm.")]
        public double PaperWidth { get; set; } = 216;

        [Category("Photo"), Description("Custom paper height, mm.")]
        public double PaperHeight { get; set; } = 279;

        [Category("Photo"), Description("Scan: a flatbed scan at a known resolution, with no paper edge to find. Photo: a photo of the parts on paper.")]
        public PhotoMode PhotoMode { get; set; } = PhotoMode.PaperPhoto;

        [Category("Photo"), Description("Scan only: the scanner resolution, dots per inch.")]
        public double ScanDpi { get; set; } = 300;

        [Category("Photo"), Description("How readily something counts as part rather than paper, 0 to 100. Raise it for pale parts, lower it if shadows are traced.")]
        public double Sensitivity { get; set; } = 50;

        [Category("Photo"), Description("Ignore this much of the paper around its edge, mm, where the sheet curls or the corners are cast in shadow.")]
        public double PaperBorder { get; set; } = 4;

        [Category("Photo"), Description("Tracing resolution, pixels per mm of paper. 8 resolves 0.125 mm.")]
        public double PixelsPerMm { get; set; } = 8;

        // ---- Scale reference -----------------------------------------------------------------------

        [Category("Scale reference"), Description("An object of known size lying on the paper next to the parts, used to correct the scale: a credit card, a square or a rectangle (for example a steel rule). It is found by its shape and size, left out of the tray, and every part is rescaled so it measures true. None = the paper (or scan resolution) sets the scale.")]
        public ScaleReferenceKind ScaleReference { get; set; } = ScaleReferenceKind.None;

        [Category("Scale reference"), Description("Square: its side, mm. Rectangle: its width, mm. Measure a printed square with calipers rather than trusting the print.")]
        public double ReferenceWidth { get; set; } = 50;

        [Category("Scale reference"), Description("Rectangle only: its height, mm.")]
        public double ReferenceHeight { get; set; } = 25;
    }

    /// <summary>
    /// Reads traced outlines from DXF, SVG, or a ZIP of either (the formats outline-app exports, and most CAD and
    /// drawing programs), and turns them into <see cref="PocketShape.Outline"/> pockets.
    /// </summary>
    public static class OutlineImporter
    {
        public static readonly string FileFilter =
            "Outlines (*.dxf;*.svg;*.zip)|*.dxf;*.svg;*.zip|DXF (*.dxf)|*.dxf|SVG (*.svg)|*.svg|ZIP of DXF/SVG (*.zip)|*.zip|All files (*.*)|*.*";

        // ---- Files -----------------------------------------------------------------------------------

        /// <summary>Reads one file (.dxf, .svg or .zip). Anything skipped or assumed is described in <paramref name="notes"/>.</summary>
        public static List<TracedShape> ReadFile(string path, List<string> notes)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".zip")
            {
                using (var stream = File.OpenRead(path))
                    return ReadZip(stream, name, notes);
            }
            return ReadText(File.ReadAllText(path), ext, name, notes);
        }

        public static List<TracedShape> ReadZip(Stream stream, string name, List<string> notes)
        {
            var shapes = new List<TracedShape>();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (ext != ".dxf" && ext != ".svg") continue;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                        shapes.AddRange(ReadText(reader.ReadToEnd(), ext, name + "-" + Path.GetFileNameWithoutExtension(entry.Name), notes));
                }
            }
            if (shapes.Count == 0) notes.Add(name + ".zip: no DXF or SVG files with closed outlines inside.");
            return shapes;
        }

        public static List<TracedShape> ReadText(string text, string extension, string name, List<string> notes)
        {
            extension = (extension ?? "").ToLowerInvariant();
            if (extension == ".svg" || (extension != ".dxf" && text.TrimStart().StartsWith("<")))
                return ReadSvg(text, name, notes);
            return ReadDxf(text, name, notes);
        }

        // ---- Grouping --------------------------------------------------------------------------------

        /// <summary>
        /// Sorts closed loops into shapes: a loop inside no other is an outer edge, a loop directly inside an outer
        /// edge is a hole in it, a loop inside a hole is a new shape, and so on.
        /// </summary>
        public static List<TracedShape> Group(IEnumerable<List<Vec2>> loops, string name, string source)
        {
            var list = loops.Where(l => l != null && l.Count >= 3)
                            .Select(l => new { Points = l, Area = Math.Abs(GeometryUtil.SignedArea(l)) })
                            .Where(l => l.Area > 1e-6)
                            .OrderByDescending(l => l.Area)
                            .ToList();

            var depth = new int[list.Count];
            var parent = new int[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                parent[i] = -1;
                // The smallest larger loop that contains this one is its parent. Larger loops come first.
                for (int j = i - 1; j >= 0; j--)
                {
                    if (Contains(list[j].Points, list[i].Points))
                    {
                        parent[i] = j;
                        break;
                    }
                }
                depth[i] = parent[i] < 0 ? 0 : depth[parent[i]] + 1;
            }

            var shapes = new List<TracedShape>();
            var shapeOf = new Dictionary<int, TracedShape>();
            for (int i = 0; i < list.Count; i++)
            {
                if (depth[i] % 2 == 0)
                {
                    var s = new TracedShape { Source = source, Outline = Shapes.EnsureCounterClockwise(list[i].Points) };
                    shapes.Add(s);
                    shapeOf[i] = s;
                }
                else
                {
                    shapeOf[parent[i]].Holes.Add(Shapes.EnsureCounterClockwise(list[i].Points));
                }
            }

            for (int i = 0; i < shapes.Count; i++)
                shapes[i].Name = shapes.Count == 1 ? name : name + "-" + (i + 1);
            return shapes;
        }

        // A loop is inside another when most of its points are (robust to traced edges that touch).
        private static bool Contains(List<Vec2> outer, List<Vec2> inner)
        {
            int step = Math.Max(1, inner.Count / 16), inside = 0, tested = 0;
            for (int k = 0; k < inner.Count; k += step)
            {
                tested++;
                if (GeometryUtil.PointInPolygon(inner[k], outer)) inside++;
            }
            return inside * 2 > tested;
        }

        // ---- Pockets ---------------------------------------------------------------------------------

        /// <summary>Turns traced shapes into Outline pockets, centred on their own middle and ready for auto-layout.</summary>
        public static List<Pocket> ToPockets(IEnumerable<TracedShape> shapes, OutlineImportOptions o, List<string> notes)
        {
            o = o ?? new OutlineImportOptions();
            var pockets = new List<Pocket>();
            foreach (var shape in shapes)
            {
                Func<Vec2, Vec2> scale = q => new Vec2((o.Mirror ? -q.X : q.X) * o.Scale, q.Y * o.Scale);
                var outline = OutlineGeometry.Clean(shape.Outline.Select(scale).ToList());
                if (outline == null) { notes.Add(shape.Name + ": skipped, the outline has no area."); continue; }

                double area = Math.Abs(GeometryUtil.SignedArea(outline));
                if (area < o.MinArea)
                {
                    notes.Add(shape.Name + ": skipped, only " + area.ToString("0.#", CultureInfo.InvariantCulture) + " mm² (MinArea is " + o.MinArea + ").");
                    continue;
                }

                var holes = shape.Holes.Select(h => OutlineGeometry.Clean(h.Select(scale).ToList())).Where(h => h != null).ToList();

                double angle = o.AlignToAxes ? OutlineGeometry.AxisAlignAngle(outline) : 0;
                Vec2 lo, hi;
                OutlineGeometry.Bounds(outline.Select(q => q.Rotated(angle)), out lo, out hi);
                Vec2 centre = new Vec2((lo.X + hi.X) / 2, (lo.Y + hi.Y) / 2);
                Func<Vec2, Vec2> place = q => q.Rotated(angle) - centre;

                int before = outline.Count;
                var points = OutlineGeometry.Simplify(outline.Select(place).ToList(), o.SimplifyTolerance);
                var holePoints = holes.Select(h => OutlineGeometry.Simplify(h.Select(place).ToList(), o.SimplifyTolerance)).ToList();

                pockets.Add(new Pocket
                {
                    Name = shape.Name,
                    Shape = PocketShape.Outline,
                    Points = points.Select(Round).ToList(),
                    Holes = holePoints.Count == 0 ? null : holePoints.Select(h => h.Select(Round).ToList()).ToList(),
                    KeepIslands = o.KeepIslands,
                    Width = Math.Round(hi.X - lo.X, 2),
                    Height = Math.Round(hi.Y - lo.Y, 2),
                    Depth = o.Depth,
                    Bottom = o.Bottom,
                    RoundMaxThickness = o.RoundMaxThickness,
                    LabelText = o.NameLabels ? shape.Name : null,
                    LabelHeight = NameLabelHeight,
                    Clearance = o.Clearance,
                    NotchDiameter = o.NotchDiameter,
                    AllowRotate = o.AllowRotate
                });

                notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.#} x {2:0.#} mm, {3} points (from {4}){5}{6}.",
                    shape.Name, hi.X - lo.X, hi.Y - lo.Y, points.Count, before,
                    holes.Count > 0 ? ", " + holes.Count + " hole(s)" : "",
                    Math.Abs(angle) > 1e-6 ? ", turned " + (angle * 180 / Math.PI).ToString("0.#", CultureInfo.InvariantCulture) + " degrees" : ""));
            }
            return pockets;
        }

        private static Vec2 Round(Vec2 q) { return new Vec2(Math.Round(q.X, 3), Math.Round(q.Y, 3)); }

        /// <summary>
        /// A tray holding the shapes, sized for the pocket depth and set to auto-lay out on the smallest grid that fits
        /// (up to MaxUnitsX x MaxUnitsY). Pass it to <see cref="Resolve"/> to lay it out, or edit it first.
        /// </summary>
        public static TrayDefinition CreateTray(string name, IEnumerable<TracedShape> shapes, OutlineImportOptions o, List<string> notes)
        {
            o = o ?? new OutlineImportOptions();
            var t = new TrayDefinition
            {
                Name = string.IsNullOrWhiteSpace(name) ? "OutlineTray" : name,
                Style = o.Style,
                MagnetHoles = o.MagnetHoles,
                StackingLip = o.StackingLip && o.Style == TrayStyle.OpenTray,
                AutoLayout = true,
                AutoGrowGrid = true,
                UnitsX = 1,
                UnitsY = 1,
                MaxUnitsX = o.MaxUnitsX,
                MaxUnitsY = o.MaxUnitsY,
            };
            t.PartTopOffset = o.TopOffset;
            t.Printer = o.Printer;
            t.PrintAllowance = o.PrintAllowance;
            t.Pockets.AddRange(ToPockets(shapes, o, notes));
            FitHeight(t);
            foreach (var p in t.Pockets.Where(q => q.Bottom == PartBottom.Round))
                notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}: round bottom, up to {1:0.#} mm thick, cut {2:0.#} mm deep.",
                    p.Name, PartCuts.Field(p, t) == null ? 0 : PartCuts.Field(p, t).Max, PartCuts.MaxCutDepth(p, t)));
            return t;
        }

        /// <summary>The deepest cut of any pocket in the tray, mm.</summary>
        public static double DeepestCut(TrayDefinition t)
        {
            return t.Pockets.Count == 0 ? 0 : t.Pockets.Max(p => PartCuts.MaxCutDepth(p, t));
        }

        /// <summary>
        /// Open trays: thickens the floor to hold the deepest pocket. Both styles: raises HeightUnits (never lowers
        /// it) to the fewest that hold the deepest pocket.
        /// </summary>
        public static void FitHeight(TrayDefinition t) { FitHeight(t, false); }

        /// <param name="raiseOnly">Keep a taller HeightUnits the user chose (the Parts tab); otherwise use the fewest that work.</param>
        public static void FitHeight(TrayDefinition t, bool raiseOnly)
        {
            double depth = Math.Max(0, DeepestCut(t));
            if (t.Style == TrayStyle.OpenTray)
            {
                // Pockets are cut into the floor, so the floor has to be thick enough to hold them.
                double minFloor = t.MagnetHoles ? GridfinitySpec.MagnetDepth + 0.8 : 0.8;
                double floor = Math.Max(GridfinitySpec.BaseHeight, Math.Max(minFloor, depth + t.MinFloorBelowPocket));
                t.FloorHeight = raiseOnly ? Math.Max(t.FloorHeight, floor) : floor;
            }
            int needed = HeightUnitsFor(t, depth);
            t.HeightUnits = raiseOnly ? Math.Max(t.HeightUnits, needed) : needed;
        }

        /// <summary>
        /// Lays the tray out and validates it. With <paramref name="nameLabels"/>, adds each pocket's name under it.
        /// The result's <see cref="CompileResult.Resolved"/> has AutoLayout and AutoGrowGrid switched off, so saving it
        /// and building it later gives exactly this layout.
        /// </summary>
        public static CompileResult Resolve(TrayDefinition tray, bool nameLabels)
        {
            // Names are laid out with their part (they are part of its footprint), so they are set before layout.
            if (nameLabels)
                foreach (var p in tray.Pockets.Where(q => q.Shape == PocketShape.Outline && string.IsNullOrWhiteSpace(q.LabelText)))
                {
                    p.LabelText = p.Name;
                    if (p.LabelHeight <= 0) p.LabelHeight = NameLabelHeight;
                }
            CompileResult c = TrayCompiler.Compile(tray);
            if (!c.Success) return c;

            var t = c.Resolved.Clone();
            t.AutoLayout = false;
            t.AutoGrowGrid = false;
            return TrayCompiler.Compile(t);
        }

        public const double NameLabelHeight = 5.0;

        /// <summary>The fewest 7 mm height units that leave the tray's minimum floor under a pocket of this depth.</summary>
        public static int HeightUnitsFor(TrayDefinition t, double depth)
        {
            // Solid block: the pocket plus the feet plus the minimum floor. Open tray: the floor holding the pockets
            // plus at least 3 mm of wall above it (the validator's limit), rounded up to whole 7 mm units.
            double needed = t.Style == TrayStyle.SolidBlock
                ? depth + GridfinitySpec.BaseHeight + t.MinFloorBelowPocket
                : t.FloorHeight + 3.0;
            return Math.Max(1, (int)Math.Ceiling((needed - 1e-9) / GridfinitySpec.HeightUnit));
        }

        // ---- DXF -------------------------------------------------------------------------------------

        /// <summary>
        /// Closed LWPOLYLINE and POLYLINE entities (with bulge arcs), CIRCLEs and ELLIPSEs from the ENTITIES section.
        /// Units come from $INSUNITS (millimetres when unset).
        /// </summary>
        public static List<TracedShape> ReadDxf(string text, string name, List<string> notes)
        {
            var pairs = DxfPairs(text);
            double unit = 1.0;
            var loops = new List<List<Vec2>>();
            int open = 0, unsupported = 0;
            var unsupportedKinds = new SortedSet<string>();

            string section = null;
            for (int i = 0; i < pairs.Count; i++)
            {
                var p = pairs[i];
                if (p.Code == 0 && p.Value == "SECTION" && i + 1 < pairs.Count && pairs[i + 1].Code == 2) { section = pairs[i + 1].Value; i++; continue; }
                if (p.Code == 0 && p.Value == "ENDSEC") { section = null; continue; }

                if (section == "HEADER" && p.Code == 9 && p.Value == "$INSUNITS" && i + 1 < pairs.Count)
                {
                    unit = UnitScale(ParseInt(pairs[i + 1].Value), name, notes);
                    i++;
                    continue;
                }
                if (section != "ENTITIES" || p.Code != 0) continue;

                int end = i + 1;
                while (end < pairs.Count && pairs[end].Code != 0) end++;
                var e = pairs.GetRange(i + 1, end - i - 1);

                switch (p.Value)
                {
                    case "LWPOLYLINE":
                        {
                            var verts = new List<Vec2>();
                            var bulges = new List<double>();
                            bool closed = (ParseInt(Get(e, 70, "0")) & 1) == 1;
                            double x = 0;
                            foreach (var q in e)
                            {
                                if (q.Code == 10) x = ParseDouble(q.Value);
                                else if (q.Code == 20) { verts.Add(new Vec2(x, ParseDouble(q.Value))); bulges.Add(0); }
                                else if (q.Code == 42 && bulges.Count > 0) bulges[bulges.Count - 1] = ParseDouble(q.Value);
                            }
                            AddPolyline(loops, verts, bulges, closed, ref open);
                            i = end - 1;
                            break;
                        }
                    case "POLYLINE":
                        {
                            bool closed = (ParseInt(Get(e, 70, "0")) & 1) == 1;
                            var verts = new List<Vec2>();
                            var bulges = new List<double>();
                            int k = end;
                            while (k < pairs.Count && !(pairs[k].Code == 0 && pairs[k].Value == "SEQEND"))
                            {
                                if (pairs[k].Code == 0 && pairs[k].Value == "VERTEX")
                                {
                                    int vEnd = k + 1;
                                    while (vEnd < pairs.Count && pairs[vEnd].Code != 0) vEnd++;
                                    var v = pairs.GetRange(k + 1, vEnd - k - 1);
                                    verts.Add(new Vec2(ParseDouble(Get(v, 10, "0")), ParseDouble(Get(v, 20, "0"))));
                                    bulges.Add(ParseDouble(Get(v, 42, "0")));
                                    k = vEnd;
                                }
                                else k++;
                            }
                            AddPolyline(loops, verts, bulges, closed, ref open);
                            i = k;
                            break;
                        }
                    case "CIRCLE":
                        {
                            var c = new Vec2(ParseDouble(Get(e, 10, "0")), ParseDouble(Get(e, 20, "0")));
                            double r = ParseDouble(Get(e, 40, "0"));
                            if (r > 0) loops.Add(Loop.Circle(c, r).ToPolyline(3.0));
                            i = end - 1;
                            break;
                        }
                    case "ELLIPSE":
                        {
                            var c = new Vec2(ParseDouble(Get(e, 10, "0")), ParseDouble(Get(e, 20, "0")));
                            var major = new Vec2(ParseDouble(Get(e, 11, "0")), ParseDouble(Get(e, 21, "0")));
                            double ratio = ParseDouble(Get(e, 40, "1"));
                            double a0 = ParseDouble(Get(e, 41, "0")), a1 = ParseDouble(Get(e, 42, (2 * Math.PI).ToString(CultureInfo.InvariantCulture)));
                            if (Math.Abs(a1 - a0 - 2 * Math.PI) < 1e-6 || Math.Abs(a1 - a0) < 1e-9)
                            {
                                var minor = new Vec2(-major.Y, major.X) * ratio;
                                var pts = new List<Vec2>();
                                for (int s = 0; s < 120; s++)
                                {
                                    double a = 2 * Math.PI * s / 120;
                                    pts.Add(c + major * Math.Cos(a) + minor * Math.Sin(a));
                                }
                                loops.Add(pts);
                            }
                            else open++;
                            i = end - 1;
                            break;
                        }
                    case "LINE": case "ARC": case "SPLINE":
                        unsupported++;
                        unsupportedKinds.Add(p.Value);
                        i = end - 1;
                        break;
                }
            }

            if (open > 0) notes.Add(name + ": skipped " + open + " open polyline(s); only closed outlines become pockets.");
            if (unsupported > 0)
                notes.Add(name + ": skipped " + unsupported + " " + string.Join("/", unsupportedKinds) + " entit" + (unsupported == 1 ? "y" : "ies") +
                          ". Join them into a closed polyline (or export from outline-app, which writes closed polylines).");

            var scaled = loops.Select(l => l.Select(q => q * unit).ToList());
            var shapes = Group(scaled, name, name + ".dxf");
            if (shapes.Count == 0) notes.Add(name + ": no closed outlines found.");
            return shapes;
        }

        private static void AddPolyline(List<List<Vec2>> loops, List<Vec2> verts, List<double> bulges, bool closed, ref int open)
        {
            if (verts.Count < 2) return;
            if (!closed && (verts[0] - verts[verts.Count - 1]).Length < 1e-3) closed = true;
            if (!closed) { open++; return; }

            int n = verts.Count;
            if ((verts[0] - verts[n - 1]).Length < 1e-9) { verts.RemoveAt(n - 1); bulges.RemoveAt(n - 1); n--; }

            var pts = new List<Vec2>();
            for (int k = 0; k < n; k++)
            {
                Vec2 a = verts[k], b = verts[(k + 1) % n];
                pts.Add(a);
                double bulge = bulges[k];
                if (Math.Abs(bulge) < 1e-9) continue;

                // A bulge is tan(included angle / 4); positive is counter-clockwise.
                double theta = 4 * Math.Atan(bulge);
                double chord = (b - a).Length;
                if (chord < 1e-9) continue;
                double r = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
                Vec2 mid = (a + b) * 0.5;
                Vec2 dir = (b - a).Normalized();
                Vec2 normal = new Vec2(-dir.Y, dir.X);
                // cos goes negative past a half circle, which correctly puts the centre on the other side of the chord.
                double h = r * Math.Cos(Math.Abs(theta) / 2) * Math.Sign(bulge);
                Vec2 centre = mid + normal * h;
                double start = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
                int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(theta) * 180 / Math.PI / 3.0));
                for (int s = 1; s < steps; s++)
                {
                    double ang = start + theta * s / steps;
                    pts.Add(new Vec2(centre.X + r * Math.Cos(ang), centre.Y + r * Math.Sin(ang)));
                }
            }
            loops.Add(pts);
        }

        private static double UnitScale(int insunits, string name, List<string> notes)
        {
            switch (insunits)
            {
                case 0: return 1.0;      // unitless: assume millimetres
                case 1: notes.Add(name + ": drawing units are inches, converted to mm."); return 25.4;
                case 2: notes.Add(name + ": drawing units are feet, converted to mm."); return 304.8;
                case 4: return 1.0;      // millimetres
                case 5: notes.Add(name + ": drawing units are centimetres, converted to mm."); return 10.0;
                case 6: notes.Add(name + ": drawing units are metres, converted to mm."); return 1000.0;
                default: notes.Add(name + ": unknown $INSUNITS " + insunits + "; assuming millimetres."); return 1.0;
            }
        }

        private struct DxfPair { public int Code; public string Value; }

        private static List<DxfPair> DxfPairs(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var pairs = new List<DxfPair>(lines.Length / 2);
            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                int code;
                if (!int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
                    throw new FormatException("Not a text DXF file (line " + (i + 1) + " should be a group code). Binary DXF is not supported.");
                pairs.Add(new DxfPair { Code = code, Value = lines[i + 1].Trim() });
            }
            return pairs;
        }

        private static string Get(List<DxfPair> e, int code, string fallback)
        {
            foreach (var q in e) if (q.Code == code) return q.Value;
            return fallback;
        }

        // ---- SVG -------------------------------------------------------------------------------------

        /// <summary>
        /// Closed paths, polygons, rects, circles and ellipses. Sizes come from the width/height (mm, cm, in, pt, px)
        /// and viewBox; with neither, one unit is taken as one millimetre. SVG's downward Y is flipped to point up.
        /// </summary>
        public static List<TracedShape> ReadSvg(string text, string name, List<string> notes)
        {
            var loops = SvgReader.Read(text, name, notes);
            var shapes = Group(loops, name, name + ".svg");
            if (shapes.Count == 0) notes.Add(name + ": no closed outlines found.");
            return shapes;
        }

        // ---- Parsing helpers -------------------------------------------------------------------------

        internal static double ParseDouble(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static int ParseInt(string s)
        {
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return (int)ParseDouble(s);
        }
    }
}
