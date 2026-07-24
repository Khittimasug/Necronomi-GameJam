using System;
using UnityEngine;

namespace Necronomi.Rhythm
{
    public enum NoteType
    {
        Tap = 0,
        Hold = 1
    }

    /// <summary>
    /// One playable note. Times are seconds on the audio clip timeline, so a chart
    /// stays valid no matter where playback is trimmed to.
    /// </summary>
    [Serializable]
    public struct NoteData : IComparable<NoteData>
    {
        [Tooltip("Seconds from the start of the audio clip")]
        public double time;

        [Min(0)] public int lane;

        public NoteType type;

        [Tooltip("Seconds. Ignored by Tap notes")]
        public double holdDuration;

        [Tooltip("How strong the onset was. Used by the density filter and for juice scaling")]
        [Range(0f, 1f)] public float strength;

        public double EndTime => type == NoteType.Hold ? time + holdDuration : time;

        public int CompareTo(NoteData other)
        {
            int byTime = time.CompareTo(other.time);
            return byTime != 0 ? byTime : lane.CompareTo(other.lane);
        }

        public static NoteData Tap(double time, int lane, float strength = 1f) => new NoteData
        {
            time = time,
            lane = lane,
            type = NoteType.Tap,
            holdDuration = 0d,
            strength = strength
        };
    }
}
