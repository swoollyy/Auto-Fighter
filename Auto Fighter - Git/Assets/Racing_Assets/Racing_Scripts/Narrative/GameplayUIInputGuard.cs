using UnityEngine;

/// <summary>
/// True while <see cref="DialogueManager"/> is playing a sequence — use to block skill-tree / gameplay UI clicks
/// while still allowing mouse movement and dialogue advance input.
/// </summary>
public static class GameplayUIInputGuard
{
    public static bool IsDialogueBlockingGameplayUi =>
        DialogueManager.Instance != null && DialogueManager.Instance.IsPlaying;
}
