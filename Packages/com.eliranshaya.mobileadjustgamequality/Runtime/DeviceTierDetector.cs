using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Picks a starting <see cref="QualityTier"/> from the hardware Unity reports at boot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a heuristic and it is wrong on roughly 20% of devices.</b> GPU name
    /// strings are inconsistent between OEMs and driver versions, RAM does not predict
    /// sustained throughput, and nothing here measures thermal headroom — a phone that
    /// benchmarks as High can throttle to Low performance after four minutes in a
    /// pocket-warm case.
    /// </para>
    /// <para>
    /// The real fix is telemetry: ship a build that reports
    /// <see cref="SystemInfo.deviceModel"/> together with measured p95 frame time, then
    /// serve a server-side override table keyed on that model string and fall back to
    /// this detector only for models you have not seen yet. Treat the score below as the
    /// cold-start guess, not as the answer.
    /// </para>
    /// <para>
    /// <see cref="MobileQualityManager"/>'s adaptive loop exists precisely because this cannot be
    /// trusted: a wrong guess is corrected within seconds by real frame times.
    /// </para>
    /// </remarks>
    public static class DeviceTierDetector
    {
        // Regexes are static so the one-shot Detect() call does not pay for compilation
        // twice if a caller re-runs detection (for example from the debug overlay).
        static readonly Regex k_AppleChip = new Regex(@"\bA(\d{1,2})\b", RegexOptions.IgnoreCase);
        static readonly Regex k_AdrenoModel = new Regex(@"Adreno\D*(\d{3,4})", RegexOptions.IgnoreCase);
        static readonly Regex k_MaliModel = new Regex(@"Mali[- ]?(?:Immortalis[- ]?)?([GT])(\d{2,3})", RegexOptions.IgnoreCase);

        /// <summary>Score at or above which the device is treated as <see cref="QualityTier.High"/>.</summary>
        public const int HighThreshold = 5;

        /// <summary>Score at or above which the device is treated as <see cref="QualityTier.Balanced"/>.</summary>
        public const int BalancedThreshold = 2;

        /// <summary>
        /// Scores the current device and maps the score onto a tier.
        /// </summary>
        /// <returns>The suggested startup tier.</returns>
        public static QualityTier Detect()
        {
            int score = Score();

            if (score >= HighThreshold)
                return QualityTier.High;
            if (score >= BalancedThreshold)
                return QualityTier.Balanced;
            return QualityTier.Low;
        }

        /// <summary>
        /// The raw additive score behind <see cref="Detect"/>. Exposed so the debug
        /// overlay and any telemetry you add can report <i>why</i> a tier was chosen,
        /// not just which one.
        /// </summary>
        /// <returns>The additive hardware score. Higher is faster.</returns>
        public static int Score()
        {
            int score = 0;

            score += ScoreMemory(SystemInfo.systemMemorySize);
            score += ScoreGpu(SystemInfo.graphicsDeviceName);
            score += ScoreCores(SystemInfo.processorCount);

            // A high-resolution panel costs fill rate on every single frame, and the
            // phones that ship them are not always the phones that can drive them.
            if ((long)Screen.width * Screen.height > 2600000L)
                score -= 1;

            // GLES3 means no compute-based URP paths and generally an older driver stack.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3)
                score -= 1;

            // Shader level below 4.5 rules out several URP features outright.
            if (SystemInfo.graphicsShaderLevel < 45)
                score -= 2;

            QualityDevLog.Log(
                $"Device score {score.ToString(CultureInfo.InvariantCulture)} | " +
                $"gpu='{SystemInfo.graphicsDeviceName}' api={SystemInfo.graphicsDeviceType} " +
                $"sm={SystemInfo.graphicsShaderLevel} ram={SystemInfo.systemMemorySize}MB " +
                $"cores={SystemInfo.processorCount} screen={Screen.width}x{Screen.height}");

            return score;
        }

        static int ScoreMemory(int megabytes)
        {
            // SystemInfo under-reports: a "8 GB" phone reports ~7400 MB once the kernel
            // and the GPU carve-out are taken off. The thresholds account for that.
            if (megabytes >= 7500) return 3;
            if (megabytes >= 5500) return 2;
            if (megabytes >= 3500) return 1;
            return -2;
        }

        static int ScoreCores(int cores)
        {
            if (cores >= 8) return 1;
            if (cores <= 4) return -1;
            return 0;
        }

        static int ScoreGpu(string gpuName)
        {
            if (string.IsNullOrEmpty(gpuName))
                return 0;

            if (gpuName.IndexOf("Apple", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return ScoreApple(gpuName);

            if (gpuName.IndexOf("Adreno", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return ScoreAdreno(gpuName);

            if (gpuName.IndexOf("Mali", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return ScoreMali(gpuName);

            // Samsung's RDNA-based part. Every shipping Xclipse is a flagship SoC.
            if (gpuName.IndexOf("Xclipse", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return 3;

            if (gpuName.IndexOf("PowerVR", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bool modern = gpuName.IndexOf("BXM", System.StringComparison.OrdinalIgnoreCase) >= 0
                              || gpuName.IndexOf("BXT", System.StringComparison.OrdinalIgnoreCase) >= 0;
                return modern ? 1 : -1;
            }

            // Unknown GPU (desktop editor, emulator, a part released after this code was
            // written). Contribute nothing rather than guessing in either direction.
            return 0;
        }

        static int ScoreApple(string gpuName)
        {
            Match match = k_AppleChip.Match(gpuName);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int generation))
            {
                if (generation >= 16) return 4;
                if (generation >= 13) return 3;
                if (generation >= 11) return 1;
                return -1;
            }

            // Recent iOS versions report a bare "Apple GPU" with no chip number, and
            // Apple silicon Macs running the iOS build report "Apple M<n>". Every device
            // in that bucket is at least A13-class, so score it as such rather than
            // falling through to the unknown-GPU zero.
            return 3;
        }

        static int ScoreAdreno(string gpuName)
        {
            Match match = k_AdrenoModel.Match(gpuName);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int model))
                return 0;

            if (model >= 730) return 4;
            if (model >= 650) return 3;
            if (model >= 618) return 2;
            if (model >= 610) return 1;
            return -1;
        }

        static int ScoreMali(string gpuName)
        {
            // Immortalis is the ray-tracing capable flagship line; it is always fast,
            // and its model number does not follow the Valhall numbering cleanly.
            if (gpuName.IndexOf("Immortalis", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return 4;

            Match match = k_MaliModel.Match(gpuName);
            if (!match.Success)
                return 0;

            string family = match.Groups[1].Value.ToUpperInvariant();
            if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int model))
                return 0;

            if (family == "T")
            {
                // Midgard. T8xx and anything older is well below our Low target.
                return -1;
            }

            // Bifrost / Valhall, "Mali-G<model>". Arm switched from two-digit to
            // three-digit model numbers with the G710, so the two ranges have to be
            // scored separately: G78 is a 2020 flagship while G31 is an entry part, and
            // a plain numeric comparison would rank them the wrong way round.
            if (model >= 100)
            {
                if (model >= 700) return 3;   // G710 / G715 / G720
                if (model >= 600) return 2;   // G610 / G615 / G620
                return 0;
            }

            if (model >= 71) return 2;        // G71..G78, Bifrost/Valhall flagships
            if (model == 68) return 1;        // G68, upper mid-range
            if (model == 52 || model == 57) return 1;
            if (model == 31 || model == 51) return -1;
            return 0;
        }
    }
}
