using UnityEngine;

/// <summary>
/// True while <see cref="DialogueManager"/> is playing a sequence — use to block skill-tree / gameplay UI clicks
/// while still allowing mouse movement and dialogue advance input.
/// Also tracks forced UI highlight tutorials (<see cref="TutorialUIHighlightCoach"/>).
/// </summary>
public static class GameplayUIInputGuard
{
    public static bool IsDialogueBlockingGameplayUi =>
        DialogueManager.Instance != null && DialogueManager.Instance.IsPlaying;

    /// <summary>True while a tutorial spotlight is forcing a single UI target click.</summary>
    public static bool IsTutorialHighlightActive { get; set; }

    /// <summary>Dialogue or tutorial highlight is blocking normal skill-tree chrome / pan.</summary>
    public static bool IsGameplayUiNavigationBlocked =>
        IsDialogueBlockingGameplayUi || IsTutorialHighlightActive;
}
