using UnityEngine;

/// <summary>
/// One dialogue entry: who is talking, which portrait to show and what is said.
/// </summary>
[System.Serializable]
public class DialogueLine
{
    [Tooltip("Name shown in Dialogue.Name")]
    public string CharName;

    [Tooltip("Portrait shown in Dialogue.Char")]
    public Sprite CharSprite;

    [TextArea(3, 8)]
    [Tooltip("Sentence shown in Dialogue.Content")]
    public string Sentence;
}

/// <summary>
/// Asset holding a whole conversation. Assign it to a BaseDialogueController.
/// </summary>
[CreateAssetMenu(fileName = "DialogueContent", menuName = "Dialogue/Dialogue Content")]
public class DialogueContent : ScriptableObject
{
    [SerializeField] private DialogueLine[] lines;

    public DialogueLine[] Lines => lines;

    public int Count => lines != null ? lines.Length : 0;

    public DialogueLine GetLine(int index)
    {
        if (index < 0 || index >= Count)
            return null;

        return lines[index];
    }
}
