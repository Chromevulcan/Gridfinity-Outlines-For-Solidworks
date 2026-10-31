using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Gridfinity.Core;

namespace Gridfinity.Sw
{
    /// <summary>
    /// The designer's Parts tab. The box is set up on the Box tab as usual; here the user imports a photo of parts on
    /// paper (or outline-app exports), and the traced parts are laid onto that box as cutouts. Each part can have a
    /// flat or round ("follows the shape") bottom and a name debossed along its centreline. The Y offset sets how high
    /// above the tray's top surface the top of every part sits; only what is below that plane is cut. Oversized trays
    /// are split into pieces that fit the chosen printer when built.
    /// </summary>
    internal sealed class PartsTab : UserControl
    {
        private readonly Func<TrayDefinition> _tray;
        private readonly Action _changed;

        private readonly ListBox _parts = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly PropertyGrid _partGrid = new PropertyGrid { Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false, HelpVisible = true };
        private readonly PropertyGrid _trayGrid = new PropertyGrid { Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false, HelpVisible = true };
        private readonly Panel _preview = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly System.Windows.Forms.Label _status = new System.Windows.Forms.Label { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(4), ForeColor = Color.FromArgb(40, 40, 40) };
        private readonly CheckBox _grow = new CheckBox { Text = "Grow the box to fit the parts", AutoSize = true, Checked = true, Margin = new Padding(8, 7, 3, 3) };
        private readonly Button _remove;
        private SplitResult _split;

        public PartsTab(Func<TrayDefinition> tray, Action changed)
        {
            _tray = tray;
            _changed = changed;
            Dock = DockStyle.Fill;

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(2), WrapContents = false };
            bar.Controls.Add(MakeButton("Import photo or outlines...", OnImport));
            _remove = MakeButton("Remove part", OnRemove);
            bar.Controls.Add(_remove);
            bar.Controls.Add(MakeButton("Lay out parts", delegate { LayOut(true); }));
            bar.Controls.Add(_grow);

