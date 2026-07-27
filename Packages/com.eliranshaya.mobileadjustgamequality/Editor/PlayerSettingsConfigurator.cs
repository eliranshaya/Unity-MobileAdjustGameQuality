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
    /// Player Settings are project-wide and permanent, and a graphics package that
    /// rewrites someone's scripting backend without asking is a package that gets deleted.
    /// Every setting here is therefore individually toggleable in the Setup window, and
    /// every change is written into the run report so it is obvious what moved.
    /// </remarks>
    internal static class PlayerSettingsConfigurator
    {
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

            /// <summary>True when at least one setting is selected.</summary>
            internal bool Any => optimizedFramePacing || linearColorSpace || graphicsApis
                                 || multithreadedRendering || il2cpp || arm64 || astcTextures || batching;
        }

        const string k_ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";

        /// <summary>
        /// Applies the selected settings.
        /// </summary>
        /// <param name="options">Which settings to write.</param>
        /// <param name="report">Run report to record the outcome in.</param>
        internal static void Apply(Options options, SetupReport report)
        {
            if (options == null || !options.Any)
                return;

            if (options.linearColorSpace)
            {
                // Changing color space triggers a full shader reimport, which is slow but
                // is the single most visible correctness fix on this list.
                if (PlayerSettings.colorSpace != ColorSpace.Linear)
                {
                    PlayerSettings.colorSpace = ColorSpace.Linear;
                    report.Update("Player Settings: Color Space -> Linear (this reimports shaders)");
                }
            }

            if (options.optimizedFramePacing)
            {
                PlayerSettings.Android.optimizedFramePacing = true;
                report.Update("Player Settings: Android Optimized Frame Pacing -> on");
            }

            if (options.graphicsApis)
            {
                // Order is the priority order Unity tries at startup, so Vulkan first.
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
                {
                    GraphicsDeviceType.Vulkan,
                    GraphicsDeviceType.OpenGLES3
                });
                report.Update("Player Settings: Android Graphics APIs -> Vulkan, then GLES3");
            }

            if (options.multithreadedRendering)
            {
                PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);
                report.Update("Player Settings: Android Multithreaded Rendering -> on");
            }

            if (options.il2cpp)
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                report.Update("Player Settings: Android Scripting Backend -> IL2CPP");
            }

            if (options.arm64)
            {
                // ARM64 only. Google Play has required a 64-bit binary since 2019 and
                // shipping ARMv7 as well only doubles the download size.
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                report.Update("Player Settings: Android Target Architectures -> ARM64");
            }

            if (options.astcTextures)
                ApplyTextureCompression(report);

            if (options.batching)
                ApplyBatching(report);

            AssetDatabase.SaveAssets();
        }

        static void ApplyTextureCompression(SetupReport report)
        {
#if UNITY_2023_1_OR_NEWER
            // The per-platform list replaced the single build subtarget; the first entry
            // is the format the default build uses.
            PlayerSettings.Android.textureCompressionFormats = new[] { TextureCompressionFormat.ASTC };
#else
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
#endif
            report.Update("Build Settings: Android Texture Compression -> ASTC");
        }

        static void ApplyBatching(SetupReport report)
        {
            // There is no public API for per-platform batching, so this edits
            // ProjectSettings.asset directly. The array is keyed by a platform name
            // string and starts out empty on a fresh project, which is why the entry has
            // to be found-or-appended rather than indexed.
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(k_ProjectSettingsPath);
            if (assets == null || assets.Length == 0)
            {
                report.Problem($"Could not open {k_ProjectSettingsPath}; batching was left alone.");
                return;
            }

            var so = new SerializedObject(assets[0]);
            SerializedProperty batching = so.FindProperty("m_BuildTargetBatching");
            if (batching == null || !batching.isArray)
            {
                report.Problem("'m_BuildTargetBatching' is missing from ProjectSettings.asset; batching was left alone.");
                return;
            }

            SerializedProperty entry = FindOrAppendPlatform(batching, "Android");
            if (entry == null)
            {
                report.Problem("Could not create the Android batching entry; batching was left alone.");
                return;
            }

            SerializedFieldHelper.TrySetRelative(entry, "m_StaticBatching", "ProjectSettings.Android", 1);
            SerializedFieldHelper.TrySetRelative(entry, "m_DynamicBatching", "ProjectSettings.Android", 1);

            so.ApplyModifiedPropertiesWithoutUndo();
            report.Update("Player Settings: Android Static + Dynamic Batching -> on");
        }

        static SerializedProperty FindOrAppendPlatform(SerializedProperty array, string platformName)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                SerializedProperty target = element.FindPropertyRelative("m_BuildTarget");
                if (target != null && string.Equals(target.stringValue, platformName, System.StringComparison.Ordinal))
                    return element;
            }

            int index = array.arraySize;
            array.InsertArrayElementAtIndex(index);
            SerializedProperty appended = array.GetArrayElementAtIndex(index);

            SerializedProperty name = appended.FindPropertyRelative("m_BuildTarget");
            if (name == null)
                return null;

            name.stringValue = platformName;
            return appended;
        }
    }
}
