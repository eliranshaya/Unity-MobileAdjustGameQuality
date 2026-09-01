using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Remembers the best rung this device has earned, across launches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rung held on merit - settled, inside budget, cool, and with no thermal penalty
    /// outstanding - is a measurement. Anything decided at startup without one is a guess. So
    /// the next launch starts from the measurement and calibration only has to confirm it,
    /// which is the difference between a rough first thirty seconds every session and a rough
    /// first thirty seconds once.
    /// </para>
    /// <para>
    /// <b>Best, not last.</b> This used to store the rung the session ended on, and that one
    /// word was a real bug: a session that ended hot ended at the bottom of the ladder, so the
    /// bottom of the ladder is what got written, and the next launch started there on a cold
    /// phone and had no way to climb out. One warm evening permanently downgraded the device.
    /// A high-water mark cannot do that, because heat never contributes to it - see
    /// <see cref="ProvenStepTracker"/> for what does.
    /// </para>
    /// <para>
    /// The cost of a high-water mark is that a device whose game has since grown heavier keeps
    /// starting one or two rungs too high and pays a short calibration for it. That is a
    /// half-minute of adjustment against a permanent downgrade, so it is the right way round.
    /// </para>
    /// <para>
    /// The ladder's length is stored alongside the index. Editing the ladder changes what an
    /// index means, so a remembered rung from a different ladder is discarded rather than
    /// applied to the wrong rung.
    /// </para>
    /// </remarks>
    public static class QualityStepMemory
    {
        const string k_ProvenStepKey = "maq.provenStep";
        const string k_LadderSizeKey = "maq.ladderSize";

        /// <summary>
        /// The key older versions wrote the <i>last</i> rung to.
        /// </summary>
        /// <remarks>
        /// Deleted rather than read. Its value has the wrong meaning - it is wherever the last
        /// session happened to stop, very possibly the bottom rung of a hot session - and
        /// reading it as a proven rung would carry the exact bug this class was rewritten to
        /// fix across the upgrade.
        /// </remarks>
        const string k_LegacyLastStepKey = "maq.step";

        /// <summary>
        /// Reads the best rung proven on this device.
        /// </summary>
        /// <param name="ladderSize">How many rungs the current ladder has.</param>
        /// <param name="step">Receives the remembered rung.</param>
        /// <returns><c>false</c> when there is nothing usable stored.</returns>
        public static bool TryLoad(int ladderSize, out int step)
        {
            step = 0;

            if (PlayerPrefs.HasKey(k_LegacyLastStepKey))
            {
                PlayerPrefs.DeleteKey(k_LegacyLastStepKey);
                PlayerPrefs.Save();
                QualityDevLog.Log("Discarded a quality step remembered by an older version - it " +
                                  "recorded where the last session stopped, not what this device proved.");
            }

            if (!PlayerPrefs.HasKey(k_ProvenStepKey))
                return false;

            if (PlayerPrefs.GetInt(k_LadderSizeKey, -1) != ladderSize)
            {
                QualityDevLog.Warn("The quality ladder changed size since this device last ran, " +
                                   "so the remembered rung was discarded.");
                Clear();
                return false;
            }

            int stored = PlayerPrefs.GetInt(k_ProvenStepKey);
            if (stored < 0 || stored >= ladderSize)
                return false;

            step = stored;
            return true;
        }

        /// <summary>
        /// Stores a rung this device has proven, if it beats what is already stored.
        /// </summary>
        /// <param name="step">The rung index. Lower is better.</param>
        /// <param name="ladderSize">How many rungs the current ladder has.</param>
        public static void Save(int step, int ladderSize)
        {
            bool sameLadder = PlayerPrefs.GetInt(k_LadderSizeKey, -1) == ladderSize;

            // Never write a worse rung over a better one. The stored value is a high-water
            // mark, and a session that could not reach last month's best has not disproved it -
            // it may simply have been a hot afternoon or a heavier level.
            if (sameLadder && PlayerPrefs.HasKey(k_ProvenStepKey)
                && PlayerPrefs.GetInt(k_ProvenStepKey) <= step)
                return;

            PlayerPrefs.SetInt(k_ProvenStepKey, step);
            PlayerPrefs.SetInt(k_LadderSizeKey, ladderSize);
            PlayerPrefs.Save();

            QualityDevLog.Log($"Proved quality step {step} on this device.");
        }

        /// <summary>Forgets the remembered rung, so the next launch calibrates from scratch.</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(k_ProvenStepKey);
            PlayerPrefs.DeleteKey(k_LadderSizeKey);
            PlayerPrefs.DeleteKey(k_LegacyLastStepKey);
            PlayerPrefs.Save();
        }
    }
}
