namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Decides which rung of the quality ladder the game should be on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plain class with no Unity dependency beyond the numbers handed to it, so the whole
    /// policy sits in one readable file and can be reasoned about without a scene.
    /// </para>
    /// <para>
    /// It decides but never applies. <see cref="MobileQualityManager"/> owns the ladder and
    /// does the applying.
    /// </para>
    /// </remarks>
    public sealed class QualityGovernor
    {
        /// <summary>Why the ladder last moved, or did not.</summary>
        public enum Reason
        {
            /// <summary>Nothing to do.</summary>
            Holding,

            /// <summary>Waiting out the cooldown from the last change.</summary>
            Cooldown,

            /// <summary>Waiting for the frames after a change to settle.</summary>
            Settling,

            /// <summary>Frame times are over budget.</summary>
            MissingTarget,

            /// <summary>Frame times are so far over that the frame rate itself is wrong.</summary>
            WrongFrameRate,

            /// <summary>The device reports itself warm.</summary>
            Heat,

            /// <summary>Cool and holding the target, so climbing back.</summary>
            Recovered,

            /// <summary>Already at the best rung.</summary>
            AtBest,

            /// <summary>Already at the worst rung.</summary>
            AtWorst,

            /// <summary>This rung has been tried and lost too many times.</summary>
            RiseAttemptsSpent
        }

        readonly AdaptationSettings m_Settings;
        readonly int[] m_RiseAttempts;

        float m_MissingForSeconds;
        float m_BadlyMissingForSeconds;
        float m_HoldingForSeconds;
        float m_WarmForSeconds;
        float m_CooldownUntil;
        float m_SettledAt;

        /// <summary>Creates a governor for a ladder of a given size.</summary>
        /// <param name="settings">Tuning. Read live, so inspector edits apply.</param>
        /// <param name="stepCount">How many rungs the ladder has.</param>
        public QualityGovernor(AdaptationSettings settings, int stepCount)
        {
            m_Settings = settings;
            m_RiseAttempts = new int[stepCount < 1 ? 1 : stepCount];
        }

        /// <summary>Why the last evaluation did what it did.</summary>
        public Reason LastReason { get; private set; } = Reason.Holding;

        /// <summary>
        /// Clears the accumulators and starts the settle window. Called after every change,
        /// and after a stall long enough that the measurements describe a moment that is over.
        /// </summary>
        /// <param name="now">Current unscaled time.</param>
        public void Reset(float now)
        {
            m_MissingForSeconds = 0f;
            m_BadlyMissingForSeconds = 0f;
            m_HoldingForSeconds = 0f;
            m_WarmForSeconds = 0f;
            m_SettledAt = now + m_Settings.settleSeconds;
        }

        /// <summary>
        /// Records that a rung was climbed to, so a rung that keeps failing is eventually
        /// left alone.
        /// </summary>
        /// <param name="step">The rung climbed to.</param>
        public void NoteRiseAttempt(int step)
        {
            if (step >= 0 && step < m_RiseAttempts.Length)
                m_RiseAttempts[step]++;
        }

        /// <summary>How many times a rung has been climbed back to this session.</summary>
        /// <param name="step">The rung to query.</param>
        /// <returns>The attempt count.</returns>
        public int RiseAttempts(int step) =>
            step >= 0 && step < m_RiseAttempts.Length ? m_RiseAttempts[step] : 0;

        /// <summary>
        /// Runs one decision.
        /// </summary>
        /// <param name="now">Current unscaled time, in seconds.</param>
        /// <param name="elapsed">Real seconds since the previous evaluation.</param>
        /// <param name="frameMs">The frame time decisions are made on - p75, not p95.</param>
        /// <param name="budgetMs">The current rung's frame budget.</param>
        /// <param name="step">The rung currently applied.</param>
        /// <param name="worstStep">Index of the last rung on the ladder.</param>
        /// <param name="halveFrameRateStep">
        /// The first rung that drops the frame rate below the current one, or <c>-1</c> when
        /// there is none.
        /// </param>
        /// <param name="warm">The device reports itself warm.</param>
        /// <param name="severe">The device reports severe throttling.</param>
        /// <param name="calibrating">
        /// Whether the opening calibration window is still open. It shortens every window, so
        /// the device lands on the right rung while the player is still loading in.
        /// </param>
        /// <returns>The rung to move to. Equal to <paramref name="step"/> means hold.</returns>
        public int Evaluate(float now, float elapsed, float frameMs, float budgetMs,
            int step, int worstStep, int halveFrameRateStep, bool warm, bool severe, bool calibrating)
        {
            if (!m_Settings.enabled)
            {
                LastReason = Reason.Holding;
                return step;
            }

            bool missing = budgetMs > 0f && frameMs > budgetMs * m_Settings.missTargetFactor;
            bool badlyMissing = budgetMs > 0f && frameMs > budgetMs * m_Settings.wrongFrameRateFactor;
            bool holding = budgetMs > 0f && frameMs <= budgetMs * m_Settings.holdingTargetFactor;

            m_MissingForSeconds = missing ? m_MissingForSeconds + elapsed : 0f;
            m_BadlyMissingForSeconds = badlyMissing ? m_BadlyMissingForSeconds + elapsed : 0f;
            m_HoldingForSeconds = holding && !warm ? m_HoldingForSeconds + elapsed : 0f;
            m_WarmForSeconds = warm ? m_WarmForSeconds + elapsed : 0f;

            // Severe heat is the one case that ignores every timer. The OS is already taking
            // performance away; the only question is whether we choose what to give up.
            if (severe && step < worstStep)
            {
                LastReason = Reason.Heat;
                return Drop(now, step, worstStep, calibrating);
            }

            if (now < m_SettledAt)
            {
                LastReason = Reason.Settling;
                return step;
            }

            if (now < m_CooldownUntil)
            {
                LastReason = Reason.Cooldown;
                return step;
            }

            // Badly missing means the device is not short of a target, it is on the wrong
            // target. Walking down one rung at a time would spend twenty seconds overheating
            // to reach a conclusion already visible in the numbers.
            if (m_BadlyMissingForSeconds >= m_Settings.wrongFrameRateSeconds
                && halveFrameRateStep > step && halveFrameRateStep <= worstStep)
            {
                LastReason = Reason.WrongFrameRate;
                m_CooldownUntil = now + m_Settings.DropCooldown(calibrating);
                Reset(now);
                return halveFrameRateStep;
            }

            if (m_MissingForSeconds >= m_Settings.DropWindow(calibrating))
            {
                LastReason = step < worstStep ? Reason.MissingTarget : Reason.AtWorst;
                return Drop(now, step, worstStep, calibrating);
            }

            if (m_WarmForSeconds >= m_Settings.heatStepSeconds)
            {
                LastReason = step < worstStep ? Reason.Heat : Reason.AtWorst;
                return Drop(now, step, worstStep, calibrating);
            }

            if (m_HoldingForSeconds < m_Settings.RiseWindow(calibrating))
            {
                LastReason = Reason.Holding;
                return step;
            }

            if (step <= 0)
            {
                LastReason = Reason.AtBest;
                return step;
            }

            int target = step - 1;
            if (RiseAttempts(target) >= m_Settings.maxRiseAttemptsPerStep)
            {
                LastReason = Reason.RiseAttemptsSpent;
                m_HoldingForSeconds = 0f;
                return step;
            }

            LastReason = Reason.Recovered;
            m_CooldownUntil = now + m_Settings.RiseCooldown(calibrating);
            NoteRiseAttempt(target);
            Reset(now);
            return target;
        }

        int Drop(float now, int step, int worstStep, bool calibrating)
        {
            if (step >= worstStep)
                return step;

            m_CooldownUntil = now + m_Settings.DropCooldown(calibrating);
            Reset(now);
            return step + 1;
        }
    }
}
