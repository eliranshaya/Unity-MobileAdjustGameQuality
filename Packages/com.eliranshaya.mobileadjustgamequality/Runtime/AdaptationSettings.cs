using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Tuning for the quality ladder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two signals drive it, and they answer different questions. <b>Frame time</b> answers
    /// "is the device failing to reach its target?" honestly, because missing a target really
    /// does show up in wall-clock frame time. It cannot answer "does the device have room to
    /// spare", because a frame cap pins the measurement to the budget. <b>Temperature</b>
    /// answers "is the device in trouble?" with no blind spot at all.
    /// </para>
    /// <para>
    /// So going down is driven by either. Coming back up needs the device to be cool
    /// <i>and</i> holding its target.
    /// </para>
    /// <para>
    /// The session has two phases. <b>Calibration</b> runs at the start, checks aggressively
    /// and reacts within a second or two, to find the right rung while the player is still
    /// loading in. <b>Steady</b> follows, checks four times a second and reacts slowly,
    /// because by then the answer is known and the only job left is noticing when it changes.
    /// </para>
    /// </remarks>
    [Serializable]
    public class AdaptationSettings
    {
        [Tooltip("Let the system move up and down the quality ladder. Off pins it to the " +
                 "starting rung.")]
        public bool enabled = true;

        [Tooltip("Which rung a device with no history starts on. The shipping ladder's rung 2 " +
                 "is 0.8 render scale, shadows on, 60 fps.")]
        [Min(0)]
        public int startStep = 2;

        [Tooltip("Remember the rung this device settled on and start there next launch. A " +
                 "measurement from the last session beats any guess made at startup.")]
        public bool rememberLastStep = true;

        [Tooltip("Seconds a rung must hold before it is written to PlayerPrefs. Stops a " +
                 "device that is still calibrating from writing on every change.")]
        [Range(1f, 120f)]
        public float rememberAfterSeconds = 10f;

        [Header("Calibration")]
        [Tooltip("Seconds after startup during which nothing is judged at all. The first " +
                 "frames of a session are shader compilation and scene loading, not " +
                 "gameplay, and judging a device on them drops it several rungs before it " +
                 "has drawn a real frame.")]
        [Range(0f, 30f)]
        public float startupGraceSeconds = 3f;

        [Tooltip("Seconds of aggressive calibration at the start of the session. During this " +
                 "window the ladder is evaluated every frame and reacts within a second, so " +
                 "the device lands on the right rung while the player is still loading in.")]
        [Range(0f, 120f)]
        public float calibrationSeconds = 30f;

        [Tooltip("Seconds of missing the target before dropping a rung, during calibration. " +
                 "Keep this above two seconds - the frame time window is two seconds long, so " +
                 "a shorter one lets a single hitch stay in the window long enough to trigger " +
                 "a drop on its own.")]
        [Range(0.25f, 10f)]
        public float calibrationDropAfterSeconds = 2.5f;

        [Tooltip("Seconds of being cool and holding the target before climbing, during calibration.")]
        [Range(0.5f, 30f)]
        public float calibrationRiseAfterSeconds = 4f;

        [Tooltip("Seconds between changes during calibration.")]
        [Range(0.25f, 10f)]
        public float calibrationCooldownSeconds = 1f;

        [Header("Steady state")]
        [Tooltip("How often the ladder is reconsidered once calibration is over.")]
        [Range(0.05f, 5f)]
        public float checkEverySeconds = 0.25f;

        [Tooltip("How often the thermal API is read. Temperature moves on the scale of " +
                 "minutes, so there is nothing to gain from asking more often - and on " +
                 "Android asking faster than once a second returns NaN.")]
        [Range(1f, 120f)]
        public float thermalPollSeconds = 30f;

        [Tooltip("Seconds of measurement ignored after every change, so a rung is judged on " +
                 "settled frames rather than on the reload that follows it.")]
        [Range(0f, 10f)]
        public float settleSeconds = 1f;

        [Header("Going down")]
        [Tooltip("p95 frame time above budget x this counts as missing the target.")]
        [Range(1f, 1.5f)]
        public float missTargetFactor = 1.08f;

        [Tooltip("Seconds of missing the target before dropping a rung.")]
        [Range(0.5f, 30f)]
        public float dropAfterSeconds = 3f;

        [Tooltip("Seconds between drops.")]
        [Range(0.5f, 30f)]
        public float dropCooldownSeconds = 2f;

        [Header("Going down: badly missing")]
        [Tooltip("p95 above budget x this means the device is not merely short, it is on the " +
                 "wrong frame rate entirely. 1.35 is roughly 44 fps against a 60 target. " +
                 "Rather than walking down one rung at a time while it overheats, the ladder " +
                 "jumps straight to the first rung that lowers the frame rate: a stable 30 " +
                 "looks and feels better than an unstable 44, and generates far less heat.")]
        [Range(1.1f, 2f)]
        public float wrongFrameRateFactor = 1.35f;

        [Tooltip("Seconds of badly missing the target before that jump.")]
        [Range(0.5f, 30f)]
        public float wrongFrameRateSeconds = 3f;

        [Header("Going down: heat")]
        [Tooltip("Seconds between drops while the device reports itself warm, regardless of " +
                 "frame times. Waiting for frames to suffer means waiting for the OS to have " +
                 "already throttled the chip. Keep this at or above the thermal poll interval, " +
                 "or the ladder will shed several rungs off one stale reading.")]
        [Range(1f, 300f)]
        public float heatStepSeconds = 30f;

        [Header("Coming back up")]
        [Tooltip("Frame time at or below budget x this counts as holding the target. It sits " +
                 "just under the miss threshold on purpose - the gap between the two is the " +
                 "hysteresis band, where the ladder neither rises nor falls. Do not set it " +
                 "near 1.0: a device with a frame cap sits naturally a few percent above its " +
                 "budget from vsync quantisation alone, so a value like 1.02 leaves a device " +
                 "that is coping perfectly stuck - never bad enough to drop, never good " +
                 "enough to climb.")]
        [Range(0.5f, 1.2f)]
        public float holdingTargetFactor = 1.05f;

        [Tooltip("Seconds of being cool and holding the target before climbing a rung.")]
        [Range(1f, 120f)]
        public float riseAfterSeconds = 20f;

        [Tooltip("Seconds between climbs.")]
        [Range(1f, 300f)]
        public float riseCooldownSeconds = 20f;

        [Tooltip("How many times a session may climb back to a rung it was dropped from " +
                 "before leaving it alone. This is what makes a device on the boundary settle " +
                 "instead of flapping for the whole session.")]
        [Range(1, 10)]
        public int maxRiseAttemptsPerStep = 3;

        /// <summary>Seconds of missing the target before a drop, for the current phase.</summary>
        /// <param name="calibrating">Whether the calibration window is still open.</param>
        /// <returns>The window in seconds.</returns>
        public float DropWindow(bool calibrating) =>
            calibrating ? calibrationDropAfterSeconds : dropAfterSeconds;

        /// <summary>Seconds of holding the target before a climb, for the current phase.</summary>
        /// <param name="calibrating">Whether the calibration window is still open.</param>
        /// <returns>The window in seconds.</returns>
        public float RiseWindow(bool calibrating) =>
            calibrating ? calibrationRiseAfterSeconds : riseAfterSeconds;

        /// <summary>Quiet time after a drop, for the current phase.</summary>
        /// <param name="calibrating">Whether the calibration window is still open.</param>
        /// <returns>The cooldown in seconds.</returns>
        public float DropCooldown(bool calibrating) =>
            calibrating ? calibrationCooldownSeconds : dropCooldownSeconds;

        /// <summary>Quiet time after a climb, for the current phase.</summary>
        /// <param name="calibrating">Whether the calibration window is still open.</param>
        /// <returns>The cooldown in seconds.</returns>
        public float RiseCooldown(bool calibrating) =>
            calibrating ? calibrationCooldownSeconds : riseCooldownSeconds;
    }
}
