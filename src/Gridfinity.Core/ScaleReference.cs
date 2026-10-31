using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace Gridfinity.Core
{
    /// <summary>An object of known size placed next to the parts, used to check and correct the scale of a tracing.</summary>
    public enum ScaleReferenceKind
    {
        [Description("None")] None,
        /// <summary>A square of known side: a printed square (measure it), a gauge block, a square of sheet.</summary>
        [Description("Square")] Square,
        /// <summary>A bank or ID card, ISO/IEC 7810 ID-1, 85.60 x 53.98 mm.</summary>
        [Description("Credit card")] CreditCard,
        /// <summary>Any rectangle of known width and height: a steel rule, a feeler gauge, a cut block.</summary>
        [Description("Rectangle")] Rectangle
    }

    /// <summary>
    /// Finds the scale reference among the traced shapes of one photo or file, removes it, and rescales the other
    /// shapes so the reference comes out at its true size. Paper is a good first scale but not an exact one (sheets
    /// vary by a millimetre or so, and a sheet that is not flat shrinks in the photo); a rigid reference lying among
    /// the parts corrects that.
    /// </summary>
    public static class ScaleReferences
    {
        public const double CardWidth = 85.60, CardHeight = 53.98;

        /// <summary>The largest correction applied without complaint; larger ones are applied but flagged.</summary>
        public const double LargeCorrection = 0.05;

        /// <summary>A reference whose sides differ by at least this much (mm) also gives the tracing's edge offset.</summary>
        public const double MinSideDifference = 20;

        /// <summary>The largest edge offset believed, mm; a larger one means the reference was traced badly.</summary>
        public const double MaxEdgeOffset = 1.5;

        /// <summary>Long and short side of the reference, mm, or false for None.</summary>
        public static bool Size(OutlineImportOptions o, out double longSide, out double shortSide)
        {
            double a, b;
            switch (o.ScaleReference)
            {
                case ScaleReferenceKind.Square: a = b = o.ReferenceWidth; break;
                case ScaleReferenceKind.CreditCard: a = CardWidth; b = CardHeight; break;
                case ScaleReferenceKind.Rectangle: a = o.ReferenceWidth; b = o.ReferenceHeight; break;
                default: longSide = shortSide = 0; return false;
            }
            longSide = Math.Max(a, b);
            shortSide = Math.Min(a, b);
            return shortSide > 0;
        }

        /// <summary>
        /// Applies the reference to the shapes from one source. Returns the scale factor used (1 when there is no
        /// reference or it was not found). The reference shape is removed from <paramref name="shapes"/>.
        /// </summary>
        public static double Apply(List<TracedShape> shapes, OutlineImportOptions o, string source, List<string> notes)
        {
            double refLong, refShort;
            if (!Size(o, out refLong, out refShort) || shapes.Count == 0) return 1;
            var c = CultureInfo.InvariantCulture;

            TracedShape best = null;
            double bestScore = double.MaxValue, bestLong = 0, bestShort = 0;
            double refAspect = refLong / refShort;
            foreach (var s in shapes)
            {
                if (s.Outline == null || s.Outline.Count < 4 || (s.Holes != null && s.Holes.Count > 0)) continue;
                double l, w, fill;
                MinRect(s.Outline, out l, out w, out fill);
                if (w <= 0) continue;
                double aspectErr = Math.Abs(l / w / refAspect - 1);
                double scale = (refLong / l + refShort / w) / 2;
                // A rectangle (rounded corners allowed) of the right proportions, within a third of the expected size.
                if (fill < 0.94 || aspectErr > 0.06 || scale < 0.75 || scale > 1.33) continue;
                double score = 2 * aspectErr + (1 - fill) + Math.Abs(scale - 1);
                if (score < bestScore) { bestScore = score; best = s; bestLong = l; bestShort = w; }
            }

            if (best == null)
            {
                notes.Add(string.Format(c, "{0}: no {1} scale reference ({2:0.##} x {3:0.##} mm) was found, so the scale is from the {4} alone. " +
                    "Lay it flat on the paper, apart from the parts, and make sure it is traced (a dark or matt reference works best).",
                    source, Describe(o.ScaleReference), refLong, refShort, o.PhotoMode == PhotoMode.Scan ? "scan resolution" : "paper size"));
                return 1;
            }

            // Measured size = a * true size + e. The scale error a is multiplicative; e is how much wider the tracing
            // draws every outline (edge blur, the tracer's half-pixel grow), the same for parts and reference. Sides
            // of clearly different length give both; a square only gives the scale.
            double kLong = refLong / bestLong, kShort = refShort / bestShort;
            double a = 2 / (kLong + kShort), e = 0;
            bool twoSided = refLong - refShort >= MinSideDifference;
            if (twoSided)
            {
                double a2 = (bestLong - bestShort) / (refLong - refShort);
                double e2 = (bestLong + bestShort - a2 * (refLong + refShort)) / 2;
                if (a2 > 0.75 && a2 < 1.33 && Math.Abs(e2) <= MaxEdgeOffset) { a = a2; e = e2; }
                else twoSided = false;
            }
            double k = 1 / a, inset = e / (2 * a);
            shapes.Remove(best);
            foreach (var s in shapes)
            {
                Scale(s.Outline, k);
                if (s.Holes != null) foreach (var h in s.Holes) Scale(h, k);
                if (Math.Abs(inset) < 0.005) continue;
                var outline = OutlineGeometry.OffsetLargest(s.Outline, -inset);
                if (outline != null) s.Outline = outline;
                if (s.Holes != null)
                    s.Holes = s.Holes.Select(h => OutlineGeometry.OffsetLargest(h, inset)).Where(h => h != null).ToList();
            }
            notes.Add(string.Format(c, "{0}: {1} scale reference measured {2:0.##} x {3:0.##} mm (true {4:0.##} x {5:0.##}); sizes corrected by {6:+0.0#;-0.0#;0}%{7}.",
                source, Describe(o.ScaleReference), bestLong, bestShort, refLong, refShort, (k - 1) * 100,
                twoSided ? string.Format(c, " and outlines {0} {1:0.##} mm per side for the tracing's edge", inset >= 0 ? "trimmed" : "grown", Math.Abs(inset)) : ""));
            if (Math.Abs(kLong - kShort) > 0.02 && !twoSided)
                notes.Add(string.Format(c, "{0}: the reference is stretched differently along and across ({1:0.###} vs {2:0.###}); the photo may not be square to the paper.", source, kLong, kShort));
            if (Math.Abs(k - 1) > LargeCorrection)
                notes.Add(string.Format(c, "{0}: that is a large correction. Check the Paper setting and the reference size.", source));
            return k;
        }

        private static string Describe(ScaleReferenceKind k)
        {
            return k == ScaleReferenceKind.CreditCard ? "credit card" : k.ToString().ToLowerInvariant();
        }

        private static void Scale(List<Vec2> pts, double k)
        {
            for (int i = 0; i < pts.Count; i++) pts[i] = new Vec2(pts[i].X * k, pts[i].Y * k);
        }

        /// <summary>Long and short side of the smallest enclosing rectangle, and how much of it the shape fills.</summary>
        public static void MinRect(IList<Vec2> pts, out double longSide, out double shortSide, out double fill)
        {
            double angle = OutlineGeometry.AxisAlignAngle(pts);
            Vec2 lo, hi;
            OutlineGeometry.Bounds(pts.Select(q => q.Rotated(angle)), out lo, out hi);
            double a = hi.X - lo.X, b = hi.Y - lo.Y;
            longSide = Math.Max(a, b);
            shortSide = Math.Min(a, b);
            fill = a * b > 0 ? Math.Abs(GeometryUtil.SignedArea(pts)) / (a * b) : 0;
        }
    }
}
