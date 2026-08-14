using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Tuning for automatic tier changes - the layer above render scale.
    /// </summary>
    /// <remarks>
    /// Every threshold here is deliberately an order of magnitude slower than the render
    /// scale loop's. Render scale is free to move; a tier change rebuilds the pipeline and
    /// the player sees a hitch, so it has to be worth it.
    /// </remarks>
    [Serializable]
    public class TierAdaptationSettings
    {
        [Tooltip("Let the system change tier on its own. Off means the tier is fixed after " +
                 "startup and only render scale adapts.")]
        public bool enabled = true;

        [Header("Down")]
        [Tooltip("Seconds of being over budget while render scale is already pinned at the " +
                 "tier minimum before dropping a tier. Downward moves are never rationed - " +
                 "if the device cannot hold the tier, nothing is gained by staying on it.")]
        [Range(5f, 300f)]
        public float demoteAfterSeconds = 30f;

        [Header("Up")]
        [Tooltip("Seconds of proven headroom - render scale at the tier maximum and frame " +
                 "times well inside budget - before trying the next tier up.")]
        [Range(5f, 600f)]
        public float promoteAfterSeconds = 60f;

        [Tooltip("p95 must stay below budget x this to count as proven headroom. Stricter " +
                 "than the render scale step-up threshold on purpose: a tier is a much " +
                 "bigger bet than 0.05 of render scale.")]
        [Range(0.1f, 1f)]
        public float promoteHeadroomFactor = 0.6f;

        [Tooltip("How many upward tier changes one session may spend. Downward changes are " +
                 "unlimited. Once the budget is gone the tier can still fall but never rise " +
                 "again this session.")]
        [Range(1, 10)]
        public int maxUpChangesPerSession = 3;

        [Tooltip("If the tier falls again within this many seconds of a promotion, the " +
                 "promotion is judged a failure: upward moves stop for this session and the " +
                 "device is marked settled, so no future session tries that step again.")]
        [Range(10f, 600f)]
        public float probeWatchSeconds = 120f;

        [Header("Memory")]
        [Tooltip("How many past sessions to remember. The most recent one decides the " +
                 "starting tier, so a device that settled on Low starts there instead of " +
                 "rediscovering it every launch.")]
        [Range(1, 20)]
        public int sessionsToRemember = 5;
    }
}
