using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// The single logging entry point for everything in <c>Runtime/</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why <see cref="ConditionalAttribute"/> instead of wrapping every call site in
    /// <c>#if DEVELOPER_MODE</c>: the C# compiler strips the call <b>and every argument
    /// expression</b> at the call site. So a call like
    /// <c>QualityDevLog.Log($"p95 {p95:F1}ms scale {scale:F2}")</c> does not build the
    /// interpolated string, does not box the floats, and does not allocate — the whole
    /// statement simply is not emitted.
    /// </para>
    /// <para>
    /// That matters because <see cref="AdaptiveQualityController"/> evaluates twice a
    /// second for the entire lifetime of the app. Wrapping call sites in <c>#if</c> would
    /// achieve the same thing but would litter every caller; the attribute keeps the
    /// callers readable and gets the identical result.
    /// </para>
    /// <para>
    /// This is the only file in <c>Runtime/</c> allowed to touch <c>UnityEngine.Debug</c>.
    /// </para>
    /// </remarks>
    public static class QualityDevLog
    {
        const string k_Prefix = "[MobileQuality] ";

        /// <summary>Developer-only information. Compiled out without <c>DEVELOPER_MODE</c>.</summary>
        /// <param name="message">The message to log.</param>
        [Conditional("DEVELOPER_MODE")]
        public static void Log(string message)
        {
            Debug.Log(k_Prefix + message);
        }

        /// <summary>Developer-only warning. Compiled out without <c>DEVELOPER_MODE</c>.</summary>
        /// <param name="message">The message to log.</param>
        [Conditional("DEVELOPER_MODE")]
        public static void Warn(string message)
        {
            Debug.LogWarning(k_Prefix + message);
        }

        /// <summary>Developer-only error. Compiled out without <c>DEVELOPER_MODE</c>.</summary>
        /// <param name="message">The message to log.</param>
        [Conditional("DEVELOPER_MODE")]
        public static void Error(string message)
        {
            Debug.LogError(k_Prefix + message);
        }

        /// <summary>
        /// A misconfiguration that breaks the game for real players, not just for developers.
        /// Deliberately <b>not</b> <see cref="ConditionalAttribute"/>: if the URP asset is
        /// missing in a shipped build, that must show up in the player log and in crash
        /// reporting, otherwise the failure is invisible to everyone.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void FatalConfigError(string message)
        {
            Debug.LogError(k_Prefix + message);
        }
    }
}
