using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MobileAdjustGameQuality.EditorTools
{
    /// <summary>
    /// The one window the user is expected to open: it generates the URP assets,
    /// renderers, quality levels, player settings and prefab in a single press.
    /// </summary>
    public class QualitySetupWindow : EditorWindow
    {
        // Options
        bool m_CreateUrpAssets = true;
        bool m_CreateQualityLevels = true;
        bool m_ReplaceQualityLevels = true;
        bool m_ApplyPlayerSettings = true;
        bool m_CreatePrefab = true;
        bool m_EnableDeveloperMode = true;
        bool m_PlayerSettingsFoldout;
        readonly PlayerSettingsConfigurator.Options m_PlayerOptions = new PlayerSettingsConfigurator.Options();

        // Status, refreshed on focus rather than every repaint so the window does not hit
        // the AssetDatabase sixty times a second while it is just sitting open.
        bool m_UrpInstalled;
        string m_UrpVersion;
        bool m_UrpActive;
        bool m_GeneratedAssetsPresent;
        bool m_QualityLevelsPresent;
        bool m_PrefabPresent;

        SetupReport m_Report;
        IReadOnlyList<string> m_SkippedFields;
        Vector2 m_Scroll;

        /// <summary>Opens the Setup window.</summary>
        [MenuItem("Tools/Mobile Quality/Setup", false, 100)]
        public static void Open()
        {
            var window = GetWindow<QualitySetupWindow>(false, "Mobile Quality", true);
            window.minSize = new Vector2(520f, 640f);
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
                DrawCheck(m_UrpInstalled,
                    m_UrpInstalled ? $"URP package installed (version {m_UrpVersion})" : "URP package not installed");
                DrawCheck(m_UrpActive, "URP is the active render pipeline in Graphics Settings");
                DrawCheck(m_GeneratedAssetsPresent, "Generated URP assets and renderers present");
                DrawCheck(m_QualityLevelsPresent, "MAQ Low / Balanced / High quality levels present");
                DrawCheck(m_PrefabPresent, "MobileQualityManager prefab present");
            }

            if (!m_UrpInstalled)
            {
                EditorGUILayout.HelpBox(
                    "Install com.unity.render-pipelines.universal through the Package Manager before running Setup.",
                    MessageType.Error);
            }
            else if (!m_UrpActive)
            {
                EditorGUILayout.HelpBox(
                    "URP is installed but is not the active pipeline. Assign a Universal Render Pipeline Asset in " +
                    "Project Settings > Graphics before running Setup, otherwise the generated assets will not be used.",
                    MessageType.Warning);
            }
        }

        static void DrawCheck(bool ok, string label)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Color previous = GUI.color;
                GUI.color = ok ? new Color(0.35f, 0.8f, 0.35f) : new Color(0.9f, 0.4f, 0.4f);
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
                m_CreateUrpAssets = EditorGUILayout.ToggleLeft(
                    "Create URP assets and renderers", m_CreateUrpAssets);
                m_CreateQualityLevels = EditorGUILayout.ToggleLeft(
                    m_ReplaceQualityLevels
                        ? "Write three quality levels"
                        : "Add three quality levels (appended, existing untouched)",
                    m_CreateQualityLevels);

                using (new EditorGUI.DisabledScope(!m_CreateQualityLevels))
                {
                    EditorGUI.indentLevel++;
                    m_ReplaceQualityLevels = EditorGUILayout.ToggleLeft(
                        "Replace all quality levels — MAQ becomes 0 / 1 / 2",
                        m_ReplaceQualityLevels);
                    EditorGUI.indentLevel--;
                }

                if (m_CreateQualityLevels && m_ReplaceQualityLevels)
                {
                    EditorGUILayout.HelpBox(
                        "Destructive. Every existing quality level is deleted and replaced by MAQ Low, " +
                        "Balanced and High at indices 0, 1 and 2. You will be asked to confirm, and the " +
                        "levels that would go are named in the dialog.\n\n" +
                        "Turn this off to append the three levels after your own instead.",
                        MessageType.Warning);
                }

                m_ApplyPlayerSettings = EditorGUILayout.ToggleLeft(
                    "Apply recommended Player Settings", m_ApplyPlayerSettings);

                using (new EditorGUI.DisabledScope(!m_ApplyPlayerSettings))
                {
                    EditorGUI.indentLevel++;
                    m_PlayerSettingsFoldout = EditorGUILayout.Foldout(m_PlayerSettingsFoldout, "Player Settings detail", true);
                    if (m_PlayerSettingsFoldout)
                    {
                        EditorGUI.indentLevel++;
                        m_PlayerOptions.optimizedFramePacing = EditorGUILayout.ToggleLeft(
                            "Android: Optimized Frame Pacing", m_PlayerOptions.optimizedFramePacing);
                        m_PlayerOptions.linearColorSpace = EditorGUILayout.ToggleLeft(
                            "Color Space: Linear (reimports shaders)", m_PlayerOptions.linearColorSpace);
                        m_PlayerOptions.graphicsApis = EditorGUILayout.ToggleLeft(
                            "Android: Graphics APIs Vulkan then GLES3", m_PlayerOptions.graphicsApis);
                        m_PlayerOptions.multithreadedRendering = EditorGUILayout.ToggleLeft(
                            "Android: Multithreaded Rendering", m_PlayerOptions.multithreadedRendering);
                        m_PlayerOptions.il2cpp = EditorGUILayout.ToggleLeft(
                            "Android: Scripting Backend IL2CPP", m_PlayerOptions.il2cpp);
                        m_PlayerOptions.arm64 = EditorGUILayout.ToggleLeft(
                            "Android: Target Architecture ARM64", m_PlayerOptions.arm64);
                        m_PlayerOptions.astcTextures = EditorGUILayout.ToggleLeft(
                            "Android: Texture Compression ASTC", m_PlayerOptions.astcTextures);
                        m_PlayerOptions.batching = EditorGUILayout.ToggleLeft(
                            "Android: Static + Dynamic Batching", m_PlayerOptions.batching);
                        EditorGUI.indentLevel--;
                    }

                    EditorGUI.indentLevel--;
                }

                m_CreatePrefab = EditorGUILayout.ToggleLeft(
                    "Create the manager prefab", m_CreatePrefab);
                m_EnableDeveloperMode = EditorGUILayout.ToggleLeft(
                    $"Enable {DeveloperDefineToggle.Define} now", m_EnableDeveloperMode);
            }
        }

        void DrawButtons()
        {
            bool anythingExists = m_GeneratedAssetsPresent || m_QualityLevelsPresent || m_PrefabPresent;

            using (new EditorGUI.DisabledScope(!m_UrpInstalled))
            {
                if (GUILayout.Button(anythingExists ? "Update Existing" : "Setup", GUILayout.Height(42f)))
                    RunSetup();
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Prefab Only", GUILayout.Height(26f)))
                    RunPrefabOnly();

                using (new EditorGUI.DisabledScope(!m_GeneratedAssetsPresent && !m_PrefabPresent))
                {
                    if (GUILayout.Button("Select Generated Assets", GUILayout.Height(26f)))
                        SelectGeneratedAssets();
                }
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUI.DisabledScope(!anythingExists))
            {
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                if (GUILayout.Button("Remove Everything", GUILayout.Height(26f)))
                    RunRemoveEverything();
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

                DrawReportSection("Created", m_Report.Created);
                DrawReportSection("Updated", m_Report.Updated);
                DrawReportSection("Notes", m_Report.Notes);

                if (m_SkippedFields != null && m_SkippedFields.Count > 0)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(
                        "Skipped on this URP/Unity version - these fields do not exist here and kept their defaults:\n  " +
                        string.Join("\n  ", m_SkippedFields),
                        MessageType.Warning);
                }

                if (m_Report.HasProblems)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(string.Join("\n\n", m_Report.Problems), MessageType.Error);
                }
            }
        }

        static void DrawReportSection(string title, IReadOnlyList<string> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            foreach (string entry in entries)
                EditorGUILayout.LabelField("    " + entry, EditorStyles.wordWrappedMiniLabel);
        }

        void RunSetup()
        {
            // Asked before anything at all is written, so cancelling leaves the project
            // byte-for-byte as it was rather than half-generated.
            if (m_CreateQualityLevels && m_ReplaceQualityLevels && !ConfirmQualityLevelReplacement())
                return;

            var report = new SetupReport();
            SerializedFieldHelper.BeginRun();

            // Step 1: validate before anything is written.
            if (!QualityTierPresets.IsUrpInstalled)
            {
                report.Problem("URP is not installed. Nothing was changed.");
                Finish(report);
                return;
            }

            if (!IsUrpActive())
            {
                report.Note("URP is not the active pipeline in Graphics Settings. The assets were still " +
                            "generated, but nothing will use them until a URP asset is assigned there.");
            }

            var urpAssets = new UniversalRenderPipelineAsset[3];
            int[] levelIndices = null;

            try
            {
                EditorUtility.DisplayProgressBar("Mobile Quality", "Preparing", 0f);

                // Step 2
                if (!QualityTierPresets.EnsureGeneratedFolder())
                {
                    report.Problem($"Could not create {QualityTierPresets.GeneratedFolder}. Nothing was generated.");
                    Finish(report);
                    return;
                }

                if (m_CreateUrpAssets)
                {
                    // Batched only around asset creation. Wrapping the prefab save and the
                    // ProjectSettings edits in the same block gains nothing - neither goes
                    // through the importer - and prefab saving inside a batch is a known
                    // source of half-written assets.
                    try
                    {
                        AssetDatabase.StartAssetEditing();

                        // Steps 3 and 4: renderers first, because the URP asset needs one
                        // to point at when it is created.
                        for (int tier = 0; tier < 3; tier++)
                        {
                            EditorUtility.DisplayProgressBar("Mobile Quality",
                                $"Creating {QualityTierPresets.TierNames[tier]} renderer", 0.1f + tier * 0.1f);

                            UniversalRendererData renderer = RendererDataFactory.CreateOrUpdate(tier, report);

                            EditorUtility.DisplayProgressBar("Mobile Quality",
                                $"Creating {QualityTierPresets.TierNames[tier]} URP asset", 0.15f + tier * 0.1f);

                            urpAssets[tier] = UrpAssetFactory.CreateOrUpdate(tier, renderer, report);
                        }
                    }
                    finally
                    {
                        AssetDatabase.StopAssetEditing();
                    }

                    // Step 5
                    EditorUtility.DisplayProgressBar("Mobile Quality", "Saving assets", 0.5f);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
                else
                {
                    for (int tier = 0; tier < 3; tier++)
                        urpAssets[tier] = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                            QualityTierPresets.UrpAssetPath(tier));
                }

                // Step 6
                if (m_CreateQualityLevels)
                {
                    EditorUtility.DisplayProgressBar("Mobile Quality", "Writing quality levels", 0.6f);
                    levelIndices = m_ReplaceQualityLevels
                        ? QualityLevelWriter.ReplaceAll(urpAssets, report)
                        : QualityLevelWriter.AppendOrUpdate(urpAssets, report);
                }
                else
                {
                    levelIndices = QualityLevelWriter.FindExistingIndices();
                }

                // Step 7
                if (m_ApplyPlayerSettings)
                {
                    EditorUtility.DisplayProgressBar("Mobile Quality", "Applying Player Settings", 0.75f);
                    PlayerSettingsConfigurator.Apply(m_PlayerOptions, report);
                }

                // Step 8
                if (m_CreatePrefab)
                {
                    EditorUtility.DisplayProgressBar("Mobile Quality", "Building prefab", 0.9f);
                    GameObject prefab = QualityPrefabBuilder.Build(levelIndices, report);
                    if (prefab != null)
                    {
                        // Ping it so the user can see where the one thing they still have
                        // to do by hand actually landed.
                        EditorGUIUtility.PingObject(prefab);
                        Selection.activeObject = prefab;
                    }
                }

                if (levelIndices == null || System.Array.IndexOf(levelIndices, -1) >= 0)
                {
                    report.Note("Quality level indices were not available, so the prefab kept its default " +
                                "indices. Run Setup with 'Add three quality levels' enabled to fix that.");
                }

                if (m_EnableDeveloperMode && !DeveloperDefineToggle.IsEnabled)
                {
                    // Last, because it triggers a domain reload and everything above must
                    // already be written to disk when the scripts reload.
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

        static bool ConfirmQualityLevelReplacement()
        {
            List<string> existing = QualityLevelWriter.ListExistingLevelNames();

            // Only the user's own levels are worth warning about. Overwriting the MAQ
            // levels this tool created on a previous run is the normal update path and
            // does not deserve a dialog.
            var doomed = new List<string>();
            for (int i = 0; i < existing.Count; i++)
            {
                if (System.Array.IndexOf(QualityTierPresets.QualityLevelNames, existing[i]) < 0)
                    doomed.Add($"{i}: {existing[i]}");
            }

            if (doomed.Count == 0)
                return true;

            string message =
                $"Setup is set to replace all quality levels, so these {doomed.Count} will be deleted:\n\n  " +
                string.Join("\n  ", doomed) +
                "\n\nThey will be replaced by MAQ Low, MAQ Balanced and MAQ High at indices 0, 1 and 2, " +
                "each pointing at its generated URP asset.\n\n" +
                "This cannot be undone. Anything referring to a deleted level by index will now resolve to " +
                "a MAQ level instead.\n\n" +
                "To keep your levels and add the three new ones after them, cancel and turn off " +
                "'Replace all quality levels'.";

            return EditorUtility.DisplayDialog("Replace all quality levels?", message, "Replace", "Cancel");
        }

        void RunPrefabOnly()
        {
            var report = new SetupReport();
            SerializedFieldHelper.BeginRun();

            int[] indices = QualityLevelWriter.FindExistingIndices();
            if (indices == null || System.Array.IndexOf(indices, -1) >= 0)
            {
                report.Note("The MAQ quality levels do not all exist yet, so the prefab was built with " +
                            "preset indices. Run the full Setup to write the real ones.");
            }

            GameObject prefab = QualityPrefabBuilder.Build(indices, report);
            if (prefab != null)
            {
                EditorGUIUtility.PingObject(prefab);
                Selection.activeObject = prefab;
            }

            AssetDatabase.SaveAssets();
            Finish(report);
        }

        void RunRemoveEverything()
        {
            var paths = new List<string>();
            for (int tier = 0; tier < 3; tier++)
            {
                AddIfExists(paths, QualityTierPresets.UrpAssetPath(tier));
                AddIfExists(paths, QualityTierPresets.RendererPath(tier));
            }

            AddIfExists(paths, QualityTierPresets.PrefabPath);

            int[] indices = QualityLevelWriter.FindExistingIndices();
            var levelNames = new List<string>();
            if (indices != null)
            {
                for (int tier = 0; tier < 3; tier++)
                {
                    if (indices[tier] >= 0)
                        levelNames.Add($"{QualityTierPresets.QualityLevelNames[tier]} (index {indices[tier]})");
                }
            }

            string message =
                "This will delete:\n\n" +
                (paths.Count > 0 ? "  " + string.Join("\n  ", paths) : "  (no generated assets)") +
                "\n\nand remove these quality levels:\n\n" +
                (levelNames.Count > 0 ? "  " + string.Join("\n  ", levelNames) : "  (none)") +
                "\n\nNothing else is touched: your own quality levels, Player Settings and Graphics Settings " +
                "stay exactly as they are.\n\n" +
                "If one of the generated URP assets is currently assigned in Graphics Settings, assign a " +
                "different one first or the project will have no active pipeline.";

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

            QualityLevelWriter.RemoveGeneratedLevels(report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Finish(report);
        }

        static void AddIfExists(List<string> paths, string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                paths.Add(path);
        }

        void SelectGeneratedAssets()
        {
            var objects = new List<Object>();
            for (int tier = 0; tier < 3; tier++)
            {
                AddObject(objects, QualityTierPresets.UrpAssetPath(tier));
                AddObject(objects, QualityTierPresets.RendererPath(tier));
            }

            AddObject(objects, QualityTierPresets.PrefabPath);

            if (objects.Count == 0)
                return;

            Selection.objects = objects.ToArray();
            EditorGUIUtility.PingObject(objects[objects.Count - 1]);
        }

        static void AddObject(List<Object> objects, string path)
        {
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null)
                objects.Add(asset);
        }

        void Finish(SetupReport report)
        {
            m_Report = report;
            m_SkippedFields = SerializedFieldHelper.Skipped;
            RefreshStatus();
            Repaint();
        }

        void RefreshStatus()
        {
            m_UrpInstalled = QualityTierPresets.IsUrpInstalled;
            m_UrpVersion = QualityTierPresets.UrpVersion;
            m_UrpActive = IsUrpActive();

            m_GeneratedAssetsPresent = true;
            for (int tier = 0; tier < 3 && m_GeneratedAssetsPresent; tier++)
            {
                m_GeneratedAssetsPresent =
                    AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QualityTierPresets.UrpAssetPath(tier)) != null &&
                    AssetDatabase.LoadAssetAtPath<UniversalRendererData>(QualityTierPresets.RendererPath(tier)) != null;
            }

            int[] indices = QualityLevelWriter.FindExistingIndices();
            m_QualityLevelsPresent = indices != null && System.Array.IndexOf(indices, -1) < 0;

            m_PrefabPresent = AssetDatabase.LoadAssetAtPath<GameObject>(QualityTierPresets.PrefabPath) != null;
        }

        static bool IsUrpActive()
        {
            return QualitySettings.renderPipeline is UniversalRenderPipelineAsset
                   || GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset;
        }
    }
}
