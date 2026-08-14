using System.Collections;
using System.Collections.Generic;
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

    [Header("Per-phrase speed (via <link> tags)")]
    [Tooltip("Speed multiplier for <link=\"slow\">...</link>. <1 = slower, >1 = faster.")]
    [SerializeField, Min(0.05f)] private float slowMultiplier = 0.35f;
    [Tooltip("Speed multiplier for <link=\"fast\">...</link>.")]
    [SerializeField, Min(0.05f)] private float fastMultiplier = 2.5f;
    [Tooltip("Speed multiplier for <link=\"pause\">...</link>. Use this on short spans (a comma, ellipsis) for dramatic beats.")]
    [SerializeField, Min(0.01f)] private float pauseMultiplier = 0.12f;
    [Tooltip("Per-link extra delay (seconds) applied BEFORE revealing the span. Use with <link=\"hold:0.5\">...</link> for custom beats.")]
    [SerializeField] private bool supportCustomSpeedAndHoldTags = true;

    [Header("Portrait (optional)")]
    [SerializeField] private Image portraitImage;
    [Tooltip("Sprite to use when no portrait is set for a line.")]
    [SerializeField] private Sprite defaultPortrait;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [Tooltip("If null, we use this GameObject. Used to show/hide the whole dialogue box.")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("Full-screen dim Image on the dialogue canvas (Background Panel). Must stay on a GO that can keep the Dialogue Box as a child — we only toggle the Image.")]
    [SerializeField] private Image screenDimImage;

    [Header("Dialogue Box Blob FX")]
    [Tooltip("First layered goo panel (Dialogue Box FX 1). Leave empty to auto-find children in hierarchy order.")]
    [SerializeField] private UIGooSlimeAnimator gooSlimePanel1;
    [Tooltip("Second layered goo panel (Dialogue Box FX 2). Leave empty to auto-find children in hierarchy order.")]
    [SerializeField] private UIGooSlimeAnimator gooSlimePanel2;
    [Tooltip("Seconds to blend blob colors when the speaker tag changes within a sequence (unscaled). First line of a sequence snaps.")]
    [SerializeField, Min(0f)] private float blobColorBlendSeconds = 0.45f;
    [Tooltip("After a speaker color snap (first line), wait this long before typewriter so the new box color reads first.")]
    [SerializeField, Min(0f)] private float blobColorSnapLeadInSeconds = 0.12f;
    [Tooltip("When speaker colors blend, wait for the blend to finish before dialogue text starts rolling.")]
    [SerializeField] private bool waitForBlobColorBeforeTypewriter = true;

    [Header("Advance hint (optional)")]
    // Touch: remount inspector mapping for advance hint root / text refs.
    [Tooltip("Root to show/hide when the line is ready to advance. If empty, advanceHintText's GameObject is used.")]
    [SerializeField] private GameObject advanceHintObject;
    [SerializeField] private TMP_Text advanceHintText;
    [Tooltip("e.g. \"Space to continue\"")]
    [SerializeField] private string advanceHintString = "Space to continue";
    [Tooltip("Seconds to wait after the typewriter finishes before showing the advance hint (unscaled).")]
    [SerializeField, Min(0f)] private float advanceHintAppearDelaySeconds = 0.35f;
    [Header("Input safety")]
    [Tooltip("Ignore skip/advance input for a short time right after setting a line. Helps prevent first-line auto-skip from startup clicks/keypresses.")]
    [SerializeField, Min(0f)] private float inputGraceSecondsAfterSetLine = 0.1f;

    private Coroutine _typewriterRoutine;
    private Coroutine _blobColorLeadInRoutine;
    private Coroutine _advanceHintRoutine;
    private bool _typewriterComplete;
    private bool _waitingForBlobColorLeadIn;
    /// <summary>When using typewriter, we reveal by vertex alpha; this is the number of characters currently visible. Mesh is built once (full text) so link effects don't restart.</summary>
    private int _visibleCharacterCount;
    /// <summary>Per-character speed multiplier (1.0 = base rate). Built from <link> tags each time a line is set.</summary>
    private float[] _charSpeedMultipliers;
    /// <summary>Per-character extra hold (seconds) applied BEFORE revealing that character. Built from <link="hold:X"> tags.</summary>
    private float[] _charHoldSeconds;
    private float _lineSetAtUnscaledTime;
    /// <summary>False until the first blob colors of the current sequence are applied (that apply snaps; later ones blend).</summary>
    private bool _blobColorsAppliedThisSequence;
    private Color _lastBlob1Fill;
    private Color _lastBlob1Rim;
    private Color _lastBlob2Fill;
    private Color _lastBlob2Rim;
    private bool _hasLastBlobColors;

    /// <summary>True when the current line is fully revealed (or when typewriter is disabled).</summary>
    public bool IsTypewriterComplete =>
        !_waitingForBlobColorLeadIn && (!useTypewriterEffect || _typewriterComplete);
    /// <summary>True once the per-line input grace window has elapsed.</summary>
    public bool CanAcceptAdvanceInput => Time.unscaledTime >= _lineSetAtUnscaledTime + inputGraceSecondsAfterSetLine;

    /// <summary>Root used to host tutorial cutouts above the screen dim but under dialogue text.</summary>
    public RectTransform OverlayHost
    {
        get
        {
            GameObject root = panelRoot != null ? panelRoot : gameObject;
            return root != null ? root.transform as RectTransform : null;
        }
    }

    /// <summary>Dialogue canvas (sort order / parenting for tutorial pierce-through).</summary>
    public Canvas HostCanvas => GetComponent<Canvas>();

    /// <summary>
    /// Toggle only the full-screen dim Image. Never deactivates the GameObject —
    /// the Dialogue Box goo lives as a child of that same object.
    /// </summary>
    public void SetScreenDimVisible(bool visible)
    {
        EnsureScreenDimImage();
        if (screenDimImage == null) return;

        screenDimImage.enabled = visible;
        if (visible)
        {
            // Guarantee the curtain reads as a dim again if something cleared alpha while hidden.
            Color c = screenDimImage.color;
            if (c.a < 0.5f)
            {
                c.a = 0.95686275f;
                screenDimImage.color = c;
            }
        }
    }

    private void EnsureScreenDimImage()
    {
        if (screenDimImage != null) return;
        Transform root = panelRoot != null ? panelRoot.transform : transform;
        Transform bg = root.Find("Background Panel");
        if (bg != null)
            screenDimImage = bg.GetComponent<Image>();
    }

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (panelRoot == null)
            panelRoot = gameObject;
        EnsureScreenDimImage();
        if (advanceHintText != null && !string.IsNullOrEmpty(advanceHintString))
            advanceHintText.text = advanceHintString;
        EnsureGooSlimePanels();
        // Hide in Awake so we don't run after NarrativeDirector.Start() has already called Show().
        Hide();
    }

    /// <summary>Show the dialogue panel (and optionally set interactable/blockRaycasts).</summary>
    public void Show()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);
        // Sit above GooIrisScreenTransition (32000) so boot/load goo never buries dialogue.
        Canvas host = HostCanvas;
        if (host != null)
        {
            host.overrideSorting = true;
            if (host.sortingOrder < 32150)
                host.sortingOrder = 32150;
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
        // Default: full-screen dim on. Tutorial view-only highlights may hide it temporarily.
        if (!TutorialUIHighlightCoach.ShouldSuppressDialogueScreenDim)
            SetScreenDimVisible(true);
        else
            SetScreenDimVisible(false);
        // Hint stays hidden until the current line finishes typewriter reveal.
        HideAdvanceHint();
    }

    /// <summary>Hide the dialogue panel.</summary>
    public void Hide()
    {
        StopBlobColorLeadIn();
        if (_typewriterRoutine != null)
        {
            StopCoroutine(_typewriterRoutine);
            _typewriterRoutine = null;
        }
        _typewriterComplete = true;
        _waitingForBlobColorLeadIn = false;
        _blobColorsAppliedThisSequence = false;
        _hasLastBlobColors = false;
        EnsureGooSlimePanels();
        gooSlimePanel1?.ResetBlobColorState();
        gooSlimePanel2?.ResetBlobColorState();
        HideAdvanceHint();
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
        SetLine(speakerName, text, portrait, null, null, null, null);
    }

    /// <summary>
    /// Set the current line content and recolor both layered Dialogue Box FX goo panels.
    /// Pass null for a color pair to leave that panel's current tint unchanged.
    /// Speaker colors apply first; typewriter waits until the color lead-in finishes.
    /// </summary>
    public void SetLine(
        string speakerName,
        string text,
        Sprite portrait,
        Color? blob1FillColor,
        Color? blob1RimColor,
        Color? blob2FillColor,
        Color? blob2RimColor)
    {
        StopBlobColorLeadIn();
        if (_typewriterRoutine != null)
        {
            StopCoroutine(_typewriterRoutine);
            _typewriterRoutine = null;
        }

        if (speakerText != null)
            speakerText.text = string.IsNullOrEmpty(speakerName) ? "" : speakerName;

        bool hasBlobColors =
            (blob1FillColor.HasValue && blob1RimColor.HasValue) ||
            (blob2FillColor.HasValue && blob2RimColor.HasValue);

        float colorLeadInSeconds = 0f;
        if (hasBlobColors)
            colorLeadInSeconds = ApplyBlobColorsAndGetLeadIn(
                blob1FillColor, blob1RimColor, blob2FillColor, blob2RimColor);

        if (portraitImage != null)
        {
            Sprite s = portrait != null ? portrait : defaultPortrait;
            portraitImage.gameObject.SetActive(s != null);
            if (s != null)
                portraitImage.sprite = s;
        }

        if (dialogueText != null)
        {
            dialogueText.text = text ?? "";
            dialogueText.maxVisibleCharacters = int.MaxValue; // Full mesh once so link/effect layout is stable
            dialogueText.ForceMeshUpdate(true, true);
            // Kill leftover quads from a longer previous line (jittered '*' ghosts, etc.).
            TMPEffectCoordinator.ClearUnusedMeshVertices(dialogueText.textInfo);

            if (useTypewriterEffect)
            {
                _typewriterComplete = false;
                _visibleCharacterCount = 0;
                _lineSetAtUnscaledTime = Time.unscaledTime;
                BuildPerCharacterTimingTables();
                HideAdvanceHint();

                if (waitForBlobColorBeforeTypewriter && colorLeadInSeconds > 0f)
                {
                    _waitingForBlobColorLeadIn = true;
                    _blobColorLeadInRoutine = StartCoroutine(BlobColorLeadInThenTypewriter(colorLeadInSeconds));
                }
                else
                {
                    _waitingForBlobColorLeadIn = false;
                    _typewriterRoutine = StartCoroutine(TypewriterRevealRoutine());
                }
            }
            else
            {
                _visibleCharacterCount = int.MaxValue;
                _typewriterComplete = true;
                _waitingForBlobColorLeadIn = false;
                ScheduleAdvanceHintAppear();
            }
        }
        else
        {
            _typewriterComplete = true;
            _waitingForBlobColorLeadIn = false;
            ScheduleAdvanceHintAppear();
        }
    }

    /// <summary>
    /// Push fill + rim colors to Dialogue Box FX 1 and FX 2.
    /// First apply in a sequence snaps; later applies blend over <see cref="blobColorBlendSeconds"/>.
    /// </summary>
    public void ApplyBlobColors(Color? fill1, Color? rim1, Color? fill2, Color? rim2)
    {
        ApplyBlobColorsAndGetLeadIn(fill1, rim1, fill2, rim2);
    }

    private float ApplyBlobColorsAndGetLeadIn(Color? fill1, Color? rim1, Color? fill2, Color? rim2)
    {
        EnsureGooSlimePanels();

        bool colorsChanged = !_hasLastBlobColors ||
            (fill1.HasValue && rim1.HasValue && !ApproximatelyColor(fill1.Value, _lastBlob1Fill)) ||
            (fill1.HasValue && rim1.HasValue && !ApproximatelyColor(rim1.Value, _lastBlob1Rim)) ||
            (fill2.HasValue && rim2.HasValue && !ApproximatelyColor(fill2.Value, _lastBlob2Fill)) ||
            (fill2.HasValue && rim2.HasValue && !ApproximatelyColor(rim2.Value, _lastBlob2Rim));

        bool immediate = !_blobColorsAppliedThisSequence;
        _blobColorsAppliedThisSequence = true;
        float duration = blobColorBlendSeconds;

        if (gooSlimePanel1 != null && fill1.HasValue && rim1.HasValue)
        {
            gooSlimePanel1.SetBlobColors(fill1.Value, rim1.Value, immediate, duration);
            _lastBlob1Fill = fill1.Value;
            _lastBlob1Rim = rim1.Value;
        }
        if (gooSlimePanel2 != null && fill2.HasValue && rim2.HasValue)
        {
            gooSlimePanel2.SetBlobColors(fill2.Value, rim2.Value, immediate, duration);
            _lastBlob2Fill = fill2.Value;
            _lastBlob2Rim = rim2.Value;
        }
        _hasLastBlobColors = true;

        if (!waitForBlobColorBeforeTypewriter || !colorsChanged)
            return 0f;
        if (immediate)
            return blobColorSnapLeadInSeconds;
        return duration;
    }

    private static bool ApproximatelyColor(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.002f
            && Mathf.Abs(a.g - b.g) < 0.002f
            && Mathf.Abs(a.b - b.b) < 0.002f
            && Mathf.Abs(a.a - b.a) < 0.002f;
    }

    private IEnumerator BlobColorLeadInThenTypewriter(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        _waitingForBlobColorLeadIn = false;
        _blobColorLeadInRoutine = null;
        if (useTypewriterEffect && !_typewriterComplete)
            _typewriterRoutine = StartCoroutine(TypewriterRevealRoutine());
    }

    private void StopBlobColorLeadIn()
    {
        if (_blobColorLeadInRoutine == null) return;
        StopCoroutine(_blobColorLeadInRoutine);
        _blobColorLeadInRoutine = null;
        _waitingForBlobColorLeadIn = false;
    }

    private void EnsureGooSlimePanels()
    {
        if (gooSlimePanel1 != null && gooSlimePanel2 != null)
            return;

        UIGooSlimeAnimator[] found = GetComponentsInChildren<UIGooSlimeAnimator>(true);
        if (found == null || found.Length == 0)
            return;

        if (gooSlimePanel1 == null && found.Length > 0)
            gooSlimePanel1 = found[0];
        if (gooSlimePanel2 == null && found.Length > 1)
            gooSlimePanel2 = found[1];
    }

    /// <summary>If typewriter is still revealing (or color lead-in is pending), reveal all immediately.</summary>
    public void SkipTypewriter()
    {
        if (!useTypewriterEffect && !_waitingForBlobColorLeadIn) return;
        if (_typewriterComplete && !_waitingForBlobColorLeadIn) return;

        StopBlobColorLeadIn();
        EnsureGooSlimePanels();
        gooSlimePanel1?.CompleteColorBlendImmediate();
        gooSlimePanel2?.CompleteColorBlendImmediate();

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
        ScheduleAdvanceHintAppear();
    }

    private void HideAdvanceHint()
    {
        if (_advanceHintRoutine != null)
        {
            StopCoroutine(_advanceHintRoutine);
            _advanceHintRoutine = null;
        }
        SetAdvanceHintVisible(false);
    }

    private void ScheduleAdvanceHintAppear()
    {
        if (_advanceHintRoutine != null)
        {
            StopCoroutine(_advanceHintRoutine);
            _advanceHintRoutine = null;
        }

        if (advanceHintAppearDelaySeconds <= 0f || !isActiveAndEnabled)
        {
            SetAdvanceHintVisible(true);
            return;
        }

        SetAdvanceHintVisible(false);
        _advanceHintRoutine = StartCoroutine(AdvanceHintAppearAfterDelay());
    }

    private IEnumerator AdvanceHintAppearAfterDelay()
    {
        float t = 0f;
        float delay = advanceHintAppearDelaySeconds;
        while (t < delay)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        _advanceHintRoutine = null;
        SetAdvanceHintVisible(true);
    }

    private void SetAdvanceHintVisible(bool visible)
    {
        // Prefer the dedicated root when assigned; otherwise toggle the hint text itself
        // (scene often only wires advanceHintText).
        if (advanceHintObject != null)
            advanceHintObject.SetActive(visible);
        else if (advanceHintText != null)
            advanceHintText.gameObject.SetActive(visible);

        // If both are assigned and the text is not under the root, keep them in sync.
        if (advanceHintObject != null && advanceHintText != null
            && advanceHintText.transform != advanceHintObject.transform
            && !advanceHintText.transform.IsChildOf(advanceHintObject.transform))
        {
            advanceHintText.gameObject.SetActive(visible);
        }
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
        // Also keeps text hidden during the speaker color lead-in (_visibleCharacterCount stays 0).
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
            ScheduleAdvanceHintAppear();
            yield break;
        }

        float baseRate = Mathf.Max(1f, typewriterCharsPerSecond);
        int nextChar = 0;
        float timeDebt = 0f;
        float holdRemaining = GetHold(0);

        while (nextChar < total)
        {
            float dt = Time.unscaledDeltaTime;

            // Spend time on the pre-character hold first (used by <link="hold:X">).
            if (holdRemaining > 0f)
            {
                if (dt <= holdRemaining)
                {
                    holdRemaining -= dt;
                    yield return null;
                    continue;
                }
                dt -= holdRemaining;
                holdRemaining = 0f;
            }

            timeDebt += dt;
            float secondsPerChar = 1f / (baseRate * GetMultiplier(nextChar));
            while (nextChar < total && timeDebt >= secondsPerChar)
            {
                timeDebt -= secondsPerChar;
                nextChar++;
                if (nextChar < total)
                {
                    holdRemaining = GetHold(nextChar);
                    if (holdRemaining > 0f)
                    {
                        // Treat hold as a hard pacing break so queued reveal time from before the hold
                        // does not instantly dump characters after the hold ends.
                        timeDebt = 0f;
                        break; // Let the outer loop consume the hold next frame.
                    }
                    secondsPerChar = 1f / (baseRate * GetMultiplier(nextChar));
                }
            }

            _visibleCharacterCount = nextChar;
            yield return null;
        }

        _visibleCharacterCount = total;
        _typewriterComplete = true;
        _typewriterRoutine = null;
        ScheduleAdvanceHintAppear();
    }

    private float GetMultiplier(int charIndex)
    {
        if (_charSpeedMultipliers == null || charIndex < 0 || charIndex >= _charSpeedMultipliers.Length)
            return 1f;
        float m = _charSpeedMultipliers[charIndex];
        return m > 0f ? m : 1f;
    }

    private float GetHold(int charIndex)
    {
        if (_charHoldSeconds == null || charIndex < 0 || charIndex >= _charHoldSeconds.Length)
            return 0f;
        return _charHoldSeconds[charIndex];
    }

    /// <summary>
    /// Scan <see cref="TMP_TextInfo.linkInfo"/> and fill per-character speed/hold tables
    /// from recognized link IDs: "slow", "fast", "pause", "speed:X", "hold:X".
    /// </summary>
    private void BuildPerCharacterTimingTables()
    {
        if (dialogueText == null)
        {
            _charSpeedMultipliers = null;
            _charHoldSeconds = null;
            return;
        }

        TMP_TextInfo info = dialogueText.textInfo;
        int total = info != null ? info.characterCount : 0;

        if (_charSpeedMultipliers == null || _charSpeedMultipliers.Length < total)
            _charSpeedMultipliers = new float[Mathf.Max(total, 1)];
        if (_charHoldSeconds == null || _charHoldSeconds.Length < total)
            _charHoldSeconds = new float[Mathf.Max(total, 1)];
        for (int i = 0; i < total; i++)
        {
            _charSpeedMultipliers[i] = 1f;
            _charHoldSeconds[i] = 0f;
        }
        if (total == 0) return;

        List<TMPLinkEffectHelper.LinkRange> ranges = new List<TMPLinkEffectHelper.LinkRange>();
        TMPLinkEffectHelper.ParseAllLinkRanges(dialogueText.text, ranges);
        if (ranges.Count == 0) return;

        // Apply outer links first; nested inner links can override speed when they overlap.
        ranges.Sort((a, b) =>
        {
            if (a.start != b.start) return a.start.CompareTo(b.start);
            int aLen = a.end - a.start;
            int bLen = b.end - b.start;
            return bLen.CompareTo(aLen);
        });

        for (int l = 0; l < ranges.Count; l++)
        {
            TMPLinkEffectHelper.LinkRange range = ranges[l];
            string id = range.id;
            if (string.IsNullOrEmpty(id))
                continue;

            float multiplier;
            float holdSeconds;
            if (!TryResolveLinkTiming(id, out multiplier, out holdSeconds))
                continue;

            int first = Mathf.Max(0, range.start);
            int end = Mathf.Min(total, range.end);
            for (int c = first; c < end; c++)
            {
                if (multiplier > 0f) _charSpeedMultipliers[c] = multiplier;
            }
            if (holdSeconds > 0f)
            {
                int holdChar = FindFirstNonWhitespaceVisibleCharacter(first, end);
                if (holdChar >= 0 && holdChar < total)
                    _charHoldSeconds[holdChar] += holdSeconds;
            }
        }
    }

    /// <summary>
    /// Returns the first visible-character index in [start, end) whose source character is not whitespace.
    /// Falls back to start when no non-whitespace character exists in the range.
    /// </summary>
    private int FindFirstNonWhitespaceVisibleCharacter(int start, int end)
    {
        if (dialogueText == null || dialogueText.textInfo == null)
            return start;
        TMP_TextInfo info = dialogueText.textInfo;
        int lo = Mathf.Clamp(start, 0, info.characterCount);
        int hi = Mathf.Clamp(end, 0, info.characterCount);
        for (int i = lo; i < hi; i++)
        {
            char c = info.characterInfo[i].character;
            if (!char.IsWhiteSpace(c))
                return i;
        }
        return lo;
    }

    /// <summary>
    /// Parse a link ID into a speed multiplier and/or a pre-reveal hold.
    /// Supported IDs: "slow", "fast", "pause", "speed:0.3", "hold:0.5".
    /// Returns false for unrelated link IDs (so link effects / other systems keep working).
    /// </summary>
    private bool TryResolveLinkTiming(string id, out float multiplier, out float holdSeconds)
    {
        multiplier = 0f;
        holdSeconds = 0f;

        if (string.Equals(id, "slow", System.StringComparison.OrdinalIgnoreCase))
        { multiplier = slowMultiplier; return true; }
        if (string.Equals(id, "fast", System.StringComparison.OrdinalIgnoreCase))
        { multiplier = fastMultiplier; return true; }
        if (string.Equals(id, "pause", System.StringComparison.OrdinalIgnoreCase))
        { multiplier = pauseMultiplier; return true; }

        if (!supportCustomSpeedAndHoldTags) return false;

        int colon = id.IndexOf(':');
        if (colon <= 0 || colon >= id.Length - 1) return false;
        string key = id.Substring(0, colon);
        string val = id.Substring(colon + 1);
        if (!float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
            return false;

        if (string.Equals(key, "speed", System.StringComparison.OrdinalIgnoreCase))
        { multiplier = Mathf.Max(0.01f, f); return true; }
        if (string.Equals(key, "hold", System.StringComparison.OrdinalIgnoreCase))
        { holdSeconds = Mathf.Max(0f, f); return true; }

        return false;
    }
}
