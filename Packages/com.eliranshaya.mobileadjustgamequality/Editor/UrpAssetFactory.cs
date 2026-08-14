using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Creates and configures the three generated <c>UniversalRenderPipelineAsset</c>
    /// assets and links each one to its renderer.
    /// </summary>
    internal static class UrpAssetFactory
    {
        /// <summary>
        /// Creates the URP asset for a tier, or reconfigures it if it already exists.
        /// </summary>
        /// <param name="tier">Tier index, 0-2.</param>
        /// <param name="rendererData">The renderer this asset should use. May be <c>null</c>.</param>
        /// <param name="report">Run report to record the outcome in.</param>
        /// <returns>The configured asset, or <c>null</c> if it could not be created.</returns>
        internal static UniversalRenderPipelineAsset CreateOrUpdate(
            int tier, UniversalRendererData rendererData, SetupReport report)
        {
            string path = QualityTierPresets.UrpAssetPath(tier);
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);

            if (asset == null)
            {
                // Create() is URP's own factory: it runs the package's default resource
                // reload and initialises the renderer list. CreateInstance would leave
                // internal resource references null and produce an asset that renders
                // magenta until someone touches it in the inspector.
                asset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(asset, path);
                report.Create(path);
            }
            else
            {
                report.Update(path);
            }

            Configure(asset, tier, rendererData);
            return asset;
        }

        static void Configure(UniversalRenderPipelineAsset asset, int tier, UniversalRendererData rendererData)
        {
            QualityTierPresets.UrpPreset preset = QualityTierPresets.Urp(tier);
            var so = new SerializedObject(asset);

            // Quality
            //
            // Render scale is read from the runtime tier defaults rather than from a number
            // in this file. The manager overwrites it at boot anyway, so a second copy here
            // would not change what renders - it would only make the generated asset show a
            // value the game never uses, which is exactly the kind of quiet disagreement
            // that costs an afternoon to track down.
            SerializedFieldHelper.TrySet(so, "m_RenderScale", MobileQualityManager.CreateDefaultTiers()[tier].baseRenderScale);
            SerializedFieldHelper.TrySet(so, "m_UpscalingFilter", preset.upscalingFilter);
            SerializedFieldHelper.TrySet(so, "m_SupportsHDR", preset.supportsHdr);
            SerializedFieldHelper.TrySet(so, "m_MSAA", preset.msaa);

            // Textures the renderer would otherwise have to produce every frame. Both cost
            // a full-screen copy on mobile bandwidth, so both stay off on every tier.
            SerializedFieldHelper.TrySet(so, "m_RequireDepthTexture", preset.requireDepthTexture);
            SerializedFieldHelper.TrySet(so, "m_RequireOpaqueTexture", preset.requireOpaqueTexture);
            SerializedFieldHelper.TrySet(so, "m_EnableLODCrossFade", preset.enableLodCrossFade);

            // Lighting
            SerializedFieldHelper.TrySet(so, "m_MainLightRenderingMode", preset.mainLightRenderingMode);
            SerializedFieldHelper.TrySet(so, "m_MainLightShadowsSupported", preset.mainLightShadows);
            SerializedFieldHelper.TrySet(so, "m_MainLightShadowmapResolution", preset.mainLightShadowmapResolution);
            SerializedFieldHelper.TrySet(so, "m_AdditionalLightsRenderingMode", preset.additionalLightsRenderingMode);
            SerializedFieldHelper.TrySet(so, "m_AdditionalLightsPerObjectLimit", preset.additionalLightsPerObjectLimit);
            SerializedFieldHelper.TrySet(so, "m_AdditionalLightShadowsSupported", preset.additionalLightShadows);
            SerializedFieldHelper.TrySet(so, "m_AdditionalLightsShadowmapResolution", preset.additionalLightsShadowmapResolution);

            // Reflection probes
            SerializedFieldHelper.TrySet(so, "m_ReflectionProbeBlending", preset.reflectionProbeBlending);
            SerializedFieldHelper.TrySet(so, "m_ReflectionProbeBoxProjection", preset.reflectionProbeBoxProjection);

            // Shadows
            SerializedFieldHelper.TrySet(so, "m_ShadowDistance", preset.shadowDistance);
            SerializedFieldHelper.TrySet(so, "m_ShadowCascadeCount", preset.shadowCascadeCount);
            SerializedFieldHelper.TrySet(so, "m_CascadeBorder", preset.cascadeBorder);
            SerializedFieldHelper.TrySet(so, "m_SoftShadowsSupported", preset.softShadows);

            // Light cookies
            SerializedFieldHelper.TrySet(so, "m_SupportsLightCookies", preset.supportsLightCookies);
            SerializedFieldHelper.TrySet(so, "m_AdditionalLightsCookieResolution", preset.additionalLightsCookieResolution);
            SerializedFieldHelper.TrySet(so, "m_AdditionalLightsCookieFormat", preset.additionalLightsCookieFormat);

            // Post-processing
            SerializedFieldHelper.TrySet(so, "m_ColorGradingMode", preset.colorGradingMode);
            SerializedFieldHelper.TrySet(so, "m_ColorGradingLutSize", preset.colorGradingLutSize);

            // Batching and misc
            SerializedFieldHelper.TrySet(so, "m_UseSRPBatcher", preset.useSrpBatcher);
            SerializedFieldHelper.TrySet(so, "m_SupportsDynamicBatching", preset.supportsDynamicBatching);
            SerializedFieldHelper.TrySet(so, "m_VolumeFrameworkUpdateMode", preset.volumeFrameworkUpdateMode);
            SerializedFieldHelper.TrySet(so, "m_SupportDataDrivenLensFlare", preset.supportDataDrivenLensFlare);
            SerializedFieldHelper.TrySet(so, "m_UseFastSRGBLinearConversion", preset.useFastSrgbLinearConversion);

            LinkRenderer(so, rendererData);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void LinkRenderer(SerializedObject so, UniversalRendererData rendererData)
        {
            if (rendererData == null)
                return;

            SerializedProperty list = so.FindProperty("m_RendererDataList");
            if (list == null || !list.isArray)
            {
                SerializedFieldHelper.TrySet(so, "m_RendererDataList", rendererData);
                return;
            }

            if (list.arraySize == 0)
                list.InsertArrayElementAtIndex(0);

            SerializedFieldHelper.TrySet(
                list.GetArrayElementAtIndex(0),
                "UniversalRenderPipelineAsset.m_RendererDataList[0]",
                rendererData);

            SerializedFieldHelper.TrySet(so, "m_DefaultRendererIndex", 0);
        }
    }
}
