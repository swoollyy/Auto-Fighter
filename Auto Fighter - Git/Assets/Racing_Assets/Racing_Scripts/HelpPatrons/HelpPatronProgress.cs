using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Patrons of The Help that can be found as on-track pickups.
/// Add new IDs here as more patrons are introduced.
/// </summary>
public enum HelpPatronId
{
    Taskmaster = 0,
}

/// <summary>
/// Collection is session-only unless <see cref="DayTrialManager.PersistAcrossSessions"/> is on
/// (same rule as day/trial saves). Quests + Inventory stay locked until The Taskmaster
/// skill-tree dialogue has finished.
/// </summary>
public static class HelpPatronProgress
{
    public const string TaskmasterCollectedFlag = "help_patron_taskmaster_collected";
    public const string TaskmasterIntroShownFlag = "taskmaster_pickup_shown";
    public const string TaskmasterSkillTreeShownFlag = "taskmaster_skilltree_shown";
    public const string TaskmasterMetFlag = "met_taskmaster";
    public const string TaskmasterIntroResourcePath = "HelpPatrons/Taskmaster_Intro";

    private const string PrefsCollectedPrefix = "HelpPatron_Collected_";
    private const string PrefsMetKey = "HelpPatron_Met_Taskmaster";
    private const string PrefsPendingSkillTreeKey = "HelpPatron_PendingSkillTree_Taskmaster";

    public static event Action OnChanged;

    private static bool _loaded;
    private static DialogueSequenceSO _cachedPickupIntro;
    private static DialogueSequenceSO _cachedSkillTreeIntro;
    private static readonly HashSet<HelpPatronId> Collected = new HashSet<HelpPatronId>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsForPlayMode()
    {
        _loaded = false;
        _cachedPickupIntro = null;
        _cachedSkillTreeIntro = null;
        Collected.Clear();
        OnChanged = null;
        // Wipe leftover collection from the previous Play Mode. Do not call this on scene reload.
        foreach (HelpPatronId id in Enum.GetValues(typeof(HelpPatronId)))
            PlayerPrefs.DeleteKey(CollectedPrefsKey(id));
        PlayerPrefs.DeleteKey(PrefsMetKey);
        PlayerPrefs.DeleteKey(PrefsPendingSkillTreeKey);
    }

    public static string CollectedFlag(HelpPatronId id)
    {
        switch (id)
        {
            case HelpPatronId.Taskmaster:
            default:
                return TaskmasterCollectedFlag;
        }
    }

    public static string DisplayName(HelpPatronId id)
    {
        switch (id)
        {
            case HelpPatronId.Taskmaster:
            default:
                return "The Taskmaster";
        }
    }

    public static bool IsCollected(HelpPatronId id)
    {
        EnsureLoaded();
        return Collected.Contains(id) || NarrativeDirector.HasStoryFlag(CollectedFlag(id));
    }

    public static bool AreQuestsAndInventoryUnlocked
    {
        get
        {
            EnsureLoaded();
            return NarrativeDirector.HasStoryFlag(TaskmasterMetFlag);
        }
    }

    /// <summary>True when collected and the garage Taskmaster sequence has not played yet.</summary>
    public static bool HasPendingSkillTreeIntro
    {
        get
        {
            EnsureLoaded();
            if (AreQuestsAndInventoryUnlocked) return false;
            if (NarrativeDirector.HasStoryFlag(TaskmasterSkillTreeShownFlag)) return false;
            if (IsCollected(HelpPatronId.Taskmaster)) return true;
            return PlayerPrefs.GetInt(PrefsPendingSkillTreeKey, 0) == 1;
        }
    }

    /// <summary>
    /// Re-apply collected flags after a scene reload so Narrative Director HasStoryFlag
    /// triggers (same pattern as can-afford Max Fuel) can see The Taskmaster pickup.
    /// </summary>
    public static void PrepareForTriggerCheck()
    {
        EnsureLoaded();
        if (IsCollected(HelpPatronId.Taskmaster)
            || PlayerPrefs.GetInt(PrefsPendingSkillTreeKey, 0) == 1)
        {
            Collected.Add(HelpPatronId.Taskmaster);
            NarrativeDirector.SetStoryFlag(TaskmasterCollectedFlag);
        }
    }

