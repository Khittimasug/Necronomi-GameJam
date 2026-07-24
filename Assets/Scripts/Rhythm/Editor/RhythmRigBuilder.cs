using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Necronomi.Rhythm.EditorTools
{
    /// <summary>
    /// Builds a playable rhythm rig in the open scene: note prefab, materials, conductor,
    /// track, input and four lanes, all wired. Re-runnable, it replaces any rig it finds.
    /// </summary>
    public static class RhythmRigBuilder
    {
        private const string PrefabFolder = "Assets/Rhythm/Prefabs";
        private const string MaterialFolder = "Assets/Rhythm/Materials";
        private const string NotePrefabPath = PrefabFolder + "/Note.prefab";
        private const string RootName = "RhythmGame";

        // Track geometry, in local space of the camera. Tweak here and rebuild.
        private const float TrackDistance = 3f;
        private const float LaneSpacing = 0.8f;
        private const float SpawnHeight = 1.9f;
        private const float HitHeight = -1.05f;
        private const int LaneCount = 4;

        private static readonly Color[] LaneColors =
        {
            new Color(0.95f, 0.25f, 0.30f),
            new Color(0.95f, 0.80f, 0.25f),
            new Color(0.30f, 0.85f, 0.95f),
            new Color(0.80f, 0.35f, 0.95f)
        };

        [MenuItem("Necronomi/Rhythm/Build Rhythm Rig In Scene", false, 20)]
        public static void Build()
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);

            NoteView notePrefab = BuildNotePrefab();
            Material lineMaterial = BuildMaterial("HitLine_Unlit", new Color(1f, 1f, 1f, 1f));

            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing);

            Camera camera = Camera.main;
            if (camera == null)
                camera = Object.FindFirstObjectByType<Camera>();

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Rhythm Rig");

            if (camera != null)
            {
                root.transform.SetParent(camera.transform, false);
                root.transform.localPosition = new Vector3(0f, 0f, TrackDistance);
                root.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Debug.LogWarning("[Rhythm] No camera in the scene, rig placed at the world origin.");
            }

            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;

            Conductor conductor = root.AddComponent<Conductor>();
            RhythmInput input = root.AddComponent<RhythmInput>();

            GameObject trackObject = CreateChild(root.transform, "Track");
            NoteTrack track = trackObject.AddComponent<NoteTrack>();

            GameObject pool = CreateChild(trackObject.transform, "Pool");

            NoteLane[] lanes = new NoteLane[LaneCount];
            float firstX = -(LaneCount - 1) * 0.5f * LaneSpacing;

            for (int i = 0; i < LaneCount; i++)
            {
                float x = firstX + i * LaneSpacing;

                GameObject laneObject = CreateChild(root.transform, $"Lane{i}");
                laneObject.transform.localPosition = new Vector3(x, 0f, 0f);

                GameObject spawn = CreateChild(laneObject.transform, "Spawn");
                spawn.transform.localPosition = new Vector3(0f, SpawnHeight, 0f);

                GameObject hit = CreateChild(laneObject.transform, "Hit");
                hit.transform.localPosition = new Vector3(0f, HitHeight, 0f);

                GameObject line = CreateQuad(hit.transform, "HitLine", lineMaterial,
                    new Vector3(LaneSpacing * 0.9f, 0.05f, 1f));
                line.transform.localPosition = Vector3.zero;

                NoteLane lane = laneObject.AddComponent<NoteLane>();

                SerializedObject laneSerialized = new SerializedObject(lane);
                laneSerialized.FindProperty("spawnPoint").objectReferenceValue = spawn.transform;
                laneSerialized.FindProperty("hitPoint").objectReferenceValue = hit.transform;
                laneSerialized.FindProperty("laneIndex").intValue = i;
                laneSerialized.FindProperty("laneColor").colorValue = LaneColors[i % LaneColors.Length];
                laneSerialized.ApplyModifiedPropertiesWithoutUndo();

                lanes[i] = lane;
            }

            SerializedObject trackSerialized = new SerializedObject(track);
            trackSerialized.FindProperty("notePrefab").objectReferenceValue = notePrefab;
            trackSerialized.FindProperty("poolParent").objectReferenceValue = pool.transform;
            trackSerialized.ApplyModifiedPropertiesWithoutUndo();

            RhythmGameController controller = root.AddComponent<RhythmGameController>();

            SerializedObject controllerSerialized = new SerializedObject(controller);
            controllerSerialized.FindProperty("conductor").objectReferenceValue = conductor;
            controllerSerialized.FindProperty("track").objectReferenceValue = track;
            controllerSerialized.FindProperty("input").objectReferenceValue = input;

            SerializedProperty laneArray = controllerSerialized.FindProperty("lanes");
            laneArray.arraySize = LaneCount;
            for (int i = 0; i < LaneCount; i++)
                laneArray.GetArrayElementAtIndex(i).objectReferenceValue = lanes[i];

            SongChart chart = FindFirstChart();
            if (chart != null)
            {
                controllerSerialized.FindProperty("chart").objectReferenceValue = chart;
                controllerSerialized.FindProperty("autoPlayOnStart").boolValue = true;
            }

            controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log(chart != null
                ? $"[Rhythm] Rig built. Auto playing '{chart.name}' ({chart.NoteCount} notes) on Play. Keys: D F J K."
                : "[Rhythm] Rig built. No chart found, assign one on the RhythmGameController. Keys: D F J K.");
        }

        private static NoteView BuildNotePrefab()
        {
            Material noteMaterial = BuildMaterial("Note_Unlit", Color.white);

            GameObject note = CreateQuad(null, "Note", noteMaterial, new Vector3(0.62f, 0.18f, 1f));

            // Hold body hangs off a pivot at the note origin, so scaling its Y grows the
            // tail upwards instead of expanding around the centre.
            GameObject holdPivot = CreateChild(note.transform, "HoldPivot");

            // The note root is already flipped, so this child must not flip again.
            GameObject holdQuad = CreateQuad(holdPivot.transform, "HoldBody", noteMaterial,
                new Vector3(0.30f, 1f, 1f), false);
            holdQuad.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            holdPivot.SetActive(true);

            NoteView view = note.AddComponent<NoteView>();

            SerializedObject serialized = new SerializedObject(view);
            serialized.FindProperty("holdBody").objectReferenceValue = holdPivot.transform;

            SerializedProperty tints = serialized.FindProperty("tintTargets");
            tints.arraySize = 2;
            tints.GetArrayElementAtIndex(0).objectReferenceValue = note.GetComponent<MeshRenderer>();
            tints.GetArrayElementAtIndex(1).objectReferenceValue = holdQuad.GetComponent<MeshRenderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            NoteView saved = PrefabUtility.SaveAsPrefabAsset(note, NotePrefabPath).GetComponent<NoteView>();
            Object.DestroyImmediate(note);

            return saved;
        }

        private static Material BuildMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            Material material = new Material(shader);
            material.name = name;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else
                material.color = color;

            // Draw on top of world geometry so a wall between the camera and the track
            // cannot swallow the notes.
            if (material.HasProperty("_ZTest"))
                material.SetInt("_ZTest", (int)CompareFunction.Always);

            material.renderQueue = 4000;

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        /// <summary>
        /// Unity's Quad faces +Z. The whole rig is parented to the camera, so +Z points away
        /// from the viewer and the quad would be backface culled. faceCamera flips it round.
        /// Pass false when the parent chain has already been flipped, or it cancels out.
        /// </summary>
        private static GameObject CreateQuad(Transform parent, string name, Material material, Vector3 scale,
            bool faceCamera = true)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;

            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            if (parent != null)
                quad.transform.SetParent(parent, false);

            quad.transform.localPosition = Vector3.zero;
            quad.transform.localRotation = faceCamera ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            quad.transform.localScale = scale;
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;

            return quad;
        }

        private static SongChart FindFirstChart()
        {
            string[] guids = AssetDatabase.FindAssets("t:SongChart");
            List<SongChart> playable = new List<SongChart>();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                SongChart chart = AssetDatabase.LoadAssetAtPath<SongChart>(path);

                if (chart != null && chart.NoteCount > 0)
                    playable.Add(chart);
            }

            return playable.Count > 0 ? playable[0] : null;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string[] parts = folder.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";

                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);

                current = next;
            }
        }
    }
}
