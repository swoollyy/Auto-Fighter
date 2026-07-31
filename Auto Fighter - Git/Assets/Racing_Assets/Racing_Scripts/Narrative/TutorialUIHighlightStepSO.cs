using UnityEngine;

/// <summary>
/// One reusable tutorial beat: after a dialogue ends, spotlight a UI target until clicked,
/// then optionally play follow-up dialogue and set a story flag.
/// Create via: Right-click > Create > Racing > Narrative > UI Highlight Step.
/// </summary>
[CreateAssetMenu(menuName = "Racing/Narrative/UI Highlight Step", fileName = "Tutorial_Highlight_")]
public class TutorialUIHighlightStepSO : ScriptableObject
{
    public enum TargetKind
    {
        SkillNode = 0,
    }

    [Header("When to start")]
    [Tooltip("Start this highlight when this dialogue sequence finishes.")]
    public DialogueSequenceSO startAfterSequence;

    [Tooltip("If set and already present, this step is skipped (play-once).")]
    public string skipIfHasFlag = "";

    [Header("Target")]
    public TargetKind targetKind = TargetKind.SkillNode;

    [Tooltip("Used when targetKind is SkillNode.")]
    public SkillType skillTarget = SkillType.MaxFuel_Add;

    [Header("Visuals")]
    [Tooltip("Extra pixels around the target rect for the cutout hole.")]
    public float holePadding = 18f;

    public Color dimColor = new Color(0.05f, 0.05f, 0.05f, 0.92f);
    public Color outlineColor = new Color(1f, 0.85f, 0.15f, 1f);

    [Min(0f)] public float outlineThickness = 6f;
    [Min(0f)] public float bobAmplitude = 8f;
    [Min(0.01f)] public float bobSpeed = 2.2f;

    [Header("On target clicked")]
    [Tooltip("Dialogue to play after the player clicks the highlighted target.")]
    public DialogueSequenceSO playOnClick;

    [Tooltip("Story flag set when the player completes this step (click).")]
    public string setFlagOnComplete = "";
}
