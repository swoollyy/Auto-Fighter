using UnityEngine;

/// <summary>
/// One reusable tutorial beat: spotlight a UI target (skill node click, or view-only cost callout).
/// Create via: Right-click > Create > Racing > Narrative > UI Highlight Step.
/// </summary>
[CreateAssetMenu(menuName = "Racing/Narrative/UI Highlight Step", fileName = "Tutorial_Highlight_")]
public class TutorialUIHighlightStepSO : ScriptableObject
{
    public enum TargetKind
    {
        SkillNode = 0,
        /// <summary>Cost line on the open skill detail panel.</summary>
        SkillDetailCost = 1,
    }

    public enum CompletionMode
    {
        /// <summary>Stay until the player clicks the highlighted target.</summary>
        WaitForTargetClick = 0,
        /// <summary>Stay until <see cref="startWhenSequenceStarts"/> finishes (view-only callout).</summary>
        UntilBoundSequenceEnds = 1,
    }

    [Header("When to start")]
    [Tooltip("Start this highlight when this dialogue sequence finishes.")]
    public DialogueSequenceSO startAfterSequence;

    [Tooltip("Start this highlight when this dialogue sequence begins (e.g. cost callout during the post-click line).")]
    public DialogueSequenceSO startWhenSequenceStarts;

    [Tooltip("If set and already present, this step is skipped (play-once).")]
    public string skipIfHasFlag = "";

    [Header("Target")]
    public TargetKind targetKind = TargetKind.SkillNode;

    [Tooltip("Used when targetKind is SkillNode (which skill node to spotlight).")]
    public SkillType skillTarget = SkillType.MaxFuel_Add;

    [Header("Completion")]
    [Tooltip("WaitForTargetClick = force-click hole. UntilBoundSequenceEnds = view-only while startWhenSequenceStarts plays.")]
    public CompletionMode completionMode = CompletionMode.WaitForTargetClick;

    [Tooltip("UntilBoundSequenceEnds only: clear once this many lines have been shown (2 = after advancing past the 2nd box). 0 = keep until the whole sequence ends.")]
    [Min(0)] public int dismissAfterLineCount = 0;

    [Header("Visuals")]
    [Tooltip("Extra pixels around the target rect for the cutout hole.")]
    public float holePadding = 18f;

    public Color dimColor = new Color(0.05f, 0.05f, 0.05f, 0.92f);
    public Color outlineColor = new Color(1f, 0.85f, 0.15f, 1f);

    [Min(0f)] public float outlineThickness = 6f;
    [Min(0f)] public float bobAmplitude = 8f;
    [Min(0.01f)] public float bobSpeed = 2.2f;

    [Header("On target clicked (WaitForTargetClick only)")]
    [Tooltip("Dialogue to play after the player clicks the highlighted target.")]
    public DialogueSequenceSO playOnClick;

    [Tooltip("Story flag set when this step finishes (click, or bound sequence end for view-only).")]
    public string setFlagOnComplete = "";

    public bool IsViewOnly => completionMode == CompletionMode.UntilBoundSequenceEnds;
}
