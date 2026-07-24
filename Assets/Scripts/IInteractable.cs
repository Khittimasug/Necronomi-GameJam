using UnityEngine;

/// <summary>
/// Anything the player can walk up to and trigger with the interact button.
/// </summary>
public interface IInteractable
{
    /// <summary>False while the target is busy or already used up.</summary>
    bool CanInteract { get; }

    /// <summary>Short label a prompt UI can show, e.g. "Talk".</summary>
    string Prompt { get; }

    Transform Transform { get; }

    void Interact(BaseInteractionController interactor);
}
