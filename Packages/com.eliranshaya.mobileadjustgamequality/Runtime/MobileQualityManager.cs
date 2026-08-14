using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// The one component this package puts in your scene. It resolves a quality tier at
    /// boot, applies it, and then keeps the frame budget by adapting render scale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything else - the frame time window, the thermal sensor, the decision policy -
    /// is a plain class this component owns. None of them are MonoBehaviours, because none
    /// of them need a transform, an inspector header or their own Update. One component
    /// means one execution order, one lifetime, and one place to look.
    /// </para>
    /// <para>
    /// Runs very early (<c>-1000</c>) so the tier is settled before any camera, volume or
    /// gameplay system reads graphics state, and survives scene loads via
    /// <c>DontDestroyOnLoad</c>. Put it in your first scene and forget about it.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("Mobile Adjust Game Quality/Mobile Quality Manager")]
    [DisallowMultipleComponent]
    public class MobileQualityManager : MonoBehaviour
    {
        const string k_TierKey = "quality.tier";
        const string k_UserSetKey = "quality.userSet";

        /// <summary>
        /// Android returns NaN from <c>getThermalHeadroom</c> if polled more than once a
        /// second, and thermal state never moves fast enough to need a tighter loop.
        /// </summary>
        const float k_ThermalPollSeconds = 5f;

        [Tooltip("One entry per QualityTier, in Low / Balanced / High order. " +
                 "Populated by Tools > Mobile Quality > Setup.")]
        [SerializeField]
        TierSettings[] tiers = CreateDefaultTiers();

        [Tooltip("Tuning for the adaptive render scale loop. The defaults are the shipping " +
                 "values - you should not need to touch anything in here.")]
        [SerializeField]
        AdaptiveSettings adaptive = new AdaptiveSettings();

        [Tooltip("Forces a tier at startup, ignoring saved preferences and device detection. " +
                 "-1 disables. Only honoured in DEVELOPER_MODE builds.")]
        [SerializeField]
        int developerForcedTier = -1;

        // Cached once and reused for the lifetime of the component. A `new WaitForSeconds`
        // inside the loop would allocate on every single iteration, twice a second,
        // forever - which is exactly the kind of steady drip that fills a mobile GC.
        // WaitForSecondsRealtime rather than WaitForSeconds because these loops must keep
        // running while the game sits at timeScale = 0 in a pause menu.
        readonly WaitForSecondsRealtime m_ThermalWait = new WaitForSecondsRealtime(k_ThermalPollSeconds);
        readonly WaitForSecondsRealtime m_EvaluateWait = new WaitForSecondsRealtime(0.5f);
        readonly WaitForSecondsRealtime m_WarmupWait = new WaitForSecondsRealtime(3f);

        FrameTimeSampler m_Sampler;
        ThermalSensor m_Thermal;
        AdaptiveScaler m_Scaler;

        UniversalRenderPipelineAsset m_UrpAsset;
        int m_ActiveFrameRate = -1;
        float m_LastEvaluateTime;
        bool m_RestartRequested;
        bool m_ThermalPinned;
        bool m_ThermalFrameRateDropped;

        /// <summary>The live instance, or <c>null</c> before the prefab has awoken.</summary>
        public static MobileQualityManager Instance { get; private set; }

        /// <summary>The tier currently applied.</summary>
        public QualityTier CurrentTier { get; private set; } = QualityTier.Balanced;

        /// <summary>The settings block for <see cref="CurrentTier"/>.</summary>
        public TierSettings Current => tiers[Mathf.Clamp((int)CurrentTier, 0, tiers.Length - 1)];

        /// <summary>The render scale currently pushed into the URP asset.</summary>
        public float CurrentRenderScale { get; private set; } = 1f;

        /// <summary>The current tier's frame budget in milliseconds.</summary>
        public float FrameBudgetMs => 1000f / Mathf.Max(1, Current.targetFrameRate);

        /// <summary>The most recent 95th-percentile frame time, in milliseconds.</summary>
        public float LastP95Ms { get; private set; }

        /// <summary>The most recent mean frame time, in milliseconds. Reporting only.</summary>
        public float LastAvgMs { get; private set; }

        /// <summary>The most recent thermal status reported by the OS.</summary>
        public ThermalLevel ThermalLevel => m_Thermal != null ? m_Thermal.Level : ThermalLevel.Unknown;

        /// <summary>The most recent thermal headroom forecast, or <c>NaN</c> if unavailable.</summary>
        public float ThermalHeadroom => m_Thermal != null ? m_Thermal.Headroom : float.NaN;

        /// <summary>True while the thermal fallback has taken over from normal adaptation.</summary>
        public bool IsThermalThrottled => m_ThermalPinned || m_ThermalFrameRateDropped;

        /// <summary>True when the oscillation guard is currently blocking upward steps.</summary>
        public bool IsRenderScaleLocked => m_Scaler != null && m_Scaler.IsUpwardLocked(Time.unscaledTime);

        /// <summary>True when upward steps are locked for the remainder of the session.</summary>
        public bool IsRenderScaleLockedForSession => m_Scaler != null && m_Scaler.IsLockedForSession;

        /// <summary>
        /// Gate for gameplay. Set to <c>false</c> during combat, cutscenes or anything else
        /// where a resolution change would be noticed, and back to <c>true</c> at a natural
        /// break. Measurement continues while this is <c>false</c>, so a change that became
        /// due mid-fight is applied the moment the fight ends.
        /// </summary>
        public bool AllowAdjustments { get; set; } = true;

        /// <summary>
        /// The inspector override, or <c>-1</c> when unset. The field is serialized
        /// unconditionally but only consulted under <c>DEVELOPER_MODE</c>, so toggling the
        /// define never dirties the prefab or drops a value.
        /// </summary>
        public int DeveloperForcedTier => developerForcedTier;

        /// <summary>Raised after a tier has been fully applied.</summary>
        public event Action<QualityTier> OnTierChanged;

        /// <summary>Raised whenever the render scale actually moves. Carries the new scale.</summary>
        public event Action<float> OnRenderScaleChanged;

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

            ValidateTiers();

            m_Sampler = new FrameTimeSampler(adaptive.SafeSampleCount);
            m_Scaler = new AdaptiveScaler(adaptive);
            m_Thermal = new ThermalSensor();
            m_Thermal.Open();
            m_Thermal.Poll();

            // Always before touching targetFrameRate: with vSync on, the frame cap is
            // silently ignored and every tier ends up at the panel refresh rate.
            QualitySettings.vSyncCount = 0;

            ApplyTier(ResolveStartupTier(), true);
        }

        void OnEnable()
        {
            StartCoroutine(ThermalRoutine());
            StartCoroutine(AdaptRoutine());
        }

        void OnDisable()
        {
            StopAllCoroutines();
        }

        void Start()
        {
#if DEVELOPER_MODE
            // The overlay is attached from code, never from the scene or the prefab. A
            // serialized reference to a class that is compiled out leaves a permanent
            // "The referenced script on this Behaviour is missing!" warning on the
            // GameObject in every player build.
            if (GetComponent<QualityDebugOverlay>() == null)
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
        /// The only per-frame work in the whole package: one ring buffer write.
        /// </summary>
        /// <remarks>
        /// This cannot become a coroutine. A p95 is only meaningful if every frame is
        /// measured, and a coroutine resuming each frame costs an iterator MoveNext plus a
        /// scheduler visit - strictly more than this method. Everything that does not need
        /// per-frame resolution lives in a coroutine instead.
        /// </remarks>
        void Update()
        {
            // Unscaled: a paused or slow-motion game still has to render on time.
            m_Sampler.Record(Time.unscaledDeltaTime * 1000f);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                return;

            // Several Android OEMs reset the frame cap and re-enable vSync when the app
            // returns to the foreground, which shows up in bug reports as "the game runs at
            // 60 for a few minutes and then goes back to 30".
            QualitySettings.vSyncCount = 0;
            if (m_ActiveFrameRate > 0)
                Application.targetFrameRate = m_ActiveFrameRate;

            // The device may have cooled in a pocket or heated in one; either way the
            // measurements from before the pause describe a moment that is over.
            m_Thermal?.Poll();
            m_RestartRequested = true;
        }

        IEnumerator ThermalRoutine()
        {
            while (true)
            {
                m_ThermalWait.Reset();
                yield return m_ThermalWait;

                m_Thermal.Poll();
            }
        }

        IEnumerator AdaptRoutine()
        {
            while (true)
            {
                // The frames right after a pipeline rebuild are shader compilation and
                // texture reloads, not gameplay. Measuring them would trigger an immediate
                // step down on a device that is perfectly fine.
                m_WarmupWait.waitTime = adaptive.warmupSeconds;
                m_WarmupWait.Reset();
                yield return m_WarmupWait;

                m_Sampler.Reset();
                m_Scaler.ResetWindow();
                m_LastEvaluateTime = Time.unscaledTime;
                m_RestartRequested = false;

                while (!m_RestartRequested)
                {
                    m_EvaluateWait.waitTime = adaptive.evaluateInterval;
                    m_EvaluateWait.Reset();
                    yield return m_EvaluateWait;

                    if (m_RestartRequested)
                        break;

                    Tick();
                }
            }
        }

        void Tick()
        {
            float now = Time.unscaledTime;
            float elapsed = now - m_LastEvaluateTime;
            m_LastEvaluateTime = now;

            // A gap far longer than the interval means the app was backgrounded or the
            // editor sat on a breakpoint. Feeding that into the hysteresis timers would
            // clear the four-second threshold in a single tick and step down for no reason.
            if (elapsed > adaptive.evaluateInterval * 5f)
            {
                m_Sampler.Reset();
                m_Scaler.ResetWindow();
                return;
            }

            if (!m_Sampler.TryGetStatistics(adaptive.MinimumSamplesToDecide, out float p95, out float average))
                return;

            LastP95Ms = p95;
            LastAvgMs = average;

            float budget = FrameBudgetMs;
            m_Scaler.Accumulate(elapsed, p95, budget);

            // Gameplay veto covers everything, thermal response included. A resolution drop
            // in the middle of a boss fight is worse than four more warm seconds.
            if (!AllowAdjustments)
                return;

            if (HandleThermal(p95 > budget * adaptive.overBudgetFactor))
                return;

            TierSettings settings = Current;
            AdaptiveScaler.Step step = m_Scaler.Decide(
                now, CurrentRenderScale, settings.minRenderScale, settings.maxRenderScale, m_Thermal.IsWarm);

            if (step == AdaptiveScaler.Step.Hold)
                return;

            float delta = step == AdaptiveScaler.Step.Down ? -adaptive.stepSize : adaptive.stepSize;
            StepRenderScale(delta, $"p95 {p95:F1}ms vs {budget:F1}ms budget");
        }

        /// <summary>
        /// Applies or unwinds the thermal fallback.
        /// </summary>
        /// <param name="overBudget">Whether p95 is currently over the frame budget.</param>
        /// <returns>True when thermal handling owns this tick and normal adaptation is skipped.</returns>
        bool HandleThermal(bool overBudget)
        {
            if (m_Thermal.IsSevere)
            {
                // Order matters, and it is the whole point of the two-step response:
                // pinning the render scale costs sharpness, which players tolerate and
                // often do not consciously notice. Halving the frame rate is the single
                // most noticeable change the game can make, so it is the last resort and
                // only happens if the cheaper move was not enough.
                if (!m_ThermalPinned)
                {
                    m_ThermalPinned = true;
                    float min = Current.minRenderScale;
                    if (!Mathf.Approximately(min, CurrentRenderScale))
                    {
                        SetRenderScale(min);
                        OnRenderScaleChanged?.Invoke(CurrentRenderScale);
                    }

                    QualityDevLog.Warn($"Thermal: severe. Render scale pinned to {min:F2}.");
                    return true;
                }

                int fallback = Current.thermalFallbackFrameRate;
                if (overBudget && !m_ThermalFrameRateDropped && fallback > 0)
                {
                    m_ThermalFrameRateDropped = true;
                    SetTargetFrameRate(fallback);
                    QualityDevLog.Warn(
                        $"Thermal: still over budget at minimum render scale. Frame cap dropped to {fallback}.");
                }

                return true;
            }

            // Cooled down. Unwind in reverse, one step per tick, so the player gets the
            // frame rate back before the resolution starts climbing again.
            if (m_ThermalFrameRateDropped)
            {
                m_ThermalFrameRateDropped = false;
                RestoreTargetFrameRate();
                QualityDevLog.Log("Thermal: cooled. Frame cap restored.");
                return true;
            }

            if (m_ThermalPinned)
            {
                m_ThermalPinned = false;
                m_Scaler.ResetWindow();
                QualityDevLog.Log("Thermal: cooled. Render scale released.");
                return true;
            }

            return false;
        }

        void StepRenderScale(float delta, string reason)
        {
            float before = CurrentRenderScale;
            SetRenderScale(before + delta);
            float after = CurrentRenderScale;

            if (Mathf.Approximately(before, after))
                return;

            QualityDevLog.Log($"Render scale {before:F2} -> {after:F2} ({reason}).");
            OnRenderScaleChanged?.Invoke(after);
        }

        /// <summary>
        /// Switches tier by index.
        /// </summary>
        /// <param name="index">0 Low, 1 Balanced, 2 High. Clamped.</param>
        /// <param name="isPlayerChoice">
        /// True when the player picked this in a settings menu. A player choice is sticky:
        /// device detection will not override it on later launches.
        /// </param>
        public void SetTier(int index, bool isPlayerChoice = false)
        {
            SetTier((QualityTier)Mathf.Clamp(index, 0, tiers.Length - 1), isPlayerChoice);
        }

        /// <summary>
        /// Switches tier.
        /// </summary>
        /// <param name="tier">The tier to apply.</param>
        /// <param name="isPlayerChoice">
        /// True when the player picked this in a settings menu. A player choice is sticky:
        /// device detection will not override it on later launches.
        /// </param>
        public void SetTier(QualityTier tier, bool isPlayerChoice = false)
        {
            int index = Mathf.Clamp((int)tier, 0, tiers.Length - 1);
            tier = (QualityTier)index;

            PlayerPrefs.SetInt(k_TierKey, index);
            if (isPlayerChoice)
                PlayerPrefs.SetInt(k_UserSetKey, 1);
            PlayerPrefs.Save();

            if (tier == CurrentTier && m_UrpAsset != null)
            {
                QualityDevLog.Log($"Tier {tier} already active; skipping the pipeline switch.");
                return;
            }

            ApplyTier(tier, true);
        }

        /// <summary>
        /// Sets the render scale, clamped to the current tier's range. This is the only
        /// graphics knob the adaptive loop is allowed to move.
        /// </summary>
        /// <param name="scale">Requested render scale.</param>
        public void SetRenderScale(float scale)
        {
            TierSettings settings = Current;
            float clamped = Mathf.Clamp(scale, settings.minRenderScale, settings.maxRenderScale);

            if (m_UrpAsset == null)
                m_UrpAsset = ResolveUrpAsset();

            if (m_UrpAsset == null)
            {
                QualityDevLog.FatalConfigError(
                    "No UniversalRenderPipelineAsset is active, so render scale cannot be applied. " +
                    "Check Graphics Settings and the Quality level's Render Pipeline Asset.");
                return;
            }

            if (Mathf.Approximately(clamped, CurrentRenderScale))
                return;

            CurrentRenderScale = clamped;
            m_UrpAsset.renderScale = clamped;
        }

        /// <summary>
        /// Overrides the frame cap without changing tier. Zeroes vSync first, otherwise the
        /// cap has no effect at all.
        /// </summary>
        /// <param name="fps">Target frames per second. Use <c>-1</c> for uncapped.</param>
        public void SetTargetFrameRate(int fps)
        {
            QualitySettings.vSyncCount = 0;
            m_ActiveFrameRate = fps;
            Application.targetFrameRate = fps;
            QualityDevLog.Log($"Target frame rate set to {fps}.");
        }

        /// <summary>Restores the frame cap defined by the current tier.</summary>
        public void RestoreTargetFrameRate()
        {
            SetTargetFrameRate(Current.targetFrameRate);
        }

        /// <summary>
        /// Re-runs device detection and applies the result, ignoring any saved player
        /// choice. This is a "reset to recommended" action, not a startup path.
        /// </summary>
        public void ApplyDetectedTier()
        {
            PlayerPrefs.DeleteKey(k_UserSetKey);
            SetTier(DeviceTierDetector.Detect());
        }

        QualityTier ResolveStartupTier()
        {
#if DEVELOPER_MODE
            if (developerForcedTier >= 0 && developerForcedTier < tiers.Length)
            {
                QualityDevLog.Warn($"Startup tier forced to {(QualityTier)developerForcedTier} by developerForcedTier.");
                return (QualityTier)developerForcedTier;
            }
#endif

            if (PlayerPrefs.HasKey(k_TierKey))
            {
                var saved = (QualityTier)Mathf.Clamp(PlayerPrefs.GetInt(k_TierKey), 0, tiers.Length - 1);
                bool userSet = PlayerPrefs.GetInt(k_UserSetKey, 0) == 1;
                QualityDevLog.Log($"Startup tier {saved} restored from {(userSet ? "player choice" : "previous session")}.");
                return saved;
            }

            QualityTier detected = DeviceTierDetector.Detect();
            QualityDevLog.Log($"Startup tier {detected} chosen by device detection.");
            return detected;
        }

        void ApplyTier(QualityTier tier, bool applyExpensiveChanges)
        {
            CurrentTier = tier;
            TierSettings settings = Current;

            if (settings.unityQualityLevel >= 0 && settings.unityQualityLevel < QualitySettings.names.Length)
            {
                // applyExpensiveChanges reloads textures and rebuilds the pipeline. Correct
                // here - this is a deliberate tier switch - and never set from the adaptive
                // loop, where it would cause a visible hitch.
                QualitySettings.SetQualityLevel(settings.unityQualityLevel, applyExpensiveChanges);
            }
            else
            {
                QualityDevLog.FatalConfigError(
                    $"Tier {tier} points at quality level {settings.unityQualityLevel}, which does not exist " +
                    $"({QualitySettings.names.Length} levels present). Re-run Tools > Mobile Quality > Setup.");
            }

            // Re-read after the level switch, never before: QualitySettings.renderPipeline
            // returns the asset assigned to the *new* level, and any reference cached across
            // the switch points at the old one.
            m_UrpAsset = ResolveUrpAsset();

            if (m_UrpAsset == null)
            {
                QualityDevLog.FatalConfigError(
                    "No UniversalRenderPipelineAsset found after switching quality level. " +
                    "The game will render with whatever pipeline is active and quality control is disabled.");
            }
            else
            {
                CurrentRenderScale = Mathf.Clamp(settings.baseRenderScale, settings.minRenderScale, settings.maxRenderScale);
                m_UrpAsset.renderScale = CurrentRenderScale;
            }

            QualitySettings.vSyncCount = 0;
            m_ActiveFrameRate = settings.targetFrameRate;
            Application.targetFrameRate = settings.targetFrameRate;

            m_ThermalPinned = false;
            m_ThermalFrameRateDropped = false;
            m_RestartRequested = true;

            QualityDevLog.Log(
                $"Applied tier {tier} | qualityLevel={settings.unityQualityLevel} " +
                $"fps={settings.targetFrameRate} renderScale={CurrentRenderScale:F2} " +
                $"urp='{(m_UrpAsset != null ? m_UrpAsset.name : "<none>")}'");

            OnTierChanged?.Invoke(tier);
        }

        static UniversalRenderPipelineAsset ResolveUrpAsset()
        {
            // The quality level's override wins when one is assigned; otherwise the
            // project-wide default is what is actually rendering.
            var asset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (asset != null)
                return asset;

            return GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        }

        void ValidateTiers()
        {
            if (tiers == null || tiers.Length != 3)
            {
                QualityDevLog.Warn("Tier array was missing or the wrong length; rebuilding it with defaults.");
                tiers = CreateDefaultTiers();
                return;
            }

            for (int i = 0; i < tiers.Length; i++)
            {
                if (tiers[i] != null)
                    continue;

                QualityDevLog.Warn($"Tier {i} was null; rebuilding it with defaults.");
                tiers[i] = CreateDefaultTiers()[i];
            }
        }

        static TierSettings[] CreateDefaultTiers()
        {
            // These match the Setup window's presets. Setup overwrites unityQualityLevel
            // with the real appended indices; the rest is already right.
            return new[]
            {
                new TierSettings
                {
                    unityQualityLevel = 0,
                    targetFrameRate = 30,
                    baseRenderScale = 0.75f,
                    minRenderScale = 0.60f,
                    maxRenderScale = 0.75f,
                    thermalFallbackFrameRate = 0
                },
                new TierSettings
                {
                    unityQualityLevel = 1,
                    targetFrameRate = 60,
                    baseRenderScale = 0.85f,
                    minRenderScale = 0.70f,
                    maxRenderScale = 0.85f,
                    thermalFallbackFrameRate = 30
                },
                new TierSettings
                {
                    unityQualityLevel = 2,
                    targetFrameRate = 60,
                    baseRenderScale = 1.00f,
                    minRenderScale = 0.80f,
                    maxRenderScale = 1.00f,
                    thermalFallbackFrameRate = 30
                }
            };
        }
    }
}
