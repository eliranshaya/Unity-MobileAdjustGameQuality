using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Remembers which rung of the ladder this device settled on, across launches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rung that held for ten seconds on real hardware is a measurement. Anything decided
    /// at startup without one is a guess. So the next launch starts where the last one ended,
    /// and calibration only has to confirm it rather than rediscover it - which is the
    /// difference between a rough first thirty seconds every session and a rough first thirty
    /// seconds once.
    /// </para>
    /// <para>
    /// The ladder's length is stored alongside the index. Editing the ladder changes what an
    /// index means, so a remembered rung from a different ladder is discarded rather than
    /// applied to the wrong rung.
    /// </para>
    /// </remarks>
    public static class QualityStepMemory
    {
        const string k_StepKey = "maq.step";
        const string k_LadderSizeKey = "maq.ladderSize";

        /// <summary>
        /// Reads the remembered rung.
        /// </summary>
        /// <param name="ladderSize">How many rungs the current ladder has.</param>
        /// <param name="step">Receives the remembered rung.</param>
        /// <returns><c>false</c> when there is nothing usable stored.</returns>
        public static bool TryLoad(int ladderSize, out int step)
        {
            step = 0;

            if (!PlayerPrefs.HasKey(k_StepKey))
                return false;

            if (PlayerPrefs.GetInt(k_LadderSizeKey, -1) != ladderSize)
            {
                QualityDevLog.Warn("The quality ladder changed size since this device last ran, " +
                                   "so the remembered rung was discarded.");
                Clear();
                return false;
            }

            int stored = PlayerPrefs.GetInt(k_StepKey);
            if (stored < 0 || stored >= ladderSize)
                return false;

            step = stored;
            return true;
        }

        /// <summary>Stores the rung this device settled on.</summary>
        /// <param name="step">The rung index.</param>
        /// <param name="ladderSize">How many rungs the current ladder has.</param>
        public static void Save(int step, int ladderSize)
        {
            if (PlayerPrefs.GetInt(k_StepKey, -1) == step && PlayerPrefs.GetInt(k_LadderSizeKey, -1) == ladderSize)
                return;

            PlayerPrefs.SetInt(k_StepKey, step);
            PlayerPrefs.SetInt(k_LadderSizeKey, ladderSize);
            PlayerPrefs.Save();

            QualityDevLog.Log($"Remembered quality step {step} for this device.");
        }

        /// <summary>Forgets the remembered rung, so the next launch calibrates from scratch.</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(k_StepKey);
            PlayerPrefs.DeleteKey(k_LadderSizeKey);
            PlayerPrefs.Save();
        }
    }
}
