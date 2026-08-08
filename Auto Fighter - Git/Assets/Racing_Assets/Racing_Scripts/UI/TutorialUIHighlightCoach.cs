using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runs <see cref="TutorialUIHighlightStepSO"/> beats: dim UI with a hole over a target,
/// yellow bobbing outline. Skill-node steps force a click; view-only steps (e.g. cost)
/// persist until their bound dialogue ends.
/// Add more steps in the Inspector — no new code needed for additional skill-node spotlights.
/// </summary>
public class TutorialUIHighlightCoach : MonoBehaviour
{
    public static TutorialUIHighlightCoach Instance { get; private set; }

    [SerializeField] private TutorialUIHighlightStepSO[] steps = new TutorialUIHighlightStepSO[0];
    [SerializeField] private RacingSkillUI skillUI;
    [SerializeField] private GameObject gameCanvasRoot;
    [SerializeField] private bool verboseDebug = true;

    [Header("Overlay sort")]
    [Tooltip("Sibling index under game canvas — high so it sits above skill tree chrome.")]
    [SerializeField] private int overlaySiblingIndex = 999;

    private TutorialUIHighlightStepSO _activeStep;
    private RacingSkillUIEntry _activeEntry;
    private RectTransform _activeTargetRt;
    private RectTransform _overlayRoot;
    private RectTransform _dimLeft;
    private RectTransform _dimRight;
    private RectTransform _dimTop;
    private RectTransform _dimBottom;
    private RectTransform _outline;
    private Image _outlineColorSource;
    private Image _dimLeftImage;
    private Image _dimRightImage;
    private Image _dimTopImage;
    private Image _dimBottomImage;
    private RectTransform _edgeL;
    private RectTransform _edgeR;
    private RectTransform _edgeB;
    private RectTransform _edgeT;
    private Image _edgeLImage;
    private Image _edgeRImage;
    private Image _edgeBImage;
    private Image _edgeTImage;
    private RectTransform _bridgeDim;
    private Image _bridgeDimImage;
    private Coroutine _resolveRoutine;
    private bool _subscribed;
    private Vector2 _outlineBaseSize;
    private float _outlineThicknessScaled = 6f;
    private float _outlineBobAmplitudeScaled = 8f;
    private bool _cutoutReady;
    private Transform _overlayDefaultParent;
    private bool _mountedOnDialogueCanvas;
    private Canvas _boostedDialogueCanvas;
    private int _dialogueCanvasSavedSortOrder;
    private const int DialogueCanvasSortBoost = 50;
    private RectTransform _liftedTargetRt;
    private Canvas _liftedTargetCanvas;
    private bool _liftedTargetCanvasAdded;
    private bool _liftedTargetOverrideWasEnabled;
    private int _liftedTargetSavedSortOrder;
    private GraphicRaycaster _liftedTargetRaycaster;
    private bool _liftedTargetRaycasterAdded;

    public static bool IsHighlightActive =>
        Instance != null && Instance._activeStep != null;

    /// <summary>View-only dialogue callouts own the screen dim — DialogueUI must not force it back on.</summary>
    public static bool ShouldSuppressDialogueScreenDim =>
        Instance != null
        && Instance._activeStep != null
        && Instance._activeStep.IsViewOnly
        && Instance._mountedOnDialogueCanvas;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        GameplayUIInputGuard.IsTutorialHighlightActive = false;

        if (skillUI == null)
            skillUI = FindObjectOfType<RacingSkillUI>(true);

