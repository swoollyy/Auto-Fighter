using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class RacingRunInventoryPanelUI : MonoBehaviour
{
    [Serializable]
    private struct InventoryItemButtonBinding
    {
        public Button button;
        public Image iconImage;
        public TMP_Text nameText;
        public TMP_Text stateText;
        public GameObject lockedBadge;
        public GameObject equippedBadge;
    }

    [Serializable]
    private struct ActiveSlotBinding
    {
        public Button button;
        public Image iconImage;
        public TMP_Text labelText;
    }

    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Inventory Items")]
    [Tooltip("If enabled, item buttons are populated by unlock order (first unlocked appears first), not by hardcoded item per row.")]
    [SerializeField] private bool useUnlockOrderForInventoryButtons = true;
    [SerializeField] private InventoryItemButtonBinding[] inventoryItemButtons;
    [SerializeField] private Sprite forcefieldIcon;
    [SerializeField] private Sprite turretIcon;
    [SerializeField] private Sprite coinFriendIcon;

    [Header("Active Slots")]
    [SerializeField] private ActiveSlotBinding[] activeSlots;
    [SerializeField] private TMP_Text slotCountText;

    [Header("Optional Summary")]
    [SerializeField] private TMP_Text equippedSummaryText;

    [Header("Play Warning (Optional)")]
    [SerializeField] private GameObject noActiveItemsWarningRoot;
    [SerializeField] private Toggle neverShowWarningToggle;
    [SerializeField] private Button warningContinueButton;
    [SerializeField] private Button warningCancelButton;

    private RacingQuestUnlockManager _quests;
    private RacingSkillTreeManager _skills;
    private const string KeyHideNoActiveWarning = "Inventory_HideNoActiveQuestItemsWarning_v1";
    private RacingQuestRunItem[] _displayItems = Array.Empty<RacingQuestRunItem>();
    private RacingQuestRunItem _draggingItem = RacingQuestRunItem.None;
    private bool _dragStartedFromInventory;
    private GameObject _dragGhost;
    private Image _dragGhostImage;

    private void Start()
    {
        // Inventory panel should be closed by default at runtime.
        if (panelRoot != null)
            panelRoot.SetActive(false);

        if (noActiveItemsWarningRoot != null)
            noActiveItemsWarningRoot.SetActive(false);
    }

    private void Awake()
    {
        WireInventoryButtons();
        WireWarningButtons();
    }

    private void OnEnable()
    {
        _quests = RacingQuestUnlockManager.Instance;
        _skills = RacingSkillTreeManager.Instance;
        if (_quests != null)
        {
            _quests.OnInventoryChanged += HandleInventoryChanged;
            _quests.OnQuestUnlocked += HandleQuestUnlocked;
        }
        if (_skills != null)
        {
            _skills.OnLevelChanged += HandleSkillLevelChanged;
            _skills.OnSkillsReset += HandleSkillsReset;
        }
        RefreshAll();
    }

    private void OnDisable()
    {
        if (_quests != null)
        {
            _quests.OnInventoryChanged -= HandleInventoryChanged;
            _quests.OnQuestUnlocked -= HandleQuestUnlocked;
        }
        if (_skills != null)
        {
            _skills.OnLevelChanged -= HandleSkillLevelChanged;
            _skills.OnSkillsReset -= HandleSkillsReset;
        }
    }

    private void Update()
    {
        bool warningOpen = noActiveItemsWarningRoot != null && noActiveItemsWarningRoot.activeSelf;
        bool panelOpen = panelRoot != null && panelRoot.activeSelf;
        if (!warningOpen && !panelOpen) return;

        if (IsBackClosePressed())
        {
            if (warningOpen)
                HideNoActiveItemsWarning();
            else
                HidePanel();
        }
    }

    private static bool IsBackClosePressed()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) return true;
        if (Input.GetKeyDown(KeyCode.JoystickButton3)) return true; // PS Triangle fallback
        if (RacingInputReader.Instance != null && RacingInputReader.Instance.MashNorthDown) return true; // PS Triangle / Xbox Y
        return false;
    }

    public void ShowPanel()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        RefreshAll();
    }

    public void HidePanel()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        HideNoActiveItemsWarning();
    }

    public void TogglePanel()
    {
        if (panelRoot == null) return;
        panelRoot.SetActive(!panelRoot.activeSelf);
        if (panelRoot.activeSelf) RefreshAll();
        else HideNoActiveItemsWarning();
    }

    public bool IsPanelOpen => panelRoot != null && panelRoot.activeSelf;

    /// <summary>
    /// Called by Play flow. Returns true if run can proceed now.
    /// If false, a warning popup was shown and caller should abort start.
    /// </summary>
    public bool CheckPlayWarningGate(Action onContinueConfirmed = null)
    {
        if (!HelpPatronProgress.AreQuestsAndInventoryUnlocked)
            return true;
        if (!ShouldShowNoActiveItemsWarning())
            return true;

        ShowNoActiveItemsWarning(onContinueConfirmed);
        return false;
    }

    private void HandleInventoryChanged()
    {
        RefreshAll();
    }

    private void HandleQuestUnlocked(RacingQuestType _)
    {
        RefreshAll();
    }

    private void HandleSkillLevelChanged(SkillType _, int __)
    {
        RefreshAll();
    }

    private void HandleSkillsReset()
    {
        RefreshAll();
    }

    private void WireInventoryButtons()
    {
        if (inventoryItemButtons == null) return;
        for (int i = 0; i < inventoryItemButtons.Length; i++)
        {
            int idx = i;
            var btn = inventoryItemButtons[i].button;
            if (btn == null) continue;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnInventoryItemPressed(idx));
            EnsureDragHandlers(btn.gameObject, idx, true);
        }
    }

    private void WireActiveSlotButtonsForDrag()
    {
        if (activeSlots == null) return;
        for (int i = 0; i < activeSlots.Length; i++)
        {
            var btn = activeSlots[i].button;
            if (btn == null) continue;
            EnsureDragHandlers(btn.gameObject, i, false);
        }
    }

    private void WireWarningButtons()
    {
        if (warningContinueButton != null)
        {
            warningContinueButton.onClick.RemoveAllListeners();
            warningContinueButton.onClick.AddListener(OnWarningContinuePressed);
        }
        if (warningCancelButton != null)
        {
            warningCancelButton.onClick.RemoveAllListeners();
            warningCancelButton.onClick.AddListener(HideNoActiveItemsWarning);
        }
    }

    private Action _pendingPlayContinueAction;

    private void ShowNoActiveItemsWarning(Action onContinueConfirmed)
    {
        _pendingPlayContinueAction = onContinueConfirmed;
        if (noActiveItemsWarningRoot != null)
            noActiveItemsWarningRoot.SetActive(true);

        if (neverShowWarningToggle != null)
            neverShowWarningToggle.isOn = PlayerPrefs.GetInt(KeyHideNoActiveWarning, 0) == 1;
    }

    private void HideNoActiveItemsWarning()
    {
        if (noActiveItemsWarningRoot != null)
            noActiveItemsWarningRoot.SetActive(false);
        _pendingPlayContinueAction = null;
    }

    private void OnWarningContinuePressed()
    {
        if (neverShowWarningToggle != null)
        {
            PlayerPrefs.SetInt(KeyHideNoActiveWarning, neverShowWarningToggle.isOn ? 1 : 0);
            PlayerPrefs.Save();
        }

        var callback = _pendingPlayContinueAction;
        HideNoActiveItemsWarning();
        callback?.Invoke();
    }

    private bool ShouldShowNoActiveItemsWarning()
    {
        if (PlayerPrefs.GetInt(KeyHideNoActiveWarning, 0) == 1)
            return false;

        var quest = _quests ?? RacingQuestUnlockManager.Instance;
        if (quest == null) return false;

        int unlockedItems = 0;
        foreach (RacingQuestRunItem item in Enum.GetValues(typeof(RacingQuestRunItem)))
        {
            if (quest.IsItemAvailableToEquip(item))
                unlockedItems++;
        }

        if (unlockedItems <= 0) return false;
        return quest.EquippedItems == null || quest.EquippedItems.Count == 0;
    }

    private void OnInventoryItemPressed(int index)
    {
        if (_quests == null || inventoryItemButtons == null || index < 0 || index >= inventoryItemButtons.Length)
            return;

        RacingQuestRunItem item = GetDisplayItemForButton(index);
        if (!_quests.IsItemAvailableToEquip(item))
            return;

        bool isEquipped = _quests.IsItemEquipped(item);
        if (isEquipped)
            _quests.UnequipItem(item);
        else
            _quests.TryEquipItem(item);
    }

    private void RefreshAll()
    {
        if (_quests == null)
            _quests = RacingQuestUnlockManager.Instance;
        if (_quests == null) return;

        WireActiveSlotButtonsForDrag();
        RefreshInventoryButtons();
        RefreshActiveSlots();
    }

    private void RefreshInventoryButtons()
    {
        if (inventoryItemButtons == null) return;
        RebuildDisplayItemOrder();

        for (int i = 0; i < inventoryItemButtons.Length; i++)
        {
            var row = inventoryItemButtons[i];
            bool hasMappedItem = i < _displayItems.Length;
            RacingQuestRunItem mappedItem = hasMappedItem ? _displayItems[i] : RacingQuestRunItem.Forcefield;

            if (row.button != null)
                row.button.gameObject.SetActive(true);

            if (useUnlockOrderForInventoryButtons && !hasMappedItem)
            {
                if (row.nameText != null)
                    row.nameText.text = "Empty";
                if (row.stateText != null)
                    row.stateText.text = "No Item";
                if (row.iconImage != null)
                {
                    row.iconImage.sprite = null;
                    row.iconImage.enabled = false;
                }
                if (row.lockedBadge != null) row.lockedBadge.SetActive(false);
                if (row.equippedBadge != null) row.equippedBadge.SetActive(false);
                if (row.button != null) row.button.interactable = false;
                continue;
            }

            bool unlocked = _quests.IsItemAvailableToEquip(mappedItem);
            bool equipped = _quests.IsItemEquipped(mappedItem);

            if (row.nameText != null)
                row.nameText.text = GetItemDisplayName(mappedItem);
            if (row.iconImage != null)
            {
                row.iconImage.sprite = GetItemIcon(mappedItem);
                row.iconImage.enabled = row.iconImage.sprite != null;
            }

            if (row.stateText != null)
            {
                if (!unlocked) row.stateText.text = "Locked";
                else row.stateText.text = equipped ? "Equipped" : "In Inventory";
            }

            if (row.lockedBadge != null) row.lockedBadge.SetActive(!unlocked);
            if (row.equippedBadge != null) row.equippedBadge.SetActive(equipped);

            if (row.button != null)
                row.button.interactable = unlocked;
        }
    }

    private void RefreshActiveSlots()
    {
        int slots = Mathf.Max(1, _quests.UnlockedItemSlots);

        if (slotCountText != null)
            slotCountText.text = $"Slots: {Mathf.Min(slots, activeSlots != null ? activeSlots.Length : slots)}";

        int visibleSlots = activeSlots != null ? activeSlots.Length : 0;
        int equippedCount = _quests.GetEquippedCount();

        if (equippedSummaryText != null)
            equippedSummaryText.text = $"Equipped: {equippedCount}/{slots}";

        for (int i = 0; i < visibleSlots; i++)
        {
            bool slotUnlocked = i < slots;
            RacingQuestRunItem slotItem = _quests.GetEquippedItemAtSlot(i);
            bool hasItem = slotItem != RacingQuestRunItem.None;

            var slot = activeSlots[i];

            if (slot.button != null)
            {
                slot.button.gameObject.SetActive(slotUnlocked);
                slot.button.onClick.RemoveAllListeners();
                if (slotUnlocked && hasItem)
                {
                    int idx = i;
                    slot.button.onClick.AddListener(() => UnequipSlotIndex(idx));
                }
            }

            if (slot.labelText != null)
            {
                if (!slotUnlocked) slot.labelText.text = "Locked";
                else slot.labelText.text = hasItem ? GetItemDisplayName(slotItem) : "Empty";
            }
            if (slot.iconImage != null)
            {
                slot.iconImage.sprite = hasItem ? GetItemIcon(slotItem) : null;
                slot.iconImage.enabled = hasItem && slot.iconImage.sprite != null;
            }
        }
    }

    private void UnequipSlotIndex(int index)
    {
        if (_quests == null) return;
        RacingQuestRunItem item = _quests.GetEquippedItemAtSlot(index);
        if (item == RacingQuestRunItem.None) return;
        _quests.UnequipItem(item);
    }

    private static string GetItemDisplayName(RacingQuestRunItem item)
    {
        switch (item)
        {
            case RacingQuestRunItem.Forcefield: return "Forcefield";
            case RacingQuestRunItem.Turret: return "Turret";
            default: return "Collection Friend";
        }
    }

    private Sprite GetItemIcon(RacingQuestRunItem item)
    {
        switch (item)
        {
            case RacingQuestRunItem.Forcefield: return forcefieldIcon;
            case RacingQuestRunItem.Turret: return turretIcon;
            case RacingQuestRunItem.CoinFriend: return coinFriendIcon;
            default: return null;
        }
    }

    private RacingQuestRunItem GetDisplayItemForButton(int index)
    {
        if (useUnlockOrderForInventoryButtons && _displayItems != null && index >= 0 && index < _displayItems.Length)
            return _displayItems[index];

        return RacingQuestRunItem.Forcefield;
    }

    private void RebuildDisplayItemOrder()
    {
        if (_quests == null)
        {
            _displayItems = Array.Empty<RacingQuestRunItem>();
            return;
        }

        if (!useUnlockOrderForInventoryButtons)
        {
            _displayItems = Array.Empty<RacingQuestRunItem>();
            return;
        }

        var unlocked = _quests.UnlockedItemsInInventoryOrder;
        if (unlocked == null || unlocked.Count == 0)
        {
            _displayItems = Array.Empty<RacingQuestRunItem>();
            return;
        }

        var list = new System.Collections.Generic.List<RacingQuestRunItem>(unlocked.Count);
        for (int i = 0; i < unlocked.Count; i++)
        {
            var item = unlocked[i];
            // Item appears in inventory only after its unlock skill node is purchased.
            if (!_quests.IsItemAvailableToEquip(item)) continue;
            if (_quests.IsItemEquipped(item)) continue;
            list.Add(item);
        }
        _displayItems = list.ToArray();
    }

    private void EnsureDragHandlers(GameObject target, int index, bool isInventorySource)
    {
        if (target == null) return;
        var handler = target.GetComponent<InventoryDragHandler>();
        if (handler == null) handler = target.AddComponent<InventoryDragHandler>();
        handler.Bind(this, index, isInventorySource);
    }

    private void StartDraggingItem(RacingQuestRunItem item, bool fromInventory)
    {
        if (item == RacingQuestRunItem.None) return;
        _draggingItem = item;
        _dragStartedFromInventory = fromInventory;

        if (_dragGhost == null)
        {
            _dragGhost = new GameObject("InventoryDragGhost");
            _dragGhost.transform.SetParent(transform.root, false);
            _dragGhostImage = _dragGhost.AddComponent<Image>();
            _dragGhostImage.raycastTarget = false;
            var rt = _dragGhost.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(56f, 56f);
        }

        _dragGhostImage.sprite = GetItemIcon(item);
        _dragGhostImage.enabled = _dragGhostImage.sprite != null;
        _dragGhost.SetActive(_dragGhostImage.enabled);
        UpdateDragGhostPosition();
    }

    private void UpdateDragGhostPosition()
    {
        if (_dragGhost == null || !_dragGhost.activeSelf) return;
        _dragGhost.transform.position = Input.mousePosition;
    }

    private void EndDraggingItem()
    {
        _draggingItem = RacingQuestRunItem.None;
        _dragStartedFromInventory = false;
        if (_dragGhost != null) _dragGhost.SetActive(false);
    }

    private void TryDropOnActiveSlot(int slotIndex)
    {
        if (_quests == null) return;
        if (_draggingItem == RacingQuestRunItem.None) return;
        _quests.TryAssignItemToSlot(_draggingItem, slotIndex);
    }

    private void TryDropBackToInventory()
    {
        if (_quests == null) return;
        if (_draggingItem == RacingQuestRunItem.None) return;
        if (_dragStartedFromInventory) return;
        _quests.UnequipItem(_draggingItem);
    }

    private sealed class InventoryDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        private RacingRunInventoryPanelUI _owner;
        private int _index;
        private bool _isInventorySource;

        public void Bind(RacingRunInventoryPanelUI owner, int index, bool isInventorySource)
        {
            _owner = owner;
            _index = index;
            _isInventorySource = isInventorySource;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_owner == null) return;

            RacingQuestRunItem item = RacingQuestRunItem.None;
            if (_isInventorySource)
            {
                item = _owner.GetDisplayItemForButton(_index);
                if (_owner._quests == null || !_owner._quests.IsItemAvailableToEquip(item))
                    item = RacingQuestRunItem.None;
            }
            else
            {
                if (_owner._quests != null)
                    item = _owner._quests.GetEquippedItemAtSlot(_index);
            }

            _owner.StartDraggingItem(item, _isInventorySource);
        }

        public void OnDrag(PointerEventData eventData)
        {
            _owner?.UpdateDragGhostPosition();
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (_owner == null) return;
            if (_isInventorySource)
                _owner.TryDropBackToInventory();
            else
                _owner.TryDropOnActiveSlot(_index);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _owner?.EndDraggingItem();
        }
    }
}
