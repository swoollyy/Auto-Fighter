using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Day / Trial progression.
///
/// The game is a sequence of trials. Each trial says: "by day X, reach Y progress (0-1) along the road."
/// Every completed run counts as one day. If the player reaches the target progress on ANY run within
/// the trial's day limit, they advance to the next trial immediately (day resets to 1 and the current
/// progression is snapshotted as the new trial's baseline). If the day limit is reached without ever
/// hitting the target, the trial is failed: all purchasable progression (skill levels + coins + sprockets)
/// is reverted to the snapshot taken at the start of that trial, and the day resets to 1 so the player
/// retries the same trial from its starting state. Quest unlocks are never reverted.
///
/// When the final trial is beaten, <see cref="OnAllTrialsCompleted"/> fires (endless mode hooks in here later).
///
/// Persistence: day, trial index, completion flag, and the baseline snapshot all save to PlayerPrefs.
/// While <see cref="persistAcrossSessions"/> is false (current testing setup, alongside the TEMP
/// ClearAllData() in RacingSkillTreeManager.Awake), saved state is ignored and reset fresh on launch.
/// </summary>
[DefaultExecutionOrder(-50)] // after RacingSkillTreeManager (-100) so its Instance + state exist
public class DayTrialManager : MonoBehaviour
{
    public static DayTrialManager Instance { get; private set; }

    [Serializable]
    public class TrialDefinition
    {
        [Tooltip("Optional label shown in the inspector / logs (e.g. 'Trial 1 - The Outskirts').")]
        public string label;

        [Tooltip("How many days (runs) the player gets to reach the target progress before this trial fails.")]
        [Min(1)] public int dayLimit = 5;

        [Tooltip("Road progress (0 = start, 1 = end of track) the player must reach on any single run within the day limit to advance.")]
        [Range(0f, 1f)] public float targetProgress = 0.5f;
    }

    [Header("Trials (played in order, top to bottom)")]
    [SerializeField] private List<TrialDefinition> trials = new();

    [Header("Persistence")]
    [Tooltip("When true, day/trial/baseline load from the save file across sessions. Keep this FALSE while testing with the TEMP ClearAllData() in RacingSkillTreeManager.Awake; turn both on together for real persistence.")]
    [SerializeField] private bool persistAcrossSessions = false;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    // ---- Runtime state ----
    public int CurrentDay { get; private set; } = 1;
    public int CurrentTrialIndex { get; private set; } = 0;
    public bool AllTrialsCompleted { get; private set; } = false;

    private SkillProgressSnapshot _baseline;

    // ---- Events (for UI / narrative) ----
    /// <summary>Fired whenever day, trial index, or completion state changes (good for refreshing a HUD).</summary>
    public event Action OnStateChanged;
    /// <summary>Fired when the player advances to a new trial (arg = new trial index).</summary>
    public event Action<int> OnTrialAdvanced;
    /// <summary>Fired when a trial is failed and progression has been reverted (arg = trial index retried).</summary>
    public event Action<int> OnTrialFailed;
    /// <summary>Fired once when the final trial is beaten. Hook endless mode here later.</summary>
    public event Action OnAllTrialsCompleted;

    // ---- Persistence keys ----
    private const string DayKey = "RacingTrial_Day";
    private const string IndexKey = "RacingTrial_Index";
    private const string AllDoneKey = "RacingTrial_AllDone";
    private const string BaselineKey = "RacingTrial_Baseline";

    // ---- Public accessors ----
    public int TrialCount => trials.Count;
    public TrialDefinition CurrentTrial =>
        (CurrentTrialIndex >= 0 && CurrentTrialIndex < trials.Count) ? trials[CurrentTrialIndex] : null;
    public float CurrentTargetProgress => CurrentTrial != null ? CurrentTrial.targetProgress : 1f;
    public int CurrentDayLimit => CurrentTrial != null ? CurrentTrial.dayLimit : 0;

