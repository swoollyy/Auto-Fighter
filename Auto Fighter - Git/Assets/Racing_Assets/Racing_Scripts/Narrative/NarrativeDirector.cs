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

    [Header("Debug")]
    [SerializeField] private bool logTriggerChecks;

    private static readonly HashSet<string> StoryFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static int _totalRunsCompleted;
    private bool _pendingMaxFuelPurchaseDialogue;
    private bool _subscribedToDialogue;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
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
        CheckTriggers();
    }

    private void Update()
    {
        if (!_subscribedToDialogue)
            SubscribeDialogue();
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

    private void HandleDialogueSequenceCompleted(DialogueSequenceSO _)
    {
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

    /// <summary>Returns true if the flag has been set.</summary>
    public static bool HasStoryFlag(string flag)
    {
        if (string.IsNullOrEmpty(flag)) return false;
        return StoryFlags.Contains(flag.Trim());
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
