using UnityEditor;
using UnityEngine;

namespace Necronomi.Rhythm.EditorTools
{
    /// <summary>Adds the numbers that matter to the chart inspector, plus a preview of the note spread.</summary>
    [CustomEditor(typeof(SongChart))]
    public sealed class SongChartEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SongChart chart = (SongChart)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);

            if (chart.NoteCount == 0)
            {
                EditorGUILayout.HelpBox("No notes. Bake this chart from Necronomi > Rhythm > Chart Baker.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Notes", chart.NoteCount.ToString());
                EditorGUILayout.LabelField("Playable length", $"{chart.PlayableLength:F1}s");
                EditorGUILayout.LabelField("Density", $"{chart.NotesPerSecond:F2} notes/s");
                EditorGUILayout.LabelField("Beat", $"{chart.SecondsPerBeat * 1000d:F0} ms");
            }

            if (chart.tempoConfidence < 0.35f && chart.NoteCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "Low tempo confidence. The beat grid is a guess: set Manual Bpm in the bake settings " +
                    "and bake again, or turn quantize off.", MessageType.Warning);
            }

            DrawDensityGraph(chart);

            EditorGUILayout.Space();

            if (GUILayout.Button("Open Chart Baker"))
                ChartBakerWindow.Open();
        }

        private static void DrawDensityGraph(SongChart chart)
        {
            if (chart.NoteCount == 0 || chart.PlayableLength <= 0.1d)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Note density over time", EditorStyles.miniBoldLabel);

            Rect rect = GUILayoutUtility.GetRect(0f, 46f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.14f));

            const int buckets = 96;
            int[] counts = new int[buckets];
            int peak = 1;

            for (int i = 0; i < chart.notes.Length; i++)
            {
                double t = (chart.notes[i].time - chart.startTime) / chart.PlayableLength;
                int bucket = Mathf.Clamp((int)(t * buckets), 0, buckets - 1);
                counts[bucket]++;
                peak = Mathf.Max(peak, counts[bucket]);
            }

            float barWidth = rect.width / buckets;

            for (int i = 0; i < buckets; i++)
            {
                float height = counts[i] / (float)peak * rect.height;
                Rect bar = new Rect(rect.x + i * barWidth, rect.yMax - height, Mathf.Max(1f, barWidth - 1f), height);
                EditorGUI.DrawRect(bar, new Color(0.85f, 0.35f, 0.25f));
            }
        }
    }
}
