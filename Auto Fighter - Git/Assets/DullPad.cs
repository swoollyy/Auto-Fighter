using UnityEngine;
using DG.Tweening;
using System.Collections;

[DisallowMultipleComponent]
public class DullPad : MonoBehaviour
{
    public enum PadState { Idle, Flipping, Active, DisabledCooldown }
    public enum UpgradeKind { Blue, Green }

    [Header("Flip Animation")]
    public float halfFlipDuration = 0.30f;
    [Tooltip("Punch parameters when becoming active.")]
    public float punchScale = 0.10f;
    public int punchVibrato = 6;
    public float punchElasticity = 0.35f;

    [Header("Upgrade Probability")]
    [Range(0f, 1f)] public float blueChance = 0.5f; // remainder -> green

    [Header("Visuals / Colors")]
    public Renderer padRenderer;
    public Light padLight;
    public Color dullColor = new Color(0.35f, 0.35f, 0.35f);
    public Color blueColor = new Color(0.25f, 0.55f, 1f);
    public Color greenColor = new Color(0.25f, 1f, 0.55f);
    public float activeLightIntensity = 2.0f;

    [Header("Upgrade Method")]
    public bool addScriptInsteadOfInstantiate = true;
    public GameObject bluePadPrefab;
    public GameObject greenPadPrefab;

    [Header("Auto Revert (Inactivity)")]
    [Tooltip("If the active pad is not hit for this long, it will flip back to dull.")]
    [Min(0f)] public float inactivityAutoRevertSeconds = 15f;
    public bool enableInactivityAutoRevert = true;

    // Runtime
    public DynamicPadManager Manager { get; set; }
    public PadState State { get; private set; } = PadState.Idle;
    public float NextEnableTime { get; private set; }
    private float _interactionCooldownSeconds;
    private bool _hasVariantScript;
    private Tween _flipTween;
    private Component _variantComponent; // BluePad / GreenPad

    // Inactivity tracking
    private float _lastActivityAt;
    private Coroutine _inactivityCR;

    // Collider cache for safety
    private Collider _collider;

    void Reset()
    {
        padRenderer = GetComponent<Renderer>();
        padLight = GetComponentInChildren<Light>();
        _collider = GetComponent<Collider>();
    }

    void Awake()
    {
        if (!padRenderer) padRenderer = GetComponent<Renderer>();
        if (padLight) { padLight.color = dullColor; padLight.intensity = 0.6f; }
        _collider = GetComponent<Collider>();
        if (_collider) _collider.enabled = true;
        ApplyColor(dullColor);
    }

    public bool IsIdle => State == PadState.Idle;
    public bool IsFlipping => State == PadState.Flipping;
    public bool IsActivePad => State == PadState.Active;

    // NEW: single parameter for cooldown after ball interaction
    public void SetInteractionCooldown(float seconds)
    {
        _interactionCooldownSeconds = Mathf.Max(0f, seconds);
    }

    public void ScheduleNextEnable(float delay)
    {
        NextEnableTime = Time.time + Mathf.Max(0.01f, delay);
    }

    public void BeginFlipAndUpgrade()
    {
        if (State != PadState.Idle) return;
        if (_collider && !_collider.enabled) _collider.enabled = true;

        State = PadState.Flipping;
        Manager?.ReserveActivation(this);
        DoFlipSequence();
    }

    private void DoFlipSequence()
    {
        _flipTween?.Kill(false);

        var startEuler = transform.localEulerAngles;
        transform.localEulerAngles = new Vector3(startEuler.x, startEuler.y, 0f);

        var seq = DOTween.Sequence().SetUpdate(false);

        seq.Append(transform.DOLocalRotate(new Vector3(startEuler.x, startEuler.y, -90f), halfFlipDuration).SetEase(Ease.InQuad));
        seq.AppendCallback(MidFlipUpgrade);
        seq.Append(transform.DOLocalRotate(new Vector3(startEuler.x, startEuler.y, 0f), halfFlipDuration).SetEase(Ease.OutQuad));
        seq.AppendCallback(() =>
        {
            if (punchScale > 0f && State == PadState.Active)
                transform.DOPunchScale(Vector3.one * punchScale, 0.35f, punchVibrato, punchElasticity);
        });

        _flipTween = seq;
    }

    private void MidFlipUpgrade()
    {
        var cur = transform.localEulerAngles;
        transform.localEulerAngles = new Vector3(cur.x, cur.y, 90f);

        UpgradeKind chosen = (Random.value < blueChance) ? UpgradeKind.Blue : UpgradeKind.Green;
        UpgradeTo(chosen);
        State = PadState.Active;
        StartInactivityWatch();
    }

