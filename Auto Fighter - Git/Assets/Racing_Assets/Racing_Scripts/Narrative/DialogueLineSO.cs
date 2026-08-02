using UnityEngine;

/// <summary>
/// Speaker identity used to look up Dialogue Box FX blob colors on the parent sequence.
/// ??? (unrevealed) uses Overseer — same character, different display name.
/// </summary>
public enum DialogueSpeakerTag
{
    Overseer = 0,
    Player = 1,
    Mechanic = 2,
}

/// <summary>
/// Fill + rim colors for both layered Dialogue Box FX goo panels.
/// </summary>
[System.Serializable]
public class DialogueBlobColorSet
{
    [Header("Dialogue Box FX 1")]
    [Tooltip("Fill color (_Color) for Dialogue Box FX layer 1.")]
    public Color blob1FillColor = new Color(0.066f, 0.066f, 0.066f, 1f);

    [Tooltip("Rim / blob color (_RimColor) for Dialogue Box FX layer 1.")]
    public Color blob1RimColor = new Color(0.17f, 0.17f, 0.17f, 1f);

    [Header("Dialogue Box FX 2")]
    [Tooltip("Fill color (_Color) for Dialogue Box FX layer 2.")]
    public Color blob2FillColor = new Color(0.066f, 0.066f, 0.066f, 1f);

    [Tooltip("Rim / blob color (_RimColor) for Dialogue Box FX layer 2.")]
    public Color blob2RimColor = new Color(0.17f, 0.17f, 0.17f, 1f);

    public static DialogueBlobColorSet DefaultGray()
    {
        return new DialogueBlobColorSet();
    }

    public static DialogueBlobColorSet DefaultOverseer()
    {
        return new DialogueBlobColorSet
        {
            blob1FillColor = new Color(0.12f, 0.015f, 0.015f, 1f),
            blob1RimColor = new Color(0.6039216f, 0f, 0f, 1f),
            blob2FillColor = new Color(0.2f, 0.02f, 0.02f, 0.85f),
            blob2RimColor = new Color(0.9f, 0.08f, 0.08f, 1f),
        };
    }

    public static DialogueBlobColorSet DefaultPlayer()
    {
        return new DialogueBlobColorSet
        {
            blob1FillColor = new Color(0.1f, 0.16f, 0.04f, 1f),
            blob1RimColor = new Color(0.61960787f, 1f, 0.23137255f, 1f),
            blob2FillColor = new Color(0.14f, 0.22f, 0.05f, 0.85f),
            blob2RimColor = new Color(0.75f, 1f, 0.4f, 1f),
        };
    }

    public static DialogueBlobColorSet DefaultMechanic()
    {
        return new DialogueBlobColorSet
        {
            blob1FillColor = new Color(0.05f, 0.09f, 0.12f, 1f),
            blob1RimColor = new Color(0.30980393f, 0.53333336f, 0.6431373f, 1f),
            blob2FillColor = new Color(0.08f, 0.14f, 0.18f, 0.85f),
            blob2RimColor = new Color(0.45f, 0.75f, 0.95f, 1f),
        };
    }
}

/// <summary>
/// Single line of dialogue: speaker name, text, and optional display settings.
/// Used inside DialogueSequenceSO.
/// </summary>
[System.Serializable]
public class DialogueLineData
{
    [Tooltip("Who is speaking — drives Dialogue Box FX blob colors from this sequence's tag palettes. ??? uses Overseer.")]
    public DialogueSpeakerTag speakerTag = DialogueSpeakerTag.Player;

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
