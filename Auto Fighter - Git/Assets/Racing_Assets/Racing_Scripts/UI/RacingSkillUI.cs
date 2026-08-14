using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Skill tree UI. Racing vs SkillTreeUI action maps are toggled from <see cref="UIManager_Racing"/> by section (not from this component's OnEnable), so car input is restored even if this object stays active when the skill-tree panel is hidden.
/// </summary>
[DefaultExecutionOrder(-50)]
public class RacingSkillUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Transform contentParent;
    [SerializeField] private RacingSkillUIEntry entryPrefab;
    [SerializeField] private TMP_Text currencyText;
    [SerializeField] private TMP_Text sprocketsText;

    [Header("Detail Panel")]
    [SerializeField] private RacingSkillDetailPanel detailPanel;

    [Header("Tree View (optional)")]
    [SerializeField] private RectTransform treeViewport;
    [SerializeField] private RectTransform treeContent;
    [SerializeField, Min(0f)] private float panSpeed = 1.0f;
    [SerializeField, Min(0.01f)] private float zoomStep = 0.1f;
    [SerializeField, Range(0.25f, 4f)] private float minZoom = 0.5f;
    [SerializeField, Range(0.25f, 4f)] private float maxZoom = 2.0f;
    [SerializeField] private bool rightMouseDragToPan = true;

    [Header("Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button questButton;
    [SerializeField] private Button inventoryButton;
    [SerializeField] private RacingQuestsPanelUI questsPanelUI;
    [SerializeField] private RacingRunInventoryPanelUI inventoryPanelUI;
    [SerializeField] private bool autoOpenFirstSkill = false;
    [SerializeField] private bool verboseDebug = false;
    [SerializeField] private bool forceDeferredBuild = true;
    [SerializeField] private bool enableLegacyPanelToggleRaycastFallback = false;

    [Tooltip("When the skill info card is open, a left-click that is not on the card, Buy, a skill node, or toolbar closes the card (no invisible blocking layer).")]
    [SerializeField] private bool dismissDetailOnOutsideClick = true;

    [Header("Tree View - Gamepad")]
    [SerializeField] private bool enableGamepadPanZoom = true;

    [Tooltip("Input Manager axis name for Right Stick Horizontal.")]
    [SerializeField] private string gamepadPanAxisX = "RightStickX";

    [Tooltip("Input Manager axis name for Right Stick Vertical.")]
    [SerializeField] private string gamepadPanAxisY = "RightStickY";

    [Tooltip("Input Manager axis name for Right Trigger (0..1).")]
    [SerializeField] private string gamepadZoomInAxis = "RightTrigger";

    [Tooltip("Input Manager axis name for Left Trigger (0..1).")]
    [SerializeField] private string gamepadZoomOutAxis = "LeftTrigger";

    [SerializeField, Range(0f, 0.5f)] private float gamepadStickDeadzone = 0.18f;

    [Tooltip("Pixels per second at full stick tilt (before panSpeed multiplier).")]
    [SerializeField, Min(0f)] private float gamepadPanPixelsPerSecond = 900f;

    [Tooltip("Zoom units per second at full trigger pull (multiplied by zoomStep).")]
    [SerializeField, Min(0f)] private float gamepadZoomSpeed = 2.0f;

    [Tooltip("Invert Y so pushing stick up pans upward (usually feels correct in UI).")]
    [SerializeField] private bool invertRightStickY = true;

    private bool _isPanning;
    private Vector2 _lastMouse;

    /// <summary>Current tree content uniform scale (zoom). 1 = authored size.</summary>
    public float TreeZoom => treeContent != null ? treeContent.localScale.x : 1f;

    /// <summary>Where skill node instances are parented (tree or list). Used for hierarchy/debug; click logic uses <see cref="RacingSkillUIEntry"/> on nodes.</summary>
    public Transform SkillNodesParent => treeContent != null ? treeContent : contentParent;

    private RacingSkillTreeManager mgr;
    private RacingQuestUnlockManager questMgr;
    private GameManager_Racing gameManager;
    private readonly List<RacingSkillUIEntry> entries = new();
    private RacingSkillUIEntry selectedEntry;
    private bool buildSucceeded;
    private int _lastPanelToggleFrame = -9999;
    private bool _tutorialInputLocked;

    void Awake()
    {
        mgr = RacingSkillTreeManager.Instance;
    }

    private void OnEnable()
    {
        EnsureManager();
        mgr?.RefreshQuestUnlockReveals();
        BindPlayButton();
        WireEvents();
        DestroyLegacyTreeViewportRaycastFillIfPresent();
        AttemptBuild(); // builds only revealed
        RefreshAll();
        ApplyDefaultTreeZoom();
        AutoOpenFirstIfNeeded();
    }

    /// <summary>
    /// Start the tree at minimum zoom (most zoomed-out) within the configured min/max range.
    /// </summary>
    private void ApplyDefaultTreeZoom()
    {
        if (!treeContent) return;
        float lo = Mathf.Min(minZoom, maxZoom);
        float hi = Mathf.Max(minZoom, maxZoom);
        float z = Mathf.Clamp(lo, 0.01f, hi);
        treeContent.localScale = new Vector3(z, z, 1f);
    }

    private void OnDisable()
    {
        UnwireEvents();
    }

    void Update()
    {
        bool blockNav = GameplayUIInputGuard.IsGameplayUiNavigationBlocked || _tutorialInputLocked;
        if (!blockNav)
        {
            HandleTreePan();
            HandleTreeZoom();
            HandleTreePanZoomGamepad();
            if (enableLegacyPanelToggleRaycastFallback)
                HandlePanelToggleByButtonRaycastFallback();
            if (dismissDetailOnOutsideClick)
                TryDismissDetailOnOutsideClick();
        }

        HandlePanelSelectionSafety();
    }

    public void BindGameManager(GameManager_Racing gm) => gameManager = gm;

    /// <summary>Find a built skill node by type (e.g. for tutorial spotlights).</summary>
    public bool TryGetEntry(SkillType type, out RacingSkillUIEntry entry)
    {
        entry = null;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            var def = e.GetDefinition();
            if (def != null && def.type == type)
            {
                entry = e;
                return true;
            }
        }
        return false;
    }

    /// <summary>Cost text rect on the open detail card (tutorial cost spotlight).</summary>
    public bool TryGetDetailCostHighlightRect(out RectTransform rect)
    {
        rect = null;
        if (detailPanel == null) return false;
        return detailPanel.TryGetCostHighlightRect(out rect);
    }

    /// <summary>
    /// While a tutorial spotlight is up, disable pan/zoom and toolbar buttons so only the hole target is usable.
    /// </summary>
    public void SetTutorialInputLock(bool locked)
    {
        _tutorialInputLocked = locked;
        if (locked)
            _isPanning = false;

        if (playButton) playButton.interactable = !locked;
        if (questButton) questButton.interactable = !locked;
        if (inventoryButton) inventoryButton.interactable = !locked;
    }

    private void BindPlayButton()
    {
        if (playButton)
        {
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(OnPlayClicked);
        }

        if (questButton)
        {
            questButton.onClick.RemoveAllListeners();
            questButton.onClick.AddListener(OnQuestClicked);
        }

        if (inventoryButton)
        {
            inventoryButton.onClick.RemoveAllListeners();
            inventoryButton.onClick.AddListener(OnInventoryClicked);
        }
    }

    private void OnPlayClicked()
    {
        if (GameplayUIInputGuard.IsGameplayUiNavigationBlocked || _tutorialInputLocked) return;

        ClearSelection();
        // HideInfo only — HideImmediate deactivates detailPanel.root, which is wired to the
        // SkillTree GameObject in scene, so it was killing the whole tree on Play.
        detailPanel?.HideInfo();

        var inventoryPanel = FindObjectOfType<RacingRunInventoryPanelUI>(true);
        if (inventoryPanel != null)
        {
            bool canProceed = inventoryPanel.CheckPlayWarningGate(ProceedToRunFromPlay);
            if (!canProceed) return;
        }

        ProceedToRunFromPlay();
    }

    private void ProceedToRunFromPlay()
    {
        // First run: controls overlay only. Loading starts when the player advances past it.
        if (FirstRunControlsOverlay.TryShow(OnFirstRunControlsDismissed))
        {
            // Drop the skill tree so it can't show through the controls iris hole.
            // Later runs intentionally keep it visible under the goo close.
            if (treeViewport != null)
                treeViewport.gameObject.SetActive(false);
            return;
        }

        // Later runs: skill tree stays up, blobs close over it, load starts immediately.
        StartRunFromSkillTree();
    }

    private void OnFirstRunControlsDismissed()
    {
        if (!gameManager)
            gameManager = FindObjectOfType<GameManager_Racing>();
        gameManager?.BeginRunAfterControlsDismissed();
    }

    private void StartRunFromSkillTree()
    {
        if (!gameManager)
            gameManager = FindObjectOfType<GameManager_Racing>();
        gameManager?.BeginRunFromSkillTree();
    }

    private void OnQuestClicked()
    {
        if (GameplayUIInputGuard.IsGameplayUiNavigationBlocked || _tutorialInputLocked) return;
        if (Time.frameCount == _lastPanelToggleFrame) return;
        _lastPanelToggleFrame = Time.frameCount;

        if (!questsPanelUI)
            questsPanelUI = FindObjectOfType<RacingQuestsPanelUI>(true);
        if (!inventoryPanelUI)
            inventoryPanelUI = FindObjectOfType<RacingRunInventoryPanelUI>(true);

        DismissSkillDetailForPanelSwitch();
        inventoryPanelUI?.HidePanel();
        questsPanelUI?.TogglePanel();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void OnInventoryClicked()
    {
        if (GameplayUIInputGuard.IsGameplayUiNavigationBlocked || _tutorialInputLocked) return;
        if (Time.frameCount == _lastPanelToggleFrame) return;
        _lastPanelToggleFrame = Time.frameCount;

        if (!inventoryPanelUI)
            inventoryPanelUI = FindObjectOfType<RacingRunInventoryPanelUI>(true);
        if (!questsPanelUI)
            questsPanelUI = FindObjectOfType<RacingQuestsPanelUI>(true);

        DismissSkillDetailForPanelSwitch();
        questsPanelUI?.HidePanel();
        inventoryPanelUI?.TogglePanel();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void DismissSkillDetailForPanelSwitch()
    {
        selectedEntry = null;
        detailPanel?.HideInfo();
    }

    private void HandlePanelSelectionSafety()
    {
        bool anyPanelOpen = IsPanelOpen(questsPanelUI) || IsPanelOpen(inventoryPanelUI);

        SyncToolbarButtons(anyPanelOpen);

        // Prevent controller Submit from activating stale selected buttons (e.g. Play)
        if (anyPanelOpen && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>
    /// Play is disabled while quest/inventory panels are open. Keeps <see cref="Selectable.targetGraphic"/> raycast
    /// in sync so non-interactable toolbar items do not confuse the EventSystem.
    /// </summary>
    private void SyncToolbarButtons(bool anyPanelOpen)
    {
        if (_tutorialInputLocked)
        {
            if (playButton != null)
            {
                playButton.interactable = false;
                if (playButton.targetGraphic != null)
                    playButton.targetGraphic.raycastTarget = false;
            }
            if (questButton != null)
            {
                questButton.interactable = false;
                if (questButton.targetGraphic != null)
                    questButton.targetGraphic.raycastTarget = false;
            }
            if (inventoryButton != null)
            {
                inventoryButton.interactable = false;
                if (inventoryButton.targetGraphic != null)
                    inventoryButton.targetGraphic.raycastTarget = false;
            }
            return;
        }

        if (playButton != null)
        {
            bool allowPlay = !anyPanelOpen;
            playButton.interactable = allowPlay;
            if (playButton.targetGraphic != null)
                playButton.targetGraphic.raycastTarget = allowPlay;
        }

        if (questButton != null)
        {
            questButton.interactable = true;
            if (questButton.targetGraphic != null)
                questButton.targetGraphic.raycastTarget = true;
        }

        if (inventoryButton != null)
        {
            inventoryButton.interactable = true;
            if (inventoryButton.targetGraphic != null)
                inventoryButton.targetGraphic.raycastTarget = true;
        }
    }

    private void HandlePanelToggleByButtonRaycastFallback()
    {
        bool anyPanelOpen = IsPanelOpen(questsPanelUI) || IsPanelOpen(inventoryPanelUI);
        if (!anyPanelOpen) return;
        if (!Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current == null) return;

        var ped = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };
        var results = new List<RaycastResult>(16);
        EventSystem.current.RaycastAll(ped, results);
        if (results.Count == 0) return;

        if (ContainsTargetInRaycast(results, questButton))
        {
            OnQuestClicked();
            return;
        }

        if (ContainsTargetInRaycast(results, inventoryButton))
        {
            OnInventoryClicked();
            return;
        }
    }

    private static bool IsPanelOpen(Object panelScript)
    {
        if (panelScript is RacingQuestsPanelUI q)
            return q.enabled && q.IsPanelOpen;
        if (panelScript is RacingRunInventoryPanelUI i)
            return i.enabled && i.IsPanelOpen;
        return false;
    }

    private static bool ContainsTargetInRaycast(List<RaycastResult> results, Button target)
    {
        if (target == null || results == null) return false;
        Transform btnT = target.transform;
        for (int i = 0; i < results.Count; i++)
        {
            Transform t = results[i].gameObject != null ? results[i].gameObject.transform : null;
            if (t == null) continue;
            if (t == btnT || t.IsChildOf(btnT))
                return true;
        }
        return false;
    }

    private void EnsureManager()
    {
        if (!mgr) mgr = RacingSkillTreeManager.Instance;
        if (!questMgr) questMgr = RacingQuestUnlockManager.Instance;
        if (detailPanel && mgr) detailPanel.Init(mgr);
        if (!gameManager) gameManager = FindObjectOfType<GameManager_Racing>();
    }

    private void WireEvents()
    {
        if (!mgr) return;
        mgr.OnCurrencyChanged += HandleCurrencyChanged;
        mgr.OnSprocketsChanged += HandleSprocketsChanged;
        mgr.OnLevelChanged += HandleLevelChanged;
        mgr.OnSkillRevealed += HandleSkillRevealed;
        mgr.OnSkillAvailabilityChanged += HandleSkillAvailabilityChanged;
        if (questMgr != null)
            questMgr.OnQuestUnlocked += HandleQuestUnlocked;
    }

    private void UnwireEvents()
    {
        if (!mgr) return;
        mgr.OnCurrencyChanged -= HandleCurrencyChanged;
        mgr.OnSprocketsChanged -= HandleSprocketsChanged;
        mgr.OnLevelChanged -= HandleLevelChanged;
        mgr.OnSkillRevealed -= HandleSkillRevealed;
        mgr.OnSkillAvailabilityChanged -= HandleSkillAvailabilityChanged;
        if (questMgr != null)
            questMgr.OnQuestUnlocked -= HandleQuestUnlocked;
    }

    private void HandleCurrencyChanged(int _) => RefreshAll();
    private void HandleSprocketsChanged(int _) => RefreshAll();
    private void HandleLevelChanged(SkillType _, int __) => RefreshAll();

    private void HandleSkillAvailabilityChanged()
    {
        SkillDefinition previouslySelected = selectedEntry != null ? selectedEntry.GetDefinition() : null;
        AttemptBuild();
        RefreshAll();
        if (!TryRestoreSelectionAfterRebuild(previouslySelected))
            AutoOpenFirstIfNeeded();
    }

    private void HandleSkillRevealed(SkillDefinition def)
    {
        SkillDefinition previouslySelected = selectedEntry != null ? selectedEntry.GetDefinition() : null;

        // Rebuild to include newly revealed skill(s)
        AttemptBuild();
        RefreshAll();
        if (!TryRestoreSelectionAfterRebuild(previouslySelected))
            AutoOpenFirstIfNeeded();
    }

    private void HandleQuestUnlocked(RacingQuestType _)
    {
        SkillDefinition previouslySelected = selectedEntry != null ? selectedEntry.GetDefinition() : null;
        AttemptBuild();
        RefreshAll();
        TryRestoreSelectionAfterRebuild(previouslySelected);
    }

    private void AttemptBuild()
    {
        buildSucceeded = false;
        ClearChildren();
        entries.Clear();
        if (!entryPrefab || !mgr || mgr.AllSkills == null || mgr.AllSkills.Count == 0)
        {
            if (verboseDebug) Debug.LogWarning("[RacingSkillUI] Missing prerequisites.");
            return;
        }

        var subset = mgr.AllSkills;
        foreach (var def in subset)
        {
            if (!def) continue;
            bool revealed = mgr.IsSkillRevealed(def.type);
            if (!revealed)
                continue; // filter unrevealed unless its quest is already completed
            RacingSkillUIEntry inst = Instantiate(entryPrefab,
                treeContent ? treeContent : contentParent);
            if (treeContent)
            {
                var rt = inst.GetComponent<RectTransform>();
                if (rt)
                    rt.anchoredPosition = def.uiPosition;
            }
            inst.Bind(def, mgr);
            inst.Selected += OnEntrySelected;
            entries.Add(inst);
        }

        buildSucceeded = entries.Count > 0;
    }

    private const string TreeViewportRaycastFillLegacyName = "TreeViewportRaycastFill";

    /// <summary>Removes the transparent fill added for the reverted global click-catcher workaround.</summary>
    private void DestroyLegacyTreeViewportRaycastFillIfPresent()
    {
        RectTransform host = treeViewport != null ? treeViewport : contentParent as RectTransform;
        if (host == null) return;
        var t = host.Find(TreeViewportRaycastFillLegacyName);
        if (t != null) Destroy(t.gameObject);
    }

    private void OnEntrySelected(SkillDefinition def)
    {
        if (GameplayUIInputGuard.IsDialogueBlockingGameplayUi) return;
        if (!def || !detailPanel) return;
        selectedEntry = entries.Find(e => e.GetDefinition() == def);
        ShowEntryDetail(selectedEntry);
    }

    /// <summary>
    /// Uses top UI raycast only — no full-tree blocking Image (that was stealing clicks from Buy when it sorted above the card).
    /// </summary>
    private void TryDismissDetailOnOutsideClick()
    {
        if (!Input.GetMouseButtonDown(0) || detailPanel == null || !detailPanel.IsInfoVisible || !dismissDetailOnOutsideClick)
            return;
        if (IsPanelOpen(questsPanelUI) || IsPanelOpen(inventoryPanelUI))
            return;

        var es = EventSystem.current;
        if (es == null) return;

        var ped = new PointerEventData(es) { position = Input.mousePosition };
        var results = new List<RaycastResult>(16);
        es.RaycastAll(ped, results);

        if (!ShouldDismissSkillDetailForRaycastResults(results))
            return;

        DismissSkillDetailFromOutsideClick();
    }

    /// <summary>
    /// If <b>any</b> raycast hit is the detail card, Buy, backdrop, a skill node, or toolbar, we do not dismiss.
    /// Using only <c>results[0]</c> breaks controller/Buy when tree or mask sorts above the card in the hit list.
    /// </summary>
    public bool ShouldDismissSkillDetailForRaycastResults(List<RaycastResult> results)
    {
        if (detailPanel == null || !detailPanel.IsInfoVisible || !dismissDetailOnOutsideClick)
            return false;
        if (results == null || results.Count == 0)
            return true;

        for (int i = 0; i < results.Count; i++)
        {
            GameObject go = results[i].gameObject;
            if (go == null) continue;
            if (detailPanel.IsHitInsideDetailUi(go))
                return false;
            if (IsRaycastHitSkillNode(go.transform))
                return false;
            if (IsRaycastHitToolbar(go.transform))
                return false;
        }

        return true;
    }

    public void DismissSkillDetailFromPointerOutside()
    {
        if (detailPanel == null || !detailPanel.IsInfoVisible)
            return;
        DismissSkillDetailFromOutsideClick();
    }

    private void DismissSkillDetailFromOutsideClick()
    {
        selectedEntry = null;
        detailPanel.HideInfo();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private bool IsRaycastHitSkillNode(Transform hitT)
    {
        if (hitT == null) return false;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            if (hitT == e.transform || hitT.IsChildOf(e.transform))
                return true;
        }
        return false;
    }

    private bool IsRaycastHitToolbar(Transform hitT)
    {
        if (hitT == null) return false;
        return IsUnderButton(hitT, playButton) || IsUnderButton(hitT, questButton) || IsUnderButton(hitT, inventoryButton);
    }

    private static bool IsUnderButton(Transform hitT, Button b)
    {
        if (b == null || hitT == null) return false;
        return hitT == b.transform || hitT.IsChildOf(b.transform);
    }

    private void HandleTreePanZoomGamepad()
    {
        if (!enableGamepadPanZoom) return;
        if (!treeViewport || !treeContent) return;
        if (!treeViewport.gameObject.activeInHierarchy) return;

        float dt = Time.unscaledDeltaTime;
        Vector2 stick = Vector2.zero;
        float zoomInput = 0f;

        if (RacingInputReader.Instance != null)
        {
            stick = RacingInputReader.Instance.Pan;
            if (invertRightStickY) stick.y = -stick.y;
            zoomInput = RacingInputReader.Instance.Zoom;
        }
        else
        {
            float x = 0f, y = 0f;
            if (!string.IsNullOrEmpty(gamepadPanAxisX)) x = Input.GetAxisRaw(gamepadPanAxisX);
            if (!string.IsNullOrEmpty(gamepadPanAxisY)) y = Input.GetAxisRaw(gamepadPanAxisY);
            if (invertRightStickY) y = -y;
            stick = new Vector2(x, y);
            if (stick.magnitude < gamepadStickDeadzone) stick = Vector2.zero;
            else stick = stick.normalized * ((stick.magnitude - gamepadStickDeadzone) / (1f - gamepadStickDeadzone));

            float rt = string.IsNullOrEmpty(gamepadZoomInAxis) ? 0f : Mathf.Clamp01(Input.GetAxisRaw(gamepadZoomInAxis));
            float lt = string.IsNullOrEmpty(gamepadZoomOutAxis) ? 0f : Mathf.Clamp01(Input.GetAxisRaw(gamepadZoomOutAxis));
            zoomInput = rt - lt;
        }

        if (stick != Vector2.zero)
        {
            Vector2 deltaPixels = stick * (gamepadPanPixelsPerSecond * dt);
            treeContent.anchoredPosition += deltaPixels * panSpeed;
        }

        if (Mathf.Abs(zoomInput) > 0.001f)
        {
        float cur = treeContent.localScale.x;
        float lo = Mathf.Min(minZoom, maxZoom);
        float hi = Mathf.Max(minZoom, maxZoom);
        float next = Mathf.Clamp(cur + (zoomInput * zoomStep * gamepadZoomSpeed * dt), lo, hi);
        treeContent.localScale = new Vector3(next, next, 1f);
        }
    }

    private void ShowEntryDetail(RacingSkillUIEntry entry)
    {
        if (!entry || !detailPanel) return;
        detailPanel.Show(entry.GetDefinition());
        detailPanel.OnHidden -= HandleDetailHidden;
        detailPanel.OnHidden += HandleDetailHidden;
    }

    private void HandleDetailHidden() => selectedEntry = null;

    private void ClearSelection() => selectedEntry = null;

    private void RefreshAll()
    {
        RefreshCurrency();
        foreach (var e in entries) e.Refresh();
    }

    private void RefreshCurrency()
    {
        if (currencyText && mgr)
            currencyText.text = $"Coins: {mgr.Currency}";

        if (sprocketsText && mgr)
        {
            bool show = mgr.HasEverEarnedSprockets || mgr.Sprockets > 0;
            sprocketsText.gameObject.SetActive(show);
            sprocketsText.text = $"Sprockets: {mgr.Sprockets}";
        }
    }

    private void ClearChildren()
    {
        Transform parent = treeContent ? treeContent : contentParent;
        if (!parent) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private void AutoOpenFirstIfNeeded()
    {
        if (!buildSucceeded || detailPanel == null) return;
        if (autoOpenFirstSkill && entries.Count > 0)
        {
            detailPanel.Show(entries[0].GetDefinition());
            selectedEntry = entries[0];
        }
        else
        {
            selectedEntry = null;
            detailPanel.HideInfo();
        }
    }

    private bool TryRestoreSelectionAfterRebuild(SkillDefinition previousDef)
    {
        if (detailPanel == null || previousDef == null || entries == null || entries.Count == 0)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            var d = e.GetDefinition();
            if (d == previousDef || (d != null && d.type == previousDef.type))
            {
                selectedEntry = e;
                ShowEntryDetail(e);
                return true;
            }
        }

        return false;
    }

    /// <summary>Overlay = null; Camera mode must pass world camera or screen rects read wrong.</summary>
    private Camera TreeUiEventCamera =>
        treeViewport != null ? treeViewport.GetComponentInParent<Canvas>()?.worldCamera : null;

    private void HandleTreePan()
    {
        if (!treeViewport || !treeContent || !rightMouseDragToPan) return;
        if (Input.GetMouseButtonDown(1) &&
            RectTransformUtility.RectangleContainsScreenPoint(treeViewport, Input.mousePosition, TreeUiEventCamera))
        {
            _isPanning = true;
            _lastMouse = Input.mousePosition;
        }
        if (_isPanning && Input.GetMouseButton(1))
        {
            var cur = (Vector2)Input.mousePosition;
            var delta = cur - _lastMouse;
            _lastMouse = cur;
            treeContent.anchoredPosition += delta * panSpeed;
        }
        if (Input.GetMouseButtonUp(1))
            _isPanning = false;
    }

    private void HandleTreeZoom()
    {
        if (!treeViewport || !treeContent) return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(treeViewport, Input.mousePosition, TreeUiEventCamera))
            return;
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.001f) return;
        float cur = treeContent.localScale.x;
        float lo = Mathf.Min(minZoom, maxZoom);
        float hi = Mathf.Max(minZoom, maxZoom);
        float next = Mathf.Clamp(cur + scroll * zoomStep, lo, hi);
        treeContent.localScale = new Vector3(next, next, 1f);
    }
}