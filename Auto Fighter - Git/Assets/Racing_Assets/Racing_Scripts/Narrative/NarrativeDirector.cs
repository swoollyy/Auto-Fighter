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

    [Header("Debug")]
    [SerializeField] private bool logTriggerChecks;

    private static readonly HashSet<string> StoryFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static int _totalRunsCompleted;

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
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        CheckTriggers();
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

    /// <summary>Call when the player completes a run (e.g. from GameManager_Racing). Used for "after first run" style triggers.</summary>
    public static void NotifyRunCompleted()
    {
        _totalRunsCompleted++;
        Instance?.CheckTriggers();
    }

    /// <summary>Get the number of runs completed this session (for conditions).</summary>
    public static int GetTotalRunsCompleted()
    {
        return _totalRunsCompleted;
    }

    /// <summary>Manually trigger dialogue by sequence ID from your trigger list, or pass a sequence asset.</summary>
    public void PlayDialogue(DialogueSequenceSO sequence)
    {
        if (sequence == null) return;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.PlaySequence(sequence);
        else if (logTriggerChecks)
            Debug.LogWarning("[NarrativeDirector] No DialogueManager in scene; cannot play: " + sequence.name);
    }

    /// <summary>Play dialogue and set a flag when done (so it won't trigger again if you use that flag in conditions).</summary>
    public void PlayDialogueOnce(DialogueSequenceSO sequence, string flagWhenDone)
    {
        if (sequence == null) return;
        if (HasStoryFlag(flagWhenDone)) return;
        var prev = DialogueManager.Instance != null ? (Action<DialogueSequenceSO>)null : null;
        if (DialogueManager.Instance != null)
        {
            void OnDone(DialogueSequenceSO seq)
            {
                if (seq == sequence && !string.IsNullOrEmpty(flagWhenDone))
                    SetStoryFlag(flagWhenDone);
                if (DialogueManager.Instance != null)
                    DialogueManager.Instance.OnSequenceCompleted -= OnDone;
            }
            DialogueManager.Instance.OnSequenceCompleted += OnDone;
            DialogueManager.Instance.PlaySequence(sequence);
        }
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

    [Tooltip("Story flag to set when this has been played (so condition can require 'not yet played').")]
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
        FirstRunOnly,       // total runs completed == 0 before this run
        AfterFirstRun,      // total runs completed >= 1
        RunCountEquals,     // total runs == value
        RunCountAtLeast,    // total runs >= value
        HasStoryFlag,       // a flag is set
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
            case TriggerType.AfterFirstRun:
                return runs >= 1;
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
}
