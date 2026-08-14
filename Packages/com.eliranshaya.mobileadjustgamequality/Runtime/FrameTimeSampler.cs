using System;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// A fixed-size ring buffer of frame times with mean, p95, min and max.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plain class, no MonoBehaviour: it holds no Unity state and needs no callbacks.
    /// <see cref="MobileQualityManager"/> feeds it one value per frame.
    /// </para>
    /// <para>
    /// Both arrays are allocated once and never grow, and the sort runs over a
    /// pre-allocated scratch copy rather than LINQ, so reading the statistics allocates
    /// nothing at all. The overlay asks for them several times a second.
    /// </para>
    /// <para>
    /// Note what these numbers can and cannot tell you. <c>Application.targetFrameRate</c>
    /// makes the engine sleep until the frame is due, and that sleep is inside
    /// <c>unscaledDeltaTime</c>. So a device comfortably holding 60 and a device only just
    /// holding 60 both report 16.67 ms. These values prove a device is <i>missing</i> its
    /// target; they can never prove it has room to spare. Adaptive Performance answers that
    /// question, with real CPU and GPU durations from the platform.
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

        /// <summary>Mean frame time over the window, in milliseconds.</summary>
        public float AverageMs { get; private set; }

        /// <summary>
        /// 75th percentile frame time, in milliseconds. This is what decisions are made on.
        /// </summary>
        /// <remarks>
        /// Not p95, and the difference matters. With vsync a frame time is quantised to
        /// multiples of the refresh interval: at a 60 fps target on a 120 Hz panel a frame is
        /// either 16.7 ms or 25 ms, with nothing in between. A device that is coping
        /// perfectly still lands a few frames on 25 ms, which drags p95 over any sensible
        /// threshold and makes it look like it is failing. p75 rides out that quantisation
        /// noise while still catching a device that is genuinely slow for a quarter of its
        /// frames or more.
        /// </remarks>
        public float P75Ms { get; private set; }

        /// <summary>
        /// 95th percentile frame time, in milliseconds. Reported, not decided on - it is the
        /// stutter measure.
        /// </summary>
        public float P95Ms { get; private set; }

        /// <summary>Fastest frame in the window, in milliseconds.</summary>
        public float MinMs { get; private set; }

        /// <summary>Slowest frame in the window, in milliseconds.</summary>
        public float MaxMs { get; private set; }

        /// <summary>Frames per second implied by <see cref="AverageMs"/>.</summary>
        public float Fps => AverageMs > 0f ? 1000f / AverageMs : 0f;

        /// <summary>
        /// Frames longer than this are stalls, not slow frames, and are thrown away.
        /// </summary>
        /// <remarks>
        /// Half a second is two frames per second. Nothing renders at two frames per second
        /// for one frame and then recovers - a spike like that is an asset load, a garbage
        /// collection, a domain reload or the editor doing something unrelated. Letting one
        /// into a two-second window skews every statistic in it and walks the ladder down for
        /// something that was never a rendering cost.
        /// </remarks>
        public const float StallThresholdMs = 500f;

        /// <summary>How many stalls have been discarded. Reporting only.</summary>
        public int DiscardedStalls { get; private set; }

        /// <summary>Adds one frame time, unless it was a stall.</summary>
        /// <param name="milliseconds">Frame time in milliseconds.</param>
        public void Record(float milliseconds)
        {
            if (milliseconds > StallThresholdMs)
            {
                DiscardedStalls++;
                return;
            }

            m_Samples[m_Head] = milliseconds;
            m_Head++;
            if (m_Head == m_Samples.Length)
                m_Head = 0;
            if (m_Count < m_Samples.Length)
                m_Count++;
        }

        /// <summary>Empties the window.</summary>
        public void Reset()
        {
            m_Head = 0;
            m_Count = 0;
            AverageMs = 0f;
            P75Ms = 0f;
            P95Ms = 0f;
            MinMs = 0f;
            MaxMs = 0f;
        }

        /// <summary>
        /// Recomputes the statistics. Call on a slow cadence - the overlay's refresh rate,
        /// not every frame.
        /// </summary>
        public void Recalculate()
        {
            int count = m_Count;
            if (count == 0)
                return;

            float sum = 0f;
            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < count; i++)
            {
                float value = m_Samples[i];
                m_Scratch[i] = value;
                sum += value;

                if (value < min) min = value;
                if (value > max) max = value;
            }

            Array.Sort(m_Scratch, 0, count);

            AverageMs = sum / count;
            P75Ms = m_Scratch[PercentileIndex(0.75f, count)];
            P95Ms = m_Scratch[PercentileIndex(0.95f, count)];
            MinMs = min;
            MaxMs = max;
        }

        static int PercentileIndex(float percentile, int count)
        {
            int index = (int)Math.Ceiling(percentile * count) - 1;
            if (index < 0) return 0;
            return index >= count ? count - 1 : index;
        }
    }
}
