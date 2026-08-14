namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Reads Unity's Adaptive Performance package, when it is installed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Adaptive Performance is the piece that actually adapts quality at runtime, and it is
    /// deliberately <b>not</b> reimplemented here. It reads real CPU and GPU durations and
    /// thermal headroom from the platform through ADPF, works out whether the frame is CPU
    /// or GPU bound, and moves around sixteen independent URP settings one level at a time.
    /// None of that can be done honestly from <c>Time.unscaledDeltaTime</c>, and none of it
    /// needs a second implementation.
    /// </para>
    /// <para>
    /// This class only reports what Adaptive Performance is seeing, so the overlay can show
    /// it. Everything is behind <c>MAQ_ADAPTIVE_PERFORMANCE</c>, defined by a version define
    /// on the assembly, so the package compiles identically whether or not Adaptive
    /// Performance is installed - no hard dependency, no compile errors either way.
    /// </para>
    /// </remarks>
    public sealed class AdaptivePerformanceBridge
    {
        /// <summary>True when Adaptive Performance is installed, initialised and running.</summary>
        public bool IsActive
        {
            get
            {
#if MAQ_ADAPTIVE_PERFORMANCE
                var holder = UnityEngine.AdaptivePerformance.Holder.Instance;
                return holder != null && holder.Active;
#else
                return false;
#endif
            }
        }

        /// <summary>True when the package is present at all, even if it failed to start.</summary>
        public bool IsInstalled
        {
#if MAQ_ADAPTIVE_PERFORMANCE
            get => true;
#else
            get => false;
#endif
        }

        /// <summary>Thermal warning level, or <c>"n/a"</c> when unavailable.</summary>
        public string ThermalWarning
        {
            get
            {
#if MAQ_ADAPTIVE_PERFORMANCE
                if (!IsActive) return "n/a";
                return UnityEngine.AdaptivePerformance.Holder.Instance.ThermalStatus.ThermalMetrics.WarningLevel.ToString();
#else
                return "n/a";
#endif
            }
        }

        /// <summary>
        /// Normalised temperature level, where 1.0 means the device is throttling. Negative
        /// when the platform does not report it.
        /// </summary>
        public float TemperatureLevel
        {
            get
            {
#if MAQ_ADAPTIVE_PERFORMANCE
                if (!IsActive) return -1f;
                return UnityEngine.AdaptivePerformance.Holder.Instance.ThermalStatus.ThermalMetrics.TemperatureLevel;
#else
                return -1f;
#endif
            }
        }

        /// <summary>Rate of temperature change per second. Negative means cooling.</summary>
        public float TemperatureTrend
        {
            get
            {
#if MAQ_ADAPTIVE_PERFORMANCE
                if (!IsActive) return 0f;
                return UnityEngine.AdaptivePerformance.Holder.Instance.ThermalStatus.ThermalMetrics.TemperatureTrend;
#else
                return 0f;
#endif
            }
        }

        /// <summary>
        /// Real CPU time for the last frame in milliseconds, excluding the frame cap's
        /// sleep. Negative when unavailable.
        /// </summary>
        public float CpuFrameMs => FrameTimeToMs(FrameTime.Cpu);

        /// <summary>Real GPU time for the last frame in milliseconds. Negative when unavailable.</summary>
        public float GpuFrameMs => FrameTimeToMs(FrameTime.Gpu);

        /// <summary>The frame time Adaptive Performance is targeting, in milliseconds.</summary>
        public float TargetFrameMs => FrameTimeToMs(FrameTime.Target);

        /// <summary>Whether the frame is CPU bound, GPU bound or hitting the target.</summary>
        public string Bottleneck
        {
            get
            {
#if MAQ_ADAPTIVE_PERFORMANCE
                if (!IsActive) return "n/a";
                return UnityEngine.AdaptivePerformance.Holder.Instance.PerformanceStatus.PerformanceMetrics.PerformanceBottleneck.ToString();
#else
                return "n/a";
#endif
            }
        }

        /// <summary>The quality index Adaptive Performance's Indexer has settled on.</summary>
        public int QualityIndex
        {
            get
            {
#if MAQ_ADAPTIVE_PERFORMANCE
                var holder = UnityEngine.AdaptivePerformance.Holder.Instance;
                if (holder == null || !holder.Active || holder.Indexer == null) return -1;
                return holder.Indexer.CurrentQualityIndex;
#else
                return -1;
#endif
            }
        }

        enum FrameTime
        {
            Cpu,
            Gpu,
            Target
        }

        static float FrameTimeToMs(FrameTime which)
        {
#if MAQ_ADAPTIVE_PERFORMANCE
            var holder = UnityEngine.AdaptivePerformance.Holder.Instance;
            if (holder == null || !holder.Active)
                return -1f;

            var metrics = holder.PerformanceStatus.FrameTiming;

            // Adaptive Performance reports seconds; the rest of this package speaks
            // milliseconds, and a negative value is its "not measured" marker.
            float seconds = which switch
            {
                FrameTime.Cpu => metrics.CpuFrameTime,
                FrameTime.Gpu => metrics.GpuFrameTime,
                _ => metrics.CurrentFrameTime
            };

            return seconds < 0f ? -1f : seconds * 1000f;
#else
            _ = which;
            return -1f;
#endif
        }
    }
}
