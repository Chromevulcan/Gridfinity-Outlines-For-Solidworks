using System;
using System.Collections.Generic;
using Gridfinity.Core;
using SolidWorks.Interop.sldworks;
using Loop = Gridfinity.Core.Loop;
using Segment = Gridfinity.Core.Segment;
using SolidWorks.Interop.swconst;

namespace Gridfinity.Sw
{
    public sealed class BuildException : Exception
    {
        public BuildException(string message) : base(message) { }
    }

    /// <summary>
    /// How this SolidWorks install interprets extrude direction and start offset on the Front Plane.
    /// Measured by <see cref="DirectionProbe"/>; defaults assume everything already points +Z.
    /// </summary>
    internal sealed class DirectionFlags
    {
        public bool ReverseBoss;
        public bool ReverseCut;
        public bool FlipStartOffset;

        // Top-plane (cradle) cuts: how the Top Plane sketch axes map to the part, and which extrude settings send a cut
        // to +Y from an offset. Measured by DirectionProbe.ProbeTopPlane.
        public int TopSx = 1;          // sketch X = TopSx * part X
        public int TopSy = 1;          // sketch Y = TopSy * part Z
        public bool TopReverse;
        public bool TopFlipStart;
        public bool TopOk;             // true once the probe has found a combination that works
        public bool TopProbing;        // true while the probe itself is running

        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>Replays one <see cref="FeaturePlan"/> as a sketch on the Front Plane plus an extrude or cut.</summary>
    internal static class FeatureExecutor
    {
        public static double M(double mm) { return mm / 1000.0; }

        public static Feature Execute(ModelDoc2 model, FeaturePlan fp, DirectionFlags flags)
        {
            if (fp.Loops.Count == 0 && fp.Texts.Count == 0 && fp.Lines.Count == 0) throw new BuildException("Feature '" + fp.Name + "' has no sketch geometry.");

            if (fp.Op == FeatureOp.Sketch) return ReferenceSketch(model, fp);

            bool top = fp.Plane == SketchPlane.Top;
            if (top && !flags.TopOk && !flags.TopProbing)
                throw new BuildException("The round-bottomed cradles need the Top-plane check, which did not succeed (see the notes above). " +
                                         "Change the pockets' Shape to Rectangle to build without it.");

            DrawSketch(model, fp, flags);

            FeatureManager fm = model.FeatureManager;
            double depth = M(fp.Depth);
            bool draft = Math.Abs(fp.DraftDegrees) > 1e-6;
            double angle = fp.DraftDegrees * Math.PI / 180.0;
            int startCondition = fp.StartZ > 1e-9
                ? (int)swStartConditions_e.swStartOffset
                : (int)swStartConditions_e.swStartSketchPlane;
            double startOffset = M(fp.StartZ);
            bool reverseBoss = flags.ReverseBoss, reverseCut = flags.ReverseCut, flipStart = flags.FlipStartOffset;
            if (top)
            {
                // Measured for the +Y side; the -Y side is its mirror image, so both switches toggle.
                reverseCut = flags.TopReverse != fp.NegativeSide;
                flipStart = flags.TopFlipStart != fp.NegativeSide;
            }
            int blind = (int)swEndConditions_e.swEndCondBlind;

            Feature feature;
            if (fp.Op == FeatureOp.Boss)
            {
                //                                  Sd    Flip   Dir                  T1     T2 D1     D2 Dchk1 Dchk2 Ddir1 Ddir2 Dang1 Dang2
                feature = fm.FeatureExtrusion3(true, false, reverseBoss, blind, 0, depth, 0, draft, false, true, false, angle, 0,
                    //  OffsetReverse1/2  TranslateSurface1/2  Merge UseFeatScope UseAutoSelect
                    false, false, false, false, true, true, true,
                    //  T0              StartOffset  FlipStartOffset
                    startCondition, startOffset, flipStart);
            }
            else
            {
                feature = fm.FeatureCut4(true, false, reverseCut, blind, 0, depth, 0, draft, false, true, false, angle, 0,
                    false, false, false, false,
                    //  NormalCut UseFeatScope UseAutoSelect AssemblyFeatureScope AutoSelectComponents PropagateFeatureToParts
                    false, true, true, true, true, false,
                    startCondition, startOffset, flipStart,
                    //  OptimizeGeometry
                    false);
            }

            if (feature == null)
                throw new BuildException("SolidWorks could not create feature '" + fp.Name + "'. Check the sketch for overlapping or open contours.");

            try { feature.Name = fp.Name; } catch { /* a duplicate name is not worth failing the build */ }
            return feature;
        }

