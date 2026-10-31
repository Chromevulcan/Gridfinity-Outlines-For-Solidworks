using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gridfinity.Core;
using Gridfinity.Trace;
using OpenCvSharp;
using Xunit;

namespace Gridfinity.Core.Tests
{
    /// <summary>
    /// The tracer is checked against a made-up photo: a Letter sheet seen at an angle on a dark desk, with uneven light
    /// and noise, holding parts of known size.
    /// </summary>
    public class TracerTests
    {
        private const double PaperW = 215.9, PaperH = 279.4, DrawPpm = 6; // the "real" sheet is drawn at 6 px/mm

        private static Mat SyntheticPhoto(Action<Mat, Func<double, double, Point>> drawParts, bool landscapeCamera = false)
        {
            // 1. The sheet, flat and to scale, with the parts drawn on it.
            int sw = (int)Math.Round(PaperW * DrawPpm), sh = (int)Math.Round(PaperH * DrawPpm);
            var sheet = new Mat(sh, sw, MatType.CV_8UC3, new Scalar(238, 240, 242));
            Func<double, double, Point> at = (xMm, yMm) => new Point((int)Math.Round(xMm * DrawPpm), (int)Math.Round(sh - yMm * DrawPpm)); // y up
            drawParts(sheet, at);

            // 2. Seen in perspective, on a dark wooden desk.
            int W = 2400, H = 2000;
            var photo = new Mat(H, W, MatType.CV_8UC3, new Scalar(45, 70, 95));
            var src = new[] { new Point2f(0, 0), new Point2f(sw, 0), new Point2f(sw, sh), new Point2f(0, sh) };
            var dst = new[] { new Point2f(620, 150), new Point2f(1840, 210), new Point2f(1930, 1830), new Point2f(520, 1780) };
            using (var hm = Cv2.GetPerspectiveTransform(src, dst))
            using (var warped = new Mat())
            using (var mask = new Mat())
            {
                Cv2.WarpPerspective(sheet, warped, hm, new Size(W, H), InterpolationFlags.Linear);
                using (var white = new Mat(sh, sw, MatType.CV_8UC1, Scalar.White))
                    Cv2.WarpPerspective(white, mask, hm, new Size(W, H), InterpolationFlags.Nearest);
                warped.CopyTo(photo, mask);
            }

            // 3. Light falling off to one side, and sensor noise.
            using (var gradient = new Mat(H, W, MatType.CV_32FC3))
            {
                var gi = gradient.GetGenericIndexer<Vec3f>();
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float f = (float)(1.0 - 0.25 * x / W - 0.1 * y / H);
                        gi[y, x] = new Vec3f(f, f, f);
                    }
                using (var p32 = new Mat())
                {
                    photo.ConvertTo(p32, MatType.CV_32FC3);
                    Cv2.Multiply(p32, gradient, p32);
                    using (var noise = new Mat(H, W, MatType.CV_32FC3))
                    {
                        Cv2.Randn(noise, new Scalar(0, 0, 0), new Scalar(4, 4, 4));
                        Cv2.Add(p32, noise, p32);
                    }
                    p32.ConvertTo(photo, MatType.CV_8UC3);
                }
            }
            sheet.Dispose();
            return photo;
        }

        private static void Box(TracedShape s, out double w, out double h)
        {
            Vec2 lo, hi;
            OutlineGeometry.Bounds(s.Outline, out lo, out hi);
            w = hi.X - lo.X; h = hi.Y - lo.Y;
        }

