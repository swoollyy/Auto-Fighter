using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks story progression (flags, run count, etc.) and can trigger dialogue sequences
/// when conditions are met (e.g. first run, after N runs, when a flag is set).
/// Optional: add to a GameObject in the scene, or call static methods from GameManager.
/// </summary>
public class NarrativeDirector : MonoBehaviour
{
    public static NarrativeDirector Instance { get; private set; }

    [Header("Story progression config (optional)")]
    [Tooltip("Dialogue to play when a condition is first met. Add entries in order of priority (first match wins).")]
    [SerializeField] private NarrativeTriggerEntry[] triggerEntries = new NarrativeTriggerEntry[0];

    [Header("First Max Fuel purchase")]
    [Tooltip("Played once the first time the player buys Max Fuel (level 1).")]
    [SerializeField] private DialogueSequenceSO maxFuelPurchasedDialogue;

    [Tooltip("Story flag set when the Max Fuel purchase dialogue starts (prevents re-play).")]
    [SerializeField] private string maxFuelPurchasedFlag = "tutorial_maxfuel_purchased";

    [Tooltip("Skill that counts as the 'first upgrade' for the purchase dialogue.")]
    [SerializeField] private SkillType firstUpgradeSkill = SkillType.MaxFuel_Add;

    [Header("Help Patrons")]
    [Tooltip("Played on the track when the player collects The Taskmaster.")]
    [SerializeField] private DialogueSequenceSO taskmasterIntroDialogue;

    [Tooltip("Played once on the skill tree after collecting The Taskmaster. Explains quests / inventory.")]
    [SerializeField] private DialogueSequenceSO taskmasterSkillTreeDialogue;

    [Header("Debug")]
    [SerializeField] private bool logTriggerChecks;

    /// <summary>On-track pickup sequence (inspector).</summary>
    public DialogueSequenceSO TaskmasterIntroDialogue => taskmasterIntroDialogue;
    /// <summary>Garage sequence after collecting The Taskmaster (inspector).</summary>
    public DialogueSequenceSO TaskmasterSkillTreeDialogue => taskmasterSkillTreeDialogue;

    private static readonly HashSet<string> StoryFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static int _totalRunsCompleted;
    private bool _pendingMaxFuelPurchaseDialogue;
    private bool _subscribedToDialogue;

    public const string Trial1CompleteFlag = "trial1_complete";
    public const string Trial1CompleteShownFlag = "trial1_complete_shown";
    private const string PrefsPendingTrial1Key = "Narrative_PendingTrial1Complete";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsForPlayMode()
    {
        StoryFlags.Clear();
        _totalRunsCompleted = 0;
        PlayerPrefs.DeleteKey(PrefsPendingTrial1Key);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (taskmasterIntroDialogue == null)
            taskmasterIntroDialogue = Resources.Load<DialogueSequenceSO>(HelpPatronProgress.TaskmasterIntroResourcePath);
        HelpPatronProgress.RegisterIntroSequence(taskmasterIntroDialogue);
        HelpPatronProgress.RegisterSkillTreeSequence(taskmasterSkillTreeDialogue);
        EnsureTaskmasterTriggerPresent();
    }

    private void OnDestroy()
    {
        UnsubscribeDialogue();
        if (Instance == this)
            Instance = null;
    }
    
    private void Start()
    {
        SubscribeDialogue();
        ApplyOpeningNarrativeSkipFromProgress();
        HelpPatronProgress.PrepareForTriggerCheck();
        // Same as CanAfford Max Fuel: play on Start while the results-return goo is still
        // sealed. GameManager / iris failsafe then opens onto the line.
        CheckTriggers();
    }

    private void Update()
    {
        if (!_subscribedToDialogue)
            SubscribeDialogue();
    }

    /// <summary>
    /// Make the Taskmaster skill-tree sequence show up in the same trigger list as other garage lines.
    /// </summary>
    private void EnsureTaskmasterTriggerPresent()
    {
        if (taskmasterSkillTreeDialogue == null) return;

        for (int i = 0; i < triggerEntries.Length; i++)
        {
            if (triggerEntries[i] != null && triggerEntries[i].sequence == taskmasterSkillTreeDialogue)
                return;
        }

        var extra = new NarrativeTriggerEntry
        {
            sequence = taskmasterSkillTreeDialogue,
            playOnce = true,
            flagWhenPlayed = HelpPatronProgress.TaskmasterSkillTreeShownFlag,
            condition = new NarrativeTriggerCondition
            {
                type = NarrativeTriggerCondition.TriggerType.HasStoryFlag,
                storyFlag = HelpPatronProgress.TaskmasterCollectedFlag
            }
        };

        var list = new List<NarrativeTriggerEntry>(triggerEntries.Length + 1);
        list.Add(extra);
        list.AddRange(triggerEntries);
        triggerEntries = list.ToArray();
    }