    private void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (persistAcrossSessions && PlayerPrefs.HasKey(DayKey))
            LoadState();
        else
            ResetProgressionToStart();
    }

    /// <summary>
    /// Call this once per completed run. <paramref name="normalizedProgressReached"/> is the furthest
    /// road progress (0-1) the player reached during that run.
    /// </summary>
    public void NotifyRunCompleted(float normalizedProgressReached)
    {
        if (AllTrialsCompleted)
            return; // Endless mode (added later) takes over past the final trial.

        var trial = CurrentTrial;
        if (trial == null)
        {
            if (verboseLogging)
                Debug.LogWarning("[DayTrial] No trial configured; run ignored. Add entries to the Trials list.");
            return;
        }

        bool reachedTarget = normalizedProgressReached >= trial.targetProgress;

        if (verboseLogging)
            Debug.Log($"[DayTrial] Run done. Trial {CurrentTrialIndex} ('{trial.label}') day {CurrentDay}/{trial.dayLimit}. " +
                      $"Reached {normalizedProgressReached:P0} vs target {trial.targetProgress:P0} -> {(reachedTarget ? "PASS" : "no")}.");

        if (reachedTarget)
        {
            AdvanceToNextTrial();
        }
        else if (CurrentDay >= trial.dayLimit)
        {
            FailCurrentTrial();
        }
        else
        {
            CurrentDay++;
            SaveState();
            OnStateChanged?.Invoke();
        }
    }

    private void AdvanceToNextTrial()
    {
        int next = CurrentTrialIndex + 1;

        if (next >= trials.Count)
        {
            AllTrialsCompleted = true;
            SaveState();
            if (verboseLogging) Debug.Log("[DayTrial] All trials completed!");
            OnAllTrialsCompleted?.Invoke();
            OnStateChanged?.Invoke();
            return;
        }

        CurrentTrialIndex = next;
        CurrentDay = 1;
        CaptureBaseline(); // snapshot the (kept) progression entering the new trial
        SaveState();

        if (verboseLogging) Debug.Log($"[DayTrial] Advanced to trial {CurrentTrialIndex} ('{CurrentTrial?.label}').");
        OnTrialAdvanced?.Invoke(CurrentTrialIndex);
        OnStateChanged?.Invoke();
    }

    private void FailCurrentTrial()
    {
        RestoreBaseline(); // revert skills + coins + sprockets to start-of-trial state (quest unlocks untouched)
        CurrentDay = 1;
        SaveState();

        if (verboseLogging) Debug.Log($"[DayTrial] Trial {CurrentTrialIndex} failed; progression reverted to its day-1 baseline.");
        OnTrialFailed?.Invoke(CurrentTrialIndex);
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Resets to trial 0 / day 1 and captures a fresh baseline from the current skill state.
    /// Used on a fresh start (or while testing without persistence).
    /// </summary>
    public void ResetProgressionToStart()
    {
        CurrentTrialIndex = 0;
        CurrentDay = 1;
        AllTrialsCompleted = false;
        CaptureBaseline();
        SaveState();
        OnStateChanged?.Invoke();
    }

    private void CaptureBaseline()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null)
        {
            if (verboseLogging) Debug.LogWarning("[DayTrial] No RacingSkillTreeManager to snapshot baseline from.");
            return;
        }
        _baseline = mgr.CaptureProgressSnapshot();
        SaveBaseline();
    }

    private void RestoreBaseline()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null || _baseline == null)
        {
            if (verboseLogging) Debug.LogWarning("[DayTrial] Cannot restore baseline (manager or snapshot missing).");
            return;
        }
        mgr.RestoreProgressSnapshot(_baseline);
    }

    // ---- Persistence ----
    private void SaveState()
    {
        if (!persistAcrossSessions) return;
        PlayerPrefs.SetInt(DayKey, CurrentDay);
        PlayerPrefs.SetInt(IndexKey, CurrentTrialIndex);
        PlayerPrefs.SetInt(AllDoneKey, AllTrialsCompleted ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void SaveBaseline()
    {
        if (!persistAcrossSessions) return;
        PlayerPrefs.SetString(BaselineKey, _baseline != null ? JsonUtility.ToJson(_baseline) : string.Empty);
        PlayerPrefs.Save();
    }

    private void LoadState()
    {
        CurrentDay = Mathf.Max(1, PlayerPrefs.GetInt(DayKey, 1));
        CurrentTrialIndex = Mathf.Max(0, PlayerPrefs.GetInt(IndexKey, 0));
        AllTrialsCompleted = PlayerPrefs.GetInt(AllDoneKey, 0) == 1;

        string json = PlayerPrefs.GetString(BaselineKey, string.Empty);
        _baseline = !string.IsNullOrEmpty(json) ? JsonUtility.FromJson<SkillProgressSnapshot>(json) : null;

        // If no baseline was saved (e.g. first run with persistence on), capture one now.
        if (_baseline == null)
            CaptureBaseline();

        OnStateChanged?.Invoke();
    }
}
