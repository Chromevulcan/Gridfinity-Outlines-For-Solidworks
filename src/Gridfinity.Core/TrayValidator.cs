using System.Globalization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Gridfinity.Core
{
    public enum Severity { Warning, Error }

    public sealed class Issue
    {
        public Severity Severity;
        public string Message;
        public override string ToString() { return Severity + ": " + Message; }
    }

    public sealed class ValidationReport
    {
        public List<Issue> Issues = new List<Issue>();

        public bool IsValid { get { return Issues.All(i => i.Severity != Severity.Error); } }
        public IEnumerable<Issue> Errors { get { return Issues.Where(i => i.Severity == Severity.Error); } }
        public IEnumerable<Issue> Warnings { get { return Issues.Where(i => i.Severity == Severity.Warning); } }

        public void Error(string message) { Issues.Add(new Issue { Severity = Severity.Error, Message = message }); }
        public void Warn(string message) { Issues.Add(new Issue { Severity = Severity.Warning, Message = message }); }

        public override string ToString()
        {
            if (Issues.Count == 0) return "No issues.";
            var sb = new StringBuilder();
            foreach (var i in Issues.OrderByDescending(i => i.Severity)) sb.AppendLine(i.ToString());
            return sb.ToString().TrimEnd();
        }
    }

    public static class TrayValidator
    {
        private const double MinWeb = 0.8; // thinnest wall/web we allow between features, mm

        public static ValidationReport Validate(TrayDefinition t)
        {
            var r = new ValidationReport();

            if (string.IsNullOrWhiteSpace(t.Name)) r.Error("Name is empty.");
            if (t.UnitsX < 1 || t.UnitsX > 20) r.Error("UnitsX must be 1 to 20.");
            if (t.UnitsY < 1 || t.UnitsY > 20) r.Error("UnitsY must be 1 to 20.");
            if (t.HeightUnits < 1 || t.HeightUnits > 30) r.Error("HeightUnits must be 1 to 30.");
            if (t.DefaultClearance < 0) r.Error("DefaultClearance cannot be negative.");
            if (t.PrintAllowance < 0 || t.PrintAllowance > 2) r.Error("PrintAllowance must be 0 to 2 mm per side.");
            if (!r.IsValid) return r; // later checks assume sane sizes

            ValidateFit(t, r);
            ValidateStyle(t, r);
            ValidatePockets(t, r);
            return r;
        }

        // Printed cutouts come out smaller than drawn; a gap below MinFitPerSide is tighter than most FDM printers hold.
        private static void ValidateFit(TrayDefinition t, ValidationReport r)
        {
            double clr = t.GeometryClearance;
            var tight = t.Pockets.Where(p => p.Fit(clr) / 2 < TrayDefinition.MinFitPerSide - 1e-9).ToList();
            if (tight.Count == 0) return;
            r.Warn(string.Format(CultureInfo.InvariantCulture,
                "{0} pocket(s) (e.g. '{1}') leave only {2:0.##} mm per side between the part and the cutout (clearance {3:0.##} / 2 + PrintAllowance {4:0.##}). " +
                "Most FDM printers need at least {5:0.##} mm per side for a part to drop in; raise PrintAllowance (Printer) or the clearance.",
                tight.Count, tight[0].Name, tight[0].Fit(clr) / 2, tight[0].Clearance ?? t.DefaultClearance, t.PrintAllowance, TrayDefinition.MinFitPerSide));
        }

        private static void ValidateStyle(TrayDefinition t, ValidationReport r)
        {
            if (t.Style == TrayStyle.SolidBlock)
            {
                if (t.StackingLip) r.Warn("StackingLip is ignored for SolidBlock; use OpenTray for a lip.");
                if (t.Pockets.Count == 0) r.Warn("SolidBlock with no pockets is just a solid block.");
                return;
            }

            if (t.WallThickness < MinWeb) r.Error("WallThickness is below " + MinWeb + " mm.");
            if (t.CompartmentsX < 1 || t.CompartmentsY < 1) r.Error("Compartment counts must be at least 1.");
            if (t.CompartmentsX > 1 || t.CompartmentsY > 1)
                if (t.DividerThickness < MinWeb) r.Error("DividerThickness is below " + MinWeb + " mm.");

            double minFloor = t.MagnetHoles ? GridfinitySpec.MagnetDepth + MinWeb : MinWeb;
            if (t.FloorHeight < minFloor)
                r.Error("FloorHeight " + t.FloorHeight + " mm leaves too little material" + (t.MagnetHoles ? " above the magnet pockets (need at least " + minFloor.ToString("0.##") + " mm)." : "."));
            if (t.FloorHeight > t.BodyTop - 3.0) r.Error("FloorHeight leaves less than 3 mm of wall above it; raise HeightUnits or lower FloorHeight.");

            if (t.WallThickness > GridfinitySpec.LipBottomInset && t.StackingLip)
                r.Warn("WallThickness exceeds the 2.6 mm lip inset, so the lip will not be visible as a ledge.");
            if (t.StackingLip && (t.LipTopInset < 0.4 || t.LipTopInset > GridfinitySpec.LipUpperChamfer))
                r.Warn("LipTopInset is outside 0.4 to 1.9 mm and will be clamped.");

            double innerW = t.OuterWidth - 2 * t.WallThickness;
            double innerD = t.OuterDepth - 2 * t.WallThickness;
            double cw = (innerW - (t.CompartmentsX - 1) * t.DividerThickness) / Math.Max(1, t.CompartmentsX);
            double cd = (innerD - (t.CompartmentsY - 1) * t.DividerThickness) / Math.Max(1, t.CompartmentsY);
            if (cw < 5 || cd < 5) r.Error("Compartments would be smaller than 5 mm; use fewer compartments or a larger grid.");
        }

        private static void ValidatePockets(TrayDefinition t, ValidationReport r)
        {
            double halfW, halfD;
            t.GetPocketRegion(t.UnitsX, t.UnitsY, MinWeb, out halfW, out halfD);

            var polys = new List<List<Vec2>>();
            var boxes = new List<Tuple<Vec2, Vec2>>();

            foreach (var p in t.Pockets)
            {
                string who = "Pocket '" + p.Name + "': ";
                bool geometryOk = true;

                if (p.Shape == PocketShape.Outline)
                {
                    if (p.Points == null || p.Points.Count < 3) { r.Error(who + "Outline needs at least 3 Points."); geometryOk = false; }
                    else if (OutlineGeometry.Clean(p.Points) == null) { r.Error(who + "the outline has no area."); geometryOk = false; }
                    else if (p.Points.Count > 2000)
                        r.Warn(who + "the outline has " + p.Points.Count + " points; SolidWorks will be slow. Re-import with a larger SimplifyTolerance.");
                    if (p.KeepIslands && p.Holes != null && p.Holes.Count > 0 && geometryOk && p.BuildIslandLoops(t.GeometryClearance).Count == 0)
                        r.Warn(who + "KeepIslands is on but every hole is too small to leave an island (under " + Pocket.MinIslandSize + " mm).");
                }
                else if (p.Width <= 0) { r.Error(who + "Width must be positive."); geometryOk = false; }
                if ((p.Shape == PocketShape.Rectangle || p.Shape == PocketShape.Slot || p.Shape == PocketShape.Cradle) && p.Height <= 0)
                { r.Error(who + "Height must be positive."); geometryOk = false; }
                if (p.Shape == PocketShape.Polygon && (p.Points == null || p.Points.Count < 3))
                { r.Error(who + "Polygon needs at least 3 Points."); geometryOk = false; }
                bool round = p.Shape == PocketShape.Outline && p.Bottom == PartBottom.Round;
                if (p.Depth <= 0 && !round) r.Error(who + "Depth must be positive.");
                if (round && t.RoundStep < 0.1) r.Warn("RoundStep under 0.1 mm makes a great many cut features.");

                if (p.Shape == PocketShape.Cradle)
                {
                    double cr = (p.Width + (p.Fit(t.GeometryClearance))) / 2;
                    if (Math.Abs(p.RotationDeg) > 1e-9) r.Error(who + "a Cradle cannot be rotated; swap Width and Height instead.");
                    if (p.Depth < cr) r.Error(who + "depth " + p.Depth + " mm is less than the cradle radius " + cr.ToString("0.##") + " mm, so the round bottom would not fit.");
                    if (p.NotchDiameter > 0) r.Warn(who + "finger notches are ignored on a Cradle.");
                }

                double cut = geometryOk ? PartCuts.MaxCutDepth(p, t) : p.Depth;
                if (PartCuts.IsPart(p) && geometryOk && cut <= 1e-6)
                    r.Warn(who + "sits entirely above the tray surface (Y offset " + PartCuts.TopOffsetFor(p, t).ToString("0.##") +
                           " mm" + (round ? "" : ", thickness " + p.Depth.ToString("0.##") + " mm") + "), so nothing is cut.");
                double bottom = t.PocketTopZ - cut;
                // In a solid block the Gridfinity feet narrow below Z = 4.75 mm, so a deeper pocket would cut through the sides.
                double minBottom = t.Style == TrayStyle.SolidBlock
                    ? Math.Max(t.MinFloorBelowPocket, GridfinitySpec.BaseHeight + t.MinFloorBelowPocket)
                    : t.MinFloorBelowPocket;
                if (bottom < minBottom)
                    r.Error(who + "depth " + cut.ToString("0.##") + " mm reaches " + bottom.ToString("0.##") + " mm above the tray base; the lowest allowed is " + minBottom.ToString("0.##") + " mm" + (t.Style == TrayStyle.SolidBlock ? " (the " + GridfinitySpec.BaseHeight + " mm base feet plus " + t.MinFloorBelowPocket + " mm of floor)." : "."));
                else if (t.MagnetHoles && bottom < GridfinitySpec.MagnetDepth + MinWeb)
                    r.Warn(who + "may break into the magnet pockets underneath.");

                if (p.NotchDiameter < 0) r.Error(who + "NotchDiameter cannot be negative.");

                if (!geometryOk)
                {
                    polys.Add(null);
                    boxes.Add(null);
                    continue;
                }

                Loop loop = p.BuildLoop(t.GeometryClearance);
                Vec2 lo, hi;
                loop.GetBounds(out lo, out hi);
                Loop notch = p.BuildNotchLoop(t.GeometryClearance);
                if (notch != null)
                {
                    Vec2 nlo, nhi;
                    notch.GetBounds(out nlo, out nhi);
                    lo = new Vec2(Math.Min(lo.X, nlo.X), Math.Min(lo.Y, nlo.Y));
                    hi = new Vec2(Math.Max(hi.X, nhi.X), Math.Max(hi.Y, nhi.Y));
                }

                // A split piece's parts may run past its edges into the next piece.
                if (t.Piece == null && (lo.X < -halfW - 1e-6 || hi.X > halfW + 1e-6 || lo.Y < -halfD - 1e-6 || hi.Y > halfD + 1e-6))
                    r.Error(who + "extends beyond the usable area (X " + lo.X.ToString("0.#") + " to " + hi.X.ToString("0.#") +
                            ", Y " + lo.Y.ToString("0.#") + " to " + hi.Y.ToString("0.#") + "; limit +/-" + halfW.ToString("0.#") + " x +/-" + halfD.ToString("0.#") + ").");

                polys.Add(loop.ToPolyline());
                boxes.Add(Tuple.Create(lo, hi));
            }

            for (int i = 0; i < polys.Count; i++)
            {
                if (polys[i] == null) continue;
                for (int j = i + 1; j < polys.Count; j++)
                {
                    if (polys[j] == null) continue;

                    // Cheap reject: bounding boxes further apart than the web we care about.
                    double gapX = Math.Max(boxes[j].Item1.X - boxes[i].Item2.X, boxes[i].Item1.X - boxes[j].Item2.X);
                    double gapY = Math.Max(boxes[j].Item1.Y - boxes[i].Item2.Y, boxes[i].Item1.Y - boxes[j].Item2.Y);
                    if (Math.Max(gapX, gapY) > MinWeb) continue;

                    double d = GeometryUtil.MinDistance(polys[i], polys[j]);
                    if (d <= 1e-6)
                        r.Error("Pockets '" + t.Pockets[i].Name + "' and '" + t.Pockets[j].Name + "' overlap.");
                    else if (d < MinWeb)
                        r.Warn("Pockets '" + t.Pockets[i].Name + "' and '" + t.Pockets[j].Name + "' are only " + d.ToString("0.##") + " mm apart.");
                }
            }

            ValidateLabels(t, r, boxes, halfW, halfD);
            ValidatePartLabels(t, r, halfW, halfD);
            if (t.Piece != null)
                foreach (var p in t.Pockets.Where(q => q.Shape == PocketShape.Cradle))
                {
                    Vec2 lo, hi;
                    p.BuildLoop(t.GeometryClearance).GetBounds(out lo, out hi);
                    if (lo.X < -halfW || hi.X > halfW || lo.Y < -halfD || hi.Y > halfD)
                        r.Error("Cradle '" + p.Name + "' crosses a seam of the split tray; cradles cannot be split.");
                }
        }

        private static void ValidatePartLabels(TrayDefinition t, ValidationReport r, double halfW, double halfD)
        {
            var named = t.Pockets.Where(p => p.HasLabel).ToList();
            if (named.Count == 0) return;
            if (t.LabelDepth <= 0) { r.Error("LabelDepth must be positive."); return; }
            if (t.PocketTopZ - t.LabelDepth < t.MinFloorBelowPocket)
                r.Error("LabelDepth " + t.LabelDepth + " mm leaves too little material under the part names.");
            foreach (var p in named)
            {
                string who = "Name '" + p.LabelText + "' of '" + p.Name + "': ";
                if (p.LabelHeight < 3) r.Warn(who + "text under 3 mm high is hard to print.");
                Vec2 a, b;
                if (!p.GetLabelLine(t.GeometryClearance, out a, out b)) continue;
                double len = (b - a).Length, width = TrayModeler.EstimateTextWidth(p.LabelText.Trim(), p.LabelHeight);
                if (width > len) r.Warn(who + "is longer than the part (about " + width.ToString("0") + " mm against " + len.ToString("0") + " mm), so it is made smaller.");
                if (t.Piece == null)
                {
                    var band = p.BuildLabelBand(t.GeometryClearance);
                    if (band != null && band.Any(q => q.X < -halfW || q.X > halfW || q.Y < -halfD || q.Y > halfD))
                        r.Warn(who + "may run past the edge of the usable area.");
                }
            }
        }

        // Label widths are estimates, so problems here are warnings, not errors.
        private static void ValidateLabels(TrayDefinition t, ValidationReport r, List<Tuple<Vec2, Vec2>> pocketBoxes, double halfW, double halfD)
        {
            if (t.Labels.Count == 0) return;

            if (t.LabelDepth <= 0) r.Error("LabelDepth must be positive.");
            else if (t.PocketTopZ - t.LabelDepth < t.MinFloorBelowPocket)
                r.Error("LabelDepth " + t.LabelDepth + " mm leaves too little material under the labels.");

            foreach (var l in t.Labels)
            {
                string who = "Label '" + l.Text + "': ";
                if (string.IsNullOrWhiteSpace(l.Text)) { r.Error("A label has no text."); continue; }
                if (l.HeightMm <= 0) { r.Error(who + "HeightMm must be positive."); continue; }
                if (l.HeightMm < 3) r.Warn(who + "text under 3 mm high is hard to print.");

                Vec2 lo, hi;
                l.GetEstimatedBounds(out lo, out hi);

                if (lo.X < -halfW || hi.X > halfW || lo.Y < -halfD || hi.Y > halfD)
                    r.Warn(who + "may run past the edge of the usable area (estimated width " + l.EstimatedWidth.ToString("0.#") + " mm).");

                foreach (var box in pocketBoxes)
                {
                    if (box == null) continue;
                    bool overlapX = lo.X < box.Item2.X && hi.X > box.Item1.X;
                    bool overlapY = lo.Y < box.Item2.Y && hi.Y > box.Item1.Y;
                    if (overlapX && overlapY)
                    {
                        r.Warn(who + "may overlap a pocket, so part of the text could be missing.");
                        break;
                    }
                }
            }
        }
    }
}
