namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Decides when the render scale should move, and refuses to let it oscillate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plain class with no Unity dependency beyond the numbers handed to it, so the whole
    /// policy - thresholds, hysteresis, cooldowns, the oscillation guard - can be read in
    /// one file and reasoned about without a scene.
    /// </para>
    /// <para>
    /// It decides but never applies. <see cref="MobileQualityManager"/> owns the render
    /// scale and does the applying.
    /// </para>
    /// </remarks>
    public sealed class AdaptiveScaler
    {
        /// <summary>What the scaler wants done this evaluation.</summary>
        public enum Step
        {
            /// <summary>Change nothing.</summary>
            Hold = 0,

            /// <summary>Lower the render scale by one step.</summary>
            Down = -1,

            /// <summary>Raise the render scale by one step.</summary>
            Up = 1
        }

        readonly AdaptiveSettings m_Settings;

        float m_OverForSeconds;
        float m_UnderForSeconds;
        float m_CooldownUntil;

        Step m_LastStep = Step.Hold;
        int m_Cycles;
        bool m_HasLockedOnce;
        bool m_SessionLocked;
        float m_UpLockUntil;

        /// <summary>Creates a scaler bound to a settings block.</summary>
        /// <param name="settings">Tuning constants. Read live, so inspector edits apply.</param>
        public AdaptiveScaler(AdaptiveSettings settings)
        {
            m_Settings = settings;
        }

        /// <summary>Completed down-then-up cycles counted since the last reset.</summary>
        public int OscillationCycles => m_Cycles;

        /// <summary>True when upward steps are locked out for the rest of the session.</summary>
        public bool IsLockedForSession => m_SessionLocked;

        /// <summary>Seconds of temporary upward lock left, or <c>0</c> when not locked.</summary>
        /// <param name="now">Current unscaled time.</param>
        /// <returns>Remaining lock time in seconds.</returns>
        public float RemainingLockSeconds(float now)
        {
            if (m_SessionLocked)
                return float.PositiveInfinity;
            float remaining = m_UpLockUntil - now;
            return remaining > 0f ? remaining : 0f;
        }

        /// <summary>True when the render scale is currently not allowed to climb.</summary>
        /// <param name="now">Current unscaled time.</param>
        /// <returns>Whether upward steps are blocked.</returns>
        public bool IsUpwardLocked(float now) => m_SessionLocked || now < m_UpLockUntil;

        /// <summary>
        /// Clears the hysteresis accumulators and the cooldown. Called after a tier change
        /// or a long stall, where the measurements no longer describe the present.
        /// </summary>
        /// <remarks>
        /// The oscillation state is deliberately <b>not</b> cleared. A device that
        /// flip-flops is still that device after a scene load, and resetting the count
        /// would let it start the whole pattern over from scratch.
        /// </remarks>
        public void ResetWindow()
        {
            m_OverForSeconds = 0f;
            m_UnderForSeconds = 0f;
            m_CooldownUntil = 0f;
        }

        /// <summary>
        /// Folds one evaluation's worth of measurement into the hysteresis timers.
        /// </summary>
        /// <remarks>
        /// Kept separate from <see cref="Decide"/> so measurement continues while gameplay
        /// has changes vetoed. A step that became due during a boss fight is then applied
        /// the moment the fight ends, instead of restarting its twelve-second timer from
        /// zero at exactly the point the player stops looking.
        /// </remarks>
        /// <param name="elapsedSeconds">Real seconds since the previous evaluation.</param>
        /// <param name="p95Ms">95th percentile frame time over the window.</param>
        /// <param name="budgetMs">The current tier's frame budget.</param>
        public void Accumulate(float elapsedSeconds, float p95Ms, float budgetMs)
        {
            bool over = p95Ms > budgetMs * m_Settings.overBudgetFactor;
            bool under = p95Ms < budgetMs * m_Settings.underBudgetFactor;

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
        }

        /// <summary>
        /// Decides what to do with the render scale, given what
        /// <see cref="Accumulate"/> has seen so far.
        /// </summary>
        /// <param name="now">Current unscaled time, in seconds.</param>
        /// <param name="currentScale">Render scale in effect right now.</param>
        /// <param name="minScale">Lowest scale this tier allows.</param>
        /// <param name="maxScale">Highest scale this tier allows.</param>
        /// <param name="deviceWarm">Whether the thermal sensor says not to raise quality.</param>
        /// <returns>The step to apply.</returns>
        public Step Decide(float now, float currentScale, float minScale, float maxScale, bool deviceWarm)
        {
            if (now < m_CooldownUntil)
                return Step.Hold;

            // Down first, and unconditionally: protecting the frame rate outranks
            // everything else here, including both kinds of oscillation lock.
            if (m_OverForSeconds >= m_Settings.sustainedOverSeconds && currentScale > minScale)
            {
                m_OverForSeconds = 0f;
                m_CooldownUntil = now + m_Settings.cooldownAfterDownSeconds;
                RegisterStep(Step.Down);
                return Step.Down;
            }

            if (m_UnderForSeconds < m_Settings.sustainedUnderSeconds || currentScale >= maxScale)
                return Step.Hold;

            // Never spend headroom on a device that is already heating up: the gain lasts
            // seconds and the throttle that follows lasts minutes.
            if (deviceWarm)
            {
                m_UnderForSeconds = 0f;
                QualityDevLog.Log("Headroom available but the device is warm; holding render scale.");
                return Step.Hold;
            }

            if (IsUpwardLocked(now))
            {
                m_UnderForSeconds = 0f;
                return Step.Hold;
            }

            // The oscillation guard fires *before* the step is applied rather than after.
            // Blocking the move leaves the scale sitting on the lower value that already
            // held the budget; reverting it afterwards would mean two more reallocations
            // to end up in the same place.
            if (m_LastStep == Step.Down && m_Cycles + 1 >= m_Settings.oscillationCycleLimit)
            {
                m_UnderForSeconds = 0f;
                EngageLock(now);
                return Step.Hold;
            }

            m_UnderForSeconds = 0f;
            m_CooldownUntil = now + m_Settings.cooldownAfterUpSeconds;
            RegisterStep(Step.Up);
            return Step.Up;
        }

        void RegisterStep(Step step)
        {
            if (step == m_LastStep)
            {
                // Two steps the same way is a trend, not a wobble - the device is simply
                // settling somewhere further down (or up). Start the pattern count over.
                m_Cycles = 0;
            }
            else if (step == Step.Up && m_LastStep == Step.Down)
            {
                m_Cycles++;
            }

            m_LastStep = step;
        }

        void EngageLock(float now)
        {
            if (m_SessionLocked)
                return;

            if (m_HasLockedOnce)
            {
                m_SessionLocked = true;
                QualityDevLog.Warn(
                    "Render scale oscillated again after the temporary lock expired. " +
                    "Upward steps are now locked for the rest of the session; the scale will " +
                    "settle at whatever value holds the budget.");
                return;
            }

            m_HasLockedOnce = true;
            m_UpLockUntil = now + m_Settings.oscillationLockSeconds;

            // Start the pattern from scratch after the lock, so a single step up once the
            // lock expires is not mistaken for the oscillation coming back.
            m_Cycles = 0;
            m_LastStep = Step.Hold;

            QualityDevLog.Warn(
                $"Render scale oscillated {m_Settings.oscillationCycleLimit} times " +
                $"(down then up, repeatedly). Upward steps locked for " +
                $"{m_Settings.oscillationLockSeconds:F0}s. If it happens again after that, " +
                "the lock becomes permanent for this session.");
        }
    }
}
