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
/// Persistence: day, trial index, completion flag, the baseline snapshot, and Help Patron
/// collection all save to PlayerPrefs. While <see cref="persistAcrossSessions"/> is false
/// (current testing setup, alongside the TEMP ClearAllData() in RacingSkillTreeManager.Awake),
/// saved state is ignored and reset fresh on launch.
/// </summary>
[DefaultExecutionOrder(-50)] // after RacingSkillTreeManager (-100) so its Instance + state exist
public class DayTrialManager : MonoBehaviour
{
    public static DayTrialManager Instance { get; private set; }

    [Header("Trials (TrialConfig assets, played in order top to bottom)")]
    [Tooltip("Drag your TrialConfig assets here in order. Each carries its own goal (dayLimit/targetProgress) " +
             "plus the full track + obstacle + creature + NPC + coin setup that gets applied when that trial is active.")]
    [SerializeField] private List<TrialConfig> trials = new();

    [Header("Persistence")]
    [Tooltip("When true, day/trial/baseline (and Help Patron collection) load from the save file across sessions. Keep this FALSE while testing with the TEMP ClearAllData() in RacingSkillTreeManager.Awake; turn both on together for real persistence.")]
    [SerializeField] private bool persistAcrossSessions = false;

    /// <summary>True when day/trial/patron collection should survive Play Mode / app restarts.</summary>
    public bool PersistAcrossSessions => persistAcrossSessions;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    // ---- Runtime state ----
    public int CurrentDay { get; private set; } = 1;
    public int CurrentTrialIndex { get; private set; } = 0;
    public bool AllTrialsCompleted { get; private set; } = false;

    private SkillProgressSnapshot _baseline;

    /// <summary>Trial config at index, or null if out of range.</summary>
    public TrialConfig GetTrialConfig(int index) =>
        (index >= 0 && index < trials.Count) ? trials[index] : null;

    /// <summary>
    /// Debug/testing: jump to a trial index (0 = first). Optionally max every skill that belonged
    /// to earlier trials' allowlists so the car starts as if those trials were already cleared.
    /// </summary>
    public void DebugJumpToTrial(int trialIndex, bool maxPreviousTrialSkills = true)
    {
        if (trials == null || trials.Count == 0)
        {
            if (verboseLogging)
                Debug.LogWarning("[DayTrial] DebugJumpToTrial: no TrialConfig list assigned.");
            return;
        }

        int clamped = Mathf.Clamp(trialIndex, 0, trials.Count - 1);
        CurrentTrialIndex = clamped;
        CurrentDay = 1;
        AllTrialsCompleted = false;

        if (maxPreviousTrialSkills && clamped > 0)
        {
            var skills = RacingSkillTreeManager.Instance;
            if (skills != null)
                skills.DebugMaxSkillsFromPriorTrials(trials, clamped);
            else if (verboseLogging)
                Debug.LogWarning("[DayTrial] DebugJumpToTrial: RacingSkillTreeManager missing; skipped skill max.");
        }

        CaptureBaseline();
        SaveState();

        if (clamped > 0)
            NarrativeDirector.MarkEarlyNarrativePassedForTrial(clamped, markPriorTrialsCompleted: false);

        if (verboseLogging)
        {
            string name = CurrentConfig != null ? CurrentConfig.trialName : "?";
            Debug.Log($"[DayTrial] DEBUG jump → trial {CurrentTrialIndex} ('{name}'), day 1" +
                      (maxPreviousTrialSkills && clamped > 0 ? " (previous trial skills maxed)." : "."));
        }

        OnTrialAdvanced?.Invoke(CurrentTrialIndex);
        OnStateChanged?.Invoke();
    }

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

    /// <summary>The active trial's config (track + spawner setup + goal), or null if none/out of range.</summary>
    public TrialConfig CurrentConfig =>
        (CurrentTrialIndex >= 0 && CurrentTrialIndex < trials.Count) ? trials[CurrentTrialIndex] : null;

    public float CurrentTargetProgress => CurrentConfig != null ? CurrentConfig.targetProgress : 1f;
    public int CurrentDayLimit => CurrentConfig != null ? CurrentConfig.dayLimit : 0;

    // ---- Run-start hooks (called by GameManager_Racing) ----

