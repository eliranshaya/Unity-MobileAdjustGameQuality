using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Watches real frame times and nudges the render scale to keep the current tier's
    /// frame budget. It never changes tier and never touches any other graphics setting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Render scale is the only knob moved at runtime because it is the only one that is
    /// free to change: shadow resolution, light counts and the rest live on the URP asset
    /// and changing them mid-session forces a pipeline rebuild, which the player sees as
    /// a hitch. Render scale only resizes render targets.
    /// </para>
    /// <para>
    /// Decisions use p95 frame time in milliseconds, never FPS. Averaged FPS hides exactly
    /// the frames players notice - a 60 FPS average with a 45 ms spike every second reads
    /// as "smooth" to an average and as "stuttering" to a person. p95 in milliseconds is
    /// linear in the thing being controlled, so a 10% overshoot means 10% too much work.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(QualityTierManager))]
    [AddComponentMenu("Mobile Adjust Game Quality/Adaptive Quality Controller")]
    [DisallowMultipleComponent]
    public class AdaptiveQualityController : MonoBehaviour
    {
        [Header("Sampling")]
        [Tooltip("How many frames the p95 window covers. 120 is about two seconds at 60 FPS.")]
        [SerializeField, Range(30, 480)]
        int sampleCount = 120;

        [Tooltip("Seconds between decisions.")]
        [SerializeField, Range(0.1f, 5f)]
        float evaluateInterval = 0.5f;

        [Tooltip("Seconds of measurement discarded after start and after every tier change.")]
        [SerializeField, Range(0f, 15f)]
        float warmupSeconds = 3f;

        [Header("Thresholds")]
        [Tooltip("p95 above budget x this counts as over budget.")]
        [SerializeField, Range(1.0f, 1.5f)]
        float overBudgetFactor = 1.08f;

        [Tooltip("p95 below budget x this counts as headroom to spend.")]
        [SerializeField, Range(0.3f, 1.0f)]
        float underBudgetFactor = 0.75f;

        [Header("Hysteresis")]
        [Tooltip("Seconds sustained over budget before stepping down.")]
        [SerializeField, Range(0.5f, 30f)]
        float sustainedOverSeconds = 4f;

        [Tooltip("Seconds sustained under budget before stepping up. Deliberately much " +
                 "longer than the step-down window: a wrong step down costs sharpness, " +
                 "a wrong step up costs frames.")]
        [SerializeField, Range(0.5f, 60f)]
        float sustainedUnderSeconds = 12f;

        [Tooltip("Seconds of quiet after a step down.")]
        [SerializeField, Range(1f, 120f)]
        float cooldownAfterDownSeconds = 20f;

        [Tooltip("Seconds of quiet after a step up.")]
        [SerializeField, Range(1f, 120f)]
        float cooldownAfterUpSeconds = 30f;

        [Tooltip("Render scale change per step.")]
        [SerializeField, Range(0.01f, 0.25f)]
        float stepSize = 0.05f;

        QualityTierManager m_Manager;
        ThermalMonitor m_Thermal;

        float[] m_Samples;
        float[] m_SortScratch;
        int m_SampleHead;
        int m_SampleFilled;

        float m_NextEvaluateTime;
        float m_LastEvaluateTime;
        float m_WarmupUntil;
        float m_CooldownUntil;
        float m_OverForSeconds;
        float m_UnderForSeconds;

        bool m_ThermalPinned;
        bool m_ThermalFrameRateDropped;

        /// <summary>
        /// Gate for gameplay. Set to <c>false</c> during combat, cutscenes or anything
        /// else where a resolution change would be noticed, and back to <c>true</c> at a
        /// natural break. Measurement continues while this is <c>false</c>, so a change
        /// that became due mid-fight is applied the moment the fight ends rather than
        /// restarting its timer from zero.
        /// </summary>
        public bool AllowAdjustments { get; set; } = true;

        /// <summary>The most recent 95th-percentile frame time, in milliseconds.</summary>
        public float LastP95Ms { get; private set; }

        /// <summary>The most recent mean frame time, in milliseconds. Reporting only.</summary>
        public float LastAvgMs { get; private set; }

        /// <summary>True while the thermal fallback has taken over from normal adaptation.</summary>
        public bool IsThermalThrottled => m_ThermalPinned || m_ThermalFrameRateDropped;

        /// <summary>Raised whenever the render scale actually moves. Carries the new scale.</summary>
        public event Action<float> OnRenderScaleChanged;

        void Awake()
        {
            m_Manager = GetComponent<QualityTierManager>();
            m_Thermal = GetComponent<ThermalMonitor>();

            m_Samples = new float[Mathf.Max(8, sampleCount)];
            m_SortScratch = new float[m_Samples.Length];
        }

        void OnEnable()
        {
            if (m_Manager != null)
                m_Manager.OnTierChanged += HandleTierChanged;

            ResetWindow();
        }

        void OnDisable()
        {
            if (m_Manager != null)
                m_Manager.OnTierChanged -= HandleTierChanged;
        }

        void Update()
        {
            // Unscaled: a paused or slow-motion game still has to render on time.
            float frameMs = Time.unscaledDeltaTime * 1000f;

            m_Samples[m_SampleHead] = frameMs;
            m_SampleHead = (m_SampleHead + 1) % m_Samples.Length;
            if (m_SampleFilled < m_Samples.Length)
                m_SampleFilled++;

            float now = Time.unscaledTime;
            if (now < m_WarmupUntil || now < m_NextEvaluateTime)
                return;

            float elapsed = Mathf.Max(0f, now - m_LastEvaluateTime);
            m_LastEvaluateTime = now;
            m_NextEvaluateTime = now + evaluateInterval;

            // A window shorter than this makes p95 a synonym for "the worst frame".
            if (m_SampleFilled < 20)
                return;

            Evaluate(now, elapsed);
        }

        void Evaluate(float now, float elapsedSeconds)
        {
            ComputeStatistics(out float p95, out float avg);
            LastP95Ms = p95;
            LastAvgMs = avg;

            float budget = m_Manager.FrameBudgetMs;
            bool over = p95 > budget * overBudgetFactor;
            bool under = p95 < budget * underBudgetFactor;

            if (over)
            {
                m_OverForSeconds += elapsedSeconds;
                m_UnderForSeconds = 0f;
            }
            else if (under)
            {
                m_UnderForSeconds += elapsedSeconds;
                m_OverForSeconds = 0f;
            }
            else
            {
                m_OverForSeconds = 0f;
                m_UnderForSeconds = 0f;
            }

            // Gameplay veto covers everything, thermal response included. A resolution
            // drop in the middle of a boss fight is worse than four more warm seconds.
            if (!AllowAdjustments)
                return;

            if (HandleThermal(over))
                return;

            if (now < m_CooldownUntil)
                return;

            QualityTierManager.TierSettings settings = m_Manager.Current;
            float scale = m_Manager.CurrentRenderScale;

            if (m_OverForSeconds >= sustainedOverSeconds && scale > settings.minRenderScale)
            {
                StepRenderScale(-stepSize, $"p95 {p95:F1}ms over {budget:F1}ms budget");
                m_CooldownUntil = now + cooldownAfterDownSeconds;
                m_OverForSeconds = 0f;
                return;
            }

            if (m_UnderForSeconds >= sustainedUnderSeconds && scale < settings.maxRenderScale)
            {
                // Never spend headroom on a device that is already heating up: the gain
                // lasts seconds and the throttle that follows lasts minutes.
                if (m_Thermal != null && m_Thermal.IsWarm)
                {
                    m_UnderForSeconds = 0f;
                    QualityDevLog.Log("Headroom available but the device is warm; holding render scale.");
                    return;
                }

                StepRenderScale(stepSize, $"p95 {p95:F1}ms under {budget:F1}ms budget");
                m_CooldownUntil = now + cooldownAfterUpSeconds;
                m_UnderForSeconds = 0f;
            }
        }

        /// <summary>
        /// Applies or unwinds the thermal fallback.
        /// </summary>
        /// <param name="overBudget">Whether p95 is currently over the frame budget.</param>
        /// <returns>True when thermal handling owns this evaluation and normal adaptation must be skipped.</returns>
        bool HandleThermal(bool overBudget)
        {
            if (m_Thermal == null)
                return false;

            if (m_Thermal.IsSevere)
            {
                // Order matters, and this is the whole point of the two-step response:
                // pinning the render scale costs sharpness, which players tolerate and
                // often do not consciously notice. Halving the frame rate is the single
                // most noticeable change the game can make, so it is the last resort and
                // only happens if the cheaper move was not enough.
                if (!m_ThermalPinned)
                {
                    m_ThermalPinned = true;
                    float min = m_Manager.Current.minRenderScale;
                    if (!Mathf.Approximately(min, m_Manager.CurrentRenderScale))
                    {
                        m_Manager.SetRenderScale(min);
                        OnRenderScaleChanged?.Invoke(m_Manager.CurrentRenderScale);
                    }
                    QualityDevLog.Warn($"Thermal: severe. Render scale pinned to {min:F2}.");
                    return true;
                }

                int fallback = m_Manager.Current.thermalFallbackFrameRate;
                if (overBudget && !m_ThermalFrameRateDropped && fallback > 0)
                {
                    m_ThermalFrameRateDropped = true;
                    m_Manager.SetTargetFrameRate(fallback);
                    QualityDevLog.Warn($"Thermal: still over budget at minimum render scale. Frame cap dropped to {fallback}.");
                }

                return true;
            }

            // Cooled down. Unwind in reverse order, one step per evaluation, so the
            // player gets the frame rate back before the resolution starts climbing.
            if (m_ThermalFrameRateDropped)
            {
                m_ThermalFrameRateDropped = false;
                m_Manager.RestoreTargetFrameRate();
                QualityDevLog.Log("Thermal: cooled. Frame cap restored.");
                return true;
            }

            if (m_ThermalPinned)
            {
                m_ThermalPinned = false;
                m_UnderForSeconds = 0f;
                m_CooldownUntil = Time.unscaledTime + cooldownAfterUpSeconds;
                QualityDevLog.Log("Thermal: cooled. Render scale released.");
                return true;
            }

            return false;
        }

        void StepRenderScale(float delta, string reason)
        {
            float before = m_Manager.CurrentRenderScale;
            m_Manager.SetRenderScale(before + delta);
            float after = m_Manager.CurrentRenderScale;

            if (Mathf.Approximately(before, after))
                return;

            QualityDevLog.Log($"Render scale {before:F2} -> {after:F2} ({reason}).");
            OnRenderScaleChanged?.Invoke(after);
        }

        void ComputeStatistics(out float p95, out float avg)
        {
            int count = m_SampleFilled;

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                float value = m_Samples[i];
                m_SortScratch[i] = value;
                sum += value;
            }

            // Array.Sort over a pre-allocated scratch buffer: no LINQ, no allocation, and
            // this runs twice a second for the lifetime of the app.
            Array.Sort(m_SortScratch, 0, count);

            int index = Mathf.Clamp(Mathf.CeilToInt(0.95f * count) - 1, 0, count - 1);
            p95 = m_SortScratch[index];
            avg = sum / count;
        }

        void HandleTierChanged(QualityTier tier)
        {
            // The frames right after a pipeline rebuild are shader compilation and texture
            // reloads, not gameplay. Measuring them would trigger an immediate step down.
            ResetWindow();
        }

        void ResetWindow()
        {
            m_SampleHead = 0;
            m_SampleFilled = 0;
            m_OverForSeconds = 0f;
            m_UnderForSeconds = 0f;

            float now = Time.unscaledTime;
            m_WarmupUntil = now + warmupSeconds;
            m_LastEvaluateTime = m_WarmupUntil;
            m_NextEvaluateTime = m_WarmupUntil + evaluateInterval;
            m_CooldownUntil = 0f;
        }
    }
}
