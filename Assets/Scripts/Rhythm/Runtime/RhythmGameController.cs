using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// Glue between the clock, the track, the input and the score. Drop this on one
    /// object with a Conductor, a NoteTrack and a RhythmInput, point it at the lanes,
    /// then call StartSong with any baked chart.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Conductor))]
    public sealed class RhythmGameController : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private Conductor conductor;
        [SerializeField] private NoteTrack track;
        [SerializeField] private RhythmInput input;
        [Tooltip("In lane order, left to right. Lane indices are assigned from this order")]
        [SerializeField] private NoteLane[] lanes;

        [Header("Song")]
        [Tooltip("Optional. Played on Start when Auto Play is on")]
        [SerializeField] private SongChart chart;
        [SerializeField] private bool autoPlayOnStart;

        [Header("Rules")]
        [SerializeField] private JudgementWindows windows = new JudgementWindows();
        [SerializeField] private ScoreSystem score = new ScoreSystem();
        [Tooltip("Seconds of silence kept after the last note before the song is called finished")]
        [SerializeField, Min(0f)] private float tailSeconds = 1.5f;

        [Header("Events")]
        [Tooltip("Song audio has been scheduled and notes are inbound")]
        public UnityEvent onSongStarted;
        [Tooltip("A note was hit. Argument is how clean the hit was")]
        public UnityEvent<Judgement> onNoteHit;
        [Tooltip("A note was NOT pressed in time and slipped past the hit line")]
        public UnityEvent onNoteMissed;
        [Tooltip("A lane key was pressed with no note in range")]
        public UnityEvent onEmptyPress;
        [Tooltip("A hold note was released early")]
        public UnityEvent onHoldBroken;
        [Tooltip("Combo dropped back to zero for any reason")]
        public UnityEvent onComboBreak;
        [Tooltip("Song reached the end, cleared or not")]
        public UnityEvent onSongFinished;
        [Tooltip("Song was abandoned by the desync guard, e.g. the player alt tabbed. No result is produced")]
        public UnityEvent onSongInterrupted;
        [Tooltip("Health ran out. Fires just before onSongFinished")]
        public UnityEvent onFailed;

        /// <summary>Code side hooks, for systems that need the data and not just the signal.</summary>
        public event Action<Judgement, NoteData> NoteJudged;
        public event Action<RhythmResult> SongFinished;

        public bool IsPlaying { get; private set; }
        public ScoreSystem Score => score;
        public JudgementWindows Windows => windows;
        public Conductor Conductor => conductor;
        public SongChart CurrentChart => chart;

        private readonly List<NoteView> expiredBuffer = new List<NoteView>(16);
        private readonly List<NoteView> holdBuffer = new List<NoteView>(8);
        private double lastNoteEnd;
        private bool failed;

        private void Reset()
        {
            conductor = GetComponent<Conductor>();
            track = GetComponentInChildren<NoteTrack>();
            input = GetComponentInChildren<RhythmInput>();
            lanes = GetComponentsInChildren<NoteLane>();
        }

        private void Awake()
        {
            if (conductor == null)
                conductor = GetComponent<Conductor>();

            if (track == null)
                track = GetComponentInChildren<NoteTrack>();

            if (input == null)
                input = GetComponentInChildren<RhythmInput>();

            windows.Validate();
            AssignLaneIndices();
        }

        private void OnEnable()
        {
            if (input != null)
                input.OnLanePressed += HandleLanePressed;

            if (conductor != null)
                conductor.OnSongEnded += HandleConductorEnded;
        }

        private void OnDisable()
        {
            if (input != null)
                input.OnLanePressed -= HandleLanePressed;

            if (conductor != null)
                conductor.OnSongEnded -= HandleConductorEnded;
        }

        private void Start()
        {
            if (autoPlayOnStart && chart != null)
                StartSong(chart);
        }

        private void Update()
        {
            if (!IsPlaying)
                return;

            double judgePosition = conductor.SongPosition;
            track.Tick(judgePosition, conductor.VisualPosition);

            ResolveExpired(judgePosition);
            ResolveHolds(judgePosition);

            if (score.Dead && !failed)
            {
                failed = true;
                conductor.Abort();
                return;
            }

            // The conductor ends on the chart window, but a chart can end well before the
            // audio does. Close out once the last note plus its tail has gone by.
            if (judgePosition > lastNoteEnd + tailSeconds)
                Finish();
        }

        /// <summary>Starts the given chart from its baked start time. Safe to call while another song runs.</summary>
        public void StartSong(SongChart nextChart)
        {
            if (nextChart == null)
            {
                Debug.LogWarning("[Rhythm] StartSong called with no chart.", this);
                return;
            }

            if (!nextChart.IsPlayable(out string reason))
            {
                Debug.LogError($"[Rhythm] Cannot play '{nextChart.name}': {reason}", nextChart);
                return;
            }

            if (lanes == null || lanes.Length == 0)
            {
                Debug.LogError("[Rhythm] No lanes assigned on the controller.", this);
                return;
            }

            if (lanes.Length < nextChart.laneCount)
            {
                Debug.LogWarning($"[Rhythm] Chart '{nextChart.name}' wants {nextChart.laneCount} lanes but only " +
                                 $"{lanes.Length} are wired. Notes in the missing lanes will be skipped.", this);
            }

            StopSong();

            chart = nextChart;
            failed = false;
            score.Reset();

            AssignLaneIndices();
            track.Setup(chart, lanes);

            lastNoteEnd = chart.LastNoteEndTime;

            conductor.Prepare(chart);
            conductor.Play();

            if (input != null)
                input.InputEnabled = true;

            IsPlaying = true;
            onSongStarted?.Invoke();
        }

        /// <summary>Stops without reporting a result. Used when the player walks away or the scene unloads.</summary>
        public void StopSong()
        {
            if (input != null)
                input.InputEnabled = false;

            if (conductor != null)
                conductor.Stop();

            if (track != null)
                track.Clear();

            IsPlaying = false;
        }

        private void HandleConductorEnded()
        {
            if (!IsPlaying)
                return;

            if (conductor.Interrupted)
            {
                StopSong();
                onSongInterrupted?.Invoke();
                return;
            }

            Finish();
        }

        private void Finish()
        {
            if (!IsPlaying)
                return;

            IsPlaying = false;

            if (input != null)
                input.InputEnabled = false;

            conductor.Stop();
            track.Clear();

            RhythmResult result = score.BuildResult(failed);

            if (failed)
                onFailed?.Invoke();

            onSongFinished?.Invoke();
            SongFinished?.Invoke(result);
        }

        private void HandleLanePressed(int lane, double pressAge)
        {
            if (!IsPlaying || lanes == null || lane < 0 || lane >= lanes.Length || lanes[lane] == null)
                return;

            lanes[lane].RaisePressed();

            // Rewind the clock by the age of the input event so a press that landed
            // mid frame is judged where it actually happened.
            double pressTime = conductor.SongPosition - pressAge;

            NoteView view = track.Claim(lane, pressTime, windows.HitSeconds, out double delta);

            if (view == null)
            {
                int comboBefore = score.Combo;
                score.RegisterEmptyPress();

                lanes[lane].RaiseEmptyPress();
                onEmptyPress?.Invoke();

                if (comboBefore > 0)
                    onComboBreak?.Invoke();

                return;
            }

            Judgement judgement = windows.Classify(delta);
            view.MarkHeadResolved();

            if (view.Note.type == NoteType.Hold && view.Note.holdDuration > 0.01d)
                view.HoldActive = true;

            ApplyJudgement(judgement, view.Note, lanes[lane]);
        }

        private void ResolveExpired(double judgePosition)
        {
            expiredBuffer.Clear();
            track.CollectExpired(judgePosition, windows.MissSeconds, expiredBuffer);

            for (int i = 0; i < expiredBuffer.Count; i++)
            {
                NoteView view = expiredBuffer[i];
                view.MarkHeadResolved();

                NoteLane lane = view.Lane;
                ApplyJudgement(Judgement.Miss, view.Note, lane);
            }
        }

        private void ResolveHolds(double judgePosition)
        {
            holdBuffer.Clear();
            track.CollectHolds(holdBuffer);

            for (int i = 0; i < holdBuffer.Count; i++)
            {
                NoteView view = holdBuffer[i];
                int lane = view.Lane != null ? view.Lane.LaneIndex : -1;

                if (judgePosition >= view.Note.EndTime)
                {
                    view.HoldActive = false;
                    score.RegisterHoldComplete();
                    track.Retire(view);
                    continue;
                }

                bool stillHeld = input == null || input.IsHeld(lane);
                if (stillHeld)
                    continue;

                // Let go early: the tail is lost, treated as a miss so combo and health react.
                view.HoldActive = false;
                onHoldBroken?.Invoke();
                ApplyJudgement(Judgement.Miss, view.Note, view.Lane);
                track.Retire(view);
            }
        }

        private void ApplyJudgement(Judgement judgement, NoteData note, NoteLane lane)
        {
            int comboBefore = score.Combo;
            score.Register(judgement);

            if (judgement == Judgement.Miss)
            {
                lane?.RaiseMissed();
                onNoteMissed?.Invoke();

                if (comboBefore > 0)
                    onComboBreak?.Invoke();
            }
            else
            {
                lane?.RaiseHit(judgement);
                onNoteHit?.Invoke(judgement);
            }

            NoteJudged?.Invoke(judgement, note);
        }

        private void AssignLaneIndices()
        {
            if (lanes == null)
                return;

            for (int i = 0; i < lanes.Length; i++)
            {
                if (lanes[i] != null)
                    lanes[i].SetLaneIndex(i);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            windows?.Validate();
        }
#endif
    }
}
