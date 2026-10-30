using System;
using System.Collections.Generic;
using System.Linq;

namespace Gridfinity.Core
{
    public sealed class LayoutResult
    {
        public bool Fits;
        public List<Pocket> Unplaced = new List<Pocket>();
    }

    /// <summary>Shelf-packs pockets into the usable area and can grow the grid until everything fits.</summary>
    public static class TrayPlanner
    {
        private sealed class Item
        {
            public Pocket Pocket;
            public double Rotation;
            public double W, H;       // bounding box size at that rotation
            public double MinX, MinY; // bounding box corner relative to the pocket centre
        }

        private sealed class Placement
        {
            public Pocket Pocket;
            public double X, Y, Rotation;
        }

        /// <summary>Arranges pockets in the current grid size. Pockets that do not fit are returned in Unplaced.</summary>
        public static LayoutResult AutoLayout(TrayDefinition t)
        {
            var result = new LayoutResult();
            foreach (var pl in Pack(t, t.UnitsX, t.UnitsY, result.Unplaced))
            {
                pl.Pocket.X = pl.X;
                pl.Pocket.Y = pl.Y;
                pl.Pocket.RotationDeg = pl.Rotation;
            }
            result.Fits = result.Unplaced.Count == 0;
            return result;
        }

        /// <summary>
        /// Finds the smallest grid (by cell count, then closest to square) in which all pockets fit, applies it
        /// and lays the pockets out. Returns false, leaving the grid unchanged, if nothing up to maxUnits works.
        /// </summary>
        public static bool SuggestGrid(TrayDefinition t, int maxUnits = 12)
        {
            int maxX = Math.Max(1, Math.Min(maxUnits, t.MaxUnitsX));
            int maxY = Math.Max(1, Math.Min(maxUnits, t.MaxUnitsY));
            var candidates = new List<Tuple<int, int>>();
            for (int x = 1; x <= maxX; x++)
                for (int y = 1; y <= maxY; y++)
                    candidates.Add(Tuple.Create(x, y));

            foreach (var c in candidates.OrderBy(k => k.Item1 * k.Item2).ThenBy(k => Math.Abs(k.Item1 - k.Item2)))
            {
                var unplaced = new List<Pocket>();
                Pack(t, c.Item1, c.Item2, unplaced);
                if (unplaced.Count > 0) continue;

                t.UnitsX = c.Item1;
                t.UnitsY = c.Item2;
                AutoLayout(t);
                return true;
            }
            return false;
        }

        private static List<Placement> Pack(TrayDefinition t, int unitsX, int unitsY, List<Pocket> unplaced)
        {
            double halfW, halfD;
            t.GetPocketRegion(unitsX, unitsY, t.EdgeMargin, out halfW, out halfD);
            double spacing = t.PocketSpacing;
            var placements = new List<Placement>();

            if (halfW <= 0 || halfD <= 0)
            {
                unplaced.AddRange(t.Pockets);
                return placements;
            }

            double maxWidth = 2 * halfW;
            var items = t.Pockets.Select(p => Orient(t, p, maxWidth))
                                 .OrderByDescending(i => i.H).ThenByDescending(i => i.W).ToList();

            // Fill rows left to right.
            var rows = new List<List<Item>>();
            var current = new List<Item>();
            double currentWidth = 0;
            foreach (var item in items)
            {
                if (item.W > 2 * halfW + 1e-9) { unplaced.Add(item.Pocket); continue; }

                double need = current.Count == 0 ? item.W : spacing + item.W;
                if (current.Count > 0 && currentWidth + need > 2 * halfW + 1e-9)
                {
                    rows.Add(current);
                    current = new List<Item>();
                    currentWidth = 0;
                    need = item.W;
                }
                current.Add(item);
                currentWidth += need;
            }
            if (current.Count > 0) rows.Add(current);

            // Stack rows bottom to top while they fit.
            var placedRows = new List<List<Item>>();
            double usedHeight = 0;
            foreach (var row in rows)
            {
                double rowH = row.Max(i => i.H);
                double next = usedHeight + (placedRows.Count == 0 ? 0 : spacing) + rowH;
                if (next > 2 * halfD + 1e-9)
                {
                    unplaced.AddRange(row.Select(i => i.Pocket));
                    continue;
                }
                placedRows.Add(row);
                usedHeight = next;
            }

            // Centre the block, and each row inside it.
            double y = -usedHeight / 2;
            foreach (var row in placedRows)
            {
                double rowH = row.Max(i => i.H);
                double rowW = row.Sum(i => i.W) + spacing * (row.Count - 1);
                double x = -rowW / 2;
                foreach (var item in row)
                {
                    double bottom = y + (rowH - item.H) / 2;
                    placements.Add(new Placement
                    {
                        Pocket = item.Pocket,
                        Rotation = item.Rotation,
                        X = x - item.MinX,
                        Y = bottom - item.MinY
                    });
                    x += item.W + spacing;
                }
                y += rowH + spacing;
            }
            return placements;
        }

        // Picks the orientation that fits the row width; if both fit, the shorter one, so rows stay compact.
        private static Item Orient(TrayDefinition t, Pocket p, double maxWidth)
        {
            Item a = Measure(t, p, p.RotationDeg);
            if (!p.AllowRotate) return a;

            Item b = Measure(t, p, p.RotationDeg + 90);
            bool aFits = a.W <= maxWidth + 1e-9, bFits = b.W <= maxWidth + 1e-9;
            if (aFits && !bFits) return a;
            if (bFits && !aFits) return b;
            return b.H < a.H - 1e-9 ? b : a;
        }

        private static Item Measure(TrayDefinition t, Pocket p, double rotationDeg)
        {
            Vec2 min, max;
            p.GetLocalBounds(t.DefaultClearance, rotationDeg, out min, out max);
            return new Item
            {
                Pocket = p,
                Rotation = rotationDeg,
                W = max.X - min.X,
                H = max.Y - min.Y,
                MinX = min.X,
                MinY = min.Y
            };
        }
    }
}
