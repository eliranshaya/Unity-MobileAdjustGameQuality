using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// The quality configuration applied at startup, and the ladder the game moves along.
    /// </summary>
    [Serializable]
    public class MobileQualityProfile
    {
        [Tooltip("Index into Unity's Quality Settings list. Written by " +
                 "Tools > Mobile Quality > Setup - it is not a constant, because it depends " +
                 "on what the project already had.")]
        public int unityQualityLevel;

        [Tooltip("Force vSync off at startup. With vSync on the frame cap is silently " +
                 "ignored and the game runs at the panel refresh rate instead.")]
        public bool disableVSync = true;

        [Tooltip("How far the main light casts shadows, in metres. One authored value that " +
                 "the ladder never changes - it only switches shadows on or off. Held here " +
                 "rather than read back from the URP asset because in the editor a runtime " +
                 "change writes to that asset on disk, so a session that ended with shadows " +
                 "off would leave a distance of zero to restore from.")]
        [Min(0f)]
        public float shadowDistance = 30f;

        [Tooltip("The quality ladder, best first. The game starts on the rung chosen in " +
                 "Adaptation and moves along it as the device copes or struggles.")]
        public QualityStep[] steps = CreateDefaultLadder();

        /// <summary>The last rung's index.</summary>
        public int WorstStep => steps != null && steps.Length > 0 ? steps.Length - 1 : 0;

        /// <summary>Clamps an index to the ladder.</summary>
        /// <param name="index">Any integer.</param>
        /// <returns>A valid rung index.</returns>
        public int ClampStep(int index) => Mathf.Clamp(index, 0, WorstStep);

        /// <summary>The rung at an index, clamped.</summary>
        /// <param name="index">Rung index.</param>
        /// <returns>The rung.</returns>
        public QualityStep Step(int index) => steps[ClampStep(index)];

        /// <summary>
        /// The first rung below <paramref name="from"/> that runs at a lower frame rate, or
        /// <c>-1</c> when there is none.
        /// </summary>
        /// <remarks>
        /// This is what the governor jumps to when the device is not merely short of its
        /// target but on the wrong one entirely.
        /// </remarks>
        /// <param name="from">The rung currently applied.</param>
        /// <returns>The rung index, or <c>-1</c>.</returns>
        public int FindLowerFrameRateStep(int from)
        {
            if (steps == null || steps.Length == 0)
                return -1;

            int current = Step(from).targetFrameRate;

            for (int i = ClampStep(from) + 1; i < steps.Length; i++)
            {
                if (steps[i].targetFrameRate < current)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// The shipping ladder.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every rung costs less than the one above it, and nothing is ever handed back on the
        /// way down. Rungs 0-3 spend resolution while keeping 60 fps and shadows. Rung 4
        /// switches shadows off - all at once, never by shortening the distance. Rungs 5-6
        /// keep spending resolution, and only rung 7 gives up 60 fps.
        /// </para>
        /// <para>
        /// Frame rate is last on purpose: it is the most noticeable change a game can make.
        /// Shadows come before it because they are the only rung here that helps a CPU-bound
        /// frame - render scale moves pixels, and a frame limited by draw calls does not care
        /// how many pixels it is drawing.
        /// </para>
        /// </remarks>
        /// <returns>Nine rungs, best first.</returns>
        public static QualityStep[] CreateDefaultLadder()
        {
            return new[]
            {
                new QualityStep { renderScale = 1.00f, shadowsEnabled = true,  shadowResolution = 1024, targetFrameRate = 60 },
                new QualityStep { renderScale = 0.90f, shadowsEnabled = true,  shadowResolution = 1024, targetFrameRate = 60 },
                new QualityStep { renderScale = 0.80f, shadowsEnabled = true,  shadowResolution = 512,  targetFrameRate = 60 },
                new QualityStep { renderScale = 0.70f, shadowsEnabled = true,  shadowResolution = 512,  targetFrameRate = 60 },
                new QualityStep { renderScale = 0.70f, shadowsEnabled = false, shadowResolution = 512,  targetFrameRate = 60 },
                new QualityStep { renderScale = 0.65f, shadowsEnabled = false, shadowResolution = 256,  targetFrameRate = 60 },
                new QualityStep { renderScale = 0.60f, shadowsEnabled = false, shadowResolution = 256,  targetFrameRate = 60 },
                new QualityStep { renderScale = 0.60f, shadowsEnabled = false, shadowResolution = 256,  targetFrameRate = 30 },
                new QualityStep { renderScale = 0.55f, shadowsEnabled = false, shadowResolution = 256,  targetFrameRate = 30 }
            };
        }
    }
}
