using System;
using System.Collections.Generic;
using IGIEditor.Core;

namespace IGIEditor
{
    /// <summary>
    /// Editor-facing facade for the trigger / object customization toolkit.
    /// The portable logic lives in IGIEditor.Core so it can be unit tested
    /// without loading the WinForms editor or the game's native DLL.
    /// </summary>
    static class QMissionTriggers
    {
        internal static List<TriggerCatalogItem> GetTriggerSources(string qscData)
        {
            var doc = new QScriptDocument(qscData);
            var items = TriggerCatalog.BuiltIn();
            items.AddRange(TriggerCatalog.FromDocument(doc));
            return items;
        }

        internal static List<TriggerPreset> GetPresets()
        {
            return TriggerCatalog.Presets();
        }

        internal static PresetTriggers BuildPreset(string qscData, string presetKey)
        {
            var doc = new QScriptDocument(qscData);
            foreach (var preset in TriggerCatalog.Presets())
            {
                if (string.Equals(preset.Key, presetKey, StringComparison.OrdinalIgnoreCase))
                    return preset.Build(doc);
            }
            throw new ArgumentException("Unknown trigger preset: " + presetKey);
        }

        internal static ApplyReport ApplyMissionTriggers(string qscData, MissionTriggerConfig config)
        {
            var doc = new QScriptDocument(qscData);
            return MissionTriggerCompiler.Apply(doc, config);
        }

        internal static string ApplyMissionTriggersToText(string qscData, MissionTriggerConfig config,
            out ApplyReport report)
        {
            var doc = new QScriptDocument(qscData);
            report = MissionTriggerCompiler.Apply(doc, config);
            return doc.Text;
        }

        internal static ObjectApplyResult ApplyObjectCustomization(string qscData,
            ObjectCustomization customization, out string updatedQsc)
        {
            var doc = new QScriptDocument(qscData);
            var result = ObjectCustomizer.Apply(doc, customization);
            updatedQsc = doc.Text;
            return result;
        }
    }
}
