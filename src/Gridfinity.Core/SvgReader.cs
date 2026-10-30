using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Gridfinity.Core
{
    /// <summary>
    /// Minimal SVG geometry reader: closed path / polygon / rect / circle / ellipse outlines, with transforms,
    /// converted to millimetres with Y up. Curves are flattened to short straight steps.
    /// </summary>
    internal static class SvgReader
    {
        // 2D affine matrix [a c e; b d f].
        private struct Mat
        {
            public double A, B, C, D, E, F;
            public static readonly Mat Identity = new Mat { A = 1, D = 1 };
            public Vec2 Apply(Vec2 p) { return new Vec2(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F); }
            public static Mat operator *(Mat m, Mat n)
            {
                return new Mat
                {
                    A = m.A * n.A + m.C * n.B, B = m.B * n.A + m.D * n.B,
                    C = m.A * n.C + m.C * n.D, D = m.B * n.C + m.D * n.D,
                    E = m.A * n.E + m.C * n.F + m.E, F = m.B * n.E + m.D * n.F + m.F
                };
            }
        }

        public static List<List<Vec2>> Read(string text, string name, List<string> notes)
        {
            XDocument doc = XDocument.Parse(text);
            XElement root = doc.Root;
            if (root == null || root.Name.LocalName != "svg") throw new FormatException(name + ": not an SVG file.");

            Mat rootM = RootTransform(root, name, notes);
            var loops = new List<List<Vec2>>();
            int open = 0;
            Walk(root, rootM, loops, ref open);
            if (open > 0) notes.Add(name + ": skipped " + open + " open path(s); only closed outlines become pockets.");

            // SVG Y points down; flip so the outline is not mirrored.
            return loops.Select(l => l.Select(q => new Vec2(q.X, -q.Y)).ToList()).ToList();
        }

        private static Mat RootTransform(XElement root, string name, List<string> notes)
        {
            double wMm, hMm;
            bool hasW = Length((string)root.Attribute("width"), out wMm);
            bool hasH = Length((string)root.Attribute("height"), out hMm);
            var vb = Numbers((string)root.Attribute("viewBox"));

            if (vb.Count == 4 && vb[2] > 0 && vb[3] > 0)
            {
                double sx = hasW ? wMm / vb[2] : hasH ? hMm / vb[3] : 1.0;
                double sy = hasH ? hMm / vb[3] : sx;
                if (!hasW && !hasH) notes.Add(name + ": no width/height in the SVG; one unit is taken as 1 mm.");
                return new Mat { A = sx, D = sy, E = -vb[0] * sx, F = -vb[1] * sy };
            }
            if (!hasW) notes.Add(name + ": no viewBox or width in the SVG; one unit is taken as 1 mm.");
            return Mat.Identity;
        }

        // Converts an SVG length to millimetres. Unitless and px use the CSS 96 px per inch.
        private static bool Length(string s, out double mm)
        {
            mm = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var m = Regex.Match(s.Trim(), @"^([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*([a-z%]*)$");
            if (!m.Success) return false;
            double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            switch (m.Groups[2].Value)
            {
                case "mm": mm = v; return true;
                case "cm": mm = v * 10; return true;
                case "in": mm = v * 25.4; return true;
                case "pt": mm = v * 25.4 / 72; return true;
                case "pc": mm = v * 25.4 / 6; return true;
                case "": case "px": mm = v * 25.4 / 96; return true;
                default: return false; // % and em cannot be resolved
            }
        }

        private static void Walk(XElement el, Mat parent, List<List<Vec2>> loops, ref int open)
        {
            string tag = el.Name.LocalName;
            if (tag == "defs" || tag == "clipPath" || tag == "mask" || tag == "symbol" || tag == "marker" || tag == "pattern") return;
            if ((string)el.Attribute("display") == "none") return;

            Mat m = parent * Transform((string)el.Attribute("transform"));
            var found = new List<List<Vec2>>();
            switch (tag)
            {
                case "path": found.AddRange(PathLoops((string)el.Attribute("d") ?? "", ref open)); break;
                case "polygon": found.Add(Pairs((string)el.Attribute("points"))); break;
                case "polyline":
                    {
                        var p = Pairs((string)el.Attribute("points"));
                        if (p.Count > 2 && (p[0] - p[p.Count - 1]).Length < 1e-3) found.Add(p); else open++;
                        break;
                    }
                case "rect":
                    {
                        double x = D(el, "x"), y = D(el, "y"), w = D(el, "width"), h = D(el, "height");
                        if (w > 0 && h > 0) found.Add(new List<Vec2> { new Vec2(x, y), new Vec2(x + w, y), new Vec2(x + w, y + h), new Vec2(x, y + h) });
                        break;
                    }
                case "circle": found.Add(Ellipse(D(el, "cx"), D(el, "cy"), D(el, "r"), D(el, "r"))); break;
                case "ellipse": found.Add(Ellipse(D(el, "cx"), D(el, "cy"), D(el, "rx"), D(el, "ry"))); break;
            }
            foreach (var l in found)
                if (l.Count >= 3) loops.Add(l.Select(m.Apply).ToList());

            foreach (var child in el.Elements()) Walk(child, m, loops, ref open);
        }

        private static double D(XElement el, string attr)
        {
            double mm;
            string s = (string)el.Attribute(attr);
            if (string.IsNullOrEmpty(s)) return 0;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out mm) ? mm : (Length(s, out mm) ? mm * 96 / 25.4 : 0);
        }

        private static List<Vec2> Ellipse(double cx, double cy, double rx, double ry)
        {
            var pts = new List<Vec2>();
            if (rx <= 0 || ry <= 0) return pts;
            for (int i = 0; i < 120; i++)
            {
                double a = 2 * Math.PI * i / 120;
                pts.Add(new Vec2(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
            }
            return pts;
        }

        private static List<Vec2> Pairs(string s)
        {
            var n = Numbers(s);
            var pts = new List<Vec2>();
            for (int i = 0; i + 1 < n.Count; i += 2) pts.Add(new Vec2(n[i], n[i + 1]));
            return pts;
        }

        private static readonly Regex NumberRx = new Regex(@"[-+]?(?:[0-9]*\.[0-9]+|[0-9]+\.?)(?:[eE][-+]?[0-9]+)?");

        private static List<double> Numbers(string s)
        {
            var list = new List<double>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (Match m in NumberRx.Matches(s)) list.Add(double.Parse(m.Value, CultureInfo.InvariantCulture));
            return list;
        }

        private static Mat Transform(string s)
        {
            Mat m = Mat.Identity;
            if (string.IsNullOrWhiteSpace(s)) return m;
            foreach (Match t in Regex.Matches(s, @"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)"))
            {
                var a = Numbers(t.Groups[2].Value);
                Mat n = Mat.Identity;
                switch (t.Groups[1].Value)
                {
                    case "matrix": if (a.Count == 6) n = new Mat { A = a[0], B = a[1], C = a[2], D = a[3], E = a[4], F = a[5] }; break;
                    case "translate": n = new Mat { A = 1, D = 1, E = a.Count > 0 ? a[0] : 0, F = a.Count > 1 ? a[1] : 0 }; break;
                    case "scale": n = new Mat { A = a.Count > 0 ? a[0] : 1, D = a.Count > 1 ? a[1] : (a.Count > 0 ? a[0] : 1) }; break;
                    case "rotate":
                        {
                            double r = (a.Count > 0 ? a[0] : 0) * Math.PI / 180, c = Math.Cos(r), sn = Math.Sin(r);
                            n = new Mat { A = c, B = sn, C = -sn, D = c };
                            if (a.Count == 3)
                                n = new Mat { A = 1, D = 1, E = a[1], F = a[2] } * n * new Mat { A = 1, D = 1, E = -a[1], F = -a[2] };
                            break;
                        }
                    case "skewX": n = new Mat { A = 1, D = 1, C = Math.Tan((a.Count > 0 ? a[0] : 0) * Math.PI / 180) }; break;
                    case "skewY": n = new Mat { A = 1, D = 1, B = Math.Tan((a.Count > 0 ? a[0] : 0) * Math.PI / 180) }; break;
                }
                m = m * n;
            }
            return m;
        }

        // ---- Path data ---------------------------------------------------------------------------------

        private static readonly Regex TokenRx = new Regex(@"[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:[0-9]*\.[0-9]+|[0-9]+\.?)(?:[eE][-+]?[0-9]+)?");

        private static List<List<Vec2>> PathLoops(string d, ref int open)
        {
            var tokens = TokenRx.Matches(d).Cast<Match>().Select(m => m.Value).ToList();
            var loops = new List<List<Vec2>>();
            List<Vec2> cur = null;
            Vec2 pos = new Vec2(0, 0), start = pos, lastCtrl = pos;
            char cmd = ' ', prev = ' ';
            int i = 0;

            Func<double> num = () => double.Parse(tokens[i++], CultureInfo.InvariantCulture);
            Func<bool> hasNum = () => i < tokens.Count && !char.IsLetter(tokens[i][0]);
            Action finish = null;
            int openCount = 0;
            finish = () =>
            {
                if (cur == null) return;
                if (cur.Count > 2 && (cur[0] - cur[cur.Count - 1]).Length < 1e-3) { cur.RemoveAt(cur.Count - 1); loops.Add(cur); }
                else if (cur.Count > 1) openCount++;
                cur = null;
            };

            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i][0])) cmd = tokens[i++][0];
                else if (cmd == ' ') { i++; continue; }

                bool rel = char.IsLower(cmd);
                char up = char.ToUpperInvariant(cmd);
                Vec2 o = rel ? pos : new Vec2(0, 0);

                switch (up)
                {
                    case 'M':
                        finish();
                        pos = o + new Vec2(num(), num());
                        start = pos;
                        cur = new List<Vec2> { pos };
                        cmd = rel ? 'l' : 'L'; // further pairs are line-tos
                        break;
                    case 'L': pos = o + new Vec2(num(), num()); Add(ref cur, pos); break;
                    case 'H': pos = new Vec2((rel ? pos.X : 0) + num(), pos.Y); Add(ref cur, pos); break;
                    case 'V': pos = new Vec2(pos.X, (rel ? pos.Y : 0) + num()); Add(ref cur, pos); break;
                    case 'C':
                        {
                            Vec2 c1 = o + new Vec2(num(), num()), c2 = o + new Vec2(num(), num()), e = o + new Vec2(num(), num());
                            Cubic(ref cur, pos, c1, c2, e); lastCtrl = c2; pos = e; break;
                        }
                    case 'S':
                        {
                            Vec2 c1 = "CcSs".IndexOf(prev) >= 0 ? pos * 2 - lastCtrl : pos;
                            Vec2 c2 = o + new Vec2(num(), num()), e = o + new Vec2(num(), num());
                            Cubic(ref cur, pos, c1, c2, e); lastCtrl = c2; pos = e; break;
                        }
                    case 'Q':
                        {
                            Vec2 c = o + new Vec2(num(), num()), e = o + new Vec2(num(), num());
                            Quad(ref cur, pos, c, e); lastCtrl = c; pos = e; break;
                        }
                    case 'T':
                        {
                            Vec2 c = "QqTt".IndexOf(prev) >= 0 ? pos * 2 - lastCtrl : pos;
                            Vec2 e = o + new Vec2(num(), num());
                            Quad(ref cur, pos, c, e); lastCtrl = c; pos = e; break;
                        }
                    case 'A':
                        {
                            double rx = num(), ry = num(), rot = num(), large = num(), sweep = num();
                            Vec2 e = o + new Vec2(num(), num());
                            Arc(ref cur, pos, rx, ry, rot, large != 0, sweep != 0, e); pos = e; break;
                        }
                    case 'Z':
                        if (cur != null) cur.Add(start);
                        pos = start;
                        finish();
                        break;
                    default: i++; break;
                }
                prev = cmd;
                if (up == 'Z' && hasNum()) cmd = 'L';
            }
            finish();
            open += openCount;
            return loops;
        }

        private static void Add(ref List<Vec2> cur, Vec2 p)
        {
            if (cur == null) cur = new List<Vec2>();
            cur.Add(p);
        }

        private static void Cubic(ref List<Vec2> cur, Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3)
        {
            const int n = 16;
            for (int k = 1; k <= n; k++)
            {
                double t = (double)k / n, u = 1 - t;
                Add(ref cur, p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t));
            }
        }

        private static void Quad(ref List<Vec2> cur, Vec2 p0, Vec2 p1, Vec2 p2)
        {
            const int n = 12;
            for (int k = 1; k <= n; k++)
            {
                double t = (double)k / n, u = 1 - t;
                Add(ref cur, p0 * (u * u) + p1 * (2 * u * t) + p2 * (t * t));
            }
        }

        // Endpoint-to-centre conversion from the SVG specification (appendix B.2.4).
        private static void Arc(ref List<Vec2> cur, Vec2 p0, double rx, double ry, double rotDeg, bool large, bool sweep, Vec2 p1)
        {
            rx = Math.Abs(rx); ry = Math.Abs(ry);
            if (rx < 1e-12 || ry < 1e-12 || (p1 - p0).Length < 1e-12) { Add(ref cur, p1); return; }

            double phi = rotDeg * Math.PI / 180, cp = Math.Cos(phi), sp = Math.Sin(phi);
            Vec2 h = (p0 - p1) * 0.5;
            double x1 = cp * h.X + sp * h.Y, y1 = -sp * h.X + cp * h.Y;
            double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            if (lambda > 1) { double s = Math.Sqrt(lambda); rx *= s; ry *= s; }

            double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            double coef = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
            double cxp = coef * rx * y1 / ry, cyp = -coef * ry * x1 / rx;
            Vec2 mid = (p0 + p1) * 0.5;
            Vec2 c = new Vec2(cp * cxp - sp * cyp + mid.X, sp * cxp + cp * cyp + mid.Y);

            double t1 = Math.Atan2((y1 - cyp) / ry, (x1 - cxp) / rx);
            double t2 = Math.Atan2((-y1 - cyp) / ry, (-x1 - cxp) / rx);
            double dt = t2 - t1;
            if (sweep && dt < 0) dt += 2 * Math.PI;
            if (!sweep && dt > 0) dt -= 2 * Math.PI;

            int n = Math.Max(4, (int)Math.Ceiling(Math.Abs(dt) * 180 / Math.PI / 5));
            for (int k = 1; k <= n; k++)
            {
                double t = t1 + dt * k / n;
                double ex = rx * Math.Cos(t), ey = ry * Math.Sin(t);
                Add(ref cur, new Vec2(cp * ex - sp * ey + c.X, sp * ex + cp * ey + c.Y));
            }
        }
    }
}
