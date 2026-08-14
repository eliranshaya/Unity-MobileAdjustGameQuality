using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

// The namespace is deliberately not ...EditorTools.iOS: a namespace segment named "iOS" makes
// the compiler resolve UnityEditor.iOS.Xcode against it and fail.
namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Makes the iOS thermal plugin work in the generated Xcode project without anyone
    /// opening Xcode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One job: verify <c>MobileQualityThermalNative.mm</c> actually reached the project,
    /// and put it there if it did not. A stale package cache or a mangled <c>.meta</c>
    /// silently drops the plugin, and the symptom is "thermal never reports anything on
    /// iOS" with a completely clean build log.
    /// </para>
    /// <para>
    /// No framework is linked, unlike a Core Haptics bridge would need: the plugin only
    /// touches <c>NSProcessInfo</c>, and Foundation is always linked into UnityFramework.
    /// Adding a framework here just to look thorough would be noise in someone's project.
    /// </para>
    /// <para>
    /// Both outcomes are logged. Silence is what makes this class of bug undiagnosable.
    /// </para>
    /// <para>
    /// The assembly is constrained to <c>UNITY_IOS</c> so it is not compiled at all - and
    /// cannot complain about a missing <c>UnityEditor.iOS.Extensions.Xcode.dll</c> - in
    /// projects without iOS Build Support installed.
    /// </para>
    /// </remarks>
    public static class MobileQualityThermalIOSPostProcess
    {
        /// <summary>GUID of MobileQualityThermalNative.mm, pinned in its committed .meta.</summary>
        const string k_NativeSourceGuid = "3c9f1a5d7e2b48c1936af04d6b8e2751";

        const string k_NativeSourceName = "MobileQualityThermalNative.mm";

        const string k_FallbackSourcePath =
            "Packages/com.eliranshaya.mobileadjustgamequality/Runtime/Plugins/iOS/" + k_NativeSourceName;

        /// <summary>Where a repaired copy is placed inside the Xcode project.</summary>
        const string k_RepairDirectory = "Libraries/MobileAdjustGameQuality";

        const string k_LogPrefix = "[MobileQuality] iOS build: ";

        /// <summary>Late, so other packages have finished restructuring the project first.</summary>
        /// <param name="target">The build target that just finished.</param>
        /// <param name="pathToBuiltProject">Path to the generated Xcode project.</param>
        [PostProcessBuild(1000)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            try
            {
                Run(pathToBuiltProject);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"{k_LogPrefix}post-process failed ({e.Message}). Thermal state will read as Unknown " +
                    $"on device and quality will adapt from frame times only. Confirm {k_NativeSourceName} " +
                    "is in UnityFramework's Compile Sources.");
            }
        }

        static void Run(string pathToBuiltProject)
        {
            string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            // The .mm compiles into UnityFramework, not the app target. Adding it to the app
            // target - the obvious move, and the usual mistake - achieves nothing, because
            // the managed DllImport("__Internal") resolves against UnityFramework.
            string frameworkTarget = project.GetUnityFrameworkTargetGuid();

            bool present = project.ContainsFileByProjectPath(RepairedProjectPath())
                           || ProjectContainsNativeSource(projectPath);

            if (!present)
                RepairMissingSource(project, frameworkTarget, pathToBuiltProject);

            project.WriteToFile(projectPath);

            Debug.Log($"{k_LogPrefix}{k_NativeSourceName} " +
                      $"{(present ? "already in the project" : "was missing and has been added to UnityFramework")}.");
        }

        /// <summary>
        /// Looks for the plugin anywhere in the pbxproj. Reading the file as text rather than
        /// walking the build phases is deliberate: it finds the plugin under whatever path
        /// Unity chose for it, across every Unity version.
        /// </summary>
        static bool ProjectContainsNativeSource(string projectPath) =>
            File.ReadAllText(projectPath).Contains(k_NativeSourceName);

        static string RepairedProjectPath() => k_RepairDirectory + "/" + k_NativeSourceName;

        static void RepairMissingSource(PBXProject project, string frameworkTarget, string buildPath)
        {
            string source = ResolveSourcePath();
            if (source == null)
            {
                Debug.LogError(
                    $"{k_LogPrefix}{k_NativeSourceName} is neither in the Xcode project nor findable in the " +
                    "package. Thermal state will read as Unknown on device.");
                return;
            }

            string destinationDirectory = Path.Combine(buildPath, k_RepairDirectory);
            Directory.CreateDirectory(destinationDirectory);

            string destination = Path.Combine(destinationDirectory, k_NativeSourceName);

            // overwrite: Append builds reuse the directory from the previous build.
            File.Copy(source, destination, true);

            string fileGuid = project.AddFile(RepairedProjectPath(), RepairedProjectPath(), PBXSourceTree.Source);
            project.AddFileToBuild(frameworkTarget, fileGuid);

            Debug.LogWarning(
                $"{k_LogPrefix}{k_NativeSourceName} was missing from the Xcode project and has been added to " +
                "UnityFramework. That usually means the plugin's .meta lost its iOS platform setting - " +
                "reimport the package if it keeps happening.");
        }

        /// <summary>
        /// Resolves the plugin by the GUID pinned in its .meta, falling back to the package
        /// path. The GUID survives the package being relocated, embedded or renamed; the path
        /// survives the asset database not knowing about the file at all.
        /// </summary>
        static string ResolveSourcePath()
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(k_NativeSourceGuid);
            if (!string.IsNullOrEmpty(assetPath) && File.Exists(assetPath))
                return assetPath;

            return File.Exists(k_FallbackSourcePath) ? k_FallbackSourcePath : null;
        }
    }
}