        [Fact]
        public void Finds_the_paper_and_traces_parts_to_half_a_millimetre()
        {
            var dark = new Scalar(40, 40, 40);
            using (var photo = SyntheticPhoto((m, at) =>
            {
                // A 120 x 18 mm bar, a 40 mm disc, and a 60 x 30 mm plate with a 20 mm hole.
                Cv2.Rectangle(m, at(30, 220), at(150, 238), dark, -1);
                Cv2.Circle(m, at(60, 150), (int)Math.Round(20 * DrawPpm), dark, -1);
                Cv2.Rectangle(m, at(110, 60), at(170, 90), new Scalar(30, 60, 160), -1); // a red part
                Cv2.Circle(m, at(140, 75), (int)Math.Round(10 * DrawPpm), new Scalar(238, 240, 242), -1);
            }))
            {
                var notes = new List<string>();
                var o = new OutlineImportOptions { Paper = PaperSize.Letter };
                var r = PhotoTracer.Trace(photo, "parts", o, notes, null);
                Assert.True(r.Shapes.Count == 3, string.Join("\n", notes) + "\n" + string.Join("\n", r.Shapes));
                Assert.Equal(PaperW, r.PaperWidthMm, 1);

                double w, h;
                var plate = r.Shapes.Single(x => x.Holes.Count == 1);
                Box(plate, out w, out h);
                Assert.InRange(w, 59.5, 60.5); Assert.InRange(h, 29.5, 30.5);
                Vec2 lo, hi;
                OutlineGeometry.Bounds(plate.Holes[0], out lo, out hi);
                Assert.InRange(hi.X - lo.X, 19.5, 20.5);

                var others = r.Shapes.Where(x => x.Holes.Count == 0).OrderBy(x => x.Area).ToList();
                Box(others[0], out w, out h); // the 40 mm disc (1257 mm²)
                Assert.InRange(w, 39.5, 40.5); Assert.InRange(h, 39.5, 40.5);
                Box(others[1], out w, out h); // the 120 x 18 mm bar
                Assert.InRange(w, 119.5, 120.5); Assert.InRange(h, 17.5, 18.5);
            }
        }

        [Fact]
        public void Traced_parts_become_a_tray()
        {
            var dark = new Scalar(30, 30, 30);
            using (var photo = SyntheticPhoto((m, at) =>
            {
                Cv2.Rectangle(m, at(40, 200), at(160, 214), dark, -1);
                Cv2.Rectangle(m, at(40, 100), at(120, 112), dark, -1);
            }))
            {
                var notes = new List<string>();
                var o = new OutlineImportOptions { Paper = PaperSize.Letter, Bottom = PartBottom.Round, NameLabels = true };
                var r = PhotoTracer.Trace(photo, "drivers", o, notes, null);
                var tray = OutlineImporter.CreateTray("Drivers", r.Shapes, o, notes);
                var c = OutlineImporter.Resolve(tray, o.NameLabels);
                Assert.True(c.Success, c.Report.ToString() + "\n" + string.Join("\n", notes));
                Assert.Equal(2, c.Resolved.Pockets.Count(p => p.Bottom == PartBottom.Round && p.HasLabel));
            }
        }
    
        private static void RoundedRect(Mat m, Func<double, double, Point> at, double x0, double y0, double x1, double y1, double r, Scalar colour)
        {
            Cv2.Rectangle(m, at(x0 + r, y0), at(x1 - r, y1), colour, -1);
            Cv2.Rectangle(m, at(x0, y0 + r), at(x1, y1 - r), colour, -1);
            int pr = (int)Math.Round(r * DrawPpm);
            foreach (var c in new[] { at(x0 + r, y0 + r), at(x1 - r, y0 + r), at(x0 + r, y1 - r), at(x1 - r, y1 - r) })
                Cv2.Circle(m, c, pr, colour, -1);
        }

        [Fact]
        public void A_credit_card_on_the_paper_corrects_a_wrong_paper_size()
        {
            var dark = new Scalar(35, 35, 35);
            using (var photo = SyntheticPhoto((m, at) =>
            {
                Cv2.Rectangle(m, at(30, 220), at(150, 238), dark, -1);                          // 120 x 18 mm bar
                RoundedRect(m, at, 40, 60, 40 + 85.60, 60 + 53.98, 3.18, new Scalar(120, 60, 20)); // a blue bank card
            }))
            {
                // Told the sheet is 2.5 % larger than it is: without the card everything comes out 2.5 % too big.
                var o = new OutlineImportOptions { Paper = PaperSize.Custom, PaperWidth = PaperW * 1.025, PaperHeight = PaperH * 1.025 };
                var notes = new List<string>();
                var r = PhotoTracer.Trace(photo, "parts", o, notes, null);
                Assert.Equal(2, r.Shapes.Count);
                double w, h;
                Box(r.Shapes.OrderBy(x => x.Area).First(), out w, out h);
                Assert.True(w > 122.4, "uncorrected bar should be too long, was " + w);

                o.ScaleReference = ScaleReferenceKind.CreditCard;
                double k = ScaleReferences.Apply(r.Shapes, o, "parts", notes);
                Assert.True(r.Shapes.Count == 1, string.Join("\n", notes));   // the card is not a part
                Assert.InRange(k, 0.965, 0.985);
                Box(r.Shapes[0], out w, out h);
                Assert.InRange(w, 119.5, 120.5);
                Assert.InRange(h, 17.5, 18.5);
                Assert.Contains(notes, n => n.Contains("credit card scale reference measured"));
            }
        }
    }
}
