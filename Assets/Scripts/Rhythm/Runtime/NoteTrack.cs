using System.Collections.Generic;
using UnityEngine;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// Spawns, moves and pools note visuals. Walks the chart with a single cursor, so
    /// cost per frame depends on notes on screen, not on chart length.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NoteTrack : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private NoteView notePrefab;
        [Tooltip("Parent for pooled instances. Falls back to this transform")]
        [SerializeField] private Transform poolParent;
        [SerializeField, Min(0)] private int prewarm = 32;

        [Header("Timing")]
        [Tooltip("Seconds a note is visible before it reaches the hit line. Lower = harder to read")]
        [SerializeField, Min(0.2f)] private float approachTime = 1.5f;
        [Tooltip("Seconds a resolved note keeps travelling past the hit line before it is recycled")]
        [SerializeField, Min(0f)] private float overshootTime = 0.25f;

        private readonly Stack<NoteView> pool = new Stack<NoteView>();
        private List<NoteView>[] active;
        private NoteLane[] lanes;
        private SongChart chart;
        private int cursor;

        public double ApproachTime => approachTime;

        public void Setup(SongChart songChart, NoteLane[] laneRefs)
        {
            Clear();

            chart = songChart;
            lanes = laneRefs;
            cursor = 0;

            if (notePrefab == null)
                Debug.LogError("[Rhythm] NoteTrack has no note prefab, nothing will be visible.", this);

            int laneCount = laneRefs != null ? laneRefs.Length : 0;
            active = new List<NoteView>[laneCount];
            for (int i = 0; i < laneCount; i++)
                active[i] = new List<NoteView>(16);

            Prewarm();
        }

        /// <summary>Spawns anything due and repositions everything on screen.</summary>
        public void Tick(double judgePosition, double visualPosition)
        {
            if (chart == null || active == null)
                return;

            SpawnDue(judgePosition);

            for (int lane = 0; lane < active.Length; lane++)
            {
                List<NoteView> list = active[lane];

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    NoteView view = list[i];
                    double past = view.UpdatePosition(visualPosition);

                    if (!view.HeadResolved || view.HoldActive)
                        continue;

                    double tail = view.Note.type == NoteType.Hold ? view.Note.holdDuration : 0d;
                    if (past < tail + overshootTime)
                        continue;

                    list.RemoveAt(i);
                    Despawn(view);
                }
            }
        }

        /// <summary>
        /// Earliest unresolved note in the lane within the window. Returns null when the
        /// press has nothing to claim, which is what an empty press event is built from.
        /// </summary>
        public NoteView Claim(int lane, double pressTime, double window, out double delta)
        {
            delta = 0d;

            if (active == null || lane < 0 || lane >= active.Length)
                return null;

            List<NoteView> list = active[lane];
            NoteView best = null;
            double bestAbs = double.MaxValue;

            for (int i = 0; i < list.Count; i++)
            {
                NoteView view = list[i];
                if (view.HeadResolved)
                    continue;

                double difference = pressTime - view.Note.time;
                double absolute = difference < 0d ? -difference : difference;

                if (absolute > window || absolute >= bestAbs)
                    continue;

                bestAbs = absolute;
                best = view;
                delta = difference;
            }

            return best;
        }

        /// <summary>Notes whose hit window has fully closed without a press.</summary>
        public void CollectExpired(double judgePosition, double missWindow, List<NoteView> into)
        {
            if (active == null)
                return;

            for (int lane = 0; lane < active.Length; lane++)
            {
                List<NoteView> list = active[lane];
                for (int i = 0; i < list.Count; i++)
                {
                    NoteView view = list[i];
                    if (view.HeadResolved)
                        continue;

                    if (judgePosition - view.Note.time > missWindow)
                        into.Add(view);
                }
            }
        }

        /// <summary>Active hold notes the player is currently sustaining.</summary>
        public void CollectHolds(List<NoteView> into)
        {
            if (active == null)
                return;

            for (int lane = 0; lane < active.Length; lane++)
            {
                List<NoteView> list = active[lane];
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].HoldActive)
                        into.Add(list[i]);
                }
            }
        }

        public void Clear()
        {
            if (active == null)
                return;

            for (int lane = 0; lane < active.Length; lane++)
            {
                List<NoteView> list = active[lane];
                for (int i = 0; i < list.Count; i++)
                    Despawn(list[i]);

                list.Clear();
            }
        }

        private void SpawnDue(double judgePosition)
        {
            NoteData[] notes = chart.notes;
            if (notes == null || notePrefab == null)
                return;

            double horizon = judgePosition + approachTime;

            while (cursor < notes.Length && notes[cursor].time <= horizon)
            {
                NoteData note = notes[cursor];
                cursor++;

                if (note.time < chart.startTime)
                    continue;

                int laneIndex = note.lane;
                if (lanes == null || laneIndex < 0 || laneIndex >= lanes.Length || lanes[laneIndex] == null)
                    continue;

                NoteView view = Rent();
                view.Bind(note, lanes[laneIndex], approachTime);
                view.UpdatePosition(judgePosition);
                active[laneIndex].Add(view);
            }
        }

        private void Prewarm()
        {
            if (notePrefab == null)
                return;

            for (int i = pool.Count; i < prewarm; i++)
            {
                NoteView view = Instantiate(notePrefab, Parent);
                view.gameObject.SetActive(false);
                pool.Push(view);
            }
        }

        private Transform Parent => poolParent != null ? poolParent : transform;

        private NoteView Rent()
        {
            if (pool.Count > 0)
                return pool.Pop();

            return Instantiate(notePrefab, Parent);
        }

        private void Despawn(NoteView view)
        {
            if (view == null)
                return;

            view.HoldActive = false;
            view.gameObject.SetActive(false);
            view.transform.SetParent(Parent, false);
            pool.Push(view);
        }

        /// <summary>Recycles a note the controller has finished with, e.g. a completed hold.</summary>
        public void Retire(NoteView view)
        {
            if (active == null || view == null)
                return;

            int lane = view.Lane != null ? view.Lane.LaneIndex : -1;
            if (lane >= 0 && lane < active.Length)
                active[lane].Remove(view);

            Despawn(view);
        }
    }
}
