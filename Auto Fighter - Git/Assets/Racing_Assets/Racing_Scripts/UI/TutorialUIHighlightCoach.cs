using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runs <see cref="TutorialUIHighlightStepSO"/> beats: dim UI with a hole over a target,
/// yellow bobbing outline, force-click that target, then play follow-up dialogue.
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
    private bool _cutoutReady;

    public static bool IsHighlightActive =>
        Instance != null && Instance._activeStep != null;

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
        if (!_cutoutReady || _activeEntry == null)
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
        _subscribed = true;
    }

    private void UnsubscribeDialogue()
    {
        if (!_subscribed) return;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.OnSequenceCompleting -= HandleSequenceCompleting;
        _subscribed = false;
    }

    private void HandleSequenceCompleting(DialogueSequenceSO completed)
    {
        if (completed == null || steps == null) return;
        if (_activeStep != null) return;

        for (int i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            if (step == null || step.startAfterSequence == null) continue;
            if (step.startAfterSequence != completed) continue;
            if (!string.IsNullOrEmpty(step.skipIfHasFlag) &&
                NarrativeDirector.HasStoryFlag(step.skipIfHasFlag))
                continue;
            if (!string.IsNullOrEmpty(step.setFlagOnComplete) &&
                NarrativeDirector.HasStoryFlag(step.setFlagOnComplete))
                continue;

            BeginStep(step);
            return;
        }
    }

    private void BeginStep(TutorialUIHighlightStepSO step)
    {
        if (step == null) return;
        if (_resolveRoutine != null)
            StopCoroutine(_resolveRoutine);

        // Same frame as dialogue teardown, before Hide — full dim so the tree never flashes.
        ShowBridgeDim(step);

        if (skillUI == null)
            skillUI = FindObjectOfType<RacingSkillUI>(true);

        if (TryResolveTarget(step, out RacingSkillUIEntry entry))
        {
            ActivateHighlight(step, entry);
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
            Debug.Log($"[TutorialUIHighlightCoach] Bridge dim up for '{step.name}' (before dialogue hide).");
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
            if (TryResolveTarget(step, out RacingSkillUIEntry entry))
            {
                ActivateHighlight(step, entry);
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

    private bool TryResolveTarget(TutorialUIHighlightStepSO step, out RacingSkillUIEntry entry)
    {
        entry = null;
        if (skillUI == null) return false;

        switch (step.targetKind)
        {
            case TutorialUIHighlightStepSO.TargetKind.SkillNode:
                return skillUI.TryGetEntry(step.skillTarget, out entry);
            default:
                return false;
        }
    }

    private void ActivateHighlight(TutorialUIHighlightStepSO step, RacingSkillUIEntry entry)
    {
        EnsureOverlay();
        if (_overlayRoot == null || entry == null) return;

        if (_activeEntry != null)
            _activeEntry.Selected -= OnHighlightedEntrySelected;

        _activeStep = step;
        _activeEntry = entry;
        entry.Selected += OnHighlightedEntrySelected;

        ApplyStepVisuals(step);
        _overlayRoot.gameObject.SetActive(true);

        // Switch from full-screen bridge to cutout + outline in the same activation.
        if (_bridgeDim != null)
            _bridgeDim.gameObject.SetActive(false);
        SetCutoutPanelsVisible(true);
        if (_outline != null)
            _outline.gameObject.SetActive(true);

        _cutoutReady = true;
        LayoutCutoutAroundTarget();

        skillUI?.SetTutorialInputLock(true);
        GameplayUIInputGuard.IsTutorialHighlightActive = true;

        if (verboseDebug)
            Debug.Log($"[TutorialUIHighlightCoach] Highlight active: '{step.name}' → {step.skillTarget}");
    }

    private void OnHighlightedEntrySelected(SkillDefinition def)
    {
        if (_activeStep == null || def == null) return;
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

    private void ClearActiveHighlight()
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

        _activeStep = null;
        _cutoutReady = false;

        if (_overlayRoot != null)
            _overlayRoot.gameObject.SetActive(false);
        if (_bridgeDim != null)
            _bridgeDim.gameObject.SetActive(false);

        skillUI?.SetTutorialInputLock(false);
        GameplayUIInputGuard.IsTutorialHighlightActive = false;
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

    private void LayoutCutoutAroundTarget()
    {
        if (_activeEntry == null || _overlayRoot == null) return;

        var targetRt = _activeEntry.transform as RectTransform;
        if (targetRt == null) return;

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

        float thickness = _activeStep != null ? _activeStep.outlineThickness : 6f;
        SetPanel(_outline, left - thickness, bottom - thickness, right + thickness, top + thickness);
        _outlineBaseSize = _outline.sizeDelta;
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
        float amp = _activeStep.bobAmplitude;
        float wave = Mathf.Sin(Time.unscaledTime * speed * Mathf.PI * 2f);
        float pulse = (wave * 0.5f + 0.5f);

        _outline.sizeDelta = _outlineBaseSize + Vector2.one * (wave * amp);
        LayoutOutlineEdges(_activeStep.outlineThickness);

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
