using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// How hot the device reports itself to be.
    /// </summary>
    /// <remarks>
    /// Mirrors Android's <c>PowerManager.THERMAL_STATUS_*</c> constants for values 0-4.
    /// Android also defines EMERGENCY (5) and SHUTDOWN (6); those are clamped to
    /// <see cref="Critical"/> because at that point the OS is about to kill the process
    /// and there is nothing a render scale can do about it.
    /// </remarks>
    public enum ThermalLevel
    {
        /// <summary>No thermal API available on this platform, or the query failed.</summary>
        Unknown = -1,

        /// <summary>No throttling.</summary>
        None = 0,

        /// <summary>Light throttling. Not yet visible to the player.</summary>
        Light = 1,

        /// <summary>Moderate throttling. Frame times start to drift.</summary>
        Moderate = 2,

        /// <summary>Severe throttling. The device is actively clocking down.</summary>
        Severe = 3,

        /// <summary>Critical throttling. Give quality back immediately.</summary>
        Critical = 4
    }

    /// <summary>
    /// Polls the platform thermal API so <see cref="AdaptiveQualityController"/> can tell
    /// "this device is slow" apart from "this device got hot".
    /// </summary>
    /// <remarks>
    /// The distinction matters: a slow device should settle at a lower render scale and
    /// stay there, while a hot device should be given quality back and then allowed to
    /// climb again once it cools. Frame times alone cannot separate the two cases.
    /// </remarks>
    [AddComponentMenu("Mobile Adjust Game Quality/Thermal Monitor")]
    [DisallowMultipleComponent]
    public class ThermalMonitor : MonoBehaviour
    {
        /// <summary>
        /// Android returns NaN from <c>getThermalHeadroom</c> if it is polled more than
        /// once per second, so five seconds gives plenty of margin and the value never
        /// moves fast enough to need a tighter loop.
        /// </summary>
        const float k_PollInterval = 5f;

        /// <summary>
        /// Headroom is a forecast where 1.0 means "throttling right now". Treating 0.85
        /// as warm gives the controller a few seconds of warning before the OS acts.
        /// </summary>
        const float k_WarmHeadroom = 0.85f;

        /// <summary>The forecast window passed to <c>getThermalHeadroom</c>, in seconds.</summary>
        const int k_HeadroomForecastSeconds = 10;

        float m_NextPollTime;

        /// <summary>The most recent thermal status. <see cref="ThermalLevel.Unknown"/> if unavailable.</summary>
        public ThermalLevel Level { get; private set; } = ThermalLevel.Unknown;

        /// <summary>
        /// The most recent thermal headroom forecast, or <c>NaN</c> when unavailable.
        /// Values approaching 1.0 mean throttling is imminent.
        /// </summary>
        public float Headroom { get; private set; } = float.NaN;

        /// <summary>
        /// True when quality must not be raised. Deliberately conservative: stepping up
        /// on a warm device is the change most likely to be immediately undone.
        /// </summary>
        public bool IsWarm
        {
            get
            {
                if (Level >= ThermalLevel.Light)
                    return true;
                return !float.IsNaN(Headroom) && Headroom >= k_WarmHeadroom;
            }
        }

        /// <summary>
        /// True when quality must actively be given back, not merely held.
        /// </summary>
        public bool IsSevere
        {
            get
            {
                if (Level >= ThermalLevel.Severe)
                    return true;
                return !float.IsNaN(Headroom) && Headroom >= 1f;
            }
        }

        /// <summary>True when the platform reported a usable value at least once.</summary>
        public bool IsAvailable => Level != ThermalLevel.Unknown || !float.IsNaN(Headroom);

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject m_PowerManager;
        AndroidJavaObject m_Activity;
        int m_ApiLevel;
        bool m_JniFailed;
#endif

        void Awake()
        {
            AcquirePlatformHandles();
            Poll();
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextPollTime)
                return;

            m_NextPollTime = Time.unscaledTime + k_PollInterval;
            Poll();
        }

        void OnApplicationPause(bool paused)
        {
            // Coming back from the background is exactly when the device may have cooled
            // down (or heated up in someone's pocket), so do not wait out the interval.
            if (!paused)
                m_NextPollTime = 0f;
        }

        void OnDestroy()
        {
            ReleasePlatformHandles();
        }

        void AcquirePlatformHandles()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Every JNI call here is wrapped: several OEM ROMs throw from
            // getSystemService or from the thermal methods themselves, and an uncaught
            // exception in Awake takes the whole app down at startup.
            try
            {
                using (var versionClass = new AndroidJavaClass("android.os.Build$VERSION"))
                    m_ApiLevel = versionClass.GetStatic<int>("SDK_INT");

                using (var playerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    m_Activity = playerClass.GetStatic<AndroidJavaObject>("currentActivity");

                if (m_Activity != null)
                    m_PowerManager = m_Activity.Call<AndroidJavaObject>("getSystemService", "power");
            }
            catch (System.Exception e)
            {
                m_JniFailed = true;
                QualityDevLog.Warn($"Thermal API unavailable, falling back to frame times only: {e.Message}");
                ReleasePlatformHandles();
            }
#endif
        }

        void ReleasePlatformHandles()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                m_PowerManager?.Dispose();
                m_Activity?.Dispose();
            }
            catch (System.Exception)
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

        void Poll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_JniFailed || m_PowerManager == null)
                return;

            try
            {
                // getCurrentThermalStatus() is API 29 (Android 10).
                if (m_ApiLevel >= 29)
                {
                    int status = m_PowerManager.Call<int>("getCurrentThermalStatus");
                    Level = (ThermalLevel)Mathf.Clamp(status, 0, 4);
                }

                // getThermalHeadroom() is API 30 (Android 11) and is the better signal:
                // it is a forecast, so it moves before the status does.
                if (m_ApiLevel >= 30)
                {
                    float headroom = m_PowerManager.Call<float>("getThermalHeadroom", k_HeadroomForecastSeconds);

                    // NaN means "polled too fast" or "no prediction yet". Keep the last
                    // good value rather than treating NaN as cool.
                    if (!float.IsNaN(headroom) && !float.IsInfinity(headroom))
                        Headroom = headroom;
                }
            }
            catch (System.Exception e)
            {
                // One failure is enough: these APIs do not recover on a device that
                // rejects them, and retrying twelve times a minute just burns JNI calls.
                m_JniFailed = true;
                Level = ThermalLevel.Unknown;
                Headroom = float.NaN;
                QualityDevLog.Warn($"Thermal poll failed, disabling thermal input: {e.Message}");
            }
