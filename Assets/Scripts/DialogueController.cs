using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Default dialogue controller: advances the dialogue on submit / click.
/// </summary>
public class DialogueController : BaseDialogueController
{
    [Header("Input")]
    [SerializeField] private bool advanceOnSubmit = true;
    [SerializeField] private bool advanceOnClick = true;

    private void Update()
    {
        if (!IsRunning)
            return;

        if (AdvancePressed())
            Advance();
    }

    private bool AdvancePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (advanceOnSubmit && Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.eKey.wasPressedThisFrame)
                return true;
        }

        if (advanceOnClick && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;
#else
        if (advanceOnSubmit &&
            (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.E)))
            return true;

        if (advanceOnClick && Input.GetMouseButtonDown(0))
            return true;
#endif
        return false;
    }
}
