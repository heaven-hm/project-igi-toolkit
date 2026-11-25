using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IGIEditor.Core
{
    /// <summary>Settings used to build a LevelFlow (mission success/failure) task.</summary>
    public sealed class MissionTriggerConfig
    {
        public TriggerExpression Success;
        public TriggerExpression Failure;
        public bool TimerEnabled;
        public float MaxPlayTime = 300f;
        public int LevelFlowTaskId = 10;

        public bool AddFailureStatusMessage;
        public int FailureStatusMessageId = -1;
        public string FailureMessageText = "MISSION_FAILED";
        public string FailureMessageSound = "fail";
        public float FailureMessageDuration = 2f;
    }

    /// <summary>LevelFlow task as read from an existing QScript file.</summary>
    public sealed class LevelFlowSettings
    {
        public int Id = 10;
        public TriggerExpression Success;
        public TriggerExpression Failure;
        public bool TimerEnabled;
        public float MaxPlayTime;
        public readonly List<string> PositionArguments = new List<string>();

        public static LevelFlowSettings Read(QScriptDocument doc)
        {
            var settings = new LevelFlowSettings();
            if (doc == null) return settings;

            var task = doc.FindFirst("LevelFlow");
            if (task == null) return settings;

            settings.Id = task.Id;
            for (int i = 0; i < Math.Min(7, task.Arguments.Count); i++)
                settings.PositionArguments.Add(task.Arguments[i]);

            if (task.Arguments.Count > 7)
                settings.Success = TryParse(task.Arguments[7]);
            if (task.Arguments.Count > 8)
                settings.Failure = TryParse(task.Arguments[8]);
            if (task.Arguments.Count > 9)
                settings.TimerEnabled = QscText.Unquote(task.Arguments[9]).Trim() == "TRUE";
            if (task.Arguments.Count > 10)
            {
                float value;
                if (float.TryParse(QscText.Unquote(task.Arguments[10]).Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    settings.MaxPlayTime = value;
            }
            return settings;
        }

        private static TriggerExpression TryParse(string raw)
        {
            var value = QscText.Unquote(raw).Trim();
            if (value.Length == 0) return null;
            try { return TriggerExpression.Parse(value); }
            catch (FormatException) { return TriggerExpression.Leaf(value); }
        }
    }

    public sealed class ApplyReport
    {
        public string LevelFlowTask;
        public string FailureStatusMessageTask;
        public bool ReplacedExistingLevelFlow;
        public bool AddedFailureStatusMessage;
    }

    /// <summary>
    /// Turns user-configured success/failure conditions into the QScript tasks
    /// the game actually evaluates. The generated LevelFlow matches the syntax
    /// already used by the toolkit (QMission.AddLevelFlow) and by retail QSC
    /// files.
    /// </summary>
    public static class MissionTriggerCompiler
    {
        public static string CompileLevelFlow(MissionTriggerConfig config, LevelFlowSettings existing = null)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (config.Success == null) throw new ArgumentException("A success trigger is required.", "config");
            if (config.Failure == null) throw new ArgumentException("A failure trigger is required.", "config");
            if (config.MaxPlayTime < 0) throw new ArgumentException("Play time cannot be negative.", "config");

            var args = new List<string>();
            if (existing != null && existing.PositionArguments.Count == 7)
                args.AddRange(existing.PositionArguments);
            else
                for (int i = 0; i < 7; i++) args.Add("0");

            args.Add(QscText.Quote(config.Success.Render()));
            args.Add(QscText.Quote(config.Failure.Render()));
            args.Add(QscText.Bool(config.TimerEnabled));
            args.Add(config.MaxPlayTime.ToString("R", CultureInfo.InvariantCulture));

            return "Task_New(" + config.LevelFlowTaskId + ", \"LevelFlow\", \"\", " +
                   string.Join(", ", args.ToArray()) + ")";
        }

        /// <summary>
        /// Builds the aggregate failure StatusMessage used by retail missions:
        /// when its condition fires, LevelFlow listens for StatusMessage_N.isSendt.
        /// </summary>
        public static string CompileFailureStatusMessage(MissionTriggerConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (config.FailureStatusMessageId <= 0)
                throw new ArgumentException("A positive StatusMessage task id is required.", "config");
            if (config.Failure == null) throw new ArgumentException("A failure trigger is required.", "config");

            return "Task_New(" + config.FailureStatusMessageId + ", \"StatusMessage\", \"\", " +
                   "0, 0, 0, 0, 0, 0, " +
                   QscText.Quote(config.Failure.Render()) + ", " +
                   QscText.Quote(config.FailureMessageText) + ", \"\", " +
                   QscText.Quote(config.FailureMessageSound) + ", " +
                   "TRUE, FALSE, " +
                   config.FailureMessageDuration.ToString("R", CultureInfo.InvariantCulture) + ")";
        }

        public static ApplyReport Apply(QScriptDocument doc, MissionTriggerConfig config)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            if (config == null) throw new ArgumentNullException("config");

            var existing = LevelFlowSettings.Read(doc);
            if (config.Success == null) config.Success = existing.Success;
            if (config.Failure == null) config.Failure = existing.Failure;

            var report = new ApplyReport();
            var flowConfig = Clone(config);

            if (config.AddFailureStatusMessage)
            {
                if (config.FailureStatusMessageId <= 0)
                    throw new InvalidOperationException("Set a positive FailureStatusMessageId first.");
                report.FailureStatusMessageTask = CompileFailureStatusMessage(config);
                doc.ReplaceByIdOrAppend(config.FailureStatusMessageId, "StatusMessage",
                    report.FailureStatusMessageTask);
                report.AddedFailureStatusMessage = true;

                flowConfig.Failure = TriggerExpression.Leaf(
                    "StatusMessage_" + config.FailureStatusMessageId + ".isSendt");
            }

            report.LevelFlowTask = CompileLevelFlow(flowConfig, existing);
            report.ReplacedExistingLevelFlow = doc.ReplaceFirstOrAppend("LevelFlow", report.LevelFlowTask);
            return report;
        }

        private static MissionTriggerConfig Clone(MissionTriggerConfig source)
        {
            return new MissionTriggerConfig
            {
                Success = source.Success,
                Failure = source.Failure,
                TimerEnabled = source.TimerEnabled,
                MaxPlayTime = source.MaxPlayTime,
                LevelFlowTaskId = source.LevelFlowTaskId,
                AddFailureStatusMessage = false,
                FailureStatusMessageId = source.FailureStatusMessageId,
                FailureMessageText = source.FailureMessageText,
                FailureMessageSound = source.FailureMessageSound,
                FailureMessageDuration = source.FailureMessageDuration
            };
        }
    }
}
