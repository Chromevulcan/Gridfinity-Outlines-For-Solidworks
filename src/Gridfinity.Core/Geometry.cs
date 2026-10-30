using System;
using System.Collections.Generic;

namespace Gridfinity.Core
{
    /// <summary>2D point / vector in millimetres.</summary>
    public struct Vec2
    {
        public double X;
        public double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Y + b.Y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }
        public static Vec2 operator *(Vec2 a, double s) { return new Vec2(a.X * s, a.Y * s); }

        [Newtonsoft.Json.JsonIgnore]
        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }

        public Vec2 Normalized()
        {
            double l = Length;
            return l < 1e-12 ? new Vec2(0, 0) : new Vec2(X / l, Y / l);
        }

        public static double Dot(Vec2 a, Vec2 b) { return a.X * b.X + a.Y * b.Y; }
        public static double Cross(Vec2 a, Vec2 b) { return a.X * b.Y - a.Y * b.X; }

        public Vec2 Rotated(double radians)
        {
            double c = Math.Cos(radians), s = Math.Sin(radians);
            return new Vec2(X * c - Y * s, X * s + Y * c);
        }

        public override string ToString() { return "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ")"; }
    }

    public enum SegmentKind { Line, Arc }

    /// <summary>A line, or a counter-clockwise arc about <see cref="Center"/> from Start to End.</summary>
    public sealed class Segment
    {
        public SegmentKind Kind;
        public Vec2 Start;
        public Vec2 End;
        public Vec2 Center; // arcs only

        public static Segment Line(Vec2 a, Vec2 b)
        {
            return new Segment { Kind = SegmentKind.Line, Start = a, End = b };
        }

        public static Segment Arc(Vec2 center, Vec2 start, Vec2 end)
        {
            return new Segment { Kind = SegmentKind.Arc, Center = center, Start = start, End = end };
        }
    }

    /// <summary>A closed contour: either a full circle or a chain of line/arc segments (CCW).</summary>
    public sealed class Loop
    {
        public bool IsCircle;
        public Vec2 Center;
        public double Radius;
        public List<Segment> Segments = new List<Segment>();

        public static Loop Circle(Vec2 center, double radius)
        {
            return new Loop { IsCircle = true, Center = center, Radius = radius };
        }

        /// <summary>Returns a copy rotated about the origin, then translated.</summary>
        public Loop Transformed(double rotationRadians, Vec2 translate)
        {
            Func<Vec2, Vec2> f = p => p.Rotated(rotationRadians) + translate;
            var l = new Loop { IsCircle = IsCircle, Radius = Radius, Center = f(Center) };
            foreach (var s in Segments)
            {
                l.Segments.Add(new Segment
                {
                    Kind = s.Kind,
                    Start = f(s.Start),
                    End = f(s.End),
                    Center = s.Kind == SegmentKind.Arc ? f(s.Center) : s.Center
                });
            }
            return l;
        }

        /// <summary>Approximates the contour as a closed polygon (first point not repeated).</summary>
        public List<Vec2> ToPolyline(double arcStepDegrees = 6.0)
        {
            var pts = new List<Vec2>();
            if (IsCircle)
            {
                int n = Math.Max(12, (int)Math.Ceiling(360.0 / arcStepDegrees));
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    pts.Add(new Vec2(Center.X + Radius * Math.Cos(a), Center.Y + Radius * Math.Sin(a)));
                }
                return pts;
            }

            foreach (var s in Segments)
            {
                pts.Add(s.Start);
                if (s.Kind != SegmentKind.Arc) continue;

                double r = (s.Start - s.Center).Length;
                double a0 = Math.Atan2(s.Start.Y - s.Center.Y, s.Start.X - s.Center.X);
                double a1 = Math.Atan2(s.End.Y - s.Center.Y, s.End.X - s.Center.X);
                if (a1 <= a0) a1 += 2 * Math.PI;
                int steps = Math.Max(1, (int)Math.Ceiling((a1 - a0) * 180.0 / Math.PI / arcStepDegrees));
                for (int i = 1; i < steps; i++)
                {
                    double a = a0 + (a1 - a0) * i / steps;
                    pts.Add(new Vec2(s.Center.X + r * Math.Cos(a), s.Center.Y + r * Math.Sin(a)));
                }
            }
            return pts;
        }

        public void GetBounds(out Vec2 min, out Vec2 max)
        {
            var pts = ToPolyline(3.0);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            if (IsCircle)
            {
                // The polyline is inscribed; use exact extremes for circles.
                minX = Center.X - Radius; maxX = Center.X + Radius;
                minY = Center.Y - Radius; maxY = Center.Y + Radius;
            }
            min = new Vec2(minX, minY);
            max = new Vec2(maxX, maxY);
        }

        public double Area()
        {
            if (IsCircle) return Math.PI * Radius * Radius;
            return Math.Abs(GeometryUtil.SignedArea(ToPolyline(3.0)));
        }
    }

    /// <summary>Builders for the contours the tray generator needs.</summary>
    public static class Shapes
    {
        public static List<Vec2> EnsureCounterClockwise(IList<Vec2> pts)
        {
            var list = new List<Vec2>(pts);
            if (GeometryUtil.SignedArea(list) < 0) list.Reverse();
            return list;
        }

        /// <summary>Convex polygon with every corner rounded to <paramref name="radius"/> (clamped to fit).</summary>
        public static Loop RoundedPolygon(IList<Vec2> points, double radius)
        {
            var p = EnsureCounterClockwise(points);
            int n = p.Count;
            var t1 = new Vec2[n];
            var t2 = new Vec2[n];
            var ctr = new Vec2[n];
            var hasArc = new bool[n];

            for (int i = 0; i < n; i++)
            {
                Vec2 prev = p[(i + n - 1) % n], cur = p[i], next = p[(i + 1) % n];
                Vec2 e1 = prev - cur, e2 = next - cur;
                Vec2 v1 = e1.Normalized(), v2 = e2.Normalized();
                double theta = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Vec2.Dot(v1, v2))));

                if (radius <= 1e-6 || theta < 1e-3 || Math.Abs(Math.PI - theta) < 1e-3)
                {
                    t1[i] = cur;
                    t2[i] = cur;
                    continue;
                }

                double tanHalf = Math.Tan(theta / 2);
                double d = radius / tanHalf;
                double dMax = 0.5 * Math.Min(e1.Length, e2.Length);
                double r = radius;
                if (d > dMax)
                {
                    d = dMax;
                    r = d * tanHalf;
                }

                t1[i] = cur + v1 * d;
                t2[i] = cur + v2 * d;
                ctr[i] = cur + (v1 + v2).Normalized() * (r / Math.Sin(theta / 2));
                hasArc[i] = true;
            }

            var loop = new Loop();
            for (int i = 0; i < n; i++)
            {
                if (hasArc[i]) loop.Segments.Add(Segment.Arc(ctr[i], t1[i], t2[i]));
                Vec2 from = t2[i];
                Vec2 to = t1[(i + 1) % n];
                if ((to - from).Length > 1e-9) loop.Segments.Add(Segment.Line(from, to));
            }
            return loop;
        }

        public static Loop RoundedRect(double cx, double cy, double width, double height, double radius)
        {
            double hx = width / 2, hy = height / 2;
            var pts = new List<Vec2>
            {
                new Vec2(cx - hx, cy - hy),
                new Vec2(cx + hx, cy - hy),
                new Vec2(cx + hx, cy + hy),
                new Vec2(cx - hx, cy + hy)
            };
            return RoundedPolygon(pts, radius);
        }

        /// <summary>Obround slot: two end-cap arcs of radius width/2 centred at c1 and c2.</summary>
        public static Loop Slot(Vec2 c1, Vec2 c2, double width)
        {
            double r = width / 2;
            Vec2 axis = c2 - c1;
            if (axis.Length < 1e-6) return Loop.Circle(c1, r);

            Vec2 u = axis.Normalized();
            Vec2 n = new Vec2(-u.Y, u.X); // left normal
            var loop = new Loop();
            loop.Segments.Add(Segment.Line(c1 - n * r, c2 - n * r));
            loop.Segments.Add(Segment.Arc(c2, c2 - n * r, c2 + n * r));
            loop.Segments.Add(Segment.Line(c2 + n * r, c1 + n * r));
            loop.Segments.Add(Segment.Arc(c1, c1 + n * r, c1 - n * r));
            return loop;
        }

        /// <summary>Regular polygon centred on the origin with a vertex on +X. Size is across flats.</summary>
        public static List<Vec2> RegularPolygon(int sides, double acrossFlats)
        {
            double apothem = acrossFlats / 2;
            double circumradius = apothem / Math.Cos(Math.PI / sides);
            var pts = new List<Vec2>();
            for (int k = 0; k < sides; k++)
            {
                double a = 2 * Math.PI * k / sides;
                pts.Add(new Vec2(circumradius * Math.Cos(a), circumradius * Math.Sin(a)));
            }
            return pts;
        }
    }

    public static class GeometryUtil
    {
        public static double SignedArea(IList<Vec2> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++)
            {
                Vec2 c = p[i], n = p[(i + 1) % p.Count];
                a += c.X * n.Y - n.X * c.Y;
            }
            return a / 2;
        }

        /// <summary>Smallest distance between two closed polygons; 0 if they touch, cross or nest.</summary>
        public static double MinDistance(IList<Vec2> a, IList<Vec2> b)
        {
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                    if (SegmentsIntersect(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count]))
                        return 0;

            if (PointInPolygon(a[0], b) || PointInPolygon(b[0], a)) return 0;

            double best = double.MaxValue;
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                {
                    Vec2 b0 = b[j], b1 = b[(j + 1) % b.Count];
                    best = Math.Min(best, PointSegmentDistance(a[i], b0, b1));
                    Vec2 a0 = a[i], a1 = a[(i + 1) % a.Count];
                    best = Math.Min(best, PointSegmentDistance(b[j], a0, a1));
                }
            return best;
        }

        public static double PointSegmentDistance(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            double len2 = Vec2.Dot(ab, ab);
            if (len2 < 1e-18) return (p - a).Length;
            double t = Math.Max(0, Math.Min(1, Vec2.Dot(p - a, ab) / len2));
            return (p - (a + ab * t)).Length;
        }

        public static bool SegmentsIntersect(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
        {
            double d1 = Vec2.Cross(q2 - q1, p1 - q1);
            double d2 = Vec2.Cross(q2 - q1, p2 - q1);
            double d3 = Vec2.Cross(p2 - p1, q1 - p1);
            double d4 = Vec2.Cross(p2 - p1, q2 - p1);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                   ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        public static bool PointInPolygon(Vec2 pt, IList<Vec2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y) &&
                    pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            }
            return inside;
        }
    }
}
