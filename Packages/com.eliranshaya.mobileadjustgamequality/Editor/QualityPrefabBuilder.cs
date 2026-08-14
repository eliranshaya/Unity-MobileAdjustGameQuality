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

            // Only the quality level index is written here, and that is the point.
            //
            // Every other tier value already arrived correct: the prefab is built from a
            // fresh AddComponent, so MobileQualityManager's field initializer has just run
            // and filled the array with its own defaults. Writing them again from a second
            // table in this assembly would mean two places holding the same numbers, and
            // the editor copy silently winning - so editing the defaults in the component
            // would appear to do nothing, which is exactly the bug this replaces.
            //
            // The index cannot come from the component, because it is not a constant:
            // it is wherever the three levels actually landed, which depends on what the
            // project already had and on whether Setup replaced or appended.
            if (qualityLevelIndices == null)
                return;

            for (int tier = 0; tier < 3; tier++)
            {
                if (tier >= qualityLevelIndices.Length || qualityLevelIndices[tier] < 0)
                    continue;

                SerializedFieldHelper.TrySetRelative(
                    tiers.GetArrayElementAtIndex(tier), "unityQualityLevel", k_Owner, qualityLevelIndices[tier]);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
