using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using IGIEditor.Core;

namespace IGIEditor
{
    /// <summary>
    /// Trigger Toolkit / Extended Object Customization tab. All QScript work is
    /// delegated to IGIEditor.Core; this class only handles WinForms plumbing.
    /// </summary>
    internal sealed class TriggerToolkitControl : UserControl
    {
        private readonly ComboBox _presetCb = new ComboBox();
        private readonly ComboBox _triggerSourceCb = new ComboBox();
        private readonly ComboBox _successOperatorCb = new ComboBox();
        private readonly ComboBox _failureOperatorCb = new ComboBox();
        private readonly CheckBox _negateCb = new CheckBox();
        private readonly CheckBox _timerCb = new CheckBox();
        private readonly CheckBox _failureStatusCb = new CheckBox();
        private readonly TextBox _playTimeTxt = new TextBox();
        private readonly TextBox _statusIdTxt = new TextBox();
        private readonly ListBox _successLb = new ListBox();
        private readonly ListBox _failureLb = new ListBox();
        private readonly TextBox _previewTxt = new TextBox();
        private readonly ComboBox _objectTaskCb = new ComboBox();
        private readonly DataGridView _paramsDgv = new DataGridView();
        private readonly TextBox _objectStatusTxt = new TextBox();
        private readonly Label _statusLbl = new Label();

        private List<TriggerCatalogItem> _sources = new List<TriggerCatalogItem>();

        internal TriggerToolkitControl()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            BuildUi();
            WireEvents();
            RefreshSources();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, Width = 980 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            var triggers = new GroupBox { Text = "Mission success / failure triggers", Dock = DockStyle.Fill, Height = 430 };
            object task = triggers;
            AddLabel(ref task, "Scenario preset", 12, 26);
            AddControl(ref task, _presetCb, 150, 22, 470, 22);
            AddButton(ref task, "Load preset", 630, 20, "loadPresetBtn", LoadPresetClick);

            AddLabel(ref task, "Trigger source (objects.qsc)", 12, 60);
            AddControl(ref task, _triggerSourceCb, 150, 56, 470, 22);

            _negateCb.Text = "Negate (NOT)";
            _negateCb.Location = new System.Drawing.Point(630, 56);
            _negateCb.Size = new System.Drawing.Size(110, 22);
            triggers.Controls.Add(_negateCb);

            AddButton(ref task, "Add to success", 12, 92, "addSuccessBtn", AddSuccessClick);
            AddButton(ref task, "Add to failure", 140, 92, "addFailureBtn", AddFailureClick);

            AddLabel(ref task, "Success (ALL of these)", 12, 124);
            AddControl(ref task, _successLb, 12, 142, 300, 110);
            AddLabel(ref task, "Join with", 322, 142);
            AddControl(ref task, _successOperatorCb, 322, 160, 90, 22);
            AddButton(ref task, "Remove", 322, 200, "removeSuccessBtn", RemoveSuccessClick);

            AddLabel(ref task, "Failure (ANY of these)", 430, 124);
            AddControl(ref task, _failureLb, 430, 142, 300, 110);
            AddLabel(ref task, "Join with", 740, 142);
            AddControl(ref task, _failureOperatorCb, 740, 160, 90, 22);
            AddButton(ref task, "Remove", 740, 200, "removeFailureBtn", RemoveFailureClick);

            _timerCb.Text = "Enable timer";
            _timerCb.Location = new System.Drawing.Point(12, 266);
            _timerCb.Size = new System.Drawing.Size(110, 22);
            triggers.Controls.Add(_timerCb);
            AddLabel(ref task, "Max play time (s)", 130, 268);
            AddControl(ref task, _playTimeTxt, 240, 264, 70, 22);
            _failureStatusCb.Text = "Aggregate failure message";
            _failureStatusCb.Location = new System.Drawing.Point(330, 266);
            _failureStatusCb.Size = new System.Drawing.Size(200, 22);
            triggers.Controls.Add(_failureStatusCb);
            AddLabel(ref task, "Message task id", 540, 268);
            AddControl(ref task, _statusIdTxt, 640, 264, 70, 22);

