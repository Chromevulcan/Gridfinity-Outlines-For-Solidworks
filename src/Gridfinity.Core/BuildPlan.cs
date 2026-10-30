using System.Collections.Generic;

namespace Gridfinity.Core
{
    public enum FeatureOp { Boss, Cut }

    /// <summary>
    /// Which plane a feature's sketch lives on. Front: loops are (X, Y), the feature spans Z = StartZ .. StartZ + Depth.
    /// Top: loops are (X, Z) and the feature runs along Y, from |Y| = StartZ to |Y| = StartZ + Depth, on the
    /// negative-Y side when <see cref="FeaturePlan.NegativeSide"/> is set (used for round-bottomed cradles).
    /// </summary>
    public enum SketchPlane { Front, Top }

    /// <summary>
    /// One extrude or cut. All sketches live on a plane at Z = 0; the feature spans
    /// Z = StartZ .. StartZ + Depth. Coordinates are millimetres with the bin centred on X/Y = 0.
    /// </summary>
    public sealed class FeaturePlan
    {
        public string Name;
        public FeatureOp Op;
        public double StartZ;
        public double Depth;

        /// <summary>Draft angle in degrees. Positive means the section grows as Z increases.</summary>
        public double DraftDegrees;

        public SketchPlane Plane = SketchPlane.Front;

        /// <summary>Top-plane features only: the cut lies at negative Y (|Y| = StartZ .. StartZ + Depth).</summary>
        public bool NegativeSide;

        public List<Loop> Loops = new List<Loop>();

        /// <summary>Text to cut into the sketch alongside any loops (labels).</summary>
        public List<TextPlan> Texts = new List<TextPlan>();

        /// <summary>
        /// When true, a failure building this feature is reported as a warning and the build carries on.
        /// Used for labels, so a text problem never costs you the whole tray.
        /// </summary>
        public bool Optional;
    }

    /// <summary>One line of sketch text. X is the left end of the text, Y the vertical centre of the capitals.</summary>
    public sealed class TextPlan
    {
        public string Text;
        public double X;
        public double Y;
        public double HeightMm;
        public string Font;
        public bool Bold;
    }

    /// <summary>
    /// CAD-independent recipe for a tray: an ordered list of features. The SolidWorks layer only
    /// replays this list, so all dimensional logic lives (and can be unit tested) in this assembly.
    /// </summary>
    public sealed class BuildPlan
    {
        public string Name;
        public List<FeaturePlan> Features = new List<FeaturePlan>();
        public Dictionary<string, string> Properties = new Dictionary<string, string>();

        public double ExpectedWidth;
        public double ExpectedDepth;
        public double ExpectedHeight;
    }
}
