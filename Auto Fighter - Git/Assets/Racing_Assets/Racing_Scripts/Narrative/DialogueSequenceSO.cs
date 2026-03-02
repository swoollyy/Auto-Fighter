using UnityEngine;

/// <summary>
/// A sequence of dialogue lines (e.g. a conversation or cutscene block).
/// Create via: Right-click in Project > Create > Racing > Narrative > Dialogue Sequence.
/// </summary>
[CreateAssetMenu(menuName = "Racing/Narrative/Dialogue Sequence", fileName = "DialogueSeq_New")]
public class DialogueSequenceSO : ScriptableObject
{
    [Tooltip("Optional ID for progress/conditions (e.g. \"intro\", \"after_first_race\").")]
    public string sequenceId = "";

    [Tooltip("Dialogue lines in order. Played one by one; player (or auto) advances.")]
    public DialogueLineData[] lines = new DialogueLineData[0];

    [Header("Optional: After sequence ends")]
    [Tooltip("If set, this story flag is set when the sequence finishes (for progression).")]
    public string setStoryFlagOnComplete = "";

    [Tooltip("If true, timeScale is set to 0 while this sequence is playing (cutscene feel).")]
    public bool pauseGameWhilePlaying = true;

    /// <summary>True if the sequence has at least one line.</summary>
    public bool HasLines => lines != null && lines.Length > 0;

    /// <summary>Number of lines.</summary>
    public int LineCount => lines?.Length ?? 0;
}
