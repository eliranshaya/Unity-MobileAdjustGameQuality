// The whole file is compiled out without DEVELOPER_MODE so the class does not exist in a
// release build. That is also why nothing may ever reference it from a scene or a prefab:
// a serialized reference to a class that no longer exists leaves a permanent
// "The referenced script on this Behaviour is missing!" warning on the GameObject.
// MobileQualityManager.Start() adds it with AddComponent inside the same #if, and only when
// OverlaySettings.show is on.
#if DEVELOPER_MODE

using System.Collections.Generic;
using UnityEngine;

namespace MobileAdjustGameQuality
{
    /// <summary>
    /// On-screen IMGUI readout of everything the quality system is doing, plus live tier
    /// switching. Developer builds only, and only when <see cref="OverlaySettings.show"/>
    /// is on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Input goes through IMGUI's own event queue rather than <c>UnityEngine.Input</c> or the
    /// Input System package, because the overlay has to work in projects configured for
    /// either backend - and a project set to "Input System Package (New)" throws from the
    /// legacy <c>Input</c> class at runtime.
    /// </para>
    /// <para>
    /// The panel measures its own content and then anchors itself, rather than sitting in a
    /// fixed rect. That is what lets the anchor be a dropdown: a box pinned to the bottom
    /// right has to know how tall it is before it knows where its top edge goes.
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

        // Rebuilt once per frame on the Layout event and reused for Repaint. IMGUI runs
        // OnGUI at least twice a frame, and these lines are interpolated strings.
        readonly List<string> m_Lines = new List<string>(16);

        bool m_Visible = true;
        int m_TapCount;
        float m_FirstTapTime;

        /// <summary>
        /// Whatever the overlay currently occupies - the panel when open, the open button
        /// when closed. Used to keep the corner-tap gesture from stealing clicks from it.
        /// </summary>
        Rect m_OccupiedRect;

        GUIStyle m_Panel;
        GUIStyle m_Text;
        GUIStyle m_Title;
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

