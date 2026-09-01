using System;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// How hot the device is, expressed as what to do about it.
    /// </summary>
    /// <remarks>
    /// Three bands rather than two, because the platform scales have a wide middle that the
    /// old warm/severe split collapsed. Android's <c>LIGHT</c> means "throttling has started
    /// and the user cannot tell" - it is the most common state a phone is in after a few
    /// minutes of play, and treating it as a reason to shed quality walks a perfectly healthy
    /// device to the bottom of the ladder and leaves it there. The right response to
    /// <c>LIGHT</c> is to stop asking for more, not to start giving things up.
    /// </remarks>
    public enum HeatBand
    {
        /// <summary>Nothing to do. The ladder may rise.</summary>
        Cool = 0,

        /// <summary>Hold the current rung. Do not rise, do not shed.</summary>
        Hold = 1,

        /// <summary>Shed a rung periodically, regardless of frame times.</summary>
        Shed = 2,

        /// <summary>The OS is taking performance away. Shed as fast as the ladder allows.</summary>
        Urgent = 3
    }

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
    /// So going down is driven by either - but the two are recorded separately and come back
    /// differently, which is the part that matters. A rung lost to frame times on a cool
    /// device is a fact about the device and is kept; climbing back needs the device to be
    /// cool <i>and</i> holding its target for a long window. A rung lost to heat is a lease,
    /// and it expires on its own as soon as the device is cool again, without waiting for the
    /// frame times to prove anything - because nothing was measured to take it away, and at a
    /// capped rung the frame times could not prove it anyway.
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

        [Tooltip("Which rung a device with no history and no readable hardware starts on. " +
                 "The last resort: a rung proven on this device wins, and a guess from the " +
                 "device's own hardware beats this one. The shipping ladder's rung 2 is 0.8 " +
                 "render scale, shadows on, 60 fps.")]
        [Min(0)]
        public int startStep = 2;

        [Tooltip("On a device with no proven rung yet, guess a starting rung from its GPU, " +
                 "memory and screen resolution instead of using Start Step. Only ever a " +
                 "guess - calibration runs straight afterwards either way - but it is the " +
                 "difference between one fixed starting rung for a market that runs from a " +
                 "4GB budget phone to current flagship silicon.")]
        public bool useDeviceProfiler = true;

        [Tooltip("Once this device has held a 60 fps rung on merit, stop the ladder at the " +
                 "last 60 fps rung instead of descending into the 30 fps ones. A phone that " +
                 "proved it can hold 60 should not quietly end up at 30. Ignored when the " +
                 "device reaches the urgent thermal band, where the OS is clocking the chip " +
                 "down itself and refusing to descend protects nothing.")]
        public bool protectProvenFrameRate = true;

        [Tooltip("In the Unity editor, pin the ladder to rung 0 and never move it. A desktop " +
                 "running a mobile game has performance to spare, so the only thing adaptation " +
                 "achieves in Play mode is making the Game view look worse than the build " +
                 "will. Turn this off for the few minutes you actually want to watch the " +
                 "ladder work. Has no effect in a player build - the check is compiled out.")]
        public bool bestStepInEditor = true;

        [Tooltip("Remember the best rung this device has earned and start there next launch. " +
                 "A measurement from a previous session beats any guess made at startup.")]
        public bool rememberProvenStep = true;

        [Tooltip("Seconds a rung must be held on merit - settled, inside budget, cool, and " +
                 "with no thermal penalty outstanding - before it counts as proven and is " +
                 "written to PlayerPrefs. Accumulated, not continuous: a player who pauses " +
                 "or loads a level has not stopped proving anything about their phone.")]
        [Range(10f, 600f)]
        public float provenAfterSeconds = 120f;

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

        [Tooltip("How often the thermal status is read. This used to be 30, which was longer " +
                 "than the window a device has to be cool for before the ladder climbs - so " +
                 "a single stale reading could block a climb for the whole window it " +
                 "covered. The headroom forecast is paced separately inside ThermalSensor, " +
                 "because Android rate-limits that call and returns NaN if you ignore it.")]
        [Range(1f, 120f)]
        public float thermalPollSeconds = 5f;

        [Tooltip("Seconds of measurement ignored after every change, so a rung is judged on " +
                 "settled frames rather than on the reload that follows it.")]
        [Range(0f, 10f)]
        public float settleSeconds = 1f;

        [Header("Going down")]
        [Tooltip("p75 frame time above budget x this counts as missing the target.")]
        [Range(1f, 1.5f)]
        public float missTargetFactor = 1.08f;

        [Tooltip("Seconds of missing the target before dropping a rung.")]
        [Range(0.5f, 30f)]
        public float dropAfterSeconds = 3f;

        [Tooltip("Seconds between drops.")]
        [Range(0.5f, 30f)]
        public float dropCooldownSeconds = 2f;

        [Header("Going down: badly missing")]
        [Tooltip("p75 above budget x this means the device is not merely short, it is on the " +
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
        [Tooltip("Status at which the ladder stops climbing but does not give anything back. " +
                 "Android LIGHT means throttling has started and the user cannot tell - it is " +
                 "the state most phones sit in after a few minutes of play, so shedding " +
                 "quality here would walk a healthy device to the bottom of the ladder for " +
                 "nothing. iOS reports this as 'fair'.")]
        public ThermalLevel holdAtStatus = ThermalLevel.Light;

        [Tooltip("Headroom forecast that also means hold. Headroom runs 0..1+ where 1.0 is " +
                 "'throttling right now', so 0.85 is a few seconds of warning. Android 11+ " +
                 "only; NaN elsewhere, which simply leaves the status to decide.")]
        [Range(0.5f, 1.5f)]
        public float holdAtHeadroom = 0.85f;

        [Tooltip("Status at which the ladder starts shedding a rung periodically, ignoring " +
                 "frame times. Android MODERATE is where Google's own guidance says to start " +
                 "reducing: the user still is not suffering, which is exactly why it is the " +
                 "right moment to act. Waiting for frames to suffer means waiting for the OS " +
                 "to have already throttled the chip.")]
        public ThermalLevel shedAtStatus = ThermalLevel.Moderate;

        [Tooltip("Headroom forecast that also means shed.")]
        [Range(0.5f, 1.5f)]
        public float shedAtHeadroom = 0.95f;

        [Tooltip("Seconds between drops while shedding. Keep this at or above the thermal " +
                 "poll interval, or the ladder will shed several rungs off one stale reading.")]
        [Range(1f, 300f)]
        public float heatStepSeconds = 30f;

        [Tooltip("Status at which the OS is actively clocking the chip down and the ladder " +
                 "stops waiting for anything. Note that iOS has no equivalent of MODERATE: it " +
                 "reports nominal, fair, serious and critical, and 'serious' maps to SEVERE " +
                 "here, so on iPhone this is the band that follows 'fair'.")]
        public ThermalLevel urgentAtStatus = ThermalLevel.Severe;

        [Tooltip("Headroom forecast that also means urgent. 1.0 is the OS saying it is " +
                 "throttling now, not in ten seconds.")]
        [Range(0.5f, 1.5f)]
        public float urgentAtHeadroom = 1f;

        [Tooltip("Seconds between drops while urgent. Zero means every evaluation, which " +
                 "takes the ladder from the top rung to the bottom in about two seconds. " +
                 "That is the right answer for a device the OS is already throttling, but it " +
                 "is also the most abrupt change the game can make - raise it to a second or " +
                 "two if you would rather the descent be visible as a slide than a jump.")]
        [Range(0f, 30f)]
        public float urgentStepSeconds;

        [Header("Cooling down")]
        [Tooltip("Seconds of being cool before one rung given up to heat is handed back. " +
                 "Deliberately much shorter than the climb window below: nothing was " +
                 "measured to take these rungs away, so nothing has to be measured to give " +
                 "them back - only the heat that caused it has to be gone.")]
        [Range(1f, 120f)]
        public float thermalRecoverySeconds = 10f;

        [Tooltip("Seconds in the background after which returning hands back every rung " +
                 "given up to heat at once, rather than one at a time. A phone that has not " +
                 "rendered for a minute has genuinely cooled, and the frame the app comes " +
                 "back on is the one moment where a large visual change costs nothing - " +
                 "nothing is in motion to see it happen. If the device is in fact still hot, " +
                 "the shedding loop puts the penalty back within the minute.")]
        [Range(5f, 600f)]
        public float backgroundedRecoverySeconds = 60f;

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

        /// <summary>
        /// Reads the sensor and says what to do about it.
        /// </summary>
        /// <remarks>
        /// Tested from the top down, so a misconfigured set of thresholds - urgent below
        /// shed, say - degrades to the most cautious answer rather than to an unpredictable
        /// one.
        /// </remarks>
        /// <param name="sensor">The thermal sensor. <c>null</c> reads as cool.</param>
        /// <returns>Which band the device is in.</returns>
        public HeatBand Band(ThermalSensor sensor)
        {
            if (sensor == null)
                return HeatBand.Cool;

            if (sensor.AtLeast(urgentAtStatus, urgentAtHeadroom))
                return HeatBand.Urgent;

            if (sensor.AtLeast(shedAtStatus, shedAtHeadroom))
                return HeatBand.Shed;

            if (sensor.AtLeast(holdAtStatus, holdAtHeadroom))
                return HeatBand.Hold;

            return HeatBand.Cool;
        }

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
