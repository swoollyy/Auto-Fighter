using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dialogue box UI: speaker name, dialogue text (supports TMP rich text and optional typewriter).
/// Assign this component to the same GameObject that has the dialogue panel, or to a child.
/// Wire up the fields in the Inspector. Enable "Rich Text" on your TMP_Text components for bold/italic/color/size tags.
/// Typewriter reveals by vertex alpha so link-tag effects stay stable (no mesh rebuild each frame).
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
[DefaultExecutionOrder(300)] // After TMP link effects (0, 50) so we mask unrevealed chars last
public class DialogueUI : MonoBehaviour
{
    [Header("Text")]
    [SerializeField] private TMP_Text speakerText;
    [Tooltip("Supports TextMeshPro rich text: <b>, <i>, <color=#RRGGBB>, <size=24>, etc. Enable 'Rich Text' on this component.")]
    [SerializeField] private TMP_Text dialogueText;

    [Header("Typewriter (optional)")]
    [Tooltip("Reveal dialogue character-by-character. Works with rich text (bold, colors, etc.).")]
    [SerializeField] private bool useTypewriterEffect;
    [Tooltip("Characters revealed per second (unscaled).")]
    [SerializeField, Min(10f)] private float typewriterCharsPerSecond = 60f;

    [Header("Portrait (optional)")]
    [SerializeField] private Image portraitImage;
    [Tooltip("Sprite to use when no portrait is set for a line.")]
    [SerializeField] private Sprite defaultPortrait;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [Tooltip("If null, we use this GameObject. Used to show/hide the whole dialogue box.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Advance hint (optional)")]
    [SerializeField] private GameObject advanceHintObject;
    [SerializeField] private TMP_Text advanceHintText;
    [Tooltip("e.g. \"Space to continue\"")]
    [SerializeField] private string advanceHintString = "Space to continue";

    private Coroutine _typewriterRoutine;
    private bool _typewriterComplete;
    /// <summary>When using typewriter, we reveal by vertex alpha; this is the number of characters currently visible. Mesh is built once (full text) so link effects don't restart.</summary>
    private int _visibleCharacterCount;

    /// <summary>True when the current line is fully revealed (or when typewriter is disabled).</summary>
    public bool IsTypewriterComplete => !useTypewriterEffect || _typewriterComplete;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (panelRoot == null)
            panelRoot = gameObject;
        if (advanceHintText != null && !string.IsNullOrEmpty(advanceHintString))
            advanceHintText.text = advanceHintString;
        // Hide in Awake so we don't run after NarrativeDirector.Start() has already called Show().
        Hide();
    }

    /// <summary>Show the dialogue panel (and optionally set interactable/blockRaycasts).</summary>
    public void Show()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
        if (advanceHintObject != null)
            advanceHintObject.SetActive(true);
    }

    /// <summary>Hide the dialogue panel.</summary>
    public void Hide()
    {
        if (_typewriterRoutine != null)
        {
            StopCoroutine(_typewriterRoutine);
            _typewriterRoutine = null;
        }
        _typewriterComplete = true;
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    /// <summary>Set the current line content (speaker, text, optional portrait). Text supports TMP rich text tags.</summary>
    public void SetLine(string speakerName, string text, Sprite portrait = null)
    {
        if (speakerText != null)
            speakerText.text = string.IsNullOrEmpty(speakerName) ? "" : speakerName;

        if (dialogueText != null)
        {
            dialogueText.text = text ?? "";
            dialogueText.maxVisibleCharacters = int.MaxValue; // Full mesh once so link/effect layout is stable
            dialogueText.ForceMeshUpdate(true, true);

            if (useTypewriterEffect)
            {
                _typewriterComplete = false;
                _visibleCharacterCount = 0;
                if (_typewriterRoutine != null)
                    StopCoroutine(_typewriterRoutine);
                _typewriterRoutine = StartCoroutine(TypewriterRevealRoutine());
            }
            else
            {
                _visibleCharacterCount = int.MaxValue;
                _typewriterComplete = true;
            }
        }
        else
        {
            _typewriterComplete = true;
        }

        if (portraitImage != null)
        {
            Sprite s = portrait != null ? portrait : defaultPortrait;
            portraitImage.gameObject.SetActive(s != null);
            if (s != null)
                portraitImage.sprite = s;
        }
    }

    /// <summary>If typewriter is still revealing, reveal all immediately. Call from DialogueManager when player advances.</summary>
    public void SkipTypewriter()
    {
        if (!useTypewriterEffect || _typewriterComplete) return;
        if (_typewriterRoutine != null)
        {
            StopCoroutine(_typewriterRoutine);
            _typewriterRoutine = null;
        }
        if (dialogueText != null)
        {
            _visibleCharacterCount = dialogueText.textInfo.characterCount;
            dialogueText.ForceMeshUpdate(true, true); // Restore full mesh/alpha so link effects show on all text
        }
        _typewriterComplete = true;
    }

    /// <summary>Set vertex alpha to 0 for characters >= _visibleCharacterCount so link effects see a stable mesh.</summary>
    private void ApplyTypewriterAlphaMask()
    {
        if (dialogueText == null) return;
        TMP_TextInfo textInfo = dialogueText.textInfo;
        int characterCount = textInfo.characterCount;
        for (int i = _visibleCharacterCount; i < characterCount; i++)
        {
            TMP_CharacterInfo ch = textInfo.characterInfo[i];
            if (!ch.isVisible) continue;
            int matIndex = ch.materialReferenceIndex;
            int vertexIndex = ch.vertexIndex;
            Color32[] colors = textInfo.meshInfo[matIndex].colors32;
            if (colors == null || vertexIndex + 3 >= colors.Length) continue;
            byte a0 = 0;
            colors[vertexIndex + 0].a = a0;
            colors[vertexIndex + 1].a = a0;
            colors[vertexIndex + 2].a = a0;
            colors[vertexIndex + 3].a = a0;
        }
        dialogueText.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    private void LateUpdate()
    {
        if (!useTypewriterEffect || _typewriterComplete || dialogueText == null) return;
        // Do NOT ForceMeshUpdate here – it runs after link effects and would wipe their vertex changes.
        // Only mask unrevealed characters so effects stay visible on the revealed portion.
        ApplyTypewriterAlphaMask();
    }

    private IEnumerator TypewriterRevealRoutine()
    {
        if (dialogueText == null) yield break;
        int total = dialogueText.textInfo.characterCount;
        if (total == 0)
        {
            _typewriterComplete = true;
            _typewriterRoutine = null;
            yield break;
        }
        float revealed = 0f;
        while (revealed < total)
        {
            revealed += typewriterCharsPerSecond * Time.unscaledDeltaTime;
            _visibleCharacterCount = Mathf.Min(Mathf.FloorToInt(revealed), total);
            yield return null;
        }
        _visibleCharacterCount = total;
        _typewriterComplete = true;
        _typewriterRoutine = null;
    }
}
