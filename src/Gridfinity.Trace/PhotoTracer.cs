using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Gridfinity.Core;
using OpenCvSharp;

namespace Gridfinity.Trace
{
    /// <summary>What the tracer found.</summary>
    public sealed class TraceResult
    {
        public List<TracedShape> Shapes = new List<TracedShape>();
        /// <summary>A PNG of the straightened paper with the traced outlines drawn on it, for checking.</summary>
        public string OverlayPath;
        public double PaperWidthMm;
        public double PaperHeightMm;
    }

    /// <summary>
    /// Traces parts from a photo of them lying on a sheet of paper (or from a flatbed scan). The paper's corners give
    /// the scale and let the perspective be removed, as outline-app does; the parts are then separated from the paper
    /// by how much darker or more coloured than the paper they are.
    ///
    /// Tips for a good photo: a plain, darker surface around the paper so its edge is clear; the camera square above
    /// the middle of the sheet; soft light from above to keep shadows short; all four corners in view.
    /// </summary>
    public static class PhotoTracer
    {
        public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp" };

        public static bool IsImage(string path)
        {
            return Extensions.Contains((Path.GetExtension(path) ?? "").ToLowerInvariant());
        }

        public static TraceResult Trace(string path, OutlineImportOptions o, List<string> notes)
        {
            o = o ?? new OutlineImportOptions();
            string name = Path.GetFileNameWithoutExtension(path);
            using (Mat img = Cv2.ImRead(path, ImreadModes.Color)) // follows the EXIF orientation of phone photos
            {
                if (img.Empty()) throw new InvalidDataException(Path.GetFileName(path) + ": not an image this tracer can read.");
                return Trace(img, name, o, notes, Path.Combine(Path.GetTempPath(), "GridfinityOutlines-" + name + "-trace.png"));
            }
        }

