using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Gridfinity.Core;
using SolidWorks.Interop.sldworks;

namespace Gridfinity.Sw
{
    /// <summary>
    /// The designer window. Five tabs:
    ///   Box       - the bin itself: grid size, height, style, lip, magnets, walls and compartments.
    ///   Baseplate - a baseplate for bins to sit on.
    ///   Cutout    - what is cut into the box: pockets, their layout, and labels.
    ///   Clips     - baseplate clips.
    ///   Parts     - import a photo of parts (or outline-app exports) and lay them onto the box as cutouts, with flat or
    ///               round bottoms, a Y offset, centrelines and names, split to fit the printer.
    /// Box, Cutout and Parts edit the same tray definition; "Build" on any of them builds the box with its cutouts.
    /// </summary>
    internal sealed class TrayDesignerForm : Form
    {
        private readonly ISldWorks _app;
        private TrayDefinition _tray;
        private BaseplateDefinition _plate = new BaseplateDefinition();
        private ClipDefinition _clips = new ClipDefinition();

        private readonly TabControl _tabs = new TabControl();
        private readonly PropertyGrid _boxGrid = MakeGrid();
        private readonly PropertyGrid _plateGrid = MakeGrid();
        private readonly PropertyGrid _cutoutGrid = MakeGrid();
        private readonly PropertyGrid _clipGrid = MakeGrid();
        private readonly Button _layoutButton;
        private readonly Button _importButton;
        private readonly Button _buildButton;
        private readonly PartsTab _partsTab;

        private const int BoxTab = 0, BaseplateTab = 1, CutoutTab = 2, ClipsTab = 3, PartsTabIndex = 4;

        public TrayDesignerForm(ISldWorks app)
        {
            _app = app;
            _tray = new TrayDefinition();
            _tray.Pockets.Add(Pocket.Rect("Example pocket", 60, 25, 10, 2));

            Text = "Gridfinity Outlines";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 900;
            Height = 780;

            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.Add(MakePage("Box", _boxGrid));
            _tabs.TabPages.Add(MakePage("Baseplate", _plateGrid));
            _tabs.TabPages.Add(MakePage("Cutout", _cutoutGrid));
            _tabs.TabPages.Add(MakePage("Clips", _clipGrid));
            _partsTab = new PartsTab(() => _tray, RefreshGrids);
            _tabs.TabPages.Add(MakePage("Parts", _partsTab));
            _tabs.SelectedIndexChanged += delegate
            {
                UpdateButtons();
                if (_tabs.SelectedIndex == PartsTabIndex) _partsTab.Rebind();
            };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6) };
            buttons.Controls.Add(MakeButton("Open JSON...", OnOpen));
            buttons.Controls.Add(MakeButton("Save JSON...", OnSave));
            _importButton = MakeButton("Import parts...", OnImportOutlines);
            buttons.Controls.Add(_importButton);
            _layoutButton = MakeButton("Auto-layout", OnLayout);
            buttons.Controls.Add(_layoutButton);
            buttons.Controls.Add(MakeButton("Validate", OnValidate));
            _buildButton = MakeButton("Build box", OnBuild);
            buttons.Controls.Add(_buildButton);

            Controls.Add(_tabs);
            Controls.Add(buttons);

