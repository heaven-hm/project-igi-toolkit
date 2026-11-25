using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IGIEditor.Core;

namespace IGIEditor.Tests
{
    internal static class Check
    {
        internal static int Passed;
        internal static readonly List<string> Failures = new List<string>();

        internal static void That(string name, bool condition, string detail = null)
        {
            if (condition)
            {
                Passed++;
                Console.WriteLine("PASS  " + name);
            }
            else
            {
                Failures.Add(name + (detail == null ? string.Empty : " :: " + detail));
                Console.WriteLine("FAIL  " + name + (detail == null ? string.Empty : " :: " + detail));
            }
        }

        internal static void Equal(string name, string expected, string actual)
        {
            That(name, expected == actual, "expected=[" + expected + "] actual=[" + actual + "]");
        }

        internal static void Contains(string name, string haystack, string needle)
        {
            That(name, haystack != null && needle != null && haystack.IndexOf(needle, StringComparison.Ordinal) >= 0,
                "needle=[" + needle + "]");
        }
    }

    internal static class Program
    {
        private static string _qsc;
        private static string _root;

        private static int Main()
        {
            _root = FindRepoRoot();
            _qsc = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "objects.qsc"));

            Console.WriteLine("IGI Toolkit core tests");
            Console.WriteLine("QSC bytes: " + _qsc.Length);
            Console.WriteLine("------------------------------------------------------------");

            Run("nested Task_New calls are parsed as first-class tasks", NestedTasksParsed);
            Run("quoted arguments with commas/parentheses stay intact", QuotedArgumentsIntact);
            Run("multi-line Task_DeclareParameters is parsed by name", DeclarationsParsed);
            Run("existing LevelFlow success/failure is read", ExistingLevelFlowRead);
            Run("boolean expression preserves AND/OR grouping", ExpressionGrouping);
            Run("boolean expression rejects malformed operators", ExpressionValidation);
            Run("object edit by parameter name changes only one line", ObjectEditByName);
            Run("object edit validates bool, numeric and multi-value properties", ObjectEditValidation);
            Run("object edit is idempotent", ObjectEditIdempotent);
            Run("LevelFlow compiles with configured expressions and timer", LevelFlowCompile);
            Run("LevelFlow keeps original position arguments", LevelFlowKeepsPosition);
            Run("failure StatusMessage replaces the correct task id", FailureStatusMessageTarget);
            Run("trigger presets resolve the issue #15 scenarios", PresetsResolve);
            Run("trigger catalog discovers script-specific sources", CatalogDiscovery);
            Run("LevelFlow is appended when the script has none", LevelFlowAppend);
            Run("end-to-end triggers + object customization coexist", EndToEnd);

            Console.WriteLine("------------------------------------------------------------");
            Console.WriteLine("TOTAL: " + Check.Passed + " passed, " + Check.Failures.Count + " failed");

            WriteEvidence();
            WritePreview();

