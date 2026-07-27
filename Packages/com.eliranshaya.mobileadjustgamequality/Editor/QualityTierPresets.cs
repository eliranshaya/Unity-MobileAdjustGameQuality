using UnityEngine;
using UnityEngine.Rendering.Universal;

// UnityEngine has its own legacy ShadowResolution enum (Low/Medium/High/VeryHigh) that
// has nothing to do with URP's pixel-sized one. Aliasing makes it impossible to pick the
// wrong one by accident.
using UrpShadowResolution = UnityEngine.Rendering.Universal.ShadowResolution;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// The target configuration for the three tiers, plus the asset paths and names the
    /// rest of the Setup agrees on.
    /// </summary>
    /// <remarks>
    /// Every enum value here is written as a cast of the real URP enum
    /// (<c>(int)MsaaQuality.Disabled</c>) rather than as a literal. That is deliberate:
    /// the compiler then resolves the number against the URP version actually installed,
    /// so a renumbering between URP versions is a build error at worst instead of a
    /// silently wrong asset. URP 17 makes the point - <c>LightRenderingMode.PerVertex</c>
    /// is 2 and <c>PerPixel</c> is 1, the opposite of what a reasonable person would
    /// assume from the inspector ordering.
    /// </remarks>
    internal static class QualityTierPresets
    {
        /// <summary>The only folder this package is allowed to generate assets into.</summary>
        internal const string GeneratedFolder = "Assets/MobileAdjustGameQuality/Generated";

        /// <summary>Path of the generated manager prefab.</summary>
        internal const string PrefabPath = GeneratedFolder + "/MobileQualityManager.prefab";

        /// <summary>Suffix used for the three tiers, in <see cref="QualityTier"/> order.</summary>
        internal static readonly string[] TierNames = { "Low", "Balanced", "High" };

        /// <summary>Quality level names, in <see cref="QualityTier"/> order.</summary>
        internal static readonly string[] QualityLevelNames = { "MAQ Low", "MAQ Balanced", "MAQ High" };

        /// <summary>Path of the generated URP asset for a tier.</summary>
        /// <param name="tier">Tier index, 0-2.</param>
        /// <returns>An asset path inside <see cref="GeneratedFolder"/>.</returns>
        internal static string UrpAssetPath(int tier) => $"{GeneratedFolder}/MAQ-URP-{TierNames[tier]}.asset";

        /// <summary>Path of the generated renderer data for a tier.</summary>
        /// <param name="tier">Tier index, 0-2.</param>
        /// <returns>An asset path inside <see cref="GeneratedFolder"/>.</returns>
        internal static string RendererPath(int tier) => $"{GeneratedFolder}/MAQ-{TierNames[tier]}-Renderer.asset";

        /// <summary>The values written into a generated <c>UniversalRenderPipelineAsset</c>.</summary>
        internal class UrpPreset
        {
            internal float renderScale;
            internal int upscalingFilter;
            internal bool supportsHdr;
            internal int msaa;
            internal bool requireDepthTexture;
            internal bool requireOpaqueTexture;
            internal bool enableLodCrossFade;
            internal int mainLightRenderingMode;
            internal bool mainLightShadows;
            internal int mainLightShadowmapResolution;
            internal int additionalLightsRenderingMode;
            internal int additionalLightsPerObjectLimit;
            internal bool additionalLightShadows;
            internal int additionalLightsShadowmapResolution;
            internal bool reflectionProbeBlending;
            internal bool reflectionProbeBoxProjection;
            internal float shadowDistance;
            internal int shadowCascadeCount;
            internal float cascadeBorder;
            internal bool softShadows;
            internal bool supportsLightCookies;
            internal int additionalLightsCookieResolution;
            internal int additionalLightsCookieFormat;
            internal int colorGradingMode;
            internal int colorGradingLutSize;
            internal bool useSrpBatcher;
            internal bool supportsDynamicBatching;
            internal int volumeFrameworkUpdateMode;
            internal bool supportDataDrivenLensFlare;
            internal bool useFastSrgbLinearConversion;
        }

        /// <summary>The values written into a generated <c>UniversalRendererData</c>.</summary>
        internal class RendererPreset
        {
            internal bool useNativeRenderPass;
            internal bool shadowTransparentReceive;
            internal int opaqueLayerMask;
            internal int transparentLayerMask;
            internal bool assignPostProcessData;
        }

        /// <summary>The values written into an appended Quality Level.</summary>
        internal class QualityLevelPreset
        {
            internal int globalTextureMipmapLimit;
            internal int anisotropicTextures;
            internal float lodBias;
            internal int maximumLodLevel;
            internal int skinWeights;
            internal int particleRaycastBudget;
            internal bool softParticles;
            internal bool softVegetation;
            internal bool realtimeReflectionProbes;
            internal bool billboardsFaceCameraPosition;
            internal int vSyncCount;
            internal int resolutionScalingFixedDpiFactor;
            internal int shadowmaskMode;
            internal bool streamingMipmapsActive;
            internal int antiAliasing;
        }

        /// <summary>The values written into the prefab's tier array.</summary>
        internal class RuntimePreset
        {
            internal int targetFrameRate;
            internal float baseRenderScale;
            internal float minRenderScale;
            internal float maxRenderScale;
            internal int thermalFallbackFrameRate;
        }

        static readonly UrpPreset[] s_Urp =
        {
            // Low
            new UrpPreset
            {
                renderScale = 0.75f,
                upscalingFilter = (int)UpscalingFilterSelection.Linear,
                supportsHdr = false,
                msaa = (int)MsaaQuality.Disabled,
                requireDepthTexture = false,
                requireOpaqueTexture = false,
                enableLodCrossFade = false,
                mainLightRenderingMode = (int)LightRenderingMode.PerPixel,
                mainLightShadows = false,
                mainLightShadowmapResolution = (int)UrpShadowResolution._512,
                additionalLightsRenderingMode = (int)LightRenderingMode.PerVertex,
                additionalLightsPerObjectLimit = 1,
                additionalLightShadows = false,
                additionalLightsShadowmapResolution = (int)UrpShadowResolution._256,
                reflectionProbeBlending = false,
                reflectionProbeBoxProjection = false,
                shadowDistance = 20f,
                shadowCascadeCount = 1,
                cascadeBorder = 0.1f,
                softShadows = false,
                supportsLightCookies = false,
                additionalLightsCookieResolution = (int)LightCookieResolution._256,
                additionalLightsCookieFormat = (int)LightCookieFormat.GrayscaleLow,
                colorGradingMode = (int)ColorGradingMode.LowDynamicRange,
                colorGradingLutSize = 16,
                useSrpBatcher = true,
                supportsDynamicBatching = true,
                volumeFrameworkUpdateMode = (int)VolumeFrameworkUpdateMode.ViaScripting,
                supportDataDrivenLensFlare = false,
                useFastSrgbLinearConversion = false
            },

            // Balanced
            new UrpPreset
            {
                renderScale = 0.85f,
                upscalingFilter = (int)UpscalingFilterSelection.Linear,
                supportsHdr = false,
                msaa = (int)MsaaQuality.Disabled,
                requireDepthTexture = false,
                requireOpaqueTexture = false,
                enableLodCrossFade = false,
                mainLightRenderingMode = (int)LightRenderingMode.PerPixel,
                mainLightShadows = true,
                mainLightShadowmapResolution = (int)UrpShadowResolution._512,
                additionalLightsRenderingMode = (int)LightRenderingMode.PerVertex,
                additionalLightsPerObjectLimit = 2,
                additionalLightShadows = false,
                additionalLightsShadowmapResolution = (int)UrpShadowResolution._256,
                reflectionProbeBlending = false,
                reflectionProbeBoxProjection = false,
                shadowDistance = 25f,
                shadowCascadeCount = 1,
                cascadeBorder = 0.1f,
                softShadows = false,
                supportsLightCookies = false,
                additionalLightsCookieResolution = (int)LightCookieResolution._256,
                additionalLightsCookieFormat = (int)LightCookieFormat.GrayscaleLow,
                colorGradingMode = (int)ColorGradingMode.LowDynamicRange,
                colorGradingLutSize = 32,
                useSrpBatcher = true,
                supportsDynamicBatching = true,
                volumeFrameworkUpdateMode = (int)VolumeFrameworkUpdateMode.EveryFrame,
                supportDataDrivenLensFlare = false,
                useFastSrgbLinearConversion = false
            },

            // High
            new UrpPreset
            {
                renderScale = 1f,
                upscalingFilter = (int)UpscalingFilterSelection.Auto,
                supportsHdr = false,
                msaa = (int)MsaaQuality.Disabled,
                requireDepthTexture = false,
                requireOpaqueTexture = false,
                enableLodCrossFade = true,
                mainLightRenderingMode = (int)LightRenderingMode.PerPixel,
                mainLightShadows = true,
                mainLightShadowmapResolution = (int)UrpShadowResolution._1024,
                additionalLightsRenderingMode = (int)LightRenderingMode.PerPixel,
                additionalLightsPerObjectLimit = 4,
                additionalLightShadows = true,
                additionalLightsShadowmapResolution = (int)UrpShadowResolution._512,
                reflectionProbeBlending = true,
                reflectionProbeBoxProjection = true,
                shadowDistance = 40f,
                shadowCascadeCount = 2,
                cascadeBorder = 0.1f,
                softShadows = true,
                supportsLightCookies = true,
                additionalLightsCookieResolution = (int)LightCookieResolution._512,
                additionalLightsCookieFormat = (int)LightCookieFormat.ColorLow,
                colorGradingMode = (int)ColorGradingMode.LowDynamicRange,
                colorGradingLutSize = 32,
                useSrpBatcher = true,
                supportsDynamicBatching = false,
                volumeFrameworkUpdateMode = (int)VolumeFrameworkUpdateMode.EveryFrame,
                supportDataDrivenLensFlare = true,
                useFastSrgbLinearConversion = false
            }
        };

        static readonly RendererPreset[] s_Renderer =
        {
            new RendererPreset
            {
                useNativeRenderPass = true,
                shadowTransparentReceive = false,
                opaqueLayerMask = -1,
                transparentLayerMask = -1,
                assignPostProcessData = false
            },
            new RendererPreset
            {
                useNativeRenderPass = true,
                shadowTransparentReceive = false,
                opaqueLayerMask = -1,
                transparentLayerMask = -1,
                assignPostProcessData = false
            },
            new RendererPreset
            {
                useNativeRenderPass = true,
                shadowTransparentReceive = true,
                opaqueLayerMask = -1,
                transparentLayerMask = -1,
                assignPostProcessData = true
            }
        };

        static readonly QualityLevelPreset[] s_QualityLevel =
        {
            new QualityLevelPreset
            {
                globalTextureMipmapLimit = 1,
                anisotropicTextures = (int)AnisotropicFiltering.Disable,
                lodBias = 0.7f,
                maximumLodLevel = 0,
                skinWeights = 2,
                particleRaycastBudget = 16,
                softParticles = false,
                softVegetation = false,
                realtimeReflectionProbes = false,
                billboardsFaceCameraPosition = false,
                vSyncCount = 0,
                resolutionScalingFixedDpiFactor = 1,
                shadowmaskMode = 0,
                streamingMipmapsActive = false,
                antiAliasing = 0
            },
            new QualityLevelPreset
            {
                globalTextureMipmapLimit = 0,
                anisotropicTextures = (int)AnisotropicFiltering.Enable,
                lodBias = 1f,
                maximumLodLevel = 0,
                skinWeights = 2,
                particleRaycastBudget = 64,
                softParticles = false,
                softVegetation = false,
                realtimeReflectionProbes = false,
                billboardsFaceCameraPosition = false,
                vSyncCount = 0,
                resolutionScalingFixedDpiFactor = 1,
                shadowmaskMode = 0,
                streamingMipmapsActive = false,
                antiAliasing = 0
            },
            new QualityLevelPreset
            {
                globalTextureMipmapLimit = 0,
                anisotropicTextures = (int)AnisotropicFiltering.Enable,
                lodBias = 1.5f,
                maximumLodLevel = 0,
                skinWeights = 4,
                particleRaycastBudget = 256,
                softParticles = true,
                softVegetation = true,
                realtimeReflectionProbes = true,
                billboardsFaceCameraPosition = true,
                vSyncCount = 0,
                resolutionScalingFixedDpiFactor = 1,
                shadowmaskMode = 0,
                streamingMipmapsActive = false,
                antiAliasing = 0
            }
        };

        static readonly RuntimePreset[] s_Runtime =
        {
            new RuntimePreset
            {
                targetFrameRate = 30,
                baseRenderScale = 0.75f,
                minRenderScale = 0.60f,
                maxRenderScale = 0.75f,
                thermalFallbackFrameRate = 0
            },
            new RuntimePreset
            {
                targetFrameRate = 60,
                baseRenderScale = 0.85f,
                minRenderScale = 0.70f,
                maxRenderScale = 0.85f,
                thermalFallbackFrameRate = 30
            },
            new RuntimePreset
            {
                targetFrameRate = 60,
                baseRenderScale = 1.00f,
                minRenderScale = 0.80f,
                maxRenderScale = 1.00f,
                thermalFallbackFrameRate = 30
            }
        };

        /// <summary>URP asset preset for a tier.</summary>
        /// <param name="tier">Tier index, 0-2.</param>
        /// <returns>The preset.</returns>
        internal static UrpPreset Urp(int tier) => s_Urp[tier];

        /// <summary>Renderer data preset for a tier.</summary>
        /// <param name="tier">Tier index, 0-2.</param>
        /// <returns>The preset.</returns>
        internal static RendererPreset Renderer(int tier) => s_Renderer[tier];

        /// <summary>Quality level preset for a tier.</summary>
        /// <param name="tier">Tier index, 0-2.</param>
        /// <returns>The preset.</returns>
        internal static QualityLevelPreset QualityLevel(int tier) => s_QualityLevel[tier];

        /// <summary>Runtime tier settings preset for a tier.</summary>
        /// <param name="tier">Tier index, 0-2.</param>
        /// <returns>The preset.</returns>
        internal static RuntimePreset Runtime(int tier) => s_Runtime[tier];

        /// <summary>
        /// The installed URP package version string, or <c>null</c> if it cannot be read.
        /// </summary>
        internal static string UrpVersion
        {
            get
            {
                UnityEditor.PackageManager.PackageInfo info = ResolveUrpPackage();
                return info?.version;
            }
        }

        /// <summary>
        /// Major version of the installed URP package. Falls back to <c>14</c>, the oldest
        /// version this package supports, when the version cannot be read - assuming the
        /// oldest means any version-gated behaviour degrades rather than breaks.
        /// </summary>
        internal static int UrpMajorVersion
        {
            get
            {
                string version = UrpVersion;
                if (string.IsNullOrEmpty(version))
                    return 14;

                int dot = version.IndexOf('.');
                string major = dot > 0 ? version.Substring(0, dot) : version;
                return int.TryParse(major, out int value) ? value : 14;
            }
        }

        /// <summary>True when the Universal Render Pipeline package is installed.</summary>
        internal static bool IsUrpInstalled => ResolveUrpPackage() != null;

        /// <summary>
        /// Creates <see cref="GeneratedFolder"/> and any missing parent, so asset creation
        /// cannot fail on a fresh import where only the scripts were copied in.
        /// </summary>
        /// <returns><c>true</c> if the folder exists after the call.</returns>
        internal static bool EnsureGeneratedFolder()
        {
            if (UnityEditor.AssetDatabase.IsValidFolder(GeneratedFolder))
                return true;

            const string root = "Assets/MobileAdjustGameQuality";
            if (!UnityEditor.AssetDatabase.IsValidFolder(root))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "MobileAdjustGameQuality");

            UnityEditor.AssetDatabase.CreateFolder(root, "Generated");
            return UnityEditor.AssetDatabase.IsValidFolder(GeneratedFolder);
        }

        static UnityEditor.PackageManager.PackageInfo ResolveUrpPackage()
        {
            // FindForAssembly is the cheap, synchronous lookup; the Client.List request
            // would need to be polled across editor frames and this runs inside OnGUI.
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(UniversalRenderPipelineAsset).Assembly);
        }
    }
}