    public static void RegisterIntroSequence(DialogueSequenceSO sequence)
    {
        if (sequence != null)
            _cachedPickupIntro = sequence;
    }

    public static void RegisterSkillTreeSequence(DialogueSequenceSO sequence)
    {
        if (sequence != null)
            _cachedSkillTreeIntro = sequence;
    }

    public static void MarkCollected(HelpPatronId id)
    {
        EnsureLoaded();
        Collected.Add(id);
        NarrativeDirector.SetStoryFlag(CollectedFlag(id));
        PlayerPrefs.SetInt(CollectedPrefsKey(id), 1);
        if (id == HelpPatronId.Taskmaster)
            PlayerPrefs.SetInt(PrefsPendingSkillTreeKey, 1);
        PlayerPrefs.Save();
        PersistIfSaving(id);
        OnChanged?.Invoke();
        Debug.Log($"[HelpPatron] Collected {DisplayName(id)}.");
    }

    public static void MarkTaskmasterMet()
    {
        EnsureLoaded();
        if (AreQuestsAndInventoryUnlocked)
        {
            PersistMetIfSaving();
            return;
        }

        NarrativeDirector.SetStoryFlag(TaskmasterMetFlag);
        PersistMetIfSaving();
        OnChanged?.Invoke();
    }

    /// <summary>On-track pickup line. Call from the collect trigger.</summary>
    public static bool TryPlayPickupIntro()
    {
        EnsureLoaded();
        if (!IsCollected(HelpPatronId.Taskmaster))
            return false;
        if (NarrativeDirector.HasStoryFlag(TaskmasterIntroShownFlag))
            return false;
        if (!TryBeginPlay(ResolvePickupIntro(), TaskmasterIntroShownFlag, "pickup"))
            return false;
        return true;
    }

    /// <summary>Garage sequence explaining quests. Call when back on the skill tree.</summary>
    public static bool TryPlaySkillTreeIntro()
    {
        EnsureLoaded();
        if (!HasPendingSkillTreeIntro)
            return false;

        var gm = GameManager_Racing.Instance;
        if (gm != null)
        {
            var state = gm.ProgressState;
            if (state == GameManager_Racing.GameProgressState.InRun ||
                state == GameManager_Racing.GameProgressState.LoadingRun ||
                state == GameManager_Racing.GameProgressState.RunEnd)
                return false;
        }

        // Returning from a run reloads under sealed goo. Play anyway — same as can-afford
        // Max Fuel — so the box is already up as the iris retracts.
        return TryBeginPlay(ResolveSkillTreeIntro(), TaskmasterSkillTreeShownFlag, "skill tree");
    }

    /// <summary>Garage helper used by GameManager return-to-tree paths.</summary>
    public static bool TryPlayPendingIntro() => TryPlaySkillTreeIntro();

    public static DialogueSequenceSO ResolvePickupIntro()
    {
        if (_cachedPickupIntro != null)
            return _cachedPickupIntro;

        var director = FindDirector();
        if (director != null && director.TaskmasterIntroDialogue != null)
        {
            _cachedPickupIntro = director.TaskmasterIntroDialogue;
            return _cachedPickupIntro;
        }

        return Resources.Load<DialogueSequenceSO>(TaskmasterIntroResourcePath);
    }

    public static DialogueSequenceSO ResolveSkillTreeIntro()
    {
        if (_cachedSkillTreeIntro != null)
            return _cachedSkillTreeIntro;

        var director = FindDirector();
        if (director != null && director.TaskmasterSkillTreeDialogue != null)
        {
            _cachedSkillTreeIntro = director.TaskmasterSkillTreeDialogue;
            return _cachedSkillTreeIntro;
        }

        return null;
    }

    public static DialogueSequenceSO ResolveTaskmasterIntro() => ResolvePickupIntro();

    public static void HandleDialogueSequenceCompleted(DialogueSequenceSO completed)
    {
        if (completed == null) return;
        if (completed != ResolveSkillTreeIntro())
            return;

        PlayerPrefs.DeleteKey(PrefsPendingSkillTreeKey);
        PlayerPrefs.Save();
        MarkTaskmasterMet();
    }