            AddButton(ref task, "Apply LevelFlow to objects.qsc", 12, 300, "applyTriggersBtn", ApplyTriggersClick);
            AddLabel(ref task, "Generated LevelFlow", 12, 336);
            _previewTxt.Multiline = true;
            _previewTxt.ReadOnly = true;
            _previewTxt.ScrollBars = ScrollBars.Both;
            AddControl(ref task, _previewTxt, 12, 354, 820, 60);

            var objects = new GroupBox { Text = "Extended object customization", Dock = DockStyle.Fill, Height = 430 };
            task = objects;
            AddLabel(ref task, "Object task", 12, 26);
            AddControl(ref task, _objectTaskCb, 100, 22, 470, 22);
            AddButton(ref task, "Load properties", 580, 20, "loadObjectBtn", LoadObjectClick);
            AddButton(ref task, "Apply changes", 690, 20, "applyObjectBtn", ApplyObjectClick);

            _paramsDgv.Location = new System.Drawing.Point(12, 56);
            _paramsDgv.Size = new System.Drawing.Size(820, 260);
            _paramsDgv.AllowUserToAddRows = false;
            _paramsDgv.AllowUserToDeleteRows = false;
            _paramsDgv.RowHeadersVisible = false;
            _paramsDgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            objects.Controls.Add(_paramsDgv);

            AddLabel(ref task, "Result", 12, 330);
            _objectStatusTxt.Multiline = true;
            _objectStatusTxt.ReadOnly = true;
            AddControl(ref task, _objectStatusTxt, 12, 348, 820, 60);

            root.Controls.Add(triggers, 0, 0);
            root.Controls.Add(objects, 1, 0);

            _statusLbl.Location = new System.Drawing.Point(12, 900);
            _statusLbl.Size = new System.Drawing.Size(950, 24);
            _statusLbl.Text = "Ready.";

            Controls.Add(root);
            Controls.Add(_statusLbl);
        }

        private void WireEvents()
        {
            _successOperatorCb.Items.AddRange(new object[] { "AND (&&)", "OR (||)" });
            _failureOperatorCb.Items.AddRange(new object[] { "OR (||)", "AND (&&)" });
            _successOperatorCb.SelectedIndex = 0;
            _failureOperatorCb.SelectedIndex = 0;
            _timerCb.Checked = false;
            _failureStatusCb.Checked = true;
            _playTimeTxt.Text = "300";
            _statusIdTxt.Text = "1391";

            _paramsDgv.CellValidating += ParamsCellValidating;
        }

        private static void AddLabel(ref object parent, string text, int x, int y)
        {
            var label = new Label { Text = text, Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(300, 20) };
            ((Control)parent).Controls.Add(label);
        }

        private static void AddControl(ref object parent, Control control, int x, int y, int width, int height)
        {
            control.Location = new System.Drawing.Point(x, y);
            control.Size = new System.Drawing.Size(width, height);
            ((Control)parent).Controls.Add(control);
        }

        private static void AddButton(ref object parent, string text, int x, int y, string name, EventHandler handler)
        {
            var button = new Button
            {
                Text = text,
                Name = name,
                Location = new System.Drawing.Point(x, y),
                Size = new System.Drawing.Size(160, 24),
                FlatStyle = FlatStyle.Flat
            };
            button.Click += handler;
            ((Control)parent).Controls.Add(button);
        }

