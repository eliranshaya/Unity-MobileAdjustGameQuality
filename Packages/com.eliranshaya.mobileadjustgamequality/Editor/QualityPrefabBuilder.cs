using UnityEditor;
using UnityEngine;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Builds the one prefab the user has to drag into their first scene.
    /// </summary>
    internal static class QualityPrefabBuilder
    {
        const string k_TiersProperty = "tiers";
        const string k_Owner = "MobileQualityManager.tiers";

        /// <summary>
        /// Creates or overwrites the manager prefab and writes the real quality level
        /// indices into its tier array.
        /// </summary>
        /// <param name="qualityLevelIndices">
        /// The three appended quality level indices in tier order. Pass <c>null</c> to
        /// keep the preset defaults, which is only correct when the levels were not written.
        /// </param>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns>The saved prefab asset, or <c>null</c> on failure.</returns>
        internal static GameObject Build(int[] qualityLevelIndices, SetupReport report)
        {
            if (!QualityTierPresets.EnsureGeneratedFolder())
            {
                report.Problem($"Could not create {QualityTierPresets.GeneratedFolder}; the prefab was not built.");
                return null;
            }

            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(QualityTierPresets.PrefabPath) != null;

            // Built from a throwaway scene object rather than by editing the existing
            // prefab in place: it is the only way to guarantee the component set is
            // exactly what this version of the package expects, with no leftovers from
            // an older run.
            var temp = new GameObject("MobileQualityManager");

            try
            {
                // One component, deliberately. The frame time window, the thermal sensor
                // and the decision policy are plain classes the manager owns, so there is
                // one execution order and one lifetime rather than three that have to
                // agree with each other.
                var manager = temp.AddComponent<MobileQualityManager>();

                WriteTiers(manager, qualityLevelIndices, report);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, QualityTierPresets.PrefabPath, out bool success);
                if (!success || prefab == null)
                {
                    report.Problem($"Unity refused to save the prefab at {QualityTierPresets.PrefabPath}.");
                    return null;
                }

                if (existed)
                    report.Update(QualityTierPresets.PrefabPath);
                else
                    report.Create(QualityTierPresets.PrefabPath);

                return prefab;
            }
            finally
            {
                // DestroyImmediate, not Destroy: outside play mode Destroy is deferred to
                // the end of the frame, which for an editor script means never.
                Object.DestroyImmediate(temp);
            }
        }

        static void WriteTiers(MobileQualityManager manager, int[] qualityLevelIndices, SetupReport report)
        {
            var so = new SerializedObject(manager);
            SerializedProperty tiers = so.FindProperty(k_TiersProperty);

            if (tiers == null || !tiers.isArray)
            {
                report.Problem($"'{k_TiersProperty}' was not found on MobileQualityManager; " +
                               "the prefab was saved with whatever defaults the component had.");
                return;
            }

            tiers.arraySize = 3;

            for (int tier = 0; tier < 3; tier++)
            {
                QualityTierPresets.RuntimePreset preset = QualityTierPresets.Runtime(tier);
                SerializedProperty element = tiers.GetArrayElementAtIndex(tier);

                // The whole point of writing this from the Setup: the quality level index
                // is wherever the levels happened to be appended, which depends on how
                // many levels the project already had.
                if (qualityLevelIndices != null && tier < qualityLevelIndices.Length && qualityLevelIndices[tier] >= 0)
                    SerializedFieldHelper.TrySetRelative(element, "unityQualityLevel", k_Owner, qualityLevelIndices[tier]);

                SerializedFieldHelper.TrySetRelative(element, "targetFrameRate", k_Owner, preset.targetFrameRate);
                SerializedFieldHelper.TrySetRelative(element, "baseRenderScale", k_Owner, preset.baseRenderScale);
                SerializedFieldHelper.TrySetRelative(element, "minRenderScale", k_Owner, preset.minRenderScale);
                SerializedFieldHelper.TrySetRelative(element, "maxRenderScale", k_Owner, preset.maxRenderScale);
                SerializedFieldHelper.TrySetRelative(element, "thermalFallbackFrameRate", k_Owner, preset.thermalFallbackFrameRate);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
