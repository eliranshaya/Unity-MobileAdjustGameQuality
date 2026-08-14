using System;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// A fixed-size ring buffer of frame times with a p95 and a mean.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plain class, no MonoBehaviour: it holds no Unity state and needs no callbacks.
    /// <see cref="MobileQualityManager"/> feeds it one value per frame.
    /// </para>
    /// <para>
    /// Both arrays are allocated once in the constructor and never grow. The sort runs
    /// over a pre-allocated scratch copy rather than LINQ, so a statistics call allocates
    /// nothing at all - which matters because it runs twice a second for the entire
    /// lifetime of the app.
    /// </para>
    /// </remarks>
    public sealed class FrameTimeSampler
    {
        readonly float[] m_Samples;
        readonly float[] m_Scratch;

        int m_Head;
        int m_Count;

        /// <summary>Creates a sampler with a fixed window.</summary>
        /// <param name="capacity">How many frames the window covers. Clamped to at least 8.</param>
        public FrameTimeSampler(int capacity)
        {
            int size = capacity < 8 ? 8 : capacity;
            m_Samples = new float[size];
            m_Scratch = new float[size];
        }

        /// <summary>How many samples are currently held.</summary>
        public int Count => m_Count;

        /// <summary>The window size.</summary>
        public int Capacity => m_Samples.Length;

        /// <summary>Adds one frame time.</summary>
        /// <param name="milliseconds">Frame time in milliseconds.</param>
        public void Record(float milliseconds)
        {
            m_Samples[m_Head] = milliseconds;
            m_Head++;
            if (m_Head == m_Samples.Length)
                m_Head = 0;
            if (m_Count < m_Samples.Length)
                m_Count++;
        }

        /// <summary>Empties the window. Used after a tier change or a long stall.</summary>
        public void Reset()
        {
            m_Head = 0;
            m_Count = 0;
        }

        /// <summary>
        /// Computes the 95th percentile and the mean over the current window.
        /// </summary>
        /// <param name="minimumSamples">Refuse to answer below this many samples.</param>
        /// <param name="p95">95th percentile frame time, in milliseconds.</param>
        /// <param name="average">Mean frame time, in milliseconds.</param>
        /// <returns><c>false</c> when there is not enough data yet.</returns>
        public bool TryGetStatistics(int minimumSamples, out float p95, out float average)
        {
            p95 = 0f;
            average = 0f;

            int count = m_Count;
            if (count < minimumSamples || count == 0)
                return false;

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                float value = m_Samples[i];
                m_Scratch[i] = value;
                sum += value;
            }

            Array.Sort(m_Scratch, 0, count);

            int index = (int)Math.Ceiling(0.95f * count) - 1;
            if (index < 0)
                index = 0;
            else if (index >= count)
                index = count - 1;

            p95 = m_Scratch[index];
            average = sum / count;
            return true;
        }
    }
}