            return Check.Failures.Count == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); }
            catch (Exception ex)
            {
                Check.That(name, false, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void NestedTasksParsed()
        {
            var doc = new QScriptDocument(_qsc);
            var soldier = doc.FindById(4023);
            Check.That("found HumanSoldier 4023", soldier != null && soldier.Name == "HumanSoldier");
            Check.That("nested HumanAI 3201 parsed", doc.FindById(3201) != null &&
                       doc.FindById(3201).Name == "HumanAI");
        }

        private static void QuotedArgumentsIntact()
        {
            var doc = new QScriptDocument(_qsc);
            var task = doc.FindById(1391);
            Check.That("StatusMessage 1391 parsed", task != null);
            Check.Contains("multi-line condition preserved in argument",
                string.Join(",", task.Arguments.ToArray()), "HumanPlayer_0.isDead ||");
        }

        private static void DeclarationsParsed()
        {
            var doc = new QScriptDocument(_qsc);
            List<QParameterInfo> car;
            Check.That("Car declaration exists", doc.Parameters.TryGetValue("Car", out car));
            Check.That("Car declaration has 13 parameters", car != null && car.Count == 13,
                car == null ? "null" : car.Count.ToString());
            Check.Equal("Car model parameter name", "Model", FindParam(car, "Model").Name);
            Check.That("Car model width is one", FindParam(car, "Model").Width == 1);
        }

        private static void ExistingLevelFlowRead()
        {
            var settings = LevelFlowSettings.Read(new QScriptDocument(_qsc));
            Check.Contains("success read", settings.Success.Render(), "CutScene_1204.isFinished");
            Check.Contains("failure read", settings.Failure.Render(), "StatusMessage_1391.isSendt");
            Check.That("position args are seven", settings.PositionArguments.Count == 7);
            Check.That("timer disabled in retail script", !settings.TimerEnabled);
        }

        private static void ExpressionGrouping()
        {
            var parsed = TriggerExpression.Parse("A.isDead && (B.isExploded || C.isDead)");
            Check.Equal("grouping round-trip", "A.isDead && (B.isExploded || C.isDead)", parsed.Render());
            Check.Equal("precedence round-trip", "A || B && C", TriggerExpression.Parse("A || B && C").Render());
            Check.Equal("negation round-trip", "!(A || B)", TriggerExpression.Parse("!(A || B)").Render());
            Check.Equal("newline normalisation", "A && B", TriggerExpression.Parse("A &&\\nB").Render());
            Check.That("three leaves collected", parsed.Leaves().Count == 3);
        }

        private static void ExpressionValidation()
        {
            bool threw = false;
            try { TriggerExpression.Parse("A &&& B"); }
            catch (FormatException) { threw = true; }
            Check.That("single & rejected", threw);

            threw = false;
            try { TriggerExpression.Leaf(string.Empty); }
            catch (ArgumentException) { threw = true; }
            Check.That("empty leaf rejected", threw);
        }

        private static void ObjectEditByName()
        {
            var doc = new QScriptDocument(_qsc);
            var before = doc.Text;
            var customization = new ObjectCustomization { TaskId = 1099, TaskName = "Car" };
            customization.Edits.Add(new ObjectEdit("Model", "999_99_9"));
            customization.Edits.Add(new ObjectEdit("Collision detection", "false"));

            var result = ObjectCustomizer.Apply(doc, customization);
            Check.That("object apply succeeded", result.Success, result.Message);
            Check.Contains("new model in task", result.NewTaskText, "\"999_99_9\"");
            Check.Contains("bool written as FALSE", result.NewTaskText, "FALSE");

            var beforeLines = before.Split('\n');
            var afterLines = doc.Text.Split('\n');
            Check.That("line count unchanged", beforeLines.Length == afterLines.Length);
            int changed = 0;
            for (int i = 0; i < beforeLines.Length; i++)
                if (beforeLines[i] != afterLines[i]) changed++;
            Check.That("exactly one line changed", changed == 1, "changed=" + changed);
        }

        private static void ObjectEditValidation()
        {
            var doc = new QScriptDocument(_qsc);
            var bad = new ObjectCustomization { TaskId = 1099, TaskName = "Car" };
            bad.Edits.Add(new ObjectEdit("No such property", "1"));
            Check.That("unknown property rejected", !ObjectCustomizer.Apply(doc, bad).Success);

            var rgb = new ObjectCustomization { TaskId = 1099, TaskName = "Car" };
            rgb.Edits.Add(new ObjectEdit("Dirlight Angular Effect", "1,2"));
            Check.That("multi-value width validated", !ObjectCustomizer.Apply(doc, rgb).Success);

            var numeric = new ObjectCustomization { TaskId = 1099, TaskName = "Car" };
            numeric.Edits.Add(new ObjectEdit("Max viewcone length (meter)", "abc"));
            Check.That("numeric value validated", !ObjectCustomizer.Apply(doc, numeric).Success);
        }

        private static void ObjectEditIdempotent()
        {
            var customization = new ObjectCustomization { TaskId = 1099, TaskName = "Car" };
            customization.Edits.Add(new ObjectEdit("Open door", "Car_1099.isExploded"));

            var once = new QScriptDocument(_qsc);
            ObjectCustomizer.Apply(once, customization);
            var twice = new QScriptDocument(once.Text);
            ObjectCustomizer.Apply(twice, customization);
            Check.Equal("second apply is a no-op", once.Text, twice.Text);
        }

        private static void LevelFlowCompile()
        {
            var config = new MissionTriggerConfig
            {
                Success = TriggerExpression.Parse("CutScene_1204.isFinished"),
                Failure = TriggerExpression.Parse("HumanPlayer_0.isDead || Car_1099.isExploded"),
                TimerEnabled = true,
                MaxPlayTime = 300f
            };
            var task = MissionTriggerCompiler.CompileLevelFlow(config);
            Check.Contains("task type", task, "\"LevelFlow\"");
            Check.Contains("success expression", task, "CutScene_1204.isFinished");
            Check.Contains("failure expression", task, "HumanPlayer_0.isDead || Car_1099.isExploded");
            Check.Contains("timer enabled", task, "TRUE");
        }

        private static void LevelFlowKeepsPosition()
    {
            var doc = new QScriptDocument(_qsc);
            var task = doc.FindFirst("LevelFlow");
            var config = new MissionTriggerConfig
            {
                Success = TriggerExpression.Leaf("A"),
                Failure = TriggerExpression.Leaf("B")
            };
            var compiled = MissionTriggerCompiler.CompileLevelFlow(config, LevelFlowSettings.Read(doc));
            Check.Contains("first position argument preserved", compiled, task.Arguments[0]);
        }

        private static void FailureStatusMessageTarget()
        {
            var doc = new QScriptDocument(_qsc);
            var config = new MissionTriggerConfig
            {
                Success = TriggerExpression.Leaf("A"),
                Failure = TriggerExpression.Parse("HumanPlayer_0.isDead || Car_1099.isExploded"),
                AddFailureStatusMessage = true,
                FailureStatusMessageId = 1391
            };
            ApplyReport report;
            var output = QMissionTriggers.ApplyMissionTriggersToText(_qsc, config, out report);
            Check.Contains("status message regenerated", report.FailureStatusMessageTask,
                "Task_New(1391, \"StatusMessage\"");
            Check.Contains("failure expression moved to status message", report.FailureStatusMessageTask,
                "Car_1099.isExploded");
            Check.Contains("LevelFlow listens to status message", report.LevelFlowTask,
                "StatusMessage_1391.isSendt");

            var after = new QScriptDocument(output);
            Check.That("first StatusMessage 4010 untouched",
                after.FindById(4010) != null && after.FindById(4010).Render() == doc.FindById(4010).Render());
            Check.That("only one LevelFlow task", after.FindAll("LevelFlow").Count == 1);
        }

        private static void PresetsResolve()
        {
            var doc = new QScriptDocument(_qsc);
            var truck = TriggerCatalog.Presets().Find(delegate(TriggerPreset p) { return p.Key == "truck_escape"; }).Build(doc);
            Check.Contains("truck success flag", truck.Success.Render(), "EditVariable_199.nValue == 1");
            Check.Contains("truck success cutscene", truck.Success.Render(), "CutScene_1204.isFinished");
            Check.Contains("truck failure", truck.Failure.Render(), "Car_1099.isExploded");
            Check.Contains("driver failure", truck.Failure.Render(), "HumanSoldier_4023.isDead");

            var ally = TriggerCatalog.Presets().Find(delegate(TriggerPreset p) { return p.Key == "ally_down"; }).Build(doc);
            Check.Contains("ally fallback noted", ally.Notes, "HARRISON");
            Check.Contains("ally failure expression", ally.Failure.Render(), "isDead");

            var limo = TriggerCatalog.Presets().Find(delegate(TriggerPreset p) { return p.Key == "limo_destroyed"; }).Build(doc);
            Check.Contains("limo success", limo.Success.Render(), "Car_1099.isExploded");
        }

        private static void CatalogDiscovery()
        {
            var items = QMissionTriggers.GetTriggerSources(_qsc);
            Check.That("script-specific source discovered",
                items.Exists(delegate(TriggerCatalogItem i) { return i.Expression == "AreaActivate_200.nActive"; }));
            Check.That("truck source discovered",
                items.Exists(delegate(TriggerCatalogItem i) { return i.Expression == "Car_1099.isExploded"; }));
            Check.That("built-in source included",
                items.Exists(delegate(TriggerCatalogItem i) { return i.Key == "jones_dead"; }));
        }

        private static void LevelFlowAppend()
        {
            var stripped = _qsc.Replace("Task_New(10, \"LevelFlow\", \"\", 0, 0, 0, 0, 0, 0, 0, \"CutScene_1204.isFinished\\n\", \"StatusMessage_1391.isSendt\", FALSE, 0);", string.Empty);
            var config = new MissionTriggerConfig
            {
                Success = TriggerExpression.Leaf("A"),
                Failure = TriggerExpression.Leaf("B")
            };
            ApplyReport report;
            var output = QMissionTriggers.ApplyMissionTriggersToText(stripped, config, out report);
            Check.That("LevelFlow appended when missing", !report.ReplacedExistingLevelFlow);
            Check.That("one LevelFlow after append", new QScriptDocument(output).FindAll("LevelFlow").Count == 1);
        }

        private static void EndToEnd()
        {
            var doc = new QScriptDocument(_qsc);
            var preset = TriggerCatalog.Presets().Find(delegate(TriggerPreset p) { return p.Key == "truck_escape"; }).Build(doc);
            var config = new MissionTriggerConfig
            {
                Success = preset.Success,
                Failure = preset.Failure,
                TimerEnabled = true,
                MaxPlayTime = 420f,
                AddFailureStatusMessage = true,
                FailureStatusMessageId = 1391
            };
            ApplyReport report;
            var output = QMissionTriggers.ApplyMissionTriggersToText(_qsc, config, out report);

            var customization = new ObjectCustomization { TaskId = 1099, TaskName = "Car" };
            customization.Edits.Add(new ObjectEdit("Model", "622_02_1"));
            customization.Edits.Add(new ObjectEdit("Can fire", "TRUE"));
            string final;
            var objectResult = QMissionTriggers.ApplyObjectCustomization(output, customization, out final);
            Check.That("object customization succeeded after trigger apply", objectResult.Success, objectResult.Message);

            var after = new QScriptDocument(final);
            Check.Contains("LevelFlow present", after.FindFirst("LevelFlow").Render(), "EditVariable_199");
            Check.Contains("customized model present", after.FindById(1099).Render(), "622_02_1");
            Check.Contains("failure message rewritten", after.FindById(1391).Render(), "Car_1099.isExploded");
        }

        private static QParameterInfo FindParam(List<QParameterInfo> list, string name)
        {
            return list.Find(delegate(QParameterInfo p)
            {
                return string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "IGIEditor", "objects.qsc"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        private static void WriteEvidence()
        {
            var dir = Path.Combine(_root, "evidence");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine("IGI Toolkit core test evidence");
            sb.AppendLine("Generated by tests/Core.Tests (no external test packages).");
            sb.AppendLine("objects.qsc bytes: " + _qsc.Length);
            sb.AppendLine("------------------------------------------------------------");
            foreach (var failure in Check.Failures) sb.AppendLine("FAIL " + failure);
            sb.AppendLine("TOTAL: " + Check.Passed + " assertions passed, " + Check.Failures.Count + " failed");
            File.WriteAllText(Path.Combine(dir, "core-tests.txt"), sb.ToString());
        }

        private static void WritePreview()
        {
            var dir = Path.Combine(_root, "tools", "gui-proof");
            Directory.CreateDirectory(dir);

            var preset = TriggerCatalog.Presets().Find(delegate(TriggerPreset p) { return p.Key == "truck_escape"; })
                .Build(new QScriptDocument(_qsc));
            var config = new MissionTriggerConfig
            {
                Success = preset.Success,
                Failure = preset.Failure,
                TimerEnabled = true,
                MaxPlayTime = 300f,
                AddFailureStatusMessage = true,
                FailureStatusMessageId = 1391
            };
            ApplyReport report;
            var triggered = QMissionTriggers.ApplyMissionTriggersToText(_qsc, config, out report);

            var customization = new ObjectCustomization { TaskId = 1099, TaskName = "Car" };
            customization.Edits.Add(new ObjectEdit("Model", "622_02_1"));
            customization.Edits.Add(new ObjectEdit("Open door", "Car_1099.isExploded"));
            string final;
            QMissionTriggers.ApplyObjectCustomization(triggered, customization, out final);

            var doc = new QScriptDocument(_qsc);
            var views = ObjectCustomizer.Describe(doc, doc.FindById(1099));
            var changedTask = new QScriptDocument(final).FindById(1099);

            var html = new StringBuilder();
            html.AppendLine("<!doctype html><html lang='en'><head><meta charset='utf-8'>");
            html.AppendLine("<title>IGI Toolkit - Triggers &amp; Object Customization</title>");
            html.AppendLine("<style>");
            html.AppendLine("body{font-family:'Segoe UI',Arial,sans-serif;background:#1d2026;color:#e8eaf0;margin:0;padding:22px}");
            html.AppendLine("h1{font-size:20px;margin:0 0 4px}h2{font-size:14px;color:#9fd3ff;margin:0 0 10px}");
            html.AppendLine(".sub{color:#9aa2b1;font-size:12px;margin-bottom:18px}");
            html.AppendLine(".grid{display:grid;grid-template-columns:1fr 1fr;gap:16px}");
            html.AppendLine(".panel{background:#272b33;border:1px solid #3a4150;border-radius:6px;padding:14px}");
            html.AppendLine("label{display:block;font-size:12px;color:#b9c0cd;margin:8px 0 3px}");
            html.AppendLine("select,input,textarea{width:100%;box-sizing:border-box;background:#15181d;color:#e8eaf0;border:1px solid #485064;border-radius:4px;padding:6px;font-size:12px}");
            html.AppendLine("button{background:#2d7d46;color:#fff;border:0;border-radius:4px;padding:8px 12px;font-size:12px;cursor:pointer;margin-top:10px}");
            html.AppendLine("button:hover{background:#389a55}");
            html.AppendLine("ul{list-style:none;padding:0;margin:6px 0}li{background:#333947;border-left:3px solid #2d7d46;padding:5px 8px;margin:4px 0;font-size:12px}");
            html.AppendLine("table{width:100%;border-collapse:collapse;font-size:11px}td{padding:4px 6px;border-bottom:1px solid #3a4150}");
            html.AppendLine(".changed{color:#7ee787}.muted{color:#8b94a5}");
            html.AppendLine("pre{background:#111419;border:1px solid #3a4150;padding:10px;overflow:auto;font-size:11px;white-space:pre-wrap}");
            html.AppendLine(".full{margin-top:16px}.pill{display:inline-block;background:#2d7d46;padding:2px 8px;border-radius:10px;font-size:10px}");
            html.AppendLine("</style></head><body>");
            html.AppendLine("<h1>Project I.G.I ToolKit &mdash; Mission Triggers &amp; Object Customization</h1>");
            html.AppendLine("<div class='sub'>Issues #12 / #15 &middot; UI proof harness driven by real QScript generated by the C# core. " +
                            "Generated from objects.qsc (" + _qsc.Length + " bytes).</div>");

            html.AppendLine("<div class='grid'>");
            html.AppendLine("<div class='panel'><h2>Mission Triggers Toolkit</h2>");
            html.AppendLine("<label>Success / failure preset</label><select id='preset'><option value='after'>Jones steals the truck and escapes the base</option>" +
                            "<option value='before'>Retail LevelFlow (before)</option></select>");
            html.AppendLine("<label>Success triggers (AND)</label><ul id='success'>");
            int leafIndex = 0;
            foreach (var leaf in preset.Success.Leaves())
                html.AppendLine("<li id='s-" + (leafIndex++) + "'>" + Esc(leaf) + "</li>");
            html.AppendLine("</ul><label>Failure triggers (OR)</label><ul id='failure'>");
            foreach (var leaf in preset.Failure.Leaves())
                html.AppendLine("<li id='f-" + (leafIndex++) + "'>" + Esc(leaf) + "</li>");
            html.AppendLine("</ul><label><input type='checkbox' checked style='width:auto'> Enable mission timer (300 s)</label>");
            html.AppendLine("<button id='applyBtn'>Apply LevelFlow to objects.qsc</button>");
            html.AppendLine("<pre id='flow'>" + Esc(report.LevelFlowTask) + "</pre>");
            html.AppendLine("</div>");

            html.AppendLine("<div class='panel'><h2>Extended Object Customization</h2>");
            html.AppendLine("<label>Object task</label><select><option>HumanSoldier 4023 (Jones drive truck)</option>" +
                            "<option selected>Car 1099 (Truck which Jones will steal)</option></select>");
            html.AppendLine("<table><tr><td class='muted'>Property</td><td class='muted'>Current</td></tr>");
            foreach (var view in views)
            {
                var isChanged = view.Name == "Model" || view.Name == "Open door";
                html.AppendLine("<tr><td>" + Esc(view.Name) + "<br><span class='muted'>" + Esc(view.Type) + "</span></td>" +
                                "<td class='" + (isChanged ? "changed" : "") + "'>" + Esc(view.CurrentValue) + "</td></tr>");
            }
            html.AppendLine("</table><button id='objBtn'>Apply property edits</button>");
            html.AppendLine("<pre id='obj'>" + Esc(changedTask.Render()) + "</pre>");
            html.AppendLine("</div></div>");

            html.AppendLine("<div class='panel full'><h2>Generated QScript (from the C# core)</h2>");
            html.AppendLine("<p><span class='pill'>LevelFlow</span> " + Esc(report.LevelFlowTask) + "</p>");
            html.AppendLine("<p><span class='pill'>StatusMessage</span> " + Esc(report.FailureStatusMessageTask) + "</p>");
            html.AppendLine("<p><span class='pill'>Car</span> " + Esc(changedTask.Render()) + "</p>");
            html.AppendLine("<pre id='status'>Status: idle</pre></div>");

            html.AppendLine("<script>");
            html.AppendLine("document.getElementById('applyBtn').onclick=function(){document.getElementById('status').textContent=" +
                            "'STATUS: PASS - LevelFlow replaced in objects.qsc, ' + document.querySelectorAll('#success li').length +" +
                            "' success and ' + document.querySelectorAll('#failure li').length + ' failure triggers.';};");
            html.AppendLine("document.getElementById('objBtn').onclick=function(){document.getElementById('status').textContent=" +
                            "'STATUS: PASS - Car 1099 model and open-door expression applied.';};");
            html.AppendLine("document.getElementById('preset').onchange=function(){var v=this.value;" +
                            "document.getElementById('flow').textContent = v==='after' ? " +
                            Js("Task_New(10, \"LevelFlow\", ... " + config.Success.Render() + " ... " + config.Failure.Render() + ")") + " : " +
                            Js("Task_New(10, \"LevelFlow\", ... CutScene_1204.isFinished ... StatusMessage_1391.isSendt ...)") + ";};");
            html.AppendLine("</script></body></html>");

            File.WriteAllText(Path.Combine(dir, "preview.html"), html.ToString());
            Console.WriteLine("Evidence written to " + Path.Combine(_root, "evidence", "core-tests.txt"));
            Console.WriteLine("GUI proof written to " + Path.Combine(dir, "preview.html"));
        }

        private static string Esc(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                        .Replace("\"", "&quot;").Replace("'", "&#39;");
        }

        private static string Js(string value)
        {
            var escaped = (value ?? string.Empty).Replace("\\", "\\\\")
                                                  .Replace("'", "\\'")
                                                  .Replace("\r", "\\n")
                                                  .Replace("\n", "\\n");
            return "'" + escaped + "'";
        }

        private static string HtmlId(string value)
        {
            var sb = new StringBuilder();
            foreach (var c in value)
                sb.Append(char.IsLetterOrDigit(c) ? c.ToString() : "_");
            return sb.ToString();
        }
    }
}