            BindGrids();
            UpdateButtons();
        }

        // ---- Construction helpers -------------------------------------------------------------
        private static PropertyGrid MakeGrid()
        {
            return new PropertyGrid
            {
                Dock = DockStyle.Fill,
                PropertySort = PropertySort.Categorized,
                ToolbarVisible = false
            };
        }

        private static TabPage MakePage(string title, Control content)
        {
            var page = new TabPage(title);
            page.Controls.Add(content);
            return page;
        }

        private static Button MakeButton(string text, EventHandler handler)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += handler;
            return b;
        }

        // ---- State ----------------------------------------------------------------------------
        private bool OnBaseplate { get { return _tabs.SelectedIndex == BaseplateTab; } }
        private bool OnClips { get { return _tabs.SelectedIndex == ClipsTab; } }

        private void BindGrids()
        {
            _boxGrid.SelectedObject = new CategoryView(_tray, "Grid", "Style", "Open tray", "Derived");
            _cutoutGrid.SelectedObject = new CategoryView(_tray, "Pockets", "Layout", "Labels");
            _plateGrid.SelectedObject = _plate;
            _clipGrid.SelectedObject = _clips;
            if (_partsTab != null) _partsTab.Rebind();
        }

        private void UpdateButtons()
        {
            _buildButton.Text = OnClips ? "Build clips" : OnBaseplate ? "Build baseplate" : "Build box";
            _layoutButton.Enabled = !OnBaseplate && !OnClips;
            _importButton.Enabled = !OnBaseplate && !OnClips;
        }

        private void RefreshGrids()
        {
            _boxGrid.Refresh();
            _cutoutGrid.Refresh();
            _plateGrid.Refresh();
            _clipGrid.Refresh();
        }

        // ---- Buttons --------------------------------------------------------------------------
        private void OnOpen(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "Gridfinity JSON (*.json)|*.json|All files (*.*)|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string text = File.ReadAllText(dlg.FileName);
                    if (ClipDefinition.LooksLikeClips(text))
                    {
                        _clips = ClipDefinition.FromJson(text);
                        _tabs.SelectedIndex = ClipsTab;
                    }
                    else if (BaseplateDefinition.LooksLikeBaseplate(text))
                    {
                        _plate = BaseplateDefinition.FromJson(text);
                        _tabs.SelectedIndex = BaseplateTab;
                    }
                    else
                    {
                        _tray = TrayDefinition.FromJson(text);
                        if (OnBaseplate || OnClips) _tabs.SelectedIndex = BoxTab;
                    }
                    BindGrids();
                    UpdateButtons();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not read that file:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            string name = OnClips ? _clips.Name : OnBaseplate ? _plate.Name : _tray.Name;
            using (var dlg = new SaveFileDialog { Filter = "Gridfinity JSON (*.json)|*.json", FileName = name + ".json" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, OnClips ? _clips.ToJson() : OnBaseplate ? _plate.ToJson() : _tray.ToJson());
            }
        }

        // Adds parts traced from a photo or outline-app exports to this tray and lays everything out again.
        private void OnImportOutlines(object sender, EventArgs e)
        {
            List<Pocket> pockets;
            OutlineImportOptions o;
            if (!OutlineImportForm.ImportPockets(this, out pockets, out o)) return;

            // The untouched starter pocket is only there as an example; drop it.
            if (_tray.Pockets.Count == 1 && _tray.Pockets[0].Name == "Example pocket") _tray.Pockets.Clear();
            _tray.Pockets.AddRange(pockets);
            _tray.AutoLayout = true;
            _tray.AutoGrowGrid = true;
            _tray.MaxUnitsX = Math.Max(_tray.MaxUnitsX, o.MaxUnitsX);
            _tray.MaxUnitsY = Math.Max(_tray.MaxUnitsY, o.MaxUnitsY);
            if (_tray.Pockets.Count == pockets.Count) { _tray.PartTopOffset = o.TopOffset; _tray.Printer = o.Printer; }

            LayoutResult r = TrayPlanner.SuggestGrid(_tray) ? TrayPlanner.AutoLayout(_tray) : new LayoutResult();
            OutlineImporter.FitHeight(_tray, true);
            BindGrids();
            RefreshGrids();
            _tabs.SelectedIndex = PartsTabIndex;

            string msg = "Added " + pockets.Count + " pocket(s). Tray is now " + _tray.UnitsX + " x " + _tray.UnitsY +
                         " cells, " + _tray.HeightUnits + " height units.";
            if (!r.Fits) msg += "\n\nNot everything fits within " + _tray.MaxUnitsX + " x " + _tray.MaxUnitsY +
                                " cells. Raise MaxUnitsX/MaxUnitsY on the Cutout tab and run Auto-layout.";
            MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, r.Fits ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void OnLayout(object sender, EventArgs e)
        {
            LayoutResult r = _tray.AutoGrowGrid
                ? (TrayPlanner.SuggestGrid(_tray) ? TrayPlanner.AutoLayout(_tray) : new LayoutResult())
                : TrayPlanner.AutoLayout(_tray);
            RefreshGrids();

            if (!r.Fits && r.Unplaced.Count > 0)
                MessageBox.Show(this, r.Unplaced.Count + " pocket(s) did not fit, e.g. '" + r.Unplaced[0].Name + "'. Enlarge the grid or turn on AutoGrowGrid.",
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnValidate(object sender, EventArgs e)
        {
            bool ok;
            string report;
            if (OnClips)
            {
                ValidationReport v = _clips.Validate();
                ok = v.IsValid;
                report = v.ToString();
            }
            else if (OnBaseplate)
            {
                BaseplateCompileResult c = BaseplateModeler.Compile(_plate);
                ok = c.Success;
                report = c.Report.ToString();
            }
            else
            {
                CompileResult c = TrayCompiler.Compile(_tray);
                ok = c.Success;
                report = c.Report.ToString();
            }

            MessageBox.Show(this, (ok ? "Ready to build.\n\n" : "Cannot build yet.\n\n") + report,
                            Text, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void OnBuild(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            SwBuildResult r;
            try
            {
                var builder = new GridfinityBuilder(_app);
                r = OnClips ? builder.BuildClips(_clips) : OnBaseplate ? builder.BuildBaseplate(_plate) : builder.Build(_tray);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            string text = r.Message;
            if (r.Warnings.Count > 0) text += "\n\nNotes:\n- " + string.Join("\n- ", r.Warnings.ToArray());
            MessageBox.Show(this, text, Text, MessageBoxButtons.OK, r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
