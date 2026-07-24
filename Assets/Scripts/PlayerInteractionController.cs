using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Interaction controller driven by a single rebindable key.
/// </summary>
public class PlayerInteractionController : BaseInteractionController
{
#if ENABLE_INPUT_SYSTEM
    [Header("Input")]
    [Tooltip("Key that triggers the interaction. Change it here, nothing else to rewire")]
    [SerializeField] private Key interactKey = Key.E;

    protected override bool InteractPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || interactKey == Key.None)
            return false;

        return keyboard[interactKey].wasPressedThisFrame;
    }
#else
    [Header("Input")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    protected override bool InteractPressed()
    {
        return Input.GetKeyDown(interactKey);
    }
#endif
}
