using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// Toggles the <c>MAQ_DEVELOPER_MODE</c> scripting define, which is what turns the debug
    /// logging and the on-screen overlay on and off.
    /// </summary>
    /// <remarks>
    /// The define carries the <c>MAQ_</c> prefix on purpose. Scripting defines are global to
    /// the project, so a generic name collides with any other package that reached for the
    /// same word, and the symptom is one package silently enabling another's debug code.
    /// <para>
    /// Set for Android, iOS and Standalone together: having it on for one platform and off
    /// for another produces "works on my machine" reports where the difference is invisible
    /// in the code.
    /// </para>
    /// </remarks>
    public static class DeveloperDefineToggle
    {
        /// <summary>The scripting define this package gates all developer-only code on.</summary>
        public const string Define = "MAQ_DEVELOPER_MODE";

        const string k_MenuPath = "Tools/Mobile Quality/Developer Mode";

        static readonly NamedBuildTarget[] k_Targets =
        {
            NamedBuildTarget.Android,
            NamedBuildTarget.iOS,
            NamedBuildTarget.Standalone
        };

        /// <summary>True when the define is set on every target this package manages.</summary>
        public static bool IsEnabled => k_Targets.All(target => ReadDefines(target).Contains(Define));

        [MenuItem(k_MenuPath, false, 200)]
        static void ToggleMenu() => SetEnabled(!IsEnabled);

        [MenuItem(k_MenuPath, true)]
        static bool ValidateToggleMenu()
        {
            Menu.SetChecked(k_MenuPath, IsEnabled);
            return true;
        }

        /// <summary>Adds or removes the define on all managed targets. Triggers a script reload.</summary>
        /// <param name="enabled">Whether developer mode should be on.</param>
        public static void SetEnabled(bool enabled)
        {
            foreach (NamedBuildTarget target in k_Targets)
            {
                List<string> defines = ReadDefines(target);
                if (enabled == defines.Contains(Define))
                    continue;

                if (enabled)
                    defines.Add(Define);
                else
                    defines.Remove(Define);

                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            }
        }

        /// <summary>
        /// Throws if <c>MAQ_DEVELOPER_MODE</c> is still set. Call from a build pipeline
        /// callback so a release build cannot ship with the overlay compiled in.
        /// </summary>
        /// <exception cref="BuildFailedException">The define survived into a release build.</exception>
        public static void AssertStrippedForRelease()
        {
            var offenders = k_Targets
                .Where(target => ReadDefines(target).Contains(Define))
                .Select(target => target.TargetName)
                .ToList();

            if (offenders.Count == 0)
                return;

            throw new BuildFailedException(
                $"{Define} is still defined for [{string.Join(", ", offenders)}]. " +
                "Turn it off under Tools > Mobile Quality > Developer Mode before a release build.");
        }

        static List<string> ReadDefines(NamedBuildTarget target)
        {
            string raw = PlayerSettings.GetScriptingDefineSymbols(target);
            if (string.IsNullOrEmpty(raw))
                return new List<string>();

            return raw.Split(';').Select(entry => entry.Trim()).Where(entry => entry.Length > 0).ToList();
        }
    }
}
