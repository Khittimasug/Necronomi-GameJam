using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// Tap along with a metronome to measure how far behind the player's audio output is.
    /// The median of the taps is stored once and every chart uses it afterwards, which
    /// removes the single biggest source of "the game feels off" complaints.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class LatencyCalibrator : MonoBehaviour
    {
        [Header("Metronome")]
        [SerializeField] private AudioClip clickClip;
        [SerializeField, Range(40f, 180f)] private float bpm = 100f;
        [Tooltip("Taps collected before the result is committed")]
        [SerializeField, Min(3)] private int sampleCount = 8;
        [Tooltip("Beats played before taps start counting")]
        [SerializeField, Min(0)] private int warmupBeats = 4;
        [Tooltip("Taps further than this from a beat are thrown away, seconds")]
        [SerializeField, Range(0.05f, 0.5f)] private float rejectWindow = 0.25f;

        [Header("Events")]
        public UnityEvent<int> onTapRecorded;
        [Tooltip("Argument is the measured latency in milliseconds")]
        public UnityEvent<float> onCalibrationFinished;

        private readonly List<double> offsets = new List<double>();
        private AudioSource source;
        private double startDspTime;
        private double nextClickDspTime;
        private int beatsPlayed;
        private bool running;

        public bool IsRunning => running;
        public int TapsRecorded => offsets.Count;
        public double SecondsPerBeat => 60d / Mathf.Max(1f, bpm);

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
        }

        private void Update()
        {
            if (!running)
                return;

            // Schedule one beat ahead so the click lands exactly on the DSP clock.
            if (AudioSettings.dspTime + 0.1d < nextClickDspTime)
                return;

            if (clickClip != null)
                source.PlayOneShot(clickClip);

            nextClickDspTime += SecondsPerBeat;
            beatsPlayed++;
        }

        public void Begin()
        {
            offsets.Clear();
            beatsPlayed = 0;
            startDspTime = AudioSettings.dspTime + 0.5d;
            nextClickDspTime = startDspTime;
            running = true;
        }

        public void Cancel()
        {
            running = false;
        }

        /// <summary>Call from any input handler on every tap while calibration runs.</summary>
        public void RegisterTap()
        {
            if (!running || beatsPlayed <= warmupBeats)
                return;

            double now = AudioSettings.dspTime;
            double beats = (now - startDspTime) / SecondsPerBeat;
            double nearest = Math.Round(beats);
            double offset = (beats - nearest) * SecondsPerBeat;

            if (Math.Abs(offset) > rejectWindow)
                return;

            offsets.Add(offset);
            onTapRecorded?.Invoke(offsets.Count);

            if (offsets.Count >= sampleCount)
                Commit();
        }

        private void Commit()
        {
            running = false;

            if (offsets.Count == 0)
                return;

            offsets.Sort();
            double median = offsets[offsets.Count / 2];

            Conductor.PlayerLatency = median;
            onCalibrationFinished?.Invoke((float)(median * 1000d));
        }
    }
}
