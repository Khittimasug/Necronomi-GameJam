using UnityEngine;
using UnityEngine.Events;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// Bridge into the existing interaction stack: walk up to something, press interact,
    /// play a chart, hand the result back. Mirrors how DialogueInteractable freezes and
    /// releases the player so the two never fight over control.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RhythmInteractable : MonoBehaviour, IInteractable
    {
        [Header("Rhythm")]
        [SerializeField] private RhythmGameController controller;
        [SerializeField] private SongChart chart;

        [Header("Interaction")]
        [SerializeField] private string prompt = "Play";
        [Tooltip("Off means this can only be played once")]
        [SerializeField] private bool repeatable = true;
        [Tooltip("Frozen while the song plays. Optional")]
        [SerializeField] private PlayerController playerToFreeze;
        [Tooltip("Shown while the song plays, hidden otherwise. Optional")]
        [SerializeField] private GameObject rhythmUiRoot;

        [Header("Events")]
        public UnityEvent onSongCleared;
        public UnityEvent onSongFailed;

        private BaseInteractionController activeInteractor;
        private bool consumed;

        public string Prompt => prompt;
        public Transform Transform => transform;

        public bool CanInteract =>
            controller != null &&
            chart != null &&
            !controller.IsPlaying &&
            (repeatable || !consumed);

        private void OnDisable()
        {
            // Never leave the player frozen or a stale handler subscribed if this
            // object goes away mid song.
            if (activeInteractor == null && controller == null)
                return;

            if (controller != null)
            {
                controller.SongFinished -= HandleSongFinished;

                if (controller.IsPlaying)
                    controller.StopSong();
            }

            Release();
        }

        public void Interact(BaseInteractionController interactor)
        {
            if (!CanInteract)
                return;

            activeInteractor = interactor;
            consumed = true;

            if (playerToFreeze != null)
                playerToFreeze.SetMovementEnabled(false);

            if (interactor != null)
                interactor.SetDetectionEnabled(false);

            if (rhythmUiRoot != null)
                rhythmUiRoot.SetActive(true);

            controller.SongFinished += HandleSongFinished;
            controller.StartSong(chart);
        }

        private void HandleSongFinished(RhythmResult result)
        {
            controller.SongFinished -= HandleSongFinished;

            if (result.failed)
                onSongFailed?.Invoke();
            else
                onSongCleared?.Invoke();

            Release();
        }

        private void Release()
        {
            if (playerToFreeze != null)
                playerToFreeze.SetMovementEnabled(true);

            if (activeInteractor != null)
                activeInteractor.SetDetectionEnabled(true);

            if (rhythmUiRoot != null)
                rhythmUiRoot.SetActive(false);

            activeInteractor = null;
        }
    }
}
