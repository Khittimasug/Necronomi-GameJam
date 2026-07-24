using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a DialogueContent asset into the dialogue UI with a typewriter effect.
/// Input handling is left to the derived class.
/// </summary>
public abstract class BaseDialogueController : MonoBehaviour
{
    [Header("Dialogue Data")]
    [SerializeField] protected DialogueContent dialogueContent;

    [Header("UI References")]
    [SerializeField] protected TMP_Text nameText;
    [SerializeField] protected TMP_Text contentText;
    [SerializeField] protected Image charImage;
    [Tooltip("Faded in while a dialogue is running. Preferred over the object list below")]
    [SerializeField] protected CanvasGroup dialogueCanvasGroup;
    [Tooltip("Optional extra objects toggled on while a dialogue is running")]
    [SerializeField] protected GameObject[] dialogueUIObjects;

    [Header("Name Plate Auto Size")]
    [Tooltip("Background behind the name (Dialogue.BG.Name)")]
    [SerializeField] protected RectTransform namePlateRect;
    [SerializeField] protected bool autoSizeNamePlate = true;
    [Tooltip("Grow to the right only, instead of growing from the center")]
    [SerializeField] protected bool keepNamePlateLeftEdge = true;
    [Tooltip("Lower bound for the scale factor, so short names keep a readable plate")]
    [SerializeField, Min(0.01f)] protected float minNamePlateScale = 0.6f;

    [Header("Typing Effect")]
    [SerializeField, Min(1f)] protected float charactersPerSecond = 40f;
    [Tooltip("Extra pause after . ! ? , in seconds")]
    [SerializeField, Min(0f)] protected float punctuationPause = 0.12f;
    [SerializeField] protected bool hideCharImageWhenEmpty = true;

    [Header("Startup")]
    [SerializeField] protected bool playOnStart = true;

    public event Action OnDialogueStarted;
    public event Action OnDialogueFinished;
    public event Action<DialogueLine> OnLineShown;

    public bool IsRunning { get; private set; }
    public bool IsTyping { get; private set; }
    public int CurrentIndex { get; private set; } = -1;
    public DialogueContent Content => dialogueContent;

    private Coroutine typingRoutine;

    // Sizes authored in the scene, used as the 1.0 reference for the name plate.
    private Vector2 baseNameSize;
    private Vector2 basePlateSize;
    private Vector2 basePlatePosition;
    private float baseNameTextWidth;
    private bool namePlateBaseCaptured;
    private int startedFrame = -1;

    protected virtual void Awake()
    {
        CaptureNamePlateBase();
        SetUIVisible(false);
    }

    protected virtual void Start()
    {
        if (playOnStart && dialogueContent != null)
            StartDialogue(dialogueContent);
    }

    /// <summary>Starts the dialogue assigned in the inspector.</summary>
    public void StartDialogue()
    {
        StartDialogue(dialogueContent);
    }

    public virtual void StartDialogue(DialogueContent content)
    {
        if (content == null || content.Count == 0)
        {
            Debug.LogWarning($"{name}: no DialogueContent to play.", this);
            return;
        }

        dialogueContent = content;
        CurrentIndex = -1;
        IsRunning = true;
        startedFrame = Time.frameCount;

        SetUIVisible(true);
        OnDialogueStarted?.Invoke();

        ShowLine(0);
    }

    /// <summary>
    /// While typing: reveal the whole sentence. Otherwise: go to the next line,
    /// or end the dialogue when there is none left.
    /// </summary>
    public virtual void Advance()
    {
        if (!IsRunning)
            return;

        // The same key press that started the dialogue must not also advance it.
        if (Time.frameCount == startedFrame)
            return;

        if (IsTyping)
        {
            CompleteTyping();
            return;
        }

        int next = CurrentIndex + 1;
        if (next >= dialogueContent.Count)
        {
            EndDialogue();
            return;
        }

        ShowLine(next);
    }

    public virtual void EndDialogue()
    {
        StopTypingRoutine();

        IsRunning = false;
        IsTyping = false;
        CurrentIndex = -1;

        SetUIVisible(false);
        OnDialogueFinished?.Invoke();
    }

