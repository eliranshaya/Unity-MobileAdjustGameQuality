using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// The per-tier configuration written by the Setup window and read at runtime.
    /// </summary>
    /// <remarks>
    /// A plain serializable class, not a component. It is data that belongs to
    /// <see cref="MobileQualityManager"/>, and giving it its own MonoBehaviour would only
    /// add an inspector header and a second object to keep in sync.
    /// </remarks>
    [Serializable]
    public class TierSettings
    {
        /// <summary>
        /// Index into Unity's Quality Settings list. Written by the Setup window from the
        /// indices the three <c>MAQ</c> levels actually landed on - it is never a constant,
        /// because the list may already contain the project's own levels.
        /// </summary>
        public int unityQualityLevel;

        /// <summary>Frame cap applied through <see cref="Application.targetFrameRate"/>.</summary>
        public int targetFrameRate = 60;

        /// <summary>Render scale applied when the tier is entered.</summary>
        public float baseRenderScale = 1f;

        /// <summary>Lowest render scale the adaptive loop may fall to.</summary>
        public float minRenderScale = 0.8f;

        /// <summary>Highest render scale the adaptive loop may climb to.</summary>
        public float maxRenderScale = 1f;

        /// <summary>
        /// Frame cap used as the last-resort thermal fallback. <c>0</c> means "never drop
        /// the frame rate on this tier" - correct for Low, which is already at 30.
        /// </summary>
        public int thermalFallbackFrameRate;
    }
}