#endif

            // iOS thermal state is available through ProcessInfo, but it needs a native
            // plugin because Unity does not surface it. This package deliberately does
            // not generate that plugin - dropping an unsigned .mm into someone's project
            // is a build-configuration surprise, not a feature. If you want it, add:
            //
            //   Plugins/iOS/MAQThermal.mm
            //   -----------------------------------------------------------------
            //   #import <Foundation/Foundation.h>
            //   extern "C" int MAQ_GetThermalState()
            //   {
            //       // NSProcessInfoThermalState: 0 nominal, 1 fair, 2 serious, 3 critical
            //       return (int)[[NSProcessInfo processInfo] thermalState];
            //   }
            //   -----------------------------------------------------------------
            //
            // and then here:
            //
            //   #if UNITY_IOS && !UNITY_EDITOR
            //   [System.Runtime.InteropServices.DllImport("__Internal")]
            //   static extern int MAQ_GetThermalState();
            //   ...
            //   int state = MAQ_GetThermalState();        // 0..3
            //   Level = state <= 0 ? ThermalLevel.None
            //         : state == 1 ? ThermalLevel.Moderate
            //         : state == 2 ? ThermalLevel.Severe
            //         :              ThermalLevel.Critical;
            //   #endif
            //
            // Note that iOS reports four states, not five, so "fair" maps to Moderate
            // rather than Light - iOS does not signal until it is already meaningful.
        }
    }
}
