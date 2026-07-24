using System.IO;
using UnityEditor;
using UnityEngine;

namespace Necronomi.Rhythm.EditorTools
{
    /// <summary>
    /// Drag in a song, press Bake, get a chart asset. Everything a new song needs.
    /// </summary>
    public sealed class ChartBakerWindow : EditorWindow
    {
        private const string DefaultFolder = "Assets/Rhythm/Charts";
        private const string SettingsKey = "necronomi.rhythm.baker.settings";
        private const string FolderKey = "necronomi.rhythm.baker.folder";

        private AudioClip clip;
        private ChartBakeSettings settings;
        private SongChart overwriteTarget;
        private string outputFolder = DefaultFolder;
        private Vector2 scroll;
        private string lastReport;
        private string lastError;

        [MenuItem("Necronomi/Rhythm/Chart Baker")]
        public static void Open()
        {
            ChartBakerWindow window = GetWindow<ChartBakerWindow>("Chart Baker");
            window.minSize = new Vector2(380f, 460f);
            window.Show();
        }

        /// <summary>Right click an AudioClip in the project to bake it straight away.</summary>
        [MenuItem("Assets/Necronomi/Bake Rhythm Chart", false, 30)]
        private static void BakeSelected()
        {
            ChartBakerWindow window = GetWindow<ChartBakerWindow>("Chart Baker");
            window.clip = Selection.activeObject as AudioClip;
            window.Show();
        }

        [MenuItem("Assets/Necronomi/Bake Rhythm Chart", true)]
        private static bool BakeSelectedValidate() => Selection.activeObject is AudioClip;

        private void OnEnable()
        {
            outputFolder = EditorPrefs.GetString(FolderKey, DefaultFolder);

            string settingsPath = EditorPrefs.GetString(SettingsKey, string.Empty);
            if (!string.IsNullOrEmpty(settingsPath))
                settings = AssetDatabase.LoadAssetAtPath<ChartBakeSettings>(settingsPath);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            clip = (AudioClip)EditorGUILayout.ObjectField("Audio Clip", clip, typeof(AudioClip), false);

            DrawImporterWarning();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            settings = (ChartBakeSettings)EditorGUILayout.ObjectField("Bake Settings", settings,
                typeof(ChartBakeSettings), false);

            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(SettingsKey,
                    settings != null ? AssetDatabase.GetAssetPath(settings) : string.Empty);
            }

            if (settings == null)
            {
                EditorGUILayout.HelpBox("No settings asset. Defaults will be used.", MessageType.Info);

                if (GUILayout.Button("Create Settings Asset"))
                    CreateSettingsAsset();
            }
            else
            {
                DrawSettingsSummary();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            overwriteTarget = (SongChart)EditorGUILayout.ObjectField(
                new GUIContent("Overwrite Chart", "Leave empty to create a new asset"),
                overwriteTarget, typeof(SongChart), false);

            using (new EditorGUI.DisabledScope(overwriteTarget != null))
            {
                EditorGUI.BeginChangeCheck();
                outputFolder = EditorGUILayout.TextField("Folder", outputFolder);
                if (EditorGUI.EndChangeCheck())
                    EditorPrefs.SetString(FolderKey, outputFolder);
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(clip == null))
            {
                if (GUILayout.Button("Bake Chart", GUILayout.Height(32f)))
                    Bake();
            }

            if (!string.IsNullOrEmpty(lastError))
                EditorGUILayout.HelpBox(lastError, MessageType.Error);

            if (!string.IsNullOrEmpty(lastReport))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(lastReport, GUILayout.MinHeight(140f));
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSettingsSummary()
        {
            EditorGUILayout.HelpBox(
                $"{settings.difficulty} · target {settings.TargetNotesPerSecond:F1} notes/s · " +
                $"{settings.laneCount} lanes · quantize {settings.quantizeMode} 1/{settings.subdivision * 4}" +
                (settings.trimToHook ? $" · hook {settings.hookLengthSeconds:F0}s" : " · full song"),
                MessageType.None);

            if (GUILayout.Button("Edit Settings Asset"))
                Selection.activeObject = settings;
        }

        private void DrawImporterWarning()
        {
            if (clip == null)
                return;

            string path = AssetDatabase.GetAssetPath(clip);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                return;

            AudioImporterSampleSettings sample = importer.defaultSampleSettings;
            bool needsLoadType = sample.loadType != AudioClipLoadType.DecompressOnLoad;
            bool needsPreload = !sample.preloadAudioData;

            if (!needsLoadType && !needsPreload)
                return;

            EditorGUILayout.HelpBox(
                "This clip cannot be read for analysis yet. It needs Load Type = Decompress On Load " +
                "and Preload Audio Data enabled.", MessageType.Warning);

            if (!GUILayout.Button("Fix Importer Settings"))
                return;

            sample.loadType = AudioClipLoadType.DecompressOnLoad;
            sample.preloadAudioData = true;
            importer.defaultSampleSettings = sample;
            importer.SaveAndReimport();
        }

        private void CreateSettingsAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Bake Settings",
                "BakeSettings_Default", "asset", "Where to save the bake settings");

            if (string.IsNullOrEmpty(path))
                return;

            ChartBakeSettings asset = CreateInstance<ChartBakeSettings>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            settings = asset;
            EditorPrefs.SetString(SettingsKey, path);
            Selection.activeObject = asset;
        }

        private void Bake()
        {
            lastError = null;
            lastReport = null;

            SongChart target = overwriteTarget;
            bool created = false;

            if (target == null)
            {
                target = CreateInstance<SongChart>();
                created = true;
            }

            bool success;
            string error;

            try
            {
                success = ChartGenerator.Generate(clip, settings, target,
                    (progress, label) => EditorUtility.DisplayProgressBar("Baking chart", label, progress),
                    out error);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!success)
            {
                lastError = error;

                if (created)
                    DestroyImmediate(target);

                return;
            }

            if (created)
            {
                string folder = EnsureFolder(outputFolder);
                string fileName = $"Chart_{clip.name}_{(settings != null ? settings.difficulty.ToString() : "Normal")}.asset";
                string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, fileName).Replace('\\', '/'));

                AssetDatabase.CreateAsset(target, path);
            }

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            overwriteTarget = target;
            lastReport = target.bakeReport;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        private static string EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                folder = DefaultFolder;

            folder = folder.Replace('\\', '/').TrimEnd('/');

            if (!folder.StartsWith("Assets"))
                folder = DefaultFolder;

            if (AssetDatabase.IsValidFolder(folder))
                return folder;

            string[] parts = folder.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";

                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);

                current = next;
            }

            return current;
        }
    }
}
