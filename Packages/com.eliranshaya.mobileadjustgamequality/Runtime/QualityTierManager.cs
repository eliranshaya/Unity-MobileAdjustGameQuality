using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// Owns the active quality tier: resolves one at boot, applies it to Unity's quality
    /// level and to the URP asset, and exposes the handful of knobs the rest of the game
    /// is allowed to touch.
    /// </summary>
    /// <remarks>
    /// Runs very early (<c>-1000</c>) so the tier is settled before any camera, volume or
    /// gameplay system reads graphics state, and survives scene loads via
    /// <c>DontDestroyOnLoad</c>.
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("Mobile Adjust Game Quality/Quality Tier Manager")]
    [DisallowMultipleComponent]
    public class QualityTierManager : MonoBehaviour
    {
        /// <summary>
        /// The per-tier configuration written by the Setup window and read at runtime.
        /// </summary>
        [Serializable]
        public class TierSettings
        {
            /// <summary>
            /// Index into Unity's Quality Settings list. Written by the Setup window from
            /// the indices of the three appended <c>MAQ</c> levels - it is never a
            /// constant, because the list already contains the project's own levels.
            /// </summary>
            public int unityQualityLevel;

            /// <summary>Frame cap applied through <see cref="Application.targetFrameRate"/>.</summary>
            public int targetFrameRate = 60;

            /// <summary>Render scale applied when the tier is entered.</summary>
            public float baseRenderScale = 1f;

            /// <summary>Lowest render scale the adaptive controller may fall to.</summary>
            public float minRenderScale = 0.8f;

            /// <summary>Highest render scale the adaptive controller may climb to.</summary>
            public float maxRenderScale = 1f;

            /// <summary>
            /// Frame cap used as the last-resort thermal fallback. <c>0</c> means "never
            /// drop the frame rate on this tier" - correct for Low, which is already at 30.
            /// </summary>
            public int thermalFallbackFrameRate;
        }

        const string k_TierKey = "quality.tier";
        const string k_UserSetKey = "quality.userSet";

        [Tooltip("One entry per QualityTier, in Low / Balanced / High order. " +
                 "Populated by Tools > Mobile Quality > Setup.")]
        [SerializeField]
        TierSettings[] tiers = CreateDefaultTiers();

        [Tooltip("Forces a tier at startup, ignoring saved preferences and device detection. " +
                 "-1 disables. Only honoured in DEVELOPER_MODE builds.")]
        [SerializeField]
        int developerForcedTier = -1;

        UniversalRenderPipelineAsset m_UrpAsset;
        int m_ActiveFrameRate = -1;

        /// <summary>The live instance, or <c>null</c> before the prefab has awoken.</summary>
        public static QualityTierManager Instance { get; private set; }

        /// <summary>The tier currently applied.</summary>
        public QualityTier CurrentTier { get; private set; } = QualityTier.Balanced;

        /// <summary>The settings block for <see cref="CurrentTier"/>.</summary>
        public TierSettings Current => tiers[Mathf.Clamp((int)CurrentTier, 0, tiers.Length - 1)];

        /// <summary>The render scale currently pushed into the URP asset.</summary>
        public float CurrentRenderScale { get; private set; } = 1f;

        /// <summary>The current tier's frame budget in milliseconds.</summary>
        public float FrameBudgetMs => 1000f / Mathf.Max(1, Current.targetFrameRate);

        /// <summary>
        /// Unscaled time of the last tier change. The adaptive controller uses it to skip
        /// its warmup window, because the frames straight after a pipeline rebuild are
        /// not representative of anything.
        /// </summary>
        public float LastTierChangeTime { get; private set; }

        /// <summary>
        /// The inspector override, or <c>-1</c> when unset. Exposed because the field is
        /// only *consulted* under <c>DEVELOPER_MODE</c> but is serialized unconditionally:
        /// keeping the field out of the <c>#if</c> means the prefab has identical
        /// serialized data in developer and release builds, so toggling the define never
        /// dirties the asset or drops a value.
        /// </summary>
        public int DeveloperForcedTier => developerForcedTier;

        /// <summary>Raised after a tier has been fully applied.</summary>
        public event Action<QualityTier> OnTierChanged;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                QualityDevLog.Warn("A second QualityTierManager was loaded; destroying the duplicate.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            ValidateTiers();

            // Always before touching targetFrameRate: with vSync on, the frame cap is
            // silently ignored and every tier ends up running at the panel refresh rate.
            QualitySettings.vSyncCount = 0;

            ApplyTier(ResolveStartupTier(), true);
        }

        void Start()
        {
#if DEVELOPER_MODE
            // The overlay is attached from code, never from the scene or the prefab.
            // A serialized reference to a class that is compiled out leaves a permanent
            // "The referenced script on this Behaviour is missing!" warning on the
            // GameObject in every player build, which is exactly the kind of noise that
            // trains people to ignore their logs.
            if (GetComponent<QualityDebugOverlay>() == null)
                gameObject.AddComponent<QualityDebugOverlay>();
#endif
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                return;

            // Several Android OEMs reset the frame cap and re-enable vSync when the app
            // returns to the foreground, which shows up as "the game runs at 60 for a
            // few minutes and then goes back to 30" in bug reports.
            QualitySettings.vSyncCount = 0;
            if (m_ActiveFrameRate > 0)
                Application.targetFrameRate = m_ActiveFrameRate;
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
        /// graphics knob the adaptive controller is allowed to move.
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
        /// Overrides the frame cap without changing tier. Zeroes vSync first, otherwise
        /// the cap has no effect at all.
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
        /// Re-runs device detection and applies the result. Ignores any saved player
        /// choice, so this is a "reset to recommended" action, not a startup path.
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
            LastTierChangeTime = Time.unscaledTime;

            TierSettings settings = Current;

            if (settings.unityQualityLevel >= 0 && settings.unityQualityLevel < QualitySettings.names.Length)
            {
                // applyExpensiveChanges reloads textures and rebuilds the pipeline. It is
                // correct here - this is a deliberate tier switch - and it is never set
                // from the adaptive loop, where it would cause a visible hitch.
                QualitySettings.SetQualityLevel(settings.unityQualityLevel, applyExpensiveChanges);
            }
            else
            {
                QualityDevLog.FatalConfigError(
                    $"Tier {tier} points at quality level {settings.unityQualityLevel}, which does not exist " +
                    $"({QualitySettings.names.Length} levels present). Re-run Tools > Mobile Quality > Setup.");
            }

            // Re-read after the level switch, never before: QualitySettings.renderPipeline
            // returns the asset assigned to the *new* level, and any reference cached
            // across the switch points at the old one.
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
            // These match the Setup window's presets. The Setup window overwrites
            // unityQualityLevel with the real appended indices; the rest is already right.
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
