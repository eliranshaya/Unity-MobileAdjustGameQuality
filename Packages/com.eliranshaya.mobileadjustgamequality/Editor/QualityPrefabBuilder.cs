using UnityEditor;
using UnityEngine;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Builds the one prefab you drag into your first scene.
    /// </summary>
    internal static class QualityPrefabBuilder
    {
        const string k_ProfileProperty = "profile";
        const string k_Owner = "MobileQualityManager.profile";

        /// <summary>
        /// Creates or overwrites the manager prefab and writes the real quality level index
        /// into it.
        /// </summary>
        /// <param name="qualityLevelIndex">
        /// The index the quality level landed on, or <c>-1</c> to leave the default.
        /// </param>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns>The saved prefab asset, or <c>null</c> on failure.</returns>
        internal static GameObject Build(int qualityLevelIndex, SetupReport report)
        {
            if (!QualityPreset.EnsureGeneratedFolder())
            {
                report.Problem($"Could not create {QualityPreset.GeneratedFolder}; the prefab was not built.");
                return null;
            }

            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(QualityPreset.PrefabPath) != null;

            // Built from a throwaway scene object rather than by editing the existing prefab
            // in place: it is the only way to guarantee the component set is exactly what
            // this version expects, with no leftovers from an older run. Awake is not called
            // at edit time, so the singleton logic does not run while this happens.
            var temp = new GameObject("MobileQualityManager");

            try
            {
                var manager = temp.AddComponent<MobileQualityManager>();
                WriteProfile(manager, qualityLevelIndex, report);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, QualityPreset.PrefabPath, out bool success);
                if (!success || prefab == null)
                {
                    report.Problem($"Unity refused to save the prefab at {QualityPreset.PrefabPath}.");
                    return null;
                }

                if (existed)
                    report.Update(QualityPreset.PrefabPath);
                else
                    report.Create(QualityPreset.PrefabPath);

                return prefab;
            }
            finally
            {
                // DestroyImmediate, not Destroy: outside play mode Destroy is deferred to the
                // end of the frame, which for an editor script means never.
                Object.DestroyImmediate(temp);
            }
        }

        static void WriteProfile(MobileQualityManager manager, int qualityLevelIndex, SetupReport report)
        {
            var so = new SerializedObject(manager);
            SerializedProperty profile = so.FindProperty(k_ProfileProperty);

            if (profile == null)
            {
                report.Problem($"'{k_ProfileProperty}' was not found on MobileQualityManager; " +
                               "the prefab was saved with the component's own defaults.");
                return;
            }

            // Only the quality level index is written here, and that is the point. Every
            // other value already arrived correct from the component's field initialiser,
            // which ran when AddComponent constructed it. A second copy of those numbers in
            // this assembly would silently win, and editing the defaults in the component
            // would appear to do nothing.
            //
            // The index cannot come from the component, because it is not a constant: it is
            // wherever the level actually landed, which depends on what the project had.
            if (qualityLevelIndex >= 0)
                SerializedFieldHelper.TrySetRelative(profile, "unityQualityLevel", k_Owner, qualityLevelIndex);

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
