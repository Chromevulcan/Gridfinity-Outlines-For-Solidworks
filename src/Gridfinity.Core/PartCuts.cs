using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace Gridfinity.Core
{
    /// <summary>One cut: these loops, cut down from the top of the tray to this depth.</summary>
    public sealed class CutLayer
    {
        public double Depth;
        public List<Loop> Loops = new List<Loop>();
        public Pocket Pocket;
    }

    /// <summary>
    /// How traced parts (Outline pockets) are cut. A part's top sits <see cref="TrayDefinition.PartTopOffset"/> above
    /// the tray's top surface (the Y offset); whatever of the part lies below the surface is removed.
    ///
    /// Flat bottom: one cut, (thickness - offset) deep, the shape of the outline.
    ///
    /// Round bottom ("follows the shape"): the part is modelled as the union of balls that fit inside its outline,
    /// each touching the part's top. So a straight shaft of width w is a cylinder of diameter w, a handle that widens
    /// gets deeper, and a tapered tip gets shallower. The depth under each point is found on a 0.25 mm grid; the pocket
    /// is then cut as steps (<see cref="TrayDefinition.RoundStep"/>), each to the deeper side so the part never rests
    /// on a step. Each step is one sketch shared by all parts, so a tray needs (deepest cut / step) cut features.
    /// </summary>
    public static class PartCuts
    {
        public const double GridMm = 0.25;
        /// <summary>Each step is grown outward by this much, mm, to cover the grid's rounding.</summary>
        public const double StepGrow = 0.4;

        public static bool IsPart(Pocket p) { return p.Shape == PocketShape.Outline; }

        public static double TopOffsetFor(Pocket p, TrayDefinition t) { return p.TopOffset ?? t.PartTopOffset; }

        /// <summary>Deepest point of the cut below the tray's top surface (0 or less = nothing is cut).</summary>
        public static double MaxCutDepth(Pocket p, TrayDefinition t)
        {
            if (!IsPart(p)) return p.Depth;
            double off = TopOffsetFor(p, t);
            if (p.Bottom == PartBottom.Flat) return p.Depth - off;
            HeightField f = Field(p, t);
            double max = f == null ? 0 : f.Max - off;
            // Cut depths are rounded up to whole steps.
            double step = Step(t);
            return max <= 0 ? max : Math.Ceiling(max / step - 1e-6) * step;
        }

        /// <summary>The cuts for one pocket, in the pocket's placed position (tray coordinates).</summary>
        public static List<CutLayer> Layers(Pocket p, TrayDefinition t)
        {
            var layers = new List<CutLayer>();
            double clr = t.GeometryClearance;

            if (!IsPart(p) || p.Bottom == PartBottom.Flat)
            {
                double depth = IsPart(p) ? p.Depth - TopOffsetFor(p, t) : p.Depth;
                if (depth <= 1e-6) return layers;
                var layer = new CutLayer { Depth = depth, Pocket = p };
                layer.Loops.Add(p.BuildLoop(clr));
                layer.Loops.AddRange(p.BuildIslandLoops(clr));
                layers.Add(layer);
                return layers;
            }

            HeightField field = Field(p, t);
            if (field == null) return layers;
            double off = TopOffsetFor(p, t), step = Step(t);
            double maxCut = field.Max - off;
            if (maxCut <= 1e-6) return layers;

            PathsD footprint = LocalFootprint(p, clr);
            double rot = p.RotationDeg * Math.PI / 180.0;
            var at = new Vec2(p.X, p.Y);
            int n = (int)Math.Ceiling(maxCut / step - 1e-6);
            for (int j = 1; j <= n; j++)
            {
                // Everything deeper than the step above is cut to this step's depth.
                double threshold = off + (j - 1) * step;
                PathsD region = field.RegionAbove(threshold);
                if (region.Count == 0) break;
                // Grow each step slightly so grid error can only make it wider, never leave the part sitting on it.
                region = Clipper.InflatePaths(region, StepGrow, JoinType.Round, EndType.Polygon, 2.0, 4, 0.02);
                region = Clipper.Intersect(region, footprint, FillRule.EvenOdd, 4);
                region = Clipper.SimplifyPaths(region, 0.05, true);
                var layer = new CutLayer { Depth = j * step, Pocket = p };
                foreach (PathD path in region)
                {
                    if (path.Count < 3 || Math.Abs(Clipper.Area(path)) < 0.05) continue;
                    var pts = path.Select(q => new Vec2(q.x, q.y)).ToList();
                    layer.Loops.Add(OutlineGeometry.ToLoop(pts).Transformed(rot, at));
                }
                if (layer.Loops.Count > 0) layers.Add(layer);
            }
            return layers;
        }

        private static double Step(TrayDefinition t) { return t.RoundStep > 0.05 ? t.RoundStep : 0.4; }

        /// <summary>The outline with clearance, minus kept islands, in local coordinates.</summary>
        private static PathsD LocalFootprint(Pocket p, double clr)
        {
            var paths = new PathsD();
            paths.Add(ToPath(p.BuildLocalLoop(clr).ToPolyline(1.0)));
            foreach (var island in p.BuildLocalIslands(clr)) paths.Add(ToPath(island));
            return paths;
        }

        private static PathD ToPath(IEnumerable<Vec2> pts)
        {
            var path = new PathD();
            foreach (var q in pts) path.Add(new PointD(q.X, q.Y));
            return path;
        }

        /// <summary>The (cached) height field of a round-bottomed part, in local coordinates.</summary>
        public static HeightField Field(Pocket p, TrayDefinition t)
        {
            if (!IsPart(p) || p.Bottom != PartBottom.Round || p.Points == null || p.Points.Count < 3) return null;
            double clr = p.Fit(t.GeometryClearance);
            string key = string.Join("|", clr, p.RoundMaxThickness, p.KeepIslands, p.Points.Count,
                                     p.Points[0].X, p.Points[0].Y, p.Points[p.Points.Count / 2].X, p.Holes == null ? 0 : p.Holes.Count);
            if (p.HeightCache is HeightField cached && p.HeightCacheKey == key) return cached;
            var field = HeightField.Build(LocalFootprint(p, t.GeometryClearance), GridMm, p.RoundMaxThickness);
            p.HeightCache = field;
            p.HeightCacheKey = key;
            return field;
        }
    }

    /// <summary>
    /// Thickness of a round-bottomed part at every point of its outline: the bottom of the deepest ball that fits
    /// inside the outline, touches the part's top, and lies over that point.
    /// </summary>
    public sealed class HeightField
    {
        public double OriginX, OriginY, Res;
        public int Nx, Ny;
        /// <summary>Thickness below the part's top, mm; values outside the outline are carried a few cells out so contours meet the edge cleanly.</summary>
        public float[] H;
        public bool[] Inside;
        public double Max;

        public double At(int i, int j) { return H[j * Nx + i]; }

        public static HeightField Build(PathsD footprint, double res, double maxThickness)
        {
            RectD b = Clipper.GetBounds(footprint);
            const int pad = 4;
            var f = new HeightField
            {
                Res = res,
                OriginX = b.left - pad * res,
                OriginY = b.top - pad * res, // Clipper's RectD 'top' is the minimum Y
                Nx = (int)Math.Ceiling((b.right - b.left) / res) + 2 * pad + 1,
                Ny = (int)Math.Ceiling((b.bottom - b.top) / res) + 2 * pad + 1,
            };
            int nx = f.Nx, ny = f.Ny, n = nx * ny;
            f.Inside = Rasterize(footprint, f);

            // Squared distance (in cells) from each inside cell to the nearest outside cell.
            double[] d2 = DistanceSquared(f.Inside, nx, ny);
            var r = new double[n]; // inscribed radius at the cell, mm
            for (int k = 0; k < n; k++) r[k] = f.Inside[k] ? Math.Max(0, Math.Sqrt(d2[k]) - 0.5) * res : 0;

            var edges = new List<double[]>();
            foreach (PathD path in footprint)
                for (int a = 0; a < path.Count; a++)
                {
                    PointD p0 = path[a], p1 = path[(a + 1) % path.Count];
                    edges.Add(new[] { p0.x, p0.y, p1.x, p1.y });
                }

            var h = new double[n];
            for (int k = 0; k < n; k++) h[k] = f.Inside[k] ? 2 * r[k] : 0; // a cell's own ball

            // Balls centred on the ridge (the medial axis) are the ones that matter; stamp each one.
            for (int j = 1; j < ny - 1; j++)
            for (int i = 1; i < nx - 1; i++)
            {
                int k = j * nx + i;
                if (!f.Inside[k] || r[k] <= 0) continue;
                if (!IsRidge(r, nx, i, j, res)) continue;
                // The grid radius can be a quarter of a cell out; use the exact distance to the outline for the balls that count.
                double rq = ExactDistance(f.OriginX + i * res, f.OriginY + j * res, edges, r[k] + 2 * res);
                int reach = (int)Math.Ceiling(rq / res);
                for (int dj = -reach; dj <= reach; dj++)
                {
                    int jj = j + dj;
                    if (jj < 0 || jj >= ny) continue;
                    for (int di = -reach; di <= reach; di++)
                    {
                        int ii = i + di;
                        if (ii < 0 || ii >= nx) continue;
                        int kk = jj * nx + ii;
                        if (!f.Inside[kk]) continue;
                        double dd = (di * di + dj * dj) * res * res;
                        if (dd > rq * rq) continue;
                        double v = rq + Math.Sqrt(rq * rq - dd);
                        if (v > h[kk]) h[kk] = v;
                    }
                }
            }

            if (maxThickness > 0)
                for (int k = 0; k < n; k++) h[k] = Math.Min(h[k], maxThickness);

            f.Max = 0;
            for (int k = 0; k < n; k++) if (f.Inside[k] && h[k] > f.Max) f.Max = h[k];

            // Carry edge values a few cells outward (largest neighbour), so a contour that reaches the outline
            // crosses it; intersecting with the exact outline then gives the true edge, not a stair-step.
            var cur = h;
            var known = (bool[])f.Inside.Clone();
            for (int pass = 0; pass < pad - 1; pass++)
            {
                var next = (double[])cur.Clone();
                var nextKnown = (bool[])known.Clone();
                for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    if (known[k]) continue;
                    double best = double.NegativeInfinity;
                    for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= nx || jj >= ny) continue;
                        int kk = jj * nx + ii;
                        if (known[kk] && cur[kk] > best) best = cur[kk];
                    }
                    if (best > double.NegativeInfinity) { next[k] = best; nextKnown[k] = true; }
                }
                cur = next;
                known = nextKnown;
            }

            f.H = new float[n];
            for (int k = 0; k < n; k++) f.H[k] = known[k] ? (float)cur[k] : -1000f;
            return f;
        }

        private static double ExactDistance(double x, double y, List<double[]> edges, double limit)
        {
            double best = limit * limit;
            foreach (var e in edges)
            {
                // Cheap reject on the bounding box of the edge.
                double minX = Math.Min(e[0], e[2]), maxX = Math.Max(e[0], e[2]), minY = Math.Min(e[1], e[3]), maxY = Math.Max(e[1], e[3]);
                double bx = x < minX ? minX - x : x > maxX ? x - maxX : 0, by = y < minY ? minY - y : y > maxY ? y - maxY : 0;
                if (bx * bx + by * by >= best) continue;
                double dx = e[2] - e[0], dy = e[3] - e[1], len2 = dx * dx + dy * dy;
                double t = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((x - e[0]) * dx + (y - e[1]) * dy) / len2));
                double qx = e[0] + t * dx - x, qy = e[1] + t * dy - y, d2 = qx * qx + qy * qy;
                if (d2 < best) best = d2;
            }
            return Math.Sqrt(best);
        }

        // A cell is on the ridge unless a neighbour's ball contains its ball (nearly).
        private static bool IsRidge(double[] r, int nx, int i, int j, double res)
        {
            double rq = r[j * nx + i];
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                if (di == 0 && dj == 0) continue;
                double rn = r[(j + dj) * nx + i + di];
                double dist = Math.Sqrt(di * di + dj * dj) * res;
                if (rn - rq >= 0.9 * dist) return false;
            }
            return true;
        }

        /// <summary>The region where the part is thicker than <paramref name="level"/>, as closed paths (local mm).</summary>
        public PathsD RegionAbove(double level)
        {
            var loops = MarchingSquares.Contours(H, Nx, Ny, (float)level);
            var paths = new PathsD();
            foreach (var loop in loops)
            {
                var path = new PathD(loop.Count);
                foreach (var q in loop) path.Add(new PointD(OriginX + q.X * Res, OriginY + q.Y * Res));
                if (path.Count >= 3) paths.Add(path);
            }
            // Even-odd union sorts out outer edges and holes whichever way the loops run.
            return Clipper.Union(paths, new PathsD(), FillRule.EvenOdd, 4);
        }

        // Cell centres inside the footprint (even-odd), by scanlines.
        private static bool[] Rasterize(PathsD paths, HeightField f)
        {
            var inside = new bool[f.Nx * f.Ny];
            var xs = new List<double>();
            for (int j = 0; j < f.Ny; j++)
            {
                double y = f.OriginY + j * f.Res;
                xs.Clear();
                foreach (PathD path in paths)
                {
                    for (int a = 0, c = path.Count; a < c; a++)
                    {
                        PointD p0 = path[a], p1 = path[(a + 1) % c];
                        if ((p0.y <= y) == (p1.y <= y)) continue;
                        xs.Add(p0.x + (y - p0.y) / (p1.y - p0.y) * (p1.x - p0.x));
                    }
                }
                xs.Sort();
                for (int m = 0; m + 1 < xs.Count; m += 2)
                {
                    int i0 = (int)Math.Ceiling((xs[m] - f.OriginX) / f.Res);
                    int i1 = (int)Math.Floor((xs[m + 1] - f.OriginX) / f.Res);
                    for (int i = Math.Max(0, i0); i <= Math.Min(f.Nx - 1, i1); i++) inside[j * f.Nx + i] = true;
                }
            }
            return inside;
        }

        // Exact Euclidean distance transform (Felzenszwalb and Huttenlocher), squared, in cells.
        private static double[] DistanceSquared(bool[] inside, int nx, int ny)
        {
            const double INF = 1e20;
            var g = new double[nx * ny];
            for (int k = 0; k < g.Length; k++) g[k] = inside[k] ? INF : 0;
            var line = new double[Math.Max(nx, ny)];
            var outLine = new double[Math.Max(nx, ny)];
            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < ny; j++) line[j] = g[j * nx + i];
                Dt1D(line, ny, outLine);
                for (int j = 0; j < ny; j++) g[j * nx + i] = outLine[j];
            }
            for (int j = 0; j < ny; j++)
            {
                Array.Copy(g, j * nx, line, 0, nx);
                Dt1D(line, nx, outLine);
                Array.Copy(outLine, 0, g, j * nx, nx);
            }
            return g;
        }

        private static void Dt1D(double[] f, int n, double[] d)
        {
            var v = new int[n];
            var z = new double[n + 1];
            int k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s;
                while (true)
                {
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                    if (s <= z[k] && k > 0) k--;
                    else break;
                }
                if (s <= z[k]) { v[0] = q; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity; k = 0; continue; }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                double dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }
    }

    /// <summary>Closed iso-contours of a grid (cell units, x along i, y along j).</summary>
    public static class MarchingSquares
    {
        public static List<List<Vec2>> Contours(float[] v, int nx, int ny, float level)
        {
            // Work on a grid padded with "below" so every contour closes.
            int w = nx + 2, h = ny + 2;
            Func<int, int, float> at = (i, j) =>
                (i <= 0 || j <= 0 || i >= w - 1 || j >= h - 1) ? float.NegativeInfinity : v[(j - 1) * nx + (i - 1)];

            // Each crossing is identified by the grid edge it lies on: horizontal edge (i,j)-(i+1,j) = 2*(j*w+i),
            // vertical edge (i,j)-(i,j+1) = 2*(j*w+i)+1. Each such edge is shared by two cells, so every crossing ends
            // up with exactly two segments and the segments chain into loops.
            var next = new Dictionary<long, long>();
            var point = new Dictionary<long, Vec2>();

            Func<int, int, long> hKey = (i, j) => 2L * (j * (long)w + i);
            Func<int, int, long> vKey = (i, j) => 2L * (j * (long)w + i) + 1;

            Func<long, Vec2> pos = key =>
            {
                Vec2 cachedPoint;
                if (point.TryGetValue(key, out cachedPoint)) return cachedPoint;
                long cell = key / 2;
                int i = (int)(cell % w), j = (int)(cell / w);
                bool vertical = key % 2 == 1;
                float a = at(i, j), b = vertical ? at(i, j + 1) : at(i + 1, j);
                double t;
                if (float.IsNegativeInfinity(a)) t = 0.0;
                else if (float.IsNegativeInfinity(b)) t = 1.0;
                else t = Math.Abs(b - a) < 1e-12 ? 0.5 : (level - a) / (double)(b - a);
                t = Math.Max(0, Math.Min(1, t));
                // Grid coordinates back in the unpadded frame.
                var q = vertical ? new Vec2(i - 1, j - 1 + t) : new Vec2(i - 1 + t, j - 1);
                point[key] = q;
                return q;
            };

            Action<long, long> seg = (from, to) => { next[from] = to; };

            for (int j = 0; j < h - 1; j++)
            for (int i = 0; i < w - 1; i++)
            {
                bool a = at(i, j) > level, b = at(i + 1, j) > level, c = at(i + 1, j + 1) > level, d = at(i, j + 1) > level;
                int code = (a ? 1 : 0) | (b ? 2 : 0) | (c ? 4 : 0) | (d ? 8 : 0);
                if (code == 0 || code == 15) continue;
                long bottom = hKey(i, j), right = vKey(i + 1, j), top = hKey(i, j + 1), left = vKey(i, j);
                // Segments run with the "above" region on their left (counter-clockwise outer loops).
                switch (code)
                {
                    case 1: seg(left, bottom); break;
                    case 2: seg(bottom, right); break;
                    case 3: seg(left, right); break;
                    case 4: seg(right, top); break;
                    case 5:
                    {
                        float centre = (at(i, j) + at(i + 1, j) + at(i + 1, j + 1) + at(i, j + 1)) / 4;
                        if (centre > level) { seg(left, top); seg(right, bottom); }
                        else { seg(left, bottom); seg(right, top); }
                        break;
                    }
                    case 6: seg(bottom, top); break;
                    case 7: seg(left, top); break;
                    case 8: seg(top, left); break;
                    case 9: seg(top, bottom); break;
                    case 10:
                    {
                        float centre = (at(i, j) + at(i + 1, j) + at(i + 1, j + 1) + at(i, j + 1)) / 4;
                        if (centre > level) { seg(bottom, left); seg(top, right); }
                        else { seg(bottom, right); seg(top, left); }
                        break;
                    }
                    case 11: seg(top, right); break;
                    case 12: seg(right, left); break;
                    case 13: seg(right, bottom); break;
                    case 14: seg(bottom, left); break;
                }
            }

            var loops = new List<List<Vec2>>();
            var used = new HashSet<long>();
            foreach (long start in next.Keys)
            {
                if (used.Contains(start)) continue;
                var loop = new List<Vec2>();
                long k = start;
                while (!used.Contains(k))
                {
                    used.Add(k);
                    loop.Add(pos(k));
                    long nk;
                    if (!next.TryGetValue(k, out nk)) break;
                    k = nk;
                }
                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }
    }
}
