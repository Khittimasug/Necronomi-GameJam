using System;
using UnityEngine;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// A baked note chart for one audio clip. Pure data: the baker writes it, the
    /// runtime only reads it. Drop in any clip, bake, assign, play.
    /// </summary>
    [CreateAssetMenu(menuName = "Necronomi/Rhythm/Song Chart", fileName = "Chart_NewSong")]
    public sealed class SongChart : ScriptableObject
    {
        [Header("Song")]
        public AudioClip clip;
        public string title;
        public string artist;

        [Header("Tempo")]
        [Tooltip("Estimated by the baker, editable by hand")]
        public float bpm = 120f;
        [Tooltip("Clip time of the first downbeat, seconds")]
        public double firstBeatTime;
        [Tooltip("0..1 confidence of the tempo estimate. Low means hand check the grid")]
        [Range(0f, 1f)] public float tempoConfidence;

        [Header("Playback window")]
        [Tooltip("Clip time playback starts at. The baker sets this to the detected hook")]
        public double startTime;
        [Tooltip("Clip time playback ends at. 0 means play to the end of the clip")]
        public double endTime;

        [Header("Layout")]
        [Min(1)] public int laneCount = 4;
        [Tooltip("Positive values push every note later. Per song fudge on top of the player latency setting")]
        public double audioOffset;

        [Header("Notes")]
        [Tooltip("Sorted by time. Use the context menu if you edit these by hand")]
        public NoteData[] notes = Array.Empty<NoteData>();

        [Header("Bake info (read only)")]
        [TextArea(3, 12)] public string bakeReport;

        public int NoteCount => notes != null ? notes.Length : 0;

        public double ClipLength => clip != null ? clip.length : 0d;

        /// <summary>Clip time playback should stop at, resolving the 0 = end of clip case.</summary>
        public double ResolvedEndTime => endTime > 0d ? endTime : ClipLength;

        public double PlayableLength => Math.Max(0d, ResolvedEndTime - startTime);

        public double SecondsPerBeat => bpm > 0f ? 60d / bpm : 0.5d;

        public float NotesPerSecond => PlayableLength > 0.01d ? (float)(NoteCount / PlayableLength) : 0f;

        public double LastNoteEndTime
        {
            get
            {
                if (NoteCount == 0)
                    return startTime;

                double last = 0d;
                for (int i = 0; i < notes.Length; i++)
                    last = Math.Max(last, notes[i].EndTime);

                return last;
            }
        }

        [ContextMenu("Sort Notes By Time")]
        public void SortNotes()
        {
            if (notes == null || notes.Length < 2)
                return;

            Array.Sort(notes);
        }

        /// <summary>True when the asset has everything the runtime needs.</summary>
        public bool IsPlayable(out string reason)
        {
            if (clip == null)
            {
                reason = "No audio clip assigned.";
                return false;
            }

            if (NoteCount == 0)
            {
                reason = "Chart has no notes. Bake it first.";
                return false;
            }

            if (laneCount < 1)
            {
                reason = "Lane count must be at least 1.";
                return false;
            }

            reason = null;
            return true;
        }

        private void OnValidate()
        {
            if (laneCount < 1)
                laneCount = 1;

            if (endTime < 0d)
                endTime = 0d;

            if (startTime < 0d)
                startTime = 0d;
        }
    }
}
