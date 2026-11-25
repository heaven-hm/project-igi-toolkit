using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IGIEditor.Core
{
    /// <summary>A single named property change requested for a level object.</summary>
    public sealed class ObjectEdit
    {
        public string Parameter;
        public string Value;

        public ObjectEdit() { }
        public ObjectEdit(string parameter, string value)
        {
            Parameter = parameter;
            Value = value;
        }
    }

    /// <summary>A reusable set of property changes for one object task.</summary>
    public sealed class ObjectCustomization
    {
        public int TaskId;
        public string TaskName;
        public string TaskNote;
        public readonly List<ObjectEdit> Edits = new List<ObjectEdit>();
    }

    /// <summary>One editable property shown in the object customizer UI.</summary>
    public sealed class ObjectParameterView
    {
        public string Name;
        public string Type;
        public string CurrentValue;
        public int Index;
        public int Width;
    }

    public sealed class ObjectApplyResult
    {
        public bool Success;
        public string Message;
        public string NewTaskText;
        public readonly List<string> Changes = new List<string>();
    }

    /// <summary>
    /// Parameter-name based editing of level objects. The editable property
    /// names come from Task_DeclareParameters in the loaded objects.qsc, so new
    /// properties do not require hard-coded argument offsets in the editor.
    /// </summary>
    public static class ObjectCustomizer
    {
        public static List<ObjectParameterView> Describe(QScriptDocument doc, QTaskInfo task)
        {
            var result = new List<ObjectParameterView>();
            if (doc == null || task == null) return result;

            var declared = GetParameters(doc, task.Name);
            int index = 0;
            if (declared != null && declared.Count > 0)
            {
                foreach (var param in declared)
                {
                    var width = param.Width;
                    var values = new List<string>();
                    for (int i = 0; i < width && index + i < task.Arguments.Count; i++)
                        values.Add(task.Arguments[index + i]);
                    result.Add(new ObjectParameterView
                    {
                        Name = param.Name,
                        Type = param.Type,
                        CurrentValue = string.Join(", ", values.ToArray()),
                        Index = index,
                        Width = width
                    });
                    index += width;
                }
            }
            else
            {
                for (int i = 0; i < task.Arguments.Count; i++)
                {
                    result.Add(new ObjectParameterView
                    {
                        Name = "Arg" + (i + 1),
                        Type = "unknown",
                        CurrentValue = task.Arguments[i],
                        Index = i,
                        Width = 1
                    });
                }
            }
            return result;
        }

        public static ObjectApplyResult Apply(QScriptDocument doc, ObjectCustomization customization)
        {
            var result = new ObjectApplyResult();
            if (doc == null) throw new ArgumentNullException("doc");
            if (customization == null) throw new ArgumentNullException("customization");
            if (customization.Edits.Count == 0)
            {
                result.Message = "No property changes were supplied.";
                return result;
            }

            var task = ResolveTask(doc, customization);
            if (task == null)
            {
                result.Message = "No matching object task was found in the loaded script.";
                return result;
            }

            var views = Describe(doc, task);
            var newArguments = new List<string>(task.Arguments);
            var replacedIndexes = new List<int>();
            foreach (var edit in customization.Edits)
            {
                if (string.IsNullOrWhiteSpace(edit.Parameter))
                {
                    result.Message = "A property name is missing.";
                    return result;
                }

                var view = views.Find(delegate(ObjectParameterView v)
                {
                    return string.Equals(v.Name, edit.Parameter.Trim(), StringComparison.OrdinalIgnoreCase);
                });
                if (view == null)
                {
                    result.Message = "Property '" + edit.Parameter + "' is not declared for " + task.Name + ".";
                    return result;
                }

                string[] values;
                string error;
                if (!FormatValue(view, edit.Value, out values, out error))
                {
                    result.Message = error;
                    return result;
                }

                for (int i = 0; i < values.Length; i++)
                {
                    if (view.Index + i >= newArguments.Count)
                    {
                        result.Message = "Property '" + view.Name + "' is outside the task arguments.";
                        return result;
                    }
                    newArguments[view.Index + i] = values[i];
                    replacedIndexes.Add(view.Index + i);
                }
                result.Changes.Add(view.Name + " = " + string.Join(", ", values));
            }

            // Edit individual arguments in place so nested tasks and the
            // original line layout survive untouched.
            foreach (var index in replacedIndexes)
                doc.ReplaceArgument(task, index, newArguments[index]);

            var renderedParts = new List<string>
            {
                task.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                QscText.Quote(task.Name),
                QscText.Quote(task.Note)
            };
            renderedParts.AddRange(newArguments);
            result.NewTaskText = "Task_New(" + string.Join(", ", renderedParts.ToArray()) + ")";
            result.Success = true;
            result.Message = "Updated " + task.Name + " (task " + task.Id + ").";
            return result;
        }

        private static QTaskInfo ResolveTask(QScriptDocument doc, ObjectCustomization customization)
        {
            if (customization.TaskId > 0)
            {
                var byId = doc.FindById(customization.TaskId);
                if (byId != null) return byId;
            }
            if (!string.IsNullOrWhiteSpace(customization.TaskNote))
            {
                var byNote = doc.FindByNote(customization.TaskNote.Trim());
                if (byNote != null) return byNote;
            }
            if (!string.IsNullOrWhiteSpace(customization.TaskName))
                return doc.FindFirst(customization.TaskName.Trim());
            return null;
        }

        private static List<QParameterInfo> GetParameters(QScriptDocument doc, string taskName)
        {
            List<QParameterInfo> declared;
            if (doc.Parameters.TryGetValue(taskName ?? string.Empty, out declared)) return declared;
            return null;
        }

        private static bool FormatValue(ObjectParameterView view, string input,
            out string[] values, out string error)
        {
            values = null;
            error = null;
            if (input == null) input = string.Empty;

            if (view.Width > 1)
            {
                var parts = input.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != view.Width)
                {
                    error = "Property '" + view.Name + "' expects " + view.Width + " values.";
                    return false;
                }
                values = parts;
                return true;
            }

            var type = view.Type ?? string.Empty;
            if (type == "bool8")
            {
                var v = input.Trim().ToLowerInvariant();
                if (v == "true" || v == "1" || v == "yes") values = new[] { "TRUE" };
                else if (v == "false" || v == "0" || v == "no") values = new[] { "FALSE" };
                else
                {
                    error = "Property '" + view.Name + "' expects TRUE or FALSE.";
                    return false;
                }
                return true;
            }

            if (IsNumeric(type))
            {
                float parsed;
                if (!float.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    error = "Property '" + view.Name + "' expects a number.";
                    return false;
                }
                values = new[] { parsed.ToString("R", CultureInfo.InvariantCulture) };
                return true;
            }

            if (input.Length >= 2 && input[0] == '"' && input[input.Length - 1] == '"')
                values = new[] { input };
            else
                values = new[] { QscText.Quote(input) };
            return true;
        }

        private static bool IsNumeric(string type)
        {
            switch (type)
            {
                case "Int32":
                case "Int16":
                case "Real32":
                case "Real64":
                case "Angle":
                case "Degrees":
                    return true;
                default:
                    return false;
            }
        }
    }
}