    /// <summary>Push the active trial's track settings into the generator. Call BEFORE GenerateTrackCo().</summary>
    public void ApplyCurrentTrialToTrack(ProceduralTrackGenerator generator)
    {
        if (generator == null) return;
        var cfg = CurrentConfig;
        if (cfg != null) generator.ApplyConfig(cfg.track);
    }

    /// <summary>Push the active trial's spawner settings into the spawners. Call BEFORE their InitializeForRun().</summary>
    public void ApplyCurrentTrialToSpawners(
        TrackObstacleSpawner obstacle,
        TrackCreatureSpawner creature,
        NPCTrafficCarSpawner npc,
        TrackCoinSpawner coins = null,
        ThrownObstacleDirector thrown = null,
        RollingLogSpawner rollingLogs = null,
        CrossObstacleDirector crossObstacles = null,
        IcePathSpawner icePaths = null,
        BounceBackObstacleSpawner bounceObstacles = null,
        TrackSpawnerQueue spawnQueue = null,
        TrackHelpPatronSpawner helpPatrons = null)
    {
        var cfg = CurrentConfig;
        if (cfg == null) return;
        if (obstacle != null) obstacle.ApplyConfig(cfg.obstacles);
        if (creature != null) creature.ApplyConfig(cfg.creatures);
        if (npc != null) npc.ApplyConfig(cfg.npcTraffic);
        if (coins != null) coins.ApplyConfig(cfg.coins);
        if (thrown != null) thrown.ApplyConfig(cfg.thrown);
        if (rollingLogs != null) rollingLogs.ApplyConfig(cfg.rollingLogs);
        if (crossObstacles != null) crossObstacles.ApplyConfig(cfg.crossObstacles);
        if (icePaths != null) icePaths.ApplyConfig(cfg.icePaths);
        if (bounceObstacles != null) bounceObstacles.ApplyConfig(cfg.bounceObstacles);
        if (spawnQueue != null) spawnQueue.ApplyConfig(cfg.spawnQueue);
        if (helpPatrons != null) helpPatrons.ApplyConfig(cfg.helpPatrons);
    }

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
        {
            LoadState();
            HelpPatronProgress.LoadFromSave();
            if (CurrentTrialIndex > 0)
                NarrativeDirector.MarkEarlyNarrativePassedForTrial(CurrentTrialIndex, markPriorTrialsCompleted: true);
        }
        else
        {
            ResetProgressionToStart();
        }
    }

    /// <summary>
    /// Call this once per completed run. <paramref name="normalizedProgressReached"/> is the furthest
    /// road progress (0-1) the player reached during that run.
    /// </summary>
    /// <summary>Create a DDOL instance if the scene has none (so day ticks even before TrialConfigs are wired).</summary>
    public static DayTrialManager EnsureExists()
    {
        if (Instance != null)
            return Instance;

        var existing = FindObjectOfType<DayTrialManager>(true);
        if (existing != null)
        {
            Instance = existing;
            DontDestroyOnLoad(existing.gameObject);
            return existing;
        }

        var go = new GameObject("DayTrialManager");
        return go.AddComponent<DayTrialManager>();
    }

    public void NotifyRunCompleted(float normalizedProgressReached)
    {
        if (AllTrialsCompleted)
            return; // Endless mode (added later) takes over past the final trial.

        var trial = CurrentConfig;
        if (trial == null)
        {
            // No TrialConfig list yet — still advance the day counter so run intros / HUD progress.
            CurrentDay++;
            SaveState();
            OnStateChanged?.Invoke();
            if (verboseLogging)
                Debug.Log($"[DayTrial] No trial configured; advanced session day to {CurrentDay}. Add TrialConfig assets for trial goals.");
            return;
        }

        bool reachedTarget = normalizedProgressReached >= trial.targetProgress;

        if (verboseLogging)
            Debug.Log($"[DayTrial] Run done. Trial {CurrentTrialIndex} ('{trial.trialName}') day {CurrentDay}/{trial.dayLimit}. " +
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

        if (verboseLogging) Debug.Log($"[DayTrial] Advanced to trial {CurrentTrialIndex} ('{CurrentConfig?.trialName}').");
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
