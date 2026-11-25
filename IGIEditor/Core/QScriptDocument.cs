using System;
using System.Collections.Generic;
using System.Text;

namespace IGIEditor.Core
{
    /// <summary>Absolute span of one argument inside the QScript document.</summary>
    public struct QArgumentSpan
    {
        public int Start;
        public int End;
        public string Text;
    }

    /// <summary>
    /// A single Task_New(...) call parsed out of an IGI QScript (.qsc) file.
    /// Arguments are kept as their original raw text so that untouched values
    /// (including IGI escape sequences such as \n) round-trip byte for byte.
    /// </summary>
    public sealed class QTaskInfo
    {
        public int Id;
        public string Name;
        public string Note;
        public List<string> Arguments = new List<string>();
        /// <summary>Spans for id, name, note and every argument, in that order.</summary>
        public List<QArgumentSpan> ArgumentSpans = new List<QArgumentSpan>();
        public int Start;
        public int End;

        /// <summary>Exactly the text between Task_New( and its matching ).</summary>
        public string Raw
        {
            get { return "Task_New(" + string.Join(",", Arguments.ToArray()) + ")"; }
        }

        /// <summary>Renders the task with normalised ", " separators.</summary>
        public string Render()
        {
            var parts = new List<string>();
            parts.Add(Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            parts.Add(QscText.Quote(Name));
            parts.Add(QscText.Quote(Note));
            parts.AddRange(Arguments);
            return "Task_New(" + string.Join(", ", parts.ToArray()) + ")";
        }
    }

    /// <summary>One entry of a Task_DeclareParameters(...) declaration.</summary>
    public sealed class QParameterInfo
    {
        public string Name;
        public string Label;
        public string Type;

        /// <summary>
        /// Number of QScript values used by this parameter in toolkit-written
        /// scripts. IGI stores ObjectPos / Real32x3 / Real64x3 / RGB as three
        /// values and Real32x9 as three orientation values (alpha, beta, gamma)
        /// in the QSC sources shipped with the toolkit.
        /// </summary>
        public int Width
        {
            get
            {
                switch (Type == null ? string.Empty : Type.Trim())
                {
                    case "ObjectPos":
                    case "Real32x3":
                    case "Real64x3":
                    case "RGB":
                    case "Real32x9":
                        return 3;
                    default:
                        return 1;
                }
            }
        }
    }

    internal struct TextEdit
    {
        public int Start;
        public int End;
        public string Text;
    }

    /// <summary>
    /// Balanced-parenthesis QScript reader/writer. It deliberately does not
    /// use a flat regex: Task_New calls are nested inside Container tasks and
    /// string arguments may themselves contain '(' or ')'.
    /// </summary>
    public sealed class QScriptDocument
    {
        private string _text;
        private readonly List<TextEdit> _edits = new List<TextEdit>();

        public readonly List<QTaskInfo> Tasks = new List<QTaskInfo>();
        public readonly Dictionary<string, List<QParameterInfo>> Parameters =
            new Dictionary<string, List<QParameterInfo>>(StringComparer.OrdinalIgnoreCase);

        public QScriptDocument(string text)
        {
            _text = text ?? string.Empty;
            Parse();
        }

        public string Text
        {
            get
            {
                Flush();
                return _text;
            }
        }

        private void Flush()
        {
            if (_edits.Count == 0) return;

            _edits.Sort(delegate(TextEdit a, TextEdit b) { return b.Start.CompareTo(a.Start); });
            for (int i = 0; i < _edits.Count; i++)
            {
                var edit = _edits[i];
                if (edit.Start < 0 || edit.End > _text.Length || edit.Start > edit.End)
                    throw new InvalidOperationException("QScript edit is out of range.");
                if (i > 0 && edit.End > _edits[i - 1].Start)
                    throw new InvalidOperationException("Overlapping QScript edits are not supported.");
                _text = _text.Substring(0, edit.Start) + edit.Text + _text.Substring(edit.End);
            }
            _edits.Clear();
            Parse();
        }