            // Left: the parts and the selected part's settings. Right: the preview and the tray-wide part settings.
            var main = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 230 };
            var left = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 150 };
            var partsBox = new GroupBox { Text = "Parts on this tray", Dock = DockStyle.Fill };
            partsBox.Controls.Add(_parts);
            var partBox = new GroupBox { Text = "Selected part", Dock = DockStyle.Fill };
            partBox.Controls.Add(_partGrid);
            left.Panel1.Controls.Add(partsBox);
            left.Panel2.Controls.Add(partBox);

            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
            var previewBox = new GroupBox { Text = "Top view, to scale (dashed red: where it is split for the printer)", Dock = DockStyle.Fill };
            previewBox.Controls.Add(_preview);
            var trayBox = new GroupBox { Text = "Y offset, round bottoms and printer", Dock = DockStyle.Fill };
            trayBox.Controls.Add(_trayGrid);
            right.Panel1.Controls.Add(previewBox);
            right.Panel2.Controls.Add(trayBox);

            main.Panel1.Controls.Add(left);
            main.Panel2.Controls.Add(right);

            Controls.Add(main);
            Controls.Add(_status);
            Controls.Add(bar);

            _parts.SelectedIndexChanged += delegate { ShowSelected(); };
            _partGrid.PropertyValueChanged += delegate { _parts.Invalidate(); Changed(); };
            _trayGrid.PropertyValueChanged += delegate { Changed(); };
            _preview.Paint += (s, e) => PreviewPainter.Paint(e.Graphics, _preview, _tray(), _split, _parts.SelectedItem as Pocket,
                                                              "Set up the box on the Box tab, then import a photo of parts.");
            _parts.Format += (s, e) =>
            {
                var p = (Pocket)e.ListItem;
                e.Value = p.Name + (p.Bottom == PartBottom.Round && PartCuts.IsPart(p) ? "  (round)" : "") + (p.HasLabel ? "  [" + p.LabelText + "]" : "");
            };
            _parts.FormattingEnabled = true;
        }

        private static Button MakeButton(string text, EventHandler handler)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += handler;
            return b;
        }

        /// <summary>Rebinds to the current tray (after Open JSON or switching tabs).</summary>
        public void Rebind()
        {
            TrayDefinition t = _tray();
            object keep = _parts.SelectedItem;
            _parts.BeginUpdate();
            _parts.Items.Clear();
            foreach (var p in t.Pockets) _parts.Items.Add(p);
            _parts.EndUpdate();
            if (keep != null && _parts.Items.Contains(keep)) _parts.SelectedItem = keep;
            else if (_parts.Items.Count > 0) _parts.SelectedIndex = 0;
            _trayGrid.SelectedObject = new CategoryView(t, "Parts", "Printer");
            ShowSelected();
            Changed(false);
        }

        private void ShowSelected()
        {
            var p = _parts.SelectedItem as Pocket;
            _partGrid.SelectedObject = p == null ? null : new CategoryView(p, "Identity", "Part", "Depth", "Fit", "Placement", "Finger notch");
            _remove.Enabled = p != null;
            _preview.Invalidate();
        }

        private void Changed() { Changed(true); }

        private void Changed(bool notify)
        {
            TrayDefinition t = _tray();
            try
            {
                var fixedTray = t.Clone();
                fixedTray.AutoLayout = false;
                fixedTray.AutoGrowGrid = false;
                _split = TraySplitter.Split(fixedTray);
            }
            catch { _split = null; }
            _status.Text = Summary(t);
            _preview.Invalidate();
            if (notify && _changed != null) _changed();
        }

        private string Summary(TrayDefinition t)
        {
            var c = CultureInfo.InvariantCulture;
            int parts = t.Pockets.Count(PartCuts.IsPart);
            string s = string.Format(c, "{0} x {1} cells, {2} height units, {3:0.#} x {4:0.#} mm. {5} part(s), deepest cut {6:0.#} mm, Y offset {7:0.#} mm.",
                t.UnitsX, t.UnitsY, t.HeightUnits, t.OuterWidth, t.OuterDepth, parts, OutlineImporter.DeepestCut(t), t.PartTopOffset);
            if (_split != null && _split.WasSplit)
                s += string.Format(c, " Too big for the {0}: builds as {1} pieces ({2}).", t.Printer, _split.Pieces.Count,
                                   string.Join(", ", _split.Pieces.Select(p => p.UnitsX + "x" + p.UnitsY)));
            else if (_split != null) s += " Fits the " + t.Printer + " in one piece.";
            return s;
        }

        // ---- Buttons

        private void OnImport(object sender, EventArgs e)
        {
            List<Pocket> pockets;
            OutlineImportOptions o;
            if (!OutlineImportForm.ImportPockets(FindForm(), out pockets, out o)) return;

            TrayDefinition t = _tray();
            // The untouched starter pocket is only there as an example; drop it.
            if (t.Pockets.Count == 1 && t.Pockets[0].Name == "Example pocket") t.Pockets.Clear();
            // The first parts on a tray bring their Y offset and printer; later imports keep what the user set here.
            if (!t.Pockets.Any(PartCuts.IsPart))
            {
                t.PartTopOffset = o.TopOffset;
                t.Printer = o.Printer;
            }
            t.Pockets.AddRange(pockets);
            t.MaxUnitsX = Math.Max(t.MaxUnitsX, o.MaxUnitsX);
            t.MaxUnitsY = Math.Max(t.MaxUnitsY, o.MaxUnitsY);
            Rebind();
            if (pockets.Count > 0) _parts.SelectedItem = pockets[0];
            LayOut(false);
        }

        private void OnRemove(object sender, EventArgs e)
        {
            var p = _parts.SelectedItem as Pocket;
            if (p == null) return;
            _tray().Pockets.Remove(p);
            Rebind();
        }

        /// <summary>Places the parts on the tray built on the Box tab, growing it if allowed, and raises its height to hold them.</summary>
        private void LayOut(bool quietIfFits)
        {
            TrayDefinition t = _tray();
            t.AutoLayout = true;
            LayoutResult r = _grow.Checked
                ? (TrayPlanner.SuggestGrid(t) ? TrayPlanner.AutoLayout(t) : new LayoutResult { Unplaced = t.Pockets.ToList() })
                : TrayPlanner.AutoLayout(t);
            int oldHeight = t.HeightUnits;
            OutlineImporter.FitHeight(t, true);
            Changed();

            var notes = new List<string>();
            if (t.HeightUnits != oldHeight) notes.Add("Raised the box to " + t.HeightUnits + " height units to hold the deepest cut.");
            if (!r.Fits)
                notes.Add(r.Unplaced.Count + " part(s) did not fit, e.g. '" + (r.Unplaced.Count > 0 ? r.Unplaced[0].Name : "?") + "'. " +
                          (_grow.Checked ? "Raise MaxUnitsX/MaxUnitsY on the Cutout tab." : "Tick \"Grow the box to fit the parts\" or enlarge the grid on the Box tab."));
            if (_split != null && _split.WasSplit)
                notes.Add("This tray is larger than the " + t.Printer + " bed, so Build makes " + _split.Pieces.Count + " pieces that print separately.");
            if (notes.Count > 0 && (!quietIfFits || !r.Fits))
                MessageBox.Show(this, string.Join("\n\n", notes), "Parts", MessageBoxButtons.OK, r.Fits ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
