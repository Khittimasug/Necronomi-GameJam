using UnityEngine;

/// <summary>
/// Interactable that plays a DialogueContent and freezes the player while it runs.
/// </summary>
[DisallowMultipleComponent]
public class DialogueInteractable : MonoBehaviour, IInteractable
{
    [Header("Dialogue")]
    [SerializeField] private BaseDialogueController dialogueController;
    [SerializeField] private DialogueContent content;

    [Header("Interaction")]
    [SerializeField] private string prompt = "Talk";
    [Tooltip("Off means this can only be played once")]
    [SerializeField] private bool repeatable = true;
    [Tooltip("Frozen while the dialogue plays. Optional")]
    [SerializeField] private PlayerController playerToFreeze;

    private BaseInteractionController activeInteractor;
    private bool consumed;

    public string Prompt => prompt;
    public Transform Transform => transform;

    public bool CanInteract =>
        dialogueController != null &&
        content != null &&
        !dialogueController.IsRunning &&
        (repeatable || !consumed);

    private void OnDisable()
    {
        // Never leave the player frozen, or a stale handler subscribed,
        // if this object goes away mid dialogue.
        if (activeInteractor == null)
            return;

        if (dialogueController != null)
            dialogueController.OnDialogueFinished -= HandleDialogueFinished;

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

        dialogueController.OnDialogueFinished += HandleDialogueFinished;
        dialogueController.StartDialogue(content);
    }

    private void HandleDialogueFinished()
    {
        dialogueController.OnDialogueFinished -= HandleDialogueFinished;
        Release();
    }

    private void Release()
    {
        if (playerToFreeze != null)
            playerToFreeze.SetMovementEnabled(true);

        if (activeInteractor != null)
            activeInteractor.SetDetectionEnabled(true);

        activeInteractor = null;
    }
}
