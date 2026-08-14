namespace MobileAdjustGameQuality
{
    /// <summary>
    /// The three graphics quality tiers this package ships with.
    /// </summary>
    /// <remarks>
    /// The integer values are part of the serialized data: they index into
    /// <see cref="MobileQualityManager"/>'s tier array and are persisted in
    /// <c>PlayerPrefs</c>. Never reorder or renumber them.
    /// </remarks>
    public enum QualityTier
    {
        /// <summary>Lowest cost. Targets 30 FPS on entry-level phones.</summary>
        Low = 0,

        /// <summary>The default. Targets 60 FPS on mainstream phones.</summary>
        Balanced = 1,

        /// <summary>Full quality. Targets 60 FPS on flagship phones.</summary>
        High = 2
    }
}
