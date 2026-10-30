namespace Gridfinity.Core
{
    /// <summary>
    /// Gridfinity constants, all in millimetres. Values follow the public Gridfinity
    /// specification (Zack Freedman). Change them here if you want a variant system.
    /// </summary>
    public static class GridfinitySpec
    {
        /// <summary>Grid pitch: one unit is 42 x 42 mm.</summary>
        public const double Pitch = 42.0;

        /// <summary>One height unit is 7 mm.</summary>
        public const double HeightUnit = 7.0;

        /// <summary>Total clearance removed from a bin versus the grid (0.25 mm per side).</summary>
        public const double FitClearance = 0.5;

        /// <summary>Outer size of one bin cell at the top of its base: 41.5 mm.</summary>
        public const double CellSize = Pitch - FitClearance;

        // Base profile, bottom to top: 45 deg chamfer, vertical, 45 deg chamfer.
        public const double BaseChamferLower = 0.8;
        public const double BaseVertical = 1.8;
        public const double BaseChamferUpper = 2.15;
        public const double BaseHeight = BaseChamferLower + BaseVertical + BaseChamferUpper; // 4.75
        public const double BaseBottomCornerRadius = 0.8;

        /// <summary>Outer corner radius of a bin and of the top of each base cell.</summary>
        public const double BinCornerRadius = 3.75;

        // Stacking lip, bottom to top: 45 deg chamfer, vertical, 45 deg chamfer.
        public const double LipLowerChamfer = 0.7;
        public const double LipVertical = 1.8;
        public const double LipUpperChamfer = 1.9;
        public const double LipHeight = LipLowerChamfer + LipVertical + LipUpperChamfer; // 4.4

        /// <summary>Distance from the bin's outer wall to the lip's lowest inner edge.</summary>
        public const double LipBottomInset = LipLowerChamfer + LipUpperChamfer; // 2.6

        /// <summary>Distance from the bin's outer wall to the lip's vertical section.</summary>
        public const double LipMidInset = LipUpperChamfer; // 1.9

        // Magnet pockets in the base.
        public const double MagnetDiameter = 6.5;
        public const double MagnetDepth = 2.4;
        public const double MagnetOffset = 13.0; // from cell centre, on both axes

        /// <summary>Outer size of a bin that spans <paramref name="units"/> grid cells.</summary>
        public static double BinSize(int units)
        {
            return units * Pitch - FitClearance;
        }
    }
}
