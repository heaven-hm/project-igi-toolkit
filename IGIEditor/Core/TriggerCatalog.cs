using System;
using System.Collections.Generic;

namespace IGIEditor.Core
{
    /// <summary>One selectable QScript condition shown in the trigger builder.</summary>
    public sealed class TriggerCatalogItem
    {
        public string Key;
        public string Label;
        public string Category;
        public string Expression;
        public string Hint;

        public TriggerCatalogItem(string key, string label, string category, string expression, string hint)
        {
            Key = key;
            Label = label;
            Category = category;
            Expression = expression;
            Hint = hint;
        }
    }

    /// <summary>Success/failure pair produced by a mission preset.</summary>
    public sealed class PresetTriggers
    {
        public TriggerExpression Success;
        public TriggerExpression Failure;
        public string Notes;
    }

    /// <summary>A named, reusable mission scenario (issue #15).</summary>
    public sealed class TriggerPreset
    {
        public string Key;
        public string Title;
        public string Description;
        public Func<QScriptDocument, PresetTriggers> Build;
    }

    /// <summary>
    /// Trigger sources and presets. Presets are resolved against the loaded
    /// objects.qsc instead of hard-coding level-one ids wherever possible.
    /// </summary>
    public static class TriggerCatalog
    {
        public static List<TriggerCatalogItem> BuiltIn()
        {
            return new List<TriggerCatalogItem>
            {
                new TriggerCatalogItem("jones_dead", "Jones is killed", "Jones",
                    "HumanPlayer_0.isDead", "Fails the mission when the player dies."),
                new TriggerCatalogItem("jones_vehicle_destroyed", "Jones' truck destroyed", "Vehicle",
                    "Car_1099.isExploded", "Uses the level-one truck (task 1099)."),
                new TriggerCatalogItem("truck_escape_done", "Truck escape cutscene finished", "Cutscene",
                    "CutScene_1204.isFinished", "Retail level-one escape signal."),
                new TriggerCatalogItem("truck_escape_flagged", "Truck escape flag set", "Variable",
                    "EditVariable_199.nValue == 1", "Retail level-one escape flag."),
                new TriggerCatalogItem("ally_dead", "Friendly soldier killed", "AI",
                    "HumanSoldier_4023.isDead", "Example soldier task id."),
                new TriggerCatalogItem("player_area", "Jones entered escape area", "Area",
                    "AreaActivate_200.nActive", "Retail level-one escape area."),
                new TriggerCatalogItem("timer_elapsed", "Two minutes elapsed", "Timer",
                    "LevelTimer_95.nTick > 120*GAME_FREQUENCY", "Uses GAME_FREQUENCY like retail scripts."),
                new TriggerCatalogItem("alarm", "Base alarm raised", "Alarm",
                    "AlarmControl_98.isAlarm", "Alarm is active."),
                new TriggerCatalogItem("status_sent", "Failure message sent", "Message",
                    "StatusMessage_1391.isSendt", "Retail LevelFlow failure input.")
            };
        }

