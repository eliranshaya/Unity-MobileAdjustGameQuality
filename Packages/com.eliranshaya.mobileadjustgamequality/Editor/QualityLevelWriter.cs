using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Appends the three <c>MAQ</c> Quality Levels, or updates them in place if they are
    /// already there, and reports the indices they landed on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity exposes no public API for creating a Quality Level, so this edits
    /// <c>ProjectSettings/QualitySettings.asset</c> through a
    /// <see cref="SerializedObject"/>. That makes it the most destructive code in the
    /// package if it gets this wrong, which is why it only ever appends and never
    /// reorders, replaces or deletes anything it did not create.
    /// </para>
    /// <para>
    /// <c>m_PerPlatformDefaultQuality</c> is deliberately never touched. Writing it would
    /// silently change which level a build ships with, and a graphics package quietly
    /// re-pointing the shipped quality level is a bug report nobody would connect back
    /// to this tool.
    /// </para>
    /// </remarks>
    internal static class QualityLevelWriter
    {
        const string k_AssetPath = "ProjectSettings/QualitySettings.asset";
        const string k_LevelsProperty = "m_QualitySettings";
        const string k_Owner = "QualityLevel";

        // Renamed in Unity 2022.2. Older projects still serialize it as textureQuality.
        static readonly string[] k_MipmapLimitPaths = { "globalTextureMipmapLimit", "textureQuality" };

        /// <summary>
        /// Writes the three quality levels.
        /// </summary>
        /// <param name="urpAssets">The three generated URP assets, in tier order. Entries may be <c>null</c>.</param>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns>
        /// The three quality level indices in tier order, or <c>null</c> if the settings
        /// asset could not be opened.
        /// </returns>
        internal static int[] AppendOrUpdate(UniversalRenderPipelineAsset[] urpAssets, SetupReport report)
        {
            SerializedObject so = OpenQualitySettings(report);
            if (so == null)
                return null;

            SerializedProperty levels = so.FindProperty(k_LevelsProperty);
            if (levels == null || !levels.isArray)
            {
                report.Problem($"'{k_LevelsProperty}' is missing from {k_AssetPath}; no quality levels were written.");
                return null;
            }

            var indices = new int[3];

            for (int tier = 0; tier < 3; tier++)
            {
                string name = QualityTierPresets.QualityLevelNames[tier];
                int index = IndexOfLevel(levels, name);

                if (index < 0)
                {
                    // Append only. InsertArrayElementAtIndex at arraySize clones the last
                    // element, which is a feature here: the handful of legacy fields this
                    // package does not write inherit the project's own values instead of
                    // Unity's cold defaults.
                    index = levels.arraySize;
                    levels.InsertArrayElementAtIndex(index);
                    report.Create($"Quality level '{name}' at index {index}");
                }
                else
                {
                    report.Update($"Quality level '{name}' at index {index}");
                }

                indices[tier] = index;
                WriteLevel(levels.GetArrayElementAtIndex(index), tier, name,
                    urpAssets != null && tier < urpAssets.Length ? urpAssets[tier] : null, report);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            return indices;
        }

        /// <summary>
        /// Wipes the quality level list and writes exactly three levels - MAQ Low,
        /// Balanced and High - at indices 0, 1 and 2.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the destructive counterpart to <see cref="AppendOrUpdate"/> and it
        /// deletes quality levels the user created. The Setup window only calls it behind
        /// an explicit opt-in and a confirmation dialog that names every level about to
        /// go, because there is no undo for it.
        /// </para>
        /// <para>
        /// The list is resized rather than cleared and refilled. Growing an array clones
        /// the last element and shrinking keeps the leading ones, so the surviving
        /// elements carry the project's own values for the legacy fields this package does
        /// not write (async upload sizes, terrain distances, built-in-pipeline shadow
        /// settings). Clearing first would leave those at zero, which is a slow project
        /// nobody could explain.
        /// </para>
        /// </remarks>
        /// <param name="urpAssets">The three generated URP assets, in tier order.</param>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns><c>{0, 1, 2}</c>, or <c>null</c> if the settings asset could not be opened.</returns>
        internal static int[] ReplaceAll(UniversalRenderPipelineAsset[] urpAssets, SetupReport report)
        {
            SerializedObject so = OpenQualitySettings(report);
            if (so == null)
                return null;

            SerializedProperty levels = so.FindProperty(k_LevelsProperty);
            if (levels == null || !levels.isArray)
            {
                report.Problem($"'{k_LevelsProperty}' is missing from {k_AssetPath}; no quality levels were written.");
                return null;
            }

            foreach (string removed in ListLevelNames(levels))
                report.Update($"Replaced quality level '{removed}'");

            bool wasEmpty = levels.arraySize == 0;
            levels.arraySize = 3;

            if (wasEmpty)
            {
                report.Note("The project had no quality levels at all, so the three new ones start from " +
                            "Unity's defaults for the fields this package does not write. Check async upload " +
                            "and terrain settings if you use them.");
            }

            var indices = new int[3];
            for (int tier = 0; tier < 3; tier++)
            {
                indices[tier] = tier;
                WriteLevel(levels.GetArrayElementAtIndex(tier), tier, QualityTierPresets.QualityLevelNames[tier],
                    urpAssets != null && tier < urpAssets.Length ? urpAssets[tier] : null, report);
                report.Create($"Quality level '{QualityTierPresets.QualityLevelNames[tier]}' at index {tier}");
            }

            ClampCurrentQuality(so, levels.arraySize);
            ClampPerPlatformDefaults(so, levels.arraySize, report);

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            return indices;
        }

        /// <summary>
        /// Removes the three <c>MAQ</c> quality levels and nothing else.
        /// </summary>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns>How many levels were removed.</returns>
        internal static int RemoveGeneratedLevels(SetupReport report)
        {
            SerializedObject so = OpenQualitySettings(report);
            if (so == null)
                return 0;

            SerializedProperty levels = so.FindProperty(k_LevelsProperty);
            if (levels == null || !levels.isArray)
                return 0;

            // Collect first, then delete from the highest index down, so removing one
            // level does not shift the index of the next one still to be removed.
            var targets = new List<int>();
            for (int i = 0; i < levels.arraySize; i++)
            {
                string name = ReadName(levels.GetArrayElementAtIndex(i));
                if (System.Array.IndexOf(QualityTierPresets.QualityLevelNames, name) >= 0)
                    targets.Add(i);
            }

            targets.Sort();
            int removed = 0;

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                // A project with zero quality levels is a broken project - Unity's own UI
                // will not let you get there. If the MAQ levels are the only ones left,
                // the last one stays and the user is told why.
                if (levels.arraySize <= 1)
                {
                    report.Note($"Kept '{ReadName(levels.GetArrayElementAtIndex(0))}' because it is the only " +
                                "quality level left, and a project must have at least one. Rename or " +
                                "reconfigure it by hand if you no longer want it.");
                    break;
                }

                report.Update($"Removed quality level '{ReadName(levels.GetArrayElementAtIndex(targets[i]))}' at index {targets[i]}");
                levels.DeleteArrayElementAtIndex(targets[i]);
                removed++;
            }

            ClampCurrentQuality(so, levels.arraySize);
            ClampPerPlatformDefaults(so, levels.arraySize, report);

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            return removed;
        }

        /// <summary>
        /// Reads the names of every quality level currently in the project, in order.
        /// Used by the Setup window to spell out what a replace would destroy.
        /// </summary>
        /// <returns>The level names, or an empty list if the settings asset cannot be read.</returns>
        internal static List<string> ListExistingLevelNames()
        {
            Object asset = LoadAsset();
            if (asset == null)
                return new List<string>();

            SerializedProperty levels = new SerializedObject(asset).FindProperty(k_LevelsProperty);
            if (levels == null || !levels.isArray)
                return new List<string>();

            return ListLevelNames(levels);
        }

        static List<string> ListLevelNames(SerializedProperty levels)
        {
            var names = new List<string>(levels.arraySize);
            for (int i = 0; i < levels.arraySize; i++)
                names.Add(ReadName(levels.GetArrayElementAtIndex(i)) ?? $"<unnamed {i}>");

            return names;
        }

        static void ClampCurrentQuality(SerializedObject so, int levelCount)
        {
            // The active level is a raw int, so shortening the list leaves it pointing
            // past the end or at somebody else's level.
            SerializedProperty current = so.FindProperty("m_CurrentQuality");
            if (current != null && levelCount > 0)
                current.intValue = Mathf.Clamp(current.intValue, 0, levelCount - 1);
        }

        static void ClampPerPlatformDefaults(SerializedObject so, int levelCount, SetupReport report)
        {
            // m_PerPlatformDefaultQuality decides which level a build actually ships with,
            // so it is normally left strictly alone. The one case where touching it is the
            // lesser evil is when the list just got shorter: an entry pointing past the end
            // is a build that ships with an undefined quality level, which is worse than a
            // clamp the user can see in the report.
            SerializedProperty map = so.FindProperty("m_PerPlatformDefaultQuality");
            if (map == null || !map.isArray || levelCount <= 0)
                return;

            for (int i = 0; i < map.arraySize; i++)
            {
                SerializedProperty pair = map.GetArrayElementAtIndex(i);
                SerializedProperty value = pair.FindPropertyRelative("second");
                if (value == null || value.propertyType != SerializedPropertyType.Integer)
                    continue;

                if (value.intValue < levelCount)
                    continue;

                SerializedProperty key = pair.FindPropertyRelative("first");
                string platform = key != null && key.propertyType == SerializedPropertyType.String
                    ? key.stringValue
                    : $"entry {i}";

                report.Note($"Default quality level for {platform} pointed at index {value.intValue}, " +
                            $"which no longer exists. Clamped to {levelCount - 1}.");
                value.intValue = levelCount - 1;
            }
        }

        /// <summary>
        /// Finds the indices of the three <c>MAQ</c> levels without modifying anything.
        /// </summary>
        /// <returns>
        /// Indices in tier order, with <c>-1</c> for a level that does not exist, or
        /// <c>null</c> if the settings asset could not be read.
        /// </returns>
        internal static int[] FindExistingIndices()
        {
            Object asset = LoadAsset();
            if (asset == null)
                return null;

            SerializedProperty levels = new SerializedObject(asset).FindProperty(k_LevelsProperty);
            if (levels == null || !levels.isArray)
                return null;

            var indices = new int[3];
            for (int tier = 0; tier < 3; tier++)
                indices[tier] = IndexOfLevel(levels, QualityTierPresets.QualityLevelNames[tier]);

            return indices;
        }

        static void WriteLevel(SerializedProperty level, int tier, string name,
            UniversalRenderPipelineAsset urpAsset, SetupReport report)
        {
            QualityTierPresets.QualityLevelPreset preset = QualityTierPresets.QualityLevel(tier);

            SerializedFieldHelper.TrySetRelative(level, "name", k_Owner, name);

            if (urpAsset != null)
                SerializedFieldHelper.TrySetRelative(level, "customRenderPipeline", k_Owner, urpAsset);
            else
                report.Problem($"Quality level '{name}' has no URP asset to point at; customRenderPipeline was left as it was.");

            SerializedFieldHelper.TrySetRelativeAny(level, k_MipmapLimitPaths, k_Owner, preset.globalTextureMipmapLimit);
            SerializedFieldHelper.TrySetRelative(level, "anisotropicTextures", k_Owner, preset.anisotropicTextures);
            SerializedFieldHelper.TrySetRelative(level, "lodBias", k_Owner, preset.lodBias);
            SerializedFieldHelper.TrySetRelative(level, "maximumLODLevel", k_Owner, preset.maximumLodLevel);
            SerializedFieldHelper.TrySetRelative(level, "skinWeights", k_Owner, preset.skinWeights);
            SerializedFieldHelper.TrySetRelative(level, "particleRaycastBudget", k_Owner, preset.particleRaycastBudget);
            SerializedFieldHelper.TrySetRelative(level, "softParticles", k_Owner, preset.softParticles);
            SerializedFieldHelper.TrySetRelative(level, "softVegetation", k_Owner, preset.softVegetation);
            SerializedFieldHelper.TrySetRelative(level, "realtimeReflectionProbes", k_Owner, preset.realtimeReflectionProbes);
            SerializedFieldHelper.TrySetRelative(level, "billboardsFaceCameraPosition", k_Owner, preset.billboardsFaceCameraPosition);
            SerializedFieldHelper.TrySetRelative(level, "vSyncCount", k_Owner, preset.vSyncCount);

            // Kept at 1 on every tier on purpose: this multiplies with the URP render
            // scale, and two independent resolution scalers produce blur nobody can
            // trace back to a setting.
            SerializedFieldHelper.TrySetRelative(level, "resolutionScalingFixedDPIFactor", k_Owner, preset.resolutionScalingFixedDpiFactor);

            SerializedFieldHelper.TrySetRelative(level, "shadowmaskMode", k_Owner, preset.shadowmaskMode);
            SerializedFieldHelper.TrySetRelative(level, "streamingMipmapsActive", k_Owner, preset.streamingMipmapsActive);
            SerializedFieldHelper.TrySetRelative(level, "antiAliasing", k_Owner, preset.antiAliasing);
        }

        static int IndexOfLevel(SerializedProperty levels, string name)
        {
            for (int i = 0; i < levels.arraySize; i++)
            {
                if (string.Equals(ReadName(levels.GetArrayElementAtIndex(i)), name, System.StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        static string ReadName(SerializedProperty level)
        {
            SerializedProperty name = level.FindPropertyRelative("name");
            return name != null ? name.stringValue : null;
        }

        static SerializedObject OpenQualitySettings(SetupReport report)
        {
            Object asset = LoadAsset();
            if (asset != null)
                return new SerializedObject(asset);

            report.Problem($"Could not open {k_AssetPath}; quality levels were not touched.");
            return null;
        }

        static Object LoadAsset()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(k_AssetPath);
            return assets != null && assets.Length > 0 ? assets[0] : null;
        }
    }
}