        /// <summary>
        /// A sketch with nothing extruded: construction centrelines (and outlines) on a plane parallel to the Front plane
        /// at fp.SketchZ, so they sit at the part tops for lining up text or other features.
        /// </summary>
        private static Feature ReferenceSketch(ModelDoc2 model, FeaturePlan fp)
        {
            Feature plane = OffsetPlane(model, fp.SketchZ, fp.Name + " plane");
            model.ClearSelection2(true);
            if (plane == null) plane = FindPlane(model, 0);
            if (plane == null || !plane.Select2(false, 0)) throw new BuildException("Could not select a plane for '" + fp.Name + "'.");

            SketchManager sm = model.SketchManager;
            sm.InsertSketch(true);
            try
            {
                sm.AddToDB = true;
                try
                {
                    foreach (LinePlan line in fp.Lines)
                    {
                        // A centreline is construction geometry already.
                        sm.CreateCenterLine(M(line.Start.X), M(line.Start.Y), 0, M(line.End.X), M(line.End.Y), 0);
                    }
                    foreach (Loop loop in fp.Loops)
                        foreach (SketchSegment seg in DrawLoop(sm, loop, 1, 1))
                            if (fp.LoopsAsConstruction && seg != null) seg.ConstructionGeometry = true;
                }
                finally { sm.AddToDB = false; }
            }
            catch
            {
                try { sm.InsertSketch(true); } catch { }
                throw;
            }

            Feature sketch = sm.ActiveSketch as Feature;
            sm.InsertSketch(true);
            model.ClearSelection2(true);
            if (sketch == null) throw new BuildException("SolidWorks did not create the sketch '" + fp.Name + "'.");
            try { sketch.Name = fp.Name; } catch { }
            return sketch;
        }

