using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>How capable this device looks before a single frame has been drawn.</summary>
    public enum DeviceClass
    {
        /// <summary>Budget hardware. Start low and let it climb if it can.</summary>
        Entry,

        /// <summary>The middle of the market, which is most of the market.</summary>
        Mid,

        /// <summary>Last year's flagship, or this year's upper mid-range.</summary>
        High,

        /// <summary>Current flagship silicon.</summary>
        Flagship
    }

    /// <summary>
    /// Guesses a starting rung from what the device says about itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a guess and is treated as one. It is consulted only when there is no proven
    /// rung for this device yet - a measurement always wins - and the calibration window runs
    /// straight afterwards either way, so a wrong guess costs the player a rough half-minute
    /// rather than a bad session. That is the whole value on offer, and it is worth having:
    /// the alternative is one fixed starting rung for a market that runs from a 4 GB budget
    /// phone to current flagship silicon.
    /// </para>
    /// <para>
    /// It is a weighted score rather than a chain of special cases, for two reasons. A score
    /// degrades gracefully when one signal cannot be read, and it can be re-tuned by moving a
    /// number instead of by rewriting the logic.
    /// </para>
    /// <para>
    /// <b>An unrecognised GPU scores as mid-range, never as low.</b> New parts ship every
    /// year and this table cannot know about them, so the failure mode has to be "a new
    /// flagship starts one rung lower than it deserved for thirty seconds" rather than "every
    /// new flagship launches on the worst rung on the ladder".
    /// </para>
    /// </remarks>
    public static class DeviceProfiler
    {
        /// <summary>1080p. Render cost scales with pixels, so this is the yardstick.</summary>
        const long k_BaselinePixels = 1920L * 1080L;

        /// <summary>
        /// What an unrecognised part scores. Deliberately mid-range - see the class remarks.
        /// </summary>
        const float k_UnknownScore = 60f;

        static bool s_Evaluated;
        static float s_Score;
        static DeviceClass s_Class;

        /// <summary>The raw 0-100ish capability score. Reporting and tuning only.</summary>
        public static float Score
        {
            get
            {
                Evaluate();
                return s_Score;
            }
        }

        /// <summary>Which band this device falls into.</summary>
        /// <returns>The device's class.</returns>
        public static DeviceClass Classify()
        {
            Evaluate();
            return s_Class;
        }

        /// <summary>
        /// The rung a device of this class should start on.
        /// </summary>
        /// <remarks>
        /// Expressed as a fraction of the ladder rather than as an index, so editing the
        /// ladder does not silently change what "start a flagship high" means.
        /// </remarks>
        /// <param name="stepCount">How many rungs the ladder has.</param>
        /// <returns>A rung index inside the ladder.</returns>
        public static int SuggestStep(int stepCount)
        {
            if (stepCount <= 1)
                return 0;

            float fraction;

            switch (Classify())
            {
                case DeviceClass.Flagship:
                    fraction = 0.10f;
                    break;
                case DeviceClass.High:
                    fraction = 0.25f;
                    break;
                case DeviceClass.Mid:
                    fraction = 0.45f;
                    break;
                default:
                    fraction = 0.70f;
                    break;
            }

            int worst = stepCount - 1;
            return Mathf.Clamp(Mathf.RoundToInt(worst * fraction), 0, worst);
        }

        /// <summary>A one-line summary of what was read, for the log and the overlay.</summary>
        /// <returns>Something like <c>High · Adreno (TM) 730 · 8192MB · 1080x2400</c>.</returns>
        public static string Describe() =>
            $"{Classify()} · {SystemInfo.graphicsDeviceName} · {SystemInfo.systemMemorySize}MB · " +
            $"{Screen.width}x{Screen.height} · score {Score:F0}";

        static void Evaluate()
        {
            if (s_Evaluated)
                return;

            s_Evaluated = true;

            // The GPU is the strongest single signal and memory is the most stable one, so
            // they carry the score between them. Pixels are a multiplier rather than a third
            // term because they do not describe the hardware - they describe how much work
            // the same hardware has been asked to do.
            string name = SystemInfo.graphicsDeviceName;

            // Memory means something different on the two platforms and cannot share a scale.
            // Apple ships far less of it for the same class of device - an iPhone 15 Pro has
            // 8 GB where an Android flagship of the same year has 12 to 16 - so an iPhone 13,
            // which is 4 GB and an A15, scored as a budget Android phone on the Android scale
            // and started three rungs lower than it deserved.
            bool apple = !string.IsNullOrEmpty(name) && name.ToLowerInvariant().Contains("apple");

            float gpu = ScoreGpu(name);
            float memory = ScoreMemory(SystemInfo.systemMemorySize, apple);

            s_Score = (gpu * 0.6f + memory * 0.4f) * PixelFactor((long)Screen.width * Screen.height);

            s_Class = s_Score >= 85f ? DeviceClass.Flagship
                : s_Score >= 65f ? DeviceClass.High
                : s_Score >= 45f ? DeviceClass.Mid
                : DeviceClass.Entry;
        }

        static float ScoreGpu(string name)
        {
            if (string.IsNullOrEmpty(name))
                return k_UnknownScore;

            string lower = name.ToLowerInvariant();

            // Apple first: one clean generation ladder and no fragmentation to work around,
            // because every A15 is the same chip in every device that carries it.
            if (lower.Contains("apple"))
            {
                if (ModelNumber(lower, 'm') > 0)
                    return 100f;

                int generation = ModelNumber(lower, 'a');

                if (generation >= 15)
                    return 100f;
                if (generation >= 13)
                    return 90f;
                if (generation >= 11)
                    return 72f;
                if (generation > 0)
                    return 52f;

                // Apple silicon this table does not recognise is still Apple silicon.
                return 85f;
            }

            // ARM's flagship line since 2022 is named, not numbered, so it is tested first.
            if (lower.Contains("immortalis"))
                return 100f;

            if (lower.Contains("adreno"))
            {
                int model = FirstNumber(lower);

                if (model >= 730)
                    return 100f;
                if (model >= 700)
                    return 92f;
                if (model >= 640)
                    return 80f;
                if (model >= 620)
                    return 62f;
                if (model >= 610)
                    return 50f;
                if (model >= 500)
                    return 36f;

                return model > 0 ? 25f : k_UnknownScore;
            }

            if (lower.Contains("mali"))
            {
                int model = ModelNumber(lower, 'g');

                // Mali changed the width of its part numbers mid-line: G710 replaced G78 at
                // the top of the range, so a three-digit part is a later generation than a
                // two-digit one rather than a bigger version of it. Compared widest first.
                if (model >= 700)
                    return 92f;
                if (model >= 600)
                    return 70f;
                if (model >= 500)
                    return 46f;
                if (model >= 70)
                    return 78f;
                if (model >= 50)
                    return 48f;
                if (model > 0)
                    return 30f;

                // Mali-T predates the whole G line and sits below all of it.
                return lower.Contains("-t") ? 22f : k_UnknownScore;
            }

            // Samsung's RDNA parts ship in flagship Exynos only.
            if (lower.Contains("xclipse"))
                return 85f;

            if (lower.Contains("powervr"))
                return 40f;

            return k_UnknownScore;
        }

        static float ScoreMemory(int megabytes, bool apple)
        {
            if (megabytes <= 0)
                return k_UnknownScore;

            // Reported memory is always a little under the marketed figure, so the bands sit
            // below the round numbers: a "12 GB" phone commonly reports about 11,400 MB.
            if (apple)
            {
                if (megabytes >= 7000)
                    return 100f;    // 8 GB: iPhone 15 Pro and up
                if (megabytes >= 5500)
                    return 90f;     // 6 GB: recent Pro models
                if (megabytes >= 3500)
                    return 78f;     // 4 GB: iPhone 12 through 14, all perfectly capable
                if (megabytes >= 2500)
                    return 55f;

                return 35f;
            }

            if (megabytes >= 11000)
                return 100f;
            if (megabytes >= 7000)
                return 85f;
            if (megabytes >= 5500)
                return 70f;
            if (megabytes >= 3500)
                return 50f;
            if (megabytes >= 2500)
                return 34f;

            return 20f;
        }

        /// <summary>
        /// How much the panel's resolution changes what this hardware is being asked for.
        /// </summary>
        /// <remarks>
        /// A 1440p panel is 1.8x the pixels of a 1080p one, and for a package whose main lever
        /// is render scale that matters as much as the chip does - a strong phone with a huge
        /// screen can need a lower rung than a mid-range one with a small screen. Clamped
        /// hard, because it should adjust the answer and never dominate it.
        /// </remarks>
        static float PixelFactor(long pixels)
        {
            if (pixels <= 0)
                return 1f;

            float ratio = (float)k_BaselinePixels / pixels;
            return Mathf.Clamp(ratio, 0.75f, 1.15f);
        }

        /// <summary>Reads the first run of digits in a string, or -1 when there is none.</summary>
        static int FirstNumber(string text)
        {
            int value = -1;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c >= '0' && c <= '9')
                    value = (value < 0 ? 0 : value) * 10 + (c - '0');
                else if (value >= 0)
                    break;
            }

            return value;
        }

        /// <summary>
        /// Reads the number attached to a model letter - the 15 in "Apple A15", the 710 in
        /// "Mali-G710" - or -1 when the string has no such token.
        /// </summary>
        /// <remarks>
        /// The letter has to start a token, or the "a" in "Adreno" would read as an Apple
        /// model number.
        /// </remarks>
        static int ModelNumber(string text, char prefix)
        {
            for (int i = 0; i < text.Length - 1; i++)
            {
                if (text[i] != prefix)
                    continue;

                if (i > 0 && char.IsLetterOrDigit(text[i - 1]))
                    continue;

                if (text[i + 1] < '0' || text[i + 1] > '9')
                    continue;

                int value = 0;

                for (int j = i + 1; j < text.Length && text[j] >= '0' && text[j] <= '9'; j++)
                    value = value * 10 + (text[j] - '0');

                return value;
            }

            return -1;
        }
    }
}