    private void UpgradeTo(UpgradeKind kind)
    {
        switch (kind)
        {
            case UpgradeKind.Blue:
                ApplyColor(blueColor);
                if (padLight) { padLight.color = blueColor; padLight.intensity = activeLightIntensity; }
                AttachVariant<BluePad>(bluePadPrefab);
                break;
            case UpgradeKind.Green:
                ApplyColor(greenColor);
                if (padLight) { padLight.color = greenColor; padLight.intensity = activeLightIntensity; }
                AttachVariant<GreenPad>(greenPadPrefab);
                break;
        }
    }

    private void AttachVariant<T>(GameObject prefab) where T : Component
    {
        if (addScriptInsteadOfInstantiate)
        {
            _variantComponent = GetComponent<T>();
            if (!_variantComponent) _variantComponent = gameObject.AddComponent<T>();
            _hasVariantScript = true;
            if (_variantComponent is IPadVariant variant)
                variant.BindHost(this);
        }
        else
        {
            if (prefab)
            {
                var inst = Instantiate(prefab, transform.position, transform.rotation, transform.parent);
                inst.transform.localScale = transform.localScale;
                var variant = inst.GetComponent<IPadVariant>();
                variant?.BindHost(null);
                Destroy(gameObject);
            }
        }
    }

    private void ApplyColor(Color c)
    {
        if (!padRenderer) return;
        if (!padRenderer.material || padRenderer.sharedMaterial == padRenderer.material)
            padRenderer.material = new Material(padRenderer.material);
        padRenderer.material.color = c;
        if (padRenderer.material.HasProperty("_Cull"))
            padRenderer.material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
    }

    // Called by Blue/Green variants when the ball interacts with the active pad.
    public void NotifyActivity()
    {
        _lastActivityAt = Time.time;
    }

    private void StartInactivityWatch()
    {
        if (!enableInactivityAutoRevert || inactivityAutoRevertSeconds <= 0f) return;
        _lastActivityAt = Time.time;
        if (_inactivityCR != null) StopCoroutine(_inactivityCR);
        _inactivityCR = StartCoroutine(InactivityRoutine());
    }

    private IEnumerator InactivityRoutine()
    {
        while (State == PadState.Active)
        {
            if (Time.time - _lastActivityAt >= inactivityAutoRevertSeconds)
            {
                RevertToDull(); // uses interaction cooldown
                yield break;
            }
            yield return null;
        }
        _inactivityCR = null;
    }

    /// <summary>
    /// Called by variant (Blue/Green) AFTER its effect completes or by inactivity timeout.
    /// Applies interaction cooldown before returning to idle.
    /// </summary>
    public void RevertToDull()
    {
        if (State != PadState.Active && State != PadState.Flipping) return;

        if (_inactivityCR != null)
        {
            StopCoroutine(_inactivityCR);
            _inactivityCR = null;
        }

        if (_hasVariantScript && _variantComponent)
        {
            Destroy(_variantComponent);
            _variantComponent = null;
            _hasVariantScript = false;
        }

        ApplyColor(dullColor);
        if (padLight)
        {
            padLight.color = dullColor;
            padLight.intensity = 0.6f;
        }

        if (_collider && !_collider.enabled) _collider.enabled = true;

        State = PadState.DisabledCooldown;
        StartCoroutine(DisabledCooldownRoutine(_interactionCooldownSeconds));
    }

    private IEnumerator DisabledCooldownRoutine(float seconds)
    {
        float endT = Time.time + seconds;
        while (Time.time < endT)
            yield return null;

        if (_collider && !_collider.enabled) _collider.enabled = true;

        State = PadState.Idle;
        Manager?.OnPadDeactivated(this);
    }

    /// <summary>
    /// Forcefully revert to dull immediately (no cooldown).
    /// Used by manager's random auto-disable tick.
    /// </summary>
    public void ForceRevertToDullNoCooldown()
    {
        if (State != PadState.Active && State != PadState.Flipping) return;

        if (_inactivityCR != null)
        {
            StopCoroutine(_inactivityCR);
            _inactivityCR = null;
        }

        if (_hasVariantScript && _variantComponent)
        {
            Destroy(_variantComponent);
            _variantComponent = null;
            _hasVariantScript = false;
        }

        ApplyColor(dullColor);
        if (padLight)
        {
            padLight.color = dullColor;
            padLight.intensity = 0.6f;
        }

        if (_collider && !_collider.enabled) _collider.enabled = true;

        State = PadState.Idle;
        Manager?.OnPadDeactivated(this);
    }
}

public interface IPadVariant
{
    void BindHost(DullPad host);
}