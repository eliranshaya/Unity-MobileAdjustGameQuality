using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>How hot the operating system reports the device to be.</summary>
    /// <remarks>
    /// These are <b>not</b> temperatures. Neither platform gives applications a number in
    /// degrees, and deliberately so: the temperature at which one phone throttles is
    /// meaningless on another, because it depends on the chassis, the cooling and the
    /// vendor's own policy. What you get instead is the OS's own verdict about how much it
    /// is holding the hardware back.
    /// </remarks>
    public enum ThermalLevel
    {
        /// <summary>No thermal API on this platform, or the query failed.</summary>
        Unknown = -1,

        /// <summary>No throttling.</summary>
        None = 0,

        /// <summary>Throttling has started. Stop raising quality.</summary>
        Light = 1,

        /// <summary>Frame times are drifting.</summary>
        Moderate = 2,

        /// <summary>The OS is actively clocking down. Give quality back.</summary>
        Severe = 3,

        /// <summary>Critical. Give quality back now.</summary>
        Critical = 4
    }

    /// <summary>
    /// Reads the platform thermal API on Android and iOS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Temperature is the only signal here that can show spare capacity. Frame time cannot,
    /// while a frame cap is active - the engine sleeps until the frame is due, so a device
    /// holding its target reports exactly the budget however much room it has to spare. The
    /// thermal forecast has no such blind spot: it says outright how close the device is to
    /// being throttled, capped or not.
    /// </para>
    /// <para>
    /// That is the <i>forecast</i>. The thermal <i>status</i> is a different thing and a much
    /// weaker one - see <see cref="AtLeast"/> for why it is not allowed to veto the forecast.
    /// </para>
    /// <para>
    /// Plain class rather than a MonoBehaviour: it owns a couple of handles and a poll
    /// method, and nothing else.
    /// </para>
    /// </remarks>
    public sealed class ThermalSensor : IDisposable
    {
        /// <summary>The forecast window passed to <c>getThermalHeadroom</c>, in seconds.</summary>
        const int k_HeadroomForecastSeconds = 10;

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Shortest gap allowed between two <c>getThermalHeadroom</c> calls, in seconds.
        /// </summary>
        /// <remarks>
        /// Not a tunable, on purpose. Android's own documentation is explicit: "Don't call the
        /// GetThermalHeadroom() API too frequently. If you do so, the API returns NaN. You
        /// shouldn't call it more than once every 10 seconds." A field somebody could set to
        /// five would not poll faster, it would silently stop returning a forecast at all, so
        /// the platform's contract is enforced here rather than left to configuration. Fifteen
        /// rather than ten is margin: the interval is measured from the previous call, and a
        /// frame hitch either side of a ten-second timer lands inside the window.
        /// <para>
        /// The status has no such limit, which is why the two reads are paced separately.
        /// </para>
        /// </remarks>
        const float k_MinHeadroomIntervalSeconds = 15f;

        AndroidJavaObject m_PowerManager;
        AndroidJavaObject m_Activity;
        int m_ApiLevel;
        bool m_Failed;
        float m_NextHeadroomAt;
#endif

#if UNITY_IOS && !UNITY_EDITOR
        // Implemented in Runtime/Plugins/iOS/MobileQualityThermalNative.mm. The symbol is
        // prefixed _MAQ_ on purpose: a generic name like _GetThermalState collides at link
        // time with any other package bridging the same API, and a duplicate-symbol error in
        // Xcode is a miserable thing to debug in someone else's project.
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern int MAQ_GetThermalState();

        bool m_Failed;
#endif

        /// <summary>The most recent status. <see cref="ThermalLevel.Unknown"/> if unavailable.</summary>
        public ThermalLevel Level { get; private set; } = ThermalLevel.Unknown;

        /// <summary>
        /// The most recent headroom forecast, or <c>NaN</c> when unavailable. Android only;
        /// Apple exposes no equivalent.
        /// </summary>
        public float Headroom { get; private set; } = float.NaN;

        /// <summary>True when the platform has reported a usable value at least once.</summary>
        public bool IsAvailable => Level != ThermalLevel.Unknown || !float.IsNaN(Headroom);

        /// <summary>
        /// Tests the device against one thermal threshold.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The sensor reports; it does not decide. Where the thresholds sit is policy, and
        /// policy lives in <see cref="AdaptationSettings"/> where it can be edited without
        /// touching this file - see <see cref="AdaptationSettings.Band"/>.
        /// </para>
        /// <para>
        /// The two signals are <b>not</b> equals, and treating them as equals is what made
        /// this package strand a cool device on the bottom rung. Android's own documentation
        /// says the status is frequently stale - "If GetThermalHeadroom() returns a high value
        /// and GetCurrentThermalStatus() still returns THERMAL_STATUS_NONE, the status is
        /// likely not updated ... we recommend using getThermalHeadroom instead" - and it is
        /// stale in both directions. It reads <c>NONE</c> on a device that is already hot, and
        /// it sits on <c>LIGHT</c> long after one has cooled down. A device reporting
        /// <c>LIGHT</c> with a forecast of 0.51 is not throttled; it is a status that has not
        /// caught up.
        /// </para>
        /// <para>
        /// So where a forecast exists it decides alone. There is one exception:
        /// <see cref="ThermalLevel.Severe"/> and above is not a forecast, it is the OS
        /// reporting that it is clocking the chip down right now, and that is never ignored
        /// however comfortable the prediction looks.
        /// </para>
        /// <para>
        /// On iOS and on Android 10 and older there is no forecast at all,
        /// <see cref="Headroom"/> stays <c>NaN</c>, and the status decides on its own.
        /// </para>
        /// </remarks>
        /// <param name="level">Status at or above which this returns true.</param>
        /// <param name="headroom">Headroom at or above which this returns true.</param>
        /// <returns>True when the device has reached the threshold.</returns>
        public bool AtLeast(ThermalLevel level, float headroom)
        {
            // Unknown is -1, so it compares below every real level and never trips a band on
            // a platform with no thermal API.
            bool statusSays = Level != ThermalLevel.Unknown && Level >= level;

            if (float.IsNaN(Headroom))
                return statusSays;

            return Headroom >= headroom || (statusSays && Level >= ThermalLevel.Severe);
        }

        /// <summary>
        /// Acquires platform handles. Safe everywhere; does nothing where there is nothing to
        /// acquire - iOS needs no setup, because the plugin reads a process-wide property.
        /// </summary>
        public void Open()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Every JNI call is wrapped: several OEM ROMs throw from getSystemService or from
            // the thermal methods themselves, and an uncaught exception during startup takes
            // the whole app down.
            try
            {
                using (var versionClass = new AndroidJavaClass("android.os.Build$VERSION"))
                    m_ApiLevel = versionClass.GetStatic<int>("SDK_INT");

                using (var playerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    m_Activity = playerClass.GetStatic<AndroidJavaObject>("currentActivity");

                if (m_Activity != null)
                    m_PowerManager = m_Activity.Call<AndroidJavaObject>("getSystemService", "power");
            }
            catch (Exception e)
            {
                m_Failed = true;
                QualityDevLog.Warn($"Thermal API unavailable; adapting from frame times only: {e.Message}");
                Dispose();
            }
#endif
        }

        /// <summary>Reads the current thermal state. Cheap, and safe from a coroutine.</summary>
        public void Poll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_Failed || m_PowerManager == null)
                return;

            try
            {
                // getCurrentThermalStatus() is API 29 (Android 10). Android also defines
                // EMERGENCY (5) and SHUTDOWN (6); both clamp to Critical, because at that
                // point the OS is about to kill the process and no render scale will help.
                if (m_ApiLevel >= 29)
                    Level = (ThermalLevel)Mathf.Clamp(m_PowerManager.Call<int>("getCurrentThermalStatus"), 0, 4);

                // getThermalHeadroom() is API 30 (Android 11) and is the better signal: it is
                // a forecast, so it moves before the status does. It is also the one call
                // here with a rate limit, so it runs on its own timer rather than on the
                // caller's - see k_MinHeadroomIntervalSeconds.
                float now = Time.realtimeSinceStartup;

                if (m_ApiLevel >= 30 && now >= m_NextHeadroomAt)
                {
                    m_NextHeadroomAt = now + k_MinHeadroomIntervalSeconds;

                    float headroom = m_PowerManager.Call<float>("getThermalHeadroom", k_HeadroomForecastSeconds);

                    // NaN means "polled too fast" or "no prediction yet". Keep the last good
                    // value rather than reading NaN as cool.
                    if (!float.IsNaN(headroom) && !float.IsInfinity(headroom))
                        Headroom = headroom;
                }
            }
            catch (Exception e)
            {
                // One failure is enough: these APIs do not recover on a device that rejects
                // them, and retrying twelve times a minute only burns JNI calls.
                m_Failed = true;
                Level = ThermalLevel.Unknown;
                Headroom = float.NaN;
                QualityDevLog.Warn($"Thermal poll failed, disabling thermal input: {e.Message}");
            }
