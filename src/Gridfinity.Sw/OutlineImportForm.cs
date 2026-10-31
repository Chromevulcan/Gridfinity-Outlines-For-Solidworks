using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Gridfinity.Core;
using Gridfinity.Trace;

namespace Gridfinity.Sw
{
    /// <summary>
    /// Picks photos of parts on paper (traced here) or outline-app exports (DXF, SVG or the _dxf.zip it makes for
    /// several objects), shows the import options, the tracing and a to-scale preview of the laid-out tray, and hands
    /// back either a ready tray (menu command) or just the parts (designer Parts tab and "Import outlines...").
    /// </summary>
    internal sealed class OutlineImportForm : Form
    {
        private readonly ListBox _files = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        private readonly PropertyGrid _grid = new PropertyGrid { Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false };
        private readonly Panel _preview = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly PictureBox _tracing = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
        private readonly TabControl _views = new TabControl { Dock = DockStyle.Fill };
        private SplitResult _split;

        // Tracing a photo takes a moment, so results are kept until the file, the photo options or the scale
        // reference change. Outline files go through the same path so the scale reference applies to them too.
        private readonly Dictionary<string, Tuple<List<TracedShape>, string, List<string>>> _traced =
            new Dictionary<string, Tuple<List<TracedShape>, string, List<string>>>(StringComparer.OrdinalIgnoreCase);
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

            Text = addToDesigner ? "Import parts into the tray" : "Build tray from photos or outlines";
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
            var fileBox = new GroupBox { Text = "Photos of parts on paper, or outline-app exports (.dxf, .svg, _dxf.zip)", Dock = DockStyle.Fill };
            fileBox.Controls.Add(_files);
            fileBox.Controls.Add(fileButtons);
            left.Panel1.Controls.Add(fileBox);

            var optionBox = new GroupBox { Text = "Options", Dock = DockStyle.Fill };
            optionBox.Controls.Add(_grid);
            left.Panel2.Controls.Add(optionBox);
            split.Panel1.Controls.Add(left);

            var trayPage = new TabPage("Tray (top view, to scale)");
            trayPage.Controls.Add(_preview);
            var tracePage = new TabPage("Tracing of the selected photo");
            tracePage.Controls.Add(_tracing);
            _views.TabPages.Add(trayPage);
            _views.TabPages.Add(tracePage);
            right.Panel1.Controls.Add(_views);
            _files.SelectedIndexChanged += delegate { ShowTracing(); };
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
            _ok = MakeButton(addToDesigner ? "Add parts" : "Build tray", delegate { DialogResult = DialogResult.OK; });
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
                Title = "Choose photos of parts on paper, or outline-app exports",
                Filter = ShapeSources.FileFilter,
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
                    try { shapes.AddRange(Read(item.ToString(), notes)); }
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
                    _split = null;
                    if (c.Success)
                    {
                        var fixedTray = _resolved.Clone();
                        fixedTray.AutoLayout = false;
                        fixedTray.AutoGrowGrid = false;
                        _split = TraySplitter.Split(fixedTray);
                        notes.AddRange(_split.Notes);
                    }
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
            ShowTracing();
        }

        private static string PhotoKey(string path, OutlineImportOptions o)
        {
            return string.Join("|", path, File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0, o.Paper, o.PaperWidth, o.PaperHeight,
                               o.PhotoMode, o.ScanDpi, o.Sensitivity, o.PaperBorder, o.PixelsPerMm,
                               o.ScaleReference, o.ReferenceWidth, o.ReferenceHeight);
        }

        private List<TracedShape> Read(string path, List<string> notes)
        {
            string key = PhotoKey(path, Options);
            Tuple<List<TracedShape>, string, List<string>> hit;
            if (!_traced.TryGetValue(key, out hit))
            {
                var traceNotes = new List<string>();
                Cursor = Cursors.WaitCursor;
                try
                {
                    string overlay;
                    var shapes = ShapeSources.Read(path, Options, traceNotes, out overlay);
                    hit = Tuple.Create(shapes, overlay, traceNotes);
                }
                finally { Cursor = Cursors.Default; }
                foreach (var old in _traced.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList()) _traced.Remove(old);
                _traced[key] = hit;
            }
            notes.AddRange(hit.Item3);
            // Hand out copies: the importer turns and simplifies the shapes it is given.
            return hit.Item1.Select(x => new TracedShape
            {
                Name = x.Name, Source = x.Source, Outline = new List<Vec2>(x.Outline),
                Holes = x.Holes.Select(h => new List<Vec2>(h)).ToList()
            }).ToList();
        }

        private void ShowTracing()
        {
            string path = _files.SelectedItem as string ?? _files.Items.Cast<object>().Select(o => o.ToString()).FirstOrDefault(PhotoTracer.IsImage);
            Tuple<List<TracedShape>, string, List<string>> hit;
            string overlay = path != null && _traced.TryGetValue(PhotoKey(path, Options), out hit) ? hit.Item2 : null;
            var old = _tracing.Image;
            _tracing.Image = null;
            if (old != null) old.Dispose();
            if (overlay != null && File.Exists(overlay))
            {
                // Load without locking the file, so it can be overwritten by the next tracing.
                using (var stream = new MemoryStream(File.ReadAllBytes(overlay))) _tracing.Image = Image.FromStream(stream);
            }
        }

        // ---- Preview -----------------------------------------------------------------------------

        private void OnPaintPreview(object sender, PaintEventArgs e)
        {
            PreviewPainter.Paint(e.Graphics, _preview, _resolved, _split, null,
                _files.Items.Count == 0 ? "Add photos or outline-app exports to see the tray." : "Nothing to show yet.");
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

    }
}
