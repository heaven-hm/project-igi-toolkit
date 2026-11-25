using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace IGIEditor
{
    /// <summary>
    /// Standalone terrain editor window. All file parsing, sculpting and 3D
    /// projection live in the portable QTerrain*.cs files; this class only
    /// provides WinForms input and GDI+ output.
    /// </summary>
    public sealed class QTerrainEditorForm : Form
    {
        private HmpFile _file;
        private HmpSlot _slot;
        private readonly TerrainCamera _camera = new TerrainCamera();
        private TerrainTool _tool = TerrainTool.Raise;
        private TerrainTool _mouseTool = TerrainTool.Raise;
        private bool _dragging;
        private string _currentPath = "";

        private PictureBox _viewport;
        private ComboBox _slotCombo;
        private NumericUpDown _radius;
        private NumericUpDown _strength;
        private ToolStripButton _raiseBtn;
        private ToolStripButton _lowerBtn;
        private ToolStripButton _smoothBtn;
        private ToolStripStatusLabel _status;

        public QTerrainEditorForm()
        {
            Text = "Project I.G.I ToolKit - Terrain Editor";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 620);
            MinimumSize = new Size(520, 400);
            KeyPreview = true;

            BuildUi();
            NewProject();
        }

        private TerrainHeightmap Map { get { return _slot == null ? null : _slot.Map; } }

        private void BuildUi()
        {
            _viewport = new PictureBox();
            _viewport.Dock = DockStyle.Fill;
            _viewport.BackColor = Color.FromArgb(220, 233, 242);
            _viewport.Paint += ViewportPaint;
            _viewport.MouseDown += ViewportMouseDown;
            _viewport.MouseMove += ViewportMouseMove;
            _viewport.MouseUp += ViewportMouseUp;
            _viewport.MouseWheel += ViewportMouseWheel;
            _viewport.Resize += delegate(object sender, EventArgs e) { _viewport.Invalidate(); };

            StatusStrip statusStrip = new StatusStrip();
            _status = new ToolStripStatusLabel("Ready");
            statusStrip.Items.Add(_status);

            ToolStrip toolbar = new ToolStrip();
            _raiseBtn = AddToolButton(toolbar, "Raise", delegate { SelectTool(TerrainTool.Raise); });
            _lowerBtn = AddToolButton(toolbar, "Lower", delegate { SelectTool(TerrainTool.Lower); });
            _smoothBtn = AddToolButton(toolbar, "Smooth", delegate { SelectTool(TerrainTool.Smooth); });
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(new ToolStripLabel("Radius"));
            _radius = new NumericUpDown();
            _radius.Minimum = 1;
            _radius.Maximum = 128;
            _radius.Value = 5;
            _radius.Width = 60;
            toolbar.Items.Add(new ToolStripControlHost(_radius));
            toolbar.Items.Add(new ToolStripLabel("Strength"));
            _strength = new NumericUpDown();
            _strength.Minimum = 1;
            _strength.Maximum = 100;
            _strength.Value = 18;
            _strength.Width = 60;
            toolbar.Items.Add(new ToolStripControlHost(_strength));
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(new ToolStripLabel("Slot"));
            _slotCombo = new ComboBox();
            _slotCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _slotCombo.Width = 150;
            _slotCombo.SelectedIndexChanged += SlotComboSelectedIndexChanged;
            toolbar.Items.Add(new ToolStripControlHost(_slotCombo));
            toolbar.Items.Add(new ToolStripSeparator());
            AddToolButton(toolbar, "Reset slot", ResetSlot);
            AddToolButton(toolbar, "Rotate L", delegate { RotateYaw(-10); });
            AddToolButton(toolbar, "Rotate R", delegate { RotateYaw(10); });

            MenuStrip menu = new MenuStrip();
            ToolStripMenuItem fileMenu = new ToolStripMenuItem("File");
            fileMenu.DropDownItems.Add(NewItem("New height map", delegate { NewProject(); }));
            fileMenu.DropDownItems.Add(NewItem("Open terrain.hmp...", delegate { PromptForHmp(); }));
            fileMenu.DropDownItems.Add(NewItem("Save terrain.hmp...", SaveHmp));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(NewItem("Import PGM / RAW...", ImportHeightmap));
            fileMenu.DropDownItems.Add(NewItem("Export PGM...", ExportPgm));
            menu.Items.Add(fileMenu);

            ToolStripMenuItem helpMenu = new ToolStripMenuItem("Help");
            helpMenu.DropDownItems.Add(NewItem("Controls", ShowHelp));
            menu.Items.Add(helpMenu);

            Controls.Add(_viewport);
            Controls.Add(statusStrip);
            Controls.Add(toolbar);
            Controls.Add(menu);
            MainMenuStrip = menu;
        }

        private static ToolStripMenuItem NewItem(string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += handler;
            return item;
        }

        private static ToolStripButton AddToolButton(ToolStrip toolbar, string text, EventHandler handler)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += handler;
            toolbar.Items.Add(button);
            return button;
        }

        /// <summary>Called by the main ToolKit when the user chooses "Resume".</summary>
        public void PromptForHmp()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "IGI height map|terrain.hmp|All files|*.*";
            dialog.Title = "Open terrain.hmp";
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            LoadFile(dialog.FileName);
        }

        public void LoadFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("A path is required.", "path");
            LoadHmp(path);
        }

        private void NewProject()
        {
            _file = new HmpFile();
            TerrainHeightmap map = new TerrainHeightmap(32);
            map.Fill(TerrainHeightmap.NeutralValue);
            _file.SetSlot(0, map);
            _currentPath = "";
            RefreshSlots(0);
            UpdateStatus();
        }

        private void LoadHmp(string path)
        {
            try
            {
                HmpFile file = HmpFile.Load(File.ReadAllBytes(path));
                int first = 0;
                for (int i = 0; i < HmpFile.MaxSlots; i++)
                {
                    if (file.GetSlot(i).Map != null) { first = i; break; }
                }
                _file = file;
                _currentPath = path;
                RefreshSlots(first);
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read height map: " + ex.Message, "Terrain Editor",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveHmp(object sender, EventArgs e)
        {
            if (_file == null) return;
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "IGI height map|*.hmp";
            dialog.FileName = string.IsNullOrEmpty(_currentPath) ? "terrain.hmp" : Path.GetFileName(_currentPath);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllBytes(dialog.FileName, _file.Save());
                _currentPath = dialog.FileName;
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save height map: " + ex.Message, "Terrain Editor",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportHeightmap(object sender, EventArgs e)
        {
            if (Map == null) return;
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "Height map image|*.pgm;*.raw";
            dialog.Title = "Import height map (8-bit grayscale)";
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);
                TerrainHeightmap imported = Path.GetExtension(dialog.FileName).ToLowerInvariant() == ".pgm"
                    ? HeightmapIo.ImportPgm(bytes)
                    : HeightmapIo.ImportRaw8(bytes, Map.Size);
                _file.SetSlot(_slotCombo.SelectedIndex, imported);
                RefreshSlots(_slotCombo.SelectedIndex);
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not import image: " + ex.Message, "Terrain Editor",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportPgm(object sender, EventArgs e)
        {
            if (Map == null) return;
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "Portable gray map|*.pgm";
            dialog.FileName = "terrain-" + _slotCombo.SelectedIndex + ".pgm";
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllBytes(dialog.FileName, HeightmapIo.ExportPgm(Map));
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not export image: " + ex.Message, "Terrain Editor",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ResetSlot(object sender, EventArgs e)
        {
            if (Map == null) return;
            Map.Fill(TerrainHeightmap.NeutralValue);
            _viewport.Invalidate();
            UpdateStatus();
        }

        private void SelectTool(TerrainTool tool)
        {
            _tool = tool;
            _raiseBtn.Checked = tool == TerrainTool.Raise;
            _lowerBtn.Checked = tool == TerrainTool.Lower;
            _smoothBtn.Checked = tool == TerrainTool.Smooth;
            UpdateStatus();
        }

        private void RotateYaw(double degrees)
        {
            _camera.YawDegrees += degrees;
            _viewport.Invalidate();
        }

        private void ShowHelp(object sender, EventArgs e)
        {
            MessageBox.Show(this,
                "Left-drag: apply selected tool\nRight-drag: lower\nMouse wheel: zoom\n" +
                "Rotate L/R buttons: orbit camera\n" +
                "Import maps PGM/RAW as 8-bit gray (128 = neutral). RAW uses the current map size.\n" +
                "A .hmp slot must also be referenced by objects.qsc HeightMap tasks in game.",
                "Terrain Editor controls", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RefreshSlots(int selected)
        {
            _slotCombo.Items.Clear();
            for (int i = 0; i < HmpFile.MaxSlots; i++)
            {
                HmpSlot slot = _file.GetSlot(i);
                if (slot.Map == null) continue;
                _slotCombo.Items.Add(i);
            }
            if (_slotCombo.Items.Count == 0)
            {
                _slot = null;
                _slotCombo.SelectedIndex = -1;
            }
            else
            {
                _slotCombo.SelectedIndex = Math.Max(0, _slotCombo.Items.IndexOf(selected));
                _slot = _file.GetSlot((int)_slotCombo.SelectedItem);
                _camera.Center = Map.Size / 2.0;
            }
            SelectTool(_tool);
            _viewport.Invalidate();
        }

        private void SlotComboSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_slotCombo.SelectedIndex < 0 || _file == null) return;
            _slot = _file.GetSlot((int)_slotCombo.SelectedItem);
            _camera.Center = Map.Size / 2.0;
            _viewport.Invalidate();
            UpdateStatus();
        }

        private void ViewportPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (Map == null) return;
            _camera.Width = _viewport.ClientSize.Width;
            _camera.Height = _viewport.ClientSize.Height;
            foreach (TerrainFace face in TerrainRenderer.BuildFaces(Map, _camera))
            {
                PointF[] points = new PointF[] { new PointF((float)face.A.X, (float)face.A.Y),
                                                 new PointF((float)face.B.X, (float)face.B.Y),
                                                 new PointF((float)face.C.X, (float)face.C.Y) };
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(face.Shade, face.Shade, face.Shade)))
                    g.FillPolygon(brush, points);
            }
        }

        private void ViewportMouseDown(object sender, MouseEventArgs e)
        {
            if (Map == null) return;
            _dragging = true;
            _viewport.Capture = true;
            _mouseTool = e.Button == MouseButtons.Right ? TerrainTool.Lower : _tool;
            Sculpt(e.X, e.Y, _mouseTool);
        }

        private void ViewportMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging && Map != null) Sculpt(e.X, e.Y, _mouseTool);
        }

        private void ViewportMouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
            _viewport.Capture = false;
        }

        private void ViewportMouseWheel(object sender, MouseEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            _camera.Zoom = Math.Max(0.2, Math.Min(8.0, _camera.Zoom * factor));
            _viewport.Invalidate();
        }

        private void Sculpt(int screenX, int screenY, TerrainTool tool)
        {
            TerrainWorldPoint point = _camera.ScreenToTerrain(screenX, screenY, 0.0);
            int x = (int)Math.Round(point.X);
            int y = (int)Math.Round(point.Y);
            if (x < 0 || y < 0 || x >= Map.Side || y >= Map.Side) return;

            double strength = (double)_strength.Value;
            if (tool == TerrainTool.Smooth) strength /= 100.0;
            TerrainSculpt.Apply(Map, tool, x, y, (double)_radius.Value, strength);
            _viewport.Invalidate();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_status == null || Map == null) return;
            _status.Text = "Slot " + _slotCombo.SelectedIndex + "  |  " + Map.Side + " x " + Map.Side +
                           "  |  tool: " + _tool + "  |  " +
                           (string.IsNullOrEmpty(_currentPath) ? "not saved yet" : _currentPath);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.D1) SelectTool(TerrainTool.Raise);
            else if (e.KeyCode == Keys.D2) SelectTool(TerrainTool.Lower);
            else if (e.KeyCode == Keys.D3) SelectTool(TerrainTool.Smooth);
            else if (e.KeyCode == Keys.Left) RotateYaw(-10);
            else if (e.KeyCode == Keys.Right) RotateYaw(10);
        }
    }
}
