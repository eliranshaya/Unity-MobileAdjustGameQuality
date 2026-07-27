using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Creates and configures the three generated <c>UniversalRendererData</c> assets.
    /// </summary>
    internal static class RendererDataFactory
    {
        /// <summary>
        /// Creates the renderer for a tier, or reconfigures it if it already exists.
        /// </summary>
        /// <param name="tier">Tier index, 0-2.</param>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns>The configured asset, or <c>null</c> if it could not be created.</returns>
        internal static UniversalRendererData CreateOrUpdate(int tier, SetupReport report)
        {
            string path = QualityTierPresets.RendererPath(tier);
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);

            if (data == null)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, path);
                report.Create(path);
            }
            else
            {
                report.Update(path);
            }

            Configure(data, tier, report);
            return data;
        }

        static void Configure(UniversalRendererData data, int tier, SetupReport report)
        {
            QualityTierPresets.RendererPreset preset = QualityTierPresets.Renderer(tier);
            var so = new SerializedObject(data);

            // Resolved by name, never by index: ForwardPlus and DeferredPlus were inserted
            // into the middle of RenderingMode after URP 14, so Forward is 0 today but the
            // ordering is not something to bet a generated asset on.
            SerializedFieldHelper.TrySetEnumByName(so, "m_RenderingMode", "Forward");
            SerializedFieldHelper.TrySetEnumByName(so, "m_DepthPrimingMode", "Disabled");
            SerializedFieldHelper.TrySetEnumByName(so, "m_CopyDepthMode", "AfterOpaques");
            SerializedFieldHelper.TrySetEnumByName(so, "m_IntermediateTextureMode", "Auto");

            // m_UseNativeRenderPass lives on the ScriptableRendererData base class rather
            // than on UniversalRendererData, but the serialized path is flat either way.
            SerializedFieldHelper.TrySet(so, "m_UseNativeRenderPass", preset.useNativeRenderPass);
            SerializedFieldHelper.TrySet(so, "m_ShadowTransparentReceive", preset.shadowTransparentReceive);
            SerializedFieldHelper.TrySet(so, "m_OpaqueLayerMask", preset.opaqueLayerMask);
            SerializedFieldHelper.TrySet(so, "m_TransparentLayerMask", preset.transparentLayerMask);
            SerializedFieldHelper.TrySet(so, "m_DefaultStencilState.overrideStencilState", false);

            ApplyPostProcessData(so, preset.assignPostProcessData, tier, report);

            // WithoutUndo: the Setup window is not an inspector edit, and putting six
            // generated assets on the undo stack invites a half-undone configuration.
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }

        static void ApplyPostProcessData(SerializedObject so, bool assign, int tier, SetupReport report)
        {
            // A null PostProcessData is what actually disables post-processing on a
            // renderer - there is no separate toggle. Low and Balanced therefore clear it.
            if (!assign)
            {
                SerializedFieldHelper.TrySetAny(so, k_PostProcessDataPaths, null);
                return;
            }

            Object postProcessData = FindUrpPostProcessData();
            if (postProcessData == null)
            {
                report.Problem(
                    $"Could not locate URP's built-in PostProcessData asset, so " +
                    $"{QualityTierPresets.TierNames[tier]} renderer was left without post-processing. " +
                    "Assign it by hand on the renderer asset if you need post effects on this tier.");
                return;
            }

            SerializedFieldHelper.TrySetAny(so, k_PostProcessDataPaths, postProcessData);
        }

        // URP 17 exposes this as a public field named postProcessData; older versions used
        // the private m_PostProcessData. Try both rather than branching on version.
        static readonly string[] k_PostProcessDataPaths = { "postProcessData", "m_PostProcessData" };

        static Object FindUrpPostProcessData()
        {
            string[] guids = AssetDatabase.FindAssets("t:PostProcessData");
            Object fallback = null;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset == null)
                    continue;

                // Prefer the copy shipped inside the URP package over any duplicate a
                // project may have created in Assets/, which is usually a stale clone.
                if (path.StartsWith("Packages/com.unity.render-pipelines.universal", System.StringComparison.Ordinal))
                    return asset;

                if (fallback == null)
                    fallback = asset;
            }

            return fallback;
        }
    }
}
