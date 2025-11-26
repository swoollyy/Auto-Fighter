using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class PowerupPickupTween : MonoBehaviour
{
    [Header("General")]
    [SerializeField] private Transform visualRoot; // optional: the transform that visually animates
    [SerializeField] private Transform playerTransform; // if null, will try to resolve from car on collect
    [SerializeField] private bool autoFindVisualRoot = true;

    [Header("Idle (pre-collect)")]
    [SerializeField] private bool playIdleHover = true;
    [SerializeField] private float idleHoverAmplitude = 0.15f;
    [SerializeField] private float idleHoverFrequency = 1.2f;
    [SerializeField] private float idleRotateSpeed = 45f;

    [Header("Collect Animation")]
    [Tooltip("Total time for bounce away and pull-in sequence.")]
    [SerializeField] private float collectTotalDuration = 0.80f;

    [Header("Bounce Away")]
    [SerializeField] private float bounceDuration = 0.30f;
    [SerializeField] private float bounceDistance = 1.2f;
    [SerializeField] private AnimationCurve bounceCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private float bounceUpward = 0.25f;

    [Header("Black Hole Pull")]
    [SerializeField] private float pullDuration = 0.50f;
    [SerializeField] private AnimationCurve pullCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private float minApproachDistance = 0.15f;
    [SerializeField] private float scaleDownAtEnd = 0.7f;

    [Header("FX")]
    [SerializeField] private ParticleSystem spawnFx;
    [SerializeField] private ParticleSystem collectFx;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip collectSfx;
    [SerializeField] private float sfxVolume = 0.85f;

    private Vector3 _idleBasePos;
    private bool _isCollected;
    private Coroutine _collectRoutine;

    private void Awake()
    {
        if (!visualRoot && autoFindVisualRoot)
        {
            // Prefer first child as visual root if present
            if (transform.childCount > 0) visualRoot = transform.GetChild(0);
            else visualRoot = transform;
        }
        if (!visualRoot) visualRoot = transform;
        _idleBasePos = visualRoot.position;
    }

    private void Update()
    {
        if (_isCollected) return;
        if (!playIdleHover) return;

        float t = Time.time;
        // Gentle hover and rotation
        Vector3 pos = _idleBasePos;
        pos.y += Mathf.Sin(t * Mathf.PI * 2f * idleHoverFrequency) * idleHoverAmplitude;
        visualRoot.position = pos;
        visualRoot.Rotate(Vector3.up, idleRotateSpeed * Time.deltaTime, Space.World);
    }

    public void PlaySpawn()
    {
        _idleBasePos = visualRoot.position;
        if (spawnFx) spawnFx.Play(true);
    }

    public void PlayCollect()
    {
        if (_isCollected) return;
        _isCollected = true;

        if (_collectRoutine != null) StopCoroutine(_collectRoutine);
        _collectRoutine = StartCoroutine(CollectSequence());
    }

    public float GetCollectDuration()
    {
        // Ensure duration matches sequence timing
        return Mathf.Max(collectTotalDuration, bounceDuration + pullDuration);
    }

    private IEnumerator CollectSequence()
    {
        // Stop idle animation baseline drift
        _idleBasePos = visualRoot.position;

        // SFX
        if (audioSource && collectSfx)
        {
            audioSource.PlayOneShot(collectSfx, sfxVolume);
        }
        // VFX
        if (collectFx) collectFx.Play(true);

        // Resolve player transform if missing
        if (!playerTransform)
        {
            // Try to find CarController in scene and use its transform
            var car = FindObjectOfType<CarController>();
            if (car) playerTransform = car.transform;
        }

        // No player target? Just a quick scale-down and vanish
        if (!playerTransform)
        {
            yield return ScaleDownAndDisable(0.25f);
            yield break;
        }

        // Phase 1: bounce away slightly from the player
        Vector3 startPos = visualRoot.position;
        Vector3 toPlayer = (playerTransform.position - startPos);
        Vector3 flatToPlayer = toPlayer; flatToPlayer.y = 0f;
        Vector3 awayDir = (-flatToPlayer.normalized);
        if (awayDir.sqrMagnitude < 1e-6f) awayDir = -transform.forward;

        Vector3 bounceTarget = startPos + awayDir * bounceDistance + Vector3.up * bounceUpward;

        float t = 0f;
        float bd = Mathf.Max(0.0001f, bounceDuration);
        while (t < bd)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / bd);
            float k = Mathf.Clamp01(bounceCurve.Evaluate(u));
            visualRoot.position = Vector3.Lerp(startPos, bounceTarget, k);
            yield return null;
        }

        // Phase 2: pull towards player (black hole style)
        Vector3 pullStart = visualRoot.position;
        float pd = Mathf.Max(0.0001f, pullDuration);
        float t2 = 0f;

        Vector3 initialScale = visualRoot.localScale;
        Vector3 targetScale = initialScale * Mathf.Clamp(scaleDownAtEnd, 0.05f, 1f);

        while (t2 < pd)
        {
            t2 += Time.deltaTime;
            float u = Mathf.Clamp01(t2 / pd);
            float k = Mathf.Clamp01(pullCurve.Evaluate(u));

            // Target point: playerTransform position (optionally offset to chest/camera height)
            Vector3 dest = playerTransform.position;
            // If car has a specific anchor, try to use it (optional)
            // e.g., dest = GetCarAnchorOrPlayer(playerTransform);

            // Ease position from pullStart to near player
            Vector3 p = Vector3.Lerp(pullStart, dest, k);

            // Clamp min distance so it doesn't clip inside the player early
            Vector3 toP = dest - p;
            float dist = toP.magnitude;
            if (dist > minApproachDistance)
            {
                visualRoot.position = p;
            }
            else
            {
                // Stop moving once we are very close
                visualRoot.position = dest - toP.normalized * minApproachDistance;
            }

            // Scale down as it approaches
            visualRoot.localScale = Vector3.Lerp(initialScale, targetScale, k);

            yield return null;
        }

        // Final snap and hide
        visualRoot.position = playerTransform.position;
        visualRoot.localScale = targetScale;

        // Disable visuals to let FuelPickup destroy on its own timing
        HideVisuals();
    }

    private IEnumerator ScaleDownAndDisable(float duration)
    {
        Vector3 startScale = visualRoot.localScale;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            visualRoot.localScale = Vector3.Lerp(startScale, Vector3.zero, u);
            yield return null;
        }
        HideVisuals();
    }

    private void HideVisuals()
    {
        // Disable renderers
        var rends = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
            rends[i].enabled = false;

        // Optionally disable collider to avoid extra hits
        var col = GetComponent<Collider>();
        if (col) col.enabled = false;
    }
}