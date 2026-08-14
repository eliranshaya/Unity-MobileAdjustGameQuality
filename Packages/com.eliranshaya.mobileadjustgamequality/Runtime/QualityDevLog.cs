using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// The single logging entry point for everything in <c>Runtime/</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="ConditionalAttribute"/> rather than <c>#if</c> at every call site: the
    /// compiler strips the call <b>and every argument expression</b>, so an interpolated
    /// string is never built in a release build. Callers stay readable and pay nothing.
    /// <para>
    /// This is the only file in <c>Runtime/</c> allowed to touch <c>UnityEngine.Debug</c>.
    /// </para>
    /// </remarks>
    public static class QualityDevLog
    {
        const string k_Prefix = "[MobileQuality] ";

        /// <summary>Developer-only information. Compiled out without <c>MAQ_DEVELOPER_MODE</c>.</summary>
        /// <param name="message">The message to log.</param>
        [Conditional("MAQ_DEVELOPER_MODE")]
        public static void Log(string message) => Debug.Log(k_Prefix + message);

        /// <summary>Developer-only warning. Compiled out without <c>MAQ_DEVELOPER_MODE</c>.</summary>
        /// <param name="message">The message to log.</param>
        [Conditional("MAQ_DEVELOPER_MODE")]
        public static void Warn(string message) => Debug.LogWarning(k_Prefix + message);

        /// <summary>
        /// A misconfiguration that breaks the game for real players. Deliberately <b>not</b>
        /// conditional: a missing quality level in a shipped build has to reach the player
        /// log and crash reporting, or the failure is invisible to everyone.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void FatalConfigError(string message) => Debug.LogError(k_Prefix + message);
    }
}
