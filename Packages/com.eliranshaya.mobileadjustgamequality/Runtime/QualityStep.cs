using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// One rung on the quality ladder. Index 0 is the best-looking rung.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single ladder rather than several independent knobs, because independent knobs
    /// fight. One number - the current rung - is the whole runtime state, which makes the
    /// behaviour something you can read off the overlay and reason about.
    /// </para>
    /// <para>
    /// Only fields URP allows to be written at runtime are here. <c>renderScale</c>,
    /// <c>shadowDistance</c> and <c>mainLightShadowmapResolution</c> have public setters;
    /// <c>supportsMainLightShadows</c> and <c>shadowCascadeCount</c> do not, so shadows are
    /// turned off by setting the distance to zero. That is not a trick: URP computes
    /// <c>cameraRenderShadows = maxShadowDistance &gt; 0</c> and skips the whole shadow pass
    /// when it is false.
    /// </para>
    /// </remarks>
    [Serializable]
    public class QualityStep
    {
        [Tooltip("URP render scale. 1 is native resolution.")]
        [Range(0.4f, 1f)]
        public float renderScale = 1f;

        [Tooltip("Main light shadows, all or nothing. Shadow *distance* is deliberately not a " +
                 "rung: shortening it leaves near objects with shadows and objects a metre " +
                 "further away without, and that boundary reads as a bug rather than as lower " +
                 "quality.")]
        public bool shadowsEnabled = true;

        [Tooltip("Main light shadowmap resolution. 256, 512, 1024 or 2048. Unlike distance " +
                 "this is uniform - every shadow gets softer together, with no visible line " +
                 "across the scene.")]
        public int shadowResolution = 1024;

        [Tooltip("Frame cap for this rung.")]
        public int targetFrameRate = 60;

        [Tooltip("URP MSAA sample count for this rung: 1 (off), 2, 4 or 8. 0 leaves the URP " +
                 "asset's own setting alone, which is how rungs authored before this field behave.")]
        public int msaaSampleCount = 0;

        /// <summary>True when this rung sets MSAA rather than leaving the asset's own.</summary>
        public bool OverridesMsaa => msaaSampleCount > 0;

        /// <summary>True when this rung draws no shadows at all.</summary>
        public bool ShadowsOff => !shadowsEnabled;

        /// <summary>The frame budget this rung's target implies, in milliseconds.</summary>
        public float FrameBudgetMs => targetFrameRate > 0 ? 1000f / targetFrameRate : 0f;

        /// <summary>A one-line description for the overlay and the log.</summary>
        /// <returns>Something like <c>0.80 · shadows 512 · 60fps</c>.</returns>
        public override string ToString() =>
            $"{renderScale:F2} · {(shadowsEnabled ? $"shadows {shadowResolution}" : "no shadows")}" +
            $"{(OverridesMsaa ? $" · msaa {msaaSampleCount}x" : "")} · {targetFrameRate}fps";
    }
}
