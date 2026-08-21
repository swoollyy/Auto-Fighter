using UnityEngine;

/// <summary>
/// A sequence of dialogue lines (e.g. a conversation or cutscene block).
/// Create via: Right-click in Project > Create > Racing > Narrative > Dialogue Sequence.
/// </summary>
[CreateAssetMenu(menuName = "Racing/Narrative/Dialogue Sequence", fileName = "DialogueSeq_New")]
public class DialogueSequenceSO : ScriptableObject
{
    [Header("Authoring reference (not used at runtime)")]
    [Tooltip("Inspector color picker for authoring narrative colors. Copy as hex and use in <color=#RRGGBB> tags.")]
    public Color authoringColorReference = Color.white;

    [Tooltip("Dialogue lines in order. Played one by one; player (or auto) advances.")]
    public DialogueLineData[] lines = new DialogueLineData[0];

    [Header("Optional: After sequence ends")]
    [Tooltip("If set, this story flag is set when the sequence finishes (for progression).")]
    public string setStoryFlagOnComplete = "";

    [Tooltip("If true, timeScale is set to 0 while this sequence is playing (cutscene feel).")]
    public bool pauseGameWhilePlaying = true;

    [Tooltip("If true, keep the game canvas visible while this dialogue plays (use for skill-tree/tutorial dialogue overlays).")]
    public bool keepGameCanvasVisibleWhilePlaying = false;

    [Header("Dialogue Box Blob Colors By Tag")]
    [Tooltip("Blob FX colors when a line uses Speaker Tag = Overseer (also used for ??? before the name reveal).")]
    public DialogueBlobColorSet overseerColors = DialogueBlobColorSet.DefaultOverseer();

    [Tooltip("Blob FX colors when a line uses Speaker Tag = Player.")]
    public DialogueBlobColorSet playerColors = DialogueBlobColorSet.DefaultPlayer();

    [Tooltip("Blob FX colors when a line uses Speaker Tag = Mechanic.")]
    public DialogueBlobColorSet mechanicColors = DialogueBlobColorSet.DefaultMechanic();

    [Tooltip("Blob FX colors when a line uses Speaker Tag = Taskmaster.")]
    public DialogueBlobColorSet taskmasterColors = DialogueBlobColorSet.DefaultTaskmaster();

    /// <summary>True if the sequence has at least one line.</summary>
    public bool HasLines => lines != null && lines.Length > 0;

    /// <summary>Number of lines.</summary>
    public int LineCount => lines?.Length ?? 0;

    /// <summary>Returns the 4 blob colors configured for the given speaker tag on this sequence.</summary>
    public DialogueBlobColorSet GetBlobColors(DialogueSpeakerTag tag)
    {
        switch (tag)
        {
            case DialogueSpeakerTag.Overseer:
                return overseerColors ?? DialogueBlobColorSet.DefaultOverseer();
            case DialogueSpeakerTag.Mechanic:
                return mechanicColors ?? DialogueBlobColorSet.DefaultMechanic();
            case DialogueSpeakerTag.Taskmaster:
                return taskmasterColors ?? DialogueBlobColorSet.DefaultTaskmaster();
            case DialogueSpeakerTag.Player:
            default:
                return playerColors ?? DialogueBlobColorSet.DefaultPlayer();
        }
    }

    private void Reset()
    {
        overseerColors = DialogueBlobColorSet.DefaultOverseer();
        playerColors = DialogueBlobColorSet.DefaultPlayer();
        mechanicColors = DialogueBlobColorSet.DefaultMechanic();
        taskmasterColors = DialogueBlobColorSet.DefaultTaskmaster();
    }
}
