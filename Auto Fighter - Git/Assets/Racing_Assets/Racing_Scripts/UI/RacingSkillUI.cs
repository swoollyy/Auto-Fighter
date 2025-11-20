using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-10)]
public class RacingSkillUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Transform contentParent;
    [SerializeField] private RacingSkillUIEntry entryPrefab;
    [SerializeField] private TMP_Text currencyText;

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
    [SerializeField] private bool autoOpenFirstSkill = true;
    [SerializeField] private bool verboseDebug = false;
    [SerializeField] private bool forceDeferredBuild = true;

    private bool _isPanning;
    private Vector2 _lastMouse;
    private Vector3 _contentBaseScale = Vector3.one;

    private RacingSkillTreeManager mgr;
    private GameManager_Racing gameManager;
    private readonly List<RacingSkillUIEntry> entries = new();
    private RacingSkillUIEntry selectedEntry;
    private bool buildSucceeded;

    void Awake()
    {
        mgr = RacingSkillTreeManager.Instance;
    }

    private void OnEnable()
    {
        EnsureManager();
        BindPlayButton();
        WireEvents();
        AttemptBuild(); // builds only revealed
        RefreshAll();
        if (treeContent) _contentBaseScale = treeContent.localScale;
        AutoOpenFirstIfNeeded();
    }

    private void OnDisable()
    {
        UnwireEvents();
    }

    void Update()
    {
        HandleTreePan();
        HandleTreeZoom();
    }

    public void BindGameManager(GameManager_Racing gm) => gameManager = gm;

    private void BindPlayButton()
    {
        if (playButton)
        {
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(OnPlayClicked);
        }
    }

    private void OnPlayClicked()
    {
        ClearSelection();
        detailPanel?.HideImmediate();
        if (!gameManager)
            gameManager = FindObjectOfType<GameManager_Racing>();
        gameManager?.BeginRun();
    }

    private void EnsureManager()
    {
        if (!mgr) mgr = RacingSkillTreeManager.Instance;
        if (detailPanel && mgr) detailPanel.Init(mgr);
        if (!gameManager) gameManager = FindObjectOfType<GameManager_Racing>();
    }

    private void WireEvents()
    {
        if (!mgr) return;
        mgr.OnCurrencyChanged += HandleCurrencyChanged;
        mgr.OnLevelChanged += HandleLevelChanged;
        mgr.OnSkillRevealed += HandleSkillRevealed; // NEW
    }

    private void UnwireEvents()
    {
        if (!mgr) return;
        mgr.OnCurrencyChanged -= HandleCurrencyChanged;
        mgr.OnLevelChanged -= HandleLevelChanged;
        mgr.OnSkillRevealed -= HandleSkillRevealed;
    }

    private void HandleCurrencyChanged(int _) => RefreshAll();
    private void HandleLevelChanged(SkillType _, int __) => RefreshAll();

    private void HandleSkillRevealed(SkillDefinition def)
    {
        // Rebuild to include newly revealed skill(s)
        AttemptBuild();
        RefreshAll();
        AutoOpenFirstIfNeeded();
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
            if (!mgr.IsSkillRevealed(def.type)) continue; // filter unrevealed
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

    private void OnEntrySelected(SkillDefinition def)
    {
        if (!def || !detailPanel) return;
        selectedEntry = entries.Find(e => e.GetDefinition() == def);
        ShowEntryDetail(selectedEntry);
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
            currencyText.text = $"Currency: {mgr.Currency}";
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
        if (!autoOpenFirstSkill || !buildSucceeded || detailPanel == null) return;
        if (entries.Count > 0)
        {
            detailPanel.Show(entries[0].GetDefinition());
            selectedEntry = entries[0];
        }
    }

    private void HandleTreePan()
    {
        if (!treeViewport || !treeContent || !rightMouseDragToPan) return;
        if (Input.GetMouseButtonDown(1) &&
            RectTransformUtility.RectangleContainsScreenPoint(treeViewport, Input.mousePosition))
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
        if (!RectTransformUtility.RectangleContainsScreenPoint(treeViewport, Input.mousePosition))
            return;
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.001f) return;
        float cur = treeContent.localScale.x;
        float next = Mathf.Clamp(cur + scroll * zoomStep, minZoom, maxZoom);
        treeContent.localScale = new Vector3(next, next, 1f);
    }
}