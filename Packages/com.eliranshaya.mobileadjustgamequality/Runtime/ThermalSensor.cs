using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// How hot the operating system reports the device to be.
    /// </summary>
    /// <remarks>
    /// These are <b>not</b> temperatures. Android does not expose a number in degrees to
    /// applications, and deliberately so: the temperature at which one phone throttles is
    /// meaningless on another, because it depends on the chassis, the cooling and the
    /// vendor's own policy. What the OS gives instead is its own verdict about how much it
    /// is currently holding the hardware back. Mirrors
    /// <c>PowerManager.THERMAL_STATUS_*</c> for values 0-4; Android also defines
    /// EMERGENCY (5) and SHUTDOWN (6), which are clamped to <see cref="Critical"/> because
    /// at that point the OS is about to kill the process and no render scale will help.
    /// </remarks>
    public enum ThermalLevel
    {
        /// <summary>No thermal API on this platform, or the query failed.</summary>
        Unknown = -1,

        /// <summary>No throttling at all.</summary>
        None = 0,

        /// <summary>Light throttling. The player cannot see it yet, but headroom is gone.</summary>
        Light = 1,

        /// <summary>Moderate throttling. Frame times start to drift.</summary>
        Moderate = 2,

        /// <summary>Severe throttling. The OS is actively clocking the chip down.</summary>
        Severe = 3,

        /// <summary>Critical. Give quality back immediately.</summary>
        Critical = 4
    }

    /// <summary>
    /// Reads the platform thermal API so the adaptive loop can tell "this device is slow"
    /// apart from "this device got hot".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction decides the response. A slow device should settle at a lower render
    /// scale and stay there. A hot device should be given quality back and then allowed to
    /// climb again once it cools. Frame times alone cannot separate the two.
    /// </para>
    /// <para>
    /// Plain class rather than a MonoBehaviour: it owns JNI handles and a poll method, and
    /// nothing else. <see cref="MobileQualityManager"/> calls <see cref="Poll"/> from a
    /// coroutine and disposes it on destroy.
    /// </para>
    /// </remarks>
    public sealed class ThermalSensor : IDisposable
    {
        /// <summary>
        /// Headroom is a forecast on a 0..1+ scale where 1.0 means "throttling right now".
        /// Treating 0.85 as warm buys the loop a few seconds of warning before the OS acts,
        /// which is the difference between holding quality and having it taken away.
        /// </summary>
        public const float WarmHeadroom = 0.85f;

        /// <summary>The forecast window passed to <c>getThermalHeadroom</c>, in seconds.</summary>
        const int k_HeadroomForecastSeconds = 10;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject m_PowerManager;
        AndroidJavaObject m_Activity;
        int m_ApiLevel;
        bool m_Failed;
#endif

        /// <summary>The most recent status. <see cref="ThermalLevel.Unknown"/> if unavailable.</summary>
        public ThermalLevel Level { get; private set; } = ThermalLevel.Unknown;

        /// <summary>
        /// The most recent headroom forecast, or <c>NaN</c> when unavailable. Approaching
        /// 1.0 means throttling is imminent.
        /// </summary>
        public float Headroom { get; private set; } = float.NaN;

        /// <summary>True when the platform has reported a usable value at least once.</summary>
        public bool IsAvailable => Level != ThermalLevel.Unknown || !float.IsNaN(Headroom);

        /// <summary>
        /// True when quality must not be raised. Deliberately conservative: stepping up on
        /// a warm device is the change most likely to be undone seconds later.
        /// </summary>
        public bool IsWarm
        {
            get
            {
                if (Level >= ThermalLevel.Light)
                    return true;
                return !float.IsNaN(Headroom) && Headroom >= WarmHeadroom;
            }
        }

        /// <summary>True when quality must actively be given back, not merely held.</summary>
        public bool IsSevere
        {
            get
            {
                if (Level >= ThermalLevel.Severe)
                    return true;
                return !float.IsNaN(Headroom) && Headroom >= 1f;
            }
        }

        /// <summary>
        /// Acquires the platform handles. Safe to call on any platform; does nothing where
        /// there is no thermal API.
        /// </summary>
        public void Open()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Every JNI call is wrapped: several OEM ROMs throw from getSystemService or
            // from the thermal methods themselves, and an uncaught exception during startup
            // takes the whole app down.
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
                QualityDevLog.Warn($"Thermal API unavailable, falling back to frame times only: {e.Message}");
                Dispose();
            }
#endif
        }

        /// <summary>Reads the current thermal state. Cheap, and safe to call from a coroutine.</summary>
        public void Poll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_Failed || m_PowerManager == null)
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
                    // good value rather than reading NaN as cool.
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

            // iOS thermal state is available through ProcessInfo, but it needs a native
            // plugin because Unity does not surface it. This package deliberately does not
            // generate that plugin - dropping an unsigned .mm into someone's project is a
            // build-configuration surprise, not a feature. If you want it, add:
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
            // Note iOS reports four states, not five, so "fair" maps to Moderate rather
            // than Light - iOS does not signal until it already means something.
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