        public static TraceResult Trace(Mat img, string name, OutlineImportOptions o, List<string> notes, string overlayPath)
        {
            double ppm = Math.Max(2, Math.Min(20, o.PixelsPerMm));
            var result = new TraceResult();
            Mat paper;
            if (o.PhotoMode == PhotoMode.Scan)
            {
                double scale = ppm / (o.ScanDpi / 25.4);
                paper = new Mat();
                Cv2.Resize(img, paper, new Size(0, 0), scale, scale, InterpolationFlags.Area);
                result.PaperWidthMm = paper.Width / ppm;
                result.PaperHeightMm = paper.Height / ppm;
                notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}: scan at {1:0} dpi, {2:0} x {3:0} mm.", name, o.ScanDpi, result.PaperWidthMm, result.PaperHeightMm));
            }
            else
            {
                double pw, ph;
                PaperSizes.Size(o, out pw, out ph);
                Point2f[] corners = FindPaper(img, pw / ph, name, notes);
                if (corners == null)
                    throw new InvalidDataException(name + ": could not find the four corners of the paper. Photograph the parts on a sheet of " + o.Paper +
                                                   " paper on a darker surface, with all four corners in view, or use Scan mode for a flatbed scan.");

                // Portrait or landscape: match the longer side of the quad to the longer side of the paper.
                double top = Dist(corners[0], corners[1]), bottom = Dist(corners[3], corners[2]);
                double left = Dist(corners[0], corners[3]), right = Dist(corners[1], corners[2]);
                bool landscape = top + bottom > left + right;
                double wMm = landscape ? ph : pw, hMm = landscape ? pw : ph;
                result.PaperWidthMm = wMm;
                result.PaperHeightMm = hMm;

                var dst = new[]
                {
                    new Point2f(0, 0), new Point2f((float)(wMm * ppm), 0),
                    new Point2f((float)(wMm * ppm), (float)(hMm * ppm)), new Point2f(0, (float)(hMm * ppm))
                };
                using (Mat h = Cv2.GetPerspectiveTransform(corners, dst))
                {
                    paper = new Mat();
                    Cv2.WarpPerspective(img, paper, h, new Size((int)Math.Round(wMm * ppm), (int)Math.Round(hMm * ppm)),
                                        InterpolationFlags.Cubic, BorderTypes.Replicate);
                }
                notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}: found the {1} sheet ({2:0} x {3:0} mm, {4}) and straightened it.",
                    name, o.Paper, wMm, hMm, landscape ? "landscape" : "portrait"));
            }

            using (paper)
            using (Mat mask = Segment(paper, o, ppm))
            {
                result.Shapes = Contours(mask, ppm, name, notes);
                result.OverlayPath = overlayPath;
                if (!string.IsNullOrEmpty(overlayPath))
                {
                    try
                    {
                        using (Mat overlay = paper.Clone())
                        {
                            Point[][] cs;
                            HierarchyIndex[] hi;
                            Cv2.FindContours(mask, out cs, out hi, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);
                            Cv2.DrawContours(overlay, cs, -1, new Scalar(40, 200, 40), Math.Max(2, (int)(ppm / 3)));
                            Cv2.ImWrite(overlayPath, overlay);
                        }
                    }
                    catch (Exception ex) { notes.Add(name + ": could not save the tracing preview (" + ex.Message + ")."); result.OverlayPath = null; }
                }
            }
            if (result.Shapes.Count == 0) notes.Add(name + ": no parts found on the paper. Try a higher Sensitivity, or a darker surface under pale parts.");
            return result;
        }

        private static double Dist(Point2f a, Point2f b) { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }

        // ---- Paper ---------------------------------------------------------------------------------

        /// <summary>The paper's corners in the full image, ordered top-left, top-right, bottom-right, bottom-left; null if not found.</summary>
        public static Point2f[] FindPaper(Mat img, double aspect, string name, List<string> notes)
        {
            double s = Math.Min(1.0, 1200.0 / Math.Max(img.Width, img.Height));
            using (var small = new Mat())
            using (var gray = new Mat())
            using (var bin = new Mat())
            {
                Cv2.Resize(img, small, new Size(0, 0), s, s, InterpolationFlags.Area);
                Cv2.CvtColor(small, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);

                Point2f[] best = null;
                double bestScore = 0;
                // Bright paper on a darker surface (Otsu), then edges for a paper on a similar surface.
                foreach (int method in new[] { 0, 1 })
                {
                    if (method == 0)
                    {
                        Cv2.Threshold(gray, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                        using (var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(9, 9)))
                            Cv2.MorphologyEx(bin, bin, MorphTypes.Close, k); // fill the parts lying on the paper
                    }
                    else
                    {
                        Cv2.Canny(gray, bin, 40, 120);
                        using (var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5)))
                            Cv2.Dilate(bin, bin, k);
                    }

                    Point[][] contours;
                    HierarchyIndex[] h;
                    Cv2.FindContours(bin, out contours, out h, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                    foreach (var c in contours.OrderByDescending(c => Cv2.ContourArea(c)).Take(5))
                    {
                        double area = Cv2.ContourArea(c);
                        if (area < 0.1 * small.Width * small.Height) break;
                        Point[] hull = Cv2.ConvexHull(c);
                        Point[] quad = null;
                        double peri = Cv2.ArcLength(hull, true);
                        for (double eps = 0.01; eps <= 0.08 && quad == null; eps += 0.005)
                        {
                            var a = Cv2.ApproxPolyDP(hull, eps * peri, true);
                            if (a.Length == 4) quad = a;
                        }
                        if (quad == null) continue;
                        var ordered = Order(quad.Select(p => new Point2f(p.X, p.Y)).ToArray());
                        double w = (Dist(ordered[0], ordered[1]) + Dist(ordered[3], ordered[2])) / 2;
                        double hgt = (Dist(ordered[0], ordered[3]) + Dist(ordered[1], ordered[2])) / 2;
                        double r = Math.Min(w, hgt) / Math.Max(w, hgt);
                        double aspectError = Math.Abs(r - aspect) / aspect;
                        if (aspectError > 0.25) continue; // perspective distorts the ratio, but not by this much
                        double score = Cv2.ContourArea(quad) * (1 - aspectError);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = ordered;
                            if (aspectError > 0.12)
                                notes.Add(name + ": the paper looks " + (aspectError * 100).ToString("0", CultureInfo.InvariantCulture) +
                                          "% out of shape; check the paper size, and hold the camera square above the sheet.");
                        }
                    }
                    if (best != null) break;
                }
                if (best == null) return null;

                // Back to full resolution, then refine each corner on the full image.
                var full = best.Select(p => new Point2f((float)(p.X / s), (float)(p.Y / s))).ToArray();
                try
                {
                    using (var g2 = new Mat())
                    {
                        Cv2.CvtColor(img, g2, ColorConversionCodes.BGR2GRAY);
                        int win = Math.Max(3, (int)(4 / s));
                        Point2f[] refined = Cv2.CornerSubPix(g2, full, new Size(win, win), new Size(-1, -1),
                            new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.05));
                        // Keep a refinement only if it stays close (corner refinement can wander onto a part or a shadow).
                        for (int i = 0; i < 4; i++)
                            if (Dist(refined[i], full[i]) < win) full[i] = refined[i];
                    }
                }
                catch { }
                return full;
            }
        }

        private static Point2f[] Order(Point2f[] p)
        {
            // Top-left has the smallest x + y, bottom-right the largest; top-right the smallest y - x.
            var tl = p.OrderBy(q => q.X + q.Y).First();
            var br = p.OrderBy(q => q.X + q.Y).Last();
            var tr = p.OrderBy(q => q.Y - q.X).First();
            var bl = p.OrderBy(q => q.Y - q.X).Last();
            return new[] { tl, tr, br, bl };
        }

        // ---- Parts ---------------------------------------------------------------------------------

        /// <summary>White where there is a part, on the straightened paper.</summary>
        public static Mat Segment(Mat paper, OutlineImportOptions o, double ppm)
        {
            using (var gray = new Mat())
            using (var hsv = new Mat())
            {
                Cv2.CvtColor(paper, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(paper, hsv, ColorConversionCodes.BGR2HSV);
                Mat[] ch = Cv2.Split(hsv);

                // Even out the lighting: fit a smooth (quadratic) surface to the paper's brightness and divide by it.
                using (var light = PaperLight(gray))
                using (var g32 = new Mat())
                using (var dark = new Mat())
                using (var sat = new Mat())
                using (var score = new Mat())
                {
                    gray.ConvertTo(g32, MatType.CV_32F);
                    Cv2.Divide(g32, light, dark, 255.0);       // paper ~ 255
                    Cv2.Subtract(new Scalar(255), dark, dark); // darkness: paper ~ 0
                    ch[1].ConvertTo(sat, MatType.CV_32F);
                    Cv2.Max(dark, sat * 0.8, score);           // coloured parts count even if they are pale
                    foreach (var c in ch) c.Dispose();

                    var s8 = new Mat();
                    score.ConvertTo(s8, MatType.CV_8U);
                    Cv2.GaussianBlur(s8, s8, new Size(3, 3), 0);

                    // Otsu chooses the split; Sensitivity moves it (50 = Otsu, 100 = half of it, 0 = 1.5 times it).
                    var mask = new Mat();
                    double otsu = Cv2.Threshold(s8, mask, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                    double sens = Math.Max(0, Math.Min(100, o.Sensitivity));
                    double thr = Math.Max(12, otsu * (1.5 - sens / 100.0));
                    Cv2.Threshold(s8, mask, thr, 255, ThresholdTypes.Binary);
                    s8.Dispose();

                    // Ignore the paper's edge.
                    int b = (int)Math.Round(Math.Max(0, o.PaperBorder) * ppm);
                    if (b > 0)
                    {
                        Cv2.Rectangle(mask, new Rect(0, 0, mask.Width, b), Scalar.Black, -1);
                        Cv2.Rectangle(mask, new Rect(0, mask.Height - b, mask.Width, b), Scalar.Black, -1);
                        Cv2.Rectangle(mask, new Rect(0, 0, b, mask.Height), Scalar.Black, -1);
                        Cv2.Rectangle(mask, new Rect(mask.Width - b, 0, b, mask.Height), Scalar.Black, -1);
                    }

                    // Remove specks, close small gaps (about half a millimetre).
                    int k = Math.Max(3, (int)Math.Round(ppm * 0.5) | 1);
                    using (var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k)))
                    {
                        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
                        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
                    }
                    return mask;
                }
            }
        }

        private static Mat PaperLight(Mat gray)
        {
            // Sample a coarse grid, keep the bright (paper) samples, least-squares fit a + bx + cy + dx^2 + exy + fy^2.
            int step = Math.Max(4, Math.Min(gray.Width, gray.Height) / 60);
            var xs = new List<double>(); var ys = new List<double>(); var vs = new List<double>();
            var idx = gray.GetGenericIndexer<byte>();
            var samples = new List<byte>();
            for (int y = step / 2; y < gray.Height; y += step)
                for (int x = step / 2; x < gray.Width; x += step) samples.Add(idx[y, x]);
            samples.Sort();
            double cut = samples.Count == 0 ? 0 : samples[(int)(samples.Count * 0.4)]; // the paper is most of the sheet
            for (int y = step / 2; y < gray.Height; y += step)
                for (int x = step / 2; x < gray.Width; x += step)
                {
                    byte v = idx[y, x];
                    if (v < cut) continue;
                    xs.Add(x / (double)gray.Width); ys.Add(y / (double)gray.Height); vs.Add(v);
                }

            var light = new Mat(gray.Size(), MatType.CV_32F);
            double[] c = null;
            if (vs.Count >= 12)
            {
                using (var A = new Mat(vs.Count, 6, MatType.CV_64F))
                using (var B = new Mat(vs.Count, 1, MatType.CV_64F))
                using (var X = new Mat())
                {
                    var ai = A.GetGenericIndexer<double>();
                    var bi = B.GetGenericIndexer<double>();
                    for (int i = 0; i < vs.Count; i++)
                    {
                        double x = xs[i], y = ys[i];
                        ai[i, 0] = 1; ai[i, 1] = x; ai[i, 2] = y; ai[i, 3] = x * x; ai[i, 4] = x * y; ai[i, 5] = y * y;
                        bi[i, 0] = vs[i];
                    }
                    if (Cv2.Solve(A, B, X, DecompTypes.SVD))
                    {
                        var xi = X.GetGenericIndexer<double>();
                        c = Enumerable.Range(0, 6).Select(i => xi[i, 0]).ToArray();
                    }
                }
            }
            double mean = vs.Count > 0 ? vs.Average() : 230;
            var li = light.GetGenericIndexer<float>();
            for (int y = 0; y < gray.Height; y++)
            {
                double fy = y / (double)gray.Height;
                for (int x = 0; x < gray.Width; x++)
                {
                    double fx = x / (double)gray.Width;
                    double v = c == null ? mean : c[0] + c[1] * fx + c[2] * fy + c[3] * fx * fx + c[4] * fx * fy + c[5] * fy * fy;
                    li[y, x] = (float)Math.Max(20, v);
                }
            }
            return light;
        }

        /// <summary>Outlines (and holes) of the white regions, in mm with Y up, named left to right and top to bottom.</summary>
        public static List<TracedShape> Contours(Mat mask, double ppm, string name, List<string> notes)
        {
            Point[][] cs;
            HierarchyIndex[] h;
            Cv2.FindContours(mask, out cs, out h, RetrievalModes.CComp, ContourApproximationModes.ApproxNone);
            double H = mask.Height;
            // Contours run through the centres of the edge pixels, half a pixel inside the true edge.
            double grow = 0.5 / ppm;
            Func<Point[], List<Vec2>> toMm = c => c.Select(p => new Vec2(p.X / ppm, (H - 1 - p.Y) / ppm)).ToList();

            var shapes = new List<Tuple<double, double, TracedShape>>();
            for (int i = 0; i < cs.Length; i++)
            {
                if (h[i].Parent >= 0) continue; // a hole; collected with its outline below
                if (cs[i].Length < 8) continue;
                var outline = OutlineGeometry.OffsetLargest(toMm(cs[i]), grow);
                if (outline == null || Math.Abs(GeometryUtil.SignedArea(outline)) < 4) continue;
                var shape = new TracedShape { Source = name, Outline = outline };
                for (int c = h[i].Child; c >= 0; c = h[c].Next)
                {
                    if (cs[c].Length < 8) continue;
                    var hole = OutlineGeometry.OffsetLargest(toMm(cs[c]), -grow);
                    if (hole != null && Math.Abs(GeometryUtil.SignedArea(hole)) >= 1) shape.Holes.Add(hole);
                }
                var r = Cv2.BoundingRect(cs[i]);
                shapes.Add(Tuple.Create((double)r.Y, (double)r.X, shape));
            }

            // Reading order: rows of parts (by top edge, 20 mm bands), then left to right.
            var ordered = shapes.OrderBy(s => Math.Floor(s.Item1 / (20 * ppm))).ThenBy(s => s.Item2).Select(s => s.Item3).ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].Name = ordered.Count == 1 ? name : name + "-" + (i + 1);
            notes.Add(name + ": traced " + ordered.Count + " part(s).");
            return ordered;
        }
    }
}
