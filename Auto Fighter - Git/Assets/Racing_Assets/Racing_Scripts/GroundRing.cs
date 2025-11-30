using System;
using UnityEngine;

/// <summary>
/// Simple ground ring visual that expands / fades and invokes callback on complete.
/// Designed to be pooled.
/// </summary>
[DisallowMultipleComponent]
public class GroundRing : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private Transform ringRoot;
    [SerializeField] private float baseScale = 1f;
    [SerializeField] private UnityEngine.UI.Image debugImage; // optional for UI prototypes

    [Header("Timing")]
    [SerializeField] private float fadeIn = 0.06f;
    [SerializeField] private float hold = 0.18f;
    [SerializeField] private float fadeOut = 0.25f;

    private Coroutine _cr;

    // Backwards-compatible Play: optional holdOverride (in seconds)
    public void Play(float radius, Action onComplete = null, float? holdOverride = null)
    {
        gameObject.SetActive(true);
        transform.localScale = Vector3.one * (radius * 0.02f); // scale tweak: ring prefab expects 1==1m maybe adjust
        if (_cr != null) StopCoroutine(_cr);
        _cr = StartCoroutine(PlayRoutine(radius, onComplete, holdOverride));
    }

    private System.Collections.IEnumerator PlayRoutine(float radius, Action onComplete, float? holdOverride)
    {
        float t = 0f;
        // Allow override of the serialized hold time if provided
        float actualHold = holdOverride.HasValue ? Mathf.Max(0f, holdOverride.Value) : hold;

        float startScale = transform.localScale.x;
        float peakScale = radius * 0.5f; // visual scale; adjust if needed

        // fade in
        float elapsed = 0f;
        while (elapsed < fadeIn)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / fadeIn);
            transform.localScale = Vector3.one * Mathf.Lerp(startScale, peakScale, k);
            yield return null;
        }

        // hold
        elapsed = 0f;
        while (elapsed < actualHold)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // fade out and shrink
        elapsed = 0f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / fadeOut);
            transform.localScale = Vector3.one * Mathf.Lerp(peakScale, startScale * 0.2f, k);
            yield return null;
        }

        onComplete?.Invoke();
        _cr = null;
    }
}