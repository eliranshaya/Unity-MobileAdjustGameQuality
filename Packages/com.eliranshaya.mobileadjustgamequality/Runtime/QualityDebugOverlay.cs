// The whole file is compiled out without DEVELOPER_MODE so the class does not exist in a
// release build. That is also why nothing may ever reference it from a scene or a prefab:
// a serialized reference to a class that no longer exists leaves a permanent
// "The referenced script on this Behaviour is missing!" warning on the GameObject.
// MobileQualityManager.Start() adds it with AddComponent inside the same #if.
#if DEVELOPER_MODE

using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// On-screen IMGUI readout of everything the quality system is doing, plus live tier
    /// switching. Developer builds only.
    /// </summary>
    /// <remarks>
    /// Input goes through IMGUI's own event queue rather than <c>UnityEngine.Input</c> or
    /// the Input System package, because the overlay has to work in projects configured for
    /// either backend - and a project set to "Input System Package (New)" throws from the
    /// legacy <c>Input</c> class at runtime.
    /// </remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class QualityDebugOverlay : MonoBehaviour
    {
        const int k_TapsToToggle = 4;
        const float k_TapWindowSeconds = 2f;
        const float k_CornerSize = 96f;

        MobileQualityManager m_Manager;

        bool m_Visible;
        int m_TapCount;
        float m_FirstTapTime;

        GUIStyle m_Panel;
        GUIStyle m_Text;
        Texture2D m_PanelTexture;

        void Awake()
        {
            m_Manager = GetComponent<MobileQualityManager>();
        }

        void OnDestroy()
        {
            if (m_PanelTexture != null)
                Destroy(m_PanelTexture);
        }

        void OnGUI()
        {
            HandleToggleInput();

            if (!m_Visible)
                return;

            EnsureStyles();

            // Sized from the font so the panel does not clip its own buttons on a phone,
            // where the DPI-scaled text is roughly twice the size it is in the editor.
            float width = Mathf.Min(Mathf.Max(440f, m_Text.fontSize * 25f), Screen.width - 20f);
            float height = Mathf.Min(m_Text.fontSize * 18f + 60f, Screen.height - 20f);
            GUILayout.BeginArea(new Rect(10f, 10f, width, height), m_Panel);
            GUILayout.Label("MOBILE QUALITY", m_Text);

            if (m_Manager == null)
            {
                GUILayout.Label("MobileQualityManager missing.", m_Text);
                GUILayout.EndArea();
                return;
            }

            float budget = m_Manager.FrameBudgetMs;
            bool over = m_Manager.LastP95Ms > budget;

            GUILayout.Label($"Tier          {m_Manager.CurrentTier}  (quality level {m_Manager.Current.unityQualityLevel})", m_Text);
            GUILayout.Label($"Target        {m_Manager.Current.targetFrameRate} fps  =  {budget:F2} ms budget", m_Text);
            GUILayout.Label($"Avg           {m_Manager.LastAvgMs:F2} ms", m_Text);
            GUILayout.Label($"p95           {m_Manager.LastP95Ms:F2} ms   {(over ? "OVER" : "ok")}", m_Text);
            GUILayout.Label($"Render scale  {m_Manager.CurrentRenderScale:F2}" +
                            $"  (min {m_Manager.Current.minRenderScale:F2} / max {m_Manager.Current.maxRenderScale:F2})", m_Text);

            string lockState = m_Manager.IsRenderScaleLockedForSession
                ? "locked for session"
                : m_Manager.IsRenderScaleLocked ? "locked (temporary)" : "free";
            GUILayout.Label($"Step up       {lockState}", m_Text);

            GUILayout.Label($"Adjustments   {(m_Manager.AllowAdjustments ? "allowed" : "blocked by gameplay")}" +
                            $"{(m_Manager.IsThermalThrottled ? "  [thermal fallback active]" : string.Empty)}", m_Text);

            string headroom = float.IsNaN(m_Manager.ThermalHeadroom) ? "n/a" : m_Manager.ThermalHeadroom.ToString("F2");
            string heatState = m_Manager.IsThermalThrottled ? "SEVERE - pinned"
                : m_Manager.IsSheddingHeat ? "WARM - shedding"
                : m_Manager.IsDeviceWarm ? "WARM - holding"
                : "cool";
            GUILayout.Label($"Thermal       {m_Manager.ThermalLevel}   headroom {headroom}", m_Text);
            GUILayout.Label($"Heat response {heatState}", m_Text);

            GUILayout.Label($"GPU           {SystemInfo.graphicsDeviceName}", m_Text);
            GUILayout.Label($"API           {SystemInfo.graphicsDeviceType}  sm{SystemInfo.graphicsShaderLevel}", m_Text);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Low", GUILayout.Height(34f)))
                m_Manager.SetTier(QualityTier.Low);
            if (GUILayout.Button("Balanced", GUILayout.Height(34f)))
                m_Manager.SetTier(QualityTier.Balanced);
            if (GUILayout.Button("High", GUILayout.Height(34f)))
                m_Manager.SetTier(QualityTier.High);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
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

            // Four taps in the top-right corner, for devices without a keyboard. The hit
            // area is an invisible IMGUI region rather than a raw touch read so it works
            // with either input backend and does not fight the game's own UI.
            if (e.type != EventType.MouseDown)
                return;

            var corner = new Rect(Screen.width - k_CornerSize, 0f, k_CornerSize, k_CornerSize);
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

        void EnsureStyles()
        {
            if (m_Panel != null)
                return;

            m_PanelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_PanelTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f));
            m_PanelTexture.Apply();

            m_Panel = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10)
            };
            m_Panel.normal.background = m_PanelTexture;

            // Scale with DPI so the overlay stays legible on a phone instead of rendering
            // as a row of ants next to a 3x UI.
            int fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.dpi > 0f ? Screen.dpi / 18f : 14f));
            m_Text = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                richText = false,
                wordWrap = false
            };
            m_Text.normal.textColor = Color.white;
        }
    }
}

#endif