    private void SubscribeDialogue()
    {
        if (_subscribedToDialogue) return;
        if (DialogueManager.Instance == null) return;
        DialogueManager.Instance.OnSequenceCompleted += HandleDialogueSequenceCompleted;
        _subscribedToDialogue = true;
    }

    private void UnsubscribeDialogue()
    {
        if (!_subscribedToDialogue) return;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.OnSequenceCompleted -= HandleDialogueSequenceCompleted;
        _subscribedToDialogue = false;
    }

    private void HandleDialogueSequenceCompleted(DialogueSequenceSO completed)
    {
        HelpPatronProgress.HandleDialogueSequenceCompleted(completed);

        if (!_pendingMaxFuelPurchaseDialogue) return;
        _pendingMaxFuelPurchaseDialogue = false;
        TryPlayFirstUpgradePurchasedDialogue(firstUpgradeSkill, 1);
    }

    /// <summary>Set a story flag (e.g. "intro_done", "met_mechanic"). Persists for session only unless you save it.</summary>
    public static void SetStoryFlag(string flag)
    {
        if (string.IsNullOrEmpty(flag)) return;
        StoryFlags.Add(flag.Trim());
    }

    /// <summary>Clear a single story flag.</summary>
    public static void ClearStoryFlag(string flag)
    {
        if (string.IsNullOrEmpty(flag)) return;
        StoryFlags.Remove(flag.Trim());
    }

    /// <summary>
    /// Wipe session story flags + run count. Called with the TEMP skill-tree wipe so Play Mode
    /// restarts do not keep tutorial / patron flags when Domain Reload is disabled.
    /// </summary>
    public static void ResetSessionState()
    {
        StoryFlags.Clear();
        _totalRunsCompleted = 0;
        PlayerPrefs.DeleteKey(PrefsPendingTrial1Key);
    }

    /// <summary>Returns true if the flag has been set.</summary>
    public static bool HasStoryFlag(string flag)
    {
        if (string.IsNullOrEmpty(flag)) return false;
        return StoryFlags.Contains(flag.Trim());
    }

    /// <summary>
    /// Treat opening-tutorial dialogue as already seen when starting past trial 1
    /// (debug jump to Track 2, or a loaded save that already cleared those gates).
    /// Call from Awake so <see cref="CheckTriggers"/> cannot play Init first.
    /// </summary>
    public static void ApplyOpeningNarrativeSkipFromProgress()
    {
        int trialIndex = 0;
        bool markPriorTrialsCompleted = false;

        var gm = GameManager_Racing.Instance;
        if (gm != null && gm.IsDebugForceStartTrial && gm.DebugStartTrialIndex > 0)
        {
            trialIndex = gm.DebugStartTrialIndex;
        }
        else if (DayTrialManager.Instance != null)
        {
            trialIndex = DayTrialManager.Instance.CurrentTrialIndex;
            markPriorTrialsCompleted = DayTrialManager.Instance.PersistAcrossSessions && trialIndex > 0;
        }

        if (trialIndex <= 0) return;
        MarkEarlyNarrativePassedForTrial(trialIndex, markPriorTrialsCompleted);
    }

