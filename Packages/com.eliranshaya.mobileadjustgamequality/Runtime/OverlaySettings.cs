using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Where the debug overlay pins itself on screen.
    /// </summary>
    public enum OverlayAnchor
    {
        /// <summary>Top-left corner.</summary>
        TopLeft,

        /// <summary>Centred along the top edge.</summary>
        TopCenter,

        /// <summary>Top-right corner.</summary>
        TopRight,

        /// <summary>Centred vertically on the left edge.</summary>
        MiddleLeft,

        /// <summary>Dead centre.</summary>
        MiddleCenter,

        /// <summary>Centred vertically on the right edge.</summary>
        MiddleRight,

        /// <summary>Bottom-left corner.</summary>
        BottomLeft,

        /// <summary>Centred along the bottom edge.</summary>
        BottomCenter,

        /// <summary>Bottom-right corner.</summary>
        BottomRight
    }

    /// <summary>
    /// Which readouts the debug overlay draws. Multi-select in the inspector.
    /// </summary>
    /// <remarks>
    /// A flags enum rather than a row of bools so the inspector shows one dropdown with
    /// checkboxes instead of twelve separate lines, and so adding a readout later does not
    /// change the serialized shape of everything around it.
    /// </remarks>
    [Flags]
    public enum OverlayFields
    {
        /// <summary>Draw nothing but the tier buttons, if those are enabled.</summary>
        None = 0,

        /// <summary>Current tier and the quality level index it maps to.</summary>
        Tier = 1 << 0,

        /// <summary>The tier's target frame rate.</summary>
        TargetFrameRate = 1 << 1,

        /// <summary>The frame budget in milliseconds that target implies.</summary>
        FrameBudget = 1 << 2,

        /// <summary>Smoothed frames per second.</summary>
        CurrentFps = 1 << 3,

        /// <summary>Mean frame time over the sampling window.</summary>
        AverageMs = 1 << 4,

        /// <summary>95th percentile frame time, with an over/ok marker.</summary>
        P95Ms = 1 << 5,

        /// <summary>Render scale, with the tier's minimum and maximum.</summary>
        RenderScale = 1 << 6,

        /// <summary>Thermal status and headroom as the OS reports them.</summary>
        Thermal = 1 << 7,

        /// <summary>Which heat band the response is in: cool, warm or severe.</summary>
        HeatResponse = 1 << 8,

        /// <summary>Whether the oscillation guard is blocking upward steps.</summary>
        OscillationLock = 1 << 9,

        /// <summary>Whether gameplay is currently vetoing changes.</summary>
        Adjustments = 1 << 10,

        /// <summary>GPU name, graphics API and shader level.</summary>
        Device = 1 << 11,

        /// <summary>Everything.</summary>
        Everything = ~0
    }

    /// <summary>
    /// Inspector settings for the on-screen debug overlay.
    /// </summary>
    /// <remarks>
    /// Serialized unconditionally even though <see cref="QualityDebugOverlay"/> itself only
    /// exists under <c>DEVELOPER_MODE</c>. Keeping the settings outside the <c>#if</c> means
    /// the prefab holds identical serialized data either way, so toggling the define never
    /// dirties the asset or drops the values you picked.
    /// </remarks>
    [Serializable]
    public class OverlaySettings
    {
        [Tooltip("Show the overlay. Requires a DEVELOPER_MODE build - in a release build the " +
                 "overlay is compiled out entirely and this does nothing.")]
        public bool show = true;

        [Tooltip("Which corner or edge the panel pins itself to.")]
        public OverlayAnchor anchor = OverlayAnchor.TopLeft;

        [Tooltip("Which readouts to draw.")]
        public OverlayFields fields = OverlayFields.Everything;

        [Tooltip("Show the three buttons that switch tier live.")]
        public bool showTierButtons = true;

        [Tooltip("Multiplies the font size, which is derived from the render target size. " +
                 "Raise it if the overlay is hard to read, lower it if it covers too much " +
                 "of the game.")]
        [Range(0.5f, 3f)]
        public float scale = 1f;

        [Tooltip("Opacity of the panel background.")]
        [Range(0f, 1f)]
        public float backgroundOpacity = 0.78f;

        [Tooltip("Distance from the screen edge, in points.")]
        [Range(0f, 120f)]
        public float margin = 12f;

        /// <summary>Tests whether a readout is enabled.</summary>
        /// <param name="field">The readout to test.</param>
        /// <returns><c>true</c> when that readout should be drawn.</returns>
        public bool Has(OverlayFields field) => (fields & field) != 0;
    }
}
