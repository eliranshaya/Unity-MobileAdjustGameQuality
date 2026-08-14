using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Applies the recommended mobile Player Settings, one opt-in checkbox at a time.
    /// </summary>
    /// <remarks>
    /// Player Settings are project-wide and permanent, and a graphics package that rewrites
    /// someone's scripting backend without asking is a package that gets deleted. Every
    /// setting is individually toggleable and every change is written into the run report.
    /// </remarks>
    internal static class PlayerSettingsConfigurator
    {
        const string k_ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";

        /// <summary>Which of the recommended settings to apply.</summary>
        internal class Options
        {
            /// <summary>Android optimized frame pacing. Smooths out frame delivery jitter.</summary>
            internal bool optimizedFramePacing = true;

            /// <summary>Linear color space. URP's lighting assumes it.</summary>
            internal bool linearColorSpace = true;

            /// <summary>Android graphics APIs: Vulkan first, GLES3 as the fallback.</summary>
            internal bool graphicsApis = true;

            /// <summary>Multithreaded rendering on Android.</summary>
            internal bool multithreadedRendering = true;

            /// <summary>IL2CPP scripting backend on Android.</summary>
            internal bool il2cpp = true;

            /// <summary>ARM64-only target architecture on Android.</summary>
            internal bool arm64 = true;

            /// <summary>ASTC texture compression for Android.</summary>
            internal bool astcTextures = true;

            /// <summary>Static and dynamic batching for Android.</summary>
            internal bool batching = true;

            /// <summary>
            /// The iOS side: Metal only, multithreaded rendering, and batching.
            /// </summary>
            /// <remarks>
            /// Shorter than the Android list because iOS decides most of it for you - the
            /// scripting backend is always IL2CPP, the architecture is always ARM64, and the
            /// texture format is chosen by the platform defaults. What is left is genuinely
            /// per-project, and batching in particular is keyed per platform, so the Android
            /// entry does nothing for an iOS build.
            /// </remarks>
            internal bool iosSettings = true;

            /// <summary>
            /// Frame Timing Stats. Adaptive Performance needs it to tell a CPU-bound frame
            /// from a GPU-bound one, and the overlay reports it.
            /// </summary>
            internal bool frameTimingStats = true;

            /// <summary>True when at least one setting is selected.</summary>
            internal bool Any => optimizedFramePacing || linearColorSpace || graphicsApis
                                 || multithreadedRendering || il2cpp || arm64 || astcTextures
                                 || batching || frameTimingStats || iosSettings;
        }

        /// <summary>Applies the selected settings.</summary>
        /// <param name="options">Which settings to write.</param>
        /// <param name="report">Run report to record the outcome in.</param>
        internal static void Apply(Options options, SetupReport report)
        {
            if (options == null || !options.Any)
                return;

            if (options.linearColorSpace && PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                // Changing color space triggers a full shader reimport, which is slow but is
                // the single most visible correctness fix on this list.
                PlayerSettings.colorSpace = ColorSpace.Linear;
                report.Update("Color Space -> Linear (this reimports shaders)");
            }

            if (options.optimizedFramePacing)
            {
                PlayerSettings.Android.optimizedFramePacing = true;
                report.Update("Android: Optimized Frame Pacing -> on");
            }

            if (options.graphicsApis)
            {
                // Order is the priority Unity tries at startup, so Vulkan first.
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                    new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
                report.Update("Android: Graphics APIs -> Vulkan, then GLES3");
            }

            if (options.multithreadedRendering)
            {
                PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);
                report.Update("Android: Multithreaded Rendering -> on");
            }

            if (options.il2cpp)
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                report.Update("Android: Scripting Backend -> IL2CPP");
            }

            if (options.arm64)
            {
                // Google Play has required a 64-bit binary since 2019, and shipping ARMv7 as
                // well only doubles the download size.
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                report.Update("Android: Target Architectures -> ARM64");
            }

            if (options.astcTextures)
            {
#if UNITY_2023_1_OR_NEWER
                PlayerSettings.Android.textureCompressionFormats = new[] { TextureCompressionFormat.ASTC };
#else
                EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
#endif
                report.Update("Android: Texture Compression -> ASTC");
            }

            if (options.batching)
                ApplyBatching("Android", report);

            if (options.iosSettings)
                ApplyIOS(report);

            if (options.frameTimingStats && !PlayerSettings.enableFrameTimingStats)
            {
                PlayerSettings.enableFrameTimingStats = true;
                report.Update("Frame Timing Stats -> on");
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The iOS half of the recommended settings.
        /// </summary>
        /// <remarks>
        /// Short on purpose. iOS makes most of the Android list for you: the scripting backend
        /// is always IL2CPP, the architecture is always ARM64, and the texture format comes
        /// from the platform defaults. Writing them anyway would only add lines to the report
        /// that describe nothing.
        /// </remarks>
        static void ApplyIOS(SetupReport report)
        {
            // Metal is the only graphics API modern Unity offers on iOS, but the project can
            // still be left on "Auto Graphics API", and pinning it makes the build reproducible
            // rather than dependent on the editor version's idea of the default.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
            report.Update("iOS: Graphics API -> Metal");

            PlayerSettings.SetMobileMTRendering(NamedBuildTarget.iOS, true);
            report.Update("iOS: Multithreaded Rendering -> on");

            // Keyed "iPhone" in ProjectSettings.asset, not "iOS". The Android entry does
            // nothing for an iOS build, which is the whole reason this call exists.
            ApplyBatching("iPhone", report);
        }

        static void ApplyBatching(string platform, SetupReport report)
        {
            // There is no public API for per-platform batching, so this edits
            // ProjectSettings.asset directly. The array is keyed by a platform name string
            // and starts out empty on a fresh project, so the entry has to be found or
            // appended rather than indexed.
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(k_ProjectSettingsPath);
            if (assets == null || assets.Length == 0)
            {
                report.Problem($"Could not open {k_ProjectSettingsPath}; {platform} batching was left alone.");
                return;
            }

            var so = new SerializedObject(assets[0]);
            SerializedProperty batching = so.FindProperty("m_BuildTargetBatching");
            if (batching == null || !batching.isArray)
            {
                report.Problem($"'m_BuildTargetBatching' is missing; {platform} batching was left alone.");
                return;
            }

            SerializedProperty entry = FindOrAppend(batching, platform);
            if (entry == null)
            {
                report.Problem($"Could not create the {platform} batching entry.");
                return;
            }

            SerializedFieldHelper.TrySetRelative(entry, "m_StaticBatching", $"ProjectSettings.{platform}", 1);
            SerializedFieldHelper.TrySetRelative(entry, "m_DynamicBatching", $"ProjectSettings.{platform}", 1);

            so.ApplyModifiedPropertiesWithoutUndo();
            report.Update($"{(platform == "iPhone" ? "iOS" : platform)}: Static + Dynamic Batching -> on");
        }

        static SerializedProperty FindOrAppend(SerializedProperty array, string platform)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                SerializedProperty target = element.FindPropertyRelative("m_BuildTarget");
                if (target != null && string.Equals(target.stringValue, platform, System.StringComparison.Ordinal))
                    return element;
            }

            int index = array.arraySize;
            array.InsertArrayElementAtIndex(index);
            SerializedProperty appended = array.GetArrayElementAtIndex(index);

            SerializedProperty name = appended.FindPropertyRelative("m_BuildTarget");
            if (name == null)
                return null;

            name.stringValue = platform;
            return appended;
        }
    }
}
