using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RacingQuestsPanelUI : MonoBehaviour
{
    [Serializable]
    private struct QuestRowBinding
    {
        public RacingQuestType questType;
        public TMP_Text nameText;
        public TMP_Text descriptionText;
        public TMP_Text rewardText;
        public TMP_Text progressText;
        public Image progressFill;
        public GameObject completedBadge;
    }

    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Quest Rows")]
    [SerializeField] private QuestRowBinding[] rows;

    [Header("Optional Summary")]
    [SerializeField] private TMP_Text completionSummaryText;
    [SerializeField] private bool overwriteQuestLabelText = false;

    private RacingQuestUnlockManager _quests;

    private void Start()
    {
        // Panels should be closed by default and only opened by button press.
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void Update()
    {
        if (panelRoot == null || !panelRoot.activeSelf) return;

        if (IsBackClosePressed())
            HidePanel();
    }

    private static bool IsBackClosePressed()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) return true;
        if (Input.GetKeyDown(KeyCode.JoystickButton3)) return true; // PS Triangle fallback
        if (RacingInputReader.Instance != null && RacingInputReader.Instance.MashNorthDown) return true; // PS Triangle / Xbox Y
        return false;
    }

    private void OnEnable()
    {
        _quests = RacingQuestUnlockManager.Instance;
        if (_quests != null)
        {
            _quests.OnQuestProgressChanged += HandleQuestProgressChanged;
            _quests.OnQuestUnlocked += HandleQuestUnlocked;
        }

        RefreshAllRows();
    }

    private void OnDisable()
    {
        if (_quests != null)
        {
            _quests.OnQuestProgressChanged -= HandleQuestProgressChanged;
            _quests.OnQuestUnlocked -= HandleQuestUnlocked;
        }
    }

    public void ShowPanel()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        RefreshAllRows();
    }

    public void HidePanel()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void TogglePanel()
    {
        if (panelRoot == null) return;
        panelRoot.SetActive(!panelRoot.activeSelf);
        if (panelRoot.activeSelf) RefreshAllRows();
    }

    private void HandleQuestProgressChanged(RacingQuestType _)
    {
        RefreshAllRows();
    }

    private void HandleQuestUnlocked(RacingQuestType _)
    {
        RefreshAllRows();
    }

    private void RefreshAllRows()
    {
        if (_quests == null)
            _quests = RacingQuestUnlockManager.Instance;
        if (_quests == null) return;

        int completed = 0;
        int total = rows != null ? rows.Length : 0;

        for (int i = 0; i < total; i++)
        {
            var row = rows[i];
            var snap = _quests.GetSnapshot(row.questType);

            if (overwriteQuestLabelText)
            {
                if (row.nameText != null) row.nameText.text = snap.title;
                if (row.descriptionText != null) row.descriptionText.text = snap.description;
                if (row.rewardText != null) row.rewardText.text = snap.reward;
            }

            int clampedCurrent = Mathf.Clamp(snap.current, 0, Mathf.Max(1, snap.required));
            if (row.progressText != null)
            {
                row.progressText.text = snap.unlocked
                    ? "Completed"
                    : $"{clampedCurrent} / {Mathf.Max(1, snap.required)}";
            }

            if (row.progressFill != null)
            {
                float t = snap.required <= 0 ? 1f : Mathf.Clamp01(snap.current / (float)snap.required);
                row.progressFill.fillAmount = snap.unlocked ? 1f : t;
            }

            if (row.completedBadge != null)
                row.completedBadge.SetActive(snap.unlocked);

            if (snap.unlocked) completed++;
        }

        if (completionSummaryText != null)
            completionSummaryText.text = $"Quests Completed: {completed}/{Mathf.Max(0, total)}";
    }
}
