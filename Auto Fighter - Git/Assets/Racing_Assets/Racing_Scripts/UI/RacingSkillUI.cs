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
    [SerializeField] private Button playButton;            // NEW: replaces Back
    [SerializeField] private bool autoOpenFirstSkill = true;
    [SerializeField] private bool verboseDebug = false;
    [SerializeField] private bool forceDeferredBuild = true;

    private bool _isPanning;
    private Vector2 _lastMouse;
    private Vector3 _contentBaseScale = Vector3.one;

    private RacingSkillTreeManager mgr;
    private GameManager_Racing gameManager;                // NEW
    private readonly List<RacingSkillUIEntry> entries = new();
    private RacingSkillUIEntry selectedEntry;              // NEW
    private bool buildSucceeded;

    void Awake()
    {
        mgr = RacingSkillTreeManager.Instance;
    }

    private void OnEnable()
    {
        EnsureManager();
        BindPlayButton();               // NEW
        TryWireEvents();
        if (verboseDebug) DumpPrereqStatus();
        AttemptBuild();
        RefreshAll();
        if (treeContent) _contentBaseScale = treeContent.localScale;
        if (autoOpenFirstSkill && buildSucceeded && detailPanel && entries.Count > 0)
            ShowEntryDetail(entries[0]);
    }

    private void DumpPrereqStatus()
    {
        Debug.Log($"[RacingSkillUI] Status - entryPrefab: {entryPrefab}, mgr: {(mgr ? "ok" : "null")}, skills: {(mgr ? mgr.AllSkills?.Count : 0)}, contentParent: {contentParent}, treeViewport: {treeViewport}, treeContent: {treeContent}, playButton: {playButton}");
    }

    private System.Collections.IEnumerator Start()
    {
        if (forceDeferredBuild)
        {
            yield return null;
            if (!buildSucceeded)
            {
                if (verboseDebug) Debug.Log("[RacingSkillUI] Deferred build attempt.");
                EnsureManager();
                AttemptBuild();
                RefreshAll();
                if (autoOpenFirstSkill && buildSucceeded && detailPanel && entries.Count > 0)
                    ShowEntryDetail(entries[0]);
            }
            if (!buildSucceeded)
            {
                yield return null;
                if (!buildSucceeded && verboseDebug)
                    Debug.LogWarning("[RacingSkillUI] Second deferred build failed.");
            }
        }
    }

    void OnDisable()
    {
        UnwireEvents();
    }

    void Update()
    {
        if (!buildSucceeded)
        {
            EnsureManager();
            AttemptBuild();
        }

        if (treeViewport && treeContent)
        {
            HandleTreePan();
            HandleTreeZoom();
        }
    }

    public void BindGameManager(GameManager_Racing gm) => gameManager = gm; // NEW public API

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
        // Hide detail if open
        ClearSelection();
        detailPanel?.HideImmediate();

        // Start race
        if (gameManager == null)
            gameManager = FindObjectOfType<GameManager_Racing>();

        if (gameManager != null)
            gameManager.BeginRun();
        else
            Debug.LogWarning("[RacingSkillUI] GameManager_Racing not found for Play button.");

        // Optionally deactivate skill UI root (manager handles root hiding)
        // gameObject.SetActive(false);
    }

    private void EnsureManager()
    {
        if (!mgr) mgr = RacingSkillTreeManager.Instance;
        if (detailPanel && mgr) detailPanel.Init(mgr);
        if (!gameManager) gameManager = FindObjectOfType<GameManager_Racing>();
    }

    private void AttemptBuild()
    {
        if (buildSucceeded) return;

        ClearChildren();
        entries.Clear();

        if (!entryPrefab || !mgr || mgr.AllSkills == null || mgr.AllSkills.Count == 0)
        {
            if (verboseDebug) Debug.LogWarning("[RacingSkillUI] Missing prerequisites.");
            return;
        }

        if (treeViewport && treeContent)
            BuildTree();
        else if (contentParent)
            BuildList();
        else
        {
            Debug.LogWarning("[RacingSkillUI] No layout targets.");
            return;
        }

        buildSucceeded = entries.Count > 0;
        Debug.Log(buildSucceeded
            ? $"[RacingSkillUI] Build succeeded ({entries.Count} skills)."
            : "[RacingSkillUI] Build produced zero nodes.");
    }

    private void BuildList()
    {
        foreach (var def in mgr.AllSkills)
        {
            if (!def) continue;
            var inst = Instantiate(entryPrefab, contentParent);
            BindEntry(inst, def);
        }
    }

    private void BuildTree()
    {
        foreach (var def in mgr.AllSkills)
        {
            if (!def) continue;
            var inst = Instantiate(entryPrefab, treeContent);
            var rt = inst.GetComponent<RectTransform>();
            if (rt)
            {
                rt.anchoredPosition = def.uiPosition;
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
            BindEntry(inst, def);
        }
    }

    private void BindEntry(RacingSkillUIEntry inst, SkillDefinition def)
    {
        inst.Bind(def, mgr);
        inst.Selected -= OnEntrySelected;
        inst.Selected += OnEntrySelected;
        entries.Add(inst);
    }

    private void OnEntrySelected(SkillDefinition def)
    {
        if (!def || !detailPanel) return;
        var entry = entries.Find(e => e.GetDefinition() == def);
        selectedEntry = entry;
        ShowEntryDetail(entry);
    }

    private void ShowEntryDetail(RacingSkillUIEntry entry)
    {
        if (detailPanel == null || entry == null) return;
        detailPanel.Show(entry.GetDefinition());
        detailPanel.OnHidden -= HandleDetailHidden;
        detailPanel.OnHidden += HandleDetailHidden;
    }

    private void HandleDetailHidden()
    {
        selectedEntry = null;
    }

    public void ClearSelection()
    {
        selectedEntry = null;
    }

    private void HandleTreePan()
    {
        if (!rightMouseDragToPan) return;
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
        if (Input.GetMouseButtonUp(1)) _isPanning = false;
    }

    private void HandleTreeZoom()
    {
        if (!RectTransformUtility.RectangleContainsScreenPoint(treeViewport, Input.mousePosition))
            return;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.001f) return;

        float cur = treeContent.localScale.x;
        float next = Mathf.Clamp(cur + scroll * zoomStep, minZoom, maxZoom);
        treeContent.localScale = new Vector3(next, next, 1f);
    }

    private void TryWireEvents()
    {
        if (!mgr) return;
        mgr.OnCurrencyChanged -= HandleCurrencyChanged;
        mgr.OnCurrencyChanged += HandleCurrencyChanged;
        mgr.OnLevelChanged -= HandleLevelChanged;
        mgr.OnLevelChanged += HandleLevelChanged;
    }

    private void UnwireEvents()
    {
        if (!mgr) return;
        mgr.OnCurrencyChanged -= HandleCurrencyChanged;
        mgr.OnLevelChanged -= HandleLevelChanged;
        if (detailPanel) detailPanel.OnHidden -= HandleDetailHidden;
    }

    private void HandleCurrencyChanged(int _) => RefreshAll();
    private void HandleLevelChanged(SkillType _, int __) => RefreshAll();

    private void RefreshAll()
    {
        RefreshCurrency();
        for (int i = 0; i < entries.Count; i++)
            entries[i].Refresh();
    }

    private void RefreshCurrency()
    {
        if (currencyText && mgr)
            currencyText.text = $"Currency: {mgr.Currency}";
    }

    private void ClearChildren()
    {
        if (contentParent)
            for (int i = contentParent.childCount - 1; i >= 0; i--)
                Destroy(contentParent.GetChild(i).gameObject);
        if (treeContent)
            for (int i = treeContent.childCount - 1; i >= 0; i--)
                Destroy(treeContent.GetChild(i).gameObject);
    }
}