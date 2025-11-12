using UnityEngine;
using DG.Tweening;

/// <summary>
/// Optional light pulse helper for pads (can be removed if unused).
/// </summary>
[DisallowMultipleComponent]
public class PadLightFX : MonoBehaviour
{
    public Light targetLight;
    public float pulsePeak = 4f;
    public float upTime = 0.15f;
    public float downTime = 0.35f;

    void Reset()
    {
        targetLight = GetComponentInChildren<Light>();
    }

    public void Pulse()
    {
        if (!targetLight) return;
        DOTween.Kill(targetLight);
        var seq = DOTween.Sequence()
            .Append(DOTween.To(() => targetLight.intensity, v => targetLight.intensity = v, pulsePeak, upTime).SetEase(Ease.OutQuad))
            .Append(DOTween.To(() => targetLight.intensity, v => targetLight.intensity = v, 1f, downTime).SetEase(Ease.InQuad));
        seq.SetTarget(targetLight);
    }
}