    public static void ClearAllData()
    {
        _loaded = true;
        Collected.Clear();
        foreach (HelpPatronId id in Enum.GetValues(typeof(HelpPatronId)))
        {
            PlayerPrefs.DeleteKey(CollectedPrefsKey(id));
            NarrativeDirector.ClearStoryFlag(CollectedFlag(id));
        }

        PlayerPrefs.DeleteKey(PrefsMetKey);
        PlayerPrefs.DeleteKey(PrefsPendingSkillTreeKey);
        NarrativeDirector.ClearStoryFlag(TaskmasterIntroShownFlag);
        NarrativeDirector.ClearStoryFlag(TaskmasterSkillTreeShownFlag);
        NarrativeDirector.ClearStoryFlag(TaskmasterMetFlag);
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }

    public static void LoadFromSave()
    {
        _loaded = true;
        foreach (HelpPatronId id in Enum.GetValues(typeof(HelpPatronId)))
        {
            if (PlayerPrefs.GetInt(CollectedPrefsKey(id), 0) == 1)
            {
                Collected.Add(id);
                NarrativeDirector.SetStoryFlag(CollectedFlag(id));
            }
        }

        if (PlayerPrefs.GetInt(PrefsMetKey, 0) == 1)
            NarrativeDirector.SetStoryFlag(TaskmasterMetFlag);

        if (PlayerPrefs.GetInt(PrefsPendingSkillTreeKey, 0) == 1
            && !NarrativeDirector.HasStoryFlag(TaskmasterMetFlag))
        {
            Collected.Add(HelpPatronId.Taskmaster);
            NarrativeDirector.SetStoryFlag(TaskmasterCollectedFlag);
        }

        OnChanged?.Invoke();
    }

    private static bool TryBeginPlay(DialogueSequenceSO seq, string shownFlag, string label)
    {
        if (seq == null || !seq.HasLines)
        {
            Debug.LogError($"[HelpPatron] Cannot play Taskmaster {label} dialogue — assign it on Narrative Director.");
            return false;
        }

        if (DialogueManager.Instance == null)
            return false;
        if (DialogueManager.Instance.IsPlaying)
            return false;

        NarrativeDirector.SetStoryFlag(shownFlag);
        Debug.Log($"[HelpPatron] Playing Taskmaster {label} dialogue: " + seq.name);
        if (NarrativeDirector.Instance != null)
            NarrativeDirector.Instance.PlayDialogue(seq);
        else
            DialogueManager.Instance.PlaySequence(seq);

        OnChanged?.Invoke();
        return true;
    }

    private static NarrativeDirector FindDirector()
    {
        var director = NarrativeDirector.Instance;
        if (director == null)
            director = UnityEngine.Object.FindObjectOfType<NarrativeDirector>(true);
        return director;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        HydrateCollectionFromPrefs();

        var day = DayTrialManager.Instance;
        if (day != null && day.PersistAcrossSessions)
            LoadFromSave();
    }

    private static void HydrateCollectionFromPrefs()
    {
        foreach (HelpPatronId id in Enum.GetValues(typeof(HelpPatronId)))
        {
            if (PlayerPrefs.GetInt(CollectedPrefsKey(id), 0) == 1)
            {
                Collected.Add(id);
                NarrativeDirector.SetStoryFlag(CollectedFlag(id));
            }
        }

        if (PlayerPrefs.GetInt(PrefsPendingSkillTreeKey, 0) == 1)
        {
            Collected.Add(HelpPatronId.Taskmaster);
            NarrativeDirector.SetStoryFlag(TaskmasterCollectedFlag);
        }
    }

    private static bool ShouldPersistToSave()
    {
        var day = DayTrialManager.Instance;
        return day != null && day.PersistAcrossSessions;
    }

    private static void PersistIfSaving(HelpPatronId id)
    {
        if (!ShouldPersistToSave()) return;
        PlayerPrefs.SetInt(CollectedPrefsKey(id), 1);
        PlayerPrefs.Save();
    }

    private static void PersistMetIfSaving()
    {
        if (!ShouldPersistToSave()) return;
        PlayerPrefs.SetInt(PrefsMetKey, 1);
        PlayerPrefs.Save();
    }

    private static string CollectedPrefsKey(HelpPatronId id)
    {
        return PrefsCollectedPrefix + id;
    }
}
