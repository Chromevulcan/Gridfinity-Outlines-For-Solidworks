using System;
using Gridfinity.Core;
using SolidWorks.Interop.sldworks;
using Loop = Gridfinity.Core.Loop;
using Segment = Gridfinity.Core.Segment;

namespace Gridfinity.Sw
{
    /// <summary>
    /// Builds a throw-away 10 x 10 mm block in a scratch part and measures where it ends up, so the real
    /// build does not depend on remembered defaults for extrude direction and start-offset direction.
    /// If anything looks unexpected it records a note and falls back to the defaults.
    /// </summary>
    internal static class DirectionProbe
    {
        public static DirectionFlags Run(ISldWorks app, string template, bool needTopPlane = false)
        {
            var flags = new DirectionFlags();
            ModelDoc2 doc = null;
            try
            {
                doc = app.NewDocument(template, 0, 0, 0) as ModelDoc2;
                if (doc == null)
                {
                    flags.Notes.Add("Probe: could not create a scratch part; using default directions.");
                    return flags;
                }

                Loop square = Shapes.RoundedRect(0, 0, 10, 10, 0);

                // Ask for a boss spanning Z = 3..7 mm and see where it actually lands.
                var boss = new FeaturePlan { Name = "ProbeBoss", Op = FeatureOp.Boss, StartZ = 3, Depth = 4 };
                boss.Loops.Add(square);
                FeatureExecutor.Execute(doc, boss, flags);

                double zMin, zMax;
                if (!TryGetZRange(doc, out zMin, out zMax))
                {
                    flags.Notes.Add("Probe: could not read the part box; using default directions.");
                    return flags;
                }

                if (Near(zMin, 3)) { /* start +, direction +: nothing to fix */ }
                else if (Near(zMax, 3)) { flags.ReverseBoss = true; }                                // [-1, 3]
                else if (Near(zMax, -3)) { flags.ReverseBoss = true; flags.FlipStartOffset = true; } // [-7, -3]
                else if (Near(zMin, -3)) { flags.FlipStartOffset = true; }                           // [-3, 1]
                else
                {
                    flags.Notes.Add("Probe: boss landed at Z " + zMin.ToString("0.##") + " to " + zMax.ToString("0.##") + " mm, which matches no known case; using default directions.");
                    flags.ReverseBoss = false;
                    flags.FlipStartOffset = false;
                    return flags;
                }

                // Verify the corrections with a second boss that must land at Z = 10..14 mm. Its top edge (14)
                // is above anything the first block can reach (7 at most), so the part box tells us.
                var boss2 = new FeaturePlan { Name = "ProbeBoss2", Op = FeatureOp.Boss, StartZ = 10, Depth = 4 };
                boss2.Loops.Add(square);
                FeatureExecutor.Execute(doc, boss2, flags);
                if (!TryGetZRange(doc, out zMin, out zMax) || !Near(zMax, 14))
                {
                    flags.Notes.Add("Probe: corrected boss did not land at Z 10 to 14 mm; results may be unreliable.");
                    return flags;
                }

                // Cut starting at Z = 13 into that second block: going up removes 1 mm of it (100 mm^3),
                // going down removes 3 mm (300 mm^3).
                double before = FeatureExecutor.Volume(doc) * 1e9; // m^3 -> mm^3
                var cut = new FeaturePlan { Name = "ProbeCut", Op = FeatureOp.Cut, StartZ = 13, Depth = 3 };
                cut.Loops.Add(square);
                FeatureExecutor.Execute(doc, cut, flags);
                double removed = before - FeatureExecutor.Volume(doc) * 1e9;

                if (Math.Abs(removed - 100) < 20) { /* cut went up: fine */ }
                else if (Math.Abs(removed - 300) < 20) { flags.ReverseCut = true; }
                else flags.Notes.Add("Probe: cut removed " + removed.ToString("0.#") + " mm^3 instead of 100 or 300; using default cut direction.");

                if (needTopPlane) ProbeTopPlane(app, template, flags);
            }
            catch (Exception ex)
            {
                flags.Notes.Add("Probe failed (" + ex.Message + "); using default directions.");
                flags.ReverseBoss = false;
                flags.ReverseCut = false;
                flags.FlipStartOffset = false;
            }
            finally
            {
                if (doc != null)
                {
                    try { app.CloseDoc(doc.GetTitle()); } catch { }
                }
            }
            return flags;
        }

