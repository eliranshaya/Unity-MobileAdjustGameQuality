namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Records the best rung this device has actually earned, so the next session can start
    /// there instead of guessing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rung counts as proven when the device has spent enough time on it while settled,
    /// inside its frame budget, cool, and with no thermal penalty outstanding - the condition
    /// <see cref="QualityGovernor.IsProving"/> reports. Nothing else counts, and that is the
    /// entire point of the class.
    /// </para>
    /// <para>
    /// What it replaced remembered the rung the session <i>ended</i> on. That sounds
    /// equivalent and is not: a session that ended hot ended at the bottom of the ladder, so
    /// the bottom of the ladder is what got written, and the next launch started there on a
    /// cold phone and never climbed out. One warm evening permanently downgraded the device.
    /// A high-water mark cannot do that, because heat never contributes to it.
    /// </para>
    /// <para>
    /// Time is accumulated, not required to be continuous. A player who pauses, loads a
    /// level or takes a call has not stopped proving anything about their phone.
    /// </para>
    /// </remarks>
    public sealed class ProvenStepTracker
    {
        /// <summary>Returned by <see cref="BestProvenStep"/> when nothing has been proven yet.</summary>
        public const int Nothing = -1;

        readonly float[] m_Seconds;

        /// <summary>Creates a tracker for a ladder of a given size.</summary>
        /// <param name="stepCount">How many rungs the ladder has.</param>
        public ProvenStepTracker(int stepCount)
        {
            m_Seconds = new float[stepCount < 1 ? 1 : stepCount];
        }

        /// <summary>
        /// The best rung proven so far, or <see cref="Nothing"/> when there is no proof yet.
        /// </summary>
        public int BestProvenStep { get; private set; } = Nothing;

        /// <summary>True once any rung has been proven, this session or a previous one.</summary>
        public bool HasProof => BestProvenStep != Nothing;

        /// <summary>
        /// Accepts a rung proven in an earlier session, so this one does not have to earn it
        /// again before the floor it implies takes effect.
        /// </summary>
        /// <param name="step">The rung read back from storage.</param>
        public void Seed(int step)
        {
            if (step >= 0 && step < m_Seconds.Length && (!HasProof || step < BestProvenStep))
                BestProvenStep = step;
        }

        /// <summary>How long a rung has been held on merit this session, in seconds.</summary>
        /// <param name="step">The rung to query.</param>
        /// <returns>Accumulated seconds, or zero for an index off the ladder.</returns>
        public float SecondsAt(int step) =>
            step >= 0 && step < m_Seconds.Length ? m_Seconds[step] : 0f;

        /// <summary>
        /// Counts time towards proving a rung. Call only while the rung is being held on
        /// merit.
        /// </summary>
        /// <param name="step">The rung currently applied.</param>
        /// <param name="elapsed">Real seconds since the previous call.</param>
        /// <param name="requiredSeconds">How long a rung must be held to count as proven.</param>
        /// <returns>
        /// True on the one call where the rung crosses the threshold and becomes the new best.
        /// The caller persists it; every other call returns false.
        /// </returns>
        public bool Accumulate(int step, float elapsed, float requiredSeconds)
        {
            if (step < 0 || step >= m_Seconds.Length)
                return false;

            // A rung no better than the one already proven cannot improve the answer, so it
            // is not worth counting. This also stops a device that settles happily on its
            // proven rung from re-reporting it for the rest of the session.
            if (HasProof && step >= BestProvenStep)
                return false;

            m_Seconds[step] += elapsed;

            if (m_Seconds[step] < requiredSeconds)
                return false;

            BestProvenStep = step;
            return true;
        }
    }
}