        /// <summary>Discovers triggerable tasks in the loaded script.</summary>
        public static List<TriggerCatalogItem> FromDocument(QScriptDocument doc)
        {
            var result = new List<TriggerCatalogItem>();
            if (doc == null) return result;

            foreach (var task in doc.Tasks)
            {
                if (task.Id <= 0 || string.IsNullOrEmpty(task.Name)) continue;
                var name = task.Name;
                var id = task.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var label = string.IsNullOrEmpty(task.Note) ? name + " " + id : task.Note;

                if (name == "HumanSoldier" || name == "HumanPlayer")
                {
                    Add(result, name + "_" + id + "_dead", label + " is dead", name,
                        name + "_" + id + ".isDead");
                }
                else if (name == "Car")
                {
                    Add(result, "car_" + id + "_exploded", label + " destroyed", "Vehicle",
                        "Car_" + id + ".isExploded");
                }
                else if (name == "ExplodeObject")
                {
                    Add(result, "explode_" + id, label + " exploded", "Object",
                        "ExplodeObject_" + id + ".isExploded");
                }
                else if (name == "CutScene")
                {
                    Add(result, "cutscene_" + id + "_finished", label + " finished", "Cutscene",
                        "CutScene_" + id + ".isFinished");
                    Add(result, "cutscene_" + id + "_run", label + " running", "Cutscene",
                        "CutScene_" + id + ".isRun");
                }
                else if (name == "AreaActivate")
                {
                    Add(result, "area_" + id, label + " active", "Area",
                        "AreaActivate_" + id + ".nActive");
                }
                else if (name == "LevelTimer")
                {
                    Add(result, "timer_" + id + "_run", label + " running", "Timer",
                        "LevelTimer_" + id + ".isRun");
                    Add(result, "timer_" + id + "_tick", label + " 120s elapsed", "Timer",
                        "LevelTimer_" + id + ".nTick > 120*GAME_FREQUENCY");
                }
                else if (name == "AlarmControl")
                {
                    Add(result, "alarm_" + id, label + " raised", "Alarm",
                        "AlarmControl_" + id + ".isAlarm");
                }
                else if (name == "StatusMessage")
                {
                    Add(result, "status_" + id + "_sent", label + " sent", "Message",
                        "StatusMessage_" + id + ".isSendt");
                }
                else if (name == "EditVariable")
                {
                    Add(result, "variable_" + id, label + " == 1", "Variable",
                        "EditVariable_" + id + ".nValue == 1");
                }
                else if (name == "Switch")
                {
                    Add(result, "switch_" + id, label + " pressed", "Switch",
                        "Switch_" + id + ".isLastPressed");
                }
                else if (name == "Terminal")
                {
                    Add(result, "terminal_" + id + "_hacked", label + " hacked", "Terminal",
                        "Terminal_" + id + ".isHacked");
                }
                else if (name == "ConditionalContainer")
                {
                    Add(result, "container_" + id + "_run", label + " running", "Logic",
                        "ConditionalContainer_" + id + ".isRun");
                }
                else if (name == "Door")
                {
                    Add(result, "door_" + id + "_closed", label + " closed", "Door",
                        "Door_" + id + ".isClosed");
                }
            }
            return result;
        }

        public static List<TriggerPreset> Presets()
        {
            return new List<TriggerPreset>
            {
                new TriggerPreset
                {
                    Key = "truck_escape",
                    Title = "Jones steals the truck and escapes the base",
                    Description = "Uses the truck, escape flag and outro cutscene from the loaded mission.",
                    Build = BuildTruckEscape
                },
                new TriggerPreset
                {
                    Key = "ally_down",
                    Title = "Enemies kill Harrison's man",
                    Description = "Fails when the friendly soldier with Harrison's model (022_01_1) dies.",
                    Build = BuildAllyDown
                },
                new TriggerPreset
                {
                    Key = "limo_destroyed",
                    Title = "Jones destroys Priboi's limo",
                    Description = "Completes when the limo vehicle explodes.",
                    Build = BuildLimoDestroyed
                },
                new TriggerPreset
                {
                    Key = "custom",
                    Title = "Custom expression",
                    Description = "Build success and failure expressions from the trigger list.",
                    Build = BuildCustom
                }
            };
        }

        private static PresetTriggers BuildCustom(QScriptDocument doc)
        {
            var existing = LevelFlowSettings.Read(doc);
            return new PresetTriggers
            {
                Success = existing.Success,
                Failure = existing.Failure,
                Notes = "Starting from the mission's current LevelFlow expressions."
            };
        }

