using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// The one window you are expected to open. Generates the URP asset, the renderer, the
    /// quality level, the Player Settings and the prefab in a single press.
    /// </summary>
    public class QualitySetupWindow : EditorWindow
    {
        bool m_CreateAssets = true;
        bool m_CreateQualityLevel = true;
        bool m_ApplyPlayerSettings = true;
        bool m_CreatePrefab = true;
        bool m_EnableDeveloperMode = true;
        bool m_PlayerSettingsFoldout;

        readonly PlayerSettingsConfigurator.Options m_PlayerOptions = new PlayerSettingsConfigurator.Options();

        // Refreshed on focus rather than every repaint, so an open window does not hit the
        // AssetDatabase sixty times a second.
        bool m_UrpInstalled;
        string m_UrpVersion;
        bool m_UrpActive;
        bool m_AssetsPresent;
        bool m_QualityLevelPresent;
        bool m_PrefabPresent;

        readonly List<string> m_DoomedLevels = new List<string>();

        SetupReport m_Report;
        IReadOnlyList<string> m_Skipped;
        Vector2 m_Scroll;

        /// <summary>Opens the Setup window.</summary>
        [MenuItem("Tools/Mobile Quality/Setup", false, 100)]
        public static void Open()
        {
            var window = GetWindow<QualitySetupWindow>(false, "Mobile Quality", true);
            window.minSize = new Vector2(500f, 600f);
            window.RefreshStatus();
            window.Show();
        }

        void OnEnable() => RefreshStatus();

        void OnFocus() => RefreshStatus();

        void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            DrawStatus();
            EditorGUILayout.Space(8f);
            DrawOptions();
            EditorGUILayout.Space(8f);
            DrawButtons();
            EditorGUILayout.Space(8f);
            DrawReport();

            EditorGUILayout.EndScrollView();
        }

        void DrawStatus()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawCheck(m_UrpInstalled, m_UrpInstalled ? $"URP installed ({m_UrpVersion})" : "URP not installed");
                DrawCheck(m_UrpActive, "URP is the active render pipeline");
                DrawCheck(m_AssetsPresent, "Generated URP asset and renderer present");
                DrawCheck(m_QualityLevelPresent, $"'{QualityPreset.QualityLevelName}' quality level present");
                DrawCheck(m_PrefabPresent, "MobileQualityManager prefab present");
                DrawCheck(DeveloperDefineToggle.IsEnabled, $"{DeveloperDefineToggle.Define} on");
            }

            if (!m_UrpInstalled)
            {
                EditorGUILayout.HelpBox(
                    "Install com.unity.render-pipelines.universal before running Setup.", MessageType.Error);
            }
            else if (!m_UrpActive)
            {
                EditorGUILayout.HelpBox(
                    "URP is installed but is not the active pipeline. Assign a Universal Render Pipeline " +
                    "Asset in Project Settings > Graphics, otherwise the generated assets will not be used.",
                    MessageType.Warning);
            }

        }

        static void DrawCheck(bool ok, string label)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Color previous = GUI.color;
                GUI.color = ok ? new Color(0.35f, 0.8f, 0.35f) : new Color(0.85f, 0.5f, 0.35f);
                GUILayout.Label(ok ? "✔" : "✘", GUILayout.Width(16f));
                GUI.color = previous;
                GUILayout.Label(label);
            }
        }

        void DrawOptions()
        {
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                m_CreateAssets = EditorGUILayout.ToggleLeft(
                    "Create the URP asset and renderer", m_CreateAssets);
                m_CreateQualityLevel = EditorGUILayout.ToggleLeft(
                    $"Replace all quality levels with '{QualityPreset.QualityLevelName}'", m_CreateQualityLevel);

                if (m_CreateQualityLevel && m_DoomedLevels.Count > 0)
                {
                    EditorGUILayout.HelpBox(
                        $"Setup will delete {m_DoomedLevels.Count} existing quality level" +
                        $"{(m_DoomedLevels.Count == 1 ? "" : "s")}:\n  " +
                        string.Join("\n  ", m_DoomedLevels) +
                        "\n\nThis cannot be undone. Anything selecting a quality level by index will " +
                        "resolve to MAQ Mobile afterwards.",
                        MessageType.Warning);
                }

                m_ApplyPlayerSettings = EditorGUILayout.ToggleLeft(
                    "Apply recommended Player Settings", m_ApplyPlayerSettings);

                using (new EditorGUI.DisabledScope(!m_ApplyPlayerSettings))
                {
                    EditorGUI.indentLevel++;
                    m_PlayerSettingsFoldout = EditorGUILayout.Foldout(m_PlayerSettingsFoldout, "Detail", true);
                    if (m_PlayerSettingsFoldout)
                    {
                        EditorGUI.indentLevel++;
                        m_PlayerOptions.optimizedFramePacing = EditorGUILayout.ToggleLeft(
                            "Android: Optimized Frame Pacing", m_PlayerOptions.optimizedFramePacing);
                        m_PlayerOptions.linearColorSpace = EditorGUILayout.ToggleLeft(
                            "Color Space: Linear (reimports shaders)", m_PlayerOptions.linearColorSpace);
                        m_PlayerOptions.graphicsApis = EditorGUILayout.ToggleLeft(
                            "Android: Vulkan then GLES3", m_PlayerOptions.graphicsApis);
                        m_PlayerOptions.multithreadedRendering = EditorGUILayout.ToggleLeft(
                            "Android: Multithreaded Rendering", m_PlayerOptions.multithreadedRendering);
                        m_PlayerOptions.il2cpp = EditorGUILayout.ToggleLeft(
                            "Android: IL2CPP", m_PlayerOptions.il2cpp);
                        m_PlayerOptions.arm64 = EditorGUILayout.ToggleLeft(
                            "Android: ARM64", m_PlayerOptions.arm64);
                        m_PlayerOptions.astcTextures = EditorGUILayout.ToggleLeft(
                            "Android: ASTC textures", m_PlayerOptions.astcTextures);
                        m_PlayerOptions.batching = EditorGUILayout.ToggleLeft(
                            "Android: Static + Dynamic Batching", m_PlayerOptions.batching);
                        m_PlayerOptions.frameTimingStats = EditorGUILayout.ToggleLeft(
                            "Frame Timing Stats (CPU/GPU timings)", m_PlayerOptions.frameTimingStats);
                        EditorGUI.indentLevel--;
                    }

                    EditorGUI.indentLevel--;
                }

                m_CreatePrefab = EditorGUILayout.ToggleLeft("Create the manager prefab", m_CreatePrefab);
                m_EnableDeveloperMode = EditorGUILayout.ToggleLeft(
                    $"Enable {DeveloperDefineToggle.Define} now", m_EnableDeveloperMode);
            }
        }

        void DrawButtons()
        {
            bool anything = m_AssetsPresent || m_QualityLevelPresent || m_PrefabPresent;

            using (new EditorGUI.DisabledScope(!m_UrpInstalled))
            {
                if (GUILayout.Button(anything ? "Update Existing" : "Setup", GUILayout.Height(42f)))
                    RunSetup();
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Prefab Only", GUILayout.Height(26f)))
                    RunPrefabOnly();

                using (new EditorGUI.DisabledScope(!anything))
                {
                    if (GUILayout.Button("Select Generated Assets", GUILayout.Height(26f)))
                        SelectGenerated();
                }
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUI.DisabledScope(!anything))
            {
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                if (GUILayout.Button("Remove Everything", GUILayout.Height(26f)))
                    RunRemove();
                GUI.backgroundColor = previous;
            }
        }

        void DrawReport()
        {
            if (m_Report == null)
                return;

            EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Unity {Application.unityVersion}   URP {m_UrpVersion ?? "unknown"}");

                DrawSection("Created", m_Report.Created);
                DrawSection("Updated", m_Report.Updated);
                DrawSection("Notes", m_Report.Notes);

                if (m_Skipped != null && m_Skipped.Count > 0)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(
                        "Skipped on this URP/Unity version - these fields do not exist here and kept their " +
                        "defaults:\n  " + string.Join("\n  ", m_Skipped), MessageType.Warning);
                }

                if (m_Report.HasProblems)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(string.Join("\n\n", m_Report.Problems), MessageType.Error);
                }
            }
        }

        static void DrawSection(string title, IReadOnlyList<string> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            foreach (string entry in entries)
                EditorGUILayout.LabelField("    " + entry, EditorStyles.wordWrappedMiniLabel);
        }

        void RunSetup()
        {
            var report = new SetupReport();
            SerializedFieldHelper.BeginRun();

            if (!QualityPreset.IsUrpInstalled)
            {
                report.Problem("URP is not installed. Nothing was changed.");
                Finish(report);
                return;
            }

            if (!IsUrpActive())
            {
                report.Note("URP is not the active pipeline in Graphics Settings. The assets were generated, " +
                            "but nothing will use them until a URP asset is assigned there.");
            }

            UniversalRenderPipelineAsset urpAsset = null;
            int levelIndex = -1;

            try
            {
                EditorUtility.DisplayProgressBar("Mobile Quality", "Preparing", 0f);

                if (!QualityPreset.EnsureGeneratedFolder())
                {
                    report.Problem($"Could not create {QualityPreset.GeneratedFolder}.");
                    Finish(report);
                    return;
                }

                if (m_CreateAssets)
                {
                    // Batched only around asset creation. The prefab save and the
                    // ProjectSettings edits gain nothing from it, and saving a prefab inside
                    // a batch is a known source of half-written assets.
                    try
                    {
                        AssetDatabase.StartAssetEditing();

                        EditorUtility.DisplayProgressBar("Mobile Quality", "Creating renderer", 0.2f);
                        UniversalRendererData renderer = AssetFactory.CreateOrUpdateRenderer(report);

                        EditorUtility.DisplayProgressBar("Mobile Quality", "Creating URP asset", 0.35f);
                        urpAsset = AssetFactory.CreateOrUpdateUrpAsset(renderer, report);
                    }
                    finally
                    {
                        AssetDatabase.StopAssetEditing();
                    }

                    EditorUtility.DisplayProgressBar("Mobile Quality", "Saving assets", 0.5f);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
                else
                {
                    urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QualityPreset.UrpAssetPath);
                }

                if (m_CreateQualityLevel)
                {
                    EditorUtility.DisplayProgressBar("Mobile Quality", "Writing the quality level", 0.65f);
                    levelIndex = QualityLevelWriter.Write(urpAsset, report);
                }
                else
                {
                    levelIndex = QualityLevelWriter.FindExistingIndex();
                }

                if (m_ApplyPlayerSettings)
                {
                    EditorUtility.DisplayProgressBar("Mobile Quality", "Applying Player Settings", 0.8f);
                    PlayerSettingsConfigurator.Apply(m_PlayerOptions, report);
                }

                if (m_CreatePrefab)
                {
                    EditorUtility.DisplayProgressBar("Mobile Quality", "Building the prefab", 0.92f);
                    GameObject prefab = QualityPrefabBuilder.Build(levelIndex, report);
                    if (prefab != null)
                    {
                        EditorGUIUtility.PingObject(prefab);
                        Selection.activeObject = prefab;
                    }
                }

                if (levelIndex < 0)
                {
                    report.Note("The quality level index was not available, so the prefab kept its default. " +
                                "Run Setup with the quality level option enabled to fix that.");
                }

                if (m_EnableDeveloperMode && !DeveloperDefineToggle.IsEnabled)
                {
                    // Last: this triggers a domain reload, and everything above has to be on
                    // disk before the scripts reload.
                    DeveloperDefineToggle.SetEnabled(true);
                    report.Update($"Scripting define {DeveloperDefineToggle.Define} -> on (Android, iOS, Standalone)");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }

            Finish(report);
        }

        void RunPrefabOnly()
        {
            var report = new SetupReport();
            SerializedFieldHelper.BeginRun();

            int index = QualityLevelWriter.FindExistingIndex();
            if (index < 0)
            {
                report.Note($"The '{QualityPreset.QualityLevelName}' quality level does not exist yet, so the " +
                            "prefab was built with the component's default index. Run the full Setup.");
            }

            GameObject prefab = QualityPrefabBuilder.Build(index, report);
            if (prefab != null)
            {
                EditorGUIUtility.PingObject(prefab);
                Selection.activeObject = prefab;
            }

            AssetDatabase.SaveAssets();
            Finish(report);
        }

        void RunRemove()
        {
            var paths = new List<string>();
            foreach (string path in QualityPreset.GeneratedPaths)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    paths.Add(path);
            }

            int levelIndex = QualityLevelWriter.FindExistingIndex();

            string message =
                "This will delete:\n\n" +
                (paths.Count > 0 ? "  " + string.Join("\n  ", paths) : "  (no generated assets)") +
                "\n\nand remove the quality level:\n\n" +
                (levelIndex >= 0 ? $"  {QualityPreset.QualityLevelName} (index {levelIndex})" : "  (none)") +
                "\n\nNothing else is touched: your own quality levels, Player Settings and Graphics Settings " +
                "stay exactly as they are.\n\n" +
                "If the generated URP asset is currently assigned in Graphics Settings, assign a different " +
                "one first or the project will have no active pipeline.";

            if (!EditorUtility.DisplayDialog("Remove Mobile Quality setup?", message, "Delete", "Cancel"))
                return;

            var report = new SetupReport();
            SerializedFieldHelper.BeginRun();

            foreach (string path in paths)
            {
                if (AssetDatabase.DeleteAsset(path))
                    report.Update($"Deleted {path}");
                else
                    report.Problem($"Could not delete {path}.");
            }

            QualityLevelWriter.Remove(report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Finish(report);
        }

        void SelectGenerated()
        {
            var objects = new List<Object>();
            foreach (string path in QualityPreset.GeneratedPaths)
            {
                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null)
                    objects.Add(asset);
            }

            if (objects.Count == 0)
                return;

            Selection.objects = objects.ToArray();
            EditorGUIUtility.PingObject(objects[objects.Count - 1]);
        }

        void Finish(SetupReport report)
        {
            m_Report = report;
            m_Skipped = SerializedFieldHelper.Skipped;
            RefreshStatus();
            Repaint();
        }

        void RefreshStatus()
        {
            m_UrpInstalled = QualityPreset.IsUrpInstalled;
            m_UrpVersion = QualityPreset.UrpVersion;
            m_UrpActive = IsUrpActive();

            m_AssetsPresent =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QualityPreset.UrpAssetPath) != null &&
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(QualityPreset.RendererPath) != null;

            m_QualityLevelPresent = QualityLevelWriter.FindExistingIndex() >= 0;

            m_DoomedLevels.Clear();
            foreach (string name in QualityLevelWriter.ListExistingNames())
            {
                if (name != QualityPreset.QualityLevelName)
                    m_DoomedLevels.Add(name);
            }
            m_PrefabPresent = AssetDatabase.LoadAssetAtPath<GameObject>(QualityPreset.PrefabPath) != null;
        }

        static bool IsUrpActive() =>
            QualitySettings.renderPipeline is UniversalRenderPipelineAsset ||
            GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset;
    }
}
