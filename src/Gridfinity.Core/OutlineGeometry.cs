using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace Gridfinity.Core
{
    /// <summary>
    /// Geometry for free-form (traced) outlines: cleaning, offsetting, simplifying and aligning arbitrary
    /// polygons, convex or not. Offsetting uses Clipper2 (Boost licence), which handles concave shapes correctly.
    /// </summary>
    public static class OutlineGeometry
    {
        private const int Precision = 4;          // decimal places Clipper keeps (0.0001 mm)
        private const double ArcTolerance = 0.02; // max deviation of the rounded corners an offset adds, mm

        /// <summary>
        /// Removes self-intersections and duplicate points, and returns the largest resulting outline,
        /// counter-clockwise. Returns null when nothing with area is left.
        /// </summary>
        public static List<Vec2> Clean(IList<Vec2> points)
        {
            if (points == null || points.Count < 3) return null;
            PathsD united = Clipper.Union(new PathsD { ToPath(points) }, FillRule.NonZero);
            return Largest(united);
        }

        /// <summary>
        /// Grows (positive <paramref name="delta"/>) or shrinks (negative) a closed outline by the same distance
        /// everywhere, rounding the corners it creates. A shrink can split a shape or remove it, so a list is returned
        /// (counter-clockwise outlines only; empty when nothing is left).
        /// </summary>
        public static List<List<Vec2>> Offset(IList<Vec2> points, double delta)
        {
            var result = new List<List<Vec2>>();
            if (points == null || points.Count < 3) return result;

            PathsD input = new PathsD { ToPath(Shapes.EnsureCounterClockwise(points)) };
            PathsD output = Math.Abs(delta) < 1e-9
                ? Clipper.Union(input, FillRule.NonZero)
                : Clipper.InflatePaths(input, delta, JoinType.Round, EndType.Polygon, 2.0, Precision, ArcTolerance);

            foreach (PathD p in output)
            {
                if (p.Count < 3 || !Clipper.IsPositive(p)) continue; // negative paths are voids inside a grown shape
                result.Add(FromPath(p));
            }
            return result.OrderByDescending(p => Math.Abs(GeometryUtil.SignedArea(p))).ToList();
        }

        /// <summary>The single largest piece of <see cref="Offset"/>, or null when nothing is left.</summary>
        public static List<Vec2> OffsetLargest(IList<Vec2> points, double delta)
        {
            var pieces = Offset(points, delta);
            return pieces.Count == 0 ? null : pieces[0];
        }

        /// <summary>
        /// Douglas-Peucker simplification: drops points that lie within <paramref name="tolerance"/> of the line
        /// through their neighbours. Traced photos produce hundreds of tiny steps; 0.1 mm keeps the shape and
        /// makes the SolidWorks sketch far lighter.
        /// </summary>
        public static List<Vec2> Simplify(IList<Vec2> points, double tolerance)
        {
            if (points == null) return null;
            if (tolerance <= 0 || points.Count < 4) return new List<Vec2>(points);
            PathsD s = Clipper.SimplifyPaths(new PathsD { ToPath(points) }, tolerance, true);
            var best = Largest(s, keepOrientation: true);
            return best ?? new List<Vec2>(points);
        }

        /// <summary>
        /// The parts of a closed outline inside an axis-aligned rectangle (a polygon with holes comes back as its
        /// outer edges and its holes; the caller draws them in one sketch).
        /// </summary>
        public static List<List<Vec2>> ClipToRect(IList<Vec2> points, double x0, double y0, double x1, double y1)
        {
            var rect = new RectD(x0, y0, x1, y1);
            PathsD output = Clipper.RectClip(rect, new PathsD { ToPath(points) }, Precision);
            return output.Where(p => p.Count >= 3 && Math.Abs(Clipper.Area(p)) > 0.01).Select(FromPath).ToList();
        }

        /// <summary>A closed loop of straight segments through the points (made counter-clockwise).</summary>
        public static Loop ToLoop(IList<Vec2> points)
        {
            var p = Shapes.EnsureCounterClockwise(points);
            var loop = new Loop();
            for (int i = 0; i < p.Count; i++)
            {
                Vec2 a = p[i], b = p[(i + 1) % p.Count];
                if ((b - a).Length > 1e-9) loop.Segments.Add(Segment.Line(a, b));
            }
            return loop;
        }

        public static void Bounds(IEnumerable<Vec2> points, out Vec2 min, out Vec2 max)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var q in points)
            {
                if (q.X < minX) minX = q.X;
                if (q.Y < minY) minY = q.Y;
                if (q.X > maxX) maxX = q.X;
                if (q.Y > maxY) maxY = q.Y;
            }
            min = new Vec2(minX, minY);
            max = new Vec2(maxX, maxY);
        }

        /// <summary>
        /// The rotation (radians) that, applied to the points, makes their smallest enclosing rectangle line up with
        /// the X and Y axes, with the long side along X. Uses the convex hull and rotating edges.
        /// </summary>
        public static double AxisAlignAngle(IList<Vec2> points)
        {
            var hull = ConvexHull(points);
            if (hull.Count < 3) return 0;

            double bestArea = double.MaxValue, bestAngle = 0;
            for (int i = 0; i < hull.Count; i++)
            {
                Vec2 e = hull[(i + 1) % hull.Count] - hull[i];
                if (e.Length < 1e-9) continue;
                double angle = -Math.Atan2(e.Y, e.X);
                Vec2 lo, hi;
                Bounds(hull.Select(q => q.Rotated(angle)), out lo, out hi);
                double area = (hi.X - lo.X) * (hi.Y - lo.Y);
                if (area < bestArea - 1e-9)
                {
                    bestArea = area;
                    bestAngle = (hi.X - lo.X) >= (hi.Y - lo.Y) ? angle : angle + Math.PI / 2;
                }
            }

            // Keep the angle in (-90, 90] degrees so shapes are not flipped end over end without need.
            while (bestAngle > Math.PI / 2 + 1e-9) bestAngle -= Math.PI;
            while (bestAngle <= -Math.PI / 2 + 1e-9) bestAngle += Math.PI;
            return bestAngle;
        }

        /// <summary>Andrew's monotone chain; counter-clockwise, no repeated first point.</summary>
        public static List<Vec2> ConvexHull(IEnumerable<Vec2> points)
        {
            var p = points.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
            if (p.Count < 3) return p;
            var hull = new Vec2[2 * p.Count];
            int k = 0;
            for (int i = 0; i < p.Count; i++)
            {
                while (k >= 2 && Vec2.Cross(hull[k - 1] - hull[k - 2], p[i] - hull[k - 2]) <= 0) k--;
                hull[k++] = p[i];
            }
            for (int i = p.Count - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && Vec2.Cross(hull[k - 1] - hull[k - 2], p[i] - hull[k - 2]) <= 0) k--;
                hull[k++] = p[i];
            }
            return hull.Take(k - 1).ToList();
        }

        // ---- Clipper conversion ------------------------------------------------------------------

        private static PathD ToPath(IEnumerable<Vec2> points)
        {
            var path = new PathD();
            foreach (var q in points) path.Add(new PointD(q.X, q.Y));
            return path;
        }

        private static List<Vec2> FromPath(PathD path)
        {
            return path.Select(q => new Vec2(q.x, q.y)).ToList();
        }

        private static List<Vec2> Largest(PathsD paths, bool keepOrientation = false)
        {
            PathD best = null;
            double bestArea = 0;
            foreach (PathD p in paths)
            {
                if (p.Count < 3) continue;
                double a = Math.Abs(Clipper.Area(p));
                if (a > bestArea) { bestArea = a; best = p; }
            }
            if (best == null || bestArea < 1e-9) return null;
            var list = FromPath(best);
            return keepOrientation ? list : Shapes.EnsureCounterClockwise(list);
        }
    }
}