        private static PresetTriggers BuildTruckEscape(QScriptDocument doc)
        {
            var existing = LevelFlowSettings.Read(doc);
            var truck = doc.FindByNote("Truck which Jones will steal") ?? doc.FindFirst("Car");
            var driver = doc.FindByNote("Jones drive truck");
            var truckId = truck != null ? truck.Id : 1099;
            var driverId = driver != null ? driver.Id : 4023;
            var outro = LastTask(doc, "CutScene");
            var flag = doc.FindById(199) ?? FirstWithName(doc, "EditVariable");

            var success = existing.Success;
            if (outro != null && flag != null && flag.Name == "EditVariable")
                success = TriggerExpression.And(
                    TriggerExpression.Leaf("EditVariable_" + flag.Id + ".nValue == 1"),
                    TriggerExpression.Leaf("CutScene_" + outro.Id + ".isFinished"));
            else if (outro != null)
                success = TriggerExpression.Leaf("CutScene_" + outro.Id + ".isFinished");

            var failed = TriggerExpression.Or(
                TriggerExpression.Leaf("HumanPlayer_0.isDead"),
                TriggerExpression.Or(
                    TriggerExpression.Leaf("Car_" + truckId + ".isExploded"),
                    TriggerExpression.Leaf("HumanSoldier_" + driverId + ".isDead")));

            return new PresetTriggers
            {
                Success = success ?? TriggerExpression.Leaf("CutScene_1204.isFinished"),
                Failure = failed,
                Notes = "Target truck task " + truckId + ", driver " + driverId +
                        (outro != null ? ", outro cutscene " + outro.Id : "") + "."
            };
        }

        private static PresetTriggers BuildAllyDown(QScriptDocument doc)
        {
            var existing = LevelFlowSettings.Read(doc);
            var ally = FindSoldierByModel(doc, "022_01_1") ?? doc.FindByNote("Harrison");
            var allyId = ally != null ? ally.Id : 0;

            return new PresetTriggers
            {
                Success = existing.Success,
                Failure = TriggerExpression.Leaf(allyId > 0
                    ? "HumanSoldier_" + allyId + ".isDead"
                    : "HumanSoldier_4023.isDead"),
                Notes = allyId > 0
                    ? "Harrison's soldier resolved to task " + allyId + "."
                    : "No HARRISON model in this script; showing level-one example id 4023."
            };
        }

        private static PresetTriggers BuildLimoDestroyed(QScriptDocument doc)
        {
            var existing = LevelFlowSettings.Read(doc);
            var limo = doc.FindByNote("limo") ?? doc.FindFirst("Car");
            var limoId = limo != null ? limo.Id : 1099;

            return new PresetTriggers
            {
                Success = TriggerExpression.Leaf("Car_" + limoId + ".isExploded"),
                Failure = existing.Failure,
                Notes = limo != null && limo.Note != null && limo.Note.IndexOf("limo", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Limo resolved to task " + limoId + "."
                    : "No limo found in this script; showing the available vehicle task " + limoId + "."
            };
        }

        private static QTaskInfo LastTask(QScriptDocument doc, string taskName)
        {
            QTaskInfo found = null;
            foreach (var task in doc.FindAll(taskName))
                if (found == null || task.Id > found.Id) found = task;
            return found;
        }

        private static QTaskInfo FirstWithName(QScriptDocument doc, string taskName)
        {
            var tasks = doc.FindAll(taskName);
            return tasks.Count > 0 ? tasks[0] : null;
        }

        private static QTaskInfo FindSoldierByModel(QScriptDocument doc, string model)
        {
            foreach (var task in doc.FindAll("HumanSoldier"))
            {
                if (task.Arguments.Count > 4 &&
                    string.Equals(QscText.Unquote(task.Arguments[4]), model, StringComparison.OrdinalIgnoreCase))
                    return task;
            }
            return null;
        }

        private static void Add(List<TriggerCatalogItem> list, string key, string label,
            string category, string expression)
        {
            list.Add(new TriggerCatalogItem(key, label, category, expression, expression));
        }
    }
}