    protected virtual void ShowLine(int index)
    {
        DialogueLine line = dialogueContent.GetLine(index);
        if (line == null)
        {
            EndDialogue();
            return;
        }

        CurrentIndex = index;

        if (nameText != null)
        {
            nameText.text = line.CharName;
            ResizeNamePlate(line.CharName);
        }

        if (charImage != null)
        {
            charImage.sprite = line.CharSprite;

            if (hideCharImageWhenEmpty)
                charImage.enabled = line.CharSprite != null;
        }

        StopTypingRoutine();
        typingRoutine = StartCoroutine(TypeRoutine(line.Sentence));

        OnLineShown?.Invoke(line);
    }

    /// <summary>
    /// Records the sizes authored in the scene. Whatever name is in the field at
    /// edit time is the reference: its width maps to scale 1.0.
    /// </summary>
    private void CaptureNamePlateBase()
    {
        if (nameText == null)
            return;

        baseNameSize = nameText.rectTransform.sizeDelta;
        baseNameTextWidth = nameText.GetPreferredValues(nameText.text).x;

        if (namePlateRect != null)
        {
            basePlateSize = namePlateRect.sizeDelta;
            basePlatePosition = namePlateRect.anchoredPosition;
        }

        namePlateBaseCaptured = baseNameTextWidth > 0.01f;

        if (!namePlateBaseCaptured && autoSizeNamePlate)
            Debug.LogWarning($"{name}: Dialogue.Name is empty in the scene, so it cannot be used as the auto size reference.", this);
    }

    /// <summary>
    /// Scales Dialogue.Name and Dialogue.BG.Name by how wide the name renders,
    /// keeping the plate/text width ratio authored in the scene. Heights are untouched.
    /// </summary>
    protected virtual void ResizeNamePlate(string charName)
    {
        if (!autoSizeNamePlate || !namePlateBaseCaptured)
            return;

        float width = nameText.GetPreferredValues(charName).x;
        float scale = Mathf.Max(minNamePlateScale, width / baseNameTextWidth);

        nameText.rectTransform.sizeDelta = new Vector2(baseNameSize.x * scale, baseNameSize.y);

        if (namePlateRect == null)
            return;

        float plateWidth = basePlateSize.x * scale;
        namePlateRect.sizeDelta = new Vector2(plateWidth, basePlateSize.y);

        if (keepNamePlateLeftEdge)
        {
            float shift = (plateWidth - basePlateSize.x) * namePlateRect.pivot.x;
            namePlateRect.anchoredPosition = new Vector2(basePlatePosition.x + shift, basePlatePosition.y);
        }
    }

    /// <summary>Reveals the current sentence instantly.</summary>
    public void CompleteTyping()
    {
        if (!IsTyping)
            return;

        StopTypingRoutine();

        if (contentText != null)
            contentText.maxVisibleCharacters = int.MaxValue;

        IsTyping = false;
    }

    private IEnumerator TypeRoutine(string sentence)
    {
        if (contentText == null)
        {
            IsTyping = false;
            yield break;
        }

        IsTyping = true;

        // Assign the full text first so rich text tags and word wrapping are
        // resolved once, then reveal it character by character.
        contentText.text = sentence;
        contentText.maxVisibleCharacters = 0;
        contentText.ForceMeshUpdate();

        int total = contentText.textInfo.characterCount;
        float delay = 1f / Mathf.Max(1f, charactersPerSecond);

        for (int i = 1; i <= total; i++)
        {
            contentText.maxVisibleCharacters = i;

            yield return new WaitForSeconds(delay);

            if (punctuationPause > 0f && IsPause(contentText.textInfo.characterInfo[i - 1].character))
                yield return new WaitForSeconds(punctuationPause);
        }

        contentText.maxVisibleCharacters = int.MaxValue;
        IsTyping = false;
        typingRoutine = null;
    }

    private static bool IsPause(char c)
    {
        return c == '.' || c == ',' || c == '!' || c == '?' || c == ':' || c == ';';
    }

    private void StopTypingRoutine()
    {
        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }
    }

    protected void SetUIVisible(bool visible)
    {
        if (dialogueCanvasGroup != null)
        {
            dialogueCanvasGroup.alpha = visible ? 1f : 0f;
            dialogueCanvasGroup.interactable = visible;
            dialogueCanvasGroup.blocksRaycasts = visible;
        }

        if (dialogueUIObjects == null)
            return;

        for (int i = 0; i < dialogueUIObjects.Length; i++)
        {
            if (dialogueUIObjects[i] != null)
                dialogueUIObjects[i].SetActive(visible);
        }
    }
}