        private void Parse()
        {
            Tasks.Clear();
            Parameters.Clear();

            foreach (var call in QscSyntax.FindCalls(_text, "Task_New"))
            {
                var spans = QscSyntax.SplitArgumentsWithSpans(call.ArgumentsText,
                    call.Start + call.KeywordLength + 1);
                if (spans.Count < 3) continue;

                var args = new List<string>();
                foreach (var span in spans) args.Add(span.Text);

                int id;
                if (!int.TryParse(QscText.Unquote(args[0]).Trim(), out id)) id = -1;

                Tasks.Add(new QTaskInfo
                {
                    Id = id,
                    Name = QscText.Unquote(args[1]).Trim(),
                    Note = QscText.Unquote(args[2]),
                    Arguments = new List<string>(args.GetRange(3, args.Count - 3)),
                    ArgumentSpans = new List<QArgumentSpan>(
                        spans.GetRange(3, spans.Count - 3)),
                    Start = call.Start,
                    End = call.End
                });
            }

            foreach (var call in QscSyntax.FindCalls(_text, "Task_DeclareParameters"))
            {
                var args = QscSyntax.SplitArguments(call.ArgumentsText);
                if (args.Count < 1) continue;

                var name = QscText.Unquote(args[0]).Trim();
                var list = new List<QParameterInfo>();
                for (int i = 1; i + 1 < args.Count; i += 2)
                {
                    list.Add(new QParameterInfo
                    {
                        Name = QscText.Unquote(args[i]).Trim(),
                        Label = QscText.Unquote(args[i]).Trim(),
                        Type = QscText.Unquote(args[i + 1]).Trim()
                    });
                }
                if (!Parameters.ContainsKey(name)) Parameters.Add(name, list);
            }
        }

        public QTaskInfo FindById(int id)
        {
            Flush();
            return Tasks.Find(delegate(QTaskInfo t) { return t.Id == id; });
        }

        public QTaskInfo FindFirst(string taskName)
        {
            Flush();
            return Tasks.Find(delegate(QTaskInfo t)
            {
                return string.Equals(t.Name, taskName, StringComparison.OrdinalIgnoreCase);
            });
        }

        public List<QTaskInfo> FindAll(string taskName)
        {
            Flush();
            return Tasks.FindAll(delegate(QTaskInfo t)
            {
                return string.Equals(t.Name, taskName, StringComparison.OrdinalIgnoreCase);
            });
        }

        public QTaskInfo FindByNote(string noteFragment)
        {
            Flush();
            if (string.IsNullOrEmpty(noteFragment)) return null;
            return Tasks.Find(delegate(QTaskInfo t)
            {
                return t.Note != null &&
                       t.Note.IndexOf(noteFragment, StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        /// <summary>
        /// Replaces one argument of a task in place, preserving all other bytes
        /// (including newlines and nested tasks) exactly.
        /// </summary>
        public void ReplaceArgument(QTaskInfo task, int argumentIndex, string newValue)
        {
            if (task == null) throw new ArgumentNullException("task");
            if (argumentIndex < 0 || argumentIndex >= task.ArgumentSpans.Count)
                throw new ArgumentOutOfRangeException("argumentIndex");
            var span = task.ArgumentSpans[argumentIndex];
            _edits.Add(new TextEdit { Start = span.Start, End = span.End, Text = newValue });
        }

        /// <summary>Replaces exactly one task call. Other bytes in the file are untouched.</summary>
        public void ReplaceTask(QTaskInfo task, string newText)
        {
            if (task == null) throw new ArgumentNullException("task");
            _edits.Add(new TextEdit { Start = task.Start, End = task.End, Text = newText });
        }

        /// <summary>
        /// Replaces the first task of the given type, or appends it when the
        /// script has no such task yet (e.g. a level with no LevelFlow).
        /// </summary>
        public bool ReplaceFirstOrAppend(string taskName, string newText)
        {
            var task = FindFirst(taskName);
            if (task != null)
            {
                ReplaceTask(task, newText);
                return true;
            }
            Append(newText);
            return false;
        }

        /// <summary>Replaces a specific task id (and type), or appends it when absent.</summary>
        public bool ReplaceByIdOrAppend(int id, string taskName, string newText)
        {
            Flush();
            var task = Tasks.Find(delegate(QTaskInfo t)
            {
                return t.Id == id && string.Equals(t.Name, taskName, StringComparison.OrdinalIgnoreCase);
            });
            if (task != null)
            {
                ReplaceTask(task, newText);
                return true;
            }
            Append(newText);
            return false;
        }

        private void Append(string newText)
        {
            var trimmed = _text.TrimEnd();
            var separator = trimmed.Length == _text.Length ? "\n" : string.Empty;
            _edits.Add(new TextEdit
            {
                Start = _text.Length,
                End = _text.Length,
                Text = separator + newText + "\n"
            });
        }
    }

    internal sealed class QscCall
    {
        public int Start;
        public int End;
        public int KeywordLength;
        public string ArgumentsText;
    }

    internal static class QscSyntax
    {
        /// <summary>
        /// Finds keyword(...) calls while ignoring keywords that appear inside
        /// quoted strings, and matching parentheses with a depth counter.
        /// </summary>
        internal static List<QscCall> FindCalls(string text, string keyword)
        {
            var result = new List<QscCall>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword)) return result;

            bool inString = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' && !IsEscaped(text, i)) inString = !inString;
                if (inString) continue;

                if (string.Compare(text, i, keyword, 0, keyword.Length, StringComparison.Ordinal) != 0)
                    continue;
                if (i > 0 && IsIdentifierChar(text[i - 1])) continue;

                int p = i + keyword.Length;
                while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
                if (p >= text.Length || text[p] != '(') continue;

                int end;
                if (!TryFindClose(text, p, out end)) continue;

                result.Add(new QscCall
                {
                    Start = i,
                    End = end + 1,
                    KeywordLength = keyword.Length,
                    ArgumentsText = text.Substring(p + 1, end - p - 1)
                });
                // Do not skip to the closing parenthesis: nested Task_New calls
                // (e.g. HumanAI inside HumanSoldier) are first-class tasks too.
            }
            return result;
        }

        private static bool TryFindClose(string text, int openParen, out int closeParen)
        {
            int depth = 0;
            bool inString = false;
            for (int i = openParen; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' && !IsEscaped(text, i)) inString = !inString;
                if (inString) continue;
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeParen = i;
                        return true;
                    }
                }
            }
            closeParen = -1;
            return false;
        }