#endif

#if UNITY_IOS && !UNITY_EDITOR
            if (m_Failed)
                return;

            try
            {
                // NSProcessInfo.thermalState. Four states rather than Android's five, and no
                // headroom equivalent - Apple exposes no forecast - so Headroom stays NaN and
                // every decision on iOS runs off the status alone.
                int state = MAQ_GetThermalState();

                // The plugin returns -1 when the API is unavailable. That is not "cool" - it
                // is "no reading", and reporting it as cool would put a false 'nominal' in the
                // overlay and make IsAvailable claim a signal that is not there.
                Level = state < 0 ? ThermalLevel.Unknown
                    : state == 0 ? ThermalLevel.None       // .nominal
                    : state == 1 ? ThermalLevel.Light      // .fair
                    : state == 2 ? ThermalLevel.Severe     // .serious - Apple says reduce work now
                    : ThermalLevel.Critical;                // .critical
            }
            catch (Exception e)
            {
                // The realistic cause is the .mm not reaching the build, which throws
                // EntryPointNotFoundException on the first call. Failing once and going quiet
                // beats an exception every second for the whole session.
                m_Failed = true;
                Level = ThermalLevel.Unknown;
                QualityDevLog.Warn(
                    $"iOS thermal query failed: {e.Message}. MobileQualityThermalNative.mm is " +
                    "probably missing from the Xcode project.");
            }
#endif
        }

        /// <summary>Releases the JNI handles.</summary>
        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                m_PowerManager?.Dispose();
                m_Activity?.Dispose();
            }
            catch (Exception)
            {
                // Disposing a dead JNI handle during teardown is not worth reporting.
            }
            finally
            {
                m_PowerManager = null;
                m_Activity = null;
            }
#endif
        }
    }
}
