using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>Where the debug overlay pins itself on screen.</summary>
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
    /// Which groups of readouts the debug overlay draws. Multi-select in the inspector.
    /// </summary>
    /// <remarks>
    /// Grouped rather than one flag per line: a flag per line would be forty entries and
    /// unusable, and the lines within a group are always wanted together.
    /// </remarks>
    [Flags]
    public enum OverlayFields
    {
        /// <summary>Draw nothing but the header.</summary>
        None = 0,

        /// <summary>FPS, average, p95, min and max frame time against the budget.</summary>
        Performance = 1 << 0,

        /// <summary>Quality level, frame cap, vSync, render scale.</summary>
        Quality = 1 << 1,

        /// <summary>What the active URP asset is configured to do.</summary>
        Urp = 1 << 2,

        /// <summary>Which rung of the ladder is applied, and why it last moved.</summary>
        Adaptation = 1 << 3,

        /// <summary>Thermal state as the OS reports it.</summary>
        Thermal = 1 << 4,

        /// <summary>Bottleneck and real CPU/GPU time, when Adaptive Performance is installed.</summary>
        AdaptivePerformance = 1 << 5,

        /// <summary>GPU, graphics API, shader level, RAM, cores.</summary>
        Device = 1 << 6,

        /// <summary>Resolution, DPI, orientation, safe area.</summary>
        Screen = 1 << 7,

        /// <summary>Allocated, reserved and mono heap.</summary>
        Memory = 1 << 8,

        /// <summary>Everything.</summary>
        Everything = ~0,

        /// <summary>
        /// The three groups worth watching all the time: what the frame rate is doing, which
        /// rung the ladder is on, and how hot the device is. Ten lines rather than forty.
        /// </summary>
        Default = Performance | Adaptation | Thermal
    }

    /// <summary>
    /// Inspector settings for the on-screen debug overlay.
    /// </summary>
    /// <remarks>
    /// Serialized unconditionally even though <see cref="QualityDebugOverlay"/> only exists
    /// under <c>MAQ_DEVELOPER_MODE</c>. Keeping the settings outside the <c>#if</c> means the
    /// prefab holds identical data either way, so toggling the define never dirties the asset
    /// or drops the values you picked.
    /// </remarks>
    [Serializable]
    public class OverlaySettings
    {
        [Tooltip("Show the overlay. Requires a MAQ_DEVELOPER_MODE build - in a release build " +
                 "the overlay is compiled out entirely and this does nothing.")]
        public bool show = true;

        [Tooltip("Which corner or edge the panel pins itself to.")]
        public OverlayAnchor anchor = OverlayAnchor.TopLeft;

        [Tooltip("Which groups of readouts to draw. The default three answer the questions " +
                 "you actually have while playing; the rest are there for when you are " +
                 "chasing something specific.")]
        public OverlayFields fields = OverlayFields.Default;

        [Tooltip("Show the small MAQ button at the anchor while the panel is closed, so it " +
                 "can be reopened by tapping. Turn it off for a completely clean screen - " +
                 "F1 and four taps in the top-right corner still work.")]
        public bool showToggleButton = true;

        [Tooltip("Start with the panel open, rather than collapsed to the toggle button.")]
        public bool openOnStart = true;

        [Tooltip("Multiplies the font size, which is derived from the render target size.")]
        [Range(0.5f, 3f)]
        public float scale = 1f;

        [Tooltip("Opacity of the panel background.")]
        [Range(0f, 1f)]
        public float backgroundOpacity = 0.8f;

        [Tooltip("Distance from the screen edge, in points.")]
        [Range(0f, 120f)]
        public float margin = 12f;

        /// <summary>Tests whether a group is enabled.</summary>
        /// <param name="field">The group to test.</param>
        /// <returns><c>true</c> when that group should be drawn.</returns>
        public bool Has(OverlayFields field) => (fields & field) != 0;
    }
}