        /// <summary>Splits call arguments on commas that are at parenthesis depth zero.</summary>
        internal static List<string> SplitArguments(string argumentsText)
        {
            var result = new List<string>();
            if (argumentsText == null) return result;

            int depth = 0;
            bool inString = false;
            var buffer = new StringBuilder();
            bool any = false;
            for (int i = 0; i < argumentsText.Length; i++)
            {
                char c = argumentsText[i];
                if (c == '"' && !IsEscaped(argumentsText, i)) inString = !inString;
                if (c == ',' && depth == 0 && !inString)
                {
                    result.Add(buffer.ToString().Trim());
                    buffer.Length = 0;
                    any = false;
                    continue;
                }
                if (!inString)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                }
                buffer.Append(c);
                if (!char.IsWhiteSpace(c)) any = true;
            }
            if (any || buffer.Length > 0) result.Add(buffer.ToString().Trim());
            return result;
        }

        /// <summary>
        /// Same as SplitArguments, but also records each argument's absolute
        /// document span so a single value can be replaced without touching
        /// newlines, nested tasks or any other byte in the file.
        /// </summary>
        internal static List<QArgumentSpan> SplitArgumentsWithSpans(string argumentsText, int baseOffset)
        {
            var result = new List<QArgumentSpan>();
            if (argumentsText == null) return result;

            int depth = 0;
            bool inString = false;
            int bufferStart = -1;
            int lastContent = -1;

            Action finish = delegate
            {
                if (bufferStart < 0) return;
                result.Add(new QArgumentSpan
                {
                    Start = baseOffset + bufferStart,
                    End = baseOffset + lastContent + 1,
                    Text = argumentsText.Substring(bufferStart, lastContent - bufferStart + 1)
                });
                bufferStart = -1;
                lastContent = -1;
            };

            for (int i = 0; i < argumentsText.Length; i++)
            {
                char c = argumentsText[i];
                if (c == '"' && !IsEscaped(argumentsText, i)) inString = !inString;
                if (c == ',' && depth == 0 && !inString)
                {
                    finish();
                    continue;
                }
                if (!inString)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                }
                if (!char.IsWhiteSpace(c))
                {
                    if (bufferStart < 0) bufferStart = i;
                    lastContent = i;
                }
            }
            finish();
            return result;
        }

        private static bool IsEscaped(string text, int quoteIndex)
        {
            int slashes = 0;
            for (int i = quoteIndex - 1; i >= 0 && text[i] == '\\'; i--) slashes++;
            return slashes % 2 == 1;
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }
    }

    internal static class QscText
    {
        /// <summary>
        /// Escapes a QScript VarString. '\n' is a QScript line-break escape and
        /// is intentionally left intact; only literal quotes and real newlines
        /// are converted.
        /// </summary>
        internal static string Escape(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("\"", "\\\"")
                        .Replace("\r\n", "\\n")
                        .Replace("\r", "\\n")
                        .Replace("\n", "\\n");
        }

        internal static string Quote(string value)
        {
            return "\"" + Escape(value) + "\"";
        }

        internal static string Unquote(string value)
        {
            if (value == null) return string.Empty;
            var v = value.Trim();
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"')
                v = v.Substring(1, v.Length - 2);
            return v.Replace("\\\"", "\"");
        }

        internal static string Bool(bool value)
        {
            return value ? "TRUE" : "FALSE";
        }
    }
}
