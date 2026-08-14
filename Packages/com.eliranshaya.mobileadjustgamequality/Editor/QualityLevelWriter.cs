using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Replaces the project's quality levels with a single <c>MAQ Mobile</c> level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity exposes no public API for creating a quality level, so this edits
    /// <c>ProjectSettings/QualitySettings.asset</c> through a <see cref="SerializedObject"/>.
    /// It is by far the most destructive code in the package: every other quality level in
    /// the project is deleted. That is the intended behaviour - one level is the whole point,
    /// and a list with unused levels in it invites something to select one by index later.
    /// </para>
    /// <para>
    /// Every removed level is named in the run report, and the Setup window lists them before
    /// you press the button.
    /// </para>
    /// </remarks>
    internal static class QualityLevelWriter
    {
        const string k_AssetPath = "ProjectSettings/QualitySettings.asset";
        const string k_LevelsProperty = "m_QualitySettings";
        const string k_Owner = "QualityLevel";

        /// <summary>
        /// Writes the quality level.
        /// </summary>
        /// <param name="urpAsset">The generated URP asset the level should point at.</param>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns>The level's index - always <c>0</c> - or <c>-1</c> on failure.</returns>
        internal static int Write(UniversalRenderPipelineAsset urpAsset, SetupReport report)
        {
            SerializedObject so = Open(report);
            if (so == null)
                return -1;

            SerializedProperty levels = so.FindProperty(k_LevelsProperty);
            if (levels == null || !levels.isArray)
            {
                report.Problem($"'{k_LevelsProperty}' is missing from {k_AssetPath}; no quality level was written.");
                return -1;
            }

            foreach (string removed in ListNames(levels))
            {
                if (removed != QualityPreset.QualityLevelName)
                    report.Update($"Removed quality level '{removed}'");
            }

            bool wasEmpty = levels.arraySize == 0;

            // Resized to one rather than cleared and refilled, and the difference is not
            // cosmetic. Shrinking keeps element 0, so the legacy fields this package does not
            // write - async upload slice and buffer size, terrain distances, the built-in
            // pipeline's shadow settings - keep the project's own values. Clearing first
            // would leave them all at zero, and a zero async upload buffer is a game that
            // stalls on every texture load with nothing in the inspector to explain it.
            levels.arraySize = 1;

            if (wasEmpty)
            {
                report.Note("The project had no quality levels at all, so the new one starts from Unity's " +
                            "defaults for the fields this package does not write. Check async upload settings " +
                            "if textures stream in slowly.");
            }

            const int index = 0;
            SerializedProperty level = levels.GetArrayElementAtIndex(index);
            report.Create($"Quality level '{QualityPreset.QualityLevelName}' at index {index}");

            SerializedFieldHelper.TrySetRelative(level, "name", k_Owner, QualityPreset.QualityLevelName);

            if (urpAsset != null)
                SerializedFieldHelper.TrySetRelative(level, "customRenderPipeline", k_Owner, urpAsset);
            else
                report.Problem("No URP asset to point the quality level at; customRenderPipeline was left alone.");

            SerializedFieldHelper.TrySetRelativeAny(level, QualityPreset.MipmapLimitPaths, k_Owner, 0);

            foreach ((string path, object value) in QualityPreset.QualityLevelFields)
                SerializedFieldHelper.TrySetRelative(level, path, k_Owner, value);

            // With exactly one level left, every index that used to point somewhere else now
            // points nowhere. Both of these are raw ints and neither is clamped by Unity.
            SerializedProperty current = so.FindProperty("m_CurrentQuality");
            if (current != null)
                current.intValue = index;

            ClampPerPlatformDefaults(so, levels.arraySize, report);

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            return index;
        }

        /// <summary>
        /// The names of every quality level currently in the project, in order. Used by the
        /// Setup window to show what pressing the button will remove.
        /// </summary>
        /// <returns>The names, oldest first, or an empty list if they cannot be read.</returns>
        internal static List<string> ListExistingNames()
        {
            Object asset = Load();
            if (asset == null)
                return new List<string>();

            SerializedProperty levels = new SerializedObject(asset).FindProperty(k_LevelsProperty);
            return levels != null && levels.isArray ? ListNames(levels) : new List<string>();
        }

        static List<string> ListNames(SerializedProperty levels)
        {
            var names = new List<string>(levels.arraySize);
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty name = levels.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                names.Add(name != null ? name.stringValue : $"<unnamed {i}>");
            }

            return names;
        }

        /// <summary>Finds the level's index without modifying anything.</summary>
        /// <returns>The index, or <c>-1</c> when it does not exist.</returns>
        internal static int FindExistingIndex()
        {
            Object asset = Load();
            if (asset == null)
                return -1;

            SerializedProperty levels = new SerializedObject(asset).FindProperty(k_LevelsProperty);
            return levels != null && levels.isArray ? IndexOf(levels, QualityPreset.QualityLevelName) : -1;
        }

        /// <summary>Removes the generated level and nothing else.</summary>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns><c>true</c> when a level was removed.</returns>
        internal static bool Remove(SetupReport report)
        {
            SerializedObject so = Open(report);
            if (so == null)
                return false;

            SerializedProperty levels = so.FindProperty(k_LevelsProperty);
            if (levels == null || !levels.isArray)
                return false;

            int index = IndexOf(levels, QualityPreset.QualityLevelName);
            if (index < 0)
                return false;

            // A project with no quality levels at all is broken, and Unity's own UI will not
            // let you get there.
            if (levels.arraySize <= 1)
            {
                report.Note($"Kept '{QualityPreset.QualityLevelName}' because it is the only quality level " +
                            "left, and a project must have at least one.");
                return false;
            }

            levels.DeleteArrayElementAtIndex(index);
            report.Update($"Removed quality level '{QualityPreset.QualityLevelName}' from index {index}");

            // The active level is a raw int, so shortening the list can leave it pointing
            // past the end.
            SerializedProperty current = so.FindProperty("m_CurrentQuality");
            if (current != null)
                current.intValue = Mathf.Clamp(current.intValue, 0, levels.arraySize - 1);

            ClampPerPlatformDefaults(so, levels.arraySize, report);

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return true;
        }

        static void ClampPerPlatformDefaults(SerializedObject so, int levelCount, SetupReport report)
        {
            // m_PerPlatformDefaultQuality decides which level a build actually ships with, so
            // it is not something to rewrite casually. Here it has to be: with one level left,
            // every entry that pointed at index 1 or above now points past the end, which is a
            // build shipping with an undefined quality level.
            SerializedProperty map = so.FindProperty("m_PerPlatformDefaultQuality");
            if (map == null || !map.isArray || levelCount <= 0)
                return;

            for (int i = 0; i < map.arraySize; i++)
            {
                SerializedProperty pair = map.GetArrayElementAtIndex(i);
                SerializedProperty value = pair.FindPropertyRelative("second");

                if (value == null || value.propertyType != SerializedPropertyType.Integer || value.intValue < levelCount)
                    continue;

                SerializedProperty key = pair.FindPropertyRelative("first");
                string platform = key != null && key.propertyType == SerializedPropertyType.String
                    ? key.stringValue
                    : $"entry {i}";

                report.Note($"Default quality level for {platform} pointed past the end of the list; " +
                            $"clamped to {levelCount - 1}.");
                value.intValue = levelCount - 1;
            }
        }

        static int IndexOf(SerializedProperty levels, string name)
        {
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty entry = levels.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (entry != null && string.Equals(entry.stringValue, name, System.StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        static SerializedObject Open(SetupReport report)
        {
            Object asset = Load();
            if (asset != null)
                return new SerializedObject(asset);

            report.Problem($"Could not open {k_AssetPath}; quality settings were not touched.");
            return null;
        }

        static Object Load()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(k_AssetPath);
            return assets != null && assets.Length > 0 ? assets[0] : null;
        }
    }
}