        if (gameCanvasRoot == null)
        {
            var ui = FindObjectOfType<UIManager_Racing>(true);
            if (ui != null)
                gameCanvasRoot = ui.GameCanvas;
        }
    }

    private void OnEnable() => SubscribeDialogue();

    private void OnDisable()
    {
        UnsubscribeDialogue();
        ClearActiveHighlight();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        UnsubscribeDialogue();
        ClearActiveHighlight();
    }

    private void Update()
    {
        if (!_subscribed)
            SubscribeDialogue();
    }

    private void LateUpdate()
    {
        if (_activeStep == null || _overlayRoot == null)
            return;
        if (!_overlayRoot.gameObject.activeSelf)
            return;

        // Show() or other systems may re-enable the solid dialogue dim — keep it off for view-only.
        if (_mountedOnDialogueCanvas && _activeStep.IsViewOnly)
        {
            var dialogueUI = FindObjectOfType<DialogueUI>(true);
            dialogueUI?.SetScreenDimVisible(false);
        }

        if (!_cutoutReady || _activeTargetRt == null)
            return;

        LayoutCutoutAroundTarget();
        AnimateOutlineBob();
    }

    private void SubscribeDialogue()
    {
        if (_subscribed) return;
        if (DialogueManager.Instance == null) return;
        // Completing fires before dialogue Hide — mount dim while dialogue panel still covers the tree.
        DialogueManager.Instance.OnSequenceCompleting += HandleSequenceCompleting;
        DialogueManager.Instance.OnSequenceStarted += HandleSequenceStarted;
        DialogueManager.Instance.OnLinePresented += HandleLinePresented;
        _subscribed = true;
    }

    private void UnsubscribeDialogue()
    {
        if (!_subscribed) return;
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.OnSequenceCompleting -= HandleSequenceCompleting;
            DialogueManager.Instance.OnSequenceStarted -= HandleSequenceStarted;
            DialogueManager.Instance.OnLinePresented -= HandleLinePresented;
        }
        _subscribed = false;
    }

    private void HandleSequenceStarted(DialogueSequenceSO started)
    {
        if (started == null || steps == null) return;
        if (_activeStep != null) return;

        for (int i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            if (step == null || step.startWhenSequenceStarts == null) continue;
            if (step.startWhenSequenceStarts != started) continue;
            if (!CanBeginStep(step)) continue;

            BeginStep(step);
            return;
        }
    }

    private void HandleLinePresented(DialogueSequenceSO sequence, int lineIndex)
    {
        if (_activeStep == null || !_activeStep.IsViewOnly) return;
        if (_activeStep.startWhenSequenceStarts == null || sequence != _activeStep.startWhenSequenceStarts)
            return;

        int dismissAfter = _activeStep.dismissAfterLineCount;
        if (dismissAfter <= 0) return;

        // lineIndex is 0-based; dismiss once we move past the Nth line (index >= N).
        if (lineIndex >= dismissAfter)
            CompleteViewOnlyStep();
    }

    private void HandleSequenceCompleting(DialogueSequenceSO completed)
    {
        if (completed == null) return;

        // View-only callouts with no early line cutoff (or still active) end with the bound sequence.
        if (_activeStep != null &&
            _activeStep.IsViewOnly &&
            _activeStep.startWhenSequenceStarts != null &&
            _activeStep.startWhenSequenceStarts == completed)
        {
            CompleteViewOnlyStep();
        }

        // Highlight may already be gone (dismissAfterLineCount) — still drop the sort boost.
        ReleaseDialogueCanvasSortBoost();

        if (steps == null) return;
        if (_activeStep != null) return;

        for (int i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            if (step == null || step.startAfterSequence == null) continue;
            if (step.startAfterSequence != completed) continue;
            if (!CanBeginStep(step)) continue;

            BeginStep(step);
            return;
        }
    }

    private static bool CanBeginStep(TutorialUIHighlightStepSO step)
    {
        if (!string.IsNullOrEmpty(step.skipIfHasFlag) &&
            NarrativeDirector.HasStoryFlag(step.skipIfHasFlag))
            return false;
        if (!string.IsNullOrEmpty(step.setFlagOnComplete) &&
            NarrativeDirector.HasStoryFlag(step.setFlagOnComplete))
            return false;
        return true;
    }

    private void BeginStep(TutorialUIHighlightStepSO step)
    {
        if (step == null) return;
        if (_resolveRoutine != null)
            StopCoroutine(_resolveRoutine);

        // Same frame as dialogue teardown / start — full dim so the tree never flashes.
        ShowBridgeDim(step);

        if (skillUI == null)
            skillUI = FindObjectOfType<RacingSkillUI>(true);

        if (TryResolveTarget(step, out RacingSkillUIEntry entry, out RectTransform targetRt))
        {
            ActivateHighlight(step, entry, targetRt);
            return;
        }

        _resolveRoutine = StartCoroutine(BeginStepWhenTargetReady(step));
    }

    private void ShowBridgeDim(TutorialUIHighlightStepSO step)
    {
        EnsureOverlay();
        if (_overlayRoot == null) return;

        _activeStep = step;
        _cutoutReady = false;
        ApplyStepVisuals(step);
        ApplyDimRaycasts(!step.IsViewOnly);
        MountOverlayForActiveDialogueIfNeeded(step);

        if (_bridgeDimImage != null)
            _bridgeDimImage.color = step.dimColor;
        if (_bridgeDim != null)
            _bridgeDim.gameObject.SetActive(true);

        // Hide cutout panels / outline until the hole target is ready.
        SetCutoutPanelsVisible(false);
        if (_outline != null)
            _outline.gameObject.SetActive(false);

        _overlayRoot.gameObject.SetActive(true);

        skillUI?.SetTutorialInputLock(true);
        GameplayUIInputGuard.IsTutorialHighlightActive = true;

        if (verboseDebug)
            Debug.Log($"[TutorialUIHighlightCoach] Bridge dim up for '{step.name}'.");
    }

    private void SetCutoutPanelsVisible(bool visible)
    {
        if (_dimLeft != null) _dimLeft.gameObject.SetActive(visible);
        if (_dimRight != null) _dimRight.gameObject.SetActive(visible);
        if (_dimTop != null) _dimTop.gameObject.SetActive(visible);
        if (_dimBottom != null) _dimBottom.gameObject.SetActive(visible);
    }

    private IEnumerator BeginStepWhenTargetReady(TutorialUIHighlightStepSO step)
    {
        if (skillUI == null)
            skillUI = FindObjectOfType<RacingSkillUI>(true);

        const int maxFrames = 120;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (TryResolveTarget(step, out RacingSkillUIEntry entry, out RectTransform targetRt))
            {
                ActivateHighlight(step, entry, targetRt);
                _resolveRoutine = null;
                yield break;
            }
            yield return null;
        }

        if (verboseDebug)
            Debug.LogWarning($"[TutorialUIHighlightCoach] Could not resolve target for step '{step.name}'.");
        _resolveRoutine = null;
        ClearActiveHighlight();
    }

    private bool TryResolveTarget(
        TutorialUIHighlightStepSO step,
        out RacingSkillUIEntry entry,
        out RectTransform targetRt)
    {
        entry = null;
        targetRt = null;
        if (skillUI == null) return false;

        switch (step.targetKind)
        {
            case TutorialUIHighlightStepSO.TargetKind.SkillNode:
                if (!skillUI.TryGetEntry(step.skillTarget, out entry) || entry == null)
                    return false;
                targetRt = entry.transform as RectTransform;
                return targetRt != null;

            case TutorialUIHighlightStepSO.TargetKind.SkillDetailCost:
                return skillUI.TryGetDetailCostHighlightRect(out targetRt);

            default:
                return false;
        }
    }

    private void ActivateHighlight(
        TutorialUIHighlightStepSO step,
        RacingSkillUIEntry entry,
        RectTransform targetRt)
    {
        EnsureOverlay();
        if (_overlayRoot == null || targetRt == null) return;

        if (_activeEntry != null)
            _activeEntry.Selected -= OnHighlightedEntrySelected;

        _activeStep = step;
        _activeEntry = entry;
        _activeTargetRt = targetRt;

        if (!step.IsViewOnly && entry != null)
            entry.Selected += OnHighlightedEntrySelected;

        ApplyStepVisuals(step);
        ApplyDimRaycasts(!step.IsViewOnly);
        MountOverlayForActiveDialogueIfNeeded(step);
        _overlayRoot.gameObject.SetActive(true);

        // Switch from full-screen bridge to cutout + outline in the same activation.
        if (_bridgeDim != null)
            _bridgeDim.gameObject.SetActive(false);
        SetCutoutPanelsVisible(true);
        if (_outline != null)
            _outline.gameObject.SetActive(true);

        _cutoutReady = true;
        LayoutCutoutAroundTarget();
        LiftTargetAboveDim(targetRt, step);

        skillUI?.SetTutorialInputLock(true);
        GameplayUIInputGuard.IsTutorialHighlightActive = true;

        if (verboseDebug)
        {
            string targetName = targetRt != null ? targetRt.name : "?";
            Debug.Log($"[TutorialUIHighlightCoach] Highlight active: '{step.name}' → {step.targetKind} ({targetName})");
        }
    }

    private void OnHighlightedEntrySelected(SkillDefinition def)
    {
        if (_activeStep == null || def == null) return;
        if (_activeStep.IsViewOnly) return;
        if (_activeStep.targetKind == TutorialUIHighlightStepSO.TargetKind.SkillNode &&
            def.type != _activeStep.skillTarget)
            return;

        CompleteActiveStep();
    }

    private void CompleteActiveStep()
    {
        var step = _activeStep;
        ClearActiveHighlight();

        if (step == null) return;

        if (!string.IsNullOrEmpty(step.setFlagOnComplete))
            NarrativeDirector.SetStoryFlag(step.setFlagOnComplete);

        if (step.playOnClick != null && DialogueManager.Instance != null)
            DialogueManager.Instance.PlaySequence(step.playOnClick);

        if (verboseDebug)
            Debug.Log($"[TutorialUIHighlightCoach] Step complete: '{step.name}'");
    }

    private void CompleteViewOnlyStep()
    {
        var step = _activeStep;
        // Dialogue may still be playing (early line cutoff) — restore the solid dim curtain now.
        ClearActiveHighlight(restoreDialogueDim: true);

        if (step == null) return;

        if (!string.IsNullOrEmpty(step.setFlagOnComplete))
            NarrativeDirector.SetStoryFlag(step.setFlagOnComplete);

        if (verboseDebug)
            Debug.Log($"[TutorialUIHighlightCoach] View-only step ended: '{step.name}'");
    }

    private void ClearActiveHighlight()
    {
        ClearActiveHighlight(restoreDialogueDim: true);
    }

    private void ClearActiveHighlight(bool restoreDialogueDim)
    {
        if (_resolveRoutine != null)
        {
            StopCoroutine(_resolveRoutine);
            _resolveRoutine = null;
        }

        if (_activeEntry != null)
        {
            _activeEntry.Selected -= OnHighlightedEntrySelected;
            _activeEntry = null;
        }

        _activeTargetRt = null;
        _activeStep = null;
        _cutoutReady = false;

        RestoreLiftedTarget();
        RestoreOverlayAfterDialogueMount(restoreDialogueDim);

        if (_overlayRoot != null)
            _overlayRoot.gameObject.SetActive(false);
        if (_bridgeDim != null)
            _bridgeDim.gameObject.SetActive(false);

        skillUI?.SetTutorialInputLock(false);
        GameplayUIInputGuard.IsTutorialHighlightActive = false;
    }

    /// <summary>
    /// Force the spotlight target to draw above dialogue/game dims so the hole content is actually bright.
    /// </summary>
    private void LiftTargetAboveDim(RectTransform targetRt, TutorialUIHighlightStepSO step)
    {
        RestoreLiftedTarget();
        if (targetRt == null || step == null || !step.IsViewOnly) return;

        _liftedTargetRt = targetRt;
        _liftedTargetCanvas = targetRt.GetComponent<Canvas>();
        if (_liftedTargetCanvas == null)
        {
            _liftedTargetCanvas = targetRt.gameObject.AddComponent<Canvas>();
            _liftedTargetCanvasAdded = true;
        }
        else
        {
            _liftedTargetCanvasAdded = false;
            _liftedTargetOverrideWasEnabled = _liftedTargetCanvas.overrideSorting;
            _liftedTargetSavedSortOrder = _liftedTargetCanvas.sortingOrder;
        }

        int baseOrder = 100;
        if (_boostedDialogueCanvas != null)
            baseOrder = _boostedDialogueCanvas.sortingOrder;
        else
        {
            var dui = FindObjectOfType<DialogueUI>(true);
            if (dui != null && dui.HostCanvas != null)
                baseOrder = dui.HostCanvas.sortingOrder;
        }

        _liftedTargetCanvas.overrideSorting = true;
        _liftedTargetCanvas.sortingOrder = baseOrder + 10;

        _liftedTargetRaycaster = targetRt.GetComponent<GraphicRaycaster>();
        if (_liftedTargetRaycaster == null)
        {
            _liftedTargetRaycaster = targetRt.gameObject.AddComponent<GraphicRaycaster>();
            _liftedTargetRaycasterAdded = true;
        }
        else
            _liftedTargetRaycasterAdded = false;
    }

    private void RestoreLiftedTarget()
    {
        if (_liftedTargetRt == null && _liftedTargetCanvas == null)
        {
            _liftedTargetCanvasAdded = false;
            _liftedTargetRaycasterAdded = false;
            return;
        }

        if (_liftedTargetRaycasterAdded && _liftedTargetRaycaster != null)
            Destroy(_liftedTargetRaycaster);
        _liftedTargetRaycaster = null;
        _liftedTargetRaycasterAdded = false;

        if (_liftedTargetCanvas != null)
        {
            if (_liftedTargetCanvasAdded)
                Destroy(_liftedTargetCanvas);
            else
            {
                _liftedTargetCanvas.overrideSorting = _liftedTargetOverrideWasEnabled;
                _liftedTargetCanvas.sortingOrder = _liftedTargetSavedSortOrder;
            }
        }

        _liftedTargetCanvas = null;
        _liftedTargetCanvasAdded = false;
        _liftedTargetRt = null;
        _liftedTargetOverrideWasEnabled = false;
        _liftedTargetSavedSortOrder = 0;
    }

    /// <summary>
    /// View-only highlights during dialogue must live on the dialogue canvas: the dialogue
    /// Background Panel otherwise sits above the game-canvas cutout and keeps the hole dark.
    /// We hide only that Image (Dialogue Box stays as its child) and draw our cutout behind the text.
    /// </summary>
    private void MountOverlayForActiveDialogueIfNeeded(TutorialUIHighlightStepSO step)
    {
        if (step == null || !step.IsViewOnly) return;
        if (DialogueManager.Instance == null || !DialogueManager.Instance.IsPlaying) return;

        EnsureOverlay();
        if (_overlayRoot == null) return;

        var dialogueUI = FindObjectOfType<DialogueUI>(true);
        if (dialogueUI == null) return;

        RectTransform host = dialogueUI.OverlayHost;
        if (host == null) return;

        if (_overlayDefaultParent == null)
            _overlayDefaultParent = _overlayRoot.parent;

        // Kill the solid dialogue dim so our hole can reveal the skill-tree cost underneath.
        dialogueUI.SetScreenDimVisible(false);

        Canvas dialogueCanvas = dialogueUI.HostCanvas;
        Canvas gameCanvas = gameCanvasRoot != null ? gameCanvasRoot.GetComponent<Canvas>() : null;
        if (dialogueCanvas != null)
        {
            int gameOrder = gameCanvas != null ? gameCanvas.sortingOrder : 0;
            if (!_mountedOnDialogueCanvas)
            {
                _boostedDialogueCanvas = dialogueCanvas;
                _dialogueCanvasSavedSortOrder = dialogueCanvas.sortingOrder;
            }
            dialogueCanvas.overrideSorting = true;
            dialogueCanvas.sortingOrder = Mathf.Max(dialogueCanvas.sortingOrder, gameOrder + DialogueCanvasSortBoost);
        }

        _overlayRoot.SetParent(host, false);
        StretchFull(_overlayRoot);
        // Behind Background Panel / Dialogue Box / speaker text so the goo box stays bright.
        _overlayRoot.SetAsFirstSibling();
        _mountedOnDialogueCanvas = true;

        if (verboseDebug)
            Debug.Log($"[TutorialUIHighlightCoach] Overlay mounted on dialogue canvas for '{step.name}'.");
    }

    private void RestoreOverlayAfterDialogueMount(bool restoreDialogueDim)
    {
        bool wasMountedOnDialogue = _mountedOnDialogueCanvas;

        // Re-enable the solid Background Panel dim whenever we tear down a dialogue-mounted cutout
        // while dialogue is still on screen (early dismiss after N lines).
        if (restoreDialogueDim && wasMountedOnDialogue)
        {
            var dialogueUI = FindObjectOfType<DialogueUI>(true);
            if (dialogueUI != null && dialogueUI.gameObject.activeInHierarchy)
            {
                dialogueUI.SetScreenDimVisible(true);
                if (verboseDebug)
                    Debug.Log("[TutorialUIHighlightCoach] Restored dialogue screen dim.");
            }
        }

        // Keep dialogue canvas above the game canvas while the sequence is still playing so the
        // restored dim actually curtains the skill tree (same stacking as during the highlight).
        bool dialogueStillPlaying =
            DialogueManager.Instance != null && DialogueManager.Instance.IsPlaying;

        if (_boostedDialogueCanvas != null && !dialogueStillPlaying)
        {
            _boostedDialogueCanvas.sortingOrder = _dialogueCanvasSavedSortOrder;
            _boostedDialogueCanvas = null;
        }

        if (_overlayRoot != null && _overlayDefaultParent != null && _overlayRoot.parent != _overlayDefaultParent)
        {
            _overlayRoot.SetParent(_overlayDefaultParent, false);
            StretchFull(_overlayRoot);
            int sibling = Mathf.Clamp(overlaySiblingIndex, 0, Mathf.Max(0, _overlayDefaultParent.childCount - 1));
            _overlayRoot.SetSiblingIndex(sibling);
        }

        _mountedOnDialogueCanvas = false;
    }

    /// <summary>Drop any leftover dialogue canvas sort boost when the sequence fully ends.</summary>
    private void ReleaseDialogueCanvasSortBoost()
    {
        if (_boostedDialogueCanvas == null) return;
        _boostedDialogueCanvas.sortingOrder = _dialogueCanvasSavedSortOrder;
        _boostedDialogueCanvas = null;
    }

    private void ApplyStepVisuals(TutorialUIHighlightStepSO step)
    {
        Color dim = step.dimColor;
        if (_dimLeftImage) _dimLeftImage.color = dim;
        if (_dimRightImage) _dimRightImage.color = dim;
        if (_dimTopImage) _dimTopImage.color = dim;
        if (_dimBottomImage) _dimBottomImage.color = dim;

        Color outline = step.outlineColor;
        // Parent image must stay fully clear — only the four edge bars draw the outline.
        if (_outlineColorSource) _outlineColorSource.color = new Color(0f, 0f, 0f, 0f);
        if (_edgeLImage) _edgeLImage.color = outline;
        if (_edgeRImage) _edgeRImage.color = outline;
        if (_edgeBImage) _edgeBImage.color = outline;
        if (_edgeTImage) _edgeTImage.color = outline;
    }

    /// <summary>
    /// Click-forced steps block input outside the hole. View-only (during dialogue) must not
    /// steal clicks from the dialogue panel when both share / overlap the game canvas.
    /// </summary>
    private void ApplyDimRaycasts(bool blockRaycasts)
    {
        if (_dimLeftImage) _dimLeftImage.raycastTarget = blockRaycasts;
        if (_dimRightImage) _dimRightImage.raycastTarget = blockRaycasts;
        if (_dimTopImage) _dimTopImage.raycastTarget = blockRaycasts;
        if (_dimBottomImage) _dimBottomImage.raycastTarget = blockRaycasts;
        if (_bridgeDimImage) _bridgeDimImage.raycastTarget = blockRaycasts;
    }

    private void LayoutCutoutAroundTarget()
    {
        if (_activeTargetRt == null || _overlayRoot == null) return;

        var targetRt = _activeTargetRt;

        Canvas.ForceUpdateCanvases();

        Vector3[] corners = new Vector3[4];
        targetRt.GetWorldCorners(corners);

        Camera cam = null;
        Canvas rootCanvas = _overlayRoot.GetComponentInParent<Canvas>();
        if (rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = rootCanvas.worldCamera;

        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = min;
        for (int i = 1; i < 4; i++)
        {
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            min = Vector2.Min(min, sp);
            max = Vector2.Max(max, sp);
        }

        float pad = _activeStep != null ? _activeStep.holePadding : 18f;
        float thickness = _activeStep != null ? _activeStep.outlineThickness : 6f;
        float bobAmp = _activeStep != null ? _activeStep.bobAmplitude : 8f;

        // Hole follows the target's scaled screen rect. Keep outline/bob readable at min zoom
        // without double-shrinking values already authored for the small default zoom.
        float nodeSpan = Mathf.Max(1f, Mathf.Min(max.x - min.x, max.y - min.y));
        thickness = Mathf.Max(thickness, Mathf.Clamp(nodeSpan * 0.05f, 1.5f, 8f));
        bobAmp = Mathf.Clamp(bobAmp, nodeSpan * 0.025f, nodeSpan * 0.1f);
        _outlineBobAmplitudeScaled = bobAmp;

        min.x -= pad;
        min.y -= pad;
        max.x += pad;
        max.y += pad;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _overlayRoot, min, cam, out Vector2 localMin) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _overlayRoot, max, cam, out Vector2 localMax))
            return;

        float left = Mathf.Min(localMin.x, localMax.x);
        float right = Mathf.Max(localMin.x, localMax.x);
        float bottom = Mathf.Min(localMin.y, localMax.y);
        float top = Mathf.Max(localMin.y, localMax.y);

        Rect rootRect = _overlayRoot.rect;
        SetPanel(_dimLeft, rootRect.xMin, bottom, left, top);
        SetPanel(_dimRight, right, bottom, rootRect.xMax, top);
        SetPanel(_dimTop, rootRect.xMin, top, rootRect.xMax, rootRect.yMax);
        SetPanel(_dimBottom, rootRect.xMin, rootRect.yMin, rootRect.xMax, bottom);

        SetPanel(_outline, left - thickness, bottom - thickness, right + thickness, top + thickness);
        _outlineBaseSize = _outline.sizeDelta;
        _outlineThicknessScaled = thickness;
        LayoutOutlineEdges(thickness);
    }

    private void LayoutOutlineEdges(float thickness)
    {
        if (_outline == null) return;
        float w = _outline.rect.width;
        float h = _outline.rect.height;

        if (_edgeL != null)
        {
            _edgeL.anchorMin = new Vector2(0f, 0.5f);
            _edgeL.anchorMax = new Vector2(0f, 0.5f);
            _edgeL.pivot = new Vector2(0f, 0.5f);
            _edgeL.sizeDelta = new Vector2(thickness, h);
            _edgeL.anchoredPosition = Vector2.zero;
        }

        if (_edgeR != null)
        {
            _edgeR.anchorMin = new Vector2(1f, 0.5f);
            _edgeR.anchorMax = new Vector2(1f, 0.5f);
            _edgeR.pivot = new Vector2(1f, 0.5f);
            _edgeR.sizeDelta = new Vector2(thickness, h);
            _edgeR.anchoredPosition = Vector2.zero;
        }

        if (_edgeB != null)
        {
            _edgeB.anchorMin = new Vector2(0.5f, 0f);
            _edgeB.anchorMax = new Vector2(0.5f, 0f);
            _edgeB.pivot = new Vector2(0.5f, 0f);
            _edgeB.sizeDelta = new Vector2(w, thickness);
            _edgeB.anchoredPosition = Vector2.zero;
        }

        if (_edgeT != null)
        {
            _edgeT.anchorMin = new Vector2(0.5f, 1f);
            _edgeT.anchorMax = new Vector2(0.5f, 1f);
            _edgeT.pivot = new Vector2(0.5f, 1f);
            _edgeT.sizeDelta = new Vector2(w, thickness);
            _edgeT.anchoredPosition = Vector2.zero;
        }
    }

    private static void SetPanel(RectTransform panel, float xMin, float yMin, float xMax, float yMax)
    {
        if (panel == null) return;
        float w = Mathf.Max(0f, xMax - xMin);
        float h = Mathf.Max(0f, yMax - yMin);
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(w, h);
        panel.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
    }

    private void AnimateOutlineBob()
    {
        if (_outline == null || _activeStep == null) return;

        float speed = _activeStep.bobSpeed;
        float amp = _outlineBobAmplitudeScaled;
        float wave = Mathf.Sin(Time.unscaledTime * speed * Mathf.PI * 2f);
        float pulse = (wave * 0.5f + 0.5f);

        _outline.sizeDelta = _outlineBaseSize + Vector2.one * (wave * amp);
        LayoutOutlineEdges(_outlineThicknessScaled);

        Color c = _activeStep.outlineColor;
        c.a = Mathf.Lerp(0.55f, 1f, pulse);
        if (_edgeLImage) _edgeLImage.color = c;
        if (_edgeRImage) _edgeRImage.color = c;
        if (_edgeBImage) _edgeBImage.color = c;
        if (_edgeTImage) _edgeTImage.color = c;
        if (_outlineColorSource) _outlineColorSource.color = new Color(0f, 0f, 0f, 0f);
    }

    private void EnsureOverlay()
    {
        if (_overlayRoot != null) return;

        if (gameCanvasRoot == null)
        {
            var ui = FindObjectOfType<UIManager_Racing>(true);
            if (ui != null)
                gameCanvasRoot = ui.GameCanvas;
        }

        if (gameCanvasRoot == null)
        {
            Debug.LogWarning("[TutorialUIHighlightCoach] No game canvas found for overlay.");
            return;
        }

        var go = new GameObject("TutorialUIHighlightOverlay", typeof(RectTransform));
        go.transform.SetParent(gameCanvasRoot.transform, false);
        int sibling = Mathf.Clamp(overlaySiblingIndex, 0, gameCanvasRoot.transform.childCount);
        go.transform.SetSiblingIndex(sibling);

        _overlayRoot = go.GetComponent<RectTransform>();
        StretchFull(_overlayRoot);

        var rootImg = go.AddComponent<Image>();
        rootImg.color = new Color(0f, 0f, 0f, 0f);
        rootImg.raycastTarget = false;

        _dimLeftImage = CreateDimPanel("DimLeft", out _dimLeft);
        _dimRightImage = CreateDimPanel("DimRight", out _dimRight);
        _dimTopImage = CreateDimPanel("DimTop", out _dimTop);
        _dimBottomImage = CreateDimPanel("DimBottom", out _dimBottom);

        // Full-screen bridge dim: up before dialogue hides, then swapped for the cutout.
        _bridgeDimImage = CreateDimPanel("BridgeDim", out _bridgeDim);
        StretchFull(_bridgeDim);
        _bridgeDim.SetAsFirstSibling();
        _bridgeDim.gameObject.SetActive(false);

        var outlineGo = new GameObject("YellowOutline", typeof(RectTransform));
        outlineGo.transform.SetParent(_overlayRoot, false);
        _outline = outlineGo.GetComponent<RectTransform>();
        _outlineColorSource = outlineGo.AddComponent<Image>();
        _outlineColorSource.color = new Color(0f, 0f, 0f, 0f);
        _outlineColorSource.raycastTarget = false;

        _edgeLImage = CreateOutlineEdge("EdgeL", _outline, out _edgeL);
        _edgeRImage = CreateOutlineEdge("EdgeR", _outline, out _edgeR);
        _edgeBImage = CreateOutlineEdge("EdgeB", _outline, out _edgeB);
        _edgeTImage = CreateOutlineEdge("EdgeT", _outline, out _edgeT);

        _overlayRoot.gameObject.SetActive(false);
    }

    private Image CreateDimPanel(string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(_overlayRoot, false);
        rt = go.GetComponent<RectTransform>();
        var img = go.GetComponent<Image>();
        img.color = new Color(0.05f, 0.05f, 0.05f, 0.92f);
        img.raycastTarget = true;
        return img;
    }

    private static Image CreateOutlineEdge(string name, RectTransform parent, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        rt = go.GetComponent<RectTransform>();
        var img = go.GetComponent<Image>();
        img.color = Color.yellow;
        img.raycastTarget = false;
        return img;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }
}