            DrawPanel(settings);
        }

        void DrawPanel(OverlaySettings settings)
        {
            GUILayout.BeginArea(m_OccupiedRect, m_Panel);

            float rowHeight = m_Text.fontSize * 1.8f;

            GUILayout.BeginHorizontal();
            GUILayout.Label(k_Title, m_Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(k_CloseLabel, m_Button, GUILayout.Width(rowHeight), GUILayout.Height(rowHeight)))
                m_Visible = false;
            GUILayout.EndHorizontal();

            for (int i = 0; i < m_Lines.Count; i++)
                GUILayout.Label(m_Lines[i], m_Text);

            if (settings.showTierButtons)
            {
                float buttonHeight = m_Text.fontSize * 2.2f;

                GUILayout.Space(m_Text.fontSize * 0.4f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Low", m_Button, GUILayout.Height(buttonHeight)))
                    m_Manager.SetTier(QualityTier.Low);
                if (GUILayout.Button("Balanced", m_Button, GUILayout.Height(buttonHeight)))
                    m_Manager.SetTier(QualityTier.Balanced);
                if (GUILayout.Button("High", m_Button, GUILayout.Height(buttonHeight)))
                    m_Manager.SetTier(QualityTier.High);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        void BuildLines(OverlaySettings settings)
        {
            m_Lines.Clear();

            float budget = m_Manager.FrameBudgetMs;

            if (settings.Has(OverlayFields.Tier))
                m_Lines.Add($"Tier          {m_Manager.CurrentTier}  (level {m_Manager.Current.unityQualityLevel})");

            if (settings.Has(OverlayFields.TargetFrameRate))
                m_Lines.Add($"Target        {m_Manager.Current.targetFrameRate} fps");

            if (settings.Has(OverlayFields.FrameBudget))
                m_Lines.Add($"Budget        {budget:F2} ms");

            if (settings.Has(OverlayFields.CurrentFps))
                m_Lines.Add($"Now           {m_Manager.CurrentFps:F1} fps");

            if (settings.Has(OverlayFields.AverageMs))
                m_Lines.Add($"Avg           {m_Manager.LastAvgMs:F2} ms");

            if (settings.Has(OverlayFields.P95Ms))
                m_Lines.Add($"p95           {m_Manager.LastP95Ms:F2} ms   {(m_Manager.LastP95Ms > budget ? "OVER" : "ok")}");

            if (settings.Has(OverlayFields.RenderScale))
                m_Lines.Add($"Render scale  {m_Manager.CurrentRenderScale:F2}" +
                            $"  ({m_Manager.Current.minRenderScale:F2} - {m_Manager.Current.maxRenderScale:F2})");

            if (settings.Has(OverlayFields.Thermal))
            {
                string headroom = float.IsNaN(m_Manager.ThermalHeadroom)
                    ? "n/a"
                    : m_Manager.ThermalHeadroom.ToString("F2");
                m_Lines.Add($"Thermal       {m_Manager.ThermalLevel}   headroom {headroom}");
            }

            if (settings.Has(OverlayFields.HeatResponse))
            {
                string heat = m_Manager.IsThermalThrottled ? "SEVERE - pinned"
                    : m_Manager.IsSheddingHeat ? "WARM - shedding"
                    : m_Manager.IsDeviceWarm ? "WARM - holding"
                    : "cool";
                m_Lines.Add($"Heat          {heat}");
            }

            if (settings.Has(OverlayFields.OscillationLock))
            {
                string state = m_Manager.IsRenderScaleLockedForSession ? "locked for session"
                    : m_Manager.IsRenderScaleLocked ? "locked (temporary)"
                    : "free";
                m_Lines.Add($"Step up       {state}");
            }

            if (settings.Has(OverlayFields.Adjustments))
                m_Lines.Add($"Adjustments   {(m_Manager.AllowAdjustments ? "allowed" : "blocked by gameplay")}");

            if (settings.Has(OverlayFields.Device))
            {
                m_Lines.Add($"GPU           {SystemInfo.graphicsDeviceName}");
                m_Lines.Add($"API           {SystemInfo.graphicsDeviceType}  sm{SystemInfo.graphicsShaderLevel}");
            }
        }

        /// <summary>
        /// Measures the built lines and returns the panel rect for the chosen anchor.
        /// </summary>
        /// <param name="settings">Current overlay settings.</param>
        /// <returns>The rect to draw the panel in.</returns>
        Rect MeasurePanel(OverlaySettings settings)
        {
            var content = new GUIContent();
            float width = 0f;

            for (int i = 0; i < m_Lines.Count; i++)
            {
                content.text = m_Lines[i];
                float lineWidth = m_Text.CalcSize(content).x;
                if (lineWidth > width)
                    width = lineWidth;
            }

            float rowHeight = m_Text.fontSize * 1.8f;

            // Header: title, a gap, then the square close button.
            content.text = k_Title;
            float headerWidth = m_Title.CalcSize(content).x + rowHeight * 1.5f;
            if (headerWidth > width)
                width = headerWidth;

            float lineHeight = m_Text.lineHeight > 0f ? m_Text.lineHeight : m_Text.fontSize * 1.2f;
            float height = rowHeight + m_Lines.Count * lineHeight;

            if (settings.showTierButtons)
            {
                height += m_Text.fontSize * 2.2f + m_Text.fontSize * 0.4f;
                float buttonRowWidth = m_Text.fontSize * 14f;
                if (buttonRowWidth > width)
                    width = buttonRowWidth;
            }

            // A couple of pixels of slack. The arithmetic above matches GUILayout exactly
            // now that the margins are zeroed, but rounding on a fractional line height
            // should not be the difference between a visible row and a clipped one.
            height += 4f;

            return AnchoredRect(settings, WithPadding(width), WithPadding(height, vertical: true));
        }

        Rect MeasureOpenButton(OverlaySettings settings)
        {
            var content = new GUIContent(k_OpenLabel);
            Vector2 size = m_Button.CalcSize(content);

            float width = Mathf.Max(size.x, m_Text.fontSize * 3.5f);
            float height = Mathf.Max(size.y, m_Text.fontSize * 1.9f);

            return AnchoredRect(settings, width, height);
        }

        float WithPadding(float value, bool vertical = false)
        {
            RectOffset padding = m_Panel.padding;
            return value + (vertical ? padding.top + padding.bottom : padding.left + padding.right);
        }

        Rect AnchoredRect(OverlaySettings settings, float width, float height)
        {
            // Never let the panel run off a phone screen, whatever the font scale is.
            width = Mathf.Min(width, Screen.width - settings.margin * 2f);
            height = Mathf.Min(height, Screen.height - settings.margin * 2f);

            return new Rect(AnchorPosition(settings, width, height), new Vector2(width, height));
        }

        static Vector2 AnchorPosition(OverlaySettings settings, float width, float height)
        {
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

            return new Vector2(x, y);
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

            // Four taps in the top-right corner, kept as a way back in on a device where the
            // open button has been turned off. The hit area is an invisible IMGUI region
            // rather than a raw touch read so it works with either input backend and does not
            // fight the game's own UI.
            if (e.type != EventType.MouseDown)
                return;

            // Skip when the overlay itself is under the tap, or anchoring to the top right
            // would make its own buttons unclickable.
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
            // Sized from the render target, not from Screen.dpi.
            //
            // dpi is the obvious choice and it is wrong here: in the editor Game view it
            // reports the *monitor's* DPI, typically 96, which divided down to a 10 pixel
            // font and a panel the size of a postage stamp no matter how large the Game view
            // was. The larger screen dimension tracks the surface the overlay is actually
            // drawn into, so one rule covers a 2400px phone in either orientation and a
            // small Game view alike.
            float reference = Mathf.Max(Screen.width, Screen.height);
            int fontSize = Mathf.Max(14, Mathf.RoundToInt(reference / 55f * settings.scale));

            if (m_Text == null)
            {
                m_Text = new GUIStyle(GUI.skin.label)
                {
                    richText = false,
                    wordWrap = false,

                    // Margins and padding are zeroed so a label occupies exactly lineHeight
                    // and nothing else. GUILayout inserts each style's margin between
                    // elements, and the skin's label carries a few pixels of it - which the
                    // measuring code below cannot see. Thirteen invisible margins added up
                    // to more than the height of the tier button row, so the row was laid
                    // out past the bottom of the panel and clipped away.
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0)
                };
                m_Text.normal.textColor = Color.white;

                m_Title = new GUIStyle(m_Text)
                {
                    fontStyle = FontStyle.Bold
                };

                // Buttons need the computed font size too. Leaving them on the skin default
                // is what made them render as unreadable specks next to correctly sized
                // text. Horizontal margin stays, so the three tier buttons do not touch.
                m_Button = new GUIStyle(GUI.skin.button)
                {
                    margin = new RectOffset(2, 2, 0, 0)
                };
            }

            m_Text.fontSize = fontSize;
            m_Title.fontSize = fontSize;
            m_Button.fontSize = fontSize;

            if (m_Panel == null)
                m_Panel = new GUIStyle(GUI.skin.box);

            // Padding tracks the font, or a 3x scale would put huge text in a box with a
            // hairline border around it.
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
