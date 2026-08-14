using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Tuning constants for the adaptive render scale loop.
    /// </summary>
    /// <remarks>
    /// Grouped into one serializable object so the component shows a single collapsed
    /// "Adaptive" foldout instead of a wall of twelve loose numbers. The defaults are the
    /// shipping values; nothing here needs to be filled in.
    /// </remarks>
    [Serializable]
    public class AdaptiveSettings
    {
        [Header("Sampling")]
        [Tooltip("How many frames the p95 window covers. 120 is about two seconds at 60 FPS.")]
        [Range(30, 480)]
        public int sampleCount = 120;

        [Tooltip("Seconds between decisions.")]
        [Range(0.1f, 5f)]
        public float evaluateInterval = 0.5f;

        [Tooltip("Seconds of measurement discarded after start and after every tier change.")]
        [Range(0f, 15f)]
        public float warmupSeconds = 3f;

        [Header("Thresholds")]
        [Tooltip("p95 above budget x this counts as over budget.")]
        [Range(1f, 1.5f)]
        public float overBudgetFactor = 1.08f;

        [Tooltip("p95 below budget x this counts as headroom to spend.")]
        [Range(0.3f, 1f)]
        public float underBudgetFactor = 0.75f;

        [Header("Hysteresis")]
        [Tooltip("Seconds sustained over budget before stepping down.")]
        [Range(0.5f, 30f)]
        public float sustainedOverSeconds = 4f;

        [Tooltip("Seconds sustained under budget before stepping up. Deliberately much " +
                 "longer than the step-down window: a wrong step down costs sharpness, " +
                 "a wrong step up costs frames.")]
        [Range(0.5f, 60f)]
        public float sustainedUnderSeconds = 12f;

        [Tooltip("Seconds of quiet after a step down.")]
        [Range(1f, 120f)]
        public float cooldownAfterDownSeconds = 20f;

        [Tooltip("Seconds of quiet after a step up.")]
        [Range(1f, 120f)]
        public float cooldownAfterUpSeconds = 30f;

        [Tooltip("Render scale change per step.")]
        [Range(0.01f, 0.25f)]
        public float stepSize = 0.05f;

        [Header("Oscillation guard")]
        [Tooltip("How many consecutive down-then-up cycles are allowed before upward steps " +
                 "are locked out. A device that keeps flip-flopping is a device that has no " +
                 "stable headroom, and every flip costs a render target reallocation.")]
        [Range(1, 6)]
        public int oscillationCycleLimit = 2;

        [Tooltip("How long upward steps stay locked the first time oscillation is detected. " +
                 "If the pattern comes back after this expires, the lock becomes permanent " +
                 "for the rest of the session.")]
        [Range(10f, 1800f)]
        public float oscillationLockSeconds = 300f;

        /// <summary>Frames the sampler keeps, clamped to something usable.</summary>
        public int SafeSampleCount => Mathf.Max(8, sampleCount);

        /// <summary>
        /// Below this many samples p95 is just "the worst frame so far", so the loop
        /// holds instead of deciding.
        /// </summary>
        public int MinimumSamplesToDecide => Mathf.Min(20, SafeSampleCount);
    }
}