        private void ParamsCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex == 3 && e.FormattedValue == null)
                e.Cancel = true;
        }

        private void RefreshSources()
        {
            var qsc = QUtils.LoadFile();
            if (string.IsNullOrEmpty(qsc))
            {
                SetStatus("objects.qsc is not loaded. Set the game/QEditor path first.");
                return;
            }

            var doc = new QScriptDocument(qsc);
            _sources = QMissionTriggers.GetTriggerSources(qsc);

            _presetCb.Items.Clear();
            foreach (var preset in QMissionTriggers.GetPresets())
                _presetCb.Items.Add(preset.Title);
            if (_presetCb.Items.Count > 0) _presetCb.SelectedIndex = 0;

            _triggerSourceCb.Items.Clear();
            foreach (var source in _sources)
                _triggerSourceCb.Items.Add(source.Label + "  ->  " + source.Expression);
            if (_triggerSourceCb.Items.Count > 0) _triggerSourceCb.SelectedIndex = 0;

            _objectTaskCb.Items.Clear();
            foreach (var task in doc.Tasks)
            {
                if (task.Id <= 0) continue;
                _objectTaskCb.Items.Add(task.Id + " - " + (string.IsNullOrEmpty(task.Note) ? task.Name : task.Note));
            }
            if (_objectTaskCb.Items.Count > 0) _objectTaskCb.SelectedIndex = 0;
        }

        private void LoadPresetClick(object sender, EventArgs e)
        {
            try
            {
                var qsc = QUtils.LoadFile();
                if (string.IsNullOrEmpty(qsc)) { SetStatus("objects.qsc is not loaded."); return; }
                if (_presetCb.SelectedIndex < 0) return;

                var presets = QMissionTriggers.GetPresets();
                var preset = presets[_presetCb.SelectedIndex];
                var built = QMissionTriggers.BuildPreset(qsc, preset.Key);

                _successLb.Items.Clear();
                _failureLb.Items.Clear();
                if (built.Success != null)
                    foreach (var leaf in built.Success.Leaves()) _successLb.Items.Add(leaf);
                if (built.Failure != null)
                    foreach (var leaf in built.Failure.Leaves()) _failureLb.Items.Add(leaf);

                UpdatePreview();
                SetStatus(preset.Title + ": " + built.Notes);
            }
            catch (Exception ex) { SetStatus("Preset failed: " + ex.Message); }
        }

        private void AddSuccessClick(object sender, EventArgs e) { AddSelectedTrigger(_successLb); }
        private void AddFailureClick(object sender, EventArgs e) { AddSelectedTrigger(_failureLb); }

        private void AddSelectedTrigger(ListBox list)
        {
            if (_triggerSourceCb.SelectedIndex < 0 || _triggerSourceCb.SelectedIndex >= _sources.Count) return;
            var expression = _sources[_triggerSourceCb.SelectedIndex].Expression;
            if (_negateCb.Checked && !expression.StartsWith("!", StringComparison.Ordinal))
                expression = "!(" + expression + ")";
            if (!list.Items.Contains(expression)) list.Items.Add(expression);
            UpdatePreview();
        }

        private void RemoveSuccessClick(object sender, EventArgs e) { RemoveSelected(_successLb); }
        private void RemoveFailureClick(object sender, EventArgs e) { RemoveSelected(_failureLb); }

        private static void RemoveSelected(ListBox list)
        {
            while (list.SelectedIndices.Count > 0) list.Items.RemoveAt(list.SelectedIndices[0]);
        }

        private void LoadObjectClick(object sender, EventArgs e)
        {
            try
            {
                var qsc = QUtils.LoadFile();
                if (string.IsNullOrEmpty(qsc)) { SetStatus("objects.qsc is not loaded."); return; }
                if (_objectTaskCb.SelectedIndex < 0) return;

                var taskId = ParseLeadingInt(_objectTaskCb.SelectedItem.ToString());
                var doc = new QScriptDocument(qsc);
                var task = doc.FindById(taskId);
                if (task == null) { SetStatus("Task " + taskId + " was not found."); return; }

                _paramsDgv.Rows.Clear();
                _paramsDgv.Columns.Clear();
                _paramsDgv.Columns.Add("Name", "Property");
                _paramsDgv.Columns.Add("Type", "Type");
                _paramsDgv.Columns.Add("Current", "Current value");
                _paramsDgv.Columns.Add("New", "New value");

                foreach (var view in ObjectCustomizer.Describe(doc, task))
                {
                    var row = _paramsDgv.Rows.Add(view.Name, view.Type, view.CurrentValue, view.CurrentValue);
                    _paramsDgv.Rows[row].Cells[0].ReadOnly = true;
                    _paramsDgv.Rows[row].Cells[1].ReadOnly = true;
                    _paramsDgv.Rows[row].Cells[2].ReadOnly = true;
                }
                SetStatus("Loaded " + _paramsDgv.Rows.Count + " properties from " + task.Name + " " + task.Id + ".");
            }
            catch (Exception ex) { SetStatus("Load properties failed: " + ex.Message); }
        }

        private void ApplyObjectClick(object sender, EventArgs e)
        {
            try
            {
                var qsc = QUtils.LoadFile();
                if (string.IsNullOrEmpty(qsc)) { SetStatus("objects.qsc is not loaded."); return; }
                if (_objectTaskCb.SelectedIndex < 0) return;

                var taskId = ParseLeadingInt(_objectTaskCb.SelectedItem.ToString());
                var customization = new ObjectCustomization { TaskId = taskId };
                foreach (DataGridViewRow row in _paramsDgv.Rows)
                {
                    if (row.IsNewRow) continue;
                    var current = row.Cells[2].Value == null ? string.Empty : row.Cells[2].Value.ToString();
                    var updated = row.Cells[3].Value == null ? string.Empty : row.Cells[3].Value.ToString();
                    if (current == updated) continue;
                    customization.Edits.Add(new ObjectEdit(row.Cells[0].Value.ToString(), updated));
                }

                if (customization.Edits.Count == 0)
                {
                    SetStatus("No property values were changed.");
                    return;
                }

                string updatedQsc;
                var result = QMissionTriggers.ApplyObjectCustomization(qsc, customization, out updatedQsc);
                if (!result.Success) { SetStatus("Customization failed: " + result.Message); return; }

                QUtils.SaveFile(updatedQsc);
                QCompiler.CompileEx(updatedQsc);
                _objectStatusTxt.Text = result.NewTaskText;
                SetStatus(result.Message + string.Join("; ", result.Changes.ToArray()));
            }
            catch (Exception ex) { SetStatus("Apply customization failed: " + ex.Message); }
        }

        private void ApplyTriggersClick(object sender, EventArgs e)
        {
            try
            {
                var qsc = QUtils.LoadFile();
                if (string.IsNullOrEmpty(qsc)) { SetStatus("objects.qsc is not loaded."); return; }

                float playTime;
                if (!float.TryParse(_playTimeTxt.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out playTime))
                {
                    SetStatus("Enter a valid play time in seconds.");
                    return;
                }
                int statusId;
                if (!int.TryParse(_statusIdTxt.Text, out statusId)) statusId = 1391;

                var success = Combine(_successLb, _successOperatorCb.SelectedIndex == 1 ? "||" : "&&");
                var failure = Combine(_failureLb, _failureOperatorCb.SelectedIndex == 0 ? "||" : "&&");
                var config = new MissionTriggerConfig
                {
                    Success = success,
                    Failure = failure,
                    TimerEnabled = _timerCb.Checked,
                    MaxPlayTime = playTime,
                    AddFailureStatusMessage = _failureStatusCb.Checked,
                    FailureStatusMessageId = statusId
                };

                ApplyReport report;
                var updatedQsc = QMissionTriggers.ApplyMissionTriggersToText(qsc, config, out report);
                QUtils.SaveFile(updatedQsc);
                QCompiler.CompileEx(updatedQsc);

                _previewTxt.Text = report.LevelFlowTask;
                SetStatus(report.ReplacedExistingLevelFlow
                    ? "LevelFlow updated in objects.qsc and compiled."
                    : "LevelFlow added to objects.qsc and compiled.");
            }
            catch (Exception ex) { SetStatus("Apply triggers failed: " + ex.Message); }
        }

        private static TriggerExpression Combine(ListBox list, string op)
        {
            var items = new List<string>();
            foreach (var item in list.Items) items.Add(item.ToString());
            if (items.Count == 0) return null;
            var expressions = new List<TriggerExpression>();
            foreach (var item in items) expressions.Add(TriggerExpression.Parse(item));
            return TriggerExpression.Combine(expressions, op);
        }

        private void UpdatePreview()
        {
            var success = Combine(_successLb, _successOperatorCb.SelectedIndex == 1 ? "||" : "&&");
            var failure = Combine(_failureLb, _failureOperatorCb.SelectedIndex == 0 ? "||" : "&&");
            var config = new MissionTriggerConfig
            {
                Success = success ?? TriggerExpression.Leaf("<add a success trigger>"),
                Failure = failure ?? TriggerExpression.Leaf("<add a failure trigger>"),
                TimerEnabled = _timerCb.Checked,
                MaxPlayTime = 300f
            };
            _previewTxt.Text = MissionTriggerCompiler.CompileLevelFlow(config);
        }

        private static int ParseLeadingInt(string text)
        {
            var value = 0;
            var part = text == null ? string.Empty : text.Split('-')[0].Trim();
            return int.TryParse(part, out value) ? value : -1;
        }

        private void SetStatus(string message)
        {
            _statusLbl.Text = message;
            if (IGIEditorUI.editorRef != null)
                IGIEditorUI.editorRef.SetStatusText(message);
        }
    }
}
