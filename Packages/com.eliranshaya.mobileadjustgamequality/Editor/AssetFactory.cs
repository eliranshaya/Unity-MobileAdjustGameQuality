using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Creates the two generated rendering assets: one renderer and one URP asset.
    /// </summary>
    internal static class AssetFactory
    {
        // URP 17 exposes this as a public field named postProcessData; older versions used
        // the private m_PostProcessData. Try both rather than branching on version.
        static readonly string[] k_PostProcessDataPaths = { "postProcessData", "m_PostProcessData" };

        /// <summary>
        /// Creates the renderer, or reconfigures it if it already exists.
        /// </summary>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns>The configured asset, or <c>null</c> if it could not be created.</returns>
        internal static UniversalRendererData CreateOrUpdateRenderer(SetupReport report)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(QualityPreset.RendererPath);

            if (data == null)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, QualityPreset.RendererPath);
                report.Create(QualityPreset.RendererPath);
            }
            else
            {
                report.Update(QualityPreset.RendererPath);
            }

            var so = new SerializedObject(data);

            // Resolved by name, never by index: ForwardPlus and DeferredPlus were inserted
            // into the middle of RenderingMode after URP 14, so Forward is 0 today but that
            // is not something to bet a generated asset on.
            SerializedFieldHelper.TrySetEnumByName(so, "m_RenderingMode", "Forward");
            SerializedFieldHelper.TrySetEnumByName(so, "m_DepthPrimingMode", "Disabled");
            SerializedFieldHelper.TrySetEnumByName(so, "m_CopyDepthMode", "AfterOpaques");
            SerializedFieldHelper.TrySetEnumByName(so, "m_IntermediateTextureMode", "Auto");

            foreach ((string path, object value) in QualityPreset.RendererFields)
                SerializedFieldHelper.TrySet(so, path, value);

            // A null postProcessData is what disables post-processing on a renderer; there is
            // no separate toggle. Keeping it enabled, because Adaptive Performance can scale
            // post-processing down at runtime and cannot turn it back on.
            SerializedFieldHelper.TrySetAny(so, k_PostProcessDataPaths, FindPostProcessData());

            // WithoutUndo: the Setup window is not an inspector edit, and putting generated
            // assets on the undo stack invites a half-undone configuration.
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            return data;
        }

        /// <summary>
        /// Creates the URP asset, or reconfigures it if it already exists.
        /// </summary>
        /// <param name="renderer">The renderer this asset should use.</param>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns>The configured asset, or <c>null</c> if it could not be created.</returns>
        internal static UniversalRenderPipelineAsset CreateOrUpdateUrpAsset(
            UniversalRendererData renderer, SetupReport report)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QualityPreset.UrpAssetPath);

            if (asset == null)
            {
                // Create() is URP's own factory: it runs the package's default resource
                // reload. CreateInstance would leave internal references null and produce an
                // asset that renders magenta until someone touches it in the inspector.
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, QualityPreset.UrpAssetPath);
                report.Create(QualityPreset.UrpAssetPath);
            }
            else
            {
                report.Update(QualityPreset.UrpAssetPath);
            }

            var so = new SerializedObject(asset);

            foreach ((string path, object value) in QualityPreset.UrpFields)
                SerializedFieldHelper.TrySet(so, path, value);

            LinkRenderer(so, renderer);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            return asset;
        }

        static void LinkRenderer(SerializedObject so, UniversalRendererData renderer)
        {
            if (renderer == null)
                return;

            SerializedProperty list = so.FindProperty("m_RendererDataList");
            if (list == null || !list.isArray)
            {
                SerializedFieldHelper.TrySet(so, "m_RendererDataList", renderer);
                return;
            }

            if (list.arraySize == 0)
                list.InsertArrayElementAtIndex(0);

            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            SerializedFieldHelper.TrySet(so, "m_DefaultRendererIndex", 0);
        }

        static UnityEngine.Object FindPostProcessData()
        {
            string[] guids = AssetDatabase.FindAssets("t:PostProcessData");
            UnityEngine.Object fallback = null;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset == null)
                    continue;

                // Prefer the copy inside the URP package over a duplicate in Assets/, which
                // is usually a stale clone.
                if (path.StartsWith("Packages/com.unity.render-pipelines.universal", StringComparison.Ordinal))
                    return asset;

                fallback ??= asset;
            }

            return fallback;
        }
    }
}
