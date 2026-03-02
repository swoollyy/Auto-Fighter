using UnityEngine;

/// <summary>
/// Single line of dialogue: speaker name, text, and optional display settings.
/// Used inside DialogueSequenceSO.
/// </summary>
[System.Serializable]
public class DialogueLineData
{
    [Tooltip("Display name of the speaker (e.g. \"Mechanic\", \"Rival\").")]
    public string speakerName = "";

    [TextArea(2, 5)]
    [Tooltip("The dialogue text to show.")]
    public string text = "";

    [Tooltip("Optional: portrait sprite for this line. Leave empty to keep previous or use default.")]
    public Sprite portrait;

    [Tooltip("Optional: delay in seconds before this line is shown (after previous line is dismissed).")]
    public float delayBeforeShow;

    [Tooltip("If true, advance automatically after autoAdvanceSeconds (e.g. for cutscene narration).")]
    public bool autoAdvance;

    [Min(0.5f)]
    [Tooltip("Seconds before auto-advancing (only if autoAdvance is true).")]
    public float autoAdvanceSeconds = 2f;
}