        /// <summary>
        /// A reference plane parallel to the Front plane at height z (mm, +Z = up out of the tray's base). The direction
        /// SolidWorks offsets in is checked from the plane's position and the plane remade the other way if needed.
        /// Null if it cannot be made (the caller then uses the Front plane).
        /// </summary>
        private static Feature OffsetPlane(ModelDoc2 model, double z, string name)
        {
            if (Math.Abs(z) < 1e-6) return null;
            Feature front = FindPlane(model, 0);
            if (front == null) return null;
            foreach (bool flip in z > 0 ? new[] { false, true } : new[] { true, false })
            {
                try
                {
                    model.ClearSelection2(true);
                    if (!front.Select2(false, 0)) return null;
                    int constraint = (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance;
                    if (flip) constraint |= (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_OptionFlip;
                    object created = model.FeatureManager.InsertRefPlane(constraint, M(Math.Abs(z)), 0, 0, 0, 0);
                    Feature f = created as Feature;
                    if (f == null) continue;
                    double actual = PlaneZ(f);
                    if (double.IsNaN(actual) || Math.Abs(actual - M(z)) < 1e-6)
                    {
                        try { f.Name = name; } catch { }
                        return f;
                    }
                    // The wrong side: delete it and try the other way.
                    model.ClearSelection2(true);
                    if (f.Select2(false, 0)) model.EditDelete();
                }
                catch (Exception ex) { SwLog.Write("Offset plane at " + z + " mm failed: " + ex.Message); }
            }
            model.ClearSelection2(true);
            return null;
        }

        // Height (m) of a plane parallel to the Front plane, from its transform; NaN if unknown.
        private static double PlaneZ(Feature f)
        {
            try
            {
                RefPlane rp = f.GetSpecificFeature2() as RefPlane;
                MathTransform tr = rp == null ? null : rp.Transform;
                double[] data = tr == null ? null : tr.ArrayData as double[];
                if (data == null || data.Length < 12) return double.NaN;
                return data[11]; // translation Z
            }
            catch { return double.NaN; }
        }

        public static double Volume(ModelDoc2 model)
        {
            MassProperty mp = model.Extension.CreateMassProperty() as MassProperty;
            return mp == null ? 0 : mp.Volume; // cubic metres
        }

        private static void DrawSketch(ModelDoc2 model, FeaturePlan fp, DirectionFlags flags)
        {
            bool top = fp.Plane == SketchPlane.Top;
            Feature plane = FindPlane(model, top ? 1 : 0);
            model.ClearSelection2(true);
            if (plane == null || !plane.Select2(false, 0))
                throw new BuildException("Could not find or select the " + (top ? "Top" : "Front") + " Plane.");

            int sx = top ? flags.TopSx : 1, sy = top ? flags.TopSy : 1;

            SketchManager sm = model.SketchManager;
            sm.InsertSketch(true);
            try
            {
                sm.AddToDB = true; // no inference or auto-relations while drawing
                try
                {
                    foreach (Loop loop in fp.Loops) DrawLoop(sm, loop, sx, sy);
                }
                finally
                {
                    sm.AddToDB = false;
                }

                foreach (TextPlan tp in fp.Texts) AddText(model, sm, tp);
            }
            catch
            {
                // Do not leave SolidWorks stuck in sketch mode when drawing fails part-way.
                try { sm.InsertSketch(true); } catch { }
                throw;
            }

            Feature sketchFeature = sm.ActiveSketch as Feature;
            string sketchName = sketchFeature == null ? null : sketchFeature.Name;
            sm.InsertSketch(true); // leave sketch mode
            model.ClearSelection2(true);

            if (sketchName == null || !model.Extension.SelectByID2(sketchName, "SKETCH", 0, 0, 0, false, 0, null, 0))
                throw new BuildException("Could not select the sketch for '" + fp.Name + "'.");
        }

        // The reference planes of a part come first in the tree: Front, Top, Right (whatever language SolidWorks runs in).
        private static Feature FindPlane(ModelDoc2 model, int index)
        {
            Feature f = model.FirstFeature() as Feature;
            int seen = 0;
            while (f != null)
            {
                if (f.GetTypeName2() == "RefPlane")
                {
                    if (seen == index) return f;
                    seen++;
                }
                f = f.GetNextFeature() as Feature;
            }
            return null;
        }

        /// <summary>
        /// Adds one line of sketch text. The text API is used through late binding (dynamic) so that a difference
        /// in its exact signature shows up as a clear message at run time instead of stopping the whole build.
        /// Several argument sets and several ways of finding the new text object are tried; what happened is written to
        /// %TEMP%\Gridfinity_addin.log and summarised in the error message if nothing works.
        /// </summary>
        private static void AddText(ModelDoc2 model, SketchManager sm, TextPlan tp)
        {
            if (string.IsNullOrEmpty(tp.Text)) return;

            // The anchor is the start of the text; capitals are about 0.7 of the character height,
            // so drop the anchor by half of that to centre the text on tp.Y.
            // Along a turned line the drop is turned too.
            double ang = tp.AngleDeg * Math.PI / 180.0;
            double ax = M(tp.X + Math.Sin(ang) * 0.35 * tp.HeightMm);
            double ay = M(tp.Y - Math.Cos(ang) * 0.35 * tp.HeightMm);

            // Every argument set failed in the same way last time, so the arguments are probably not the problem. Try the
            // same call typed instead of late-bound, and the text without the inch mark, as well as the original.
            var texts = new List<string> { tp.Text };
            if (tp.Text.IndexOf('"') >= 0) texts.Add(tp.Text.Replace("\"", "''"));

            var problems = new List<string>();
            object created = null;
            bool inserted = false;
            dynamic doc = model;

            foreach (string text in texts)
            {
                for (int mode = 0; mode < 4 && !inserted; mode++)
                {
                    int alignment = mode % 2 == 0 ? 0 : 1;
                    bool typed = mode < 2;
                    try
                    {
                        model.ClearSelection2(true);
                        if (typed) created = model.InsertSketchText(ax, ay, 0.0, text, alignment, 0, 0, 100, 100);
                        else created = doc.InsertSketchText(ax, ay, 0.0, text, alignment, 0, 0, 100, 100);
                        inserted = true;
                        SwLog.Write("InsertSketchText worked (" + (typed ? "typed" : "late-bound") + ", alignment " + alignment + ", text '" + text + "')");
                    }
                    catch (Exception ex)
                    {
                        problems.Add((typed ? "typed" : "late-bound") + "/align " + alignment + "/'" + text + "': " + ex.Message);
                    }
                }
                if (inserted) break;
            }

            if (!inserted)
            {
                SwLog.Write("InsertSketchText failed for '" + tp.Text + "': " + string.Join(" | ", problems.ToArray()));
                throw new BuildException("SolidWorks rejected the text call for '" + tp.Text + "' with every variant tried (" + problems[0] + ").");
            }

            // Find an object that carries the text format: the return value, the current selection, or the sketch's text segments.
            var candidates = new List<object>();
            if (created != null) candidates.Add(created);
            try
            {
                SelectionMgr sel = (SelectionMgr)model.SelectionManager;
                int n = sel.GetSelectedObjectCount2(-1);
                for (int i = 1; i <= n; i++)
                {
                    object o = sel.GetSelectedObject6(i, -1);
                    if (o != null) candidates.Add(o);
                }
            }
            catch (Exception ex) { SwLog.Write("Reading the selection after InsertSketchText failed: " + ex.Message); }
            try
            {
                dynamic sk = sm.ActiveSketch;
                object segs = sk.GetSketchTextSegments();
                Array arr = segs as Array;
                if (arr != null && arr.Length > 0) candidates.Add(arr.GetValue(arr.Length - 1));
            }
            catch (Exception ex) { SwLog.Write("GetSketchTextSegments failed: " + ex.Message); }

            string failure = "no object with a text format was found";
            foreach (object candidate in candidates)
            {
                try
                {
                    dynamic c = candidate;
                    dynamic format = null;
                    try { format = c.GetTextFormat(); }
                    catch { format = c.TextFormat; }
                    if (format == null) { failure = "the text format was empty"; continue; }

                    format.CharHeight = M(tp.HeightMm);
                    if (!string.IsNullOrEmpty(tp.Font)) { try { format.TypeFaceName = tp.Font; } catch { } }
                    try { format.Bold = tp.Bold; } catch { }
                    if (Math.Abs(ang) > 1e-9)
                    {
                        // Escapement is the angle of the text line, in radians.
                        try { format.Escapement = ang; }
                        catch (Exception ex) { SwLog.Write("Could not turn the text '" + tp.Text + "': " + ex.Message); }
                    }
                    c.SetTextFormat(false, format);
                    return;
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                }
            }

            SwLog.Write("Could not set the text size for '" + tp.Text + "': " + failure);
            throw new BuildException("The text '" + tp.Text + "' was created but its size could not be set (" + failure + ").");
        }

        private static List<SketchSegment> DrawLoop(SketchManager sm, Loop loop, int sx, int sy)
        {
            var made = new List<SketchSegment>();
            if (loop.IsCircle)
            {
                made.Add(sm.CreateCircleByRadius(M(sx * loop.Center.X), M(sy * loop.Center.Y), 0, M(loop.Radius)));
                return made;
            }

            // Mirroring one axis (not both) turns a counter-clockwise arc into a clockwise one.
            short arcDirection = (short)(sx * sy < 0 ? -1 : 1);

            foreach (Segment s in loop.Segments)
            {
                if (s.Kind == SegmentKind.Line)
                {
                    made.Add(sm.CreateLine(M(sx * s.Start.X), M(sy * s.Start.Y), 0, M(sx * s.End.X), M(sy * s.End.Y), 0));
                }
                else
                {
                    // Direction 1 = counter-clockwise from the first point to the second.
                    made.Add(sm.CreateArc(M(sx * s.Center.X), M(sy * s.Center.Y), 0,
                                 M(sx * s.Start.X), M(sy * s.Start.Y), 0,
                                 M(sx * s.End.X), M(sy * s.End.Y), 0, arcDirection));
                }
            }
            return made;
        }
    }

    /// <summary>Appends to %TEMP%\Gridfinity_addin.log, the same file the add-in start-up uses.</summary>
    internal static class SwLog
    {
        public static void Write(string message)
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "GridfinityOutlines", "Logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "Gridfinity_addin.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + System.Environment.NewLine);
            }
            catch { }
        }
    }
}
