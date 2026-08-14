using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// UnityEngine has its own legacy ShadowResolution enum (Low/Medium/High/VeryHigh) that has
// nothing to do with URP's pixel-sized one. Aliasing makes it impossible to pick the wrong
// one by accident.
using UrpShadowResolution = UnityEngine.Rendering.Universal.ShadowResolution;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// The single mobile quality configuration this package generates, plus the paths and
    /// names the rest of the Setup agrees on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every enum value is written as a cast of the real URP enum
    /// (<c>(int)MsaaQuality.Disabled</c>) rather than a literal, so the compiler resolves it
    /// against the URP version actually installed. A renumbering becomes a build error
    /// instead of a silently wrong asset. URP 17 makes the point:
    /// <c>LightRenderingMode.PerPixel</c> is 1 and <c>PerVertex</c> is 2, the opposite of the
    /// inspector ordering, and <c>MsaaQuality.Disabled</c> is 1 rather than 0.
    /// </para>
    /// <para>
    /// One configuration, not three. Tiers only pay for themselves if something switches
    /// between them at runtime, and switching a URP asset rebuilds the pipeline. Adaptive
    /// Performance adapts inside a single configuration instead, which is why there is only
    /// one here.
    /// </para>
    /// </remarks>
    internal static class QualityPreset
    {
        /// <summary>The only folder this package generates into.</summary>
        internal const string GeneratedFolder = "Assets/MobileAdjustGameQuality/Generated";

        /// <summary>Name of the generated quality level.</summary>
        internal const string QualityLevelName = "MAQ Mobile";

        /// <summary>Path of the generated URP asset.</summary>
        internal const string UrpAssetPath = GeneratedFolder + "/MAQ-URP.asset";

        /// <summary>Path of the generated renderer data.</summary>
        internal const string RendererPath = GeneratedFolder + "/MAQ-Renderer.asset";

        /// <summary>Path of the generated manager prefab.</summary>
        internal const string PrefabPath = GeneratedFolder + "/MobileQualityManager.prefab";

        /// <summary>How far the main light casts shadows, in metres.</summary>
        const float k_ShadowDistance = 30f;

        /// <summary>
        /// How long the fade-out at the end of the shadow range is, in metres - the
        /// inspector's "Last Border".
        /// </summary>
        const float k_ShadowLastBorderMeters = 2f;

        /// <summary>
        /// "Last Border" as URP actually stores it.
        /// </summary>
        /// <remarks>
        /// <c>m_CascadeBorder</c> is a <b>normalised 0-1 fraction</b> of the last cascade's
        /// length, not a distance. The inspector multiplies it by the shadow distance before
        /// showing you metres, which is why writing the number you read in the inspector
        /// produces a border sixty metres long. Derived here so the two stay in agreement if
        /// the shadow distance ever changes.
        /// </remarks>
        static float CascadeBorder => k_ShadowLastBorderMeters / k_ShadowDistance;

        /// <summary>Values written into the generated <c>UniversalRenderPipelineAsset</c>.</summary>
        /// <remarks>
        /// Tuned as a sensible mobile middle. Adaptive Performance moves several of these
        /// down at runtime when the device needs it, so the baseline can afford to be the
        /// quality you actually want rather than the quality the worst device can hold.
        /// </remarks>
        internal static readonly (string path, object value)[] UrpFields =
        {
            ("m_RenderScale", 0.8f),
            ("m_UpscalingFilter", (int)UpscalingFilterSelection.Auto),

            // HDR and MSAA are off on mobile: on a phone-sized screen both cost bandwidth
            // out of proportion to what they give back.
            ("m_SupportsHDR", false),
            ("m_MSAA", (int)MsaaQuality.Disabled),

            // Both of these force a full-screen copy every frame.
            ("m_RequireDepthTexture", false),
            ("m_RequireOpaqueTexture", false),

            ("m_EnableLODCrossFade", false),

            ("m_MainLightRenderingMode", (int)LightRenderingMode.PerPixel),
            ("m_MainLightShadowsSupported", true),
            ("m_MainLightShadowmapResolution", (int)UrpShadowResolution._1024),

            // Additional lights off entirely. This is the single biggest saving available on
            // mobile: it removes the per-object light loop from every lit shader rather than
            // just limiting how many lights it runs.
            ("m_AdditionalLightsRenderingMode", (int)LightRenderingMode.Disabled),
            ("m_AdditionalLightsPerObjectLimit", 0),
            ("m_AdditionalLightShadowsSupported", false),
            ("m_AdditionalLightsShadowmapResolution", (int)UrpShadowResolution._256),

            ("m_ReflectionProbeBlending", false),
            ("m_ReflectionProbeBoxProjection", false),

            // Shadows
            ("m_ShadowDistance", k_ShadowDistance),
            ("m_ShadowCascadeCount", 1),
            ("m_CascadeBorder", k_ShadowLastBorderMeters / k_ShadowDistance),
            ("m_ShadowDepthBias", 1f),
            ("m_ShadowNormalBias", 0f),
            ("m_SoftShadowsSupported", true),
            ("m_SoftShadowQuality", (int)SoftShadowQuality.Medium),

            ("m_SupportsLightCookies", false),
            ("m_AdditionalLightsCookieResolution", (int)LightCookieResolution._256),
            ("m_AdditionalLightsCookieFormat", (int)LightCookieFormat.GrayscaleLow),

            ("m_ColorGradingMode", (int)ColorGradingMode.LowDynamicRange),
            ("m_ColorGradingLutSize", 32),

            ("m_UseSRPBatcher", true),
            ("m_SupportsDynamicBatching", false),
            ("m_VolumeFrameworkUpdateMode", (int)VolumeFrameworkUpdateMode.EveryFrame),

            // Off because they are unused, and each one is a shader keyword that would
            // otherwise be compiled into every variant that might need it.
            ("m_SupportsTerrainHoles", false),
            ("m_SupportDataDrivenLensFlare", false),
            ("m_SupportScreenSpaceLensFlare", false),

            ("m_UseFastSRGBLinearConversion", false)
        };

        /// <summary>Values written into the generated <c>UniversalRendererData</c>.</summary>
        internal static readonly (string path, object value)[] RendererFields =
        {
            ("m_UseNativeRenderPass", true),
            ("m_ShadowTransparentReceive", false),
            ("m_OpaqueLayerMask", -1),
            ("m_TransparentLayerMask", -1),
            ("m_DefaultStencilState.overrideStencilState", false)
        };

        /// <summary>Values written into the generated Quality Level.</summary>
        internal static readonly (string path, object value)[] QualityLevelFields =
        {
            ("anisotropicTextures", (int)AnisotropicFiltering.Enable),
            ("lodBias", 1f),
            ("maximumLODLevel", 0),
            ("skinWeights", 2),

            // Zero, because nothing here uses it. This is the per-frame raycast budget that
            // Particle Systems share when their Collision module is set to Medium or Low -
            // the approximate modes. With collision off, or set to High, it is never read at
            // all. Raise it if you ever enable approximate particle collision, or those
            // collisions will simply never resolve.
            ("particleRaycastBudget", 0),

            ("softParticles", false),

            // Terrain detail meshes and terrain billboards. No terrain, nothing to draw.
            ("softVegetation", false),
            ("billboardsFaceCameraPosition", false),

            ("realtimeReflectionProbes", false),
            ("vSyncCount", 0),

            // Left at 1 deliberately: this multiplies with the URP render scale, and two
            // independent resolution scalers produce blur nobody can trace to a setting.
            ("resolutionScalingFixedDPIFactor", 1),

            ("shadowmaskMode", 0),
            ("streamingMipmapsActive", false),
            ("antiAliasing", 0)
        };

        /// <summary>Renamed in Unity 2022.2; older projects still use <c>textureQuality</c>.</summary>
        internal static readonly string[] MipmapLimitPaths = { "globalTextureMipmapLimit", "textureQuality" };

        /// <summary>The installed URP package version, or <c>null</c> when it cannot be read.</summary>
        internal static string UrpVersion => ResolveUrp()?.version;

        /// <summary>True when the Universal Render Pipeline package is installed.</summary>
        internal static bool IsUrpInstalled => ResolveUrp() != null;

        /// <summary>True when Unity's Adaptive Performance package is installed.</summary>
        internal static bool IsAdaptivePerformanceInstalled
        {
#if MAQ_ADAPTIVE_PERFORMANCE
            get => true;
#else
            get => false;
#endif
        }

        /// <summary>Every asset path this package generates.</summary>
        internal static IEnumerable<string> GeneratedPaths
        {
            get
            {
                yield return RendererPath;
                yield return UrpAssetPath;
                yield return PrefabPath;
            }
        }

        /// <summary>
        /// Creates <see cref="GeneratedFolder"/> and any missing parent.
        /// </summary>
        /// <returns><c>true</c> if the folder exists after the call.</returns>
        internal static bool EnsureGeneratedFolder()
        {
            if (AssetDatabase.IsValidFolder(GeneratedFolder))
                return true;

            const string root = "Assets/MobileAdjustGameQuality";
            if (!AssetDatabase.IsValidFolder(root))
                AssetDatabase.CreateFolder("Assets", "MobileAdjustGameQuality");

            AssetDatabase.CreateFolder(root, "Generated");
            return AssetDatabase.IsValidFolder(GeneratedFolder);
        }

        static UnityEditor.PackageManager.PackageInfo ResolveUrp() =>
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UniversalRenderPipelineAsset).Assembly);
    }
}
