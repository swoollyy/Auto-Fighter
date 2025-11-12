using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BumperLightController : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Light bumperLight;
    [SerializeField] private string lightObjectName = "BumperLight";

    [Header("Color/Range")]
    [SerializeField] private Color lightColor = new Color(1f, 0.9f, 0.3f);
    [SerializeField] private float baseRange = 4.0f;
    [SerializeField] private LightShadows shadows = LightShadows.None;

    [Header("Baseline")]
    [SerializeField, Tooltip("Idle baseline intensity when alive.")]
    private float baselineIntensity = 1.67f;

    [Header("Normal Hit Pulse")]
    [SerializeField] private float pulsePeakIntensity = 9f;
    [SerializeField] private float pulseUpTime = 0.10f;
    [SerializeField] private float pulseDownTime = 0.30f;

    [Header("Ricochet Mode")]
    [SerializeField] private float ricochetBaseIntensity = 2.0f;
    [SerializeField] private float ricochetAddPerHit = 0.8f;
    [SerializeField] private float ricochetMaxIntensity = 10f;
    [SerializeField] private float ricochetRange = 5.0f;
    [SerializeField] private float ricochetStepTween = 0.08f;

    [Header("Idle Return")]
    [SerializeField] private bool idleTurnOff = true;
    [SerializeField] private float idleOffDelay = 0.75f;
    [SerializeField] private float idleOffFade = 0.25f;

    [Header("Ricochet Cleanup")]
    [Tooltip("Fade completely off after ricochet ends instead of staying at baseline immediately.")]
    [SerializeField] private bool fadeOffAfterRicochet = true;
    [Tooltip("Delay before starting forced off fade (seconds).")]
    [SerializeField] private float postRicochetOffDelay = 0.25f;

    private Tween _intensityTween;
    private bool _ricochetMode;
    private bool _dead;
    private float _lastActiveTime;
    private bool _idleFading;

    // NEW: suppress idle fade while post-ricochet sequence drives intensity down
    private float _suppressIdleFadeUntil;

    void Awake()
    {
        EnsureLight();
        ApplyBaseSettings();
        _lastActiveTime = Time.time;
    }

    void Update()
    {
        if (!_dead && idleTurnOff && !_ricochetMode && bumperLight && !_idleFading)
        {
            // Skip idle fade until ricochet cleanup sequence finished
            if (Time.time < _suppressIdleFadeUntil) return;

            if (Time.time - _lastActiveTime >= idleOffDelay)
            {
                _idleFading = true;
                _intensityTween?.Kill(false);
                float target = Mathf.Max(0f, baselineIntensity);
                _intensityTween = DOTween.To(() => bumperLight.intensity, v => bumperLight.intensity = v, target, idleOffFade)
                    .SetEase(Ease.InQuad)
                    .SetUpdate(true)
                    .OnComplete(() =>
                    {
                        if (bumperLight)
                        {
                            bumperLight.intensity = target;
                            bumperLight.enabled = target > 0f;
                        }
                        _idleFading = false;
                    });
            }
        }

        // Safety: if not ricochet and intensity is irrationally high, clamp down
        if (!_ricochetMode && !_dead && bumperLight)
        {
            float over = bumperLight.intensity - Mathf.Max(baselineIntensity, 0f);
            if (over > 0.01f && Time.time >= _suppressIdleFadeUntil)
            {
                bumperLight.intensity = Mathf.Lerp(bumperLight.intensity, Mathf.Max(baselineIntensity, 0f), Time.deltaTime * 6f);
            }
        }
    }

    void OnDisable() => _intensityTween?.Kill(false);

    private void EnsureLight()
    {
        if (bumperLight != null) return;

        var child = transform.Find(lightObjectName);
        if (child != null)
            bumperLight = child.GetComponent<Light>();

        if (bumperLight == null)
        {
            var go = new GameObject(lightObjectName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            bumperLight = go.AddComponent<Light>();
        }
    }

    private void ApplyBaseSettings()
    {
        if (!bumperLight) return;
        bumperLight.type = LightType.Point;
        bumperLight.color = lightColor;
        bumperLight.range = baseRange;
        bumperLight.shadows = shadows;
        bumperLight.intensity = baselineIntensity;
        bumperLight.enabled = !_dead && (baselineIntensity > 0f);
    }

    private void MarkActive()
    {
        _lastActiveTime = Time.time;
        if (bumperLight && !bumperLight.enabled)
        {
            bumperLight.enabled = true;
            if (bumperLight.intensity <= 0f && !_ricochetMode)
                bumperLight.intensity = baselineIntensity;
        }
    }

    private void SetIntensityInstant(float v)
    {
        if (!bumperLight) return;
        bumperLight.intensity = Mathf.Max(0f, v);
    }

    public void PulseFlash()
    {
        if (_dead || _ricochetMode || !bumperLight) return;

        MarkActive();
        _idleFading = false;

        _intensityTween?.Kill(false);
        bumperLight.enabled = true;

        _intensityTween = DOTween.Sequence().SetUpdate(true)
            .Append(DOTween.To(() => bumperLight.intensity, x => bumperLight.intensity = x, pulsePeakIntensity, pulseUpTime).SetEase(Ease.OutQuad))
            .Append(DOTween.To(() => bumperLight.intensity, x => bumperLight.intensity = x, baselineIntensity, pulseDownTime).SetEase(Ease.InQuad));
    }

    public void StartRicochetMode()
    {
        if (_dead || !bumperLight) return;
        _ricochetMode = true;
        _idleFading = false;
        _suppressIdleFadeUntil = 0f;

        MarkActive();
        _intensityTween?.Kill(false);
        DOTween.Kill(bumperLight); // kill any leftover tweens targeting light

        bumperLight.enabled = true;
        bumperLight.range = ricochetRange;

        float target = Mathf.Max(baselineIntensity, ricochetBaseIntensity);
        if (bumperLight.intensity < target)
            SetIntensityInstant(target);
    }

    public void IncrementRicochet()
    {
        if (_dead || !bumperLight) return;
        _ricochetMode = true;
        MarkActive();
        bumperLight.enabled = true;
        bumperLight.range = ricochetRange;

        float target = Mathf.Min(ricochetMaxIntensity, bumperLight.intensity + Mathf.Max(0f, ricochetAddPerHit));
        _intensityTween?.Kill(false);
        DOTween.Kill(bumperLight);
        _intensityTween = DOTween.To(() => bumperLight.intensity, v => bumperLight.intensity = v, target, ricochetStepTween)
                                 .SetEase(Ease.OutQuad)
                                 .SetUpdate(true);
    }

    public void EndRicochetMode()
    {
        if (_dead || !bumperLight || !_ricochetMode) return;

        _ricochetMode = false;

        _intensityTween?.Kill(false);
        DOTween.Kill(bumperLight);

        bumperLight.range = baseRange;

        if (fadeOffAfterRicochet)
        {
            // Prevent idle fade from interrupting our off sequence
            _suppressIdleFadeUntil = Time.time + postRicochetOffDelay + idleOffFade + 0.05f;

            float targetBaseline = Mathf.Max(0f, baselineIntensity);
            _intensityTween = DOTween.Sequence().SetUpdate(true)
                .Append(DOTween.To(() => bumperLight.intensity, v => bumperLight.intensity = v, targetBaseline, 0.20f).SetEase(Ease.InQuad))
                .AppendInterval(postRicochetOffDelay)
                .Append(DOTween.To(() => bumperLight.intensity, v => bumperLight.intensity = v, 0f, idleOffFade)
                              .SetEase(Ease.InQuad)
                              .OnComplete(() =>
                              {
                                  if (bumperLight)
                                  {
                                      bumperLight.intensity = 0f;
                                      bumperLight.enabled = false;
                                  }
                              }));
        }
        else
        {
            // Allow immediate idle fade by backdating last active time
            _lastActiveTime = Time.time - idleOffDelay - 0.02f;
            _suppressIdleFadeUntil = 0f;
            _intensityTween = DOTween.To(() => bumperLight.intensity, v => bumperLight.intensity = v, Mathf.Max(0f, baselineIntensity), 0.25f)
                                     .SetEase(Ease.InQuad)
                                     .SetUpdate(true);
        }
    }

    public void HandleBumperDeath()
    {
        _dead = true;
        _ricochetMode = false;
        _intensityTween?.Kill(false);
        DOTween.Kill(bumperLight);
        if (bumperLight)
        {
            bumperLight.intensity = 0f;
            bumperLight.enabled = false;
            bumperLight.range = baseRange;
        }
    }

    public void HandleBumperRevive()
    {
        _dead = false;
        _ricochetMode = false;
        _intensityTween?.Kill(false);
        DOTween.Kill(bumperLight);
        ApplyBaseSettings();
        MarkActive();
    }

    public void ForceResetLight(bool offCompletely)
    {
        _intensityTween?.Kill(false);
        DOTween.Kill(bumperLight);
        if (!bumperLight) return;
        bumperLight.range = baseRange;
        bumperLight.intensity = offCompletely ? 0f : baselineIntensity;
        bumperLight.enabled = !offCompletely && baselineIntensity > 0f;
        _suppressIdleFadeUntil = 0f;
    }

    public void EndRicochetLight()
    {
        EndRicochetMode();
    }
}