// The whole file is compiled out without MAQ_DEVELOPER_MODE so the class does not exist in
// a release build. That is also why nothing may reference it from a scene or a prefab: a
// serialized reference to a class that no longer exists leaves a permanent "referenced
// script is missing" warning on the GameObject. MobileQualityManager.Start() adds it with
// AddComponent inside the same #if, and only when OverlaySettings.show is on.
#if MAQ_DEVELOPER_MODE

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// On-screen IMGUI readout of everything the quality system can see. Developer builds
    /// only, and only when <see cref="OverlaySettings.show"/> is on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Input goes through IMGUI's own event queue rather than <c>UnityEngine.Input</c> or the
    /// Input System package, because the overlay has to work in projects configured for
    /// either backend - a project set to "Input System Package (New)" throws from the legacy
    /// <c>Input</c> class at runtime.
    /// </para>
    /// <para>
    /// The panel measures its own content and then anchors itself. That is what lets the
    /// anchor be a dropdown: a box pinned to the bottom right has to know how tall it is
    /// before it knows where its top edge goes.
    /// </para>
    /// </remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class QualityDebugOverlay : MonoBehaviour
    {
        const string k_Title = "MOBILE QUALITY";
        const string k_CloseLabel = "X";
        const string k_OpenLabel = "MAQ";

        const int k_TapsToToggle = 4;
        const float k_TapWindowSeconds = 2f;
        const float k_TapCornerSize = 96f;

        MobileQualityManager m_Manager;
        readonly List<string> m_Lines = new List<string>(48);

        bool m_Visible = true;
        int m_TapCount;
        float m_FirstTapTime;
        Rect m_OccupiedRect;

        GUIStyle m_Panel;
        GUIStyle m_Text;
        GUIStyle m_Title;
        GUIStyle m_Section;
        GUIStyle m_Button;
        Texture2D m_PanelTexture;
        float m_TextureOpacity = -1f;

        void Awake()
        {
            m_Manager = GetComponent<MobileQualityManager>();

            OverlaySettings settings = m_Manager != null ? m_Manager.Overlay : null;
            m_Visible = settings == null || settings.openOnStart;
        }

        void OnDestroy()
        {
            if (m_PanelTexture != null)
                Destroy(m_PanelTexture);
        }

        void OnGUI()
        {
            if (m_Manager == null)
                return;

            OverlaySettings settings = m_Manager.Overlay;
            if (settings == null || !settings.show)
                return;

            EnsureStyles(settings);
            HandleToggleInput();

            // Built once per frame on the layout pass and reused for the repaint. IMGUI calls
            // OnGUI at least twice a frame and these are interpolated strings.
            if (Event.current.type == EventType.Layout)
            {
                if (m_Visible)
                {
                    BuildLines(settings);
                    m_OccupiedRect = MeasurePanel(settings);
                }
                else
                {
                    m_OccupiedRect = settings.showToggleButton ? MeasureOpenButton(settings) : Rect.zero;
                }
            }

            if (!m_Visible)
            {
                if (settings.showToggleButton && GUI.Button(m_OccupiedRect, k_OpenLabel, m_Button))
                    m_Visible = true;
                return;
            }

            DrawPanel();
        }

        void DrawPanel()
        {
            GUILayout.BeginArea(m_OccupiedRect, m_Panel);

            float rowHeight = m_Text.fontSize * 1.8f;

            GUILayout.BeginHorizontal();
            GUILayout.Label(k_Title, m_Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(k_CloseLabel, m_Button, GUILayout.Width(rowHeight), GUILayout.Height(rowHeight)))
                m_Visible = false;
            GUILayout.EndHorizontal();

            foreach (string line in m_Lines)
            {
                // A line that starts with '#' is a section heading, not data.
                if (line.Length > 0 && line[0] == '#')
                    GUILayout.Label(line.Substring(1), m_Section);
                else
                    GUILayout.Label(line, m_Text);
            }

            GUILayout.EndArea();
        }

        void BuildLines(OverlaySettings settings)
        {
            m_Lines.Clear();

            if (settings.Has(OverlayFields.Performance))
                BuildPerformance();

            if (settings.Has(OverlayFields.Quality))
                BuildQuality();

            if (settings.Has(OverlayFields.Adaptation))
                BuildAdaptation();

            if (settings.Has(OverlayFields.Thermal))
                BuildThermal();

            if (settings.Has(OverlayFields.Urp))
                BuildUrp();

            if (settings.Has(OverlayFields.AdaptivePerformance))
                BuildAdaptivePerformance();

            if (settings.Has(OverlayFields.Device))
                BuildDevice();

            if (settings.Has(OverlayFields.Screen))
                BuildScreen();

            if (settings.Has(OverlayFields.Memory))
                BuildMemory();
        }

        void BuildPerformance()
        {
            FrameTimeSampler stats = m_Manager.Stats;
            QualityStep step = m_Manager.CurrentStep;
            float budget = step.FrameBudgetMs;
            bool over = budget > 0f && stats.P75Ms > budget * m_Manager.Adaptation.missTargetFactor;

            m_Lines.Add("#PERFORMANCE");
            m_Lines.Add($"FPS         {stats.Fps:F0} / {step.targetFrameRate}");
            m_Lines.Add($"Frame       {stats.P75Ms:F1} p75 · {stats.P95Ms:F1} p95 · {stats.MaxMs:F1} worst");
            m_Lines.Add(budget > 0f
                ? $"Budget      {budget:F1} ms   {(over ? "OVER" : "ok")}"
                : "Budget      uncapped");
        }

        void BuildAdaptation()
        {
            string phase = m_Manager.IsInStartupGrace
                ? "warming up"
                : m_Manager.IsCalibrating
                    ? $"calibrating {m_Manager.CalibrationRemaining:F0}s"
                    : "steady";

            m_Lines.Add("#ADAPTATION");
            m_Lines.Add($"Step        {m_Manager.CurrentStepIndex} of 0-{m_Manager.Profile.WorstStep}" +
                        $"  (0 = best)" +
                        $"{(m_Manager.IsStepRemembered ? " · saved" : string.Empty)}");
            m_Lines.Add($"Applied     {m_Manager.CurrentStep}");
            m_Lines.Add($"State       {phase} · {m_Manager.LastReason}" +
                        $"{(m_Manager.AllowAdjustments ? string.Empty : " · BLOCKED")}");
        }

        void BuildThermal()
        {
            ThermalSensor thermal = m_Manager.Thermal;

            m_Lines.Add("#THERMAL");

            if (thermal == null || !thermal.IsAvailable)
            {
                m_Lines.Add("Thermal     no API here (editor or desktop)");
                return;
            }

            string headroom = float.IsNaN(thermal.Headroom) ? string.Empty : $" · headroom {thermal.Headroom:F2}";
            string state = thermal.IsSevere ? "SEVERE" : thermal.IsWarm ? "WARM" : "cool";

            m_Lines.Add($"Thermal     {state} · {thermal.Level}{headroom}");
        }

        void BuildQuality()
        {
            m_Lines.Add("#QUALITY");
            m_Lines.Add($"Level         {m_Manager.CurrentQualityLevelName}  (index {QualitySettings.GetQualityLevel()})");
            m_Lines.Add($"Target FPS    {m_Manager.CurrentStep.targetFrameRate}");
            m_Lines.Add($"vSync         {QualitySettings.vSyncCount}");
            m_Lines.Add($"LOD bias      {QualitySettings.lodBias:F2}");
            m_Lines.Add($"Texture limit {QualitySettings.globalTextureMipmapLimit}");
            m_Lines.Add($"Aniso         {QualitySettings.anisotropicFiltering}");
        }

        void BuildUrp()
        {
            m_Lines.Add("#URP");

            UniversalRenderPipelineAsset urp = m_Manager.UrpAsset;
            if (urp == null)
            {
                m_Lines.Add("Asset         <none active>");
                return;
            }

            m_Lines.Add($"Asset         {urp.name}");
            m_Lines.Add($"Render scale  {urp.renderScale:F2}");
            m_Lines.Add($"MSAA          {urp.msaaSampleCount}x");
            m_Lines.Add($"HDR           {(urp.supportsHDR ? "on" : "off")}");
            m_Lines.Add($"Main shadows  {(urp.supportsMainLightShadows ? $"{urp.mainLightShadowmapResolution}" : "off")}");
            m_Lines.Add($"Add. shadows  {(urp.supportsAdditionalLightShadows ? $"{urp.additionalLightsShadowmapResolution}" : "off")}");
            m_Lines.Add($"Add. lights   {urp.maxAdditionalLightsCount}  ({urp.additionalLightsRenderingMode})");
            m_Lines.Add($"Shadow dist   {urp.shadowDistance:F0}  ({urp.shadowCascadeCount} cascade{(urp.shadowCascadeCount == 1 ? "" : "s")})");
            m_Lines.Add($"Soft shadows  {(urp.supportsSoftShadows ? "on" : "off")}");
            m_Lines.Add($"SRP batcher   {(urp.useSRPBatcher ? "on" : "off")}");
        }

        void BuildAdaptivePerformance()
        {
            AdaptivePerformanceBridge ap = m_Manager.AdaptivePerformance;

            m_Lines.Add("#ADAPTIVE PERFORMANCE");

            if (!ap.IsInstalled)
            {
                m_Lines.Add("Not installed. Optional - the ladder above does not need it.");
                return;
            }

            if (!ap.IsActive)
            {
                m_Lines.Add("Installed but not running on this device.");
                return;
            }

            m_Lines.Add($"Thermal       {ap.ThermalWarning}");
            m_Lines.Add($"Temp level    {FormatSigned(ap.TemperatureLevel)}   trend {ap.TemperatureTrend:F2}");
            m_Lines.Add($"CPU frame     {FormatMs(ap.CpuFrameMs)}");
            m_Lines.Add($"GPU frame     {FormatMs(ap.GpuFrameMs)}");
            m_Lines.Add($"AP target     {FormatMs(ap.TargetFrameMs)}");
            m_Lines.Add($"Bottleneck    {ap.Bottleneck}");
            m_Lines.Add($"Quality index {(ap.QualityIndex < 0 ? "n/a" : ap.QualityIndex.ToString())}");
        }

        void BuildDevice()
        {
            m_Lines.Add("#DEVICE");
            m_Lines.Add($"Model         {SystemInfo.deviceModel}");
            m_Lines.Add($"GPU           {SystemInfo.graphicsDeviceName}");
            m_Lines.Add($"API           {SystemInfo.graphicsDeviceType}  sm{SystemInfo.graphicsShaderLevel}");
            m_Lines.Add($"VRAM          {SystemInfo.graphicsMemorySize} MB");
            m_Lines.Add($"RAM           {SystemInfo.systemMemorySize} MB");
            m_Lines.Add($"CPU           {SystemInfo.processorCount} cores");
        }

        void BuildScreen()
        {
            m_Lines.Add("#SCREEN");
            m_Lines.Add($"Resolution    {Screen.width} x {Screen.height}");
            m_Lines.Add($"DPI           {Screen.dpi:F0}");
            m_Lines.Add($"Refresh       {Screen.currentResolution.refreshRateRatio.value:F0} Hz");
            m_Lines.Add($"Orientation   {Screen.orientation}");
            m_Lines.Add($"Safe area     {Screen.safeArea.width:F0} x {Screen.safeArea.height:F0}");
        }

        void BuildMemory()
        {
            const float toMb = 1f / (1024f * 1024f);

            m_Lines.Add("#MEMORY");
            m_Lines.Add($"Allocated     {Profiler.GetTotalAllocatedMemoryLong() * toMb:F0} MB");
            m_Lines.Add($"Reserved      {Profiler.GetTotalReservedMemoryLong() * toMb:F0} MB");
            m_Lines.Add($"Mono heap     {Profiler.GetMonoUsedSizeLong() * toMb:F0} MB");
        }

        static string FormatMs(float ms) => ms < 0f ? "n/a" : $"{ms:F2} ms";

        static string FormatSigned(float value) => value < 0f ? "n/a" : value.ToString("F2");

        Rect MeasurePanel(OverlaySettings settings)
        {
            var content = new GUIContent();
            float width = 0f;

            foreach (string line in m_Lines)
            {
                bool heading = line.Length > 0 && line[0] == '#';
                content.text = heading ? line.Substring(1) : line;

                float lineWidth = (heading ? m_Section : m_Text).CalcSize(content).x;
                if (lineWidth > width)
                    width = lineWidth;
            }

            float rowHeight = m_Text.fontSize * 1.8f;

            content.text = k_Title;
            float headerWidth = m_Title.CalcSize(content).x + rowHeight * 1.5f;
            if (headerWidth > width)
                width = headerWidth;

            float lineHeight = m_Text.lineHeight > 0f ? m_Text.lineHeight : m_Text.fontSize * 1.2f;
            float height = rowHeight + m_Lines.Count * lineHeight + 4f;

            RectOffset padding = m_Panel.padding;
            return AnchoredRect(settings,
                width + padding.left + padding.right,
                height + padding.top + padding.bottom);
        }

        Rect MeasureOpenButton(OverlaySettings settings)
        {
            Vector2 size = m_Button.CalcSize(new GUIContent(k_OpenLabel));
            return AnchoredRect(settings,
                Mathf.Max(size.x, m_Text.fontSize * 3.5f),
                Mathf.Max(size.y, m_Text.fontSize * 1.9f));
        }

        static Rect AnchoredRect(OverlaySettings settings, float width, float height)
        {
            width = Mathf.Min(width, Screen.width - settings.margin * 2f);
            height = Mathf.Min(height, Screen.height - settings.margin * 2f);

            float margin = settings.margin;
            float x;
            float y;

            switch (settings.anchor)
            {
                case OverlayAnchor.TopCenter:
                case OverlayAnchor.MiddleCenter:
                case OverlayAnchor.BottomCenter:
                    x = (Screen.width - width) * 0.5f;
                    break;
                case OverlayAnchor.TopRight:
                case OverlayAnchor.MiddleRight:
                case OverlayAnchor.BottomRight:
                    x = Screen.width - width - margin;
                    break;
                default:
                    x = margin;
                    break;
            }

            switch (settings.anchor)
            {
                case OverlayAnchor.MiddleLeft:
                case OverlayAnchor.MiddleCenter:
                case OverlayAnchor.MiddleRight:
                    y = (Screen.height - height) * 0.5f;
                    break;
                case OverlayAnchor.BottomLeft:
                case OverlayAnchor.BottomCenter:
                case OverlayAnchor.BottomRight:
                    y = Screen.height - height - margin;
                    break;
                default:
                    y = margin;
                    break;
            }

            return new Rect(x, y, width, height);
        }

        void HandleToggleInput()
        {
            Event e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1)
            {
                m_Visible = !m_Visible;
                e.Use();
                return;
            }

            if (e.type != EventType.MouseDown)
                return;

            // A tap on the overlay itself is never part of the corner gesture, or anchoring
            // to the top right would make its own buttons unusable.
            if (m_OccupiedRect.Contains(e.mousePosition))
                return;

            var corner = new Rect(Screen.width - k_TapCornerSize, 0f, k_TapCornerSize, k_TapCornerSize);
            if (!corner.Contains(e.mousePosition))
                return;

            if (m_TapCount == 0 || Time.unscaledTime - m_FirstTapTime > k_TapWindowSeconds)
            {
                m_TapCount = 0;
                m_FirstTapTime = Time.unscaledTime;
            }

            m_TapCount++;
            if (m_TapCount < k_TapsToToggle)
                return;

            m_TapCount = 0;
            m_Visible = !m_Visible;
            e.Use();
        }

        void EnsureStyles(OverlaySettings settings)
        {
            // Sized from the render target, not from Screen.dpi. In the editor Game view dpi
            // reports the monitor's, around 96, which produced a panel the size of a stamp.
            float reference = Mathf.Max(Screen.width, Screen.height);
            int fontSize = Mathf.Max(14, Mathf.RoundToInt(reference / 55f * settings.scale));

            if (m_Text == null)
            {
                // Margins and padding are zeroed so a label occupies exactly lineHeight.
                // GUILayout inserts each style's margin between elements, and the skin's
                // label carries a few pixels of it that the measuring code cannot see -
                // enough, over forty lines, to push content past the bottom of the panel.
                m_Text = new GUIStyle(GUI.skin.label)
                {
                    richText = false,
                    wordWrap = false,
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0)
                };
                m_Text.normal.textColor = Color.white;

                m_Title = new GUIStyle(m_Text) { fontStyle = FontStyle.Bold };

                m_Section = new GUIStyle(m_Text) { fontStyle = FontStyle.Bold };
                m_Section.normal.textColor = new Color(0.45f, 0.8f, 1f);

                m_Button = new GUIStyle(GUI.skin.button) { margin = new RectOffset(2, 2, 0, 0) };
            }

            m_Text.fontSize = fontSize;
            m_Title.fontSize = fontSize;
            m_Section.fontSize = fontSize;
            m_Button.fontSize = fontSize;

            if (m_Panel == null)
                m_Panel = new GUIStyle(GUI.skin.box);

            int pad = Mathf.RoundToInt(fontSize * 0.6f);
            m_Panel.padding = new RectOffset(pad, pad, pad, pad);

            if (m_PanelTexture == null || !Mathf.Approximately(m_TextureOpacity, settings.backgroundOpacity))
            {
                if (m_PanelTexture != null)
                    Destroy(m_PanelTexture);

                m_PanelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                m_PanelTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, settings.backgroundOpacity));
                m_PanelTexture.Apply();

                m_Panel.normal.background = m_PanelTexture;
                m_TextureOpacity = settings.backgroundOpacity;
            }
        }
    }
}

#endif