        /// <summary>
        /// Works out how Top Plane sketches and cuts behave: which way sketch X and Y point, and which extrude settings send
        /// a cut from an offset towards +Y. Builds a block X 0..20, Y 0..20, Z 0..10 and tries cutting a 4 x 4 x 10 mm slot at
        /// X 3..7, Y 5..15, Z 4..8 with each combination until exactly that volume (160 mm^3) disappears.
        /// </summary>
        private static void ProbeTopPlane(ISldWorks app, string template, DirectionFlags flags)
        {
            ModelDoc2 doc = null;
            flags.TopProbing = true;
            try
            {
                doc = app.NewDocument(template, 0, 0, 0) as ModelDoc2;
                if (doc == null) { flags.Notes.Add("Top-plane probe: could not create a scratch part."); return; }

                var block = new FeaturePlan { Name = "ProbeBlock", Op = FeatureOp.Boss, StartZ = 0, Depth = 10 };
                block.Loops.Add(Shapes.RoundedRect(10, 10, 20, 20, 0));
                FeatureExecutor.Execute(doc, block, flags);

                foreach (int sx in new[] { 1, -1 })
                    foreach (int sy in new[] { 1, -1 })
                        foreach (int rev in new[] { 0, 1 })
                            foreach (int flip in new[] { 0, 1 })
                            {
                                flags.TopSx = sx; flags.TopSy = sy; flags.TopReverse = rev == 1; flags.TopFlipStart = flip == 1;
                                var cut = new FeaturePlan { Name = "ProbeTopCut", Op = FeatureOp.Cut, Plane = SketchPlane.Top, StartZ = 5, Depth = 10 };
                                cut.Loops.Add(Shapes.RoundedRect(5, 6, 4, 4, 0));
                                double before = FeatureExecutor.Volume(doc) * 1e9;
                                try { FeatureExecutor.Execute(doc, cut, flags); }
                                catch (BuildException) { continue; }
                                double removed = before - FeatureExecutor.Volume(doc) * 1e9;
                                if (Math.Abs(removed - 160) < 15)
                                {
                                    flags.TopOk = true;
                                    flags.Notes.Add("Top-plane probe: sketch X x" + sx + ", sketch Y x" + sy + ", reverse " + flags.TopReverse + ", flip start " + flags.TopFlipStart + ".");
                                    return;
                                }
                            }

                flags.Notes.Add("Top-plane probe: no combination cut the expected slot, so the round-bottomed cradles cannot be built.");
            }
            catch (Exception ex)
            {
                flags.Notes.Add("Top-plane probe failed (" + ex.Message + "), so the round-bottomed cradles cannot be built.");
            }
            finally
            {
                flags.TopProbing = false;
                if (doc != null)
                {
                    try { app.CloseDoc(doc.GetTitle()); } catch { }
                }
            }
        }

        private static bool TryGetZRange(ModelDoc2 doc, out double zMinMm, out double zMaxMm)
        {
            zMinMm = zMaxMm = 0;
            PartDoc part = doc as PartDoc;
            double[] box = part == null ? null : part.GetPartBox(true) as double[];
            if (box == null || box.Length < 6) return false;
            zMinMm = box[2] * 1000.0;
            zMaxMm = box[5] * 1000.0;
            return true;
        }

        private static bool Near(double a, double b) { return Math.Abs(a - b) < 0.05; }
    }
}
