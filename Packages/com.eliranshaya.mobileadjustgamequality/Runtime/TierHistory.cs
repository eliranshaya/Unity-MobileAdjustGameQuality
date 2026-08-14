using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// What the last few sessions settled on, persisted across launches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Device detection is a heuristic and is wrong on roughly a fifth of devices. Measured
    /// frame times are not a heuristic. This is how the measurement outlives the session
    /// that made it: the next launch starts on the tier the last one settled on rather than
    /// rediscovering the same answer, badly, for the first thirty seconds every time.
    /// </para>
    /// <para>
    /// The current session's entry is written when the session starts and rewritten on every
    /// tier change, rather than being saved on quit. Mobile apps are very often killed
    /// without <c>OnApplicationQuit</c> ever running, and a history that only records tidy
    /// exits would mostly record nothing.
    /// </para>
    /// </remarks>
    public sealed class TierHistory
    {
        const string k_HistoryKey = "quality.history";
        const string k_SettledKey = "quality.settled";

        readonly List<QualityTier> m_Sessions = new List<QualityTier>();

        bool m_SessionStarted;

        /// <summary>Past sessions, oldest first, including the one currently running.</summary>
        public IReadOnlyList<QualityTier> Sessions => m_Sessions;

        /// <summary>
        /// True when a promotion has already been tried and failed on this device, so no
        /// future session should spend a hitch finding that out again.
        /// </summary>
        public bool IsSettled { get; private set; }

        /// <summary>Reads the stored history.</summary>
        public void Load()
        {
            m_Sessions.Clear();
            m_SessionStarted = false;
            IsSettled = PlayerPrefs.GetInt(k_SettledKey, 0) == 1;

            string raw = PlayerPrefs.GetString(k_HistoryKey, string.Empty);
            if (string.IsNullOrEmpty(raw))
                return;

            string[] parts = raw.Split(',');
            foreach (string part in parts)
            {
                if (int.TryParse(part, out int value))
                    m_Sessions.Add((QualityTier)Mathf.Clamp(value, 0, 2));
            }
        }

        /// <summary>
        /// The tier the previous session ended on, if there is one.
        /// </summary>
        /// <param name="tier">Receives the remembered tier.</param>
        /// <returns><c>false</c> when this device has no history yet.</returns>
        public bool TryGetRememberedTier(out QualityTier tier)
        {
            tier = QualityTier.Balanced;

            // Skip the entry belonging to the session currently running.
            int index = m_SessionStarted ? m_Sessions.Count - 2 : m_Sessions.Count - 1;
            if (index < 0)
                return false;

            tier = m_Sessions[index];
            return true;
        }

        /// <summary>Appends an entry for the session that is starting.</summary>
        /// <param name="tier">The tier the session starts on.</param>
        /// <param name="capacity">How many sessions to keep.</param>
        public void BeginSession(QualityTier tier, int capacity)
        {
            m_Sessions.Add(tier);
            m_SessionStarted = true;
            Trim(capacity);
            Save();
        }

        /// <summary>
        /// Rewrites the current session's entry. Called on every tier change so the stored
        /// value is always the tier the session is actually on.
        /// </summary>
        /// <param name="tier">The tier now in effect.</param>
        public void UpdateCurrentSession(QualityTier tier)
        {
            if (!m_SessionStarted || m_Sessions.Count == 0)
                return;

            if (m_Sessions[m_Sessions.Count - 1] == tier)
                return;

            m_Sessions[m_Sessions.Count - 1] = tier;
            Save();
        }

        /// <summary>
        /// Marks the device as settled, so no future session tries to promote again.
        /// </summary>
        public void MarkSettled()
        {
            if (IsSettled)
                return;

            IsSettled = true;
            PlayerPrefs.SetInt(k_SettledKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Forgets everything. Used by "reset to recommended".</summary>
        public void Clear()
        {
            m_Sessions.Clear();
            m_SessionStarted = false;
            IsSettled = false;

            PlayerPrefs.DeleteKey(k_HistoryKey);
            PlayerPrefs.DeleteKey(k_SettledKey);
            PlayerPrefs.Save();
        }

        /// <summary>Compact rendering for the debug overlay, oldest first.</summary>
        /// <returns>Something like <c>High &gt; Balanced &gt; Low</c>.</returns>
        public string Describe()
        {
            if (m_Sessions.Count == 0)
                return "none";

            var builder = new StringBuilder();
            for (int i = 0; i < m_Sessions.Count; i++)
            {
                if (i > 0)
                    builder.Append(" > ");
                builder.Append(m_Sessions[i]);
            }

            return builder.ToString();
        }

        void Trim(int capacity)
        {
            int limit = Mathf.Max(1, capacity);
            while (m_Sessions.Count > limit)
                m_Sessions.RemoveAt(0);
        }

        void Save()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < m_Sessions.Count; i++)
            {
                if (i > 0)
                    builder.Append(',');
                builder.Append((int)m_Sessions[i]);
            }

            PlayerPrefs.SetString(k_HistoryKey, builder.ToString());
            PlayerPrefs.Save();
        }
    }
}
