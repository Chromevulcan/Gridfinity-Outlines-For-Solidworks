using System;
using System.ComponentModel;
using Newtonsoft.Json;

namespace Gridfinity.Core
{
    /// <summary>
    /// A line of text debossed (cut into) the top surface of the tray, so it can be filled with paint
    /// or printed in a second colour.
    /// </summary>
    public class Label
    {
        [Category("Text")]
        public string Text { get; set; } = "Label";

        [Category("Placement"), Description("Left end of the text, relative to the bin centre, mm.")]
        public double X { get; set; }

        [Category("Placement"), Description("Vertical centre of the text, relative to the bin centre, mm.")]
        public double Y { get; set; }

        [Category("Text"), Description("Character height, mm. About 6 mm gives strokes wide enough for a 0.4 mm nozzle.")]
        public double HeightMm { get; set; } = 6;

        public override string ToString() { return Text; }

        /// <summary>
        /// Deliberately generous estimate of the text width, used only for fit checks and layout.
        /// Bold capitals in a typical sans-serif font are about 0.7 x the character height wide.
        /// </summary>
        [JsonIgnore, Browsable(false)]
        public double EstimatedWidth { get { return (Text ?? "").Length * HeightMm * 0.7; } }

        public void GetEstimatedBounds(out Vec2 min, out Vec2 max)
        {
            min = new Vec2(X, Y - HeightMm / 2);
            max = new Vec2(X + EstimatedWidth, Y + HeightMm / 2);
        }

        public static Label At(string text, double x, double y, double heightMm = 6)
        {
            return new Label { Text = text, X = x, Y = y, HeightMm = heightMm };
        }
    }
}
