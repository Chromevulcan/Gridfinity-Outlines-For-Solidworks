using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Gridfinity.Core;

namespace Gridfinity.Sw
{
    /// <summary>
    /// Picks outline-app exports (DXF, SVG or the _dxf.zip it makes for several objects), shows the import options and
    /// a to-scale preview of the laid-out tray, and hands back either a ready tray (menu command) or just the pockets
    /// (designer "Import outlines...").
    /// </summary>
    internal sealed class OutlineImportForm : Form
    {
        private readonly ListBox _files = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        private readonly PropertyGrid _grid = new PropertyGrid { Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false };
        private readonly Panel _preview = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly TextBox _notes = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly TextBox _name = new TextBox { Width = 220 };
        private readonly Button _ok;
        private readonly bool _addToDesigner;

        private TrayDefinition _resolved;
        private bool _ready;
        private List<Pocket> _pockets = new List<Pocket>();

        public OutlineImportOptions Options { get; } = new OutlineImportOptions();

        /// <summary>Remembered between uses in one SolidWorks session.</summary>
        private static OutlineImportOptions _lastOptions;
        private static string _lastFolder;

        private OutlineImportForm(bool addToDesigner, IEnumerable<string> files)
        {
            _addToDesigner = addToDesigner;
            if (_lastOptions != null) Options = Copy(_lastOptions);

            Text = addToDesigner ? "Import outlines into the tray" : "Build tray from outlines";
            StartPosition = FormStartPosition.CenterParent;
            Width = 1000;
            Height = 720;
            MinimumSize = new Size(760, 520);

            // Left: files + options. Right: preview + notes.
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 380 };
            var left = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 170 };
            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 470 };

            var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
            fileButtons.Controls.Add(MakeButton("Add files...", delegate { AddFiles(); }));
            fileButtons.Controls.Add(MakeButton("Remove", delegate { RemoveSelected(); }));
            fileButtons.Controls.Add(MakeButton("Clear", delegate { _files.Items.Clear(); Recalculate(); }));
            var fileBox = new GroupBox { Text = "outline-app exports (.dxf, .svg, _dxf.zip)", Dock = DockStyle.Fill };
            fileBox.Controls.Add(_files);
            fileBox.Controls.Add(fileButtons);
            left.Panel1.Controls.Add(fileBox);

            var optionBox = new GroupBox { Text = "Options", Dock = DockStyle.Fill };
            optionBox.Controls.Add(_grid);
            left.Panel2.Controls.Add(optionBox);
            split.Panel1.Controls.Add(left);

            var previewBox = new GroupBox { Text = "Preview (top view, to scale)", Dock = DockStyle.Fill };
            previewBox.Controls.Add(_preview);
            right.Panel1.Controls.Add(previewBox);
            var notesBox = new GroupBox { Text = "Notes", Dock = DockStyle.Fill };
            notesBox.Controls.Add(_notes);
            right.Panel2.Controls.Add(notesBox);
            split.Panel2.Controls.Add(right);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), FlowDirection = FlowDirection.LeftToRight };
            if (!addToDesigner)
            {
                bottom.Controls.Add(new System.Windows.Forms.Label { Text = "Tray name:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
                bottom.Controls.Add(_name);
                _name.TextChanged += delegate { Recalculate(); };
            }
            _ok = MakeButton(addToDesigner ? "Add pockets" : "Build tray", delegate { DialogResult = DialogResult.OK; });
            bottom.Controls.Add(_ok);
            bottom.Controls.Add(MakeButton("Cancel", delegate { DialogResult = DialogResult.Cancel; }));
            CancelButton = (Button)bottom.Controls[bottom.Controls.Count - 1];

            Controls.Add(split);
            Controls.Add(bottom);

            _grid.SelectedObject = Options;
            _grid.PropertyValueChanged += delegate { Recalculate(); };
            _preview.Paint += OnPaintPreview;
            _preview.Resize += delegate { _preview.Invalidate(); };

            foreach (var f in files ?? Enumerable.Empty<string>()) _files.Items.Add(f);
            if (_files.Items.Count > 0 && !addToDesigner) _name.Text = DefaultName();
            Recalculate();
        }

        // ---- Entry points ------------------------------------------------------------------------

        /// <summary>
        /// Menu command: pick files, preview, and return a laid-out tray (AutoLayout off, so it builds as previewed).
        /// The tray is also saved as JSON next to the first file, so it can be reopened and edited in the designer.
        /// </summary>
        public static bool CreateTray(IWin32Window owner, out TrayDefinition tray, out string jsonPath)
        {
            tray = null;
            jsonPath = null;
            string[] files = PickFiles(owner);
            if (files == null) return false;

            using (var form = new OutlineImportForm(false, files))
            {
                if (form.ShowDialog(owner) != DialogResult.OK) return false;
                _lastOptions = Copy(form.Options);
                if (form._resolved == null || !form._ready)
                {
                    MessageBox.Show(owner, "The tray is not ready to build. See the notes in the outline window.", "Gridfinity Outlines",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                tray = form._resolved;

                try
                {
                    string folder = Path.GetDirectoryName(form._files.Items[0].ToString());
                    jsonPath = Path.Combine(folder, SafeFileName(tray.Name) + ".json");
                    File.WriteAllText(jsonPath, tray.ToJson());
                }
                catch (Exception)
                {
                    jsonPath = null; // read-only folder etc.; the build still goes ahead
                }
                return true;
            }
        }

        /// <summary>
        /// Designer button: returns the traced pockets (not positioned) and the pocket depth. The caller adds them to
        /// its tray and runs auto-layout.
        /// </summary>
        public static bool ImportPockets(IWin32Window owner, out List<Pocket> pockets, out OutlineImportOptions options)
        {
            pockets = null;
            options = null;
            string[] files = PickFiles(owner);
            if (files == null) return false;

            using (var form = new OutlineImportForm(true, files))
            {
                if (form.ShowDialog(owner) != DialogResult.OK) return false;
                _lastOptions = Copy(form.Options);
                if (form._pockets.Count == 0) return false;
                pockets = form._pockets;
                options = form.Options;
                return true;
            }
        }

        private static string[] PickFiles(IWin32Window owner)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Choose outline-app exports",
                Filter = OutlineImporter.FileFilter,
                Multiselect = true,
                InitialDirectory = _lastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
            })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK || dlg.FileNames.Length == 0) return null;
                _lastFolder = Path.GetDirectoryName(dlg.FileNames[0]);
                return dlg.FileNames;
            }
        }

        // ---- Work --------------------------------------------------------------------------------

        private void AddFiles()
        {
            string[] files = PickFiles(this);
            if (files == null) return;
            foreach (var f in files)
                if (!_files.Items.Contains(f)) _files.Items.Add(f);
            if (!_addToDesigner && string.IsNullOrWhiteSpace(_name.Text)) _name.Text = DefaultName();
            Recalculate();
        }

        private void RemoveSelected()
        {
            if (_files.SelectedIndex >= 0) _files.Items.RemoveAt(_files.SelectedIndex);
            Recalculate();
        }

        private void Recalculate()
        {
            var notes = new List<string>();
            _resolved = null;
            _ready = false;
            _pockets = new List<Pocket>();
            try
            {
                var shapes = new List<TracedShape>();
                foreach (var item in _files.Items)
                {
                    try { shapes.AddRange(OutlineImporter.ReadFile(item.ToString(), notes)); }
                    catch (Exception ex) { notes.Add(Path.GetFileName(item.ToString()) + ": " + ex.Message); }
                }

                string name = _addToDesigner || string.IsNullOrWhiteSpace(_name.Text) ? "OutlineTray" : _name.Text.Trim();
                TrayDefinition tray = OutlineImporter.CreateTray(name, shapes, Options, notes);
                _pockets = new List<Pocket>(tray.Pockets); // fresh objects on every recalculation

                if (tray.Pockets.Count > 0)
                {
                    CompileResult c = OutlineImporter.Resolve(tray, Options.NameLabels);
                    _resolved = c.Resolved;
                    _ready = c.Success;
                    notes.Add("");
                    notes.Add(string.Format("Tray: {0} x {1} cells ({2:0.#} x {3:0.#} mm), {4} height units ({5:0.#} mm).",
                        _resolved.UnitsX, _resolved.UnitsY, _resolved.OuterWidth, _resolved.OuterDepth, _resolved.HeightUnits, _resolved.TotalHeight));
                    notes.Add(c.Success ? "Ready to build." : "Cannot build yet:");
                    notes.Add(c.Report.ToString());
                }
                else if (_files.Items.Count > 0)
                {
                    notes.Add("No usable outlines found.");
                }
            }
            catch (Exception ex)
            {
                notes.Add("Error: " + ex.Message);
            }

            _notes.Text = string.Join(Environment.NewLine, notes);
            _ok.Enabled = _addToDesigner ? _pockets.Count > 0 : _ready;
            _preview.Invalidate();
        }

        // ---- Preview -----------------------------------------------------------------------------

        private void OnPaintPreview(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            TrayDefinition t = _resolved;
            if (t == null)
            {
                g.DrawString(_files.Items.Count == 0 ? "Add outline-app exports to see the tray." : "Nothing to show yet.",
                             Font, Brushes.Gray, 10, 10);
                return;
            }

            double W = t.OuterWidth, D = t.OuterDepth, pad = 8;
            float scale = (float)Math.Min((_preview.Width - 2 * pad) / W, (_preview.Height - 2 * pad) / D);
            if (scale <= 0) return;

            // Tray coordinates are centred with Y up; the panel's Y runs down.
            var state = g.Save();
            g.TranslateTransform(_preview.Width / 2f, _preview.Height / 2f);
            g.ScaleTransform(scale, -scale);

            using (var outline = new Pen(Color.FromArgb(68, 85, 102), 1.2f / scale))
            using (var thin = new Pen(Color.FromArgb(153, 170, 187), 0.8f / scale) { DashStyle = DashStyle.Dash })
            using (var magnet = new Pen(Color.FromArgb(187, 136, 136), 0.8f / scale))
            using (var edge = new Pen(Color.FromArgb(17, 34, 51), 0.8f / scale))
            using (var binFill = new SolidBrush(Color.FromArgb(233, 238, 243)))
            using (var pocketFill = new SolidBrush(Color.FromArgb(61, 110, 153)))
            using (var notchFill = new SolidBrush(Color.FromArgb(91, 143, 189)))
            {
                var bin = new RectangleF((float)(-W / 2), (float)(-D / 2), (float)W, (float)D);
                g.FillRectangle(binFill, bin);
                g.DrawRectangle(outline, bin.X, bin.Y, bin.Width, bin.Height);

                foreach (var c in TrayModeler.CellCenters(t))
                {
                    g.DrawRectangle(thin, (float)(c.X - 18.6), (float)(c.Y - 18.6), 37.2f, 37.2f);
                    if (!t.MagnetHoles) continue;
                    float r = (float)(GridfinitySpec.MagnetDiameter / 2);
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sy in new[] { -1, 1 })
                            g.DrawEllipse(magnet, (float)(c.X + sx * GridfinitySpec.MagnetOffset) - r, (float)(c.Y + sy * GridfinitySpec.MagnetOffset) - r, 2 * r, 2 * r);
                }

                double clr = t.DefaultClearance;
                foreach (var p in t.Pockets)
                {
                    FillLoop(g, p.BuildLoop(clr), pocketFill, edge);
                    foreach (var island in p.BuildIslandLoops(clr)) FillLoop(g, island, binFill, edge);
                    var notch = p.BuildNotchLoop(clr);
                    if (notch != null) FillLoop(g, notch, notchFill, edge);
                }
            }
            g.Restore(state);

            // Pocket and label text, drawn unflipped.
            using (var small = new Font(Font.FontFamily, 8f))
            {
                Func<double, double, PointF> toScreen = (x, y) => new PointF(_preview.Width / 2f + (float)x * scale, _preview.Height / 2f - (float)y * scale);
                var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                foreach (var p in t.Pockets) g.DrawString(p.Name, small, Brushes.White, toScreen(p.X, p.Y), centre);
                foreach (var l in t.Labels) g.DrawString(l.Text, small, Brushes.DarkRed, toScreen(l.X, l.Y + l.HeightMm / 2));
            }
        }

        private static void FillLoop(Graphics g, Loop loop, Brush fill, Pen edge)
        {
            var pts = loop.ToPolyline(4.0).Select(v => new PointF((float)v.X, (float)v.Y)).ToArray();
            if (pts.Length < 3) return;
            g.FillPolygon(fill, pts);
            g.DrawPolygon(edge, pts);
        }

        // ---- Helpers -----------------------------------------------------------------------------

        private string DefaultName()
        {
            string first = Path.GetFileNameWithoutExtension(_files.Items[0].ToString());
            return _files.Items.Count == 1 ? first : first + "_set";
        }

        private static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrWhiteSpace(s) ? "OutlineTray" : s;
        }

        private static OutlineImportOptions Copy(OutlineImportOptions o)
        {
            var copy = new OutlineImportOptions();
            foreach (var prop in typeof(OutlineImportOptions).GetProperties())
                if (prop.CanRead && prop.CanWrite) prop.SetValue(copy, prop.GetValue(o, null), null);
            return copy;
        }

        private static Button MakeButton(string text, EventHandler handler)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += handler;
            return b;
        }

        private sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        }
    }
}
