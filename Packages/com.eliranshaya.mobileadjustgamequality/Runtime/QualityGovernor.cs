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
    /// <para>
    /// <b>Two numbers, not one.</b> <see cref="PerfStep"/> is what this device is believed to
    /// be capable of, and it only ever moves on evidence the frame times can actually carry.
    /// <see cref="ThermalPenalty"/> is a temporary charge on top of it, added because the
    /// device is hot and removed again when it is not. <see cref="AppliedStep"/> is the sum.
    /// </para>
    /// <para>
    /// Keeping them apart is the whole point. When heat and performance wrote to the same
    /// number, a phone that got warm once walked to the bottom of the ladder and stayed
    /// there for the rest of the session - and for every session after it, because the rung
    /// it was stranded on was then remembered as the rung it belonged on. Nothing recorded
    /// that the descent had been thermal, so nothing could undo it. A penalty that knows it
    /// is a penalty expires on its own.
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

            /// <summary>Cool again, so the thermal penalty is being handed back.</summary>
            Cooling,

            /// <summary>Cool and holding the target, so climbing back.</summary>
            Recovered,

            /// <summary>Already at the best rung.</summary>
            AtBest,

            /// <summary>Already at the worst rung.</summary>
            AtWorst,

            /// <summary>Stopped by the floor this device earned. See <see cref="FloorStep"/>.</summary>
            AtFloor,

            /// <summary>This rung has been tried and lost too many times.</summary>
            RiseAttemptsSpent
        }

        readonly AdaptationSettings m_Settings;
        readonly int[] m_RiseAttempts;
        readonly int m_WorstStep;

        float m_MissingForSeconds;
        float m_BadlyMissingForSeconds;
        float m_HoldingForSeconds;
        float m_SheddingForSeconds;
        float m_CoolingForSeconds;
        float m_UrgentCooldownUntil;
        float m_CooldownUntil;
        float m_SettledAt;

        /// <summary>Creates a governor for a ladder of a given size.</summary>
        /// <param name="settings">Tuning. Read live, so inspector edits apply.</param>
        /// <param name="stepCount">How many rungs the ladder has.</param>
        public QualityGovernor(AdaptationSettings settings, int stepCount)
        {
            m_Settings = settings;

            int count = stepCount < 1 ? 1 : stepCount;
            m_RiseAttempts = new int[count];
            m_WorstStep = count - 1;
        }

        /// <summary>
        /// The rung this device is believed to be capable of. Moves only on frame times, and
        /// only while the device is cool enough for those frame times to mean anything.
        /// </summary>
        public int PerfStep { get; private set; }

        /// <summary>
        /// How many rungs are currently being given up to heat. Always zero on a cool device.
        /// </summary>
        public int ThermalPenalty { get; private set; }

        /// <summary>The rung actually in force: <see cref="PerfStep"/> plus the penalty.</summary>
        public int AppliedStep => Clamp(PerfStep + ThermalPenalty);

        /// <summary>
        /// The worst rung the ladder is allowed to descend to under its own steam. Defaults
        /// to the last rung, which is no limit at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Set from what the device has actually proven: once a phone has held a 60 fps rung
        /// on merit, the ladder stops at the last rung that still runs at 60 rather than
        /// carrying on into the 30 fps rungs below it. A device that demonstrated it can do
        /// something should not quietly end up somewhere it never needed to be.
        /// </para>
        /// <para>
        /// This is the one guard in the package that can refuse a downward step, so it has
        /// exactly one exception, in <see cref="DescentLimit"/>: from
        /// <see cref="HeatBand.Urgent"/> upward the floor is ignored. At that point the OS is
        /// clocking the chip down itself, and holding 60 in the settings does not hold 60 on
        /// screen - it only means the game is hot, missing its target anyway, and refusing to
        /// do the one thing that would help.
        /// </para>
        /// </remarks>
        public int FloorStep { get; set; } = int.MaxValue;

        /// <summary>Why the last evaluation did what it did.</summary>
        public Reason LastReason { get; private set; } = Reason.Holding;

        /// <summary>
        /// True while the applied rung is being held on merit: settled, inside budget, cool,
        /// and with no thermal penalty outstanding.
        /// </summary>
        /// <remarks>
        /// This is the only condition under which time spent on a rung says anything durable
        /// about the device, which is why <see cref="ProvenStepTracker"/> counts seconds
        /// against it and nothing else does.
        /// </remarks>
        public bool IsProving { get; private set; }

        /// <summary>
        /// Puts the ladder on a rung outright and clears any thermal penalty.
        /// </summary>
        /// <param name="step">The rung to treat as this device's capability.</param>
        public void SetPerfStep(int step)
        {
            PerfStep = Clamp(step);
            ThermalPenalty = 0;
        }

        /// <summary>
        /// Hands back every rung currently charged to heat, at once.
        /// </summary>
        /// <remarks>
        /// For the one moment where a large visual change is free: returning from a long
        /// spell in the background. Nothing is in motion on the frame the app comes back, so
        /// a jump that would be jarring mid-game costs nothing there. If the device is in
        /// fact still hot, the shedding loop puts the penalty back within the minute.
        /// </remarks>
        /// <returns>True when there was a penalty to release.</returns>
        public bool ReleaseThermalPenalty()
        {
            if (ThermalPenalty == 0)
                return false;

            ThermalPenalty = 0;
            LastReason = Reason.Cooling;
            return true;
        }

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
            m_SheddingForSeconds = 0f;
            m_CoolingForSeconds = 0f;
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
        /// <param name="budgetMs">The applied rung's frame budget.</param>
        /// <param name="lowerFrameRateStep">
        /// The first rung below the applied one that runs at a lower frame rate, or <c>-1</c>
        /// when there is none.
        /// </param>
        /// <param name="heat">What the device's temperature says to do.</param>
        /// <param name="calibrating">
        /// Whether the opening calibration window is still open. It shortens every window, so
        /// the device lands on the right rung while the player is still loading in.
        /// </param>
        /// <returns>The rung to apply. Equal to the previous one means hold.</returns>
        public int Evaluate(float now, float elapsed, float frameMs, float budgetMs,
            int lowerFrameRateStep, HeatBand heat, bool calibrating)
        {
            if (!m_Settings.enabled)
            {
                IsProving = false;
                LastReason = Reason.Holding;
                return AppliedStep;
            }

            bool missing = budgetMs > 0f && frameMs > budgetMs * m_Settings.missTargetFactor;
            bool badlyMissing = budgetMs > 0f && frameMs > budgetMs * m_Settings.wrongFrameRateFactor;
            bool holding = budgetMs > 0f && frameMs <= budgetMs * m_Settings.holdingTargetFactor;
            bool settled = now >= m_SettledAt;

            m_MissingForSeconds = missing ? m_MissingForSeconds + elapsed : 0f;
            m_BadlyMissingForSeconds = badlyMissing ? m_BadlyMissingForSeconds + elapsed : 0f;
            m_SheddingForSeconds = heat >= HeatBand.Shed ? m_SheddingForSeconds + elapsed : 0f;
            m_CoolingForSeconds = heat == HeatBand.Cool ? m_CoolingForSeconds + elapsed : 0f;

            // Holding the applied rung only says something about the device when no penalty is
            // outstanding. With one applied the game is running easier than the rung being
            // judged, so meeting the budget there proves nothing about PerfStep.
            IsProving = settled && holding && heat == HeatBand.Cool && ThermalPenalty == 0;

            // Three outcomes for the climb window, not two. Frames over budget are real
            // counter-evidence and wipe it. Cool frames inside budget earn it. The Hold band
            // is neither, so the window is frozen rather than cleared - the device is warm
            // enough that climbing would probably be undone within the minute, which is a
            // reason to stop asking for more, not a reason to forget what was already
            // measured. Clearing it here is what stranded a cooled phone on the bottom rung:
            // one stale LIGHT reading threw away every second of progress towards a climb,
            // and on a phone in use those readings never stop arriving.
            if (!holding || heat >= HeatBand.Shed)
                m_HoldingForSeconds = 0f;
            else if (IsProving)
                m_HoldingForSeconds += elapsed;

            // Urgent is the one case that ignores the settle and cooldown timers. The OS is
            // already taking performance away; the only question left is whether we choose
            // what to give up. Its own interval is separate and defaults to zero, so by
            // default this fires on every evaluation.
            if (heat >= HeatBand.Urgent && now >= m_UrgentCooldownUntil && AppliedStep < m_WorstStep)
            {
                m_UrgentCooldownUntil = now + m_Settings.urgentStepSeconds;
                return Shed(now, heat, calibrating);
            }

            if (!settled)
            {
                LastReason = Reason.Settling;
                return AppliedStep;
            }

            if (now < m_CooldownUntil)
            {
                LastReason = Reason.Cooldown;
                return AppliedStep;
            }

            // Badly missing means the device is not short of a target, it is on the wrong
            // target. Walking down one rung at a time would spend twenty seconds overheating
            // to reach a conclusion already visible in the numbers.
            if (m_BadlyMissingForSeconds >= m_Settings.wrongFrameRateSeconds
                && lowerFrameRateStep > AppliedStep && lowerFrameRateStep <= DescentLimit(heat))
                return JumpTo(now, lowerFrameRateStep, heat, calibrating);

            if (m_MissingForSeconds >= m_Settings.DropWindow(calibrating))
                return StepDown(now, heat, calibrating);

            if (m_SheddingForSeconds >= m_Settings.heatStepSeconds)
                return Shed(now, heat, calibrating);

            // A penalty is a lease on the device's quality, not a verdict about it, so it
            // expires on its own once the device is cool. It deliberately does not wait for
            // the frame times to justify the climb: nothing was measured to add it, and at a
            // capped rung the frame times could not justify anything anyway - the cap pins
            // them to the budget however much room the device has.
            if (ThermalPenalty > 0)
            {
                if (heat == HeatBand.Cool && m_CoolingForSeconds >= m_Settings.thermalRecoverySeconds)
                    return Recover(now);

                LastReason = heat == HeatBand.Cool ? Reason.Cooling : Reason.Holding;
                return AppliedStep;
            }

            if (m_HoldingForSeconds < m_Settings.RiseWindow(calibrating))
            {
                LastReason = Reason.Holding;
                return AppliedStep;
            }

            if (PerfStep <= 0)
            {
                LastReason = Reason.AtBest;
                return AppliedStep;
            }

            int target = PerfStep - 1;
            if (RiseAttempts(target) >= m_Settings.maxRiseAttemptsPerStep)
            {
                LastReason = Reason.RiseAttemptsSpent;
                m_HoldingForSeconds = 0f;
                return AppliedStep;
            }

            LastReason = Reason.Recovered;
            m_CooldownUntil = now + m_Settings.RiseCooldown(calibrating);
            PerfStep = target;
            NoteRiseAttempt(target);
            Reset(now);
            return AppliedStep;
        }

        /// <summary>
        /// Gives up one rung because the frame times say the applied one is not being met.
        /// </summary>
        /// <remarks>
        /// Where it is charged depends on whether the reading can be trusted. On a cool device
        /// a missed budget is what the device can do, so it is written to
        /// <see cref="PerfStep"/> and kept. On a warm one the frames may be the device or may
        /// be the throttling, and there is no way to tell those apart from here, so it is
        /// charged to the penalty and unwinds when the device cools.
        /// </remarks>
        int StepDown(float now, HeatBand heat, bool calibrating)
        {
            int applied = AppliedStep;
            int limit = DescentLimit(heat);

            if (applied >= limit)
            {
                LastReason = limit < m_WorstStep ? Reason.AtFloor : Reason.AtWorst;
                return applied;
            }

            LastReason = Reason.MissingTarget;

            if (heat == HeatBand.Cool)
            {
                // Absorb the outstanding penalty as well: a rung that has just been missed on
                // a cool device must not be left with something waiting to unwind back into it.
                PerfStep = applied + 1;
                ThermalPenalty = 0;
            }
            else
            {
                ThermalPenalty++;
            }

            m_CooldownUntil = now + m_Settings.DropCooldown(calibrating);
            Reset(now);
            return AppliedStep;
        }

        /// <summary>Gives up one rung to heat alone, without consulting the frame times.</summary>
        int Shed(float now, HeatBand heat, bool calibrating)
        {
            int limit = DescentLimit(heat);

            if (AppliedStep >= limit)
            {
                LastReason = limit < m_WorstStep ? Reason.AtFloor : Reason.AtWorst;
                return AppliedStep;
            }

            LastReason = Reason.Heat;
            ThermalPenalty++;

            m_CooldownUntil = now + m_Settings.DropCooldown(calibrating);
            Reset(now);
            return AppliedStep;
        }

        /// <summary>Jumps straight to a rung that runs at a lower frame rate.</summary>
        int JumpTo(float now, int target, HeatBand heat, bool calibrating)
        {
            LastReason = Reason.WrongFrameRate;

            int clamped = Clamp(target);

            if (heat == HeatBand.Cool)
            {
                PerfStep = clamped;
                ThermalPenalty = 0;
            }
            else
            {
                int penalty = clamped - PerfStep;
                ThermalPenalty = penalty > 0 ? penalty : 0;
            }

            m_CooldownUntil = now + m_Settings.DropCooldown(calibrating);
            Reset(now);
            return AppliedStep;
        }

        /// <summary>Hands one rung of the thermal penalty back.</summary>
        int Recover(float now)
        {
            LastReason = Reason.Cooling;
            ThermalPenalty--;

            // Reset zeroes the cooling window too, so the next rung comes back a full
            // recovery interval later rather than on the very next evaluation.
            Reset(now);
            return AppliedStep;
        }

        /// <summary>
        /// The worst rung a descent may reach right now.
        /// </summary>
        /// <remarks>
        /// The floor this device earned, except from <see cref="HeatBand.Urgent"/> upward,
        /// where the whole ladder is available again. Frame rate protection is worth having
        /// right up to the point where the OS is throttling the chip regardless of what the
        /// game asks for, and past that point refusing to descend protects nothing.
        /// </remarks>
        int DescentLimit(HeatBand heat)
        {
            if (heat >= HeatBand.Urgent)
                return m_WorstStep;

            return FloorStep < m_WorstStep ? FloorStep : m_WorstStep;
        }

        int Clamp(int step) => step < 0 ? 0 : step > m_WorstStep ? m_WorstStep : step;
    }
}
