using UnityEngine;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// Visual for one note. Position is derived from the song clock every frame rather
    /// than integrated, so a frame hitch can never desync a note from its audio.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NoteView : MonoBehaviour
    {
        [Tooltip("Optional. Scaled along its local Y to cover the hold duration")]
        [SerializeField] private Transform holdBody;
        [Tooltip("Optional. Tinted with the lane colour on spawn")]
        [SerializeField] private Renderer[] tintTargets;
        [Tooltip("Optional. Same as tintTargets but for UI or sprites")]
        [SerializeField] private SpriteRenderer[] spriteTargets;

        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock propertyBlock;

        public NoteData Note { get; private set; }
        public NoteLane Lane { get; private set; }

        /// <summary>True once the head of the note has been resolved, hit or missed.</summary>
        public bool HeadResolved { get; private set; }

        /// <summary>True while the player is correctly holding a hold note.</summary>
        public bool HoldActive { get; internal set; }

        private double approachTime = 1.5d;

        public void Bind(NoteData note, NoteLane lane, double approach)
        {
            Note = note;
            Lane = lane;
            approachTime = Mathf.Max(0.05f, (float)approach);
            HeadResolved = false;
            HoldActive = false;

            gameObject.SetActive(true);
            ApplyTint(lane.LaneColor);
            ApplyHoldLength(lane);
        }

        public void MarkHeadResolved() => HeadResolved = true;

        /// <summary>Moves the note for the current song time. Returns how far past the hit line it is, in seconds.</summary>
        public double UpdatePosition(double songPosition)
        {
            double delta = Note.time - songPosition;
            float progress = (float)(1d - delta / approachTime);
            transform.position = Lane.Evaluate(progress);

            return -delta;
        }

        private void ApplyHoldLength(NoteLane lane)
        {
            if (holdBody == null)
                return;

            bool isHold = Note.type == NoteType.Hold && Note.holdDuration > 0.01d;
            holdBody.gameObject.SetActive(isHold);

            if (!isHold)
                return;

            // Hold length in world units = how far the track scrolls during the hold.
            float trackLength = Vector3.Distance(lane.SpawnPoint.position, lane.HitPoint.position);
            float unitsPerSecond = trackLength / Mathf.Max(0.05f, (float)approachTime);
            float length = unitsPerSecond * (float)Note.holdDuration;

            Vector3 scale = holdBody.localScale;
            scale.y = Mathf.Max(0.01f, length);
            holdBody.localScale = scale;
        }

        private void ApplyTint(Color color)
        {
            if (spriteTargets != null)
            {
                for (int i = 0; i < spriteTargets.Length; i++)
                {
                    if (spriteTargets[i] != null)
                        spriteTargets[i].color = color;
                }
            }

            if (tintTargets == null || tintTargets.Length == 0)
                return;

            propertyBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < tintTargets.Length; i++)
            {
                Renderer renderer = tintTargets[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(ColorId, color);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