    /// <summary>
    /// Skip Init / skill-tree intro / max-fuel tutorial / first-run controls when the player
    /// is already on a later trial. Does not invent "you beat trial 1" dialogue unless
    /// <paramref name="markPriorTrialsCompleted"/> is true (real save).
    /// </summary>
    public static void MarkEarlyNarrativePassedForTrial(int trialIndex, bool markPriorTrialsCompleted)
    {
        if (trialIndex <= 0) return;

        SetStoryFlag("init_finish");
        SetStoryFlag("init_shown");
        SetStoryFlag("skilltree_first_entry");
        SetStoryFlag("skilltree_first_shown");
        SetStoryFlag("tutorial_maxfuel_purchased");
        SetStoryFlag("can_afford_maxfuel_shown");
        SetStoryFlag("tutorial_maxfuel_clicked");
        SetStoryFlag("tutorial_maxfuel_cost_shown");
        SetStoryFlag(FirstRunControlsOverlay.ShownStoryFlag);

        if (!markPriorTrialsCompleted) return;

        for (int i = 0; i < trialIndex; i++)
        {
            int trialNumber = i + 1;
            SetStoryFlag($"trial{trialNumber}_complete");
            // Same as Taskmaster: a pending garage line must still play on the return
            // from beating the trial. Don't stamp _shown until CheckTriggers plays it.
            if (trialNumber == 1 && PlayerPrefs.GetInt(PrefsPendingTrial1Key, 0) == 1)
                continue;
            SetStoryFlag($"trial{trialNumber}_complete_shown");
        }
    }

