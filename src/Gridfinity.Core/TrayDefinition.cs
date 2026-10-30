using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Gridfinity.Core
{
    public enum TrayStyle
    {
        /// <summary>Solid block to full height; pockets are cut down from the top face.</summary>
        SolidBlock,
        /// <summary>Walled bin with an open cavity (optional dividers and stacking lip); pockets are cut into the floor.</summary>
        OpenTray
    }

    /// <summary>Everything needed to describe a tray or part holder. Serialises to/from JSON.</summary>
    public class TrayDefinition
    {
        // ---- Identity / grid ------------------------------------------------------------------
        [Category("Grid"), Description("Part name, also used as the file name stem.")]
        public string Name { get; set; } = "GridfinityTray";

        [Category("Grid"), Description("Number of 42 mm cells along X.")]
        public int UnitsX { get; set; } = 2;

        [Category("Grid"), Description("Number of 42 mm cells along Y.")]
        public int UnitsY { get; set; } = 2;

        [Category("Grid"), Description("Height in 7 mm units, measured from the bottom to the top of the walls (stacking lip is extra).")]
        public int HeightUnits { get; set; } = 3;

        // ---- Style ----------------------------------------------------------------------------
        [Category("Style")]
        public TrayStyle Style { get; set; } = TrayStyle.SolidBlock;

        [Category("Style"), Description("Cut 6.5 x 2.4 mm magnet pockets in each cell's four corners.")]
        public bool MagnetHoles { get; set; }

        [Category("Style"), Description("OpenTray only: add the 4.4 mm stacking lip on top of the walls.")]
        public bool StackingLip { get; set; }

        [Category("Style"), Description("OpenTray + lip: wall thickness left at the very top of the lip, mm (0.4 to 1.9).")]
        public double LipTopInset { get; set; } = 0.8;

        // ---- Open tray ------------------------------------------------------------------------
        [Category("Open tray"), Description("Outer wall thickness, mm.")]
        public double WallThickness { get; set; } = 1.2;

        [Category("Open tray"), Description("Height of the cavity floor above the bottom of the part, mm. Default is the top of the base profile.")]
        public double FloorHeight { get; set; } = GridfinitySpec.BaseHeight;

        [Category("Open tray"), Description("Compartments along X (dividers between them).")]
        public int CompartmentsX { get; set; } = 1;

        [Category("Open tray"), Description("Compartments along Y.")]
        public int CompartmentsY { get; set; } = 1;

        [Category("Open tray"), Description("Divider thickness, mm.")]
        public double DividerThickness { get; set; } = 1.2;

        // ---- Layout ---------------------------------------------------------------------------
        [Category("Layout"), Description("Automatically arrange pockets (overwrites their X, Y and rotation).")]
        public bool AutoLayout { get; set; } = true;

        [Category("Layout"), Description("Increase UnitsX / UnitsY to the smallest grid in which every pocket fits (implies AutoLayout).")]
        public bool AutoGrowGrid { get; set; }

        [Category("Layout"), Description("AutoGrowGrid never goes wider than this many cells along X. 5 cells (209.5 mm) fits most 220 mm+ print beds; 6 fits 256 mm beds.")]
        public int MaxUnitsX { get; set; } = 12;

        [Category("Layout"), Description("AutoGrowGrid never goes deeper than this many cells along Y.")]
        public int MaxUnitsY { get; set; } = 12;

        [Category("Layout"), Description("Total clearance added to every pocket unless the pocket sets its own, mm.")]
        public double DefaultClearance { get; set; } = 0.4;

        [Category("Layout"), Description("Free space kept between pockets and the part edge / inner wall, mm.")]
        public double EdgeMargin { get; set; } = 1.6;

        [Category("Layout"), Description("Gap between neighbouring pockets used by AutoLayout, mm.")]
        public double PocketSpacing { get; set; } = 2.0;

        [Category("Layout"), Description("Minimum material left under the deepest point of a pocket, mm.")]
        public double MinFloorBelowPocket { get; set; } = 1.0;

        // ---- Pockets --------------------------------------------------------------------------
        [Category("Pockets")]
        public List<Pocket> Pockets { get; set; } = new List<Pocket>();

        // ---- Labels ---------------------------------------------------------------------------
        [Category("Labels"), Description("Text debossed into the top surface. Positions are not changed by AutoLayout.")]
        public List<Label> Labels { get; set; } = new List<Label>();

        [Category("Labels"), Description("How deep the text is cut, mm. 0.8 mm is four layers at 0.2 mm, handy for a colour change or paint fill.")]
        public double LabelDepth { get; set; } = 0.8;

        [Category("Labels"), Description("Font name for the labels. Empty uses the SolidWorks default.")]
        public string LabelFont { get; set; } = "Arial";

        [Category("Labels"), Description("Bold labels give thicker strokes that print and fill better.")]
        public bool LabelBold { get; set; } = true;

        // ---- Derived (read-only) --------------------------------------------------------------
        [JsonIgnore, Category("Derived")]
        public double OuterWidth { get { return GridfinitySpec.BinSize(UnitsX); } }

        [JsonIgnore, Category("Derived")]
        public double OuterDepth { get { return GridfinitySpec.BinSize(UnitsY); } }

        /// <summary>Z of the top of the walls (not counting the lip).</summary>
        [JsonIgnore, Category("Derived")]
        public double BodyTop { get { return HeightUnits * GridfinitySpec.HeightUnit; } }

        [JsonIgnore, Category("Derived")]
        public bool HasLip { get { return Style == TrayStyle.OpenTray && StackingLip; } }

        [JsonIgnore, Category("Derived")]
        public double TotalHeight { get { return BodyTop + (HasLip ? GridfinitySpec.LipHeight : 0); } }

        /// <summary>Z that pockets are measured down from: the block top, or the cavity floor.</summary>
        [JsonIgnore, Category("Derived")]
        public double PocketTopZ { get { return Style == TrayStyle.SolidBlock ? BodyTop : FloorHeight; } }

        /// <summary>Half-extents of the area pockets may occupy, keeping <paramref name="margin"/> from the edge or wall.</summary>
        public void GetPocketRegion(int unitsX, int unitsY, double margin, out double halfWidth, out double halfDepth)
        {
            double inset = margin + (Style == TrayStyle.OpenTray ? WallThickness : 0);
            halfWidth = GridfinitySpec.BinSize(unitsX) / 2 - inset;
            halfDepth = GridfinitySpec.BinSize(unitsY) / 2 - inset;
        }

        // ---- JSON -----------------------------------------------------------------------------
        private static JsonSerializerSettings Settings()
        {
            var s = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };
            s.Converters.Add(new StringEnumConverter());
            return s;
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Settings()); }

        public static TrayDefinition FromJson(string json)
        {
            return JsonConvert.DeserializeObject<TrayDefinition>(json, Settings());
        }

        /// <summary>Deep copy (via JSON), so planning never mutates the caller's object.</summary>
        public TrayDefinition Clone() { return FromJson(ToJson()); }
    }
}
