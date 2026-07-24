using System;
using UnityEngine;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// The clock everything else trusts. Song position comes from AudioSettings.dspTime,
    /// never Time.time and never AudioSource.time, because only the DSP clock stays
    /// locked to the audio hardware.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class Conductor : MonoBehaviour
    {
        public const string LatencyPrefsKey = "necronomi.rhythm.latency";

        [Header("Playback")]
        [Tooltip("Silent count in before the audio starts, seconds. Gives the first notes room to approach")]
        [SerializeField, Min(0f)] private float leadIn = 2f;
        [Tooltip("Applied on top of the calibrated player latency, seconds. Positive delays the notes")]
        [SerializeField] private double globalOffset;

        [Header("Smoothing")]
        [Tooltip("The DSP clock only ticks once per audio buffer. Interpolate between ticks so notes glide")]
        [SerializeField] private bool smoothVisualPosition = true;
        [Tooltip("Resync the smoothed clock if it drifts further than this from the DSP clock, seconds")]
        [SerializeField, Min(0.005f)] private double maxSmoothingDrift = 0.05d;

        [Header("Safety")]
        [Tooltip("Abandon the song if the clock jumps this far in one frame, instead of auto missing every " +
                 "remaining note. Covers alt tab and breakpoints. Keep it well above a normal frame hitch")]
        [SerializeField, Min(0.25f)] private double maxTimeJump = 1.5d;
        [Tooltip("The guard stays off for this long after the audio starts. Entering play mode hitches hard " +
                 "while shaders compile, and that must not read as a desync")]
        [SerializeField, Min(0f)] private double guardGraceSeconds = 1.5d;
        [Tooltip("Updates that must run before the guard arms. The first frame after Play absorbs however " +
                 "long the load took, so it can never be measured as a jump")]
        [SerializeField, Min(1)] private int guardGraceFrames = 3;
        [Tooltip("Turn the guard off entirely while debugging timing")]
        [SerializeField] private bool desyncGuardEnabled = true;

        private AudioSource source;
        private SongChart chart;
        private double dspStartTime;
        private double lastDspTime;
        private double smoothedPosition;
        private double lastPosition;
        private double guardArmTime;
        private int guardArmFrame;
        private bool running;

        /// <summary>Fires once when playback passes the chart end time.</summary>
        public event Action OnSongEnded;

        public bool IsRunning => running;

        /// <summary>True when the last song was abandoned by the desync guard rather than played out.</summary>
        public bool Interrupted { get; private set; }

        public SongChart Chart => chart;

        public AudioSource Source => source;

        /// <summary>Player calibrated output latency in seconds, shared by every song.</summary>
        public static double PlayerLatency
        {
            get => PlayerPrefs.GetFloat(LatencyPrefsKey, 0f);
            set
            {
                PlayerPrefs.SetFloat(LatencyPrefsKey, (float)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Total shift applied to the timeline, seconds.</summary>
        public double TotalOffset => PlayerLatency + globalOffset + (chart != null ? chart.audioOffset : 0d);

        /// <summary>
        /// Clip time the song is at right now. Negative of the chart start during the
        /// lead in, which is what lets the first notes fly in before audio begins.
        /// </summary>
        public double SongPosition
        {
            get
            {
                if (chart == null)
                    return 0d;

                return AudioSettings.dspTime - dspStartTime + chart.startTime - TotalOffset;
            }
        }

        /// <summary>Same timeline as SongPosition but interpolated between DSP ticks. Use this for visuals only.</summary>
        public double VisualPosition => smoothVisualPosition && running ? smoothedPosition : SongPosition;

        /// <summary>Seconds until audio actually starts. Zero or negative once it is playing.</summary>
        public double TimeUntilAudio => dspStartTime - AudioSettings.dspTime;

        /// <summary>Position expressed in beats from the first downbeat.</summary>
        public double BeatPosition
        {
            get
            {
                if (chart == null || chart.bpm <= 0f)
                    return 0d;

                return (SongPosition - chart.firstBeatTime) / chart.SecondsPerBeat;
            }
        }

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
        }

        private void Update()
        {
            if (!running)
                return;

            if (smoothVisualPosition)
                UpdateSmoothing();

            double position = SongPosition;

            // The DSP clock keeps running while the game does not: alt tab or a breakpoint land
            // here. Abandon the song rather than miss every remaining note. Held off until the
            // grace window closes, because entering play mode hitches hard while shaders compile
            // and that is not a desync.
            if (desyncGuardEnabled && Time.frameCount >= guardArmFrame && AudioSettings.dspTime >= guardArmTime
                                   && position - lastPosition > maxTimeJump)
            {
                Interrupted = true;
                Abort();
                return;
            }

            lastPosition = position;

            if (chart != null && position >= chart.ResolvedEndTime)
                Finish();
        }

        /// <summary>Loads a chart without starting it. Safe to call while something else is playing.</summary>
        public void Prepare(SongChart nextChart)
        {
            Stop();

            chart = nextChart;

            if (chart == null || chart.clip == null)
                return;

            source.clip = chart.clip;
            source.time = (float)Mathf.Clamp((float)chart.startTime, 0f, Mathf.Max(0f, chart.clip.length - 0.05f));
        }

        /// <summary>Schedules the audio and starts the clock. Notes may spawn during the lead in.</summary>
        public void Play()
        {
            if (chart == null || chart.clip == null)
            {
                Debug.LogWarning("[Conductor] Play called with no chart or clip.", this);
                return;
            }

            source.clip = chart.clip;
            source.time = (float)Mathf.Clamp((float)chart.startTime, 0f, Mathf.Max(0f, chart.clip.length - 0.05f));

            Interrupted = false;
            dspStartTime = AudioSettings.dspTime + Math.Max(0.05f, leadIn);
            guardArmTime = dspStartTime + guardGraceSeconds;
            guardArmFrame = Time.frameCount + guardGraceFrames;
            source.PlayScheduled(dspStartTime);

            smoothedPosition = SongPosition;
            lastPosition = SongPosition;
            lastDspTime = AudioSettings.dspTime;
            running = true;
        }

        public void Stop()
        {
            if (source != null && source.isPlaying)
                source.Stop();

            running = false;
        }

        /// <summary>Stops playback and reports the song as ended, for a fail out.</summary>
        public void Abort()
        {
            if (!running)
                return;

            Stop();
            OnSongEnded?.Invoke();
        }

        private void Finish()
        {
            running = false;

            if (source.isPlaying)
                source.Stop();

            OnSongEnded?.Invoke();
        }

        private void UpdateSmoothing()
        {
            double dsp = AudioSettings.dspTime;

            if (dsp > lastDspTime)
            {
                // The audio buffer advanced: trust the hardware clock again.
                lastDspTime = dsp;
                double authoritative = SongPosition;

                if (Math.Abs(authoritative - smoothedPosition) > maxSmoothingDrift)
                    smoothedPosition = authoritative;
                else
                    smoothedPosition = smoothedPosition * 0.7d + authoritative * 0.3d;
            }
            else
            {
                // Between buffers: extrapolate with frame time so visuals do not stair step.
                smoothedPosition += Time.unscaledDeltaTime;
            }
        }
    }
}
