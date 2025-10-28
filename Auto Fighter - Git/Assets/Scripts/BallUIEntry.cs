using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public sealed class BallUIEntry : MonoBehaviour
{
    [SerializeField] private Image colorDot;
    [SerializeField] private TMP_Text multiplierText;

    private Ball _ball;

    // When using a prefab, these are wired in the Inspector. If constructed at runtime, BindRuntime wires them.
    public void BindRuntime(Image dot, TMP_Text label)
    {
        colorDot = dot;
        multiplierText = label;
    }

    // Assign a Ball to this row
    public void Init(Ball ball)
    {
        _ball = ball;
    }

    // Call to sync the visuals to current ball state
    public void Refresh(Ball ball)
    {
        if (!ball) return;

        // Color the dot: apply a little “intensity” boost to give some visual separation
        float boost = Mathf.Clamp(ball.EmissionIntensityUI, 0.5f, 2.0f);
        var c = ball.GlowColor;
        var bright = new Color(Mathf.Clamp01(c.r * boost), Mathf.Clamp01(c.g * boost), Mathf.Clamp01(c.b * boost), 1f);
        if (colorDot) colorDot.color = bright;

        // Show combo multiplier if active; otherwise default “x1.0”
        float mult = ball.IsComboActive ? ball.CurrentComboMultiplierUI : 1f;
        if (multiplierText) multiplierText.text = $"x{mult:0.0}";
    }
}