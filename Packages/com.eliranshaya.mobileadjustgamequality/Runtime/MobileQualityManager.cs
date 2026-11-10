using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// The one component this package puts in your scene. Drop the generated prefab into
    /// your first scene and it holds the frame budget by moving along a quality ladder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two signals drive it, and they answer different questions. <b>Frame time</b> answers
    /// "is the device failing to reach its target?" - it can answer that honestly, because
    /// missing a target really does show up in wall-clock frame time. It cannot answer "does
    /// the device have room to spare", because <c>Application.targetFrameRate</c> makes the
    /// engine sleep until the frame is due, pinning the measurement to the budget.
    /// <b>Temperature</b> answers "is the device in trouble?" with no such blind spot, on
    /// both platforms, capped or not.
    /// </para>
    /// <para>
    /// The two are kept apart all the way through. <see cref="QualityGovernor"/> holds a rung
    /// this device is believed capable of and a separate, expiring penalty for heat;
    /// <see cref="ProvenStepTracker"/> records only the rungs held on merit, so a hot
    /// afternoon can never be mistaken for a slow phone.
    /// </para>
    /// <para>
    /// Runs early (<c>-1000</c>) so quality is settled before any camera or volume reads
    /// graphics state, and survives scene loads via <c>DontDestroyOnLoad</c>.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("Mobile Adjust Game Quality/Mobile Quality Manager")]
    [DisallowMultipleComponent]
    public class MobileQualityManager : MonoBehaviour
    {
        /// <summary>Frames the statistics window covers. Two seconds at 60 FPS.</summary>
        const int k_SampleCount = 120;

        [Tooltip("The quality configuration and the ladder. Populated by " +
                 "Tools > Mobile Quality > Setup.")]
        [SerializeField]
        MobileQualityProfile profile = new MobileQualityProfile();

        [Tooltip("How the game moves along the ladder.")]
        [SerializeField]
        AdaptationSettings adaptation = new AdaptationSettings();

        [Tooltip("On-screen debug readout. Needs a MAQ_DEVELOPER_MODE build - in a release " +
                 "build the overlay is compiled out and none of this has any effect.")]
        [SerializeField]
        OverlaySettings overlay = new OverlaySettings();

        // Constructed once and reused. A `new WaitForSeconds` inside a loop would allocate on
        // every iteration, forever. Realtime rather than scaled, so both loops keep running
        // while the game sits at timeScale = 0 in a pause menu.
        readonly WaitForSecondsRealtime m_ThermalWait = new WaitForSecondsRealtime(5f);
        readonly WaitForSecondsRealtime m_CheckWait = new WaitForSecondsRealtime(0.25f);

        FrameTimeSampler m_Sampler;
        ThermalSensor m_Thermal;
        QualityGovernor m_Governor;
        ProvenStepTracker m_Proven;
        UniversalRenderPipelineAsset m_UrpAsset;

        float m_LastCheckTime;
        float m_CalibrationEndsAt;
        float m_GraceEndsAt;
        DateTime m_BackgroundedAt = DateTime.MinValue;

        /// <summary>The live instance, or <c>null</c> before the prefab has awoken.</summary>
        public static MobileQualityManager Instance { get; private set; }

        /// <summary>The quality configuration and ladder.</summary>
        public MobileQualityProfile Profile => profile;

        /// <summary>Ladder tuning.</summary>
        public AdaptationSettings Adaptation => adaptation;

        /// <summary>Debug overlay configuration.</summary>
        public OverlaySettings Overlay => overlay;

        /// <summary>Frame time statistics over the last two seconds.</summary>
        public FrameTimeSampler Stats => m_Sampler;

        /// <summary>What the OS says about the device's temperature.</summary>
        public ThermalSensor Thermal => m_Thermal;

        /// <summary>Adaptive Performance's view of the device, when that package is installed.</summary>
        public AdaptivePerformanceBridge AdaptivePerformance { get; private set; }

        /// <summary>The URP asset actually rendering.</summary>
        public UniversalRenderPipelineAsset UrpAsset => m_UrpAsset;

        /// <summary>Which rung of the ladder is applied.</summary>
        public int CurrentStepIndex => m_Governor != null ? m_Governor.AppliedStep : 0;

        /// <summary>The rung that is applied.</summary>
        public QualityStep CurrentStep => profile.Step(CurrentStepIndex);

        /// <summary>
        /// The rung this device is believed capable of, before any allowance for heat.
        /// </summary>
        public int PerfStep => m_Governor != null ? m_Governor.PerfStep : 0;

        /// <summary>How many rungs are currently being given up to heat. Zero when cool.</summary>
        public int ThermalPenalty => m_Governor != null ? m_Governor.ThermalPenalty : 0;

        /// <summary>
        /// The best rung this device has held on merit, or
        /// <see cref="ProvenStepTracker.Nothing"/> when it has not proven one yet.
        /// </summary>
        public int ProvenStep => m_Proven != null ? m_Proven.BestProvenStep : ProvenStepTracker.Nothing;

        /// <summary>True once this device has proven a rung, this session or an earlier one.</summary>
        public bool IsStepProven => m_Proven != null && m_Proven.HasProof;

        /// <summary>
        /// The worst rung a normal descent may reach, from the frame rate this device has
        /// proven it can hold. <see cref="int.MaxValue"/> means no floor.
        /// </summary>
        public int FloorStep => m_Governor != null ? m_Governor.FloorStep : int.MaxValue;

        /// <summary>Why the ladder last moved, or did not.</summary>
        public QualityGovernor.Reason LastReason =>
            m_Governor != null ? m_Governor.LastReason : QualityGovernor.Reason.Holding;

        /// <summary>
        /// True while the opening calibration window is still open: every frame is evaluated
        /// and the ladder reacts within a second, so the device lands on the right rung while
        /// the player is still loading in.
        /// </summary>
        public bool IsCalibrating => Time.unscaledTime < m_CalibrationEndsAt;

        /// <summary>Seconds of calibration left, or zero.</summary>
        public float CalibrationRemaining => Mathf.Max(0f, m_CalibrationEndsAt - Time.unscaledTime);

        /// <summary>True while the opening grace is still running and nothing is judged.</summary>
        public bool IsInStartupGrace => Time.unscaledTime < m_GraceEndsAt;

        /// <summary>
        /// Gate for gameplay. Set to <c>false</c> during a cutscene or anything else where a
        /// quality change would be noticed, and back to <c>true</c> at a natural break.
        /// </summary>
        public bool AllowAdjustments { get; set; } = true;

        /// <summary>
        /// What the device's temperature currently says to do, from the thresholds on
        /// <see cref="adaptation"/>.
        /// </summary>
        public HeatBand Heat => adaptation == null ? HeatBand.Cool : adaptation.Band(m_Thermal);

        /// <summary>
        /// True when the ladder is held at rung 0 because this is the editor.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A desktop running a mobile game has performance to spare, so in Play mode the
        /// ladder has nothing useful to find - all it does is make the Game view look worse
        /// than the build will, over a scene you are trying to look at. Frame times and
        /// temperature are still measured and still shown in the overlay; only the decision
        /// is suppressed.
        /// </para>
        /// <para>
        /// The editor is a poor judge anyway: the Game view renders a phone resolution on a
        /// desktop panel, next to the inspector and the console, with the editor's own stalls
        /// in the frame times. Turn <c>bestStepInEditor</c> off when you specifically want to
        /// watch the ladder move; leave it on the rest of the time.
        /// </para>
        /// <para>
        /// Compiled out entirely in a player build - the field is never read there, so a
        /// prefab that happens to have it ticked cannot pin a shipped game to rung 0.
        /// </para>
        /// </remarks>
        public bool IsPinnedToBest
        {
#if UNITY_EDITOR
            get => adaptation != null && adaptation.bestStepInEditor;
#else
            get => false;
#endif
        }

        /// <summary>Raised whenever the ladder moves. Carries the new rung index.</summary>
        public event Action<int> OnStepChanged;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                QualityDevLog.Warn("A second MobileQualityManager was loaded; destroying the duplicate.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            int ladderSize = profile.steps != null ? profile.steps.Length : 1;

            m_Sampler = new FrameTimeSampler(k_SampleCount);
            m_Governor = new QualityGovernor(adaptation, ladderSize);
            m_Proven = new ProvenStepTracker(ladderSize);
            AdaptivePerformance = new AdaptivePerformanceBridge();

            m_Thermal = new ThermalSensor();
            m_Thermal.Open();
            m_Thermal.Poll();

            ApplyQualityLevel();

            int start = ResolveStartStep(ladderSize, out string origin);

            if (IsPinnedToBest)
            {
                // No grace and no calibration to run. Skipping calibration also keeps the
                // per-frame path to a single ring buffer write: it is the calibration branch
                // in Update that recalculates every frame, and there is nothing here for it
                // to decide. The steady-state coroutine keeps the overlay live at 4 Hz.
                m_GraceEndsAt = Time.unscaledTime;
                m_CalibrationEndsAt = Time.unscaledTime;
            }
            else
            {
                m_GraceEndsAt = Time.unscaledTime + adaptation.startupGraceSeconds;
                m_CalibrationEndsAt = m_GraceEndsAt + adaptation.calibrationSeconds;
            }

            ApplyStep(start, origin);
            RefreshFloor();
        }

        /// <summary>
        /// Chooses the rung to open on, in descending order of how much the answer is worth.
        /// </summary>
        /// <remarks>
        /// A rung this device proved on real hardware is a measurement and always wins. Below
        /// that, the device's own GPU, memory and screen are a far better guess than one fixed
        /// number for the whole market. <c>startStep</c> is the last resort.
        /// </remarks>
        int ResolveStartStep(int ladderSize, out string origin)
        {
            if (IsPinnedToBest)
            {
                // Not the proven rung either: a rung proven in the editor was proven on a
                // desktop, and opening a Play session on it is the same guess with an extra
                // step in front of it.
                origin = "editor (pinned to the best rung)";
                return 0;
            }

            if (adaptation.rememberProvenStep && QualityStepMemory.TryLoad(ladderSize, out int proven))
            {
                // Seeded rather than merely applied, so the frame rate floor this device
                // earned is in force from the first second rather than after it re-proves it.
                m_Proven.Seed(proven);
                origin = "proven on this device";
                return profile.ClampStep(proven);
            }

            if (adaptation.useDeviceProfiler)
            {
                origin = "device profile";
                return profile.ClampStep(DeviceProfiler.SuggestStep(ladderSize));
            }

            origin = "default";
            return profile.ClampStep(adaptation.startStep);
        }

        void OnEnable()
        {
            StartCoroutine(ThermalRoutine());
            StartCoroutine(CheckRoutine());
        }

        void OnDisable() => StopAllCoroutines();

        void Start()
        {
#if MAQ_DEVELOPER_MODE
            QualityDevLog.Log($"Device profile: {DeviceProfiler.Describe()}");

            // Added from code, never from the prefab. A serialized reference to a class that
            // is compiled out leaves a permanent "referenced script is missing" warning on
            // the GameObject in every release build.
            if (overlay != null && overlay.show && GetComponent<QualityDebugOverlay>() == null)
                gameObject.AddComponent<QualityDebugOverlay>();
#endif
        }

        void OnDestroy()
        {
            m_Thermal?.Dispose();

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// The only per-frame work: one ring buffer write.
        /// </summary>
        /// <remarks>
        /// This cannot be a coroutine. A percentile is only meaningful if every frame is
        /// measured, and a coroutine resuming each frame costs an iterator <c>MoveNext</c>
        /// plus a scheduler visit - strictly more than this method call.
        /// </remarks>
        void Update()
        {
            m_Sampler.Record(Time.unscaledDeltaTime * 1000f);

            // Calibration evaluates every frame. It is a bounded window - thirty seconds by
            // default - and the cost is one sort of a 120-float scratch buffer, so a few
            // microseconds. Note that what actually decides how fast this converges is the
            // calibration window lengths, not the check rate: a one-second sustain window
            // cannot fire more than once a second however often it is asked.
            if (IsCalibrating)
                Check();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                m_BackgroundedAt = DateTime.UtcNow;
                return;
            }

            // Several Android OEMs reset the frame cap and re-enable vSync when the app
            // returns to the foreground, which shows up as "the game drops to 30 after a
            // while" in bug reports. The measurements from before the pause describe a moment
            // that is over, so the window starts again too.
            ApplyFrameRate(CurrentStep);
            m_Sampler.Reset();
            m_Thermal?.Poll();
            m_Governor.Reset(Time.unscaledTime);
            m_LastCheckTime = Time.unscaledTime;

            RecoverFromBackground();
        }

        /// <summary>
        /// Hands back everything given up to heat when the app returns from a long spell in
        /// the background.
        /// </summary>
        /// <remarks>
        /// A phone that has not rendered for a minute has genuinely cooled, and the frame the
        /// app comes back on is the one moment where a large visual change costs nothing -
        /// there is nothing in motion for the player to see it happen against. The fresh
        /// reading is treated as permission to hand the rungs back rather than as proof that
        /// keeping them is safe: Android updates the thermal status on its own cadence, so a
        /// device that still reads warm keeps its penalty, and if handing it back turns out to
        /// be wrong the shedding loop takes it away again within the minute.
        /// </remarks>
        void RecoverFromBackground()
        {
            if (m_BackgroundedAt == DateTime.MinValue)
                return;

            double away = (DateTime.UtcNow - m_BackgroundedAt).TotalSeconds;
            m_BackgroundedAt = DateTime.MinValue;

            if (away < adaptation.backgroundedRecoverySeconds || Heat >= HeatBand.Shed)
                return;

            if (m_Governor.ReleaseThermalPenalty())
                ApplyResolvedStep("cooled while in the background");
        }

        IEnumerator ThermalRoutine()
        {
            while (true)
            {
                m_ThermalWait.waitTime = adaptation.thermalPollSeconds;
                m_ThermalWait.Reset();
                yield return m_ThermalWait;

                m_Thermal.Poll();
            }
        }

        IEnumerator CheckRoutine()
        {
            m_LastCheckTime = Time.unscaledTime;

            while (true)
            {
                m_CheckWait.waitTime = adaptation.checkEverySeconds;
                m_CheckWait.Reset();
                yield return m_CheckWait;

                // Update already drove the checks this phase; running them here as well would
                // double-count elapsed time into the hysteresis timers.
                if (!IsCalibrating)
                    Check();
            }
        }

        void Check()
        {
            m_Sampler.Recalculate();

            float now = Time.unscaledTime;
            float elapsed = now - m_LastCheckTime;

            // Recalculate above, then stop: the overlay still reports honest frame times and
            // an honest temperature, but nothing acts on them.
            if (IsPinnedToBest)
            {
                m_LastCheckTime = now;
                return;
            }

            // Nothing is judged during the opening grace, and nothing is judged until the
            // window is full: a partial window is dominated by whatever happened during
            // loading, and a percentile over six frames is just "one of the worst frames".
            if (!AllowAdjustments || now < m_GraceEndsAt || m_Sampler.Count < k_SampleCount)
            {
                m_LastCheckTime = now;
                return;
            }

            // A gap far longer than the interval means the app was backgrounded or the editor
            // sat on a breakpoint. Feeding that into the timers would clear a three-second
            // threshold in a single tick.
            m_LastCheckTime = now;

            if (elapsed > adaptation.checkEverySeconds * 5f)
            {
                m_Sampler.Reset();
                m_Governor.Reset(now);
                return;
            }

            RefreshFloor();

            int previous = m_Governor.AppliedStep;

            int next = m_Governor.Evaluate(
                now, elapsed,
                m_Sampler.P75Ms,
                CurrentStep.FrameBudgetMs,
                profile.FindLowerFrameRateStep(previous),
                Heat,
                IsCalibrating);

            if (next != previous)
            {
                ApplyResolvedStep(m_Governor.LastReason.ToString());
                return;
            }

            RecordProof(elapsed);
        }

        /// <summary>
        /// Counts time towards proving the applied rung, and stores it once it is earned.
        /// </summary>
        void RecordProof(float elapsed)
        {
            if (!m_Governor.IsProving)
                return;

            if (!m_Proven.Accumulate(CurrentStepIndex, elapsed, adaptation.provenAfterSeconds))
                return;

            RefreshFloor();

            if (adaptation.rememberProvenStep)
                QualityStepMemory.Save(m_Proven.BestProvenStep, profile.steps.Length);
        }

        /// <summary>
        /// Points the governor's floor at the last rung running at the frame rate this device
        /// has proven it can hold.
        /// </summary>
        /// <remarks>
        /// Recomputed rather than cached so that turning the setting off in the inspector
        /// takes effect immediately, the way every other value here does. It is a walk over
        /// nine ints.
        /// </remarks>
        void RefreshFloor()
        {
            m_Governor.FloorStep = adaptation.protectProvenFrameRate && m_Proven.HasProof
                ? profile.FloorStepFor(m_Proven.BestProvenStep)
                : int.MaxValue;
        }

        /// <summary>
        /// Moves to a rung of the ladder and applies it.
        /// </summary>
        /// <remarks>
        /// Treats the rung as this device's capability, so any outstanding thermal penalty is
        /// cleared: an explicit choice is not something the ladder should then unwind from.
        /// </remarks>
        /// <param name="index">Rung index, clamped to the ladder.</param>
        /// <param name="reason">Why, for the log.</param>
        public void ApplyStep(int index, string reason = "manual")
        {
            m_Governor.SetPerfStep(profile.ClampStep(index));
            ApplyResolvedStep(reason);
        }

        /// <summary>URP accepts 1, 2, 4 or 8; anything else is rounded down to the nearest.</summary>
        static int ClampMsaa(int samples) => samples >= 8 ? 8 : samples >= 4 ? 4 : samples >= 2 ? 2 : 1;

        /// <summary>Applies whichever rung the governor currently resolves to.</summary>
        void ApplyResolvedStep(string reason)
        {
            int index = CurrentStepIndex;
            QualityStep step = profile.Step(index);

            if (m_UrpAsset != null)
            {
                // Only fields URP allows to be written at runtime. supportsMainLightShadows
                // and shadowCascadeCount have no public setter, which is why shadows are
                // turned off with a zero distance instead: URP computes
                // cameraRenderShadows = maxShadowDistance > 0 and skips the whole pass.
                m_UrpAsset.renderScale = step.renderScale;

                // All or nothing. Zero is how shadows get switched off, because
                // supportsMainLightShadows has no public setter; switching them back on
                // restores the single authored distance rather than anything per-rung, so no
                // rung can ever leave near objects shadowed and objects behind them not.
                m_UrpAsset.shadowDistance = step.shadowsEnabled ? profile.shadowDistance : 0f;

                // Through UrpCompat because URP 14 declares this property's setter internal
                // and URP 17 does not, so assigning it here would not compile on 2022.3.
                if (step.shadowsEnabled)
                    UrpCompat.TrySetMainLightShadowmapResolution(m_UrpAsset, step.shadowResolution);

                // Only written when it changes: a new sample count reallocates the camera's
                // render targets, so a step change that keeps MSAA must not pay for that.
                if (step.OverridesMsaa)
                {
                    int msaa = ClampMsaa(step.msaaSampleCount);
                    if (m_UrpAsset.msaaSampleCount != msaa)
                        m_UrpAsset.msaaSampleCount = msaa;
                }
            }

            ApplyFrameRate(step);

            m_Governor.Reset(Time.unscaledTime);
            m_Sampler.Reset();

            QualityDevLog.Log($"Quality step {index}/{profile.WorstStep} — {step} ({reason}).");
            OnStepChanged?.Invoke(index);
        }

        void ApplyFrameRate(QualityStep step)
        {
            // Always first: with vSync on, the frame cap is silently ignored.
            if (profile.disableVSync)
                QualitySettings.vSyncCount = 0;

            Application.targetFrameRate = step.targetFrameRate;
        }

        /// <summary>The name Unity has for the active quality level.</summary>
        public string CurrentQualityLevelName
        {
            get
            {
                string[] names = QualitySettings.names;
                int index = QualitySettings.GetQualityLevel();
                return index >= 0 && index < names.Length ? names[index] : "<none>";
            }
        }

        void ApplyQualityLevel()
        {
            int levelCount = QualitySettings.names.Length;

            if (profile.unityQualityLevel < 0 || profile.unityQualityLevel >= levelCount)
            {
                QualityDevLog.FatalConfigError(
                    $"Quality level {profile.unityQualityLevel} does not exist ({levelCount} present). " +
                    "Re-run Tools > Mobile Quality > Setup.");
            }
            else
            {
                // applyExpensiveChanges reloads textures and rebuilds the pipeline. Correct
                // here because it happens once, at startup, before anything is on screen.
                QualitySettings.SetQualityLevel(profile.unityQualityLevel, true);
            }

            // Re-read after the switch, never before: QualitySettings.renderPipeline returns
            // the asset assigned to the *new* level, and a reference cached across the switch
            // points at the old one.
            m_UrpAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset
                         ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

            if (m_UrpAsset == null)
            {
                QualityDevLog.FatalConfigError(
                    "No UniversalRenderPipelineAsset is active, so nothing can be adapted. Check " +
                    "Project Settings > Graphics and the quality level's Render Pipeline Asset.");
            }
        }
    }
}