    /// <summary>
    /// Same pattern as collecting The Taskmaster: set the garage trigger flag and
    /// remember it across the results→skill-tree scene reload.
    /// </summary>
    public static void MarkTrial1Complete()
    {
        SetStoryFlag(Trial1CompleteFlag);
        PlayerPrefs.SetInt(PrefsPendingTrial1Key, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Call when the player completes a run (e.g. from GameManager_Racing).
    /// Increments run count only — post-run skill-tree dialogue is evaluated on
    /// <see cref="NotifyReturnedToSkillTree"/> so it does not play over the results screen.
    /// </summary>
    public static void NotifyRunCompleted()
    {
        _totalRunsCompleted++;
    }

    /// <summary>
    /// Call when the flow returns to the skill tree (after results, or boot).
    /// Evaluates garage overlay triggers (e.g. can-afford Max Fuel, run-count).
    /// </summary>
    public static void NotifyReturnedToSkillTree()
    {
        Instance?.CheckTriggers();
    }

    /// <summary>
    /// Call after a successful skill purchase. Plays the Max Fuel purchase dialogue once
    /// when the configured first-upgrade skill reaches level 1.
    /// </summary>
    public static void NotifySkillPurchased(SkillType type, int newLevel)
    {
        Instance?.TryPlayFirstUpgradePurchasedDialogue(type, newLevel);
    }

    /// <summary>Get the number of runs completed this session (for conditions).</summary>
    public static int GetTotalRunsCompleted()
    {
        return _totalRunsCompleted;
    }

    /// <summary>Manually trigger dialogue by passing a sequence asset.</summary>
    public void PlayDialogue(DialogueSequenceSO sequence)
    {
        if (sequence == null) return;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.PlaySequence(sequence);
        else if (logTriggerChecks)
            Debug.LogWarning("[NarrativeDirector] No DialogueManager in scene; cannot play: " + sequence.name);
    }

    /// <summary>
    /// Play dialogue and reserve <paramref name="flagWhenDone"/> immediately so this trigger cannot fire again
    /// during the same session (fixes race: other <see cref="DialogueManager.OnSequenceCompleted"/> listeners
    /// may run before a "set flag on complete" callback and re-enter <see cref="CheckTriggers"/>).
    /// </summary>
    public void PlayDialogueOnce(DialogueSequenceSO sequence, string flagWhenDone)
    {
        if (sequence == null) return;
        if (HasStoryFlag(flagWhenDone)) return;
        if (DialogueManager.Instance == null) return;

        SetStoryFlag(flagWhenDone);
        if (string.Equals(flagWhenDone, Trial1CompleteShownFlag, StringComparison.OrdinalIgnoreCase))
        {
            PlayerPrefs.DeleteKey(PrefsPendingTrial1Key);
            PlayerPrefs.Save();
        }
        DialogueManager.Instance.PlaySequence(sequence);
    }

    private void TryPlayFirstUpgradePurchasedDialogue(SkillType type, int newLevel)
    {
        if (maxFuelPurchasedDialogue == null) return;
        if (type != firstUpgradeSkill || newLevel != 1) return;
        if (string.IsNullOrEmpty(maxFuelPurchasedFlag)) return;
        if (HasStoryFlag(maxFuelPurchasedFlag)) return;

        if (DialogueManager.Instance != null && DialogueManager.Instance.IsPlaying)
        {
            _pendingMaxFuelPurchaseDialogue = true;
            if (logTriggerChecks)
                Debug.Log("[NarrativeDirector] Deferring Max Fuel purchase dialogue — another sequence is playing.");
            return;
        }

        if (logTriggerChecks)
            Debug.Log("[NarrativeDirector] First upgrade purchased — " + maxFuelPurchasedDialogue.name);

        PlayDialogueOnce(maxFuelPurchasedDialogue, maxFuelPurchasedFlag);
    }

    /// <summary>Evaluate trigger entries and play the first matching sequence (once per condition).</summary>
    public void CheckTriggers()
    {
        HelpPatronProgress.PrepareForTriggerCheck();
        if (PlayerPrefs.GetInt(PrefsPendingTrial1Key, 0) == 1)
            SetStoryFlag(Trial1CompleteFlag);

        if (DialogueManager.Instance != null && DialogueManager.Instance.IsPlaying)
            return;

        for (int i = 0; i < triggerEntries.Length; i++)
        {
            var entry = triggerEntries[i];
            if (entry.sequence == null) continue;
            if (!entry.condition.Evaluate())
                continue;
            if (entry.playOnce && HasStoryFlag(entry.flagWhenPlayed))
                continue;

            if (logTriggerChecks)
                Debug.Log("[NarrativeDirector] Trigger: " + entry.sequence.name);

            if (entry.playOnce && !string.IsNullOrEmpty(entry.flagWhenPlayed))
                PlayDialogueOnce(entry.sequence, entry.flagWhenPlayed);
            else
                PlayDialogue(entry.sequence);

            break; // one trigger per check
        }
    }
}

/// <summary>
/// Defines when to play a dialogue sequence (condition + play-once flag).
/// </summary>
[Serializable]
public class NarrativeTriggerEntry
{
    [Tooltip("Dialogue sequence to play when condition is met.")]
    public DialogueSequenceSO sequence;

    [Tooltip("When to play this sequence.")]
    public NarrativeTriggerCondition condition = new NarrativeTriggerCondition();

    [Tooltip("If true, only play once per session (or use flagWhenPlayed for persistence).")]
    public bool playOnce = true;

    [Tooltip("Story flag set as soon as this sequence starts (dedupe / block re-trigger before OnSequenceCompleted order issues).")]
    public string flagWhenPlayed = "";
}

/// <summary>
/// Condition for when to trigger narrative dialogue.
/// </summary>
[Serializable]
public class NarrativeTriggerCondition
{
    public enum TriggerType
    {
        Always,
        FirstRunOnly,              // total runs completed == 0
        CanAffordFirstMaxFuel,     // can buy Max Fuel level 1 (still at level 0)
        RunCountEquals,            // total runs == value
        RunCountAtLeast,           // total runs >= value
        HasStoryFlag,              // a flag is set
        DoesNotHaveStoryFlag
    }

    public TriggerType type = TriggerType.FirstRunOnly;
    [Tooltip("Used for RunCountEquals, RunCountAtLeast.")]
    public int runCountValue = 1;
    [Tooltip("Used for HasStoryFlag, DoesNotHaveStoryFlag.")]
    public string storyFlag = "";

    public bool Evaluate()
    {
        int runs = NarrativeDirector.GetTotalRunsCompleted();
        switch (type)
        {
            case TriggerType.Always:
                return true;
            case TriggerType.FirstRunOnly:
                return runs == 0;
            case TriggerType.CanAffordFirstMaxFuel:
                return CanAffordFirstMaxFuelUpgrade();
            case TriggerType.RunCountEquals:
                return runs == runCountValue;
            case TriggerType.RunCountAtLeast:
                return runs >= runCountValue;
            case TriggerType.HasStoryFlag:
                return !string.IsNullOrEmpty(storyFlag) && NarrativeDirector.HasStoryFlag(storyFlag);
            case TriggerType.DoesNotHaveStoryFlag:
                return string.IsNullOrEmpty(storyFlag) || !NarrativeDirector.HasStoryFlag(storyFlag);
            default:
                return false;
        }
    }

    /// <summary>
    /// True when Max Fuel is still unpurchased and the player can afford its first level.
    /// </summary>
    private static bool CanAffordFirstMaxFuelUpgrade()
    {
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr == null) return false;
        if (mgr.GetLevel(SkillType.MaxFuel_Add) != 0) return false;
        return mgr.CanAffordNextLevel(SkillType.MaxFuel_Add);
    }
}
