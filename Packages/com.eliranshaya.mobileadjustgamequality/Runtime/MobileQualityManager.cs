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
        readonly WaitForSecondsRealtime m_ThermalWait = new WaitForSecondsRealtime(30f);
        readonly WaitForSecondsRealtime m_CheckWait = new WaitForSecondsRealtime(0.25f);

        FrameTimeSampler m_Sampler;
        ThermalSensor m_Thermal;
        QualityGovernor m_Governor;
        UniversalRenderPipelineAsset m_UrpAsset;

        float m_LastCheckTime;
        float m_CalibrationEndsAt;
        float m_GraceEndsAt;
        float m_StepStableSince;
        bool m_StepRemembered;

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
        public int CurrentStepIndex { get; private set; }

        /// <summary>The rung that is applied.</summary>
        public QualityStep CurrentStep => profile.Step(CurrentStepIndex);

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

        /// <summary>True once the current rung has held long enough to be written to PlayerPrefs.</summary>
        public bool IsStepRemembered => m_StepRemembered;

        /// <summary>
        /// Gate for gameplay. Set to <c>false</c> during a cutscene or anything else where a
        /// quality change would be noticed, and back to <c>true</c> at a natural break.
        /// </summary>
        public bool AllowAdjustments { get; set; } = true;

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

            m_Sampler = new FrameTimeSampler(k_SampleCount);
            m_Governor = new QualityGovernor(adaptation, profile.steps != null ? profile.steps.Length : 1);
            AdaptivePerformance = new AdaptivePerformanceBridge();

            m_Thermal = new ThermalSensor();
            m_Thermal.Open();
            m_Thermal.Poll();

            ApplyQualityLevel();

            // A rung that held on real hardware last session is a measurement; startStep is a
            // guess. Prefer the measurement.
            int start = profile.ClampStep(adaptation.startStep);
            string origin = "default";

            if (adaptation.rememberLastStep && QualityStepMemory.TryLoad(profile.steps.Length, out int remembered))
            {
                start = profile.ClampStep(remembered);
                origin = "remembered from the last session";
            }

            m_GraceEndsAt = Time.unscaledTime + adaptation.startupGraceSeconds;
            m_CalibrationEndsAt = m_GraceEndsAt + adaptation.calibrationSeconds;
            ApplyStep(start, origin);
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
        /// This cannot be a coroutine. A p95 is only meaningful if every frame is measured,
        /// and a coroutine resuming each frame costs an iterator <c>MoveNext</c> plus a
        /// scheduler visit - strictly more than this method call.
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
                return;

            // Several Android OEMs reset the frame cap and re-enable vSync when the app
            // returns to the foreground, which shows up as "the game drops to 30 after a
            // while" in bug reports. The measurements from before the pause describe a moment
            // that is over, so the window starts again too.
            ApplyFrameRate(CurrentStep);
            m_Sampler.Reset();
            m_Thermal?.Poll();
            m_Governor.Reset(Time.unscaledTime);
            m_LastCheckTime = Time.unscaledTime;
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

            // Nothing is judged during the opening grace, and nothing is judged until the
            // window is full: a partial window is dominated by whatever happened during
            // loading, and p95 over six frames is just "the worst frame".
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

            int next = m_Governor.Evaluate(
                now, elapsed,
                m_Sampler.P75Ms,
                CurrentStep.FrameBudgetMs,
                CurrentStepIndex,
                profile.WorstStep,
                profile.FindLowerFrameRateStep(CurrentStepIndex),
                m_Thermal.IsWarm,
                m_Thermal.IsSevere,
                IsCalibrating);

            if (next != CurrentStepIndex)
            {
                ApplyStep(next, m_Governor.LastReason.ToString());
                return;
            }

            RememberIfSettled(now);
        }

        /// <summary>
        /// Writes the current rung to PlayerPrefs once it has held long enough to count as
        /// where this device belongs, rather than somewhere it passed through.
        /// </summary>
        void RememberIfSettled(float now)
        {
            if (!adaptation.rememberLastStep || m_StepRemembered)
                return;

            if (now - m_StepStableSince < adaptation.rememberAfterSeconds)
                return;

            QualityStepMemory.Save(CurrentStepIndex, profile.steps.Length);
            m_StepRemembered = true;
        }

        /// <summary>
        /// Moves to a rung of the ladder and applies it.
        /// </summary>
        /// <param name="index">Rung index, clamped to the ladder.</param>
        /// <param name="reason">Why, for the log.</param>
        public void ApplyStep(int index, string reason = "manual")
        {
            int clamped = profile.ClampStep(index);
            QualityStep step = profile.Step(clamped);

            CurrentStepIndex = clamped;

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

                if (step.shadowsEnabled)
                    m_UrpAsset.mainLightShadowmapResolution = step.shadowResolution;
            }

            ApplyFrameRate(step);

            m_Governor.Reset(Time.unscaledTime);
            m_Sampler.Reset();

            m_StepStableSince = Time.unscaledTime;
            m_StepRemembered = false;

            QualityDevLog.Log($"Quality step {clamped}/{profile.WorstStep} — {step} ({reason}).");
            OnStepChanged?.Invoke(clamped);
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
