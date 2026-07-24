using UnityEngine;
using UnityEngine.Events;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// One column of the track. Notes travel from spawnPoint to hitPoint, so the lane
    /// works the same whether the track is a flat 2D bar or angled away in 3D.
    /// Wire MMFeedbacks or any other juice into the events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NoteLane : MonoBehaviour
    {
        [Header("Geometry")]
        [Tooltip("Where notes appear. Falls back to this transform")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Where notes must be pressed. Falls back to this transform")]
        [SerializeField] private Transform hitPoint;

        [Header("Identity")]
        [SerializeField, Min(0)] private int laneIndex;
        [Tooltip("Optional tint pushed onto spawned notes")]
        [SerializeField] private Color laneColor = Color.white;

        [Header("Events")]
        [Tooltip("Fired whenever the lane key goes down, hit or not")]
        public UnityEvent onPressed;
        [Tooltip("Fired when a note in this lane is hit. Argument is the judgement")]
        public UnityEvent<Judgement> onHit;
        [Tooltip("Fired when a note in this lane is missed, either by a bad press or by letting it pass")]
        public UnityEvent onMissed;
        [Tooltip("Fired when the key goes down with no note anywhere near it")]
        public UnityEvent onEmptyPress;

        public int LaneIndex => laneIndex;
        public Color LaneColor => laneColor;
        public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;
        public Transform HitPoint => hitPoint != null ? hitPoint : transform;

        /// <summary>Position a note should sit at. 0 is the spawn point, 1 is the hit line, past 1 overshoots.</summary>
        public Vector3 Evaluate(float progress) =>
            Vector3.LerpUnclamped(SpawnPoint.position, HitPoint.position, progress);

        public void SetLaneIndex(int index) => laneIndex = Mathf.Max(0, index);

        internal void RaisePressed() => onPressed?.Invoke();
        internal void RaiseHit(Judgement judgement) => onHit?.Invoke(judgement);
        internal void RaiseMissed() => onMissed?.Invoke();
        internal void RaiseEmptyPress() => onEmptyPress?.Invoke();

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = laneColor;
            Gizmos.DrawLine(SpawnPoint.position, HitPoint.position);
            Gizmos.DrawWireSphere(HitPoint.position, 0.15f);
        }
#endif
    }
}
