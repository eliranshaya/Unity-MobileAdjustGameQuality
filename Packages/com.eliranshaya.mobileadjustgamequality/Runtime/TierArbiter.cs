namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Decides when the whole tier should change, once render scale has run out of room.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the layer above <see cref="AdaptiveScaler"/>. Render scale can only trade
    /// resolution; a tier trades shadows, lights, post-processing and texture budget as
    /// well. It is the bigger lever and the more expensive one, because applying it rebuilds
    /// the pipeline and the player sees a hitch.
    /// </para>
    /// <para>
    /// Hence the asymmetry that runs through the whole class. Dropping a tier is unrationed:
    /// if the device cannot hold the current one, nothing is gained by staying. Raising a
    /// tier is a bet, so it needs proven headroom, costs one of a small per-session budget,
    /// and is watched afterwards - if the tier falls again shortly after a promotion, that
    /// promotion was wrong, and the device is marked settled so no future session pays to
    /// learn it again.
    /// </para>
    /// </remarks>
    public sealed class TierArbiter
    {
        /// <summary>What the arbiter wants done this evaluation.</summary>
        public enum TierStep
        {
            /// <summary>Stay on the current tier.</summary>
            Hold = 0,

            /// <summary>Drop one tier.</summary>
            Down = -1,

            /// <summary>Raise one tier.</summary>
            Up = 1
        }

        readonly TierAdaptationSettings m_Settings;

        float m_StarvedSeconds;
        float m_HeadroomSeconds;
        int m_UpChangesUsed;
        bool m_UpwardLocked;
        bool m_ProbeActive;
        float m_ProbeDeadline;

        /// <summary>Creates an arbiter bound to a settings block.</summary>
        /// <param name="settings">Tuning. Read live, so inspector edits apply.</param>
        public TierArbiter(TierAdaptationSettings settings)
        {
            m_Settings = settings;
        }

        /// <summary>Upward tier changes spent so far this session.</summary>
        public int UpChangesUsed => m_UpChangesUsed;

        /// <summary>True when no further upward tier change will be attempted this session.</summary>
        public bool IsUpwardLocked => m_UpwardLocked || m_UpChangesUsed >= m_Settings.maxUpChangesPerSession;

        /// <summary>True while a promotion is still inside its watch window.</summary>
        public bool IsProbing => m_ProbeActive;

        /// <summary>
        /// Set when a promotion was followed by a demotion inside the watch window. The
        /// manager persists this so future sessions stop trying.
        /// </summary>
        public bool ProbeFailed { get; private set; }

        /// <summary>
        /// Clears the accumulators. Called after a tier change or a long stall, where the
        /// measurements no longer describe the present. The session budget and the probe
        /// verdict deliberately survive.
        /// </summary>
        public void ResetWindow()
        {
            m_StarvedSeconds = 0f;
            m_HeadroomSeconds = 0f;
        }

        /// <summary>
        /// Stops this session from trying to raise the tier again. Used when the device
        /// arrives already marked settled from a previous session.
        /// </summary>
        public void LockUpward()
        {
            m_UpwardLocked = true;
        }

        /// <summary>
        /// Runs one decision.
        /// </summary>
        /// <param name="now">Current unscaled time, in seconds.</param>
        /// <param name="elapsedSeconds">Real seconds since the previous evaluation.</param>
        /// <param name="p95Ms">95th percentile frame time over the window.</param>
        /// <param name="budgetMs">The current tier's frame budget.</param>
        /// <param name="atMinScale">Render scale is already at the tier minimum.</param>
        /// <param name="atMaxScale">Render scale is already at the tier maximum.</param>
        /// <param name="canGoDown">A lower tier exists.</param>
        /// <param name="canGoUp">A higher tier exists.</param>
        /// <param name="deviceWarm">The thermal sensor says not to raise quality.</param>
        /// <param name="overBudgetFactor">Shared with the render scale loop.</param>
        /// <returns>The tier step to apply.</returns>
        public TierStep Evaluate(float now, float elapsedSeconds, float p95Ms, float budgetMs,
            bool atMinScale, bool atMaxScale, bool canGoDown, bool canGoUp, bool deviceWarm,
            float overBudgetFactor)
        {
            if (!m_Settings.enabled)
                return TierStep.Hold;

            // Starved: render scale has already given everything it has and the frames are
            // still not there. Only then is the tier the thing that is wrong.
            bool starved = atMinScale && p95Ms > budgetMs * overBudgetFactor;
            m_StarvedSeconds = starved ? m_StarvedSeconds + elapsedSeconds : 0f;

            if (starved && canGoDown && m_StarvedSeconds >= m_Settings.demoteAfterSeconds)
            {
                m_StarvedSeconds = 0f;
                m_HeadroomSeconds = 0f;

                // A tier that falls back this soon after being raised was raised in error.
                if (m_ProbeActive && now <= m_ProbeDeadline)
                {
                    m_ProbeActive = false;
                    ProbeFailed = true;
                    m_UpwardLocked = true;
                    QualityDevLog.Warn(
                        "Tier promotion did not hold. Upward tier changes are off for this session, " +
                        "and the device is marked settled so future sessions will not try it either.");
                }

                return TierStep.Down;
            }

            // The probe survived its watch window, so the promotion stands.
            if (m_ProbeActive && now > m_ProbeDeadline)
                m_ProbeActive = false;

            bool headroom = atMaxScale && !deviceWarm && p95Ms < budgetMs * m_Settings.promoteHeadroomFactor;
            m_HeadroomSeconds = headroom ? m_HeadroomSeconds + elapsedSeconds : 0f;

            if (!headroom || !canGoUp || IsUpwardLocked)
                return TierStep.Hold;

            if (m_HeadroomSeconds < m_Settings.promoteAfterSeconds)
                return TierStep.Hold;

            m_HeadroomSeconds = 0f;
            m_StarvedSeconds = 0f;
            m_UpChangesUsed++;
            m_ProbeActive = true;
            m_ProbeDeadline = now + m_Settings.probeWatchSeconds;

            return TierStep.Up;
        }
    }
}
