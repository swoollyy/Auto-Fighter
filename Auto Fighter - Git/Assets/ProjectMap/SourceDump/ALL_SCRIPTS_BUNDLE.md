# All Scripts Bundle
- Generated: 2025-11-13T23:04:11.6663113Z (UTC)
- Unity: 2022.3.62f2
- Files: 127

## Assets/BumperAnimScript.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class BumperAnimScript : MonoBehaviour
{

    private Material defMaterial;
    private Color defMatColor;

    private Bumper bumper;

    [SerializeField] private bool resetHPBarAlphaToZero = true; // hide HP bar before each flash
    [SerializeField] private Image HPBar;              // assign your HP bar image here
    [SerializeField] private Color hpFlashColor = Color.white;
    [SerializeField, Range(0f, 1f)] private float hpFlashAlpha = 0.9f;
    [SerializeField] private float hpFlashDuration = 0.18f; // total flash time
    [SerializeField] private Vector2 hpPunchScale = new Vector2(1.08f, 1.08f);
    [SerializeField] private int hpPunchVibrato = 9;        // how �wobbly� the punch is
    [SerializeField, Range(0f, 1f)] private float hpPunchElasticity = 0.12f;
    [SerializeField, Range(0f, .1f)] private float genScale = 0.04f; // general scale reduction to keep things in check

    private Vector3 _defLocalScale;        // default bumper scale
    private Vector3 _hpRTDefaultScale;     // default HP bar rect scale
    private float _hpGroupDefaultAlpha;    // default canvas group alpha

    private Color _hpDefaultColor;
    private RectTransform _hpRT;

    [SerializeField] private CanvasGroup hpGroup;


    void Awake()
    {
        if(hpGroup != null) hpGroup.alpha = 0f;
    }

    // Start is called before the first frame update
    void Start()
    {
        defMaterial = GetComponent<Renderer>().material;
        defMatColor = defMaterial.color;
        bumper = GetComponent<Bumper>();

        if(HPBar != null)
        {
            _hpDefaultColor = HPBar.color;
            _hpRT = HPBar.rectTransform;

        }

        if(hpGroup != null)
        {
            hpGroup.interactable = false;
            hpGroup.blocksRaycasts = false;
        }

        _defLocalScale = transform.localScale;

        if (_hpRT != null)
            _hpRTDefaultScale = _hpRT.localScale;

        if (hpGroup != null)
            _hpGroupDefaultAlpha = hpGroup.alpha;
    }

    // Update is called once per frame
    void Update()
    {
        HPBar.fillAmount = Mathf.MoveTowards(HPBar.fillAmount, bumper.curHealth / bumper.maxHealth, Time.deltaTime * 2);
    }

    public void ResetTweenState()
    {
        transform.DOKill(false);
        if (defMaterial != null) defMaterial.DOKill(false);
        if (HPBar != null) HPBar.DOKill(false);
        if (_hpRT != null) _hpRT.DOKill(false);
        if (hpGroup != null) hpGroup.DOKill(false);

        transform.localScale = _defLocalScale;
        if(_hpRT != null) _hpRT.localScale = _hpRTDefaultScale;

        if (defMaterial != null) defMaterial.color = defMatColor;
        if(HPBar != null) HPBar.color = _hpDefaultColor;


        if(hpGroup != null)
        {
            hpGroup.alpha = resetHPBarAlphaToZero ? 0f : _hpGroupDefaultAlpha;
            hpGroup.interactable = false;
            hpGroup.blocksRaycasts = false;
        }

    }

    public void BumperHit()
    {
        ResetTweenState();
        DOTween.Kill(transform);
        transform.DOPunchScale(new Vector3(.3f, .3f, .3f), 0.2f, 2, .1f);
        defMaterial.DOColor(Color.white, 0.1f).OnComplete(() => {
            defMaterial.DOColor(defMatColor, 0.1f);
        });

        FlashHPBar();
    }

    private void FlashHPBar()
    {

        if (hpGroup != null)
        {
            DOTween.Kill(hpGroup);
            hpGroup.DOFade(1f, 0.05f).SetUpdate(true); // quick pop-in, pause-safe
        }

        if (HPBar == null) return;

        DOTween.Kill(HPBar);
        if (_hpRT != null) DOTween.Kill(_hpRT);

        var target = hpFlashColor;
        target.a = Mathf.Clamp01(hpFlashAlpha);

        var half = hpFlashDuration * 0.5f;

        // create seq first
        var seq = DOTween.Sequence().SetId(HPBar);

        // 1) flash up to target color
        seq.Append(HPBar.DOColor(target, half).SetEase(Ease.OutQuad));

        // 2) punch (guarded)
        if (_hpRT != null)
        {
            seq.Join(_hpRT.DOPunchScale(
                new Vector3(hpPunchScale.x - genScale, hpPunchScale.y - genScale, 0f),
                hpFlashDuration,
                hpPunchVibrato,
                hpPunchElasticity
            ).SetEase(Ease.OutQuad));
        }

        // 3) return to default color
        seq.Append(HPBar.DOColor(_hpDefaultColor, half).SetEase(Ease.InQuad));

        // 4) fade out the whole bar
        if (hpGroup != null)
            seq.Append(hpGroup.DOFade(0f, 0.25f).SetEase(Ease.InQuad).SetUpdate(true));

        // make the whole sequence run while paused
        seq.SetUpdate(true);

    }

}

```

## Assets/BumperLightController.cs

```csharp
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
```

## Assets/DullPad.cs

```csharp
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
```

## Assets/Editor/ProjectSummary.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.IO; // <-- added

public static class ProjectSummary
{
    private const string OutputDir = "Assets/ProjectMap";
    private const string MdPath = OutputDir + "/PROJECT_SUMMARY.md";
    private const string JsonPath = OutputDir + "/project-summary.json";

    [MenuItem("Tools/Generate Project Summary")]
    public static void Generate()
    {
        try
        {
            System.IO.Directory.CreateDirectory(OutputDir);
            EditorUtility.DisplayProgressBar("Project Summary", "Collecting assemblies...", 0.05f);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(IsProjectAssembly)
                .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var map = new SummaryMap
            {
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                assemblies = new List<AssemblyInfo>(),
            };

            int iAsm = 0;
            foreach (var asm in assemblies)
            {
                EditorUtility.DisplayProgressBar("Project Summary", $"Scanning {asm.GetName().Name}", 0.1f + 0.8f * (iAsm++ / Math.Max(1f, assemblies.Length)));
                var ainfo = new AssemblyInfo { name = asm.GetName().Name, types = new List<TypeInfoLite>() };

                foreach (var t in SafeGetTypes(asm))
                {
                    if (t == null || t.FullName == null) continue;
                    if (t.FullName.StartsWith("UnityEngine.") || t.FullName.StartsWith("UnityEditor.")) continue;

                    var til = new TypeInfoLite
                    {
                        name = t.Name,
                        fullName = t.FullName,
                        ns = t.Namespace ?? "",
                        baseType = t.BaseType?.FullName ?? "",
                        isMonoBehaviour = typeof(MonoBehaviour).IsAssignableFrom(t),
                        isScriptableObject = typeof(ScriptableObject).IsAssignableFrom(t),
                        implements = t.GetInterfaces().Select(x => x.FullName ?? x.Name).Distinct().OrderBy(s => s).ToList(),
                        fields = new List<MemberLite>(),
                        properties = new List<MemberLite>(),
                        methods = new List<MethodLite>(),
                        unityMessages = new List<string>(),
                        isIPowerup = t.GetInterfaces().Any(ii => ii.Name == "IPowerup" || (ii.FullName ?? "").EndsWith(".IPowerup")),
                    };

                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        bool serialized = f.IsPublic && !Attribute.IsDefined(f, typeof(NonSerializedAttribute))
                                          || Attribute.IsDefined(f, typeof(SerializeField));
                        if (serialized || f.IsPublic)
                        {
                            til.fields.Add(new MemberLite { name = f.Name, type = f.FieldType.FullName ?? f.FieldType.Name, flags = serialized ? "serialized" : (f.IsPublic ? "public" : "") });
                            if (til.fields.Count > 24) break;
                        }
                    }

                    foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    {
                        til.properties.Add(new MemberLite { name = p.Name, type = p.PropertyType.FullName ?? p.PropertyType.Name, flags = $"{(p.CanRead ? "get" : "")}{(p.CanWrite ? "/set" : "")}" });
                        if (til.properties.Count > 24) break;
                    }

                    foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsSpecialName) continue;
                        til.methods.Add(new MethodLite
                        {
                            name = m.Name,
                            ret = m.ReturnType.FullName ?? m.ReturnType.Name,
                            @params = m.GetParameters().Select(pp => pp.ParameterType.Name).ToList()
                        });
                        if (til.methods.Count > 24) break;
                    }

                    var unityMsgs = new[] { "Awake", "OnEnable", "Start", "Update", "LateUpdate", "FixedUpdate", "OnDisable", "OnDestroy", "OnCollisionEnter", "OnCollisionExit", "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay", "OnValidate", "Reset" };
                    foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                        if (unityMsgs.Contains(m.Name)) til.unityMessages.Add(m.Name);
                    til.unityMessages = til.unityMessages.Distinct().OrderBy(s => s).ToList();

                    ainfo.types.Add(til);
                }

                ainfo.types = ainfo.types.OrderBy(t => t.fullName, StringComparer.OrdinalIgnoreCase).ToList();
                map.assemblies.Add(ainfo);
            }

            map.summary = new Summary
            {
                assemblies = map.assemblies.Count,
                monoBehaviours = map.assemblies.Sum(a => a.types.Count(t => t.isMonoBehaviour)),
                scriptableObjects = map.assemblies.Sum(a => a.types.Count(t => t.isScriptableObject)),
                ipowerups = map.assemblies.SelectMany(a => a.types.Where(t => t.isIPowerup).Select(t => t.fullName)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                classesNamedPowerup = map.assemblies.SelectMany(a => a.types.Where(t => t.name.EndsWith("Powerup", StringComparison.OrdinalIgnoreCase)).Select(t => t.fullName)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                pinballTypes = map.assemblies.SelectMany(a => a.types.Where(t => t.name.Equals("Pinball", StringComparison.OrdinalIgnoreCase)).Select(t => t.fullName)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                runContextTypes = map.assemblies.SelectMany(a => a.types.Where(t => t.name.Equals("IRunContext", StringComparison.OrdinalIgnoreCase) || t.name.EndsWith("RunContext")).Select(t => t.fullName)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            };

            System.IO.File.WriteAllText(MdPath, BuildMarkdown(map), new UTF8Encoding(false));

            var json = EditorJsonUtility.ToJson(map, true);
            System.IO.File.WriteAllText(JsonPath, json, new UTF8Encoding(false));

            AssetDatabase.Refresh();
            Debug.Log($"[ProjectSummary] Generated:\n- {MdPath}\n- {JsonPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProjectSummary] Failed: {ex}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // NEW: Export all C# sources under Assets into a single bundle I can ingest here
    [MenuItem("Tools/Export All Scripts For Review")]
    public static void ExportAllScriptsForReview()
    {
        try
        {
            Directory.CreateDirectory(OutputDir);
            var dumpDir = Path.Combine(OutputDir, "SourceDump").Replace("\\", "/");
            Directory.CreateDirectory(dumpDir);

            string bundlePath = Path.Combine(dumpDir, "ALL_SCRIPTS_BUNDLE.md").Replace("\\", "/");
            var projectAssetsAbs = Application.dataPath.Replace("\\", "/"); // .../Project/Assets
            var projectRootAbs = Path.GetDirectoryName(projectAssetsAbs)!.Replace("\\", "/");

            var allCs = Directory.EnumerateFiles(projectAssetsAbs, "*.cs", SearchOption.AllDirectories)
                                 .Select(p => p.Replace("\\", "/"))
                                 .Where(p => !p.Contains("/ProjectMap/")) // avoid dumping the dump
                                 .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            using (var sw = new StreamWriter(bundlePath, false, new UTF8Encoding(false)))
            {
                sw.WriteLine("# All Scripts Bundle");
                sw.WriteLine($"- Generated: {DateTime.UtcNow:o} (UTC)");
                sw.WriteLine($"- Unity: {Application.unityVersion}");
                sw.WriteLine($"- Files: {allCs.Count}");
                sw.WriteLine();

                int i = 0;
                foreach (var abs in allCs)
                {
                    EditorUtility.DisplayProgressBar("Export All Scripts", $"Bundling {++i}/{allCs.Count}", i / (float)Math.Max(1, allCs.Count));

                    var rel = "Assets" + abs.Substring(projectAssetsAbs.Length);
                    sw.WriteLine($"## {rel}");
                    sw.WriteLine();
                    sw.WriteLine("```csharp");
                    sw.Write(File.ReadAllText(abs));
                    sw.WriteLine();
                    sw.WriteLine("```");
                    sw.WriteLine();
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[ProjectSummary] Exported all scripts to: {bundlePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProjectSummary] ExportAllScriptsForReview failed: {ex}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static bool IsProjectAssembly(Assembly asm)
    {
        var n = asm.GetName().Name ?? "";
        string[] exclude = { "Unity.", "UnityEngine", "UnityEditor", "System", "Microsoft", "mscorlib", "netstandard", "Bee", "nunit" };
        return !exclude.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException rtle) { return rtle.Types.Where(t => t != null); }
        catch { return Array.Empty<Type>(); }
    }

    private static string BuildMarkdown(SummaryMap map)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Project Summary");
        sb.AppendLine($"- Generated: {map.generatedAtUtc} (UTC)");
        sb.AppendLine($"- Unity: {map.unityVersion}");
        sb.AppendLine($"- Assemblies: {map.summary.assemblies}");
        sb.AppendLine($"- MonoBehaviours: {map.summary.monoBehaviours}");
        sb.AppendLine($"- ScriptableObjects: {map.summary.scriptableObjects}");
        if (map.summary.ipowerups.Any())
        {
            sb.AppendLine("- IPowerup implementations:");
            foreach (var p in map.summary.ipowerups) sb.AppendLine($"  - {p}");
        }
        if (map.summary.classesNamedPowerup.Any())
        {
            sb.AppendLine("- *Powerup classes:");
            foreach (var p in map.summary.classesNamedPowerup) sb.AppendLine($"  - {p}");
        }
        if (map.summary.pinballTypes.Any())
        {
            sb.AppendLine("- Pinball types:");
            foreach (var p in map.summary.pinballTypes) sb.AppendLine($"  - {p}");
        }
        if (map.summary.runContextTypes.Any())
        {
            sb.AppendLine("- RunContext-related types:");
            foreach (var p in map.summary.runContextTypes) sb.AppendLine($"  - {p}");
        }
        sb.AppendLine();
        foreach (var asm in map.assemblies.OrderBy(a => a.name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"## {asm.name}");
            foreach (var t in asm.types)
            {
                var tags = new List<string>();
                if (t.isMonoBehaviour) tags.Add("MonoBehaviour");
                if (t.isScriptableObject) tags.Add("ScriptableObject");
                if (t.isIPowerup) tags.Add("IPowerup");
                var tagStr = tags.Count > 0 ? $" ({string.Join(", ", tags)})" : "";
                sb.AppendLine($"- {t.fullName}{tagStr}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Serializable]
    private class SummaryMap
    {
        public string generatedAtUtc;
        public string unityVersion;
        public List<AssemblyInfo> assemblies;
        public Summary summary;
    }
    [Serializable] private class Summary { public int assemblies, monoBehaviours, scriptableObjects; public List<string> ipowerups, classesNamedPowerup, pinballTypes, runContextTypes; }
    [Serializable] private class AssemblyInfo { public string name; public List<TypeInfoLite> types; }
    [Serializable]
    private class TypeInfoLite
    {
        public string name, fullName, ns, baseType;
        public bool isMonoBehaviour, isScriptableObject, isIPowerup;
        public List<string> implements;
        public List<MemberLite> fields;
        public List<MemberLite> properties;
        public List<MethodLite> methods;
        public List<string> unityMessages;
    }
    [Serializable] private class MemberLite { public string name, type, flags; }
    [Serializable] private class MethodLite { public string name, ret; public List<string> @params; }
}
```

## Assets/FireWispVelocityDriver.cs

```csharp
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class FireVelocityFeeder : MonoBehaviour
{
    public int materialIndex = 1;      // fire material slot
    public bool useRigidbody = true;
    public float smoothTime = 0.06f;
    public float velScale = 1f;

    public bool sendAngular = true;
    public float angSmooth = 0.06f;

    Rigidbody _rb;
    Renderer _rend;
    MaterialPropertyBlock _mpb;

    Vector3 _prevPos, _velSmoothed, _angSmoothed;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rend = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
        _prevPos = transform.position;

        // IMPORTANT: PropertyBlocks are ignored by Static Batching
        gameObject.isStatic = false;
    }

    void Update()
    {
        Vector3 vel = Vector3.zero;
        Vector3 ang = Vector3.zero;

        if (useRigidbody && _rb != null)
        {
            vel = _rb.velocity;
            ang = _rb.angularVelocity;
        }
        else
        {
            // Derive velocity from transform motion
            Vector3 pos = transform.position;
            vel = (pos - _prevPos) / Mathf.Max(Time.deltaTime, 1e-5f);
            _prevPos = pos;
        }

        // Smooth to reduce jitter
        float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, smoothTime));
        _velSmoothed = Vector3.Lerp(_velSmoothed, vel, t);

        float ta = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, angSmooth));
        _angSmoothed = Vector3.Lerp(_angSmoothed, ang, ta);

        // Apply to material slot
        _rend.GetPropertyBlock(_mpb, materialIndex);
        _mpb.SetVector("_VelWS", _velSmoothed * velScale);
        if (sendAngular) _mpb.SetVector("_AngVelWS", _angSmoothed);
        _rend.SetPropertyBlock(_mpb, materialIndex);
    }
}

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleAudio.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

#if true // MODULE_MARKER
using System;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using UnityEngine;
using UnityEngine.Audio; // Required for AudioMixer

#pragma warning disable 1591
namespace DG.Tweening
{
	public static class DOTweenModuleAudio
    {
        #region Shortcuts

        #region Audio

        /// <summary>Tweens an AudioSource's volume to the given value.
        /// Also stores the AudioSource as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach (0 to 1)</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DOFade(this AudioSource target, float endValue, float duration)
        {
            if (endValue < 0) endValue = 0;
            else if (endValue > 1) endValue = 1;
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.volume, x => target.volume = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens an AudioSource's pitch to the given value.
        /// Also stores the AudioSource as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DOPitch(this AudioSource target, float endValue, float duration)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.pitch, x => target.pitch = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #endregion

        #region AudioMixer

        /// <summary>Tweens an AudioMixer's exposed float to the given value.
        /// Also stores the AudioMixer as the tween's target so it can be used for filtered operations.
        /// Note that you need to manually expose a float in an AudioMixerGroup in order to be able to tween it from an AudioMixer.</summary>
        /// <param name="floatName">Name given to the exposed float to set</param>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DOSetFloat(this AudioMixer target, string floatName, float endValue, float duration)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(()=> {
                    float currVal;
                    target.GetFloat(floatName, out currVal);
                    return currVal;
                }, x=> target.SetFloat(floatName, x), endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #region Operation Shortcuts

        /// <summary>
        /// Completes all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens completed
        /// (meaning the tweens that don't have infinite loops and were not already complete)
        /// </summary>
        /// <param name="withCallbacks">For Sequences only: if TRUE also internal Sequence callbacks will be fired,
        /// otherwise they will be ignored</param>
        public static int DOComplete(this AudioMixer target, bool withCallbacks = false)
        {
            return DOTween.Complete(target, withCallbacks);
        }

        /// <summary>
        /// Kills all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens killed.
        /// </summary>
        /// <param name="complete">If TRUE completes the tween before killing it</param>
        public static int DOKill(this AudioMixer target, bool complete = false)
        {
            return DOTween.Kill(target, complete);
        }

        /// <summary>
        /// Flips the direction (backwards if it was going forward or viceversa) of all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens flipped.
        /// </summary>
        public static int DOFlip(this AudioMixer target)
        {
            return DOTween.Flip(target);
        }

        /// <summary>
        /// Sends to the given position all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens involved.
        /// </summary>
        /// <param name="to">Time position to reach
        /// (if higher than the whole tween duration the tween will simply reach its end)</param>
        /// <param name="andPlay">If TRUE will play the tween after reaching the given position, otherwise it will pause it</param>
        public static int DOGoto(this AudioMixer target, float to, bool andPlay = false)
        {
            return DOTween.Goto(target, to, andPlay);
        }

        /// <summary>
        /// Pauses all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens paused.
        /// </summary>
        public static int DOPause(this AudioMixer target)
        {
            return DOTween.Pause(target);
        }

        /// <summary>
        /// Plays all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens played.
        /// </summary>
        public static int DOPlay(this AudioMixer target)
        {
            return DOTween.Play(target);
        }

        /// <summary>
        /// Plays backwards all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens played.
        /// </summary>
        public static int DOPlayBackwards(this AudioMixer target)
        {
            return DOTween.PlayBackwards(target);
        }

        /// <summary>
        /// Plays forward all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens played.
        /// </summary>
        public static int DOPlayForward(this AudioMixer target)
        {
            return DOTween.PlayForward(target);
        }

        /// <summary>
        /// Restarts all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens restarted.
        /// </summary>
        public static int DORestart(this AudioMixer target)
        {
            return DOTween.Restart(target);
        }

        /// <summary>
        /// Rewinds all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens rewinded.
        /// </summary>
        public static int DORewind(this AudioMixer target)
        {
            return DOTween.Rewind(target);
        }

        /// <summary>
        /// Smoothly rewinds all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens rewinded.
        /// </summary>
        public static int DOSmoothRewind(this AudioMixer target)
        {
            return DOTween.SmoothRewind(target);
        }

        /// <summary>
        /// Toggles the paused state (plays if it was paused, pauses if it was playing) of all tweens that have this target as a reference
        /// (meaning tweens that were started from this target, or that had this target added as an Id)
        /// and returns the total number of tweens involved.
        /// </summary>
        public static int DOTogglePause(this AudioMixer target)
        {
            return DOTween.TogglePause(target);
        }

        #endregion

        #endregion

        #endregion
    }
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleEPOOutline.cs

```csharp
using UnityEngine;

#if false || EPO_DOTWEEN // MODULE_MARKER

using EPOOutline;
using DG.Tweening.Plugins.Options;
using DG.Tweening;
using DG.Tweening.Core;

namespace DG.Tweening
{
    public static class DOTweenModuleEPOOutline
    {
        public static int DOKill(this SerializedPass target, bool complete)
        {
            return DOTween.Kill(target, complete);
        }

        public static TweenerCore<float, float, FloatOptions> DOFloat(this SerializedPass target, string propertyName, float endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetFloat(propertyName), x => target.SetFloat(propertyName, x), endValue, duration);
            tweener.SetOptions(true).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Color, Color, ColorOptions> DOFade(this SerializedPass target, string propertyName, float endValue, float duration)
        {
            var tweener = DOTween.ToAlpha(() => target.GetColor(propertyName), x => target.SetColor(propertyName, x), endValue, duration);
            tweener.SetOptions(true).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Color, Color, ColorOptions> DOColor(this SerializedPass target, string propertyName, Color endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetColor(propertyName), x => target.SetColor(propertyName, x), endValue, duration);
            tweener.SetOptions(false).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Vector4, Vector4, VectorOptions> DOVector(this SerializedPass target, string propertyName, Vector4 endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetVector(propertyName), x => target.SetVector(propertyName, x), endValue, duration);
            tweener.SetOptions(false).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<float, float, FloatOptions> DOFloat(this SerializedPass target, int propertyId, float endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetFloat(propertyId), x => target.SetFloat(propertyId, x), endValue, duration);
            tweener.SetOptions(true).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Color, Color, ColorOptions> DOFade(this SerializedPass target, int propertyId, float endValue, float duration)
        {
            var tweener = DOTween.ToAlpha(() => target.GetColor(propertyId), x => target.SetColor(propertyId, x), endValue, duration);
            tweener.SetOptions(true).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Color, Color, ColorOptions> DOColor(this SerializedPass target, int propertyId, Color endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetColor(propertyId), x => target.SetColor(propertyId, x), endValue, duration);
            tweener.SetOptions(false).SetTarget(target);
            return tweener;
        }

        public static TweenerCore<Vector4, Vector4, VectorOptions> DOVector(this SerializedPass target, int propertyId, Vector4 endValue, float duration)
        {
            var tweener = DOTween.To(() => target.GetVector(propertyId), x => target.SetVector(propertyId, x), endValue, duration);
            tweener.SetOptions(false).SetTarget(target);
            return tweener;
        }

        public static int DOKill(this Outlinable.OutlineProperties target, bool complete = false)
        {
            return DOTween.Kill(target, complete);
        }

        public static int DOKill(this Outliner target, bool complete = false)
        {
            return DOTween.Kill(target, complete);
        }

        /// <summary>
        /// Controls the alpha (transparency) of the outline
        /// </summary>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this Outlinable.OutlineProperties target, float endValue, float duration)
        {
            var tweener = DOTween.ToAlpha(() => target.Color, x => target.Color = x, endValue, duration);
            tweener.SetOptions(true).SetTarget(target);
            return tweener;
        }

        /// <summary>
        /// Controls the color of the outline
        /// </summary>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this Outlinable.OutlineProperties target, Color endValue, float duration)
        {
            var tweener = DOTween.To(() => target.Color, x => target.Color = x, endValue, duration);
            tweener.SetOptions(false).SetTarget(target);
            return tweener;
        }

        /// <summary>
        /// Controls the amount of blur applied to the outline
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DOBlurShift(this Outlinable.OutlineProperties target, float endValue, float duration, bool snapping = false)
        {
            var tweener = DOTween.To(() => target.BlurShift, x => target.BlurShift = x, endValue, duration);
            tweener.SetOptions(snapping).SetTarget(target);
            return tweener;
        }

        /// <summary>
        /// Controls the amount of blur applied to the outline
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DOBlurShift(this Outliner target, float endValue, float duration, bool snapping = false)
        {
            var tweener = DOTween.To(() => target.BlurShift, x => target.BlurShift = x, endValue, duration);
            tweener.SetOptions(snapping).SetTarget(target);
            return tweener;
        }

        /// <summary>
        /// Controls the amount of dilation applied to the outline
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DODilateShift(this Outlinable.OutlineProperties target, float endValue, float duration, bool snapping = false)
        {
            var tweener = DOTween.To(() => target.DilateShift, x => target.DilateShift = x, endValue, duration);
            tweener.SetOptions(snapping).SetTarget(target);
            return tweener;
        }

        /// <summary>
        /// Controls the amount of dilation applied to the outline
        /// </summary>
        public static TweenerCore<float, float, FloatOptions> DODilateShift(this Outliner target, float endValue, float duration, bool snapping = false)
        {
            var tweener = DOTween.To(() => target.DilateShift, x => target.DilateShift = x, endValue, duration);
            tweener.SetOptions(snapping).SetTarget(target);
            return tweener;
        }
    }
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModulePhysics.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

#if true // MODULE_MARKER
using System;
using DG.Tweening.Core;
using DG.Tweening.Core.Enums;
using DG.Tweening.Plugins;
using DG.Tweening.Plugins.Core.PathCore;
using DG.Tweening.Plugins.Options;
using UnityEngine;

#pragma warning disable 1591
namespace DG.Tweening
{
	public static class DOTweenModulePhysics
    {
        #region Shortcuts

        #region Rigidbody

        /// <summary>Tweens a Rigidbody's position to the given value.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOMove(this Rigidbody target, Vector3 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody's X position to the given value.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOMoveX(this Rigidbody target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, new Vector3(endValue, 0, 0), duration);
            t.SetOptions(AxisConstraint.X, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody's Y position to the given value.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOMoveY(this Rigidbody target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, new Vector3(0, endValue, 0), duration);
            t.SetOptions(AxisConstraint.Y, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody's Z position to the given value.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOMoveZ(this Rigidbody target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, new Vector3(0, 0, endValue), duration);
            t.SetOptions(AxisConstraint.Z, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody's rotation to the given value.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="mode">Rotation mode</param>
        public static TweenerCore<Quaternion, Vector3, QuaternionOptions> DORotate(this Rigidbody target, Vector3 endValue, float duration, RotateMode mode = RotateMode.Fast)
        {
            TweenerCore<Quaternion, Vector3, QuaternionOptions> t = DOTween.To(() => target.rotation, target.MoveRotation, endValue, duration);
            t.SetTarget(target);
            t.plugOptions.rotateMode = mode;
            return t;
        }

        /// <summary>Tweens a Rigidbody's rotation so that it will look towards the given position.
        /// Also stores the rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="towards">The position to look at</param><param name="duration">The duration of the tween</param>
        /// <param name="axisConstraint">Eventual axis constraint for the rotation</param>
        /// <param name="up">The vector that defines in which direction up is (default: Vector3.up)</param>
        public static TweenerCore<Quaternion, Vector3, QuaternionOptions> DOLookAt(this Rigidbody target, Vector3 towards, float duration, AxisConstraint axisConstraint = AxisConstraint.None, Vector3? up = null)
        {
            TweenerCore<Quaternion, Vector3, QuaternionOptions> t = DOTween.To(() => target.rotation, target.MoveRotation, towards, duration)
                .SetTarget(target).SetSpecialStartupMode(SpecialStartupMode.SetLookAt);
            t.plugOptions.axisConstraint = axisConstraint;
            t.plugOptions.up = (up == null) ? Vector3.up : (Vector3)up;
            return t;
        }

        #region Special

        /// <summary>Tweens a Rigidbody's position to the given value, while also applying a jump effect along the Y axis.
        /// Returns a Sequence instead of a Tweener.
        /// Also stores the Rigidbody as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="jumpPower">Power of the jump (the max height of the jump is represented by this plus the final Y offset)</param>
        /// <param name="numJumps">Total number of jumps</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Sequence DOJump(this Rigidbody target, Vector3 endValue, float jumpPower, int numJumps, float duration, bool snapping = false)
        {
            if (numJumps < 1) numJumps = 1;
            float startPosY = 0;
            float offsetY = -1;
            bool offsetYSet = false;
            Sequence s = DOTween.Sequence();
            Tween yTween = DOTween.To(() => target.position, target.MovePosition, new Vector3(0, jumpPower, 0), duration / (numJumps * 2))
                .SetOptions(AxisConstraint.Y, snapping).SetEase(Ease.OutQuad).SetRelative()
                .SetLoops(numJumps * 2, LoopType.Yoyo)
                .OnStart(() => startPosY = target.position.y);
            s.Append(DOTween.To(() => target.position, target.MovePosition, new Vector3(endValue.x, 0, 0), duration)
                    .SetOptions(AxisConstraint.X, snapping).SetEase(Ease.Linear)
                ).Join(DOTween.To(() => target.position, target.MovePosition, new Vector3(0, 0, endValue.z), duration)
                    .SetOptions(AxisConstraint.Z, snapping).SetEase(Ease.Linear)
                ).Join(yTween)
                .SetTarget(target).SetEase(DOTween.defaultEaseType);
            yTween.OnUpdate(() => {
                if (!offsetYSet) {
                    offsetYSet = true;
                    offsetY = s.isRelative ? endValue.y : endValue.y - startPosY;
                }
                Vector3 pos = target.position;
                pos.y += DOVirtual.EasedValue(0, offsetY, yTween.ElapsedPercentage(), Ease.OutQuad);
                target.MovePosition(pos);
            });
            return s;
        }

        /// <summary>Tweens a Rigidbody's position through the given path waypoints, using the chosen path algorithm.
        /// Also stores the Rigidbody as the tween's target so it can be used for filtered operations.
        /// <para>NOTE: to tween a rigidbody correctly it should be set to kinematic at least while being tweened.</para>
        /// <para>BEWARE: doesn't work on Windows Phone store (waiting for Unity to fix their own bug).
        /// If you plan to publish there you should use a regular transform.DOPath.</para></summary>
        /// <param name="path">The waypoints to go through</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="pathType">The type of path: Linear (straight path), CatmullRom (curved CatmullRom path) or CubicBezier (curved with control points)</param>
        /// <param name="pathMode">The path mode: 3D, side-scroller 2D, top-down 2D</param>
        /// <param name="resolution">The resolution of the path (useless in case of Linear paths): higher resolutions make for more detailed curved paths but are more expensive.
        /// Defaults to 10, but a value of 5 is usually enough if you don't have dramatic long curves between waypoints</param>
        /// <param name="gizmoColor">The color of the path (shown when gizmos are active in the Play panel and the tween is running)</param>
        public static TweenerCore<Vector3, Path, PathOptions> DOPath(
            this Rigidbody target, Vector3[] path, float duration, PathType pathType = PathType.Linear,
            PathMode pathMode = PathMode.Full3D, int resolution = 10, Color? gizmoColor = null
        )
        {
            if (resolution < 1) resolution = 1;
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => target.position, target.MovePosition, new Path(pathType, path, resolution, gizmoColor), duration)
                .SetTarget(target).SetUpdate(UpdateType.Fixed);

            t.plugOptions.isRigidbody = true;
            t.plugOptions.mode = pathMode;
            return t;
        }
        /// <summary>Tweens a Rigidbody's localPosition through the given path waypoints, using the chosen path algorithm.
        /// Also stores the Rigidbody as the tween's target so it can be used for filtered operations
        /// <para>NOTE: to tween a rigidbody correctly it should be set to kinematic at least while being tweened.</para>
        /// <para>BEWARE: doesn't work on Windows Phone store (waiting for Unity to fix their own bug).
        /// If you plan to publish there you should use a regular transform.DOLocalPath.</para></summary>
        /// <param name="path">The waypoint to go through</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="pathType">The type of path: Linear (straight path), CatmullRom (curved CatmullRom path) or CubicBezier (curved with control points)</param>
        /// <param name="pathMode">The path mode: 3D, side-scroller 2D, top-down 2D</param>
        /// <param name="resolution">The resolution of the path: higher resolutions make for more detailed curved paths but are more expensive.
        /// Defaults to 10, but a value of 5 is usually enough if you don't have dramatic long curves between waypoints</param>
        /// <param name="gizmoColor">The color of the path (shown when gizmos are active in the Play panel and the tween is running)</param>
        public static TweenerCore<Vector3, Path, PathOptions> DOLocalPath(
            this Rigidbody target, Vector3[] path, float duration, PathType pathType = PathType.Linear,
            PathMode pathMode = PathMode.Full3D, int resolution = 10, Color? gizmoColor = null
        )
        {
            if (resolution < 1) resolution = 1;
            Transform trans = target.transform;
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => trans.localPosition, x => target.MovePosition(trans.parent == null ? x : trans.parent.TransformPoint(x)), new Path(pathType, path, resolution, gizmoColor), duration)
                .SetTarget(target).SetUpdate(UpdateType.Fixed);

            t.plugOptions.isRigidbody = true;
            t.plugOptions.mode = pathMode;
            t.plugOptions.useLocalPosition = true;
            return t;
        }
        // Used by path editor when creating the actual tween, so it can pass a pre-compiled path
        internal static TweenerCore<Vector3, Path, PathOptions> DOPath(
            this Rigidbody target, Path path, float duration, PathMode pathMode = PathMode.Full3D
        )
        {
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => target.position, target.MovePosition, path, duration)
                .SetTarget(target);

            t.plugOptions.isRigidbody = true;
            t.plugOptions.mode = pathMode;
            return t;
        }
        internal static TweenerCore<Vector3, Path, PathOptions> DOLocalPath(
            this Rigidbody target, Path path, float duration, PathMode pathMode = PathMode.Full3D
        )
        {
            Transform trans = target.transform;
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => trans.localPosition, x => target.MovePosition(trans.parent == null ? x : trans.parent.TransformPoint(x)), path, duration)
                .SetTarget(target);

            t.plugOptions.isRigidbody = true;
            t.plugOptions.mode = pathMode;
            t.plugOptions.useLocalPosition = true;
            return t;
        }

        #endregion

        #endregion

        #endregion
	}
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModulePhysics2D.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

#if true // MODULE_MARKER
using System;
using DG.Tweening.Core;
using DG.Tweening.Plugins;
using DG.Tweening.Plugins.Core.PathCore;
using DG.Tweening.Plugins.Options;
using UnityEngine;

#pragma warning disable 1591
namespace DG.Tweening
{
	public static class DOTweenModulePhysics2D
    {
        #region Shortcuts

        #region Rigidbody2D Shortcuts

        /// <summary>Tweens a Rigidbody2D's position to the given value.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOMove(this Rigidbody2D target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody2D's X position to the given value.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOMoveX(this Rigidbody2D target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, new Vector2(endValue, 0), duration);
            t.SetOptions(AxisConstraint.X, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody2D's Y position to the given value.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOMoveY(this Rigidbody2D target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.position, target.MovePosition, new Vector2(0, endValue), duration);
            t.SetOptions(AxisConstraint.Y, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Rigidbody2D's rotation to the given value.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DORotate(this Rigidbody2D target, float endValue, float duration)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.rotation, target.MoveRotation, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #region Special

        /// <summary>Tweens a Rigidbody2D's position to the given value, while also applying a jump effect along the Y axis.
        /// Returns a Sequence instead of a Tweener.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations.
        /// <para>IMPORTANT: a rigidbody2D can't be animated in a jump arc using MovePosition, so the tween will directly set the position</para></summary>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="jumpPower">Power of the jump (the max height of the jump is represented by this plus the final Y offset)</param>
        /// <param name="numJumps">Total number of jumps</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Sequence DOJump(this Rigidbody2D target, Vector2 endValue, float jumpPower, int numJumps, float duration, bool snapping = false)
        {
            if (numJumps < 1) numJumps = 1;
            float startPosY = 0;
            float offsetY = -1;
            bool offsetYSet = false;
            Sequence s = DOTween.Sequence();
            Tween yTween = DOTween.To(() => target.position, x => target.position = x, new Vector2(0, jumpPower), duration / (numJumps * 2))
                .SetOptions(AxisConstraint.Y, snapping).SetEase(Ease.OutQuad).SetRelative()
                .SetLoops(numJumps * 2, LoopType.Yoyo)
                .OnStart(() => startPosY = target.position.y);
            s.Append(DOTween.To(() => target.position, x => target.position = x, new Vector2(endValue.x, 0), duration)
                    .SetOptions(AxisConstraint.X, snapping).SetEase(Ease.Linear)
                ).Join(yTween)
                .SetTarget(target).SetEase(DOTween.defaultEaseType);
            yTween.OnUpdate(() => {
                if (!offsetYSet) {
                    offsetYSet = true;
                    offsetY = s.isRelative ? endValue.y : endValue.y - startPosY;
                }
                Vector3 pos = target.position;
                pos.y += DOVirtual.EasedValue(0, offsetY, yTween.ElapsedPercentage(), Ease.OutQuad);
                target.MovePosition(pos);
            });
            return s;
        }

        /// <summary>Tweens a Rigidbody2D's position through the given path waypoints, using the chosen path algorithm.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations.
        /// <para>NOTE: to tween a Rigidbody2D correctly it should be set to kinematic at least while being tweened.</para>
        /// <para>BEWARE: doesn't work on Windows Phone store (waiting for Unity to fix their own bug).
        /// If you plan to publish there you should use a regular transform.DOPath.</para></summary>
        /// <param name="path">The waypoints to go through</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="pathType">The type of path: Linear (straight path), CatmullRom (curved CatmullRom path) or CubicBezier (curved with control points)</param>
        /// <param name="pathMode">The path mode: 3D, side-scroller 2D, top-down 2D</param>
        /// <param name="resolution">The resolution of the path (useless in case of Linear paths): higher resolutions make for more detailed curved paths but are more expensive.
        /// Defaults to 10, but a value of 5 is usually enough if you don't have dramatic long curves between waypoints</param>
        /// <param name="gizmoColor">The color of the path (shown when gizmos are active in the Play panel and the tween is running)</param>
        public static TweenerCore<Vector3, Path, PathOptions> DOPath(
            this Rigidbody2D target, Vector2[] path, float duration, PathType pathType = PathType.Linear,
            PathMode pathMode = PathMode.Full3D, int resolution = 10, Color? gizmoColor = null
        )
        {
            if (resolution < 1) resolution = 1;
            int len = path.Length;
            Vector3[] path3D = new Vector3[len];
            for (int i = 0; i < len; ++i) path3D[i] = path[i];
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => target.position, x => target.MovePosition(x), new Path(pathType, path3D, resolution, gizmoColor), duration)
                .SetTarget(target).SetUpdate(UpdateType.Fixed);

            t.plugOptions.isRigidbody2D = true;
            t.plugOptions.mode = pathMode;
            return t;
        }
        /// <summary>Tweens a Rigidbody2D's localPosition through the given path waypoints, using the chosen path algorithm.
        /// Also stores the Rigidbody2D as the tween's target so it can be used for filtered operations
        /// <para>NOTE: to tween a Rigidbody2D correctly it should be set to kinematic at least while being tweened.</para>
        /// <para>BEWARE: doesn't work on Windows Phone store (waiting for Unity to fix their own bug).
        /// If you plan to publish there you should use a regular transform.DOLocalPath.</para></summary>
        /// <param name="path">The waypoint to go through</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="pathType">The type of path: Linear (straight path), CatmullRom (curved CatmullRom path) or CubicBezier (curved with control points)</param>
        /// <param name="pathMode">The path mode: 3D, side-scroller 2D, top-down 2D</param>
        /// <param name="resolution">The resolution of the path: higher resolutions make for more detailed curved paths but are more expensive.
        /// Defaults to 10, but a value of 5 is usually enough if you don't have dramatic long curves between waypoints</param>
        /// <param name="gizmoColor">The color of the path (shown when gizmos are active in the Play panel and the tween is running)</param>
        public static TweenerCore<Vector3, Path, PathOptions> DOLocalPath(
            this Rigidbody2D target, Vector2[] path, float duration, PathType pathType = PathType.Linear,
            PathMode pathMode = PathMode.Full3D, int resolution = 10, Color? gizmoColor = null
        )
        {
            if (resolution < 1) resolution = 1;
            int len = path.Length;
            Vector3[] path3D = new Vector3[len];
            for (int i = 0; i < len; ++i) path3D[i] = path[i];
            Transform trans = target.transform;
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => trans.localPosition, x => target.MovePosition(trans.parent == null ? x : trans.parent.TransformPoint(x)), new Path(pathType, path3D, resolution, gizmoColor), duration)
                .SetTarget(target).SetUpdate(UpdateType.Fixed);

            t.plugOptions.isRigidbody2D = true;
            t.plugOptions.mode = pathMode;
            t.plugOptions.useLocalPosition = true;
            return t;
        }
        // Used by path editor when creating the actual tween, so it can pass a pre-compiled path
        internal static TweenerCore<Vector3, Path, PathOptions> DOPath(
            this Rigidbody2D target, Path path, float duration, PathMode pathMode = PathMode.Full3D
        )
        {
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => target.position, x => target.MovePosition(x), path, duration)
                .SetTarget(target);

            t.plugOptions.isRigidbody2D = true;
            t.plugOptions.mode = pathMode;
            return t;
        }
        internal static TweenerCore<Vector3, Path, PathOptions> DOLocalPath(
            this Rigidbody2D target, Path path, float duration, PathMode pathMode = PathMode.Full3D
        )
        {
            Transform trans = target.transform;
            TweenerCore<Vector3, Path, PathOptions> t = DOTween.To(PathPlugin.Get(), () => trans.localPosition, x => target.MovePosition(trans.parent == null ? x : trans.parent.TransformPoint(x)), path, duration)
                .SetTarget(target);

            t.plugOptions.isRigidbody2D = true;
            t.plugOptions.mode = pathMode;
            t.plugOptions.useLocalPosition = true;
            return t;
        }

        #endregion

        #endregion

        #endregion
	}
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleSprite.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

#if true // MODULE_MARKER
using System;
using UnityEngine;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;

#pragma warning disable 1591
namespace DG.Tweening
{
	public static class DOTweenModuleSprite
    {
        #region Shortcuts

        #region SpriteRenderer

        /// <summary>Tweens a SpriteRenderer's color to the given value.
        /// Also stores the spriteRenderer as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this SpriteRenderer target, Color endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.To(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Material's alpha color to the given value.
        /// Also stores the spriteRenderer as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this SpriteRenderer target, float endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.ToAlpha(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a SpriteRenderer's color using the given gradient
        /// (NOTE 1: only uses the colors of the gradient, not the alphas - NOTE 2: creates a Sequence, not a Tweener).
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="gradient">The gradient to use</param><param name="duration">The duration of the tween</param>
        public static Sequence DOGradientColor(this SpriteRenderer target, Gradient gradient, float duration)
        {
            Sequence s = DOTween.Sequence();
            GradientColorKey[] colors = gradient.colorKeys;
            int len = colors.Length;
            for (int i = 0; i < len; ++i) {
                GradientColorKey c = colors[i];
                if (i == 0 && c.time <= 0) {
                    target.color = c.color;
                    continue;
                }
                float colorDuration = i == len - 1
                    ? duration - s.Duration(false) // Verifies that total duration is correct
                    : duration * (i == 0 ? c.time : c.time - colors[i - 1].time);
                s.Append(target.DOColor(c.color, colorDuration).SetEase(Ease.Linear));
            }
            s.SetTarget(target);
            return s;
        }

        #endregion

        #region Blendables

        #region SpriteRenderer

        /// <summary>Tweens a SpriteRenderer's color to the given value,
        /// in a way that allows other DOBlendableColor tweens to work together on the same target,
        /// instead than fight each other as multiple DOColor would do.
        /// Also stores the SpriteRenderer as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The value to tween to</param><param name="duration">The duration of the tween</param>
        public static Tweener DOBlendableColor(this SpriteRenderer target, Color endValue, float duration)
        {
            endValue = endValue - target.color;
            Color to = new Color(0, 0, 0, 0);
            return DOTween.To(() => to, x => {
                    Color diff = x - to;
                    to = x;
                    target.color += diff;
                }, endValue, duration)
                .Blendable().SetTarget(target);
        }

        #endregion

        #endregion

        #endregion
	}
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleUI.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

#if true // MODULE_MARKER

using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening.Core;
using DG.Tweening.Core.Enums;
using DG.Tweening.Plugins;
using DG.Tweening.Plugins.Options;
using Outline = UnityEngine.UI.Outline;
using Text = UnityEngine.UI.Text;

#pragma warning disable 1591
namespace DG.Tweening
{
	public static class DOTweenModuleUI
    {
        #region Shortcuts

        #region CanvasGroup

        /// <summary>Tweens a CanvasGroup's alpha color to the given value.
        /// Also stores the canvasGroup as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DOFade(this CanvasGroup target, float endValue, float duration)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.alpha, x => target.alpha = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #endregion

        #region Graphic

        /// <summary>Tweens an Graphic's color to the given value.
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this Graphic target, Color endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.To(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens an Graphic's alpha color to the given value.
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this Graphic target, float endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.ToAlpha(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #endregion

        #region Image

        /// <summary>Tweens an Image's color to the given value.
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this Image target, Color endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.To(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens an Image's alpha color to the given value.
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this Image target, float endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.ToAlpha(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens an Image's fillAmount to the given value.
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach (0 to 1)</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<float, float, FloatOptions> DOFillAmount(this Image target, float endValue, float duration)
        {
            if (endValue > 1) endValue = 1;
            else if (endValue < 0) endValue = 0;
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.fillAmount, x => target.fillAmount = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens an Image's colors using the given gradient
        /// (NOTE 1: only uses the colors of the gradient, not the alphas - NOTE 2: creates a Sequence, not a Tweener).
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="gradient">The gradient to use</param><param name="duration">The duration of the tween</param>
        public static Sequence DOGradientColor(this Image target, Gradient gradient, float duration)
        {
            Sequence s = DOTween.Sequence();
            GradientColorKey[] colors = gradient.colorKeys;
            int len = colors.Length;
            for (int i = 0; i < len; ++i) {
                GradientColorKey c = colors[i];
                if (i == 0 && c.time <= 0) {
                    target.color = c.color;
                    continue;
                }
                float colorDuration = i == len - 1
                    ? duration - s.Duration(false) // Verifies that total duration is correct
                    : duration * (i == 0 ? c.time : c.time - colors[i - 1].time);
                s.Append(target.DOColor(c.color, colorDuration).SetEase(Ease.Linear));
            }
            s.SetTarget(target);
            return s;
        }

        #endregion

        #region LayoutElement

        /// <summary>Tweens an LayoutElement's flexibleWidth/Height to the given value.
        /// Also stores the LayoutElement as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOFlexibleSize(this LayoutElement target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => new Vector2(target.flexibleWidth, target.flexibleHeight), x => {
                    target.flexibleWidth = x.x;
                    target.flexibleHeight = x.y;
                }, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens an LayoutElement's minWidth/Height to the given value.
        /// Also stores the LayoutElement as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOMinSize(this LayoutElement target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => new Vector2(target.minWidth, target.minHeight), x => {
                target.minWidth = x.x;
                target.minHeight = x.y;
            }, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens an LayoutElement's preferredWidth/Height to the given value.
        /// Also stores the LayoutElement as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOPreferredSize(this LayoutElement target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => new Vector2(target.preferredWidth, target.preferredHeight), x => {
                target.preferredWidth = x.x;
                target.preferredHeight = x.y;
            }, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        #endregion

        #region Outline

        /// <summary>Tweens a Outline's effectColor to the given value.
        /// Also stores the Outline as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this Outline target, Color endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.To(() => target.effectColor, x => target.effectColor = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Outline's effectColor alpha to the given value.
        /// Also stores the Outline as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this Outline target, float endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.ToAlpha(() => target.effectColor, x => target.effectColor = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Outline's effectDistance to the given value.
        /// Also stores the Outline as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOScale(this Outline target, Vector2 endValue, float duration)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.effectDistance, x => target.effectDistance = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #endregion

        #region RectTransform

        /// <summary>Tweens a RectTransform's anchoredPosition to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorPos(this RectTransform target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.anchoredPosition, x => target.anchoredPosition = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's anchoredPosition X to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorPosX(this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.anchoredPosition, x => target.anchoredPosition = x, new Vector2(endValue, 0), duration);
            t.SetOptions(AxisConstraint.X, snapping).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's anchoredPosition Y to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorPosY(this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.anchoredPosition, x => target.anchoredPosition = x, new Vector2(0, endValue), duration);
            t.SetOptions(AxisConstraint.Y, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a RectTransform's anchoredPosition3D to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOAnchorPos3D(this RectTransform target, Vector3 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.anchoredPosition3D, x => target.anchoredPosition3D = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's anchoredPosition3D X to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOAnchorPos3DX(this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.anchoredPosition3D, x => target.anchoredPosition3D = x, new Vector3(endValue, 0, 0), duration);
            t.SetOptions(AxisConstraint.X, snapping).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's anchoredPosition3D Y to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOAnchorPos3DY(this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.anchoredPosition3D, x => target.anchoredPosition3D = x, new Vector3(0, endValue, 0), duration);
            t.SetOptions(AxisConstraint.Y, snapping).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's anchoredPosition3D Z to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOAnchorPos3DZ(this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(() => target.anchoredPosition3D, x => target.anchoredPosition3D = x, new Vector3(0, 0, endValue), duration);
            t.SetOptions(AxisConstraint.Z, snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a RectTransform's anchorMax to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorMax(this RectTransform target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.anchorMax, x => target.anchorMax = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a RectTransform's anchorMin to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorMin(this RectTransform target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.anchorMin, x => target.anchorMin = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a RectTransform's pivot to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOPivot(this RectTransform target, Vector2 endValue, float duration)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.pivot, x => target.pivot = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's pivot X to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOPivotX(this RectTransform target, float endValue, float duration)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.pivot, x => target.pivot = x, new Vector2(endValue, 0), duration);
            t.SetOptions(AxisConstraint.X).SetTarget(target);
            return t;
        }
        /// <summary>Tweens a RectTransform's pivot Y to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOPivotY(this RectTransform target, float endValue, float duration)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.pivot, x => target.pivot = x, new Vector2(0, endValue), duration);
            t.SetOptions(AxisConstraint.Y).SetTarget(target);
            return t;
        }

        /// <summary>Tweens a RectTransform's sizeDelta to the given value.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOSizeDelta(this RectTransform target, Vector2 endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.sizeDelta, x => target.sizeDelta = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        /// <summary>Punches a RectTransform's anchoredPosition towards the given direction and then back to the starting one
        /// as if it was connected to the starting position via an elastic.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="punch">The direction and strength of the punch (added to the RectTransform's current position)</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="vibrato">Indicates how much will the punch vibrate</param>
        /// <param name="elasticity">Represents how much (0 to 1) the vector will go beyond the starting position when bouncing backwards.
        /// 1 creates a full oscillation between the punch direction and the opposite direction,
        /// while 0 oscillates only between the punch and the start position</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Tweener DOPunchAnchorPos(this RectTransform target, Vector2 punch, float duration, int vibrato = 10, float elasticity = 1, bool snapping = false)
        {
            return DOTween.Punch(() => target.anchoredPosition, x => target.anchoredPosition = x, punch, duration, vibrato, elasticity)
                .SetTarget(target).SetOptions(snapping);
        }

        /// <summary>Shakes a RectTransform's anchoredPosition with the given values.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="strength">The shake strength</param>
        /// <param name="vibrato">Indicates how much will the shake vibrate</param>
        /// <param name="randomness">Indicates how much the shake will be random (0 to 180 - values higher than 90 kind of suck, so beware). 
        /// Setting it to 0 will shake along a single direction.</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        /// <param name="fadeOut">If TRUE the shake will automatically fadeOut smoothly within the tween's duration, otherwise it will not</param>
        /// <param name="randomnessMode">Randomness mode</param>
        public static Tweener DOShakeAnchorPos(this RectTransform target, float duration, float strength = 100, int vibrato = 10, float randomness = 90, bool snapping = false, bool fadeOut = true, ShakeRandomnessMode randomnessMode = ShakeRandomnessMode.Full)
        {
            return DOTween.Shake(() => target.anchoredPosition, x => target.anchoredPosition = x, duration, strength, vibrato, randomness, true, fadeOut, randomnessMode)
                .SetTarget(target).SetSpecialStartupMode(SpecialStartupMode.SetShake).SetOptions(snapping);
        }
        /// <summary>Shakes a RectTransform's anchoredPosition with the given values.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="strength">The shake strength on each axis</param>
        /// <param name="vibrato">Indicates how much will the shake vibrate</param>
        /// <param name="randomness">Indicates how much the shake will be random (0 to 180 - values higher than 90 kind of suck, so beware). 
        /// Setting it to 0 will shake along a single direction.</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        /// <param name="fadeOut">If TRUE the shake will automatically fadeOut smoothly within the tween's duration, otherwise it will not</param>
        /// <param name="randomnessMode">Randomness mode</param>
        public static Tweener DOShakeAnchorPos(this RectTransform target, float duration, Vector2 strength, int vibrato = 10, float randomness = 90, bool snapping = false, bool fadeOut = true, ShakeRandomnessMode randomnessMode = ShakeRandomnessMode.Full)
        {
            return DOTween.Shake(() => target.anchoredPosition, x => target.anchoredPosition = x, duration, strength, vibrato, randomness, fadeOut, randomnessMode)
                .SetTarget(target).SetSpecialStartupMode(SpecialStartupMode.SetShake).SetOptions(snapping);
        }

        #region Special

        /// <summary>Tweens a RectTransform's anchoredPosition to the given value, while also applying a jump effect along the Y axis.
        /// Returns a Sequence instead of a Tweener.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="jumpPower">Power of the jump (the max height of the jump is represented by this plus the final Y offset)</param>
        /// <param name="numJumps">Total number of jumps</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Sequence DOJumpAnchorPos(this RectTransform target, Vector2 endValue, float jumpPower, int numJumps, float duration, bool snapping = false)
        {
            if (numJumps < 1) numJumps = 1;
            float startPosY = 0;
            float offsetY = -1;
            bool offsetYSet = false;

            // Separate Y Tween so we can elaborate elapsedPercentage on that insted of on the Sequence
            // (in case users add a delay or other elements to the Sequence)
            Sequence s = DOTween.Sequence();
            Tween yTween = DOTween.To(() => target.anchoredPosition, x => target.anchoredPosition = x, new Vector2(0, jumpPower), duration / (numJumps * 2))
                .SetOptions(AxisConstraint.Y, snapping).SetEase(Ease.OutQuad).SetRelative()
                .SetLoops(numJumps * 2, LoopType.Yoyo)
                .OnStart(()=> startPosY = target.anchoredPosition.y);
            s.Append(DOTween.To(() => target.anchoredPosition, x => target.anchoredPosition = x, new Vector2(endValue.x, 0), duration)
                    .SetOptions(AxisConstraint.X, snapping).SetEase(Ease.Linear)
                ).Join(yTween)
                .SetTarget(target).SetEase(DOTween.defaultEaseType);
            s.OnUpdate(() => {
                if (!offsetYSet) {
                    offsetYSet = true;
                    offsetY = s.isRelative ? endValue.y : endValue.y - startPosY;
                }
                Vector2 pos = target.anchoredPosition;
                pos.y += DOVirtual.EasedValue(0, offsetY, s.ElapsedDirectionalPercentage(), Ease.OutQuad);
                target.anchoredPosition = pos;
            });
            return s;
        }

        #endregion

        #endregion

        #region ScrollRect

        /// <summary>Tweens a ScrollRect's horizontal/verticalNormalizedPosition to the given value.
        /// Also stores the ScrollRect as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Tweener DONormalizedPos(this ScrollRect target, Vector2 endValue, float duration, bool snapping = false)
        {
            return DOTween.To(() => new Vector2(target.horizontalNormalizedPosition, target.verticalNormalizedPosition),
                x => {
                    target.horizontalNormalizedPosition = x.x;
                    target.verticalNormalizedPosition = x.y;
                }, endValue, duration)
                .SetOptions(snapping).SetTarget(target);
        }
        /// <summary>Tweens a ScrollRect's horizontalNormalizedPosition to the given value.
        /// Also stores the ScrollRect as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Tweener DOHorizontalNormalizedPos(this ScrollRect target, float endValue, float duration, bool snapping = false)
        {
            return DOTween.To(() => target.horizontalNormalizedPosition, x => target.horizontalNormalizedPosition = x, endValue, duration)
                .SetOptions(snapping).SetTarget(target);
        }
        /// <summary>Tweens a ScrollRect's verticalNormalizedPosition to the given value.
        /// Also stores the ScrollRect as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static Tweener DOVerticalNormalizedPos(this ScrollRect target, float endValue, float duration, bool snapping = false)
        {
            return DOTween.To(() => target.verticalNormalizedPosition, x => target.verticalNormalizedPosition = x, endValue, duration)
                .SetOptions(snapping).SetTarget(target);
        }

        #endregion

        #region Slider

        /// <summary>Tweens a Slider's value to the given value.
        /// Also stores the Slider as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<float, float, FloatOptions> DOValue(this Slider target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(() => target.value, x => target.value = x, endValue, duration);
            t.SetOptions(snapping).SetTarget(target);
            return t;
        }

        #endregion

        #region Text

        /// <summary>Tweens a Text's color to the given value.
        /// Also stores the Text as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOColor(this Text target, Color endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.To(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>
        /// Tweens a Text's text from one integer to another, with options for thousands separators
        /// </summary>
        /// <param name="fromValue">The value to start from</param>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="addThousandsSeparator">If TRUE (default) also adds thousands separators</param>
        /// <param name="culture">The <see cref="CultureInfo"/> to use (InvariantCulture if NULL)</param>
        public static TweenerCore<int, int, NoOptions> DOCounter(
            this Text target, int fromValue, int endValue, float duration, bool addThousandsSeparator = true, CultureInfo culture = null
        ){
            int v = fromValue;
            CultureInfo cInfo = !addThousandsSeparator ? null : culture ?? CultureInfo.InvariantCulture;
            TweenerCore<int, int, NoOptions> t = DOTween.To(() => v, x => {
                v = x;
                target.text = addThousandsSeparator
                    ? v.ToString("N0", cInfo)
                    : v.ToString();
            }, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Text's alpha color to the given value.
        /// Also stores the Text as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param><param name="duration">The duration of the tween</param>
        public static TweenerCore<Color, Color, ColorOptions> DOFade(this Text target, float endValue, float duration)
        {
            TweenerCore<Color, Color, ColorOptions> t = DOTween.ToAlpha(() => target.color, x => target.color = x, endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Text's text to the given value.
        /// Also stores the Text as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end string to tween to</param><param name="duration">The duration of the tween</param>
        /// <param name="richTextEnabled">If TRUE (default), rich text will be interpreted correctly while animated,
        /// otherwise all tags will be considered as normal text</param>
        /// <param name="scrambleMode">The type of scramble mode to use, if any</param>
        /// <param name="scrambleChars">A string containing the characters to use for scrambling.
        /// Use as many characters as possible (minimum 10) because DOTween uses a fast scramble mode which gives better results with more characters.
        /// Leave it to NULL (default) to use default ones</param>
        public static TweenerCore<string, string, StringOptions> DOText(this Text target, string endValue, float duration, bool richTextEnabled = true, ScrambleMode scrambleMode = ScrambleMode.None, string scrambleChars = null)
        {
            if (endValue == null) {
                if (Debugger.logPriority > 0) Debugger.LogWarning("You can't pass a NULL string to DOText: an empty string will be used instead to avoid errors");
                endValue = "";
            }
            TweenerCore<string, string, StringOptions> t = DOTween.To(() => target.text, x => target.text = x, endValue, duration);
            t.SetOptions(richTextEnabled, scrambleMode, scrambleChars)
                .SetTarget(target);
            return t;
        }

        #endregion

        #region Blendables

        #region Graphic

        /// <summary>Tweens a Graphic's color to the given value,
        /// in a way that allows other DOBlendableColor tweens to work together on the same target,
        /// instead than fight each other as multiple DOColor would do.
        /// Also stores the Graphic as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The value to tween to</param><param name="duration">The duration of the tween</param>
        public static Tweener DOBlendableColor(this Graphic target, Color endValue, float duration)
        {
            endValue = endValue - target.color;
            Color to = new Color(0, 0, 0, 0);
            return DOTween.To(() => to, x => {
                Color diff = x - to;
                to = x;
                target.color += diff;
            }, endValue, duration)
                .Blendable().SetTarget(target);
        }

        #endregion

        #region Image

        /// <summary>Tweens a Image's color to the given value,
        /// in a way that allows other DOBlendableColor tweens to work together on the same target,
        /// instead than fight each other as multiple DOColor would do.
        /// Also stores the Image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The value to tween to</param><param name="duration">The duration of the tween</param>
        public static Tweener DOBlendableColor(this Image target, Color endValue, float duration)
        {
            endValue = endValue - target.color;
            Color to = new Color(0, 0, 0, 0);
            return DOTween.To(() => to, x => {
                Color diff = x - to;
                to = x;
                target.color += diff;
            }, endValue, duration)
                .Blendable().SetTarget(target);
        }

        #endregion

        #region Text

        /// <summary>Tweens a Text's color BY the given value,
        /// in a way that allows other DOBlendableColor tweens to work together on the same target,
        /// instead than fight each other as multiple DOColor would do.
        /// Also stores the Text as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The value to tween to</param><param name="duration">The duration of the tween</param>
        public static Tweener DOBlendableColor(this Text target, Color endValue, float duration)
        {
            endValue = endValue - target.color;
            Color to = new Color(0, 0, 0, 0);
            return DOTween.To(() => to, x => {
                Color diff = x - to;
                to = x;
                target.color += diff;
            }, endValue, duration)
                .Blendable().SetTarget(target);
        }

        #endregion

        #endregion

        #region Shapes

        /// <summary>Tweens a RectTransform's anchoredPosition so that it draws a circle around the given center.
        /// Also stores the RectTransform as the tween's target so it can be used for filtered operations.<para/>
        /// IMPORTANT: SetFrom(value) requires a <see cref="Vector2"/> instead of a float, where the X property represents the "from degrees value"</summary>
        /// <param name="center">Circle-center/pivot around which to rotate (in UI anchoredPosition coordinates)</param>
        /// <param name="endValueDegrees">The end value degrees to reach (to rotate counter-clockwise pass a negative value)</param>
        /// <param name="duration">The duration of the tween</param>
        /// <param name="relativeCenter">If TRUE the <see cref="center"/> coordinates will be considered as relative to the target's current anchoredPosition</param>
        /// <param name="snapping">If TRUE the tween will smoothly snap all values to integers</param>
        public static TweenerCore<Vector2, Vector2, CircleOptions> DOShapeCircle(
            this RectTransform target, Vector2 center, float endValueDegrees, float duration, bool relativeCenter = false, bool snapping = false
        )
        {
            TweenerCore<Vector2, Vector2, CircleOptions> t = DOTween.To(
                CirclePlugin.Get(), () => target.anchoredPosition, x => target.anchoredPosition = x, center, duration
            );
            t.SetOptions(endValueDegrees, relativeCenter, snapping).SetTarget(target);
            return t;
        }

        #endregion

        #endregion

        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████
        // ███ INTERNAL CLASSES ████████████████████████████████████████████████████████████████████████████████████████████████
        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████

        public static class Utils
        {
            /// <summary>
            /// Converts the anchoredPosition of the first RectTransform to the second RectTransform,
            /// taking into consideration offset, anchors and pivot, and returns the new anchoredPosition
            /// </summary>
            public static Vector2 SwitchToRectTransform(RectTransform from, RectTransform to)
            {
                Vector2 localPoint;
                Vector2 fromPivotDerivedOffset = new Vector2(from.rect.width * 0.5f + from.rect.xMin, from.rect.height * 0.5f + from.rect.yMin);
                Vector2 screenP = RectTransformUtility.WorldToScreenPoint(null, from.position);
                screenP += fromPivotDerivedOffset;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(to, screenP, null, out localPoint);
                Vector2 pivotDerivedOffset = new Vector2(to.rect.width * 0.5f + to.rect.xMin, to.rect.height * 0.5f + to.rect.yMin);
                return to.anchoredPosition + localPoint - pivotDerivedOffset;
            }
        }
	}
}
#endif

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleUnityVersion.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

using System;
using UnityEngine;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
//#if UNITY_2018_1_OR_NEWER && (NET_4_6 || NET_STANDARD_2_0)
//using Task = System.Threading.Tasks.Task;
//#endif

#pragma warning disable 1591
namespace DG.Tweening
{
    /// <summary>
    /// Shortcuts/functions that are not strictly related to specific Modules
    /// but are available only on some Unity versions
    /// </summary>
	public static class DOTweenModuleUnityVersion
    {
        #region Material

        /// <summary>Tweens a Material's color using the given gradient
        /// (NOTE 1: only uses the colors of the gradient, not the alphas - NOTE 2: creates a Sequence, not a Tweener).
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="gradient">The gradient to use</param><param name="duration">The duration of the tween</param>
        public static Sequence DOGradientColor(this Material target, Gradient gradient, float duration)
        {
            Sequence s = DOTween.Sequence();
            GradientColorKey[] colors = gradient.colorKeys;
            int len = colors.Length;
            for (int i = 0; i < len; ++i) {
                GradientColorKey c = colors[i];
                if (i == 0 && c.time <= 0) {
                    target.color = c.color;
                    continue;
                }
                float colorDuration = i == len - 1
                    ? duration - s.Duration(false) // Verifies that total duration is correct
                    : duration * (i == 0 ? c.time : c.time - colors[i - 1].time);
                s.Append(target.DOColor(c.color, colorDuration).SetEase(Ease.Linear));
            }
            s.SetTarget(target);
            return s;
        }
        /// <summary>Tweens a Material's named color property using the given gradient
        /// (NOTE 1: only uses the colors of the gradient, not the alphas - NOTE 2: creates a Sequence, not a Tweener).
        /// Also stores the image as the tween's target so it can be used for filtered operations</summary>
        /// <param name="gradient">The gradient to use</param>
        /// <param name="property">The name of the material property to tween (like _Tint or _SpecColor)</param>
        /// <param name="duration">The duration of the tween</param>
        public static Sequence DOGradientColor(this Material target, Gradient gradient, string property, float duration)
        {
            Sequence s = DOTween.Sequence();
            GradientColorKey[] colors = gradient.colorKeys;
            int len = colors.Length;
            for (int i = 0; i < len; ++i) {
                GradientColorKey c = colors[i];
                if (i == 0 && c.time <= 0) {
                    target.SetColor(property, c.color);
                    continue;
                }
                float colorDuration = i == len - 1
                    ? duration - s.Duration(false) // Verifies that total duration is correct
                    : duration * (i == 0 ? c.time : c.time - colors[i - 1].time);
                s.Append(target.DOColor(c.color, property, colorDuration).SetEase(Ease.Linear));
            }
            s.SetTarget(target);
            return s;
        }

        #endregion

        #region CustomYieldInstructions

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed or complete.
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForCompletion(true);</code>
        /// </summary>
        public static CustomYieldInstruction WaitForCompletion(this Tween t, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForCompletion(t);
        }

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed or rewinded.
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForRewind();</code>
        /// </summary>
        public static CustomYieldInstruction WaitForRewind(this Tween t, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForRewind(t);
        }

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed.
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForKill();</code>
        /// </summary>
        public static CustomYieldInstruction WaitForKill(this Tween t, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForKill(t);
        }

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed or has gone through the given amount of loops.
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForElapsedLoops(2);</code>
        /// </summary>
        /// <param name="elapsedLoops">Elapsed loops to wait for</param>
        public static CustomYieldInstruction WaitForElapsedLoops(this Tween t, int elapsedLoops, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForElapsedLoops(t, elapsedLoops);
        }

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed
        /// or has reached the given time position (loops included, delays excluded).
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForPosition(2.5f);</code>
        /// </summary>
        /// <param name="position">Position (loops included, delays excluded) to wait for</param>
        public static CustomYieldInstruction WaitForPosition(this Tween t, float position, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForPosition(t, position);
        }

        /// <summary>
        /// Returns a <see cref="CustomYieldInstruction"/> that waits until the tween is killed or started
        /// (meaning when the tween is set in a playing state the first time, after any eventual delay).
        /// It can be used inside a coroutine as a yield.
        /// <para>Example usage:</para><code>yield return myTween.WaitForStart();</code>
        /// </summary>
        public static CustomYieldInstruction WaitForStart(this Tween t, bool returnCustomYieldInstruction)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return null;
            }
            return new DOTweenCYInstruction.WaitForStart(t);
        }

        #endregion

#if UNITY_2018_1_OR_NEWER
        #region Unity 2018.1 or Newer

        #region Material

        /// <summary>Tweens a Material's named texture offset property with the given ID to the given value.
        /// Also stores the material as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="propertyID">The ID of the material property to tween (also called nameID in Unity's manual)</param>
        /// <param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOOffset(this Material target, Vector2 endValue, int propertyID, float duration)
        {
            if (!target.HasProperty(propertyID)) {
                if (Debugger.logPriority > 0) Debugger.LogMissingMaterialProperty(propertyID);
                return null;
            }
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.GetTextureOffset(propertyID), x => target.SetTextureOffset(propertyID, x), endValue, duration);
            t.SetTarget(target);
            return t;
        }

        /// <summary>Tweens a Material's named texture scale property with the given ID to the given value.
        /// Also stores the material as the tween's target so it can be used for filtered operations</summary>
        /// <param name="endValue">The end value to reach</param>
        /// <param name="propertyID">The ID of the material property to tween (also called nameID in Unity's manual)</param>
        /// <param name="duration">The duration of the tween</param>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOTiling(this Material target, Vector2 endValue, int propertyID, float duration)
        {
            if (!target.HasProperty(propertyID)) {
                if (Debugger.logPriority > 0) Debugger.LogMissingMaterialProperty(propertyID);
                return null;
            }
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(() => target.GetTextureScale(propertyID), x => target.SetTextureScale(propertyID, x), endValue, duration);
            t.SetTarget(target);
            return t;
        }

        #endregion

        #region .NET 4.6 or Newer

#if UNITY_2018_1_OR_NEWER && (NET_4_6 || NET_STANDARD_2_0)

        #region Async Instructions

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed or complete.
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.WaitForCompletion();</code>
        /// </summary>
        public static async System.Threading.Tasks.Task AsyncWaitForCompletion(this Tween t)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active && !t.IsComplete()) await System.Threading.Tasks.Task.Yield();
        }

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed or rewinded.
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.AsyncWaitForRewind();</code>
        /// </summary>
        public static async System.Threading.Tasks.Task AsyncWaitForRewind(this Tween t)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active && (!t.playedOnce || t.position * (t.CompletedLoops() + 1) > 0)) await System.Threading.Tasks.Task.Yield();
        }

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed.
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.AsyncWaitForKill();</code>
        /// </summary>
        public static async System.Threading.Tasks.Task AsyncWaitForKill(this Tween t)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active) await System.Threading.Tasks.Task.Yield();
        }

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed or has gone through the given amount of loops.
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.AsyncWaitForElapsedLoops();</code>
        /// </summary>
        /// <param name="elapsedLoops">Elapsed loops to wait for</param>
        public static async System.Threading.Tasks.Task AsyncWaitForElapsedLoops(this Tween t, int elapsedLoops)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active && t.CompletedLoops() < elapsedLoops) await System.Threading.Tasks.Task.Yield();
        }

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed or started
        /// (meaning when the tween is set in a playing state the first time, after any eventual delay).
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.AsyncWaitForPosition();</code>
        /// </summary>
        /// <param name="position">Position (loops included, delays excluded) to wait for</param>
        public static async System.Threading.Tasks.Task AsyncWaitForPosition(this Tween t, float position)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active && t.position * (t.CompletedLoops() + 1) < position) await System.Threading.Tasks.Task.Yield();
        }

        /// <summary>
        /// Returns an async <see cref="System.Threading.Tasks.Task"/> that waits until the tween is killed.
        /// It can be used inside an async operation.
        /// <para>Example usage:</para><code>await myTween.AsyncWaitForKill();</code>
        /// </summary>
        public static async System.Threading.Tasks.Task AsyncWaitForStart(this Tween t)
        {
            if (!t.active) {
                if (Debugger.logPriority > 0) Debugger.LogInvalidTween(t);
                return;
            }
            while (t.active && !t.playedOnce) await System.Threading.Tasks.Task.Yield();
        }

        #endregion
#endif

        #endregion

        #endregion
#endif
    }

    // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████
    // ███ CLASSES █████████████████████████████████████████████████████████████████████████████████████████████████████████
    // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████

    public static class DOTweenCYInstruction
    {
        public class WaitForCompletion : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active && !t.IsComplete();
            }}
            readonly Tween t;
            public WaitForCompletion(Tween tween)
            {
                t = tween;
            }
        }

        public class WaitForRewind : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active && (!t.playedOnce || t.position * (t.CompletedLoops() + 1) > 0);
            }}
            readonly Tween t;
            public WaitForRewind(Tween tween)
            {
                t = tween;
            }
        }

        public class WaitForKill : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active;
            }}
            readonly Tween t;
            public WaitForKill(Tween tween)
            {
                t = tween;
            }
        }

        public class WaitForElapsedLoops : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active && t.CompletedLoops() < elapsedLoops;
            }}
            readonly Tween t;
            readonly int elapsedLoops;
            public WaitForElapsedLoops(Tween tween, int elapsedLoops)
            {
                t = tween;
                this.elapsedLoops = elapsedLoops;
            }
        }

        public class WaitForPosition : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active && t.position * (t.CompletedLoops() + 1) < position;
            }}
            readonly Tween t;
            readonly float position;
            public WaitForPosition(Tween tween, float position)
            {
                t = tween;
                this.position = position;
            }
        }

        public class WaitForStart : CustomYieldInstruction
        {
            public override bool keepWaiting { get {
                return t.active && !t.playedOnce;
            }}
            readonly Tween t;
            public WaitForStart(Tween tween)
            {
                t = tween;
            }
        }
    }
}

```

## Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleUtils.cs

```csharp
// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2018/07/13

using System;
using System.Reflection;
using UnityEngine;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Core.PathCore;
using DG.Tweening.Plugins.Options;

#pragma warning disable 1591
namespace DG.Tweening
{
    /// <summary>
    /// Utility functions that deal with available Modules.
    /// Modules defines:
    /// - DOTAUDIO
    /// - DOTPHYSICS
    /// - DOTPHYSICS2D
    /// - DOTSPRITE
    /// - DOTUI
    /// Extra defines set and used for implementation of external assets:
    /// - DOTWEEN_TMP ► TextMesh Pro
    /// - DOTWEEN_TK2D ► 2D Toolkit
    /// </summary>
	public static class DOTweenModuleUtils
    {
        static bool _initialized;

        #region Reflection

        /// <summary>
        /// Called via Reflection by DOTweenComponent on Awake
        /// </summary>
#if UNITY_2018_1_OR_NEWER
        [UnityEngine.Scripting.Preserve]
#endif
        public static void Init()
        {
            if (_initialized) return;

            _initialized = true;
            DOTweenExternalCommand.SetOrientationOnPath += Physics.SetOrientationOnPath;

#if UNITY_EDITOR
#if UNITY_4_3 || UNITY_4_4 || UNITY_4_5 || UNITY_4_6 || UNITY_5 || UNITY_2017_1
            UnityEditor.EditorApplication.playmodeStateChanged += PlaymodeStateChanged;
#else
            UnityEditor.EditorApplication.playModeStateChanged += PlaymodeStateChanged;
#endif
#endif
        }

#if UNITY_2018_1_OR_NEWER
#pragma warning disable
        [UnityEngine.Scripting.Preserve]
        // Just used to preserve methods when building, never called
        static void Preserver()
        {
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            MethodInfo mi = typeof(MonoBehaviour).GetMethod("Stub");
        }
#pragma warning restore
#endif

        #endregion

#if UNITY_EDITOR
        // Fires OnApplicationPause in DOTweenComponent even when Editor is paused (otherwise it's only fired at runtime)
#if UNITY_4_3 || UNITY_4_4 || UNITY_4_5 || UNITY_4_6 || UNITY_5 || UNITY_2017_1
        static void PlaymodeStateChanged()
        #else
        static void PlaymodeStateChanged(UnityEditor.PlayModeStateChange state)
#endif
        {
            if (DOTween.instance == null) return;
            DOTween.instance.OnApplicationPause(UnityEditor.EditorApplication.isPaused);
        }
#endif

        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████
        // ███ INTERNAL CLASSES ████████████████████████████████████████████████████████████████████████████████████████████████
        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████

        public static class Physics
        {
            // Called via DOTweenExternalCommand callback
            public static void SetOrientationOnPath(PathOptions options, Tween t, Quaternion newRot, Transform trans)
            {
#if true // PHYSICS_MARKER
                if (options.isRigidbody) ((Rigidbody)t.target).rotation = newRot;
                else trans.rotation = newRot;
#else
                trans.rotation = newRot;
#endif
            }

            // Returns FALSE if the DOTween's Physics2D Module is disabled, or if there's no Rigidbody2D attached
            public static bool HasRigidbody2D(Component target)
            {
#if true // PHYSICS2D_MARKER
                return target.GetComponent<Rigidbody2D>() != null;
#else
                return false;
#endif
            }

            #region Called via Reflection


            // Called via Reflection by DOTweenPathInspector
            // Returns FALSE if the DOTween's Physics Module is disabled, or if there's no rigidbody attached
#if UNITY_2018_1_OR_NEWER
            [UnityEngine.Scripting.Preserve]
#endif
            public static bool HasRigidbody(Component target)
            {
#if true // PHYSICS_MARKER
                return target.GetComponent<Rigidbody>() != null;
#else
                return false;
#endif
            }

            // Called via Reflection by DOTweenPath
#if UNITY_2018_1_OR_NEWER
            [UnityEngine.Scripting.Preserve]
#endif
            public static TweenerCore<Vector3, Path, PathOptions> CreateDOTweenPathTween(
                MonoBehaviour target, bool tweenRigidbody, bool isLocal, Path path, float duration, PathMode pathMode
            ){
                TweenerCore<Vector3, Path, PathOptions> t = null;
                bool rBodyFoundAndTweened = false;
#if true // PHYSICS_MARKER
                if (tweenRigidbody) {
                    Rigidbody rBody = target.GetComponent<Rigidbody>();
                    if (rBody != null) {
                        rBodyFoundAndTweened = true;
                        t = isLocal
                            ? rBody.DOLocalPath(path, duration, pathMode)
                            : rBody.DOPath(path, duration, pathMode);
                    }
                }
#endif
#if true // PHYSICS2D_MARKER
                if (!rBodyFoundAndTweened && tweenRigidbody) {
                    Rigidbody2D rBody2D = target.GetComponent<Rigidbody2D>();
                    if (rBody2D != null) {
                        rBodyFoundAndTweened = true;
                        t = isLocal
                            ? rBody2D.DOLocalPath(path, duration, pathMode)
                            : rBody2D.DOPath(path, duration, pathMode);
                    }
                }
#endif
                if (!rBodyFoundAndTweened) {
                    t = isLocal
                        ? target.transform.DOLocalPath(path, duration, pathMode)
                        : target.transform.DOPath(path, duration, pathMode);
                }
                return t;
            }

            #endregion
        }
    }
}

```

## Assets/PowerupPickupTween.cs

```csharp
using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PowerupPickupTween : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Visual root to animate. Defaults to self if null.")]
    public Transform model;

    [Header("Update")]
    [Tooltip("DOTween update type.")]
    public UpdateType updateType = UpdateType.Normal;
    [Tooltip("Ignore Time.timeScale (use unscaled time).")]
    public bool independentUpdate = true;

    [Header("Spawn")]
    public float spawnScaleFrom = 0.0f;
    public float spawnScaleTo = 1.0f;
    public float spawnDuration = 0.35f;
    public Ease spawnEase = Ease.OutBack;

    [Header("Idle Hover")]
    public float hoverAmplitude = 0.15f;
    public float hoverHalfCycle = 0.6f;
    public Ease hoverEase = Ease.InOutSine;

    [Header("Rotate")]
    public float rotateSpeedDegPerSec = 90f;

    [Header("Collect Punch")]
    public float collectPunchScale = 0.25f;
    public float collectDuration = 0.18f;
    public Ease collectEase = Ease.OutBack;

    private Vector3 _baseLocalPos;
    private Tweener _hoverTw;
    private Tweener _rotateTw;
    private Tweener _spawnTw;

    void Awake()
    {
        if (!model) model = transform;
        _baseLocalPos = model.localPosition;
    }

    void OnDisable()
    {
        KillAllTweens();
        if (model)
        {
            model.localPosition = _baseLocalPos;
            model.localScale = Vector3.one;
        }
    }

    public void PlaySpawn()
    {
        if (!model) return;
        KillAllTweens();

        _baseLocalPos = model.localPosition;
        model.localScale = Vector3.one * Mathf.Max(0f, spawnScaleFrom);

        _spawnTw = model
            .DOScale(spawnScaleTo, Mathf.Max(0.01f, spawnDuration))
            .SetEase(spawnEase)
            .SetUpdate(updateType, independentUpdate)
            .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
            .OnComplete(StartIdle);

        float lift = Mathf.Abs(hoverAmplitude) * 0.5f;
        model.DOLocalMoveY(_baseLocalPos.y + lift, spawnDuration * 0.5f)
             .SetLoops(2, LoopType.Yoyo)
             .SetEase(Ease.OutSine)
             .SetUpdate(updateType, independentUpdate)
             .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
    }

    public void PlayCollect()
    {
        if (!model) return;

        model.DOPunchScale(Vector3.one * collectPunchScale,
                           Mathf.Max(0.05f, collectDuration), vibrato: 1, elasticity: 0.5f)
             .SetEase(collectEase)
             .SetUpdate(updateType, independentUpdate)
             .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
    }

    public float GetCollectDuration() => Mathf.Max(0.05f, collectDuration);

    private void StartIdle()
    {
        if (!model) return;

        if (Mathf.Abs(hoverAmplitude) > 0.0001f)
        {
            _hoverTw = model.DOLocalMoveY(_baseLocalPos.y + hoverAmplitude,
                                          Mathf.Max(0.05f, hoverHalfCycle))
                           .SetEase(hoverEase)
                           .SetLoops(-1, LoopType.Yoyo)
                           .SetUpdate(updateType, independentUpdate)
                           .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        if (Mathf.Abs(rotateSpeedDegPerSec) > 0.01f)
        {
            float oneTurnTime = 360f / Mathf.Abs(rotateSpeedDegPerSec);
            _rotateTw = model.DOLocalRotate(
                            new Vector3(0f, Mathf.Sign(rotateSpeedDegPerSec) * 360f, 0f),
                            oneTurnTime, RotateMode.LocalAxisAdd)
                        .SetEase(Ease.Linear)
                        .SetLoops(-1, LoopType.Incremental)
                        .SetUpdate(updateType, independentUpdate)
                        .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }
    }

    private void KillAllTweens()
    {
        _spawnTw?.Kill();
        _hoverTw?.Kill();
        _rotateTw?.Kill();
        _spawnTw = _hoverTw = _rotateTw = null;
    }
}
```

## Assets/Scripts/Assassin.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Assassin : BaseCharacter
{

    protected float hpLvlIncrease = 5.78f;
    protected float minAtkLvlIncrease = 2.4f;
    protected float maxAtkLvlIncrease = 2.7f;

    protected float endLVLinc = 2.04f;
    protected float strLVLinc = 3.44f;
    protected float agiLVLinc = 4.88f;
    protected float witLVLinc = 1.54f;
    protected float chaLVLinc = 3.67f;

    public Assassin(string name, int level)
        : base(name, "Assassin", level, 463.6f, 100f, 46.2f, 53.8f, 105f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 10;
        traits[TraitType.Wit] = 4;
        traits[TraitType.Charm] = 7;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public Assassin()
        : base("", "Assassin", 5, 463.6f, 100f, 46.2f, 53.8f, 105f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 10;
        traits[TraitType.Wit] = 4;
        traits[TraitType.Charm] = 7;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);

    }

}

```

## Assets/Scripts/Ball.cs

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Ball : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] private float baseDamage = 5f;

    private int flatBonusDamage;
    private float damageMultiplier = 1f;          // permanent aggregated factor (1 = no change)
    private float tempDamageMultiplier = 1f;      // active temporary factor (1 = no change)
    private float tempDamageMultiplierStore = 1f; // queued temp factor to apply
    private int bonusBouncesNeeded;
    private int bonusBouncesRemaining;
    private int tmpBonusBouncesNeeded;
    private int tmpBonusBouncesRemaining;
    private float tempDamageBounceMultiplier = 1f;
    private int tempDamageBouncesRemaining;
    private float forceFieldGravity;

    // === Speed Uncap (BluePad interaction) ===
    [Header("Runtime Speed Uncap")]
    [SerializeField, Tooltip("DEBUG: show current uncapped state in inspector")]
    private bool _maxSpeedUncapped;                       // NEW
    private float _maxSpeedUncapUntil;                    // NEW
    private float _originalMaxSpeedBeforeUncap;           // NEW
    private float _temporaryMaxSpeedTarget;               // NEW
    private bool _restoreOriginalMaxOnExpire;             // NEW

    // Apply temporary effects while uncapped
    private bool _uncapModsApplied;
    private float _savedBounciness = -1f;
    private float _uncapDamageBonus = 1f;

    // Add near existing Glow fields
    [Header("Glow Light Smoothing")]
    [SerializeField, Tooltip("Units/sec to brighten when speeding up. Higher = snappier rise.")]
    private float glowIntensityRiseRate = 8f;
    [SerializeField, Range(0.03f, 0.6f), Tooltip("Smooth time (s) to dim when slowing down. Lower = faster.")]
    private float glowIntensityFallSmoothTime = 0.18f;

    private float _glowIntensity;      // smoothed current intensity
    private float _glowIntensityVel;   // velocity for SmoothDamp

        // ================= Kill‑Bumper Velocity Control =================
    [Header("Kill Bumper Velocity Control")]
    [SerializeField, Tooltip("Apply a soft clamp to post‑kill bumper impulses so the ball does not go flying.")]
    private bool limitKillBumperImpulse = true;
    [SerializeField, Tooltip("Target cap for speed right after killing a bumper (only if exceeded).")]
    private float killBumperSpeedCap = 55f;
    [SerializeField, Range(0f, 1f), Tooltip("How strongly to damp the excess above the cap (0 = none, 1 = snap directly to cap).")]    
    private float killBumperDampStrength = 0.65f;
    [SerializeField, Tooltip("Clamp absolute vertical (Y) component after kill to prevent huge upward/downward launches.")]
    private float killBumperVerticalClamp = 25f;
    [SerializeField, Tooltip("Ignore soft clamp if the added impulse was small relative to current speed (ratio threshold).")]
    private float killBumperImpulseRatioThreshold = 0.35f;

    [Header("Global Velocity Clamp")]
    [SerializeField, Tooltip("Clamp to the finite uncap value (>0) while the uncap window is active. Fully uncapped (<=0) remains uncapped.")]
    private bool clampDuringUncapIfFinite = true;

    [SerializeField, Tooltip("Optional absolute safety ceiling regardless of state. 0 = disabled.")]
    private float hardSpeedCap = 0f;

    /// <summary>
    /// Temporarily disable clamping OR raise maxSpeed for 'duration' seconds.
    /// If newMaxSpeed <= 0: remove clamp entirely. Otherwise raise to newMaxSpeed.
    /// Also applies temporary bounciness (+80%) and damage (+20%) bonuses during the window.
    /// </summary>
    public void TemporarilyUncapMaxSpeed(float duration, float newMaxSpeed, bool restoreOriginal = true)
    {
        duration = Mathf.Max(0.05f, duration);

        // Capture the original cap only the first time we enter the window.
        if (!_maxSpeedUncapped)
        {
            _originalMaxSpeedBeforeUncap = maxSpeed;
            _restoreOriginalMaxOnExpire = restoreOriginal;
        }
        else
        {
            // If any subsequent call asks to restore, keep that intent.
            _restoreOriginalMaxOnExpire |= restoreOriginal;
        }

        _maxSpeedUncapped = true;

        // Extend the window if re-applied while active (don't shorten an existing longer window).
        _maxSpeedUncapUntil = Mathf.Max(_maxSpeedUncapUntil, Time.time + duration);

        // Prefer the highest temporary cap while active.
        // newMaxSpeed <= 0 means "fully uncapped": keep it as 0 so FixedUpdate doesn't set maxSpeed.
        if (newMaxSpeed <= 0f)
        {
            _temporaryMaxSpeedTarget = 0f;
        }
        else
        {
            _temporaryMaxSpeedTarget = _temporaryMaxSpeedTarget <= 0f
                ? newMaxSpeed
                : Mathf.Max(_temporaryMaxSpeedTarget, newMaxSpeed);
        }

        // Apply temporary physics/damage bonuses immediately
        if (!_uncapModsApplied)
            ApplyUncapMods();

        // Optionally bump current velocity so player feels immediate boost when fully uncapped.
        var rbLocal = rb;
        if (rbLocal && newMaxSpeed > 0 && rbLocal.velocity.magnitude < newMaxSpeed * 0.5f)
        {
            rbLocal.velocity = rbLocal.velocity.normalized * Mathf.Min(newMaxSpeed * 0.65f, newMaxSpeed);
        }
    }

    // === Glow / Combo UI state ===
    [Header("Glow")]
    [SerializeField] private Color glowColor = Color.red; // UI and material "glow" color
    [SerializeField, Range(0f, 5f)] private float emissionBase = 1.5f; // base emission when no combo
    [SerializeField, Range(0f, 5f)] private float emissionPerComboStep = 0.30f; // extra intensity per combo step

    // NEW: optional light attached to the ball prefab
    [SerializeField, Tooltip("Optional child Light used to match glow color and modulate intensity with speed.")]
    private Light glowLight;

    private const float LightIntensityMin = .6f;
    private const float LightIntensityMax = 3.5f;

    private Renderer _renderer;
    private Material _runtimeMat; // unique instance so enabling emission doesn't affect shared material
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    public static event System.Action<Ball> OnBallActivated;
    public static event System.Action<Ball> OnBallDeactivated;
    public event System.Action<Ball> OnComboChanged;

    public Color GlowColor
    {
        get => glowColor;
        set
        {
            glowColor = value;
            ApplyGlow();
            OnComboChanged?.Invoke(this);
        }
    }

    public bool IsComboActive => comboActive;
    public float CurrentComboMultiplierUI => CurrentComboMultiplier;
    public float EmissionIntensityUI => ComputeEmissionIntensity();

    private int _nextPortalHitMult = 1;

    public void ActivatePortalBoost(int mult = 2)
    {
        _nextPortalHitMult = Mathf.Max(_nextPortalHitMult, Mathf.Max(1, mult));
    }
    public int ConsumePortalBoost()
    {
        int m = _nextPortalHitMult;
        _nextPortalHitMult = 1;
        return Mathf.Max(1, m);
    }

    [SerializeField] private int comboThreshold = 3;
    [SerializeField, Range(0.05f, 1f)] private float comboBonusPerHit = 0.10f;
    [SerializeField] private LayerMask comboBreakLayers;

    private int comboHitStreak;
    private bool comboActive;

    public float CurrentComboMultiplier
    {
        get
        {
            if (!comboActive) return 1f;
            int effectiveHits = Mathf.Max(0, comboHitStreak - (comboThreshold - 1));
            return 1f + comboBonusPerHit * effectiveHits;
        }
    }

    private const float DAMAGE_BASELINE = 5f;

    public float BaseDamage
    {
        get => baseDamage;
        set => baseDamage = Mathf.Max(0f, value);
    }

    public float CurrentMultipliers => damageMultiplier * tempDamageMultiplier * tempDamageBounceMultiplier * _uncapDamageBonus;
    public float CurrentDamage => Mathf.Max(0f, (baseDamage + flatBonusDamage) * CurrentMultipliers);
    public float ScoreXpDamageFactor => Mathf.Max(0f, DAMAGE_BASELINE > 0f ? (CurrentDamage / DAMAGE_BASELINE) : 1f);

    public void AddDamageMultiplier(float addPercent)
    {
        if (Mathf.Approximately(addPercent, 0f)) return;
        damageMultiplier = Mathf.Max(0f, damageMultiplier + addPercent);
    }

    public void AddFlatDamage(int flatDamage, int bounces)
    {
        if (flatDamage == 0) return;
        flatBonusDamage = 0;
        flatBonusDamage += flatDamage;
        bonusBouncesNeeded = bounces;
        bonusBouncesRemaining = bonusBouncesNeeded;
    }

    public void AddTempDamageMultiplier(float factor, int bounces)
    {
        tempDamageMultiplierStore = Mathf.Max(0f, factor);
        tmpBonusBouncesNeeded = bounces;
        tmpBonusBouncesRemaining = tmpBonusBouncesNeeded;
        tempDamageMultiplier = 1f;
    }

    public void AddTempDamageForBounce(float factor, int bounces)
    {
        // CHANGED: factor is now multiplicative (2 => double damage), not additive
        tempDamageBounceMultiplier *= Mathf.Max(0f, factor);
        tempDamageBouncesRemaining = bounces;
    }

    public void ConsumeBounceForDamageMods()
    {
        bonusBouncesRemaining--;
        tmpBonusBouncesRemaining--;
        tempDamageBouncesRemaining--;

        if (bonusBouncesRemaining < 0 && tmpBonusBouncesRemaining < 0 && tempDamageBouncesRemaining < 0)
            return;

        if (bonusBouncesRemaining == 0)
            flatBonusDamage = 0;

        if (tmpBonusBouncesRemaining > 0)
        {
            tempDamageMultiplier = 1f;
        }
        else if (tmpBonusBouncesRemaining == 0)
        {
            tempDamageMultiplier = 1 + tempDamageMultiplierStore;
            ResetTempBounceMods();
        }

        if (tempDamageBouncesRemaining == 0)
        {
            tempDamageBounceMultiplier = 1;
        }
    }

    private void ResetDamageMods()
    {
        flatBonusDamage = 0;
        bonusBouncesRemaining = 0;
    }

    private void ResetTempBounceMods()
    {
        tmpBonusBouncesRemaining = tmpBonusBouncesNeeded;
    }

    private PhysicMaterial runtimePhysMat;

    public void EnsureUniquePhysicMaterial()
    {
        if (col == null) col = GetComponent<Collider>();
        if (col == null) return;
        if (runtimePhysMat != null) return;
        var src = col.material;

        if (src != null)
        {
            runtimePhysMat = Instantiate(src);
            runtimePhysMat.name = src.name + " (Runtime)";
        }
        else
        {
            runtimePhysMat = new PhysicMaterial("RuntimePhysMat");
        }

        col.material = runtimePhysMat;
    }

    public void AdjustBounciness(float factor)
    {
        EnsureUniquePhysicMaterial();
        if (col != null && col.material != null)
        {
            col.material.bounciness *= factor;
        }
    }

    public bool IsInLaunchTube { get; private set; }
    public bool IsTouchingPaddles { get; private set; }
    public bool IsActive { get; private set; }

    Rigidbody rb;
    float debugTimer;

    Pinball pinball;

    public int bounceCount { private set; get; }
    public int bumpCount { private set; get; }
    public int bumpCountConsecutive { private set; get; }

    private string lastBouncedTag;

    Vector3 prevBumpDirection = Vector3.zero;
    int sameDirHits = 0;
    float lastBumpTime = -999f;

    float sameDirDotThreshold = .98f;
    int sameDirHitLimit = 25;
    float sameDirWindow = .25f;

    bool debugTimerStart;
    bool resetBall = false;
    bool isStuck;
    public float maxSpeed = 50f;

    [SerializeField] private ParticleSystemForceField forceField;

    // OLD (was directly mutated). Keep for UI/debug.
    public float forceFieldRadius = 0f;

    // NEW: persistent baseline captured from prefab
    [SerializeField, Tooltip("Baseline (prefab) radius; level-up inherits via Pinball.XPBaseRadiusScale")]
    private float baseForceFieldRadius = 0f;

    // NEW: temporary multiplier for one-off effects (e.g., vacuum)
    private float tempForcefieldScale = 1f;

    public float TempForcefieldScale => tempForcefieldScale;

    public void SetTempForcefieldScaleAbsolute(float newAbsoluteScale)
    {
        newAbsoluteScale = Mathf.Max(0.0001f, newAbsoluteScale);
        float factor = newAbsoluteScale / Mathf.Max(0.0001f, tempForcefieldScale);
        ApplyTempForcefieldScale(factor);
    }


    public float forceFieldRadiusEffective => baseForceFieldRadius * (Pinball.Instance ? Pinball.Instance.XPBaseRadiusScale : 1f) * tempForcefieldScale;

    Collider col;

    int count;
    int dirBumpCount;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        pinball = GameObject.FindWithTag("PinballManager").GetComponent<Pinball>();
        _renderer = GetComponent<Renderer>();

        // Try auto-bind the child light if not set
        if (!glowLight)
            glowLight = GetComponentInChildren<Light>(true);
    }

    void OnEnable()
    {
        IsActive = true;
        if (Pinball.Instance != null)
            Pinball.Instance.RegisterBall(this);
        col = GetComponent<Collider>();
        XPCollectorRegistry.I?.Register(col);

        if (_renderer != null)
        {
            var shared = _renderer.sharedMaterial;
            if (shared != null && (_runtimeMat == null || _renderer.sharedMaterial == _runtimeMat))
            {
                _runtimeMat = Instantiate(shared);
                _runtimeMat.name = shared.name + " (Runtime Ball)";
                _runtimeMat.EnableKeyword("_EMISSION");
                _renderer.sharedMaterial = _runtimeMat;
            }
            ApplyGlow();        // sets emission and syncs light color
            UpdateGlowLightIntensity(); // initialize intensity
            if (glowLight) _glowIntensity = glowLight.intensity;
        }

        OnBallActivated?.Invoke(this);
    }

    void OnDisable()
    {
        IsActive = false;
        if (Pinball.Instance != null)
            Pinball.Instance.UnregisterBall(this);
        XPCollectorRegistry.I?.Unregister(col);

        // If we get disabled mid-uncap, restore the original cap (prevents sticky maxSpeed).
        if (_maxSpeedUncapped)
        {
            if (_restoreOriginalMaxOnExpire)
                maxSpeed = _originalMaxSpeedBeforeUncap;
            _temporaryMaxSpeedTarget = 0f;
            _maxSpeedUncapped = false;
        }

        // Ensure temp uncap mods are reverted if ball gets disabled
        RevertUncapMods();

        BreakCombo("Ball disabled");
        OnBallDeactivated?.Invoke(this);
    }

    void Start()
    {
        count = 0;
        debugTimer = 0;
        IsActive = true;

        forceFieldGravity = forceField.gravity.constant;

        // Capture baseline from prefab and initialize effective radius
        baseForceFieldRadius = forceField.endRange;
        forceFieldRadius = baseForceFieldRadius;
        RefreshForcefieldFromContext();
    }

    void FixedUpdate()
    {
        // Speed cap logic
        if (_maxSpeedUncapped)
        {
            // Ensure temporary mods applied in case this was set elsewhere
            if (!_uncapModsApplied)
                ApplyUncapMods();

            if (Time.time >= _maxSpeedUncapUntil)
            {
                // Expired -> restore original maxSpeed if requested and revert temporary mods
                if (_restoreOriginalMaxOnExpire)
                    maxSpeed = _originalMaxSpeedBeforeUncap;

                _temporaryMaxSpeedTarget = 0f; // fully clear the window
                RevertUncapMods();
                _maxSpeedUncapped = false;
            }
            else
            {
                // While uncapped:
                //   - newMaxSpeed <= 0f => fully uncapped (no clamp here)
                //   - newMaxSpeed  > 0f => treat as a finite cap; clamp if enabled
                if (_temporaryMaxSpeedTarget > 0f)
                {
                    maxSpeed = Mathf.Max(_temporaryMaxSpeedTarget, _originalMaxSpeedBeforeUncap);
                    if (clampDuringUncapIfFinite)
                        EnforceSpeedCapNow(maxSpeed);
                }
            }
        }
        else
        {
            // Normal clamp
            EnforceSpeedCapNow(maxSpeed);

            // Safety: if for any reason mods are still applied while not uncapped, revert them
            if (_uncapModsApplied)
                RevertUncapMods();
        }

        // Optional absolute safety hard cap (applied last, regardless of state)
        if (hardSpeedCap > 0f)
            EnforceSpeedCapNow(hardSpeedCap);

        // Update the glow light intensity based on current speed
        UpdateGlowLightIntensity();
    }

    void Update()
    {
    }

    private void RegisterBumperHitForCombo()
    {
        comboHitStreak++;

        if (!comboActive && comboHitStreak >= comboThreshold)
        {
            comboActive = true;
        }

        ApplyGlow();
        OnComboChanged?.Invoke(this);
    }

    private float ComputeEmissionIntensity()
    {
        if (!comboActive) return Mathf.Max(0f, emissionBase);
        int steps = Mathf.Max(0, comboHitStreak - (comboThreshold - 1));
        return Mathf.Max(0f, emissionBase + emissionPerComboStep * steps);
    }

    public void RandomizeGlowColor()
    {
        var c = UnityEngine.Random.ColorHSV(0f, 1f, 0.65f, 1f, 0.9f, 1f);
        GlowColor = c;
    }

    private void ApplyGlow()
    {
        if (_renderer == null) return;
        float intensity = ComputeEmissionIntensity();
        var emissive = (Color)(glowColor * Mathf.LinearToGammaSpace(intensity));
        if (_runtimeMat != null)
        {
            _runtimeMat.EnableKeyword("_EMISSION");
            _runtimeMat.SetColor(EmissionColorId, emissive);
            _runtimeMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            var mpb = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionColorId, emissive);
            _renderer.SetPropertyBlock(mpb);
        }

        // Keep the light color in sync with glow color
        SyncGlowLightColor();
    }

    // NEW: keep the attached light color in sync with the ball glow color
    private void SyncGlowLightColor()
    {
        if (glowLight != null)
            glowLight.color = glowColor;
    }

    // NEW: map speed [0..maxSpeed] to intensity [0.5 .. 2]
    private void UpdateGlowLightIntensity()
    {
        if (glowLight == null || rb == null) return;

        // Target intensity from current speed
        float max = Mathf.Max(0.01f, maxSpeed);
        float speed = rb.velocity.magnitude;
        float t = Mathf.InverseLerp(0f, max, speed);
        float target = Mathf.Lerp(LightIntensityMin, LightIntensityMax, t);

        // Initialize smoothed value once
        if (_glowIntensity <= 0f)
            _glowIntensity = glowLight.intensity > 0f ? glowLight.intensity : target;

        // Fast rise, slower fall
        float dt = Time.unscaledDeltaTime;
        if (target >= _glowIntensity)
        {
            // Quick but not instant rise
            _glowIntensity = Mathf.MoveTowards(_glowIntensity, target, glowIntensityRiseRate * dt);
        }
        else
        {
            // Smooth, slightly slower decay
            _glowIntensity = Mathf.SmoothDamp(_glowIntensity, target, ref _glowIntensityVel, glowIntensityFallSmoothTime, Mathf.Infinity, dt);
        }

        _glowIntensity = Mathf.Clamp(_glowIntensity, LightIntensityMin, LightIntensityMax);
        glowLight.intensity = _glowIntensity;
    }

    private void BreakCombo(string reason = null)
    {
        if (!comboActive && comboHitStreak == 0) return;
        comboHitStreak = 0;
        comboActive = false;
        ApplyGlow();
        OnComboChanged?.Invoke(this);
    }

    public void ApplyPaddleDamageEffect(PaddleEffectData effect)
    {
        int flat = 0;
        int bounces = 0;

        switch (effect.Element)
        {
            case PaddleState.Fire:
                flat = effect.FireBonusDamage;
                bounces = effect.FireBounceDuration;
                break;
            case PaddleState.Water:
                flat = effect.WaterDamageFlat;
                bounces = effect.WaterBounceDuration;
                break;
            case PaddleState.Earth:
                flat = 0;
                bounces = effect.EarthBounceDuration;
                break;
            case PaddleState.Electric:
                flat = 0;
                bounces = effect.ElectricBounceDuration;
                break;
            default:
                break;
        }

        AddFlatDamage(flat, bounces);
    }

    public void Launch(float power)
    {
        rb.velocity = Vector3.zero;
        rb.AddForce(Vector3.forward * (50f * power), ForceMode.Impulse);
    }

    public void Push(float power)
    {
        rb.AddForce((-Vector3.right * (40f * power)) + (Vector3.forward * (5.5f * power)), ForceMode.Impulse);
    }

    public void Bump(Vector3 direction, float deltaV, int bumperKind, Bumper bumperInstance, int portalBoost = 1)
    {
        bumpCount++;

        Vector3 currentDir = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;

        if (Time.time - lastBumpTime > sameDirWindow)
        {
            sameDirHits = 0;
        }

        if (prevBumpDirection != Vector3.zero)
        {
            float cos = Vector3.Dot(currentDir, prevBumpDirection.normalized);
            if (Mathf.Abs(cos) >= sameDirDotThreshold)
                sameDirHits++;
            else
                sameDirHits = 0;
        }

        lastBumpTime = Time.time;
        prevBumpDirection = currentDir;

        if (sameDirHits > sameDirHitLimit)
        {
            ResetRb();
            return;
        }

        rb.AddForce(currentDir * deltaV, ForceMode.Impulse);

        // NEW: immediate velocity cap after an impulse
        if (!_maxSpeedUncapped || (_temporaryMaxSpeedTarget > 0f && clampDuringUncapIfFinite))
        {
            float cap = _maxSpeedUncapped
                ? Mathf.Max(_temporaryMaxSpeedTarget, _originalMaxSpeedBeforeUncap)
                : maxSpeed;
            EnforceSpeedCapNow(cap);
        }
        if (hardSpeedCap > 0f) EnforceSpeedCapNow(hardSpeedCap);

        ApplyKillBumperVelocitySoftClamp(bumperInstance);
        RegisterBumperHitForCombo();

        int baseScore = bumperKind == 0 ? 100 : 50;
        int adjustedScore = Mathf.RoundToInt(baseScore * CurrentComboMultiplier);

        pinball?.AddScore(adjustedScore, bumpCount, bumpCountConsecutive, ScoreXpDamageFactor, ScoreMultPortal: Mathf.Max(1, portalBoost));

        GetComponent<BallElementalState>()?.OnBounce(bumperInstance);
        ConsumeBounceForDamageMods();
    }

    void OnTriggerEnter(Collider other)
    {
        if (pinball != null && pinball.CurrentState == PinballState.Play)
        {
            lastBouncedTag = other.gameObject.tag;
            bounceCount++;

            if (lastBouncedTag == "Bumper" || lastBouncedTag == "SmallBumper")
            {
                bumpCountConsecutive++;
            }
            else bumpCountConsecutive = 0;
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (other.gameObject.tag == "BallThreshold")
            IsInLaunchTube = true;
    }

    void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.tag == "Paddle")
            IsTouchingPaddles = true;
    }

    void OnCollisionEnter(Collision collision)
    {
        var other = collision.collider;
        if (other.CompareTag("Bumper") || other.CompareTag("SmallBumper") || other.CompareTag("Paddle"))
            return;

        bool inBreakLayer = (comboBreakLayers.value & (1 << other.gameObject.layer)) != 0;
        if (inBreakLayer || other.CompareTag("Wall"))
            BreakCombo("Wall");
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.tag == "Paddle")
            IsTouchingPaddles = false;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.gameObject.tag == "BallThreshold")
            IsInLaunchTube = false;
    }

    public void ResetRb()
    {
        sameDirHits = 0;
        prevBumpDirection = Vector3.zero;

        float speed = rb.velocity.magnitude;
        if (speed < 1f) speed = 8f;

        Vector3 baseDir = rb.velocity.sqrMagnitude > .01f ? rb.velocity.normalized : Vector3.forward;
        baseDir.y = 0f;

        float bigDeflect = UnityEngine.Random.Range(120f, 160f);
        Vector3 newDir = (Quaternion.AngleAxis(bigDeflect, Vector3.up) * baseDir).normalized;

        rb.velocity = newDir * speed;
    }

    public void OnPaddleHit(PaddleEffectData effect)
    {
        var elem = GetComponent<BallElementalState>();
        if (elem == null) return;

        switch (effect.Element)
        {
            case PaddleState.Fire:
                elem.SetFireState(
                    effect.FireBonusDamage,
                    effect.FireBurnDamage,
                    effect.FireBurnDuration,
                    effect.FireBounceDuration,
                    effect.FireCanExplode,
                    effect.FireExplosionSize,
                    effect.FireExplosionDamageFlat,
                    effect.FireIsCursed
                );
                break;
            case PaddleState.Water:
                elem.SetWaterState(
                    effect.WaterBonusXP,
                    effect.WaterDamageFlat,
                    effect.WaterDrenchDuration,
                    effect.WaterBounceDuration,
                    effect.WaterCanBurst,
                    effect.WaterBurstSize,
                    effect.WaterBurstDamageFlat,
                    effect.WaterIsCursed
                );
                break;
            case PaddleState.Earth:
                elem.SetEarthState(
                    effect.EarthBonusDamage,
                    effect.EarthFissureDuration,
                    effect.EarthXPBonus,
                    effect.EarthScoreBonus,
                    effect.EarthBounceDuration,
                    effect.EarthIsCursed
                );
                break;
            case PaddleState.Electric:
                elem.SetElectricState(
                    effect.ElectricShockDamage,
                    effect.ElectricChainCount,
                    effect.ElectricXPBonus,
                    effect.ElectricScoreBonus,
                    effect.ElectricBounceDuration,
                    effect.ElectricIsCursed
                );
                break;
            default:
                break;
        }

        ApplyPaddleDamageEffect(effect);
    }

    // === XP Forcefield helpers (NEW model) ===

    // Recompute effective XP radius from baseline, global scale and temp scale
    public void RefreshForcefieldFromContext()
    {
        float global = Pinball.Instance ? Pinball.Instance.XPBaseRadiusScale : 1f;
        float eff = Mathf.Max(0f, baseForceFieldRadius) * Mathf.Max(0.0001f, global) * Mathf.Max(0.0001f, tempForcefieldScale);
        forceFieldRadius = eff;
        if (forceField) forceField.endRange = eff;
    }

    // Temporary scaling used by one-shot powerups (vacuum)
    public void ApplyTempForcefieldScale(float scaleFactor)
    {
        tempForcefieldScale *= Mathf.Max(0.0001f, scaleFactor);
        RefreshForcefieldFromContext();
    }

    // Backwards-compat shim (if any old callsites exist)
    [Obsolete("Use ApplyTempForcefieldScale instead.")]
    public void UpdateForcefield(float amount) => ApplyTempForcefieldScale(amount);

    public void UpdateForcefieldStrength(float strength)
    {
        forceField.gravity = strength;
    }

    public void ResetForcefieldStrength()
    {
        forceField.gravity = forceFieldGravity;
    }

    // NEW: comprehensive copy helper
    public void CopyFrom(Ball src)
    {
        if (src == null || ReferenceEquals(src, this)) return;

        // Base stats
        BaseDamage = src.BaseDamage;

        // Permanent + temporary multipliers
        flatBonusDamage = src.flatBonusDamage;
        damageMultiplier = src.damageMultiplier;

        tempDamageMultiplier = src.tempDamageMultiplier;
        tempDamageMultiplierStore = src.tempDamageMultiplierStore;
        tempDamageBounceMultiplier = src.tempDamageBounceMultiplier;

        // Bounce windows/counters
        bonusBouncesNeeded = src.bonusBouncesNeeded;
        bonusBouncesRemaining = src.bonusBouncesRemaining;
        tmpBonusBouncesNeeded = src.tmpBonusBouncesNeeded;
        tmpBonusBouncesRemaining = src.tmpBonusBouncesRemaining;
        tempDamageBouncesRemaining = src.tempDamageBouncesRemaining;

        var col = GetComponent<Collider>();
        var srcCol = src.GetComponent<Collider>();

        col.excludeLayers = srcCol.excludeLayers;

        // Size & speed
        transform.localScale = src.transform.localScale;
        maxSpeed = src.maxSpeed;

        // PhysicMaterial properties
        EnsureUniquePhysicMaterial();
        src.EnsureUniquePhysicMaterial();
        if (col && col.material && src.col && src.col.material)
        {
            col.material.bounciness = src.col.material.bounciness;
            col.material.dynamicFriction = src.col.material.dynamicFriction;
            col.material.staticFriction = src.col.material.staticFriction;
            col.material.bounceCombine = src.col.material.bounceCombine;
            col.material.frictionCombine = src.col.material.frictionCombine;
        }

        // XP forcefield state (NEW: inherit baseline & temp, then recompute from Pinball context)
        if (forceField && src.forceField)
        {
            baseForceFieldRadius = src.baseForceFieldRadius;
            tempForcefieldScale = src.tempForcefieldScale;
            forceField.gravity = src.forceField.gravity;
            RefreshForcefieldFromContext();
        }
    }

    private void ApplyKillBumperVelocitySoftClamp(Bumper bumper)
    {
        if (!limitKillBumperImpulse || bumper == null || !bumper.IsDead) return;

        // Only skip if fully uncapped (<= 0). If we have a finite uncap target, still clamp to it.
        if (_maxSpeedUncapped && _temporaryMaxSpeedTarget <= 0f) return;
        if (rb == null) return;

        // Determine the working cap reference for this moment
        float capRef = _maxSpeedUncapped && _temporaryMaxSpeedTarget > 0f
            ? Mathf.Max(_temporaryMaxSpeedTarget, _originalMaxSpeedBeforeUncap)
            : maxSpeed;

        Vector3 v = rb.velocity;
        float speed = v.magnitude;
        if (speed <= 0.0001f) return;

        // If the impulse wasn't disproportionately large, do nothing.
        float ratio = (speed - capRef) / Mathf.Max(1f, capRef);
        if (ratio < killBumperImpulseRatioThreshold && speed <= killBumperSpeedCap) return;

        // Only clamp if above desired cap (soft approach)
        if (speed > killBumperSpeedCap)
        {
            float excess = speed - killBumperSpeedCap;
            float damped = killBumperSpeedCap + excess * (1f - killBumperDampStrength);
            v = v.normalized * Mathf.Max(killBumperSpeedCap, damped);
        }

        // Vertical clamp (optional; preserve sign)
        if (Mathf.Abs(v.y) > killBumperVerticalClamp)
            v.y = Mathf.Sign(v.y) * killBumperVerticalClamp;

        // Ensure we don't exceed our working cap or the global hard cap afterwards
        float finalCap = capRef;
        if (hardSpeedCap > 0f) finalCap = Mathf.Min(finalCap, hardSpeedCap);
        if (finalCap > 0f && v.magnitude > finalCap)
            v = v.normalized * finalCap;

        rb.velocity = v;
    }

    // Backward-compat shim
    public void GetProperties(Ball ball)
    {
        CopyFrom(ball);
    }

    public void UpdateForcefield(float amount, bool obsoleteCompatOnly) => ApplyTempForcefieldScale(amount); // keep API stable for any lingering calls

    // === Helpers for temporary uncap bonuses ===
    private void ApplyUncapMods()
    {
        // +80% bounciness (x1.8)
        EnsureUniquePhysicMaterial();
        if (col != null && col.material != null)
        {
            _savedBounciness = col.material.bounciness;
            col.material.bounciness = _savedBounciness * 1.8f;
        }

        // +20% damage multiplier
        _uncapDamageBonus = 1.75f;

        _uncapModsApplied = true;
    }

    private void RevertUncapMods()
    {
        if (!_uncapModsApplied) return;

        if (col != null && col.material != null && _savedBounciness >= 0f)
        {
            col.material.bounciness = _savedBounciness;
        }

        _uncapDamageBonus = 1f;
        _savedBounciness = -1f;
        _uncapModsApplied = false;
    }

    private void EnforceSpeedCapNow(float cap)
    {
        if (!rb) return;
        cap = Mathf.Max(0f, cap);
        if (cap <= 0f) return;

        var v = rb.velocity;
        float speed = v.magnitude;
        if (speed > cap)
            rb.velocity = v.normalized * cap;
    }

}
```

## Assets/Scripts/BallElementalState.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BallElementalState : MonoBehaviour
{
    [SerializeField]
    private ElementalState initialState = ElementalState.None;

    [Header("Element Overlay Materials (optional)")]
    [Tooltip("1st overlay material for Fire state.")]
    [SerializeField] private Material fireMaterial1;
    [Tooltip("2nd overlay material for Fire state.")]
    [SerializeField] private Material fireMaterial2;
    [SerializeField] private Material waterMaterial;
    [SerializeField] private Material earthMaterial;
    [SerializeField] private Material electricMaterial;

    [Tooltip("Instantiate a unique copy of the overlay material(s) per ball (safe if you mutate).")]
    [SerializeField] private bool instantiateElementMaterials = true;

    [Tooltip("Primary slot index where elemental overlay starts (Fire will also use the next slot).")]
    [SerializeField] private int elementMaterialSlot = 1;

    [Header("Fire Helper")]
    [Tooltip("Fire velocity feeder script (auto-added if missing on Fire).")]
    [SerializeField] private bool autoAddFireVelocityFeeder = true;

    Pinball PM;

    public ElementalState CurrentState = ElementalState.None;

    private Renderer _rend;
    private FireVelocityFeeder _fireFeeder;

    // Track currently applied element overlay materials (Fire uses 2)
    private readonly List<Material> _activeElementMaterials = new List<Material>();
    // Cache of original (base) materials so we can restore cleanly
    private Material[] _baseMaterials;
    private bool _cachedBase;

    // For clean up when we instantiate
    private readonly List<Material> _instancedOverlayMaterials = new();

    private Ball ball;
    private float originalMaxSpeed;

    private static readonly Dictionary<(ElementalState, ElementalState), ElementalState> combinations =
        new()
        {
            {(ElementalState.Fire, ElementalState.Water), ElementalState.Steam},
            {(ElementalState.Water, ElementalState.Fire), ElementalState.Steam},
            {(ElementalState.Fire, ElementalState.Earth), ElementalState.Magma},
            {(ElementalState.Earth, ElementalState.Fire), ElementalState.Magma},
            {(ElementalState.Fire, ElementalState.Air), ElementalState.Wildfire},
            {(ElementalState.Air, ElementalState.Fire), ElementalState.Wildfire},
            {(ElementalState.Water, ElementalState.Earth), ElementalState.Sludge},
            {(ElementalState.Earth, ElementalState.Water), ElementalState.Sludge},
            {(ElementalState.Water, ElementalState.Air), ElementalState.Vapor},
            {(ElementalState.Air, ElementalState.Water), ElementalState.Vapor},
            {(ElementalState.Air, ElementalState.Earth), ElementalState.Whirlwind},
            {(ElementalState.Earth, ElementalState.Air), ElementalState.Whirlwind},
        };

    private float fireTempDamage;
    private float fireBurnDamage;
    private float fireBurnDuration;
    private bool fireExplode;
    private float fireExplosionSize;
    private int fireExplosionDamage;
    private bool fireEffectActive;
    private bool fireIsCursed;

    private float waterBonusXP;
    private int waterBonusDamage;
    private float waterDrenchDuration;
    private bool waterExplode;
    private float waterBurstSize;
    private int waterExplosionDamage;
    private bool waterEffectActive;
    private bool waterIsCursed;

    private int earthFissureDamage;
    private float earthCrustDuration;
    private float earthBonusXP;
    private float earthBonusScore;
    private bool earthEffectActive;
    private bool earthIsCursed;

    private int electricShockDamage;
    private int electricChainCount;
    private float electricBonusXP;
    private float electricBonusScore;
    private bool electricEffectActive;
    private bool electricIsCursed;

    private bool areEffectsActive => fireEffectActive || waterEffectActive || earthEffectActive || electricEffectActive;

    private int fireBouncesRemaining;
    private int waterBouncesRemaining;
    private int earthBouncesRemaining;
    private int electricBouncesRemaining;

    public float FireActiveTempDamage => fireTempDamage;
    public float FireBurnDamage => fireBurnDamage;
    public float FireBurnDuration => fireBurnDuration;
    public bool FireExplode => fireExplode;
    public float FireExplosionSize => fireExplosionSize;
    public int FireExplosionDamage => fireExplosionDamage;
    public int FireBouncesRemaining => fireBouncesRemaining;
    public bool FireEffectActive => fireEffectActive;
    public bool FireIsCursed => fireIsCursed;

    public float WaterBonusXP => waterBonusXP;
    public int WaterBonusDamage => waterBonusDamage;
    public float WaterDrenchDuration => waterDrenchDuration;
    public bool WaterExplode => waterExplode;
    public float WaterBurstSize => waterBurstSize;
    public int WaterExplosionDamage => waterExplosionDamage;
    public int WaterBouncesRemaining => waterBouncesRemaining;
    public bool WaterEffectActive => waterEffectActive;
    public bool WaterIsCursed => waterIsCursed;

    public int EarthFissureDamage => earthFissureDamage;
    public float EarthCrustDuration => earthCrustDuration;
    public float EarthBonusXP => earthBonusXP;
    public float EarthBonusScore => earthBonusScore;
    public bool EarthEffectActive => earthEffectActive;
    public bool EarthIsCursed => earthIsCursed;
    public int EarthBouncesRemaining => earthBouncesRemaining;

    public int ElectricShockDamage => electricShockDamage;
    public int ElectricChainCount => electricChainCount;
    public float ElectricBonusXP => electricBonusXP;
    public float ElectricBonusScore => electricBonusScore;
    public bool ElectricEffectActive => electricEffectActive;
    public bool ElectricIsCursed => electricIsCursed;
    public int ElectricBouncesRemaining => electricBouncesRemaining;

    private void Awake()
    {
        ball = GetComponent<Ball>();
        if (ball == null)
            Debug.LogWarning("BallElementalState requires a Ball component on the same GameObject.");

        _rend = GetComponent<Renderer>();
        if (_rend == null)
            Debug.LogWarning("BallElementalState requires a Renderer component.");

        _fireFeeder = GetComponent<FireVelocityFeeder>();
        if (_fireFeeder) _fireFeeder.enabled = false;

        PM = GameObject.FindWithTag("PinballManager")?.GetComponent<Pinball>();
    }

    void Start()
    {
        CurrentState = initialState;
        if (ball != null)
            originalMaxSpeed = ball.maxSpeed;

        CacheBaseMaterialsOnce();

        if (CurrentState != ElementalState.None)
            ApplyElementMaterial(CurrentState);
    }

    private void CacheBaseMaterialsOnce()
    {
        if (_cachedBase || _rend == null) return;
        // Use .materials so we get instances consistent with runtime modifications (not shared).
        _baseMaterials = _rend.materials.ToArray();
        _cachedBase = true;
    }

    public void SetState(ElementalState newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;
        ApplyStateEffects();
        ApplyElementMaterial(newState);
        // TODO: VFX / SFX
    }

    public void CombineWith(ElementalState newElement)
    {
        var combined = CombineElements(CurrentState, newElement);
        SetState(combined);
    }

    public ElementalState CombineElements(ElementalState existing, ElementalState incoming)
    {
        return combinations.TryGetValue((existing, incoming), out var result) ? result : incoming;
    }

    private void ApplyStateEffects()
    {
        if (ball == null) return;
        switch (CurrentState)
        {
            case ElementalState.Fire:
                break;
            case ElementalState.Water:
                break;
            case ElementalState.Earth:
                break;
            case ElementalState.Air:
                break;
            default:
                ball.maxSpeed = originalMaxSpeed;
                break;
        }
    }

    public void ClearState()
    {
        CurrentState = ElementalState.None;
        RemoveElementMaterials();
        if (_fireFeeder) _fireFeeder.enabled = false;
        // TODO: remove VFX / SFX
    }

    private void ApplyElementMaterial(ElementalState state)
    {
        if (_rend == null) return;
        CacheBaseMaterialsOnce();

        // Remove previous overlays first.
        RemoveElementMaterials();

        if (_fireFeeder) _fireFeeder.enabled = false;

        var mats = _rend.materials.ToList(); // current stack (should now equal _baseMaterials after removal)

        // Ensure slot index is not negative
        if (elementMaterialSlot < 0) elementMaterialSlot = 0;

        // Local creation helper
        Material Make(Material src)
        {
            if (src == null) return null;
            if (!instantiateElementMaterials) return src;
            var inst = new Material(src);
            _instancedOverlayMaterials.Add(inst);
            return inst;
        }

        switch (state)
        {
            case ElementalState.Fire:
                if (fireMaterial1 == null || fireMaterial2 == null) return;
                EnsureCapacity(mats, elementMaterialSlot + 2);
                var fireMat1 = Make(fireMaterial1);
                var fireMat2 = Make(fireMaterial2);
                mats[elementMaterialSlot] = fireMat1;
                mats[elementMaterialSlot + 1] = fireMat2;
                _activeElementMaterials.Add(fireMat1);
                _activeElementMaterials.Add(fireMat2);
                if (!_fireFeeder && autoAddFireVelocityFeeder)
                    _fireFeeder = gameObject.AddComponent<FireVelocityFeeder>();
                if (_fireFeeder) _fireFeeder.enabled = true;
                break;

            case ElementalState.Water:
                if (waterMaterial == null) return;
                EnsureCapacity(mats, elementMaterialSlot + 1);
                var wMat = Make(waterMaterial);
                mats[elementMaterialSlot] = wMat;
                _activeElementMaterials.Add(wMat);
                break;

            case ElementalState.Earth:
                if (earthMaterial == null) return;
                EnsureCapacity(mats, elementMaterialSlot + 1);
                var eMat = Make(earthMaterial);
                mats[elementMaterialSlot] = eMat;
                _activeElementMaterials.Add(eMat);
                break;

            case ElementalState.Electric:
                if (electricMaterial == null) return;
                EnsureCapacity(mats, elementMaterialSlot + 1);
                var elMat = Make(electricMaterial);
                mats[elementMaterialSlot] = elMat;
                _activeElementMaterials.Add(elMat);
                break;

            default:
                return;
        }

        _rend.materials = mats.ToArray();
    }

    // Ensure list has at least 'requiredCount' items by extending with base material clones (not overlay)
    private void EnsureCapacity(List<Material> mats, int requiredCount)
    {
        if (!_cachedBase || _baseMaterials == null || _baseMaterials.Length == 0) return;
        var baseRef = _baseMaterials[0];
        while (mats.Count < requiredCount)
        {
            // Use the first base material reference (extra slots will still render using that material until replaced)
            mats.Add(baseRef);
        }
    }

    private void RemoveElementMaterials()
    {
        if (_rend == null) return;

        // If nothing active, still restore to base if we previously modified length.
        if (_activeElementMaterials.Count == 0)
        {
            if (_cachedBase)
                _rend.materials = _baseMaterials.ToArray();
            return;
        }

        var before = _rend.materials;
        // Restore original base set (fast & deterministic) instead of trying to surgically remove.
        if (_cachedBase)
        {
            _rend.materials = _baseMaterials.ToArray();
        }
        else
        {
            // Fallback: rebuild removing overlays by reference
            var mats = before.Where(m => !_activeElementMaterials.Contains(m)).ToArray();
            _rend.materials = mats;
        }

        // Clean up instanced overlay materials (avoid leaking)
        if (instantiateElementMaterials && _instancedOverlayMaterials.Count > 0)
        {
            foreach (var inst in _instancedOverlayMaterials)
            {
                if (inst != null)
                    Destroy(inst);
            }
            _instancedOverlayMaterials.Clear();
        }

        _activeElementMaterials.Clear();
        // Debug (optional): Uncomment if you need verification
        // Debug.Log($"[BallElementalState] Cleared overlays. Before count={before.Length}, After count={_rend.materials.Length}");
    }

    public void OnBounce(Bumper bumper)
    {
        if (!areEffectsActive) return;

        var elem = bumper.gameObject.GetComponent<BumperElementalState>();

        if (fireEffectActive && bumper != null)
        {
            elem.ClearElement();
            elem.ApplyBurn(fireBurnDamage * ball.CurrentMultipliers, fireBurnDuration);
        }
        if (waterEffectActive && bumper != null)
        {
            elem.ClearElement();
            elem.ApplyDrenched(waterDrenchDuration, waterBonusXP);
        }
        if (earthEffectActive && bumper != null)
        {
            elem.ClearElement();
            elem.ApplyCrusted(earthFissureDamage * ball.CurrentMultipliers, earthCrustDuration, earthBonusXP, earthBonusScore);
        }
        if (electricEffectActive && bumper != null)
        {
            elem.ClearElement();
            elem.ApplyShocked(electricShockDamage * ball.CurrentMultipliers, electricBonusXP, electricBonusScore);
        }

        switch (CurrentState)
        {
            case ElementalState.Fire:
                fireBouncesRemaining--;
                if (fireBouncesRemaining <= 0) { fireEffectActive = false; ClearState(); }
                break;
            case ElementalState.Water:
                waterBouncesRemaining--;
                if (waterBouncesRemaining <= 0) { waterEffectActive = false; ClearState(); }
                break;
            case ElementalState.Earth:
                earthBouncesRemaining--;
                if (earthBouncesRemaining <= 0) { earthEffectActive = false; ClearState(); }
                break;
            case ElementalState.Electric:
                electricBouncesRemaining--;
                if (electricBouncesRemaining <= 0) { electricEffectActive = false; ClearState(); }
                break;
        }
    }

    #region Elemental State Methods

    public void SetFireState(int bonusDamage, float burnDamage, float burnDuration, int bounceDuration, bool canExplode, float explosionRadius, int explosionDamageFlat, bool cursed)
    {
        waterEffectActive = earthEffectActive = electricEffectActive = false;
        fireEffectActive = true;

        fireTempDamage = bonusDamage;
        fireBurnDamage = burnDamage;
        fireBurnDuration = burnDuration;
        fireBouncesRemaining += bounceDuration;
        if (fireBouncesRemaining > bounceDuration) fireBouncesRemaining = bounceDuration;
        fireExplode = canExplode;
        fireExplosionSize = explosionRadius;
        fireExplosionDamage = explosionDamageFlat;
        fireIsCursed = cursed;

        SetState(ElementalState.Fire);
    }

    public void SetWaterState(float bonusXP, int bonusDamage, float drenchDuration, int bounceDuration, bool canBurst, float burstRadius, int burstDamageFlat, bool cursed)
    {
        electricEffectActive = fireEffectActive = earthEffectActive = false;
        waterEffectActive = true;

        waterBonusXP = bonusXP;
        waterBonusDamage = bonusDamage;
        waterDrenchDuration = drenchDuration;
        waterBouncesRemaining += bounceDuration;
        if (waterBouncesRemaining > bounceDuration) waterBouncesRemaining = bounceDuration;
        waterExplode = canBurst;
        waterBurstSize = burstRadius;
        waterExplosionDamage = burstDamageFlat;
        waterIsCursed = cursed;

        SetState(ElementalState.Water);
    }

    public void SetEarthState(int fissureDamage, float crustDuration, float bonusXP, float bonusScore, int bounceDuration, bool cursed)
    {
        fireEffectActive = waterEffectActive = electricEffectActive = false;
        earthEffectActive = true;

        earthFissureDamage = fissureDamage;
        earthCrustDuration = crustDuration;
        earthBonusXP = bonusXP;
        earthBonusScore = bonusScore;
        earthBouncesRemaining += bounceDuration;
        if (earthBouncesRemaining > bounceDuration) earthBouncesRemaining = bounceDuration;
        earthIsCursed = cursed;

        SetState(ElementalState.Earth);
    }

    public void SetElectricState(int shockDamage, int chainCount, float bonusXP, float bonusScore, int bounceDuration, bool cursed)
    {
        fireEffectActive = waterEffectActive = earthEffectActive = false;
        electricEffectActive = true;

        electricShockDamage = shockDamage;
        electricChainCount = chainCount;
        electricBonusXP = bonusXP;
        electricBonusScore = bonusScore;
        electricBouncesRemaining += bounceDuration;
        if (electricBouncesRemaining > bounceDuration) electricBouncesRemaining = bounceDuration;
        electricIsCursed = cursed;

        SetState(ElementalState.Electric);
    }

    #endregion
}
```

## Assets/Scripts/BallElements.cs

```csharp
using UnityEngine;

public enum ElementalState
{
    None,
    Fire,
    Water,
    Earth,
    Air,
    Electric,

    Steam,
    Magma,
    Wildfire,

    Sludge,
    Vapor,

    Whirlwind,

}
```

## Assets/Scripts/BallUIEntry.cs

```csharp
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

        // Color the dot: apply a little �intensity� boost to give some visual separation
        float boost = Mathf.Clamp(ball.EmissionIntensityUI, 0.5f, 2.0f);
        var c = ball.GlowColor;
        var bright = new Color(Mathf.Clamp01(c.r * boost), Mathf.Clamp01(c.g * boost), Mathf.Clamp01(c.b * boost), 1f);
        if (colorDot) colorDot.color = bright;

        // Show combo multiplier if active; otherwise default �x1.0�
        float mult = ball.IsComboActive ? ball.CurrentComboMultiplierUI : 1f;
        if (multiplierText) multiplierText.text = $"x{mult:0.0}";
    }
}
```

## Assets/Scripts/BallXPBar.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BallXPBar : MonoBehaviour
{

    public Image xpBar;
    public Image xpBarHolder;


    public TMP_Text levelText;

    float target;
    public float reduceSpeed;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        xpBar.fillAmount = Mathf.MoveTowards(xpBar.fillAmount, target, reduceSpeed * Time.deltaTime);

    }

    public void UpdateXP(float currentXP, float maxXP, int level)
    {
        Debug.Log($"Start max XP {maxXP}");
        target = currentXP / maxXP;
        levelText.text = $"Level: {level}";
    }

}

```

## Assets/Scripts/BaseCharacter.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum TraitType
{
    Endurance, //HP, Def, Res
    Strength, //MinAtk, MaxAtk, Break
    Agility, //Speed, Evasion, Crit
    Wit, //Intelligence, Mana, Luck
    Charm, // Luck, Lifesteal
}
public enum StatType
{
    Health, //E
    Mana, //W
    MinAtk, //S
    MaxAtk, //S
    Accuracy,
    Speed, // A
    Defense, //E
    Resistance, //E
    Evasion, //A
    Critical, // A
    Break, // S
    Intelligence, // W
    Luck, // C
    Lifesteal // C
}

public class BaseCharacter
{



    public string name;
    public string charClass { get; private set; }
    public int level { get; private set; }

    public float TurnMeter { get; private set; } = 0f;
    public void ConsumeTurnMeter() => TurnMeter = 0;
    public bool IsTurnReady => TurnMeter >= 100f * (((previousHits + 1) * 1.35f) * 2f);
    public int previousHits = 0;
    public void FillTurnMeter() => TurnMeter += Speed.Value;

    public  Dictionary<StatType, CharacterStat> stats = new();
    public  Dictionary<TraitType, float> traits = new();

    public CharacterStat Health => stats[StatType.Health];
    public CharacterStat Mana => stats[StatType.Mana];
    public CharacterStat MinAtk => stats[StatType.MinAtk];
    public CharacterStat MaxAtk => stats[StatType.MaxAtk];
    public CharacterStat Speed=> stats[StatType.Speed];
    public CharacterStat Defense => stats[StatType.Defense];
    public CharacterStat Accuracy => stats[StatType.Accuracy];
    public CharacterStat Critical => stats[StatType.Critical];
    public CharacterStat Break => stats[StatType.Break];
    public CharacterStat Evasion => stats[StatType.Evasion];
    public CharacterStat Resistance => stats[StatType.Resistance];
    public CharacterStat Luck => stats[StatType.Luck];

    public float Endurance;
    public float Strength;
    public float Agility;
    public float Wit;
    public float Charm;



    public BaseCharacter()
    {
        name = string.Empty;
        charClass = string.Empty;
        level = 0;
        InitializeStats();
        InitializeTraits();
    }

    public BaseCharacter(string characterName, string characterClass, int characterLevel, float hp, float minAtk, float maxAtk)
    {
        name = characterName;
        charClass = characterClass;
        level = characterLevel;

        InitializeStats();
        InitializeTraits();

        stats[StatType.Health].baseValue = hp;
        stats[StatType.MinAtk].baseValue = minAtk;
        stats[StatType.MaxAtk].baseValue = maxAtk;
    }

    public BaseCharacter(string characterName, string characterClass, int characterLevel, float hp, float mp, float minAtk, float maxAtk)
    {
        name = characterName;
        charClass = characterClass;
        level = characterLevel;

        InitializeStats();
        InitializeTraits();

        stats[StatType.Health].baseValue = hp;
        stats[StatType.Mana].baseValue = mp;
        stats[StatType.MinAtk].baseValue = minAtk;
        stats[StatType.MaxAtk].baseValue = maxAtk;
    }

    public BaseCharacter(string characterName, string characterClass, int characterLevel, float hp, float mp, float minAtk, float maxAtk, float spd)
    {
        name = characterName;
        charClass = characterClass;
        level = characterLevel;

        InitializeStats();
        InitializeTraits();
        stats[StatType.Health].baseValue = hp;
        stats[StatType.Mana].baseValue = mp;
        stats[StatType.MinAtk].baseValue = minAtk;
        stats[StatType.MaxAtk].baseValue = maxAtk;
        stats[StatType.Speed].baseValue = spd;
    }

    public static BaseCharacter CreateCharacterFromClass(string className)
    {
        switch (className)
        {
            case "Warrior": return new Warrior();
            case "Mage": return new Mage();
            case "Druid": return new Druid();
            case "Assassin": return new Assassin();
            case "Tank": return new Tank();
            default: return new BaseCharacter();
        }
    }


    public virtual void InitializeStats()
    {

        foreach(StatType type in System.Enum.GetValues(typeof(StatType)))
        {
            stats[type] = new CharacterStat(0f);
        }

        stats[StatType.Health].baseValue = 100f;
        stats[StatType.Mana].baseValue = 100f;
        stats[StatType.MinAtk].baseValue = 50f;
        stats[StatType.MaxAtk].baseValue = 50f;
        stats[StatType.Speed].baseValue = 100f;
        stats[StatType.Defense].baseValue = 0f;
    }

    public virtual void InitializeRandomStats(Dictionary<StatType, CharacterStat> p1Stats)
    {

        foreach (StatType type in System.Enum.GetValues(typeof(StatType)))
        {
            float offset1 = Random.Range(-.05f, .05f);
            float offset2 = Random.Range(-.05f, .05f);
            float min = Mathf.Min(offset1, offset2);
            float max = Mathf.Max(offset1, offset2);
            float randomizedValue = p1Stats[type].Value * Random.Range(min, max);
            float finalValue = Mathf.Round((p1Stats[type].Value + randomizedValue) * 10f) / 10f;
            this.stats[type] = new CharacterStat(Mathf.Max(0.1f, finalValue));
            Debug.Log($"Random Value Generated - {randomizedValue} : New Stat Value {this.stats[type].Value}\nMin # - {min} : Max # - {max}");

        }
        this.RefillAllVitals(); 
    }

    public virtual void InitializeTraits()
    {
        foreach (TraitType trait in System.Enum.GetValues(typeof(TraitType)))
        {
            traits[trait] = 0;
        }
    }

    public virtual void InitializeRandomTraits(Dictionary<TraitType, float> p1Traits)
    {

        foreach (TraitType trait in System.Enum.GetValues(typeof(TraitType)))
        {
            float offset1 = Random.Range(-.05f, .05f);
            float offset2 = Random.Range(-.05f, .05f);

            float min = Mathf.Min(offset1, offset2);
            float max = Mathf.Max(offset1, offset2);

            float randomizedValue = p1Traits[trait] * Random.Range(min, max);
            float finalValue = Mathf.Round(p1Traits[trait] + randomizedValue);
            this.traits[trait] = Mathf.Max(0, finalValue);
        }
    }

    public virtual void ApplyTraitBonuses()
    {
        stats[StatType.Health].baseValue += traits[TraitType.Endurance] * .01f; //1% endurance value
        stats[StatType.Defense].baseValue += traits[TraitType.Endurance] * 0.5f; //half endurance value
        stats[StatType.Resistance].baseValue += traits[TraitType.Endurance] * 0.3f; //30% endurance value... etc.

        stats[StatType.MinAtk].baseValue += traits[TraitType.Strength] * 1f;
        stats[StatType.MaxAtk].baseValue += traits[TraitType.Strength] * 2f;
        stats[StatType.Break].baseValue += traits[TraitType.Strength] * 0.4f;

        stats[StatType.Speed].baseValue += traits[TraitType.Agility] * 1.2f;
        stats[StatType.Evasion].baseValue += traits[TraitType.Agility] * 0.5f;
        stats[StatType.Critical].baseValue += traits[TraitType.Agility] * 0.25f;

        stats[StatType.Intelligence].baseValue += traits[TraitType.Wit] * 1.5f;
        stats[StatType.Luck].baseValue += traits[TraitType.Wit] * 0.5f;
        stats[StatType.Accuracy].baseValue += traits[TraitType.Wit] * 2f;


        stats[StatType.Luck].baseValue += traits[TraitType.Charm] * 0.05f;
        stats[StatType.Lifesteal].baseValue += traits[TraitType.Charm] * 0.4f;
        stats[StatType.Accuracy].baseValue += traits[TraitType.Charm] * .8f;

        stats[StatType.Critical].baseValue += stats[StatType.Luck].baseValue * 0.25f;

    }

    public virtual void ApplyLevelScaling() { }

    public Dictionary<StatType, CharacterStat> GetStats()
    {
        return stats;
    }

    public Dictionary<TraitType, float> GetTraits()
    {
        return traits;
    }

    public virtual void PrintStats()
    {
        Debug.Log($"Name: {name}");
        Debug.Log($"Class: {charClass}");
        Debug.Log($"Level: {level}");
        Debug.Log($"base health: {stats[StatType.Health].baseValue}");

        foreach( var stat in stats)
        {
            Debug.Log($"{stat.Key} Value: {stat.Value.Value}");
        }
    }

    public void RefillAllVitals()
    {
        this.stats[StatType.Health].RefillToMax();
        this.stats[StatType.Mana].RefillToMax();
    }

    public void RandomizeCharacter(int playerLevel, Dictionary<StatType, CharacterStat> playerStats, Dictionary<TraitType, float> playerTraits)
    {
        this.name = "Generated";
        this.level = Random.Range(playerLevel - 2, playerLevel + 3);
        InitializeRandomStats(playerStats);
        Debug.Log($"Base Value - {this.Health.baseValue}");
        InitializeRandomTraits(playerTraits);
        Debug.Log($"Base Value - {this.Health.baseValue}");
        this.ApplyLevelScaling();
        Debug.Log($"Base Value - {this.Health.baseValue}");
        this.ApplyTraitBonuses();
        Debug.Log($"Base Value - {this.Health.baseValue}");
    }





}

```

## Assets/Scripts/BluePad.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class BluePad : MonoBehaviour, IPadVariant
{
    [Header("Activation")]
    public bool onlyDuringPlay = true;

    [Header("General Boost")]
    public float minSpeedToBoost = 10f;
    public float targetMinSpeed = 24f;
    [Range(0.1f, 3f)] public float boostGain = 1.5f;

    [Header("Flap Tuning")]
    public float upwardPerPlanarSpeed = 0.35f;
    [Range(0f, 2f)] public float reflectDownwardZFactor = 0.85f;
    public float maxUpwardZ = 60f;

    [Header("Stall Handling")]
    public float stallSpeedThreshold = 3.0f;
    public float stallUpwardSpeed = 18f;
    public bool correctDownwardWhenStalling = true;

    [Header("Axis Assumptions")]
    public Vector3 upwardAxis = Vector3.forward;

    [Header("Vertical Jump (+Z Enforcement)")]
    public float guaranteedUpwardSpeed = 10f;
    public float verticalLiftSpeed = 4f;

    [Header("Refire Control")]
    public bool singleActivation = true;
    public float minRefireInterval = 0.15f;

    [Header("Planar Reset")]
    public bool zeroOnlyZBeforeLift = true;

    [Header("Speed Uncap After Boost")]
    public float uncapDuration = .85f;
    public float uncapMaxSpeed = 80f;
    public bool restoreMaxOnExpire = true;

    [Header("Collision / High Speed")]
    public ForceMode forceMode = ForceMode.VelocityChange;
    public bool enforceContinuousCollision = true;

    [Header("Slow-Mo FX")]
    public bool enableSlowMo = true;
    [Range(0.05f, 1f)] public float slowMoScale = 0.3f;
    [Min(0.05f)] public float slowMoHoldDuration = 0.14f;
    [Min(0.02f)] public float slowMoEaseOutDuration = 0.10f;

    [Header("PostFX")]
    public bool enablePostFX = true;
    [Range(0f, 1f)] public float vignettePeak = 0.45f;

    private readonly Dictionary<int, float> _lastApplyTimeByBall = new();
    private readonly HashSet<int> _insideAppliedOnce = new();

    private Collider _trigger;

    private bool _ownsSlowMo;
    private Coroutine _slowMoCR;
    private PostFXController _postFX;

    private DullPad _host;
    public void BindHost(DullPad host) => _host = host;

    void Reset()
    {
        _trigger = GetComponent<Collider>();
        if (_trigger) _trigger.isTrigger = true;
    }

    void Awake()
    {
        _trigger = GetComponent<Collider>();
        _postFX = Pinball.Instance?.PostFX;
    }

    void OnTriggerEnter(Collider other)
    {
        TryApply(other);
    }

    void OnTriggerExit(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (!rb) return;
        int id = rb.GetInstanceID();
        _insideAppliedOnce.Remove(id);
    }

    private bool CanApply(int id)
    {
        if (singleActivation && _insideAppliedOnce.Contains(id))
            return false;
        if (!_lastApplyTimeByBall.TryGetValue(id, out float last))
            return true;
        return (Time.time - last) >= minRefireInterval;
    }

    private void MarkApplied(int id)
    {
        _lastApplyTimeByBall[id] = Time.time;
        if (singleActivation)
            _insideAppliedOnce.Add(id);
    }

    private void TryApply(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (!rb) return;

        var ball = rb.GetComponent<Ball>();
        if (!ball || !ball.isActiveAndEnabled || !ball.IsActive) return;

        if (onlyDuringPlay)
        {
            var pm = Pinball.Instance;
            if (!pm || pm.CurrentState != PinballState.Play) return;
        }

        _host?.NotifyActivity();

        int id = rb.GetInstanceID();
        if (!CanApply(id)) return;

        if (enforceContinuousCollision && rb.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic)
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Vector3 v = rb.velocity;
        Vector3 planar = new Vector3(v.x, 0f, v.z);
        float speed = planar.magnitude;
        float preZ = v.z;

        if (speed <= Mathf.Max(0.01f, stallSpeedThreshold))
        {
            if (correctDownwardWhenStalling && planar.z < 0f)
                rb.velocity = new Vector3(rb.velocity.x, rb.velocity.y, 0f);

            Vector3 dirUp = upwardAxis; dirUp.y = 0f;
            if (dirUp.sqrMagnitude < 1e-5f) dirUp = Vector3.forward;
            dirUp.Normalize();

            Vector3 desiredPlanar = dirUp * Mathf.Max(0f, stallUpwardSpeed);
            Vector3 delta = desiredPlanar - new Vector3(rb.velocity.x, 0f, rb.velocity.z);
            rb.AddForce(new Vector3(delta.x, 0f, delta.z), forceMode);
        }
        else if (speed < targetMinSpeed)
        {
            Vector3 dir = planar.sqrMagnitude > 1e-6f ? planar.normalized : upwardAxis.normalized;
            float deltaSpeed = (targetMinSpeed - speed) * Mathf.Max(0.1f, boostGain);
            rb.AddForce(new Vector3(dir.x, 0f, dir.z) * deltaSpeed, forceMode);
        }

        if (zeroOnlyZBeforeLift)
            rb.velocity = new Vector3(rb.velocity.x, rb.velocity.y, 0f);

        float targetZ = Mathf.Max(guaranteedUpwardSpeed, verticalLiftSpeed);

        if (preZ < 0f)
            targetZ += (-preZ) * Mathf.Max(0f, reflectDownwardZFactor);

        if (upwardPerPlanarSpeed > 0f)
            targetZ += speed * upwardPerPlanarSpeed;

        if (maxUpwardZ > 0f)
            targetZ = Mathf.Min(targetZ, maxUpwardZ);

        var newVel = rb.velocity;
        newVel.z = Mathf.Max(targetZ, 0f);
        rb.velocity = newVel;

        ball.TemporarilyUncapMaxSpeed(uncapDuration, uncapMaxSpeed, restoreMaxOnExpire);

        if (enableSlowMo)
            StartSlowMo();

        Pinball.Instance?.ScreenShake();
        MarkApplied(id);

        // FIX: Disable collider only briefly; will re-enable before revert to avoid leaving pad un-hittable.
        if (_trigger) _trigger.enabled = false;
    }

    private void StartSlowMo()
    {
        if (_slowMoCR != null) return;
        _slowMoCR = StartCoroutine(SlowMoRoutine());
    }

    private IEnumerator SlowMoRoutine()
    {
        _ownsSlowMo = true;
        TimeScaleHub.Begin(this, slowMoScale, affectFixedDelta: true);

        if (enablePostFX && _postFX)
        {
            _postFX.VignetteMax = vignettePeak;
            _postFX.SetVignette(0f);
            _postFX.FadeVignette(0.25f, 0.08f);
            _postFX.ChromaticPulse(0.25f, 0.06f, 0.14f);
        }

        float holdEnd = Time.realtimeSinceStartup + slowMoHoldDuration;
        while (Time.realtimeSinceStartup < holdEnd)
            yield return null;

        yield return new WaitForSecondsRealtime(slowMoEaseOutDuration);

        TimeScaleHub.End(this);
        _ownsSlowMo = false;

        if (enablePostFX && _postFX)
            _postFX.ClearVignette(0.15f);

        _slowMoCR = null;

        // FIX: Re-enable collider BEFORE host reverts (so underlying DullPad stays usable).
        if (_trigger) _trigger.enabled = true;

        if (_host != null)
        {
            _host.RevertToDull();
        }
        else
        {
            StartCoroutine(PostActivationDisable());
        }
    }

    private IEnumerator PostActivationDisable()
    {
        if (_trigger)
            _trigger.enabled = false;

        yield return new WaitForSecondsRealtime(2.0f);

        if (_trigger)
            _trigger.enabled = true;

        _insideAppliedOnce.Clear();
    }

    private void CancelSlowMo()
    {
        if (_slowMoCR != null)
        {
            StopCoroutine(_slowMoCR);
            _slowMoCR = null;
        }
        if (_ownsSlowMo)
        {
            TimeScaleHub.End(this);
            _ownsSlowMo = false;
        }
        if (enablePostFX && _postFX)
            _postFX.ClearVignette(0.15f);
    }

    void OnDisable()
    {
        // FIX: Safety re-enable collider if script is disabled mid-effect.
        if (_trigger && !_trigger.enabled) _trigger.enabled = true;
        CancelSlowMo();
    }
}
```

## Assets/Scripts/Brawler.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Brawler : BaseCharacter
{

    protected float hpLvlIncrease = 6.7f;
    protected float minAtkLvlIncrease = 2.4f;
    protected float maxAtkLvlIncrease = 2.9f;

    protected float endLVLinc = 2.48f;
    protected float strLVLinc = 4.37f;
    protected float agiLVLinc = 2.26f;
    protected float witLVLinc = .74f;
    protected float chaLVLinc = 1.82f;


    public Brawler(string name, int level)
        : base(name, "Brawler", level, 514.9f, 95f, 56.2f, 62.8f, 100f)
    {
        traits[TraitType.Endurance] = 7;
        traits[TraitType.Strength] = 10;
        traits[TraitType.Agility] = 6;
        traits[TraitType.Wit] = 2;
        traits[TraitType.Charm] = 4;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public Brawler()
    : base("", "Brawler", 5, 514.9f, 95f, 56.2f, 62.8f, 100f)
    {
        traits[TraitType.Endurance] = 7;
        traits[TraitType.Strength] = 10;
        traits[TraitType.Agility] = 6;
        traits[TraitType.Wit] = 2;
        traits[TraitType.Charm] = 4;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);

    }
}

```

## Assets/Scripts/Bumper.cs

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BumperType
{
    Small,
    Default,
    Large
}

[DisallowMultipleComponent]
public class Bumper : MonoBehaviour
{
    [SerializeField] public float curHealth;
    [SerializeField] public float maxHealth;
    [SerializeField] public float cooldown;

    public BumperType type;

    private readonly Dictionary<int, float> lastHitTimeByBall = new();
    [SerializeField] private float hitCooldown = 0.02f;

    private Vector3 lastContactPoint;
    private float lastContactTime;
    [SerializeField] private float contactPointTimeout = 0.25f;

    private BumperElementalState bumperElemental;
    private Pinball pinball;

    private float _lastDmgFactorForXP = 1f;
    public float LastDmgFactorForXP => _lastDmgFactorForXP;

    private Vector3 normal;

    [SerializeField] private bool isDead = false;
    public bool IsDead => isDead;

    private static readonly List<Bumper> AllBumpers = new();
    public static IEnumerable<Bumper> EnumerateAll() => AllBumpers;

    private BumperLightController _light;

    private void OnEnable() => AllBumpers.Add(this);
    private void OnDisable() => AllBumpers.Remove(this);

    private void Awake()
    {
        pinball = Pinball.Instance ?? GameObject.FindWithTag("PinballManager")?.GetComponent<Pinball>();
        bumperElemental = GetComponent<BumperElementalState>();
        curHealth = maxHealth;
        isDead = false;

        _light = GetComponent<BumperLightController>();
        if (_light == null) _light = gameObject.AddComponent<BumperLightController>();
    }

    private void OnCollisionEnter(Collision col)
    {
        var rb = col.rigidbody;
        if (rb == null) return;

        var ballComp = rb.GetComponent<Ball>();
        var ballElem = rb.GetComponent<BallElementalState>();

        int id = rb.GetInstanceID();
        if (lastHitTimeByBall.TryGetValue(id, out float last) && Time.time - last < hitCooldown)
            return;
        lastHitTimeByBall[id] = Time.time;

        // Detect if the colliding ball is currently in a ricochet window.
        var assist = rb.GetComponent<RicochetAssist>();
        bool ricochetActive = assist && assist.IsActive;

        // Light behavior:
        // - Normal hit: flash to peak then back to baseline
        // - Ricochet hit: persistent light that increases with each ricochet hit
        if (ricochetActive)
        {
            _light?.StartRicochetMode();
            _light?.IncrementRicochet();
        }
        else
        {
            _light?.PulseFlash();
        }

        var contact = col.contacts[0];
        lastContactPoint = contact.point;
        lastContactTime = Time.time;

        normal = new Vector3(contact.normal.x, 0f, contact.normal.z).normalized;
        if (normal == Vector3.zero)
        {
            normal = (col.transform.position - transform.position);
            normal.y = 0f;
            normal.Normalize();
        }

        float totalDamage = ballComp != null ? ballComp.CurrentDamage : (pinball != null ? pinball.Damage : 0f);
        bool fireTick = ballElem != null && ballElem.CurrentState == ElementalState.Fire;

        float dmgFactor = ballComp != null ? ballComp.ScoreXpDamageFactor : 1f;
        _lastDmgFactorForXP = dmgFactor;

        int bumperKind = CompareTag("SmallBumper") ? 1 : 0;
        float deltaV = bumperKind == 0 ? 150f : 200f;

        int portalBoost = ballComp != null ? ballComp.ConsumePortalBoost() : 1;
        rb.velocity = Vector3.zero;
        ballComp?.Bump(normal, deltaV, bumperKind, this, portalBoost);

        if (isDead) return;

        TakeDamage(totalDamage, elemDmg: fireTick, damageFactor: _lastDmgFactorForXP, sourceBall: ballComp);

        if (pinball != null)
        {
            var dropPos = (Time.time - lastContactTime) <= contactPointTimeout ? lastContactPoint : transform.position;
            PowerupSystem.TrySpawnPickupOnHit(pinball, dropPos, pinball as IRunContext);
        }
    }

    private Bumper FindNearestOther(float maxDistance = Mathf.Infinity)
    {
        Vector3 p = transform.position;
        Bumper nearest = null;
        float bestSqr = maxDistance * maxDistance;

        for (int i = 0; i < AllBumpers.Count; i++)
        {
            var b = AllBumpers[i];
            if (b == null || b == this) continue;
            float d = (b.transform.position - p).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; nearest = b; }
        }
        return nearest;
    }

    public void TakeDamage(float amount, bool elemDmg, float damageFactor = 1f, int xpScoreMult = 1, Ball sourceBall = null)
    {
        if (isDead) return;

        _lastDmgFactorForXP = Mathf.Max(0f, damageFactor);
        bumperElemental?.RecordCrustedIncomingDamage(amount, sourceBall);

        curHealth -= amount;

        GetComponent<BumperAnimScript>()?.BumperHit();
        pinball?.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecent = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecent ? lastContactPoint : transform.position;
            Vector3 offset = basePos + new Vector3(0, 4, 0);
            DamageNumbers.Spawn((float)Math.Round(amount, 1, MidpointRounding.AwayFromZero), offset);
        }

        if (pinball != null)
        {
            if (elemDmg)
            {
                if (curHealth > 0)
                    pinball.SpawnXP(transform.position, isDead: false, isTakingElemDamage: true, damageFactor: _lastDmgFactorForXP, mult: xpScoreMult);
            }
            else
            {
                if (bumperElemental != null && bumperElemental.CurrentState == BumperState.Drenched)
                    pinball.SpawnBonusWaterXP(transform.position, bumperElemental.WaterBonusXP, damageFactor: _lastDmgFactorForXP, mult: xpScoreMult);
                else
                    pinball.SpawnXP(transform.position, isDead: false, isTakingElemDamage: false, damageFactor: _lastDmgFactorForXP, mult: xpScoreMult);
            }
        }

        if (curHealth <= 0f)
            Die(elemDmg, xpScoreMult);
    }

    public void TakeFissureDamage(float amount, float damageFactor)
    {
        if (isDead) return;
        _lastDmgFactorForXP = Mathf.Max(0f, damageFactor);
        curHealth -= amount;

        GetComponent<BumperAnimScript>()?.BumperHit();
        pinball?.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecent = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecent ? lastContactPoint : transform.position;
            Vector3 offset = basePos + new Vector3(0, 4, 0);
            DamageNumbers.Spawn((float)Math.Round(amount, 1, MidpointRounding.AwayFromZero), offset);
        }

        if (pinball != null && bumperElemental != null)
            pinball.SpawnBonusEarthXP(transform.position, bumperElemental.EarthBonusXP, damageFactor: _lastDmgFactorForXP);

        if (curHealth <= 0f)
            Die(false, 1);
    }

    public void TakeShockDamage(float amount, float damageFactor, bool propogate = false)
    {
        if (isDead) return;
        _lastDmgFactorForXP = Mathf.Max(0f, damageFactor);
        bumperElemental?.RecordCrustedIncomingDamage(amount, null);

        if (propogate)
        {
            var nearest = FindNearestOther();
            if (nearest) nearest.TakeShockDamage(amount, _lastDmgFactorForXP, false);
        }

        curHealth -= amount;

        GetComponent<BumperAnimScript>()?.BumperHit();
        pinball?.ScreenShake();

        if (DamageNumbers.IsReady)
        {
            bool hasRecent = (Time.time - lastContactTime) <= contactPointTimeout;
            Vector3 basePos = hasRecent ? lastContactPoint : transform.position;
            Vector3 offset = basePos + new Vector3(1, 4, -1);
            DamageNumbers.Spawn((float)Math.Round(amount, 1, MidpointRounding.AwayFromZero), offset);
        }

        if (pinball != null && bumperElemental != null)
            pinball.SpawnBonusEarthXP(transform.position, bumperElemental.ElectricBonusXP, damageFactor: _lastDmgFactorForXP);

        if (curHealth <= 0f)
            Die(false, 1);
    }

    public void RicochetHit(Ball ball, Vector3 forcedDirection, int portalBoost = 1, bool elemDmgOverride = false)
    {
        if (ball == null || isDead) return;
        lastHitTimeByBall[ball.GetInstanceID()] = Time.time;

        _light?.StartRicochetMode();
        _light?.IncrementRicochet();

        Vector3 dir = forcedDirection;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = (transform.position - ball.transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();
        }

        int bumperKind = CompareTag("SmallBumper") ? 1 : 0;
        float deltaV = bumperKind == 0 ? 150f : 200f;
        ball.Bump(dir, deltaV, bumperKind, this, portalBoost);

        float totalDamage = ball.CurrentDamage;
        float dmgFactor = ball.ScoreXpDamageFactor;
        _lastDmgFactorForXP = dmgFactor;

        if (!isDead)
            TakeDamage(totalDamage, elemDmg: elemDmgOverride, damageFactor: dmgFactor, xpScoreMult: portalBoost, sourceBall: ball);
    }

    private void Die(bool elemDmg, int xpScoreMult)
    {
        if (isDead) return;
        isDead = true;
        curHealth = 0f;

        // Turn light off immediately
        _light?.HandleBumperDeath();

        if (pinball != null)
        {
            pinball.SpawnXP(transform.position, isDead: true, isTakingElemDamage: elemDmg, damageFactor: _lastDmgFactorForXP, mult: xpScoreMult);
            pinball.destroyedBumperBonusActive = true;
            StartCoroutine(pinball.RespawnRoutine(this));
        }
    }

    public void Revive()
    {
        curHealth = maxHealth;
        isDead = curHealth <= 0f;
        if (!isDead)
            _light?.HandleBumperRevive();
    }

    public void EndRicochetLight()
    {
        _light?.EndRicochetMode();
    }
}
```

## Assets/Scripts/BumperElementalState.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BumperElementalState : MonoBehaviour
{

    private Bumper bumper;
    public BumperState CurrentState = BumperState.None;

    private float fireBurnExpireAt;
    private float fireBurnNextTickAt;
    private float fireBurnDamagePerTick;
    private float fireBurnTickInterval = .5f;

    private float waterBonusXP;
    private float waterDrenchExpireAt;

    private float earthFissureDamage;
    private float earthBonusXP;
    private float earthBonusScore;
    private float earthCrustExpireAt;

    private float electricShockDamage;
    private float electricBonusXP;
    private float electricBonusScore;

    public float WaterBonusXP => waterBonusXP;

    public float EarthFissureDamage => earthFissureDamage;
    public float EarthBonusXP => earthBonusXP;
    public float EarthBonusScore => earthBonusScore;

    public float ElectricShockDamage => electricShockDamage;
    public float ElectricBonusXP => electricBonusXP;
    public float ElectricBonusScore => electricBonusScore;

    // NEW: accumulation for Earth fissure
    private float earthAccumulatedDamage;
    private Ball lastBallDuringCrust; // last ball that dealt damage while crusted


    void Awake()
    {
        bumper = GetComponent<Bumper>();
    }

    void Start() { }

    void Update()
    {
        switch (CurrentState)
        {
            case BumperState.None:
                break;
            case BumperState.Burning:
                HandleBurning();
                break;
            case BumperState.Drenched:
                HandleDrenched();
                break;
            case BumperState.Crusted:
                HandleCrusted();
                break;
            case BumperState.Shocked:
                HandleShocked();
                break;
            default:
                break;
        }
    }

    public void ApplyBurn(float dps, float duration)
    {
        CurrentState = BumperState.Burning;
        fireBurnExpireAt = Time.time + duration;
        fireBurnNextTickAt = Time.time + fireBurnTickInterval;
        fireBurnDamagePerTick = dps * fireBurnTickInterval;
    }

    private void HandleBurning()
    {
        if (Time.time >= fireBurnNextTickAt)
        {
            fireBurnNextTickAt += fireBurnTickInterval;
            bumper.TakeDamage(fireBurnDamagePerTick, elemDmg: true);
        }

        if (Time.time >= fireBurnExpireAt)
        {
            ClearBurn();
        }
    }

    public void ClearBurn()
    {
        fireBurnExpireAt = 0f;
        fireBurnNextTickAt = 0f;
        fireBurnDamagePerTick = 0f;
        if (CurrentState == BumperState.Burning)
            CurrentState = BumperState.None;
    }

    public void ApplyDrenched(float duration, float bonusXP)
    {
        CurrentState = BumperState.Drenched;
        waterDrenchExpireAt = Time.time + duration;
        waterBonusXP = bonusXP;
    }

    private void HandleDrenched()
    {
        if (Time.time >= waterDrenchExpireAt)
        {
            ClearDrenched();
        }
    }

    public void ClearDrenched()
    {
        waterDrenchExpireAt = 0f;
        waterBonusXP = 0f;
        if (CurrentState == BumperState.Drenched)
            CurrentState = BumperState.None;
    }

    public void ApplyCrusted(float damage, float duration, float bonusXP, float bonusScore, float bonusUnused, float bonusUnused2)
    {
        // NOTE: parameters kept for compatibility (reward code passes more args).
        CurrentState = BumperState.Crusted;
        float newExpire = Time.time + duration;
        earthFissureDamage = damage; // legacy baseline, not directly used for eruption now
        earthBonusXP = bonusXP;
        earthBonusScore = bonusScore;
        if (newExpire > earthCrustExpireAt)
            earthCrustExpireAt = newExpire;

        // NEW reset accumulation
        earthAccumulatedDamage = 0f;
        lastBallDuringCrust = null;
    }

    // Overload used by reward (keeping original signature)
    public void ApplyCrusted(float damage, float duration, float bonusXP, float bonusScore)
    {
        ApplyCrusted(damage, duration, bonusXP, bonusScore, 0f, 0f);
    }

    public void HandleCrusted()
    {
        if (Time.time >= earthCrustExpireAt)
        {
            // NEW: eruption damage = 75% of accumulated damage during crust.
            // Minimum: 75% of the current damage of a relevant ball.
            float eruptionFromAccum = 0.75f * earthAccumulatedDamage;

            // Pick ball reference: last one that hit while crusted, else primary pinball ball.
            Ball anchorBall = lastBallDuringCrust;
            if (anchorBall == null)
                anchorBall = Pinball.Instance != null ? Pinball.Instance.ball : null;

            float minDamage = 0f;
            if (anchorBall != null)
                minDamage = 0.75f * anchorBall.CurrentDamage;

            float finalDamage = Mathf.Max(eruptionFromAccum, minDamage);

            bumper.TakeFissureDamage(finalDamage, bumper.LastDmgFactorForXP);

            // Clear / reset state
            ClearCrusted();
        }
    }

    public void ClearCrusted()
    {
        earthFissureDamage = 0f;
        earthCrustExpireAt = 0f;
        earthBonusXP = 0f;
        earthBonusScore = 0f;
        earthAccumulatedDamage = 0f;
        lastBallDuringCrust = null;
        if (CurrentState == BumperState.Crusted)
            CurrentState = BumperState.None;
    }

    public void ApplyShocked(float damage, float bonusXP, float bonusScore)
    {
        CurrentState = BumperState.Shocked;
        electricShockDamage = damage;
        electricBonusXP = bonusXP;
        electricBonusScore = bonusScore;
    }

    public void HandleShocked()
    {
        bumper.TakeShockDamage(electricShockDamage, bumper.LastDmgFactorForXP, true);
        ClearShocked();
    }

    public void ClearShocked()
    {
        electricShockDamage = 0f;
        electricBonusXP = 0f;
        electricBonusScore = 0f;
        if (CurrentState == BumperState.Shocked)
            CurrentState = BumperState.None;
    }

    public void ClearElement()
    {
        ClearBurn();
        ClearDrenched();
        ClearCrusted();
        ClearShocked();
    }

    // NEW: record incoming damage while crusted
    public void RecordCrustedIncomingDamage(float amount, Ball sourceBall)
    {
        if (CurrentState != BumperState.Crusted) return;
        if (amount > 0f)
            earthAccumulatedDamage += amount;
        if (sourceBall != null)
            lastBallDuringCrust = sourceBall;
    }

}
```

## Assets/Scripts/BumperElements.cs

```csharp
using UnityEngine;

public enum BumperState
{
    None,
    Burning,
    Drenched,
    Crusted,
    Windswept,
    Shocked,

    Steaming,
    Molten,
    Blazing,

    Sludged,
    Misted,

    Whirling,

}
```

## Assets/Scripts/CameraFollowSimple.cs

```csharp
using UnityEngine;
using DG.Tweening;

[DisallowMultipleComponent]
public class CameraFollowSimple : MonoBehaviour
{
    [Header("Follow Target")]
    [SerializeField] private Transform target;

    [Header("Zoom Framing")]
    [SerializeField] private bool anchorTargetOnZoom = true;
    [SerializeField, Min(0f)] private float targetAnchorDuration = 0.25f;
    [SerializeField, Min(0f)] private float anchorReleaseDistance = 0.35f;
    [SerializeField] private bool recenterLateralDuringZoom = true;
    [SerializeField, Min(0f)] private float lateralRecenterSpeed = 6f;

    [Header("Zoom Jitter Suppression")]
    [SerializeField, Min(0f)] private float microMoveThreshold = 0.015f;
    [SerializeField, Min(0f)] private float stableTrackSpeed = 8f;
    [SerializeField, Min(0f)] private float chargingStableTrackSpeed = 3f;
    [SerializeField] private bool disableSmoothDampWhileAnchored = true;
    [SerializeField] private bool forceExactForwardDuringAnchor = true;

    private Vector3 _stableTargetPos;
    private bool _isCharging;

    private Vector3 _zoomAnchorTargetPos;
    private bool _zoomAnchored;
    private float _zoomAnchorEndTime;

    [Header("Pre-Zoom Lock-On")]
    [SerializeField, Min(0f)] private float preZoomLockTolerance = 0.05f;
    [SerializeField] private bool requireLockBeforeZoom = true;

    [SerializeField] private bool hardSnapOnLockStart = true;
    [SerializeField] private bool hardSnapOnAnchorStart = true;

    private bool _justAnchored;

    private Vector3 ComputeDesiredPosition(Vector3 effTargetPos, Vector3 fwdNow)
    {
        return effTargetPos
               - fwdNow * followDistance
               + Vector3.up * height
               + lateralOffset;
    }

    private bool _waitingForPreZoomLock;
    private float _pendingZoomDistance;
    private float _pendingZoomHeight;
    private System.Action _onPreZoomLocked;

    [Header("Rig")]
    [SerializeField] private float followDistance = 12f;
    [SerializeField] private float height = 10f;
    [SerializeField] private float damping = 12f;
    [SerializeField] private Vector3 lateralOffset = Vector3.zero;
    [SerializeField] private bool lookAtTarget = true;
    [SerializeField] private bool lockRotation = true;
    [SerializeField] private bool enableZoomSmoothing = true;
    [SerializeField, Min(0.01f)] private float zoomLerpSpeed = 6f;

    private Vector3 _velocity;
    private Quaternion _initialRotation;
    private Vector3 _initialForward;
    private float _targetFollowDistance;
    private float _targetHeight;

    // Shake state
    private Vector3 _shakeOffset;
    private Tween _shakeTween;

    public Transform Target
    {
        get => target;
        set => target = value;
    }

    public float FollowDistance
    {
        get => followDistance;
        set => followDistance = Mathf.Max(0.5f, value);
    }

    public float Height
    {
        get => height;
        set => height = Mathf.Max(0f, value);
    }

    public float Damping
    {
        get => damping;
        set => damping = Mathf.Max(0f, value);
    }

    void Awake()
    {
        _initialRotation = transform.rotation;
        _initialForward = transform.forward;
        _targetFollowDistance = followDistance;
        _targetHeight = height;
    }

    void LateUpdate()
    {
        if (!target) return;

        // Zoom interpolation
        if (enableZoomSmoothing)
        {
            followDistance = Mathf.Lerp(followDistance, _targetFollowDistance, Time.unscaledDeltaTime * zoomLerpSpeed);
            height = Mathf.Lerp(height, _targetHeight, Time.unscaledDeltaTime * zoomLerpSpeed);
        }
        else
        {
            followDistance = _targetFollowDistance;
            height = _targetHeight;
        }

        // Handle anchor lifecycle
        if (_zoomAnchored)
        {
            bool closeEnough = Mathf.Abs(followDistance - _targetFollowDistance) <= anchorReleaseDistance
                            && Mathf.Abs(height - _targetHeight) <= anchorReleaseDistance;

            if (Time.unscaledTime >= _zoomAnchorEndTime || closeEnough)
                _zoomAnchored = false;
        }

        // Update filtered target position while anchored
        Vector3 rawTargetPos = target.position;
        if (_zoomAnchored)
        {
            float trackSpeed = _isCharging ? chargingStableTrackSpeed : stableTrackSpeed;
            Vector3 diff = rawTargetPos - _stableTargetPos;

            if (diff.sqrMagnitude > microMoveThreshold * microMoveThreshold)
                _stableTargetPos = Vector3.Lerp(_stableTargetPos, rawTargetPos, Time.unscaledDeltaTime * trackSpeed);
        }
        else
        {
            _stableTargetPos = rawTargetPos;
        }

        Vector3 effectiveTargetPos = _zoomAnchored ? _stableTargetPos : rawTargetPos;

        // Forward vector handling
        Vector3 fwd = lockRotation ? _initialForward : transform.forward;
        if (forceExactForwardDuringAnchor && lockRotation && _zoomAnchored)
            fwd = _initialForward;

        // Optional lateral recentralization during zoom anchor
        if (recenterLateralDuringZoom && _zoomAnchored)
            lateralOffset = Vector3.Lerp(lateralOffset, Vector3.zero, Time.unscaledDeltaTime * lateralRecenterSpeed);

        Vector3 desiredPos = effectiveTargetPos
                           - fwd * followDistance
                           + Vector3.up * height
                           + lateralOffset
                           + _shakeOffset; // additive camera shake

        // Pre-zoom lock check
        if (_waitingForPreZoomLock)
        {
            float distToDesired = (transform.position - desiredPos).magnitude;
            if (distToDesired <= preZoomLockTolerance)
            {
                _waitingForPreZoomLock = false;
                ApplyPendingZoomAndCallback();
            }
        }

        if (_zoomAnchored && disableSmoothDampWhileAnchored)
        {
            // Hard snap once on the first anchored frame to remove any visible drift
            if (hardSnapOnAnchorStart && _justAnchored)
            {
                transform.position = desiredPos;
                _velocity = Vector3.zero;
                _justAnchored = false;
            }
            else
            {
                // Then short, tight blend to avoid jitter while anchored
                transform.position = Vector3.Lerp(
                    transform.position,
                    desiredPos,
                    Time.unscaledDeltaTime * (damping <= 0f ? 20f : damping)
                );
            }
        }
        else
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPos,
                ref _velocity,
                damping <= 0f ? 0f : (1f / damping)
            );
        }

        // Rotation maintenance
        if (lockRotation)
        {
            if (transform.rotation != _initialRotation)
                transform.rotation = _initialRotation;
        }
        else if (lookAtTarget)
        {
            Vector3 lookPos = rawTargetPos;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation((lookPos - transform.position).normalized, Vector3.up),
                Time.unscaledDeltaTime * (damping <= 0f ? 20f : damping)
            );
        }
    }

    public void ZoomTo(float newDistance, float newHeight)
    {
        _targetFollowDistance = Mathf.Max(0.5f, newDistance);
        _targetHeight = Mathf.Max(0f, newHeight);

        if (anchorTargetOnZoom && target)
        {
            _zoomAnchorTargetPos = target.position;
            _stableTargetPos = _zoomAnchorTargetPos;
            _zoomAnchored = true;
            _justAnchored = true;
            _zoomAnchorEndTime = Time.unscaledTime + targetAnchorDuration;
        }
    }

    public void SnapZoom(float newDistance, float newHeight)
    {
        _targetFollowDistance = followDistance = Mathf.Max(0.5f, newDistance);
        _targetHeight = height = Mathf.Max(0f, newHeight);

        if (anchorTargetOnZoom && target)
        {
            _zoomAnchorTargetPos = target.position;
            _stableTargetPos = _zoomAnchorTargetPos;
            _zoomAnchored = true;
            _justAnchored = true;
            _zoomAnchorEndTime = Time.unscaledTime + Mathf.Min(0.05f, targetAnchorDuration);
        }
    }

    public void CancelZoomAnchor() => _zoomAnchored = false;

    public void LockOnThenZoom(float zoomDistance, float zoomHeight, System.Action onLocked = null)
    {
        if (!target)
        {
            onLocked?.Invoke();
            return;
        }

        _pendingZoomDistance = Mathf.Max(0.5f, zoomDistance);
        _pendingZoomHeight = Mathf.Max(0f, zoomHeight);
        _onPreZoomLocked = onLocked;

        if (!requireLockBeforeZoom)
        {
            ApplyPendingZoomAndCallback();
            return;
        }

        StabilizeNow();
        // NEW: remove the brief "machine view" by snapping immediately to current desired framing
        if (hardSnapOnLockStart && target)
        {
            // Recompute a desired position exactly like LateUpdate()
            Vector3 rawTargetPos = target.position;
            Vector3 fwdNow = lockRotation ? _initialForward : transform.forward;

            // If an anchor will be used, respect the stable target position (already set by StabilizeNow)
            Vector3 effTargetPos = _zoomAnchored ? _stableTargetPos : rawTargetPos;

            Vector3 snapPos = ComputeDesiredPosition(effTargetPos, fwdNow);
            transform.position = snapPos;
            _velocity = Vector3.zero;

            if (!lockRotation && lookAtTarget)
            {
                Vector3 lookPos = rawTargetPos;
                transform.rotation = Quaternion.LookRotation((lookPos - transform.position).normalized, Vector3.up);
            }
        }

        _waitingForPreZoomLock = true;
    }

    private void ApplyPendingZoomAndCallback()
    {
        _targetFollowDistance = _pendingZoomDistance;
        _targetHeight = _pendingZoomHeight;

        if (anchorTargetOnZoom && target)
        {
            _zoomAnchorTargetPos = target.position;
            _stableTargetPos = _zoomAnchorTargetPos;
            _zoomAnchored = true;
            _zoomAnchorEndTime = Time.unscaledTime + targetAnchorDuration;
        }

        var cb = _onPreZoomLocked;
        _onPreZoomLocked = null;
        cb?.Invoke();
    }

    public void StabilizeNow()
    {
        if (!target) return;
        _stableTargetPos = target.position;
        _velocity = Vector3.zero;
    }

    public void SetCharging(bool charging) => _isCharging = charging;

    public void SnapToTarget()
    {
        if (!target) return;
        Vector3 desiredPos = target.position - transform.forward * followDistance + Vector3.up * height + lateralOffset;
        transform.position = desiredPos;
        if (lookAtTarget) transform.LookAt(target);
    }

    // Camera shake (additive offset)
    public void StartShake(float duration, float strength, int vibrato, float randomness)
    {
        _shakeTween?.Kill(false);
        _shakeOffset = Vector3.zero;

        // Use a dummy transform-less tween updating a vector offset
        _shakeTween = DOTween.Shake(
            () => _shakeOffset,
            v => _shakeOffset = v,
            duration,
            strength,
            vibrato,
            randomness,
            fadeOut: true
        )
        .SetUpdate(false)
        .SetTarget(this)
        .OnKill(() => _shakeOffset = Vector3.zero)
        .OnComplete(() => _shakeOffset = Vector3.zero);
    }
}
```

## Assets/Scripts/CharacterStat.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections.ObjectModel;

[Serializable]
public class CharacterStat
{
    public float baseValue;
    public float BaseValue => (float)Math.Round(baseValue, 1);

    public float currentValue;

    public virtual float Value
    {
        get
        {
            if (isDirty)
            {
                _value = CalculateFinalValue();
                isDirty = false;
            }
            return _value;
        }
    }

    protected bool isDirty = true;
    protected float _value;

    protected readonly List<StatModifier> statModifiers;
    public readonly ReadOnlyCollection<StatModifier> StatModifiers;

    public CharacterStat()
    {
        statModifiers = new List<StatModifier>();
        StatModifiers = statModifiers.AsReadOnly();
    }

    public CharacterStat(float value) : this()
    {
        baseValue = value;
    }

    public virtual void AddModifier(StatModifier mod)
    {
        isDirty = true;
        statModifiers.Add(mod);
        statModifiers.Sort(CompareModifierOrder);
    }



    protected virtual int CompareModifierOrder(StatModifier a, StatModifier b)
    {
        if (a.Order < b.Order)
            return -1;
        else if(a.Order > b.Order)
            return 1;
        return 0; // a.Order == b.Order
    }

    public virtual bool RemoveModifier(StatModifier mod)
    {
        if(statModifiers.Remove(mod))
        {
            isDirty = true;
            return true;
        }
        return false;
    }

    public virtual bool RemoveAllModifiersFromSource(object source)
    {
        bool didRemove = false;

        for(int i = statModifiers.Count - 1; i >= 0; i--)
        {
            if(statModifiers[i].Source == source)
            {
                isDirty = true;
                didRemove = true;
                statModifiers.RemoveAt(i);
            }
        }

        return didRemove;
    }

    protected virtual float CalculateFinalValue()
    {
        float finalValue = baseValue;
        float sumPercentAdd = 0;

        for(int i = 0; i < statModifiers.Count; i++)
        {
            StatModifier mod = statModifiers[i];

            if(mod.Type == StatModType.Flat)
            {
                finalValue += mod.Value;
            }
            else if(mod.Type == StatModType.PercentAdd)
            {
                sumPercentAdd += mod.Value;

                if(i + 1 >= statModifiers.Count || statModifiers[i + 1].Type != StatModType.PercentAdd)
                {
                    finalValue *= 1 + sumPercentAdd;
                    sumPercentAdd = 0;
                }
            }
            else if (mod.Type == StatModType.PercentMult)
            {
                finalValue *= 1 + mod.Value;
            }
        }

        return (float)Math.Round(finalValue, 1);
    }

    public void RefillToMax()
    {
        isDirty = true;
    }

}

```

## Assets/Scripts/CharacterUI.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class CharacterUI : MonoBehaviour
{

    [Header("UI Canvases")]
    public GameObject homeHUD;
    public GameObject mainMenuHUD;
    public GameObject pinballHUD;
    public GameObject mainStoryHUD;
    public GameObject battleHUD;
    public GameObject preBattleHUD;
    public GameObject winHUD;
    public GameObject loseHUD;
    public GameObject initGameHUD;
    public GameObject initNameHUD;

    [Header("Player 1 UI")]
    public TMP_Text player1Name;
    public TMP_Text player1Class;
    public TMP_Text player1Level;
    public TMP_Text player1Stats;
    public TMP_Text player1Traits;
    [Header("Player 1 Init UI")]
    public TMP_Text player1StatsInit;
    public TMP_Text player1TraitsInit;
    public TMP_Text playerNameInput;

    [Header("Player 2 UI")]
    public TMP_Text player2Name;
    public TMP_Text player2Class;
    public TMP_Text player2Level;
    public TMP_Text player2Stats;
    public TMP_Text player2Traits;

    [Header("Combat System Information")]
    public bool winner = false;

    public void SetCharacterUI(BaseCharacter character, bool isPlayer1)
    {
        string stats = "";
        string traits = "";
        foreach (var stat in character.GetStats())
        {
            if (stat.Value == character.Health || stat.Value == character.Mana)
            {
                stats += $"{stat.Key}: {Mathf.Max(0f, stat.Value.BaseValue)} / {stat.Value.Value}\n";
            }
            else if (stat.Value == character.MinAtk)
            {
                stats += $"Min-Max Atk: {character.MinAtk.Value} - {character.MaxAtk.Value}\n";
            }
            else if (stat.Value == character.MaxAtk)
                stats += "";
            else
                stats += $"{stat.Key}: {stat.Value.Value}\n";
        }
        foreach (var trait in character.GetTraits())
        {
            traits += $"{trait.Key}: {trait.Value}\n";
        }

        if (isPlayer1)
        {
            player1Name.text = character.name;
            player1Class.text = character.charClass;
            player1Level.text = $"Lv {character.level}";
            player1Stats.text = stats;
            player1Traits.text = traits;
        }
        else
        {
            player2Name.text = character.name;
            player2Class.text = character.charClass;
            player2Level.text = $"Lv {character.level}";
            player2Stats.text = stats;
            player2Traits.text = traits;

        }

    }

    public void SetInitCharacterUI(BaseCharacter character)
    {
        string stats = "";
        string traits = "";
        foreach (var stat in character.GetStats())
        {
            if (stat.Value == character.Health || stat.Value == character.Mana)
            {
                stats += $"{stat.Key}: {stat.Value.BaseValue} / {stat.Value.Value}\n";
            }
            else if (stat.Value == character.MinAtk)
            {
                stats += $"Min-Max Atk: {character.MinAtk.Value} - {character.MaxAtk.Value}\n";
            }
            else if (stat.Value == character.MaxAtk)
                stats += "";
            else
                stats += $"{stat.Key}: {stat.Value.Value}\n";
        }
        foreach (var trait in character.GetTraits())
        {
            traits += $"{trait.Key}: {trait.Value}\n";
        }

            player1StatsInit.text = stats;
            player1TraitsInit.text = traits;
    }

    public void UpdateUI()
    {

    }

    public void HandleInitLoad()
    {
        homeHUD.SetActive(true);

        battleHUD.SetActive(false);
        preBattleHUD.SetActive(false);
        initGameHUD.SetActive(false);
        initNameHUD.SetActive(false);
        winHUD.SetActive(false);
        loseHUD.SetActive(false);
        mainStoryHUD.SetActive(false);
        mainMenuHUD.SetActive(false);
    }
    public void HandleChooseCharacter()
    {
        initGameHUD.SetActive(true);

        homeHUD.SetActive(false);
    }

    public void HandlePreBattle()
    {
        battleHUD.SetActive(true);
        preBattleHUD.SetActive(true);

        initNameHUD.SetActive(false);
        mainStoryHUD.SetActive(false);
    }

    public void HandlePinball()
    {
        pinballHUD.SetActive(true);

        mainMenuHUD.SetActive(false);
    }

    public void HandleInitName()
    {
        initNameHUD.SetActive(true);

        initGameHUD.SetActive(false);
    }

    public void HandleMainMenu()
    {
        mainMenuHUD.SetActive(true);

        DisableAllButMM();
    }

    public void HandleBackToMM()
    {
        mainMenuHUD.SetActive(true);

        mainStoryHUD.SetActive(false);
    }

    public void HandleMainStory()
    {
        mainStoryHUD.SetActive(true);

        mainMenuHUD.SetActive(false);
    }

    public void HandleBattle()
    {
        battleHUD.SetActive(true);

        mainMenuHUD.SetActive(false);
        preBattleHUD.SetActive(false);
    }

    public void HandleBattleFinished()
    {
        if (winner)
            winHUD.SetActive(true);
        else
            loseHUD.SetActive(true);

        battleHUD.SetActive(false);
        preBattleHUD.SetActive(false);
    }

    public void DisableMenu()
    {
            winHUD.SetActive(false);
        loseHUD.SetActive(false);
    }

    public void DisableAllButMM()
    {
        battleHUD.SetActive(false);
        preBattleHUD.SetActive(false);
        initGameHUD.SetActive(false);
        initNameHUD.SetActive(false);
        winHUD.SetActive(false);
        loseHUD.SetActive(false);
        mainStoryHUD.SetActive(false);
        homeHUD.SetActive(false);
    }

    public void SetPlayerName(BaseCharacter player)
    {
        player.name = playerNameInput.text;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

```

## Assets/Scripts/CollectAllXPPowerup.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class CollectAllXPPowerup : IPowerup
{
    public string Id => "collect-all-xp";
    public float Weight => 1.0f;
    public string DebugLabel => "Collect All XP";

    [Header("Vacuum Settings")]
    [SerializeField] private float duration = 4f;
    [SerializeField] private float anchorRangeMultiplier = 400f;
    [SerializeField] private float anchorGravity = 36f;
    [SerializeField, Min(0.01f)] private float reassertInterval = 0.1f;
    [SerializeField] private bool debugLogs = true;

    // Prevent stacking runaway radius
    private static bool _vacuumActive;
    private static float _vacuumEndsAt;

    public bool CanTrigger(IRunContext ctx) => ctx is Pinball pb && pb.CurrentState == PinballState.Play;

    public void Execute(Pinball pm, Vector3 triggerPos)
    {
        if (!pm) return;

        var anchor = pm.ball;
        if (anchor == null || !anchor.isActiveAndEnabled || !anchor.IsActive)
        {
            foreach (var b in Object.FindObjectsOfType<Ball>())
            {
                if (b && b.isActiveAndEnabled && b.IsActive) { anchor = b; break; }
            }
        }
        if (!anchor) return;

        // If already active, just extend the window (do NOT snapshot inflated radii again)
        if (_vacuumActive)
        {
            _vacuumEndsAt = Mathf.Max(_vacuumEndsAt, Time.time + duration);
            if (debugLogs) Debug.Log($"[CollectAllXPPowerup] Extended vacuum -> ends at {_vacuumEndsAt:0.00}");
            return;
        }

        _vacuumActive = true;
        _vacuumEndsAt = Time.time + duration;
        pm.StartCoroutine(VacuumToAnchor(pm, anchor));
    }

    private struct FFState
    {
        public ParticleSystemForceField ff;
        public bool enabled;
        public float startRange;
        public float endRange;
        public ParticleSystem.MinMaxCurve gravity;
    }

    private IEnumerator VacuumToAnchor(Pinball pm, Ball initialAnchor)
    {
        var registry = XPCollectorRegistry.I;
        if (debugLogs) Debug.Log($"[CollectAllXPPowerup] Vacuum start -> anchor {initialAnchor?.name}");

        // Snapshot current state once (do NOT treat expanded radius as new baseline later)
        var originals = new List<FFState>(32);
        foreach (var b in Object.FindObjectsOfType<Ball>())
        {
            if (!b || !b.isActiveAndEnabled || !b.IsActive) continue;
            var ffs = b.GetComponentsInChildren<ParticleSystemForceField>(true);
            foreach (var ff in ffs)
            {
                if (!ff) continue;
                originals.Add(new FFState
                {
                    ff = ff,
                    enabled = ff.enabled,
                    startRange = ff.startRange,
                    endRange = ff.endRange,
                    gravity = ff.gravity
                });
            }
        }
        if (debugLogs) Debug.Log($"[CollectAllXPPowerup] Found {originals.Count} forcefields.");

        Ball anchorBall = initialAnchor;

        void ApplySuppressionAndBoost(Ball anchor)
        {
            for (int i = 0; i < originals.Count; i++)
            {
                var st = originals[i];
                if (!st.ff) continue;
                var owner = st.ff.GetComponentInParent<Ball>();
                if (!owner) continue;

                if (owner == anchor)
                {
                    st.ff.enabled = true;
                    st.ff.startRange = st.startRange;
                    st.ff.endRange = st.endRange * Mathf.Max(1f, anchorRangeMultiplier);
                    st.ff.gravity = new ParticleSystem.MinMaxCurve(anchorGravity);
                }
                else
                {
                    st.ff.enabled = false;
                }
            }
        }

        void SetRegistryToAnchor(Ball anchor)
        {
            if (registry == null) return;
            registry.collectors.Clear();
            if (anchor)
            {
                var col = anchor.GetComponent<Collider>();
                if (col) registry.collectors.Add(col);
            }
            registry.NotifyChanged();
        }

        void RestoreForcefields()
        {
            for (int i = 0; i < originals.Count; i++)
            {
                var st = originals[i];
                if (!st.ff) continue;
                st.ff.enabled = st.enabled;
                st.ff.startRange = st.startRange;
                st.ff.endRange = st.endRange;
                st.ff.gravity = st.gravity;
            }
            if (debugLogs) Debug.Log("[CollectAllXPPowerup] Forcefields restored.");
        }

        void RebuildRegistryFromActiveBalls()
        {
            if (registry == null) return;
            registry.collectors.Clear();

            var balls = Object.FindObjectsOfType<Ball>();
            for (int i = 0; i < balls.Length; i++)
            {
                var b = balls[i];
                if (!b || !b.isActiveAndEnabled || !b.IsActive) continue;
                var col = b.GetComponent<Collider>();
                if (col) registry.collectors.Add(col);
            }
            registry.NotifyChanged();
            if (debugLogs) Debug.Log($"[CollectAllXPPowerup] Registry rebuilt (count={registry.collectors.Count}).");
        }

        void RefreshAllBallForcefields()
        {
            foreach (var b in Object.FindObjectsOfType<Ball>())
            {
                if (b && b.isActiveAndEnabled && b.IsActive)
                    b.RefreshForcefieldFromContext(); // ensures original radius restored
            }
        }

        ApplySuppressionAndBoost(anchorBall);
        SetRegistryToAnchor(anchorBall);
        pm.ScreenShake();

        float reassertT = 0f;
        while (Time.time < _vacuumEndsAt)
        {
            // unscaled progress so slowmo doesn't extend effect
            reassertT += Time.unscaledDeltaTime;

            if (!anchorBall || !anchorBall.isActiveAndEnabled || !anchorBall.IsActive)
            {
                Ball newAnchor = null;
                foreach (var b in Object.FindObjectsOfType<Ball>())
                {
                    if (b && b.isActiveAndEnabled && b.IsActive) { newAnchor = b; break; }
                }
                if (newAnchor && newAnchor != anchorBall)
                {
                    anchorBall = newAnchor;
                    ApplySuppressionAndBoost(anchorBall);
                    SetRegistryToAnchor(anchorBall);
                    if (debugLogs) Debug.Log($"[CollectAllXPPowerup] Anchor changed -> {anchorBall.name}");
                }
            }

            if (reassertT >= reassertInterval)
            {
                reassertT = 0f;
                ApplySuppressionAndBoost(anchorBall);
                SetRegistryToAnchor(anchorBall);
            }

            yield return null;
        }

        // Restore
        RestoreForcefields();
        RebuildRegistryFromActiveBalls();
        RefreshAllBallForcefields(); // CRUCIAL: revert inflated radius to true baseline
        _vacuumActive = false;

        if (debugLogs) Debug.Log("[CollectAllXPPowerup] Vacuum end.");
    }
}
```

## Assets/Scripts/CollectXP.cs

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[RequireComponent(typeof(ParticleSystem))]
public class CollectXP : MonoBehaviour
{
    [Header("Trigger Binding")]
    [SerializeField, Min(1)] int maxTargets = 16;          // keep small (perf)
    [SerializeField, Range(0.05f, 1f)] float refreshInterval = 0.25f;

    [Header("XP Settings")]
    [SerializeField] int xpPerParticle = 2;

    [Header("References (drag in Inspector if available)")]
    [SerializeField] BallXPBar ballXPScript;              // where you display/accumulate XP

    ParticleSystem ps;
    ParticleSystem.TriggerModule trigger;

    private readonly List<Collider> assignedTargets = new();

    // Reuse buffers to avoid GC allocations:
    static readonly List<ParticleSystem.Particle> enteredBuf = new(256);
    static readonly List<(Collider c, float d2)> sortBuf = new(64);

    float elapsed;

    void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        trigger = ps.trigger;
    }

    void OnEnable()
    {
        XPCollectorRegistry.OnChanged += RebindTargets;

        SceneManager.sceneLoaded += OnSceneLoaded;

        StartCoroutine(RebindNextFrame());
    }

    void OnDisable()
    {
        XPCollectorRegistry.OnChanged -= RebindTargets;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RebindTargets();
    }

    System.Collections.IEnumerator RebindNextFrame()
    {
        yield return null;
        RebindTargets();
    }

    void Start()
    {
        // Fallbacks if not wired in Inspector (still better to drag in!)
        if (!ballXPScript)
        {
            var go = GameObject.FindWithTag("BallXPHolder");
            if (go) ballXPScript = go.GetComponent<BallXPBar>();
        }

        // First binding pass
        RebindTargets();
    }

    void Update()
    {

        // Periodically refresh the set of tracked colliders (nearest few)
        elapsed += Time.deltaTime;
        if (elapsed >= refreshInterval)
        {
            elapsed = 0f;
            RebindTargets();
        }

        // Self-destroy once this system (and children) are done
        if (ps.particleCount == 0)
            Destroy(gameObject);
    }

    void RebindTargets()
    {
        var regs = XPCollectorRegistry.I?.collectors;
        if (regs == null || regs.Count == 0) return;

        sortBuf.Clear();
        Vector3 p = transform.position;

        // Collect (collider, squaredDistance)
        for (int i = 0; i < regs.Count; i++)
        {
            var c = regs[i];
            if (!c) continue;
            var center = c.bounds.center;
            float d2 = (center - p).sqrMagnitude;
            sortBuf.Add((c, d2));
        }

        // Sort nearest first
        sortBuf.Sort((a, b) => a.d2.CompareTo(b.d2));
        assignedTargets.Clear();
        // Assign nearest up to maxTargets
        int assignCount = Mathf.Min(maxTargets, sortBuf.Count);
        for (int i = 0; i < assignCount; i++)
        {
            trigger.SetCollider(i, sortBuf[i].c);
            assignedTargets.Add(sortBuf[i].c); // keep a local list to pick nearest on collect
        }
        for (int i = assignCount; i < maxTargets; i++)
            trigger.SetCollider(i, null);
    }

    void OnParticleTrigger()
    {
        // Pull only the particles that ENTERED a trigger this frame
        enteredBuf.Clear();
        int count = ps.GetTriggerParticles(ParticleSystemTriggerEventType.Enter, enteredBuf);

        for (int i = 0; i < count; i++)
        {
            // Award XP
            if (Pinball.Instance)
            {
                Pinball.Instance.AddXP(xpPerParticle);
            }

            // Spawn XP numbers at the actual collection point
            var p = enteredBuf[i];
            var main = ps.main;
            Vector3 worldPos = main.simulationSpace == ParticleSystemSimulationSpace.World
                ? p.position
                : ps.transform.TransformPoint(p.position);

            if (XPNumbers.IsReady)
            {
                // pick the nearest assigned collector (typically the ball) to this particle
                Transform follow = null;
                float best = float.PositiveInfinity;
                for (int t = 0; t < assignedTargets.Count; t++)
                {
                    var col = assignedTargets[t];
                    if (!col) continue;
                    float d2 = (col.bounds.center - worldPos).sqrMagnitude;
                    if (d2 < best) { best = d2; follow = col.transform; }
                }

                // Fallback to world-space if none found
                if (follow)
                    XPNumbers.Spawn(Mathf.RoundToInt(Pinball.Instance.FinalXPCalculated), follow, new Vector3(0f, 1.25f, 0f));
                else
                    XPNumbers.Spawn(Mathf.RoundToInt(Pinball.Instance.FinalXPCalculated), worldPos + new Vector3(0f, 1.25f, 0f));
            }
            // Kill ONLY the collected particle
            p.remainingLifetime = 0f;
            enteredBuf[i] = p;
        }

        // Write changes back
        if (count > 0)
            ps.SetTriggerParticles(ParticleSystemTriggerEventType.Enter, enteredBuf);
    }

}
```

## Assets/Scripts/CombatSystem.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace Game.Combat
{
    public class CombatSystem
    {

        private BaseCharacter player1;
        private BaseCharacter player2;

        public List<BaseCharacter> upcomingTurns = new List<BaseCharacter>(8);

        protected BaseCharacter firstAttacker;

        private bool firstAttackerRemoved;

        public float multiAtkChancePercent;


        private float attackerAcc;
        private float attackerBrk;
        private float attackerCrt;

        private float defenderEva;
        private float defenderDef;
        private float defenderRes;

        private float AccEvaRatio;
        private float BrkDefRatio;
        private float CrtResRatio;

        private float critRatio;
        private float blockRatio;

        private float dodgePenalty;
        private float dodgeChance;
        private float hitChance;

        private float damage;

        private float dmgPen;



        bool doOnce;

        // Start is called before the first frame update
        void Start()
        {
        }

        // Update is called once per frame
        void Update()
        {

        }

        public void Initialize(BaseCharacter p1, BaseCharacter p2)
        {
            player1 = p1;
            player2 = p2;
        }

        public BaseCharacter DetermineFirstTurn()
        {
            if(player1.Speed.Value >= player2.Speed.Value)
            {
                firstAttacker = player1;
                player1.previousHits++;
                return player1;
            }
            else if(player2.Speed.Value > player1.Speed.Value)
            {
                firstAttacker = player2;
                player2.previousHits++;
                return player2;
            }

                return null;
        }

        public void DetermineTurns()
        {


            if(player1 != null && player2 != null)
            {

                const int maxTurns = 8;
                while (upcomingTurns.Count < maxTurns)
                {
                    player1.FillTurnMeter();
                        player2.FillTurnMeter();



                    if (player1.previousHits > 0)
                    {
                        if (LuckyHit(player1, player2, player1.previousHits))
                        {
                            player2.previousHits = 0;
                            player1.previousHits++;
                            upcomingTurns.Add(player1);
                            player1.ConsumeTurnMeter();
                            player2.ConsumeTurnMeter();
                            Debug.Log("P1 Lucky Hit!");
                        }
                        else if (player1.Speed.Value > (player2.Speed.Value * 5))
                        {
                            if (player1.previousHits >= player1.Speed.Value / player2.Speed.Value)
                            {
                                player1.ConsumeTurnMeter();
                                player2.ConsumeTurnMeter();
                                upcomingTurns.Add(player2);
                                player1.previousHits = 0;
                            }
                        }
                        else
                        {
                            player1.ConsumeTurnMeter();
                            player1.previousHits = 0;
                            player2.previousHits = 0;
                        }
                    }
                    else if (player2.previousHits > 0)
                    {
                        if (LuckyHit(player2, player1, player2.previousHits))
                        {
                            player1.previousHits = 0;
                            player2.previousHits++;
                            upcomingTurns.Add(player2);
                            player1.ConsumeTurnMeter();
                            player2.ConsumeTurnMeter();
                            Debug.Log("P2 Lucky Hit!");
                        }
                        else if (player2.Speed.Value > (player1.Speed.Value * 5))
                        {
                            if (player2.previousHits >= player2.Speed.Value / player1.Speed.Value)
                            {
                                player1.ConsumeTurnMeter();
                                player2.ConsumeTurnMeter();
                                upcomingTurns.Add(player1);
                                player2.previousHits = 0;
                            }
                        }
                        else
                        {
                            player2.ConsumeTurnMeter();
                            player1.previousHits = 0;
                            player2.previousHits = 0;
                        }
                    }

                    if (player1.IsTurnReady && (!player2.IsTurnReady || player1.TurnMeter >= player2.TurnMeter))
                    {
                        player1.previousHits++;
                        upcomingTurns.Add(player1);
                        Debug.Log("P1 Added from turn!");
                        player2.previousHits = 0;
                        player1.ConsumeTurnMeter();
                    }
                    else if (player2.IsTurnReady)
                    {
                        player2.previousHits++;
                        upcomingTurns.Add(player2);
                        Debug.Log("P2 Added from turn!");
                        player1.previousHits = 0;
                        player2.ConsumeTurnMeter();
                    }
                }
                if (upcomingTurns[0] != null)
                    if (upcomingTurns[0].name == firstAttacker.name && !firstAttackerRemoved)
                    {
                        upcomingTurns.RemoveAt(0);
                        firstAttackerRemoved = true;
                    }



            }

        }


        public void ExecuteAttack(BaseCharacter attacker, BaseCharacter defender)
        {
            damage = Random.Range(attacker.MinAtk.Value, attacker.MaxAtk.Value);

            attackerAcc = attacker.Accuracy.Value;
            attackerBrk = attacker.Break.Value;
            attackerCrt = attacker.Critical.Value;

            defenderEva = defender.Evasion.Value;
            defenderDef = defender.Defense.Value;
            defenderRes = defender.Resistance.Value;



            //Evasion-to-Accuracy check //Dodge


            //Break-to-Defense check //Penetration or Defense


            





            if (WillAttackerHit())
              {
                Debug.Log($" {attacker.name} Can hit! \nHit % - {hitChance} \nDodge % - {dodgeChance}");
                if(!WillDefenderDodge())
                {
                    CalculateDamage();
                    Debug.Log($"{attacker.name} Dmg Pen - {dmgPen}\nDamage - {damage}");
                        if (WillAttackerCrit())
                        {
                            damage *= 1.5f;
                            Debug.Log($" {attacker.name} Crit hit! \nCrit % - {critRatio}");
                        }
                        else if(WillDefenderBlock())
                        {
                            damage *= .5f;
                            Debug.Log($" {defender.name} Blocked hit! \nBlock % - {blockRatio}");
                        }
                    defender.Health.baseValue -= damage;
                }
                else
                    Debug.Log($" {defender.name} Dodged! \nHit % - {hitChance} \nDodge % - {dodgeChance}");

            }
              else
                Debug.Log($" {attacker.name} Missed! \nHit % - {hitChance} \nDodge % - {dodgeChance}");
            }
        public bool LuckyHit(BaseCharacter attacker, BaseCharacter defender, int priorHits)
        {
            float decayRate = 0.2f;
            if (priorHits > 5f)
                decayRate = .3f;
            else if (priorHits > 10f)
                decayRate += .5f;
            float penaltyMultiplier = Mathf.Exp(-decayRate * priorHits); // Shrinks toward 0 over time
            float speedValueAtkr = Mathf.Max(1, attacker.Speed.Value * penaltyMultiplier);
            float speedValueDfdr = defender.Speed.Value;

            float speedRatio = Mathf.Log((speedValueAtkr / speedValueDfdr) + 1, 2f);


            float rawSpeedDiff = Mathf.Abs(speedValueAtkr - speedValueDfdr);

            float scaleFactor = Mathf.Lerp(5f, 2f, Mathf.Clamp01(rawSpeedDiff / 9999f));

            float chance = Mathf.Clamp01(speedRatio / scaleFactor);

            float luck = Mathf.Max(0f, attacker.Luck.Value);
            float luckBonus = Mathf.Clamp01(luck / 99999f) * .25f;
            
            multiAtkChancePercent = chance;

            float finalChance = Mathf.Clamp01(chance + luckBonus);

            Debug.Log($"Luck Bonus -  {attacker.name} {luckBonus}");
            Debug.Log($"Chance -  {attacker.name} {chance}");
            Debug.Log($"Combo -  {attacker.name} {luckBonus + chance}");

            return Random.value < chance;
        }

        public bool WillAttackerHit()
        {
            //Accuracy-to-Evasion check //Accuracy
            AccEvaRatio = attackerAcc / Mathf.Max(1f, defenderEva);
            hitChance = (float)System.Math.Tanh((double)(AccEvaRatio / 1.88f));
            return (Random.value < hitChance);
        }

        public bool WillDefenderDodge()
        {
            dodgePenalty = Mathf.Lerp(0f, 0.15f, 1f - hitChance);
            dodgeChance = Mathf.Clamp01(dodgeChance - dodgePenalty);
            if (defenderEva >= attackerAcc)
            {
                AccEvaRatio = defenderEva / Mathf.Max(1f, attackerAcc);
                dodgeChance = AccEvaRatio / (AccEvaRatio + 2.5f);
            }
            else
            {
                AccEvaRatio = defenderEva / Mathf.Max(1f, attackerAcc);
                dodgeChance = 0.5f * (AccEvaRatio / (AccEvaRatio + 1.5f));
            }
            dodgeChance = Mathf.Clamp01(dodgeChance);
            return (Random.value < Mathf.Clamp01(dodgeChance - dodgePenalty));
        }

        public bool WillAttackerCrit()
        {
            //Crit-to-Resistance check //Crits or Block

            CrtResRatio = attackerCrt / Mathf.Max(1f, defenderRes);
            critRatio = 0f;

            if (Mathf.Approximately(CrtResRatio, 1f))
            {
                critRatio = 0f;
            }
            else if (CrtResRatio > 1f)
            {
                critRatio = Mathf.Log10(CrtResRatio) / Mathf.Log10(10f); // up to +1.0 (100%)
            }

            Debug.Log($"Crit % - {critRatio}");


            if (attackerCrt == 0f)
                return false;
            else if (attackerCrt > defenderRes)
                return (Random.value < critRatio);
            else
            {
                Debug.Log($"Buffed Crit %! - {critRatio+ .05f}");
                return (Random.value < critRatio + .05f);
            }
        }

        public bool WillDefenderBlock()
        {
            CrtResRatio = attackerCrt / Mathf.Max(1f, defenderRes);
            blockRatio = 0f;

            if (Mathf.Approximately(CrtResRatio, 1f))
            {
                blockRatio = 0f;
            }
            else if(CrtResRatio <= 1f)
            {
                float inverseRatio = defenderRes / Mathf.Max(1f, attackerCrt);
                blockRatio = Mathf.Clamp01(Mathf.Log10(inverseRatio) / Mathf.Log10(10f)); // down to -1.0 (-100%)
            }

            Debug.Log($"Block % - {blockRatio}");

            if (defenderRes == 0f)
                return false;
            else if (defenderRes >= attackerCrt)
                return (Random.value < blockRatio);
            else
            {
                Debug.Log($"Buffed Block %! - {blockRatio + .05f}");
                return (Random.value < blockRatio + .05f);
            }
        }

        public void CalculateDamage()
        {
            BrkDefRatio = attackerBrk / Mathf.Max(1f, defenderDef);

            if (Mathf.Approximately(BrkDefRatio, 1f))
            {
                dmgPen = 0f;
            }
            else if (BrkDefRatio > 1f)
            {
                dmgPen = Mathf.Log10(BrkDefRatio) / Mathf.Log10(10f); // up to +1.0 (100%)
            }
            else
            {
                float inverseRatio = defenderDef / Mathf.Max(1f, attackerBrk);
                dmgPen = -Mathf.Log10(inverseRatio) / Mathf.Log10(10f); // down to -1.0 (-100%)
            }

            damage = Mathf.Round((damage + (damage * dmgPen)) * 10f) / 10f;
        }

    }



}


```

## Assets/Scripts/DamageNumberStyleSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

[CreateAssetMenu(menuName = "UI/Damage Numbers/Style", fileName = "DamageNumberStyle")]
public class DamageNumberStyleSO : ScriptableObject
{
    [Header("Typography")]
    public TMP_FontAsset font;
    public Material fontMaterial;
    public float baseFontSize = 4f;
    public Color defaultColor = Color.white;

    [Header("Timing")]
    public float duration = 0.9f;
    [Range(0.01f, 0.3f)] public float fadeInFraction = 0.08f;
    [Range(0.1f, 0.6f)] public float fadeOutFraction = 0.25f;

    [Header("Motion")]
    public float riseDistance = 1.25f;

    [Header("Scale Pop")]
    public float popFromScale = 0.6f;
    public float popToScale = 1.1f;

    [Header("Rendering")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 500;

    [Header("Update")]
    public bool useUnscaledTime = false;
}

```

## Assets/Scripts/Druid.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Druid : BaseCharacter
{

    protected float hpLvlIncrease = 5.8f;
    protected float minAtkLvlIncrease = 1.8f;
    protected float maxAtkLvlIncrease = 2.5f;

    protected float endLVLinc = 2.43f;
    protected float strLVLinc = 2.22f;
    protected float agiLVLinc = 1.65f;
    protected float witLVLinc = 4.31f;
    protected float chaLVLinc = 3.86f;

    public Druid(string name, int level)
        : base(name, "Druid", level, 498.2f, 110f, 47.9f, 49.8f, 100f)
    {
        traits[TraitType.Endurance] = 6;
        traits[TraitType.Strength] = 5;
        traits[TraitType.Agility] = 3;
        traits[TraitType.Wit] = 9;
        traits[TraitType.Charm] = 6;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public Druid()
    : base("", "Druid", 5, 498.2f, 110f, 47.9f, 49.8f, 100f)
    {
        traits[TraitType.Endurance] = 7;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 3;
        traits[TraitType.Wit] = 6;
        traits[TraitType.Charm] = 5;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);
    }

}

```

## Assets/Scripts/DynamicPadManager.cs

```csharp
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Central controller:
/// - Keeps at most maxActivePads enabled (upgraded or flipping) at once.
/// - Pads sit in an idle dull state until:
///     * Their individual nextEnableTime has elapsed
///     * Active/flipping count < maxActivePads
///   Then manager triggers their flip/upgrade.
/// - When a pad finishes its effect via ball interaction, it reverts to dull and enters an interaction cooldown before being eligible again.
/// - NEW: Random auto-disable ("tick off") will turn off active pads after a random active lifetime, bypassing the interaction cooldown.
/// </summary>
[DisallowMultipleComponent]
public class DynamicPadManager : MonoBehaviour
{
    [Header("Limits")]
    [SerializeField, Min(1)] private int maxActivePads = 3;

    [Header("Enable Timing")]
    [SerializeField, Min(0.1f)] private float minEnableDelay = 2f;
    [SerializeField, Min(0.1f)] private float maxEnableDelay = 6f;

    [Header("Interaction Cooldown (after ball interaction)")]
    [SerializeField, Min(0f)] private float interactionDisabledCooldownSeconds = 30f;

    [Header("Random Auto-Disable (Tick Off)")]
    [Tooltip("If enabled, active pads will randomly go off after a random active lifetime (bypassing interaction cooldown).")]
    [SerializeField] private bool enableRandomAutoDisable = true;
    [SerializeField, Min(0.1f)] private float minActiveLifetimeSeconds = 3f;
    [SerializeField, Min(0.1f)] private float maxActiveLifetimeSeconds = 10f;

    [Header("References / Auto-Discover")]
    [Tooltip("If empty, will find all DullPad components in scene at Start.")]
    [SerializeField] private List<DullPad> pads = new();

    // Count of pads currently reserved (flipping or active)
    private int _activeCount;
    // Pads that have been reserved (activation slot claimed)
    private readonly HashSet<DullPad> _reservedPads = new();
    private readonly List<DullPad> _eligibleBuffer = new();

    // NEW: per-pad scheduled auto-disable times
    private readonly Dictionary<DullPad, float> _autoDisableAt = new();

    void Start()
    {
        if (pads.Count == 0)
            pads.AddRange(FindObjectsOfType<DullPad>());

        foreach (var p in pads)
        {
            if (!p) continue;
            p.Manager = this;
            p.SetInteractionCooldown(interactionDisabledCooldownSeconds);
            p.ScheduleNextEnable(RandomEnableDelay());
        }
    }

    void Update()
    {
        TickEnableLogic();
        TickDisableLogic();
    }

    private void TickEnableLogic()
    {
        // If at capacity (including flipping), push back any idle pads whose timers just matured
        if (_activeCount >= maxActivePads)
        {
            KickBackEligibleIdlePads();
            return;
        }

        _eligibleBuffer.Clear();
        float now = Time.time;

        foreach (var p in pads)
        {
            if (!p) continue;
            if (!p.IsIdle) continue; // only idle dull pads
            if (p.NextEnableTime <= now)
                _eligibleBuffer.Add(p);
        }

        if (_eligibleBuffer.Count == 0) return;

        int freeSlots = Mathf.Max(0, maxActivePads - _activeCount);
        int toActivate = Mathf.Min(freeSlots, _eligibleBuffer.Count);

        for (int i = 0; i < toActivate; i++)
        {
            var pad = _eligibleBuffer[i];
            if (!pad) continue;
            pad.BeginFlipAndUpgrade(); // ReserveActivation happens inside
        }
    }

    // NEW: random auto-disable ticking
    private void TickDisableLogic()
    {
        if (!enableRandomAutoDisable) return;

        float now = Time.time;

        // Cleanup stale entries and ensure schedule exists for active pads
        for (int i = pads.Count - 1; i >= 0; i--)
        {
            var pad = pads[i];
            if (!pad) continue;

            if (pad.IsActivePad)
            {
                if (!_autoDisableAt.ContainsKey(pad))
                    _autoDisableAt[pad] = now + RandomActiveLifetime();

                // Time to auto-disable this pad (bypass interaction cooldown)
                if (_autoDisableAt.TryGetValue(pad, out float when) && now >= when)
                {
                    // Force disable without cooldown; will immediately return to Idle and notify manager
                    pad.ForceRevertToDullNoCooldown();
                    _autoDisableAt.Remove(pad);
                }
            }
            else
            {
                // Not active -> no pending auto-disable time
                _autoDisableAt.Remove(pad);
            }
        }
    }

    private void KickBackEligibleIdlePads()
    {
        float now = Time.time;
        foreach (var p in pads)
        {
            if (!p) continue;
            if (!p.IsIdle) continue;
            if (p.NextEnableTime <= now)
                p.ScheduleNextEnable(RandomEnableDelay()); // delay again since slots full
        }
    }

    private float RandomEnableDelay()
    {
        float min = Mathf.Max(0.01f, minEnableDelay);
        float max = Mathf.Max(min, maxEnableDelay);
        return Random.Range(min, max);
    }

    private float RandomActiveLifetime()
    {
        float min = Mathf.Max(0.01f, minActiveLifetimeSeconds);
        float max = Mathf.Max(min, maxActiveLifetimeSeconds);
        return Random.Range(min, max);
    }

    /// <summary>
    /// Called by DullPad when flip starts. Reserves an activation slot immediately.
    /// Also schedules random auto-disable if enabled.
    /// </summary>
    public void ReserveActivation(DullPad pad)
    {
        if (!pad) return;
        if (_reservedPads.Contains(pad)) return;
        _reservedPads.Add(pad);
        _activeCount++;

        if (enableRandomAutoDisable)
            _autoDisableAt[pad] = Time.time + RandomActiveLifetime();
        else
            _autoDisableAt.Remove(pad);
    }

    /// <summary>
    /// Called when pad effect ends and reverts to dull (either via interaction cooldown or forced auto-disable).
    /// </summary>
    public void OnPadDeactivated(DullPad pad)
    {
        if (!pad) return;
        _autoDisableAt.Remove(pad);

        if (_reservedPads.Remove(pad))
            _activeCount = Mathf.Max(0, _activeCount - 1);

        pad.ScheduleNextEnable(RandomEnableDelay());
    }

    // External API (optional)
    public void SetMaxActive(int max) => maxActivePads = Mathf.Max(1, max);
    public void SetEnableWindow(float minDelay, float maxDelay)
    {
        minEnableDelay = Mathf.Max(0.01f, minDelay);
        maxEnableDelay = Mathf.Max(minEnableDelay, maxDelay);
    }
    public void SetInteractionCooldown(float seconds)
    {
        interactionDisabledCooldownSeconds = Mathf.Max(0f, seconds);
        foreach (var p in pads)
            if (p) p.SetInteractionCooldown(interactionDisabledCooldownSeconds);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.35f);
        foreach (var p in pads)
        {
            if (!p) continue;
            Gizmos.DrawWireSphere(p.transform.position + Vector3.up * 0.1f, 0.4f);
        }
    }
#endif
}
```

## Assets/Scripts/EndGame.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EndGame : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float scanInterval = 0.20f; // "every couple of ticks"
    [SerializeField, Min(0f)] private float boundsPadding = 0.02f;   // small pad for overlap box
    private float _scanTimer;

    private Pinball pm;
    private Collider _col;
    private BoxCollider _box;

    void Start()
    {
        pm = GameObject.FindWithTag("PinballManager").GetComponent<Pinball>();
        _col = GetComponent<Collider>();
        _box = _col as BoxCollider;

        if (_col == null)
            Debug.LogWarning("[EndGame] No Collider found; overlap scan disabled.");
        else if (_box == null)
            Debug.LogWarning("[EndGame] Overlap scan requires BoxCollider. Will rely on collision/trigger only.");
    }

    void Update()
    {
        // periodic overlap scan to catch missed entries
        _scanTimer += Time.unscaledDeltaTime;
        if (_scanTimer >= scanInterval)
        {
            _scanTimer = 0f;
            ScanForOverlappingBalls();
        }
    }

    void OnCollisionEnter(Collision col)
    {
        var ball = col.gameObject.GetComponent<Ball>();
        var rb = col.gameObject.GetComponent<Rigidbody>();
        TryDrain(ball, rb);
    }

    void OnCollisionStay(Collision col)
    {
        var ball = col.gameObject.GetComponent<Ball>();
        var rb = col.gameObject.GetComponent<Rigidbody>();
        TryDrain(ball, rb);
    }

    void OnTriggerEnter(Collider other)
    {
        var ball = other.GetComponent<Ball>() ?? other.GetComponentInParent<Ball>();
        var rb = ball ? ball.GetComponent<Rigidbody>() : null;
        TryDrain(ball, rb);
    }

    private void ScanForOverlappingBalls()
    {
        if (_box == null) return; // only safe/oriented with BoxCollider

        // Build oriented box in world space from the BoxCollider (not from bounds!)
        Vector3 centerWS = _box.transform.TransformPoint(_box.center);
        Vector3 halfExtentsWS = Vector3.Scale(_box.size * 0.5f, _box.transform.lossyScale) + Vector3.one * boundsPadding;
        Quaternion rotWS = _box.transform.rotation;

        var hits = Physics.OverlapBox(centerWS, halfExtentsWS, rotWS, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (!h) continue;
            if (h.transform == transform || h.transform.IsChildOf(transform)) continue; // skip self

            var ball = h.GetComponent<Ball>() ?? h.GetComponentInParent<Ball>();
            if (ball == null) continue;

            var rb = ball.GetComponent<Rigidbody>();
            TryDrain(ball, rb);
        }
    }

    private void TryDrain(Ball ball, Rigidbody rb)
    {
        if (pm == null || ball == null) return;

        // only drain during active play, matching original behavior
        if (pm.CurrentState == PinballState.Play && ball.IsActive)
        {
            pm.DisableBall(ball);
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
```

## Assets/Scripts/GameManager.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Game.Combat;

public enum GameState
{
    InitLoad,
    ChooseCharacter,
    BeginTutorial,
    MainMenu,
    Pinball,
    MainStory,
    PreBattle,
    Battle,
    BattleFinished
}

public enum GameMode
{
    None,
    Tutorial,
    MainStory,
    Pinball
}


public class GameManager : MonoBehaviour
{

    protected CombatSystem combatSystem;

    private GameState currentState;
    public GameState CurrentState => currentState;

    private GameMode currentMode;
    public GameMode CurrentMode => currentMode;

    public Warrior warrior;
    public Mage mage;
    public Druid druid;
    public Assassin assassin;
    public Tank tank;
    public Brawler brawler;


    protected BaseCharacter currentTurn;
    protected BaseCharacter tempChar;

    public CharacterUI ui;

    bool firstTurn;
    bool stopIt;

    protected int turnCount;

    private float actionTimer = 2f;
    public float actionDelay = 2f;

    public BaseCharacter player1;
    public BaseCharacter player2;


    // Start is called before the first frame update
    void Start()
    {
        ChangeState(GameState.InitLoad);
        ChangeMode(GameMode.None);

        /*warrior = new Warrior("Jacque", 5);
        mage = new Mage("Jill", 4);
        druid = new Druid("Lacroix", 6);
        assassin = new Assassin("Jinga", 5);
        tank = new Tank("Ronald", 7);
        player1 = warrior;
        */





    }

    // Update is called once per frame
    void Update()
    {

        //auto-battling function
        if(currentState == GameState.Battle)
        {
            if (combatSystem != null)
            {
                actionTimer += Time.deltaTime;
                if (actionTimer >= actionDelay)
                {
                    AutoBattle();
                    actionTimer = 0;
                }

                if (player1 != null && player2 != null)
                if (combatSystem.upcomingTurns.Count < 8 && (player1.Health.BaseValue > 0 && player2.Health.BaseValue > 0))
                {
                    combatSystem.DetermineTurns();
                }



                if (stopIt)
                    for (int i = 0; i < combatSystem.upcomingTurns.Count; i++)
                    {
                        Debug.Log($"Turn {i} - {combatSystem.upcomingTurns[i].name}");
                        if (i == 7)
                            stopIt = false;
                    }
            }
        }
        else if(currentState == GameState.BattleFinished)
        {
            actionTimer = 2;
            stopIt = false;
        }
    }

    public void ChangeState(GameState newState)
    {
        currentState = newState;

        switch (newState)
        {
            case GameState.InitLoad:
                HandleInitLoad();
                break;
            case GameState.ChooseCharacter:
                HandleChooseCharacter();
                break;
            case GameState.MainMenu:
                HandleMainMenu();
                break;
            case GameState.Pinball:
                HandlePinball();
                break;
            case GameState.MainStory:
                HandleMainStory();
                break;
            case GameState.PreBattle:
                HandlePreBattle();
                break;
            case GameState.Battle:
                HandleBattle();
                break;
            case GameState.BattleFinished:
                HandleBattleFinished();
                break;
        }
    }

    public void ChangeMode(GameMode newMode)
    {
        currentMode = newMode;

        switch (newMode)
        {
            case GameMode.None:
                break;
            case GameMode.Tutorial:
                break;
            case GameMode.MainStory:
                break;
            case GameMode.Pinball:
                break;
        }
    }

    public void HandleInitLoad()
    {
        ui.HandleInitLoad();
    }
    public void HandleChooseCharacter()
    {
        ui.HandleChooseCharacter();
    }
    public void HandleMainMenu()
    {
        ui.DisableMenu();
        ui.HandleMainMenu();
    }

    public void HandleMainStory()
    {
        ui.HandleMainStory();
    }

    public void HandleBattleFinished()
    {
        ui.HandleBattleFinished();
    }

    public void HandlePreBattle()
    {
        if (currentMode == GameMode.Tutorial)
        {
            player2 = new BaseCharacter();
            player2.name = "Dummy";
            ui.SetCharacterUI(player2, false);

        }
        else if (currentMode == GameMode.MainStory)
        {
            player2 = new BaseCharacter();
            player2.name = "1-1";
            ui.SetCharacterUI(player2, false);
        }
        else Debug.Log($"Bozo");
            ui.SetCharacterUI(player1, true);
        ui.HandlePreBattle();
        combatSystem = new CombatSystem();
    }
    public void HandleBattle()
    {
        ui.HandleBattle();
    }

    public void HandlePinball()
    {
        ui.HandlePinball();
    }



    public void OnBeginButtonPressed()
    {
        ChangeState(GameState.ChooseCharacter);
    }

    public void OnHomeButtonPressed()
    {
        ChangeMode(GameMode.None);
        ChangeState(GameState.MainMenu);
    }
    public void OnMainStoryButtonPressed()
    {
        ChangeState(GameState.MainStory);
    }
    public void OnMSBattleButtonPressed()
    {
        ChangeMode(GameMode.MainStory);
        ChangeState(GameState.PreBattle);
    }

    public void OnWarriorButtonPressed()
    {
        tempChar = new Warrior();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnMageButtonPressed()
    {
        tempChar = new Mage();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnDruidButtonPressed()
    {
        tempChar = new Druid();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnTankButtonPressed()
    {
        tempChar = new Tank();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnAssassinButtonPressed()
    {
        tempChar = new Assassin();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnBrawlerButtonPressed()
    {
        tempChar = new Brawler();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnCharConfirmButtonPressed()
    {
        if(tempChar != null)
        {
            player1 = tempChar;
            ui.HandleInitName();
        }
    }
    public void OnNameConfirmButtonPressed()
    {
        ui.SetPlayerName(player1);
        ChangeMode(GameMode.Tutorial);
        ChangeState(GameState.PreBattle);
    }

    public void OnStartBattleButtonPressed()
    {
        ChangeState(GameState.Battle);
    }

    public void StartBattle()
    {
        ChangeState(GameState.Battle);
    }


    public void AutoBattle()
    {
        turnCount++;
        if(!firstTurn)
        {
            //player2 = GenerateEnemy(player1);
            combatSystem.Initialize(player1, player2);
            currentTurn = combatSystem.DetermineFirstTurn();
            firstTurn = true;
        }


        stopIt = true;
        HandleTurn();
    }

    public void FinishBattle(BaseCharacter winner)
    {
        //StopAutoBattle();
        if(winner == player1)
            ui.winner = true;
        else
            ui.winner = false;
        ChangeState(GameState.BattleFinished);
        turnCount = 0;
    }


    public void HandleTurn()
    {
        if(turnCount >= 2)
        {
            currentTurn = combatSystem.upcomingTurns[0];
            combatSystem.upcomingTurns.RemoveAt(0);
        }

        BaseCharacter attacker = currentTurn;
        BaseCharacter defender = currentTurn == player1 ? player2 : player1;

        combatSystem.ExecuteAttack(attacker, defender);
        ui.SetCharacterUI(player1, true);
        ui.SetCharacterUI(player2, false);

        //the attacker killed the defender first
        if(defender.Health.BaseValue <= 0)
        {
            FinishBattle(attacker);
        }
        //the attacker hit the defender, but the defender survived and the attacker died to recoil dmg at some point
        else if(attacker.Health.BaseValue <= 0 && defender.Health.BaseValue > 0)
        {
            FinishBattle(defender);
        }


    }



    private BaseCharacter GenerateEnemy(BaseCharacter player1)
    {
        string[] allClasses = new string[] { "Warrior", "Mage", "Assassin", "Druid", "Tank" };
        string chosenClass = allClasses[Random.Range(0, allClasses.Length)];
        BaseCharacter player2 = BaseCharacter.CreateCharacterFromClass(chosenClass);

        player2.RandomizeCharacter(player1.level, player1.stats, player1.traits);

        Debug.Log(player2.Health.BaseValue);
        ui.SetCharacterUI(player2, false);

        return player2;

    }

}

```

## Assets/Scripts/GreenPad.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GreenPad : MonoBehaviour, IPadVariant
{
    [Header("Aim Settings")]
    public float slowMoScale = 0.15f;
    public float aimWindow = 0.85f;
    public float minLaunchSpeed = 15f;
    public float maxLaunchSpeed = 30f;
    public float magnetPull = 0f;
    public Transform focusPoint;

    [Header("Camera Zoom (Optional)")]
    public bool applyCameraZoom = false;
    public float zoomDistance = 8f;
    public float zoomHeight = 6f;
    public float zoomRecoverDelay = 0.25f;
    public float restoreDistance = 12f;
    public float restoreHeight = 10f;

    [Header("Next-Hit Buff")]
    public float nextHitDamageFactor = 2f;
    public int nextHitBounces = 1;

    private DullPad _host;
    private Collider _col;

    public void BindHost(DullPad host) => _host = host;

    void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col) _col.isTrigger = true;
    }

    private void Reset()
    {
        if (!focusPoint) focusPoint = transform;
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        var ball = other.attachedRigidbody ? other.attachedRigidbody.GetComponent<Ball>() : null;
        if (!ball) return;
        if (!_col) return;

        _host?.NotifyActivity();

        var rb = ball.GetComponent<Rigidbody>();
        if (rb)
        {
            Vector3 toward = ((focusPoint ? focusPoint.position : transform.position) - rb.position);
            toward.y = 0f;
            rb.AddForce(toward.normalized * magnetPull, ForceMode.VelocityChange);
        }

        var pm = Pinball.Instance ?? GameObject.FindWithTag("PinballManager")?.GetComponent<Pinball>();
        if (!pm) return;

        pm.EnterGreenPadAim(ball, focusPoint ? focusPoint : transform,
            slowMoScale, aimWindow, minLaunchSpeed, maxLaunchSpeed,
            nextHitDamageFactor, nextHitBounces);

        if (applyCameraZoom)
        {
            var camFollow = Camera.main ? Camera.main.GetComponent<CameraFollowSimple>() : null;
            if (camFollow)
            {
                camFollow.ZoomTo(zoomDistance, zoomHeight);
                camFollow.StabilizeNow();
                StartCoroutine(RestoreCameraZoomAfter(camFollow, aimWindow + zoomRecoverDelay));
            }
        }

        // Disable during aim window
        if (_col) _col.enabled = false;

        StartCoroutine(RevertAfterAimWindow());
    }

    private IEnumerator RestoreCameraZoomAfter(CameraFollowSimple cam, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (cam) cam.ZoomTo(restoreDistance, restoreHeight);
    }

    private IEnumerator RevertAfterAimWindow()
    {
        yield return new WaitForSeconds(aimWindow + 0.05f);

        // FIX: Re-enable collider BEFORE revert so base dull collider remains usable.
        if (_col && !_col.enabled) _col.enabled = true;

        if (_host != null)
            _host.RevertToDull();
    }

    void OnDisable()
    {
        // FIX: Safety if disabled mid-window.
        if (_col && !_col.enabled) _col.enabled = true;
    }
}
```

## Assets/Scripts/GrenadeController.cs

```csharp
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GrenadeController : MonoBehaviour
{
    [Header("Bindings")]
    [SerializeField] private Ball ball;
    [SerializeField] private Rigidbody ballRb;

    [Header("Config")]
    [SerializeField] private float cooldown = 10f;
    [SerializeField] private float fuseSeconds = 2f;
    [SerializeField] private float radius = 6f;
    [SerializeField, Range(0.01f, 1f)] private float maxPctAtCenter = 0.80f;
    [SerializeField, Range(0.01f, 1f)] private float minPctAtEdge = 0.60f;
    [SerializeField, Range(0f, 1f)] private float inheritVelFactor = 0.75f;
    [SerializeField] private float upArcMin = 2.5f;
    [SerializeField] private float upArcMax = 8.0f;
    [SerializeField, Range(0f, 1f)] private float linearDrag = 0.28f;
    [SerializeField, Range(0f, 1f)] private float angularDrag = 0.15f;
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.35f;
    [SerializeField] private float customGravityY = -14f;

    private float _cooldownRemain;
    private Pinball _pm;
    private PinballUIM _ui;

    public void Bind(Ball b,
        float cd, float fuse, float rad, float maxPct, float minPct,
        float inheritFactor, float arcMin, float arcMax,
        float linDrag, float angDrag, float bounce, float gravityY)
    {
        ball = b;
        ballRb = b ? b.GetComponent<Rigidbody>() : null;
        _pm = Pinball.Instance;
        _ui = FindFirstObjectByType<PinballUIM>();

        cooldown = Mathf.Max(0.05f, cd);
        fuseSeconds = Mathf.Max(0.05f, fuse);
        radius = Mathf.Max(0.05f, rad);
        maxPctAtCenter = Mathf.Clamp01(maxPct);
        minPctAtEdge = Mathf.Clamp01(minPct);
        inheritVelFactor = Mathf.Clamp01(inheritFactor);
        upArcMin = Mathf.Max(0f, arcMin);
        upArcMax = Mathf.Max(arcMin, arcMax);
        linearDrag = Mathf.Clamp01(linDrag);
        angularDrag = Mathf.Clamp01(angDrag);
        bounciness = Mathf.Clamp01(bounce);
        customGravityY = gravityY;

        if (_ui && ball) _ui.RegisterGrenadeIcon(ball);
        SetUiReady(true);
    }

    public void ForceCooldown(float seconds)
    {
        _cooldownRemain = Mathf.Max(_cooldownRemain, seconds);
        SetUiReady(false);
        SetUiCooldown(1f);
    }

    void OnDisable()
    {
        if (_ui && ball) _ui.UnregisterGrenadeIcon(ball);
    }

    void Update()
    {
        if (!_pm || !ball || !ball.isActiveAndEnabled || !ball.IsActive) return;

        if (_cooldownRemain > 0f)
        {
            _cooldownRemain -= Time.deltaTime;
            if (_cooldownRemain <= 0f)
            {
                _cooldownRemain = 0f;
                SetUiReady(true);
            }
            else
            {
                float norm = Mathf.Clamp01(_cooldownRemain / cooldown);
                SetUiCooldown(norm);
            }
        }

        if (_pm.CurrentState != PinballState.Play) return;

        if (Input.GetKeyDown(KeyCode.Space) && _cooldownRemain <= 0f)
            DropGrenade();
    }

    private void DropGrenade()
    {
        if (!ball || !ballRb) return;

        var go = new GameObject("GrenadeProjectile");
        go.layer = LayerMask.NameToLayer("Projectile");
        go.transform.position = ball.transform.position + new Vector3(0f, 0.2f, 0f);
        var proj = go.AddComponent<GrenadeProjectile>();
        proj.Init(new GrenadeProjectile.Params
        {
            fuseSeconds = fuseSeconds,
            radius = radius,
            maxPctAtCenter = maxPctAtCenter,
            minPctAtEdge = minPctAtEdge,
            inheritVelocityFactor = inheritVelFactor,
            upArcMin = upArcMin,
            upArcMax = upArcMax,
            linearDrag = linearDrag,
            angularDrag = angularDrag,
            bounciness = bounciness,
            customGravityY = customGravityY,
            ownerBall = ball
        });

        _cooldownRemain = cooldown;
        SetUiReady(false);
        SetUiCooldown(1f);
    }

    private void SetUiReady(bool ready)
    {
        if (_ui && ball) _ui.SetBallGrenadeReady(ball, ready);
    }

    private void SetUiCooldown(float normalizedRemaining)
    {
        if (_ui && ball) _ui.SetBallGrenadeCooldown(ball, normalizedRemaining);
    }
}
```

## Assets/Scripts/GrenadeProjectile.cs

```csharp
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class GrenadeProjectile : MonoBehaviour
{
    public struct Params
    {
        public float fuseSeconds;
        public float radius;
        public float maxPctAtCenter;
        public float minPctAtEdge;
        public float inheritVelocityFactor;
        public float upArcMin;
        public float upArcMax;
        public float linearDrag;
        public float angularDrag;
        public float bounciness;
        public float customGravityY;
        public Ball ownerBall;
    }

    private Params P;
    private Rigidbody rb;
    private float bornAt;
    private bool exploded;

    // Cached runtime material (for color / emission)
    private Material _mat;

    public void Init(Params p)
    {
        P = p;

        // Visual sphere (bigger & matches ball glow color)
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "GrenadeMesh";
        sphere.transform.SetParent(transform, false);
        sphere.transform.localScale = Vector3.one * 0.6f; // was 0.25f (larger now)
        var col = sphere.GetComponent<Collider>(); if (col) Destroy(col);

        var rend = sphere.GetComponent<MeshRenderer>();
        if (rend)
        {
            _mat = new Material(Shader.Find("Standard"));
            Color glowBase = (P.ownerBall && P.ownerBall.isActiveAndEnabled)
                ? P.ownerBall.GlowColor
                : new Color(1f, 0.35f, 0.15f);

            float emissiveIntensity = (P.ownerBall && P.ownerBall.isActiveAndEnabled)
                ? Mathf.Clamp(P.ownerBall.EmissionIntensityUI * 1.2f, 0.2f, 5f)
                : 1.5f;

            ConfigureStandardFade(_mat, new Color(glowBase.r, glowBase.g, glowBase.b, 0.85f));
            if (_mat.HasProperty("_EmissionColor"))
            {
                _mat.EnableKeyword("_EMISSION");
                _mat.SetColor("_EmissionColor", glowBase * Mathf.LinearToGammaSpace(emissiveIntensity));
            }
            rend.sharedMaterial = _mat;
        }

        var rootCol = gameObject.AddComponent<SphereCollider>();
        rootCol.radius = 0.3f;
        rootCol.material = new PhysicMaterial("GrenadePhysMat")
        {
            bounciness = Mathf.Clamp01(P.bounciness),
            bounceCombine = PhysicMaterialCombine.Maximum,
            frictionCombine = PhysicMaterialCombine.Average,
            dynamicFriction = 0.4f,
            staticFriction = 0.5f
        };

        rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.drag = Mathf.Clamp01(P.linearDrag);
        rb.angularDrag = Mathf.Clamp01(P.angularDrag);

        Vector3 inherit = Vector3.zero;
        if (P.ownerBall && P.ownerBall.isActiveAndEnabled)
        {
            var brb = P.ownerBall.GetComponent<Rigidbody>();
            if (brb) inherit = brb.velocity * Mathf.Clamp01(P.inheritVelocityFactor);
        }

        var planar = new Vector3(inherit.x, 0f, inherit.z);
        float speed = planar.magnitude;
        float refSpeed = P.ownerBall ? Mathf.Max(0.01f, P.ownerBall.maxSpeed) : 50f;
        float t = Mathf.InverseLerp(0f, refSpeed, speed);
        float up = Mathf.Lerp(P.upArcMin, P.upArcMax, t);
        rb.velocity = new Vector3(inherit.x, up, inherit.z);

        bornAt = Time.time;
        StartCoroutine(FuseCoroutine());
    }

    void FixedUpdate()
    {
        if (!rb) return;
        rb.AddForce(new Vector3(0f, P.customGravityY, 0f), ForceMode.Acceleration);
    }

    private IEnumerator FuseCoroutine()
    {
        float end = Time.time + Mathf.Max(0.05f, P.fuseSeconds);
        while (Time.time < end) yield return null;
        if (!exploded) Explode();
    }

    private void Explode()
    {
        exploded = true;

        float currentDamage = (P.ownerBall && P.ownerBall.isActiveAndEnabled) ? P.ownerBall.CurrentDamage : 0f;
        float currentFactor = (P.ownerBall && P.ownerBall.isActiveAndEnabled) ? P.ownerBall.ScoreXpDamageFactor : 1f;

        var pm = Pinball.Instance;
        if (pm)
        {
            pm.ScreenShakeGrenade();
            pm.PostFX?.BloomPulse(0.5f, 0.05f, 0.30f); // NEW bloom pulse
            pm.PostFX?.ChromaticPulse(0.30f, 0.05f, 0.22f);
        }

        SpawnRingVfx();
        SpawnExplosionLight(); // NEW yellow light flash

        var hits = Physics.OverlapSphere(transform.position, P.radius, ~0, QueryTriggerInteraction.Collide);
        if (hits != null && hits.Length > 0)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                var bumper = hits[i].GetComponent<Bumper>() ?? hits[i].GetComponentInParent<Bumper>();
                if (!bumper || !bumper.gameObject.activeInHierarchy || bumper.IsDead) continue;

                float d = Vector3.Distance(transform.position, bumper.transform.position);
                float nt = Mathf.Clamp01(d / Mathf.Max(0.0001f, P.radius));
                float pct = Mathf.Lerp(P.maxPctAtCenter, P.minPctAtEdge, nt);

                // CHANGED: 3x ball damage baseline, then apply falloff
                float dmg = (currentDamage * 3f) * pct;

                float xpFactor = currentFactor * 0.8f;

                bumper.TakeDamage(dmg, elemDmg: false, damageFactor: xpFactor);

                if (pm)
                {
                    int baseScore = bumper.type == BumperType.Small ? 50 : 100;
                    int scaled = Mathf.RoundToInt(baseScore * 0.8f);
                    pm.AddScore(Mathf.Max(1, scaled), 0, 0, xpFactor);
                }
            }
        }

        Destroy(gameObject);
    }

    private void SpawnRingVfx()
    {
        var ring = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ring.name = "GrenadeRingVFX";
        ring.transform.position = transform.position;
        var col = ring.GetComponent<Collider>(); if (col) col.isTrigger = true;

        var rend = ring.GetComponent<MeshRenderer>();
        if (rend)
        {
            var mat = new Material(Shader.Find("Standard"));
            ConfigureStandardFade(mat, new Color(1f, 0.85f, 0.2f, 0.75f));
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.2f) * Mathf.LinearToGammaSpace(2f));
            }
            rend.sharedMaterial = mat;
        }

        ring.transform.localScale = Vector3.one * 0.4f;
        var seq = DOTween.Sequence().SetUpdate(true);
        seq.Join(ring.transform.DOScale(Vector3.one * (P.radius * 2f), 0.25f).SetEase(Ease.OutQuad));
        if (rend) seq.Join(rend.material.DOFade(0f, 0.25f));
        seq.OnComplete(() => Destroy(ring));
    }

    // NEW: temporary yellow light flash
    private void SpawnExplosionLight()
    {
        var lightGO = new GameObject("GrenadeExplosionLight");
        lightGO.transform.position = transform.position + Vector3.up * 0.25f;
        var l = lightGO.AddComponent<Light>();
        l.color = new Color(1f, 0.9f, 0.3f);
        l.intensity = 0f;
        l.range = P.radius * 2.2f;
        l.shadows = LightShadows.None;

        DOTween.Sequence().SetUpdate(true)
            .Append(DOTween.To(() => l.intensity, v => l.intensity = v, 9f, 0.10f).SetEase(Ease.OutQuad))
            .Append(DOTween.To(() => l.intensity, v => l.intensity = v, 0f, 0.30f).SetEase(Ease.InQuad))
            .OnComplete(() => Destroy(lightGO));
    }

    private static void ConfigureStandardFade(Material m, Color c)
    {
        if (!m) return;
        m.SetFloat("_Mode", 2f);
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        m.SetColor("_Color", c);
    }
}
```

## Assets/Scripts/GrenadeRewardRuntime.cs

```csharp
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GrenadeRewardRuntime : MonoBehaviour
{
    [Header("Defaults")]
    public float DefaultCooldown = 10f;
    public float DefaultFuseSeconds = 2f;
    public float DefaultRadius = 6f;
    [Range(0.01f, 1f)] public float DefaultMaxPctAtCenter = 0.80f;
    [Range(0.01f, 1f)] public float DefaultMinPctAtEdge = 0.60f;

    [Header("Motion/Physics")]
    [Range(0f, 1f)] public float InheritVelocityFactor = 0.75f;
    [Range(0f, 1f)] public float LinearDrag = 0.28f;
    [Range(0f, 1f)] public float AngularDrag = 0.15f;
    [Range(0f, 1f)] public float Bounciness = 0.35f;
    [Min(0f)] public float UpArcMin = 2.5f;
    [Min(0f)] public float UpArcMax = 8.0f;
    public float CustomGravityY = -14f;

    private readonly Dictionary<Ball, GrenadeController> _controllers = new();

    void OnEnable()
    {
        Ball.OnBallActivated += HandleBallActivated;
        Ball.OnBallDeactivated += HandleBallDeactivated;
        RebindAll();
    }

    void OnDisable()
    {
        Ball.OnBallActivated -= HandleBallActivated;
        Ball.OnBallDeactivated -= HandleBallDeactivated;
        DestroyAllControllers();
    }

    public void RebindAll()
    {
        DestroyAllControllers();
        var balls = FindObjectsByType<Ball>(FindObjectsSortMode.None);
        for (int i = 0; i < balls.Length; i++)
        {
            var b = balls[i];
            if (b && b.isActiveAndEnabled && b.IsActive)
                AttachTo(b);
        }
    }

    public void ForceGlobalCooldown(float seconds)
    {
        foreach (var kv in _controllers)
            if (kv.Value) kv.Value.ForceCooldown(seconds);
    }

    private void HandleBallActivated(Ball b) => AttachTo(b);

    private void HandleBallDeactivated(Ball b)
    {
        if (!b) return;
        if (_controllers.TryGetValue(b, out var ctrl))
        {
            if (ctrl) Destroy(ctrl.gameObject);
            _controllers.Remove(b);
        }
    }

    private void AttachTo(Ball ball)
    {
        if (!ball || _controllers.ContainsKey(ball)) return;

        var go = new GameObject("GrenadeController (Ball)");
        go.transform.SetParent(ball.transform, false);
        var ctrl = go.AddComponent<GrenadeController>();
        ctrl.Bind(ball,
            DefaultCooldown, DefaultFuseSeconds, DefaultRadius,
            DefaultMaxPctAtCenter, DefaultMinPctAtEdge,
            InheritVelocityFactor, UpArcMin, UpArcMax,
            LinearDrag, AngularDrag, Bounciness, CustomGravityY);

        _controllers[ball] = ctrl;
    }

    private void DestroyAllControllers()
    {
        foreach (var kv in _controllers)
            if (kv.Value) Destroy(kv.Value.gameObject);
        _controllers.Clear();
    }
}
```

## Assets/Scripts/IDamageNumberSystem.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IDamageNumberSystem
{
    void Spawn(float amount, Vector3 position, Color? overrideColor = null);
}


/// Thin static facade so gameplay code can do: DamageNumbers.Spawn(...).
/// Keeps callers decoupled from the underlying UI/animation implementation.
public static class DamageNumbers
{
    /// Set once by the concrete implementation on startup.
    public static IDamageNumberSystem System { get; set; }

    public static bool IsReady => System != null;

    public static void Register(IDamageNumberSystem system) => System = system;

    public static void Spawn(float amount, Vector3 position, Color? overrideColor = null)
        => System?.Spawn(amount, position, overrideColor);
}

```

## Assets/Scripts/IPowerup.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPowerup
{
    string Id { get; }
    float Weight { get; }
    string DebugLabel { get; }
    bool CanTrigger(IRunContext ctx);
    void Execute(Pinball pm, Vector3 triggerPos);

}

```

## Assets/Scripts/IRunContext.cs

```csharp
using System.Collections.Generic;

public interface IRunContext
{

    bool IsActive(string rewardId);
    bool IsAvailable(string rewardId);
    bool Owns(string rewardId);
    bool HasExclusiveKeyActive(string key);

    int Lives { get; }
    int MaxLives { get; }

    void ApplyGrantedLives(int amount);

    void MarkOwned(string rewardId);
    void SetActive(string rewardId, bool on);
    void SetAvailable(string rewardId, bool on);
    void SetExclusive(string key, bool on);

    IEnumerable<string> ActiveKeys { get; }

    void ApplyScoreMultiplier(float multiplier, bool isCursed);
    void ApplyXPMultiplier(float multiplier, bool isCursed);

    void ApplyScoreBonusTime(float time, bool isCursed);
    void ApplyXPBonusTime(float time, bool isCursed);

    void ApplyShrinkFX(float size, float speed, float bounciness, float scoreMult, float bonusHits, int bounces, bool bonus, bool isCursed);
    void ApplyGrowFX(float size, float speed, float bounciness, float scoreMult, float bonusHits, int bounces, bool bonus, bool isCursed);

    void ApplyDamageFX(float amount);
    void ApplyDmgPerBounceFX(float damageMult, int bouncesNeeded);



    void ApplyXPForcefield(float radiusIncrease);

    void ApplyAdditionalBalls(int additionalBalls);

}
```

## Assets/Scripts/Item.cs

```csharp
public class Item
{
    public void Equip(CharacterStat c)
    {
        c.AddModifier(new StatModifier(10, StatModType.Flat, this));
        c.AddModifier(new StatModifier(.1f, StatModType.PercentMult, this));
    }

    public void Unequip(CharacterStat c)
    {
        c.RemoveAllModifiersFromSource(this);
    }

}

```

## Assets/Scripts/IXPNumberSystem.cs

```csharp
using UnityEngine;

public interface IXPNumberSystem
{
    void Spawn(int amount, Vector3 position, Color? overrideColor = null);
    void SpawnFollow(int amount, Transform follow, Vector3 localOffset, Color? overrideColor = null); // NEW
}

public static class XPNumbers
{
    public static IXPNumberSystem System { get; set; }
    public static bool IsReady => System != null;
    public static void Register(IXPNumberSystem system) => System = system;
    public static void Spawn(int amount, Vector3 position, Color? overrideColor = null)
        => System?.Spawn(amount, position, overrideColor);
    // NEW: follow a Transform (e.g., ball)
    public static void Spawn(int amount, Transform follow, Vector3 localOffset, Color? overrideColor = null)
        => System?.SpawnFollow(amount, follow, localOffset, overrideColor);
}

```

## Assets/Scripts/Level Up Rewards/BallDuplicateRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Ball Duplicate FX")]
public class BallDuplicateRewardSO : RewardSO
{

    [SerializeField] private int additionalBalls = 1;
    [SerializeField] private bool cursed = false;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyAdditionalBalls(additionalBalls);

    }

    public override bool IsEligible(IRunContext ctx)
    {
        // keep all global rules (ownership, stacking, exclusivity, etc.)
        if (!base.IsEligible(ctx))
            return false;


        if (cursed && ctx.Lives <= 1)
            return false;

        return true;
    }
}

```

## Assets/Scripts/Level Up Rewards/BallGrowRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Ball Grow")]
public class BallGrowRewardSO : RewardSO
{
    [SerializeField] private float size = 1f;
    [SerializeField] private float speed = 1f;
    [SerializeField] private float multiplier = 1f;
    [SerializeField] private float bonusHits = 1f;
    [SerializeField] private float bounciness = 1f;
    [SerializeField] private int bouncesForBonusHits = 1;
    [SerializeField] private bool bonus = false;
    [SerializeField] private bool cursed = false;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyGrowFX(size, speed, bounciness, multiplier, bonusHits, bouncesForBonusHits, bonus, cursed);

    }
}

```

## Assets/Scripts/Level Up Rewards/BallShrinkRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Ball Shrink")]
public class BallShrinkRewardSO : RewardSO
{
    [SerializeField] private float size = 1f;
    [SerializeField] private float speed = 1f;
    [SerializeField] private float multiplier = 1f;
    [SerializeField] private float bonusHits = 1f;
    [SerializeField] private float bounciness = 1f;
    [SerializeField] private int bouncesForBonusHits = 1;
    [SerializeField] private bool bonus = false;
    [SerializeField] private bool cursed = false;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyShrinkFX(size, speed, bounciness, multiplier, bonusHits, bouncesForBonusHits, bonus, cursed);

    }
}

```

## Assets/Scripts/Level Up Rewards/DamagePerBounceRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/DmgPerBounce")]
public class DamagePerBounceRewardSO : RewardSO
{
    [SerializeField] private float damageMult = 10f;
    [SerializeField] private int bouncesNeeded = 1;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyDmgPerBounceFX(damageMult, bouncesNeeded);

    }
}

```

## Assets/Scripts/Level Up Rewards/DamageRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Damage")]
public class DamageRewardSO : RewardSO
{
    [SerializeField] private float damageMult = 1f;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyDamageFX(damageMult);

    }
}

```

## Assets/Scripts/Level Up Rewards/EarthPaddleRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Earth Paddle FX")]
public class EarthPaddleRewardSO : RewardSO
{

    [SerializeField] private int fissureDamage = 1;
    [SerializeField] private float crustedDuration = 1f;
    [SerializeField] private float fissureHitScoreMultiplier = 1f;
    [SerializeField] private float fissureHitXPMultiplier = 1f;
    [SerializeField] private int bounceDuration = 1;
    [SerializeField] private bool cursed = false;


    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);
        isPaddleReward = true;
    }

    public override bool IsEligible(IRunContext ctx)
    {
        if(!base.IsEligible(ctx))
            return false;

        if (ctx is Pinball pb && pb.AreBothPaddlesElemental())
            return false;
        return true;
    }

    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        paddle.ApplyEarth(fissureDamage, crustedDuration, fissureHitScoreMultiplier, fissureHitXPMultiplier, bounceDuration, cursed);
    }
}

```

## Assets/Scripts/Level Up Rewards/ElectricPaddleRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Electric Paddle FX")]
public class ElectricPaddleRewardSO : RewardSO
{
    [SerializeField] private int shockDamage = 1;
    [SerializeField] private int chainCount = 1;
    [SerializeField] private int bounceDuration = 1;
    [SerializeField] private float xpBonus = 0.1f;
    [SerializeField] private float scoreBonus = 0.1f;
    [SerializeField] private bool cursed = false;
    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);
        isPaddleReward = true;


    }
    public override bool IsEligible(IRunContext ctx)
    {
        if (!base.IsEligible(ctx))
            return false;

        if (ctx is Pinball pb && pb.AreBothPaddlesElemental())
            return false;

        return true;
    }
    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        Debug.Log("nice!");
        paddle.ApplyElectric(shockDamage, chainCount, xpBonus, scoreBonus, bounceDuration, cursed);

    }
}

```

## Assets/Scripts/Level Up Rewards/FirePaddleRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Fire Paddle FX")]
public class FirePaddleRewardSO : RewardSO
{
    [SerializeField] private int bonusDamageFlat = 1;
    [SerializeField] private float burnDamage = 1f;
    [SerializeField] private float burnDuration = 1f;
    [SerializeField] private int explosionDamageFlat = 1;
    [SerializeField] private int bounceDuration = 1;
    [SerializeField] private float explosionSize = 1f;
    [SerializeField] private bool canExplode = false;
    [SerializeField] private bool cursed = false;



    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);
        isPaddleReward = true;


    }
    public override bool IsEligible(IRunContext ctx)
    {
        if (!base.IsEligible(ctx))
            return false;

        if (ctx is Pinball pb && pb.AreBothPaddlesElemental())
            return false;
        return true;
    }
    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        Debug.Log("nice!");
        paddle.ApplyFire(bonusDamageFlat, burnDamage, burnDuration, bounceDuration, canExplode, explosionSize, explosionDamageFlat, cursed);

    }

}

```

## Assets/Scripts/Level Up Rewards/GrenadeAbilityRewardSO.cs

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Grenade Ability")]
public sealed class GrenadeAbilityRewardSO : RewardSO
{
    [Header("Defaults (tunable)")]
    [SerializeField, Min(0.1f)] private float cooldownSeconds = 10f;
    [SerializeField, Min(0.05f)] private float fuseSeconds = 2f;
    [SerializeField, Min(0.1f)] private float radius = 6f;
    [SerializeField, Range(0.01f, 1f)] private float maxPctAtCenter = 0.80f;
    [SerializeField, Range(0.01f, 1f)] private float minPctAtEdge = 0.60f;
    [SerializeField, Range(0.1f, 1f)] private float inheritVelFactor = 0.75f;
    [SerializeField, Range(0f, 1f)] private float velocityDrag = 0.28f;
    [SerializeField, Range(0f, 1f)] private float angularDrag = 0.15f;
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.35f;
    [SerializeField, Min(0f)] private float upArcMin = 2.5f;
    [SerializeField, Min(0f)] private float upArcMax = 8.0f;

    [Header("Custom Gravity")]
    [SerializeField] private float customGravityY = -14f; // stronger downward accel (affects grenade only)

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);

        var pm = Pinball.Instance;
        if (!pm) return;

        var go = new GameObject("GrenadeRewardRuntime");
        go.transform.SetParent(pm.transform, false);
        var rt = go.AddComponent<GrenadeRewardRuntime>();
        rt.DefaultCooldown = Mathf.Max(0.1f, cooldownSeconds);
        rt.DefaultFuseSeconds = Mathf.Max(0.05f, fuseSeconds);
        rt.DefaultRadius = Mathf.Max(0.05f, radius);
        rt.DefaultMaxPctAtCenter = Mathf.Clamp01(maxPctAtCenter);
        rt.DefaultMinPctAtEdge = Mathf.Clamp01(minPctAtEdge);
        rt.InheritVelocityFactor = Mathf.Clamp01(inheritVelFactor);
        rt.LinearDrag = Mathf.Clamp01(velocityDrag);
        rt.AngularDrag = Mathf.Clamp01(angularDrag);
        rt.Bounciness = Mathf.Clamp01(bounciness);
        rt.UpArcMin = Mathf.Max(0f, upArcMin);
        rt.UpArcMax = Mathf.Max(upArcMin, upArcMax);
        rt.CustomGravityY = customGravityY;

        rt.RebindAll();
    }

    public override bool IsEligible(IRunContext ctx)
    {
        if (!base.IsEligible(ctx)) return false;
        return true;
    }
}
```

## Assets/Scripts/Level Up Rewards/LifeRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Life Reward", fileName = "LifeReward")]
public sealed class LifeRewardSO : RewardSO
{
    // Optional manual override; leave 0 to use rarity mapping below
    [SerializeField, Tooltip("Override grant amount. Leave 0 to use rarity mapping.")]
    private int overrideAmount = 0;

    // Map rarity -> lives granted: Rare=1, Epic=2, Legendary=3, Artifact=4
    private int Amount =>
        overrideAmount > 0 ? overrideAmount :
        Rarity switch
        {
            RewardRarity.Rare => 1,
            RewardRarity.Epic => 2,
            RewardRarity.Legendary => 3,
            RewardRarity.Artifact => 4,
            _ => 1
        };

    public override bool IsEligible(IRunContext ctx)
    {
        // keep all global rules (ownership, stacking, exclusivity, etc.)
        if (!base.IsEligible(ctx))
            return false;

        // lives must be known and not already capped
        if (ctx.MaxLives <= 0 || ctx.Lives >= ctx.MaxLives)
            return false;

        // never offer a life reward that would exceed max
        // e.g., at 4/5 only Amount=1 (Rare) is eligible
        if (ctx.Lives + Amount > ctx.MaxLives)
            return false;

        return true;
    }

    public override void Apply(IRunContext ctx)
    {
        // Pinball implements this; value is clamped there as well
        ctx.ApplyGrantedLives(Amount);
    }
}

```

## Assets/Scripts/Level Up Rewards/PortalWarpRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Portal Warp")]
public class PortalWarpRewardSO : RewardSO
{
    [Header("Portal Prefab")]
    [Tooltip("Cube-like visual. Untilted, no rotation at runtime.")]
    [SerializeField] private GameObject portalVisualPrefab;

    [Header("Raycast Layers")]
    [SerializeField] private LayerMask leftPlaneLayer;
    [SerializeField] private LayerMask rightPlaneLayer;
    [SerializeField] private LayerMask topPlaneLayer;

    [Header("Distances")]
    [SerializeField, Min(0.1f)] private float maxActiveDistance = 12f;
    [SerializeField, Min(0.01f)] private float triggerDistance = 1.25f;
    [SerializeField, Min(0.01f)] private float insideMargin = 0.35f;

    [Header("Impulse")]
    [SerializeField, Min(1f)] private float lateralImpulse = 28f;
    [SerializeField, Min(1f)] private float topImpulse = 30f;

    [Header("Cooldown (seconds)")]
    [SerializeField, Min(0.1f)] private float cooldownSeconds = 20f;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);

        var pm = Pinball.Instance;

        var go = new GameObject("PortalWarpRuntime");
        go.transform.SetParent(pm.transform, false);
        var runtime = go.AddComponent<PortalWarpRewardRuntime>();

        runtime.PortalVisualPrefab = portalVisualPrefab;
        runtime.LeftPlaneLayer = leftPlaneLayer;
        runtime.RightPlaneLayer = rightPlaneLayer;
        runtime.TopPlaneLayer = topPlaneLayer;

        runtime.MaxActiveDistance = maxActiveDistance;
        runtime.TriggerDistance = triggerDistance;
        runtime.InsideMargin = insideMargin;

        runtime.LateralImpulse = lateralImpulse;
        runtime.TopImpulse = topImpulse;
        runtime.postFX = pm.PostFX;

        runtime.CooldownSeconds = cooldownSeconds;

        // NEW: fix race with OnEnable by rebuilding controllers after config is set
        runtime.RebindAll();
    }
}
```

## Assets/Scripts/Level Up Rewards/ScoreMultiplierRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Score Multiplier")]
public class ScoreMultiplierRewardSO : RewardSO
{
    [SerializeField] private float multiplier = 1f;
    [SerializeField] private float bonusTime = 30f;
    [SerializeField] private bool cursed = false;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyScoreMultiplier(multiplier, cursed);
        ctx.ApplyScoreBonusTime(bonusTime, cursed);

    }
}

```

## Assets/Scripts/Level Up Rewards/WaterPaddleRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Water Paddle FX")]
public class WaterPaddleRewardSO : RewardSO
{
    [SerializeField] private float bonusXPPerc = 1f;
    [SerializeField] private int bonusDamageFlat = 1;
    [SerializeField] private float drenchDuration = 1f;
    [SerializeField] private int explosionDamageFlat = 1;
    [SerializeField] private int bounceDuration = 1;
    [SerializeField] private float explosionSize = 1f;
    [SerializeField] private bool canExplode = false;
    [SerializeField] private bool cursed = false;



    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, false);
        ctx.SetExclusive(ExclusivityKey, true);
        isPaddleReward = true;


    }

    public override bool IsEligible(IRunContext ctx)
    {
        if (!base.IsEligible(ctx))
            return false;

        if (ctx is Pinball pb && pb.AreBothPaddlesElemental())
            return false;
        return true;
    }

    public override void ApplyToPaddle(PaddleElementalState paddle)
    {
        Debug.Log("niceu!");
        paddle.ApplyWater(bonusXPPerc, bonusDamageFlat, drenchDuration, bounceDuration, canExplode, explosionSize, explosionDamageFlat, cursed);

    }

}

```

## Assets/Scripts/Level Up Rewards/XPGravityRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/XP Gravity")]
public class XPGravityRewardSO : RewardSO
{
    [SerializeField] private float radiusIncrease = 1f;


    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetAvailable(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyXPForcefield(radiusIncrease);

    }
}

```

## Assets/Scripts/Level Up Rewards/XPMultiplierRewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/XP Multiplier")]
public class XPMultiplierRewardSO : RewardSO
{
    [SerializeField] private float multiplier = 1f;
    [SerializeField] private float bonusTime = 30f;
    [SerializeField] private bool cursed = false;

    public override void Apply(IRunContext ctx)
    {
        ctx.MarkOwned(Id);
        ctx.SetActive(Id, true);
        ctx.SetExclusive(ExclusivityKey, true);

        ctx.ApplyXPMultiplier(multiplier, cursed);
        ctx.ApplyXPBonusTime(bonusTime, cursed);

    }
}

```

## Assets/Scripts/Mage.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mage : BaseCharacter
{

    protected float hpLvlIncrease = 4.9f;
    protected float minAtkLvlIncrease = 2.6f;
    protected float maxAtkLvlIncrease = 2.9f;

    protected float endLVLinc = 2.54f;
    protected float strLVLinc = 2.26f;
    protected float agiLVLinc = 1.69f;
    protected float witLVLinc = 4.65f;
    protected float chaLVLinc = 3.84f;

    public Mage(string name, int level)
        : base(name, "Mage", level, 487.8f, 120f, 51.9f, 56.2f, 100f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 10;
        traits[TraitType.Charm] = 6;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public Mage()
    : base("", "Mage", 5, 487.8f, 120f, 51.9f, 56.2f, 100f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 9;
        traits[TraitType.Charm] = 6;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);
    }
}

```

## Assets/Scripts/NukeBumpersPowerup.cs

```csharp
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NukeBumpersPowerup : IPowerup
{
    public string Id => "nuke-bumpers";
    public float Weight => 0.6f;
    public string DebugLabel => "Nuke Bumpers";

    // Always eligible; pickup roll occurs only during active play.
    public bool CanTrigger(IRunContext ctx) => true;

    // Deals percentage damage to all bumpers; awards score and XP at 75% of the ball�s current damage factor for balance.
    public void Execute(Pinball pinball, Vector3 triggerPos)
    {
        if (!pinball) return;

        // Use any active ball�s factor; fall back to 1x. Then apply the 0.75 debuff for balance.
        float damageFactor = 1f;
        var anchor = pinball.ball;
        if (anchor && anchor.isActiveAndEnabled && anchor.IsActive) damageFactor = anchor.ScoreXpDamageFactor;
        else
        {
            var any = Object.FindObjectsOfType<Ball>();
            for (int i = 0; i < any.Length; i++)
            {
                if (any[i] && any[i].isActiveAndEnabled && any[i].IsActive)
                {
                    damageFactor = any[i].ScoreXpDamageFactor;
                    break;
                }
            }
        }
        const float NUKE_DEBUFF = 0.75f;
        float awardFactor = Mathf.Max(0f, damageFactor * NUKE_DEBUFF);

        const float percent = 0.35f;  // 35% of current HP
        const float minDamage = 10f;

        foreach (var bumper in Bumper.EnumerateAll())
        {
            if (!bumper) continue;

            // Apply damage (XP handled inside TakeDamage via passed factor).
            float amount = Mathf.Max(minDamage, bumper.curHealth * percent);
            bumper.TakeDamage(amount, elemDmg: false, damageFactor: awardFactor);

            // Award score per affected bumper, using tiered base similar to bumpers, scaled by awardFactor.
            int baseScore = bumper.type == BumperType.Small ? 50 : 100;
            pinball.AddScore(baseScore, bumpCount: 0, bumpCountConsec: 0, damageFactor: awardFactor);
        }

        pinball.ScreenShake();
    }
}
```

## Assets/Scripts/PaddleEffectData.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PaddleEffectData
{
    public readonly PaddleState Element;

    // Fire fields (extend later for other elements)
    public readonly int FireBonusDamage;
    public readonly float FireBurnDamage;
    public readonly float FireBurnDuration;
    public readonly int FireBounceDuration;
    public readonly bool FireCanExplode;
    public readonly float FireExplosionSize;
    public readonly int FireExplosionDamageFlat;
    public readonly bool FireIsCursed;

    // Water fields (extend later for other elements)
    public readonly float WaterBonusXP;
    public readonly int WaterDamageFlat;
    public readonly float WaterDrenchDuration;
    public readonly int WaterBounceDuration;
    public readonly bool WaterCanBurst;
    public readonly float WaterBurstSize;
    public readonly int WaterBurstDamageFlat;
    public readonly bool WaterIsCursed;

    public readonly int EarthBonusDamage;
    public readonly float EarthFissureDuration;
    public readonly float EarthXPBonus;
    public readonly float EarthScoreBonus;
    public readonly int EarthBounceDuration;
    public readonly bool EarthIsCursed;

    public readonly int ElectricShockDamage;
    public readonly int ElectricChainCount;
    public readonly float ElectricXPBonus;
    public readonly float ElectricScoreBonus;
    public readonly int ElectricBounceDuration;
    public readonly bool ElectricIsCursed;

    public PaddleEffectData(
        PaddleState element,
        int fireBonusDamage = 0,
        float fireBurnDamage = 0f,
        float fireBurnDuration = 0f,
        int fireBounceDuration = 0,
        bool fireCanExplode = false,
        float fireExplosionSize = 0f,
        int fireExplosionDamageFlat = 0,
        bool fireIsCursed = false,

        float waterBonusXP = 0,
        int waterDamageFlat = 0,
        float waterDrenchDuration = 0f,
        int waterBounceDuration = 0,
        bool waterCanBurst = false,
        float waterBurstSize = 0f,
        int waterBurstDamageFlat = 0,
        bool waterIsCursed = false,

                int earthBonusDamage = 0,
        float earthFissureDuration = 0f,
        float earthXPBonus = 0f,
        float earthScoreBonus = 0f,
        int earthBounceDuration = 0,
        bool earthIsCursed = false,
        
        int electricShockDamage = 0,
        int electricChainCount = 0,
        float electricXPBonus = 0f,
        float electricScoreBonus = 0f,
        int electricBounceDuration = 0,
        bool electricIsCursed = false
        )
    {
        Element = element;
        FireBonusDamage = fireBonusDamage;
        FireBurnDamage = fireBurnDamage;
        FireBurnDuration = fireBurnDuration;
        FireBounceDuration = fireBounceDuration;
        FireCanExplode = fireCanExplode;
        FireExplosionSize = fireExplosionSize;
        FireExplosionDamageFlat = fireExplosionDamageFlat;
        FireIsCursed = fireIsCursed;

        WaterBonusXP = waterBonusXP;
        WaterDamageFlat = waterDamageFlat;
        WaterDrenchDuration = waterDrenchDuration;
        WaterBounceDuration = waterBounceDuration;
        WaterCanBurst = waterCanBurst;
        WaterBurstSize = waterBurstSize;
        WaterBurstDamageFlat = waterBurstDamageFlat;
        WaterIsCursed = waterIsCursed;

        EarthBonusDamage = earthBonusDamage;
        EarthFissureDuration = earthFissureDuration;
        EarthXPBonus = earthXPBonus;
        EarthScoreBonus = earthScoreBonus;
        EarthBounceDuration = earthBounceDuration;
        EarthIsCursed = earthIsCursed;

        ElectricShockDamage = electricShockDamage;
        ElectricChainCount = electricChainCount;
        ElectricXPBonus = electricXPBonus;
        ElectricScoreBonus = electricScoreBonus;
        ElectricBounceDuration = electricBounceDuration;
        ElectricIsCursed = electricIsCursed;
    }
}

```

## Assets/Scripts/PaddleElementalState.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PaddleElementalState : MonoBehaviour
{
    [SerializeField]
    private PaddleState initialState = PaddleState.None;

    public PaddleState CurrentState = PaddleState.None;



    public int FireBonusDamage { get; private set; }
    public int FireBounceDuration { get; private set; }
    public float FireBurnDamage { get; private set; }
    public float FireBurnDuration { get; private set; }
    public bool FireCanExplode { get; private set; }
    public float FireExplosionSize { get; private set; }
    public int FireExplosionDamageFlat { get; private set; }
    public bool FireIsCursed { get; private set; }

    public float WaterBonusXP { get; private set; }
    public int WaterBonusDamage { get; private set; }
    public float WaterDrenchDuration { get; private set; }
    public int WaterBounceDuration { get; private set; }
    public bool WaterCanBurst { get; private set; }
    public float WaterBurstSize { get; private set; }
    public int WaterBurstDamageFlat { get; private set; }
    public bool WaterIsCursed { get; private set; }

    public int EarthFissureDamage { get; private set; }
    public float EarthCrustDuration { get; private set; }
    public float EarthXPBonus { get; private set; }
    public float EarthScoreBonus { get; private set; }
    public int EarthBounceDuration { get; private set; }
    public bool EarthIsCursed { get; private set; }

    public int ElectricShockDamage { get; private set; }
    public int ElectricChainCount { get; private set; }
    public float ElectricXPBonus { get; private set; }
    public float ElectricScoreBonus { get; private set; }
    public int ElectricBounceDuration { get; private set; }
    public bool ElectricIsCursed { get; private set; }


    void Start()
    {
        CurrentState = initialState;
    }

    public void SetPaddleState(PaddleState newState)
    {
        if (newState == PaddleState.None || CurrentState == newState)
            return;
        switch (newState)
            {
                case PaddleState.Fire:
                    CurrentState = PaddleState.Fire;
                    break;
                case PaddleState.Water:
                    CurrentState = PaddleState.Water;
                    break;
                case PaddleState.Earth:
                    CurrentState = PaddleState.Earth;
                    break;
            case PaddleState.Electric:
                    CurrentState = PaddleState.Electric;
                    break;
                default:
                    CurrentState = PaddleState.None;
                    break;
        }
    }

    public void StoreFireData(int bonusDamage, float burnDamage, float burnDuration, int bounceDuration, bool canExplode, float explosionSize, int explosionDamageFlat, bool cursed)
    {
        FireBonusDamage = bonusDamage;
        FireBurnDamage = burnDamage;
        FireBurnDuration = burnDuration;
        FireBounceDuration = bounceDuration;
        FireCanExplode = canExplode;
        FireExplosionSize = explosionSize;
        FireExplosionDamageFlat = explosionDamageFlat;
        FireIsCursed = cursed;
    }

    public void StoreWaterData(float bonusXP, int bonusDamage, float drenchDuration, int bounceDuration, bool canBurst, float burstSize, int burstDamageFlat, bool cursed)
    {
        WaterBonusXP = bonusXP;
        WaterBonusDamage = bonusDamage;
        WaterDrenchDuration = drenchDuration;
        WaterBounceDuration = bounceDuration;
        WaterCanBurst = canBurst;
        WaterBurstSize = burstSize;
        WaterBurstDamageFlat = burstDamageFlat;
        WaterIsCursed = cursed;
        WaterBounceDuration = bounceDuration;
        WaterIsCursed = cursed;
    }

    public void StoreEarthData(int bonusDamage, float crustDuration, float xpBonus, float scoreBonus, int bounceDuration, bool cursed)
    {
        EarthFissureDamage = bonusDamage;
        EarthCrustDuration = crustDuration;
        EarthXPBonus = xpBonus;
        EarthScoreBonus = scoreBonus;
        EarthBounceDuration = bounceDuration;
        EarthIsCursed = cursed;
    }

    public void StoreElectricData(int shockDamage, int chainCount, float xpBonus, float scoreBonus, int bounceDuration, bool cursed)
    {
        ElectricShockDamage = shockDamage;
        ElectricChainCount = chainCount;
        ElectricXPBonus = xpBonus;
        ElectricScoreBonus = scoreBonus;
        ElectricBounceDuration = bounceDuration;
        ElectricIsCursed = cursed;
    }
    public void ApplyFire(int bonusDamageFlat, float burnDamage, float burnDur, int bounceDur, bool canExplode, float explosionSize, int explosionDamageFlat, bool cursed)
    {
        SetPaddleState(PaddleState.Fire);
        StoreFireData(bonusDamageFlat, burnDamage, burnDur, bounceDur, canExplode, explosionSize, explosionDamageFlat, cursed);
        // TODO: spawn paddle fire VFX/SFX here
    }

    public void ApplyWater(float bonusXP, int bonusDamage, float drenchDuration, int bounceDuration, bool canBurst, float burstSize, int burstDamageFlat, bool cursed)
    {
        SetPaddleState(PaddleState.Water);
        StoreWaterData(bonusXP, bonusDamage, drenchDuration, bounceDuration, canBurst, burstSize, burstDamageFlat, cursed);
    }

    public void ApplyEarth(int fissureDamage, float crustDuration, float xpBonus, float scoreBonus, int bounceDuration, bool cursed)
    {
        SetPaddleState(PaddleState.Earth);
        StoreEarthData(fissureDamage, crustDuration, xpBonus, scoreBonus, bounceDuration, cursed);
    }

    public void ApplyElectric(int shockDamage, int chainCount, float xpBonus, float scoreBonus, int bounceDuration, bool cursed)
    {
        SetPaddleState(PaddleState.Electric);
        StoreElectricData(shockDamage, chainCount, xpBonus, scoreBonus, bounceDuration, cursed);
    }

    public PaddleEffectData GetEffectData()
    {
        return new PaddleEffectData(
            CurrentState,
            FireBonusDamage,
            FireBurnDamage,
            FireBurnDuration,
            FireBounceDuration,
            FireCanExplode,
            FireExplosionSize,
            FireExplosionDamageFlat,
            FireIsCursed,

            WaterBonusXP,
            WaterBonusDamage,
            WaterDrenchDuration,
            WaterBounceDuration,
            WaterCanBurst,
            WaterBurstSize,
            WaterBurstDamageFlat,
            WaterIsCursed,

            EarthFissureDamage,
            EarthCrustDuration,
            EarthXPBonus,
            EarthScoreBonus,
            EarthBounceDuration,
            EarthIsCursed,
            
            ElectricShockDamage,
            ElectricChainCount,
            ElectricXPBonus,
            ElectricScoreBonus,
            ElectricBounceDuration,
            ElectricIsCursed);
    }

}

```

## Assets/Scripts/PaddleElements.cs

```csharp
using UnityEngine;

public enum PaddleState
{
    None,
    Fire,
    Water,
    Earth,
    Air,
    Electric
}
```

## Assets/Scripts/PadLightFX.cs

```csharp
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
```

## Assets/Scripts/Pinball.cs

```csharp
using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// Pinball gameplay state machine
public enum PinballState
{
    None,
    Charging,
    Push,
    Play,
    LevelUp,
    PaddleSelect,
    SkillAim,
    ResetBall,
    GameOver
}

[DisallowMultipleComponent]
public class Pinball : MonoBehaviour, IRunContext
{
    public static Pinball Instance { get; private set; }

    [Header("XP Forcefield")]
    [SerializeField, Tooltip("Global base scale applied to each ball's XP forcefield baseline.")]
    private float xpBaseRadiusScale = 1f;
    public float XPBaseRadiusScale => xpBaseRadiusScale;

    #region Reward / RunContext
    private readonly HashSet<string> owned = new();
    private readonly HashSet<string> active = new();
    private readonly HashSet<string> available = new();
    private readonly HashSet<string> exclusiveKeysActive = new();

    public bool Owns(string rewardId) => owned.Contains(rewardId);
    public bool IsActive(string rewardId) => active.Contains(rewardId);
    public bool IsAvailable(string rewardId) => available.Contains(rewardId);
    public bool HasExclusiveKeyActive(string key) =>
        !string.IsNullOrEmpty(key) && exclusiveKeysActive.Contains(key);
    public IEnumerable<string> ActiveKeys => exclusiveKeysActive;

    public void MarkOwned(string rewardId) => owned.Add(rewardId);
    public void SetActive(string rewardId, bool on) { if (on) active.Add(rewardId); else active.Remove(rewardId); }
    public void SetAvailable(string rewardId, bool on) { if (on) available.Add(rewardId); else available.Remove(rewardId); }
    public void SetExclusive(string key, bool on)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (on) exclusiveKeysActive.Add(key); else exclusiveKeysActive.Remove(key);
    }

    private static readonly Dictionary<RewardRarity, float> rarityWeights = new()
    {
        {RewardRarity.Common, 88f},
        {RewardRarity.Uncommon, 52f},
        {RewardRarity.Rare, 25f},
        {RewardRarity.Epic, 12f},
        {RewardRarity.Legendary, 3f},
        {RewardRarity.Artifact, .5f},
        {RewardRarity.Cursed, .5f},
    };
    #endregion

    #region Serialized configuration
    [Header("Input")]
    [SerializeField] private KeyCode chargeKey = KeyCode.Space;
    [SerializeField] private KeyCode leftPaddleKey = KeyCode.A;
    [SerializeField] private KeyCode rightPaddleKey = KeyCode.D;

    [Header("Progression")]
    [Min(0)] public float curXP = 0;
    [Min(1)] public float maxXP;
    public int level { get; private set; } = 1;
    private float lastBaseXPMultApplied = 1f;
    private float lastBaseScoreMultApplied = 1f;

    private Tween _timeScaleTween, _fovTween;

    [Header("Debug / Portal Reset")]
    [SerializeField, Min(0f)] private float portalResetDebugCooldown = 0.75f;

    [Header("Skill Aim (Green Pad)")]
    [SerializeField] private LineRenderer aimLine;
    [SerializeField] private float camAimFov = 38f;
    [SerializeField] private float camFovTween = 0.12f;
    [SerializeField] private CameraFollowSimple camFollow;
    private readonly object _skillAimSlowmoToken = new object();

    private Ball _aimBall;
    private Transform _aimFocus;
    private float _aimWindowDur;
    private float _aimTimeLeft;
    private float _aimMinSpeed, _aimMaxSpeed;
    private float _preAimFov;
    private float _preAimTimeScale = 1f;
    private float _targetSlowMo = 0.15f;
    private float _nextHitFactor = 2f;
    private int _nextHitBounces = 1;

    private const float SkillAimUncapDuration = .5f;
    private const float SkillAimUncapMaxSpeed = 100f;

    [SerializeField] private float aimFollowDistance = 8f;
    [SerializeField] private float aimFollowTween = 0.12f;
    [SerializeField] private float aimLineMaxLen = 0.5f;

    private Vector3 _lastAimDir = Vector3.forward;
    private Transform _camDefaultTarget;
    private float _camDefaultFollowDist;
    private float _camDefaultHeight;
    private float _camDefaultFov;
    private Vector3 _camDefaultCamPos;
    private Quaternion _camDefaultCamRot;
    private Vector3 _preAimCamPos;
    private Quaternion _preAimCamRot;
    private Tween _camDistTween, _camPosTween, _camRotTween;
    private Transform _preAimCamTarget;
    private float _preAimFollowDist;
    private Transform _aimCamTarget;

    [Header("Skill Aim PostFX")]
    [SerializeField] private PostFXController postFX;
    public PostFXController PostFX => postFX;
    [SerializeField, Range(0f, 1f)] private float vignetteMax = 0.55f;

    [Header("References")]
    [SerializeField] public Ball ball;
    [SerializeField] private PinballUIM ui;
    [SerializeField] private GameObject ballClonePrefab;
    [SerializeField] private GameObject invisWalls;
    [SerializeField] private PinballFlipper leftPaddle;
    [SerializeField] private PinballFlipper rightPaddle;

    [Header("Charge/Push Tuning")]
    [SerializeField, Min(0.05f)] private float chargeMaxCharging = 1.5f;
    [SerializeField, Min(0.05f)] private float chargeMaxPush = 1.0f;
    [SerializeField, Range(0f, 1f)] private float minChargeToLaunch = 0.10f;
    [SerializeField, Range(0f, 1f)] private float chargeToEnterPush = 0.65f;

    [Header("Balls / Lives")]
    [SerializeField, Range(0, 99)] private int startingLives = 3;
    [SerializeField, Range(1, 99)] private int maxLives = 5;
    public int Lives => lives;
    public int MaxLives => maxLives;

    [Header("Score/XP Multipliers")]
    [SerializeField, Tooltip("Base (floor) score multiplier that increases with leveling.")]
    private float baseScoreMult = 1f;
    [SerializeField, Tooltip("Base (floor) XP multiplier that increases with leveling.")]
    private float baseXPMult = 1f;

    [Header("Powerups & Drops")]
    [SerializeField, Range(0f, 1f)]
    public float PowerupDropChance = .03f;

    [Header("Visual FX")]
    [SerializeField] private Camera mainCam;
    [SerializeField, Min(0f)] private float shakeDuration = 0.15f;
    [SerializeField, Min(0f)] private float shakeStrength = 0.3f;
    [SerializeField, Min(1)] private int shakeVibrato = 12;
    [SerializeField, Range(0f, 180f)] private float shakeRandomness = 90f;
    [SerializeField] private ParticleSystem xpFXPrefab;

    [Header("UX/UI")]
    [SerializeField] private BallXPBar ballXPScript;

    [Header("Bumper Death FX")]
    [SerializeField] private float explosionForce = 50f;
    [SerializeField] private float explosionRadius = 3f;

    [Header("Global Physics")]
    [SerializeField] private bool overrideGlobalGravity = true;
    [SerializeField] private Vector3 gravityOverride = new Vector3(0f, 0f, -19.62f);

    [Header("LevelUp Flow")]
    [SerializeField, Tooltip("If a Level Up occurs during SkillAim, wait this long (unscaled) after aim ends before showing rewards.")]
    private float postAimLevelUpDelay = 0.20f;
    private bool _queueLevelUpAfterAim;

    [Header("Post-LevelUp Resume SlowMo")]
    [SerializeField] private bool enableResumeSlowMo = true;
    [SerializeField, Tooltip("Initial timeScale when resuming from LevelUp (0.05..1). Lower = stronger slow-mo.")]
    [Range(0.05f, 1f)] private float resumeSlowMoStartScale = 0.18f;
    [SerializeField, Tooltip("Hold time (unscaled) at the strongest slow-mo before easing back to normal.")]
    [Min(0f)] private float resumeSlowMoHold = 0.30f;
    [SerializeField, Tooltip("Ease-out time (unscaled) to return to normal timeScale.")]
    [Min(0.05f)] private float resumeSlowMoEase = 0.30f;
    private readonly object _postLevelUpSlowmoToken = new object();
    private Tween _resumeSlowmoTween;
    #endregion

    #region Runtime state
    private PinballState currentState;
    public PinballState CurrentState => currentState;

    private bool isHoldingCharge;
    private float chargeTimer;
    public float chargePercentage { get; private set; }
    private float chargeMax;

    private readonly Queue<SkillAimRequest> _aimQueue = new();
    private struct SkillAimRequest
    {
        public Ball ball;
        public Transform focus;
        public float slowMo;
        public float windowUnscaled;
        public float minSpeed;
        public float maxSpeed;
        public float nextHitFactor;
        public int nextHitBounces;
        public SkillAimRequest(Ball b, Transform f, float s, float w, float minV, float maxV, float nf, int nb)
        {
            ball = b; focus = f; slowMo = s; windowUnscaled = w; minSpeed = minV; maxSpeed = maxV; nextHitFactor = nf; nextHitBounces = nb;
        }
    }

    private int score;
    private int mult;
    private float scoreMultiplier = 1f;
    private float xpMultiplier = 1f;
    private float scoreBonusTimer;
    private float xpBonusTimer;
    public int Score => score;
    public int Mult => mult;
    public float ScoreMultiplier => scoreMultiplier;
    public bool IsScoreMultiplierActive => scoreMultiplier > 1f;
    public float ScoreBonusTimeRemaining => scoreBonusTimer;
    public float XPMultiplier => xpMultiplier;
    public bool IsXPMultiplierActive => xpMultiplier > 1f;
    public float XPBonusTimeRemaining => xpBonusTimer;
    private float finalXPCalculated;

    public float FinalXPCalculated => finalXPCalculated;

    private bool canHitL, canHitR, hasBeenHitL, hasBeenHitR;

    private readonly List<Ball> liveBalls = new();
    private int _primaryBallId;
    public int ballCount { get; private set; }
    private int lives;

    [SerializeField, Min(0f)] private float noBallResetGrace = 0.20f;
    private float _noBallTimer;

    private Vector3 _ballStartPos;
    private Quaternion _ballStartRot;
    private int pendingLevelUps = 0;
    private RewardSO pendingPaddleReward;
    private readonly List<RewardSO> rewardPool = new();

    public bool destroyedBumperBonusActive;
    private float destroyedBumperScoreMult = 2f;
    private int xpCount = 3;
    private bool wallTimerStart;
    private float wallTimer;
    private float _defaultFixedDeltaTime;
    public float DefaultFixedDeltaTime => _defaultFixedDeltaTime;

    private float _lastTimeScaleCheck;

    private int bumperBouncesS;
    private int bumperBouncesG;
    private bool extraHitsS;
    private bool extraHitsG;
    private float bonusHitsS;
    private float bonusHitsG;
    private int bouncesForBonusS;
    private int bouncesForBonusG;

    private const float X2_MULT_MAGIC = 100f;
    private const float X4_MULT_MAGIC = 200f;

    public float Damage = 5f;
    #endregion

    #region Helper (rounding)
    private static float Round2(float v) => Mathf.Round(v * 100f) / 100f;
    #endregion

    #region Unity lifecycle
    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _defaultFixedDeltaTime = Time.fixedDeltaTime;
        TimeScaleHub.EnsureInitialized(_defaultFixedDeltaTime);
        if (overrideGlobalGravity)
            Physics.gravity = gravityOverride;
        DOTween.SetTweensCapacity(500, 50);
        if (ball != null)
        {
            RegisterBall(ball);
            _ballStartPos = ball.transform.position;
            _ballStartRot = ball.transform.rotation;
            _primaryBallId = ball.GetInstanceID();
        }
        maxXP = XPFormula.XpReq(level);
        maxLives = Mathf.Max(1, maxLives);
        startingLives = Mathf.Clamp(startingLives, 0, maxLives);
        lives = startingLives;
        lastBaseXPMultApplied = baseXPMult;
        lastBaseScoreMultApplied = baseScoreMult;

        xpBaseRadiusScale = Mathf.Max(0.0001f, xpBaseRadiusScale);

        if (camFollow)
        {
            _camDefaultTarget = camFollow.Target;
            _camDefaultFollowDist = camFollow.FollowDistance;
            _camDefaultHeight = camFollow.Height;
        }
        if (mainCam)
        {
            _camDefaultFov = mainCam.fieldOfView;
            _camDefaultCamPos = mainCam.transform.position;
            _camDefaultCamRot = mainCam.transform.rotation;
        }
    }

    private async void Start()
    {
        var loader = Addressables.LoadAssetsAsync<RewardSO>("Rewards", rewardPool.Add);
        await loader.Task;

        ui?.InitLives(maxLives);
        OnLivesChanged();

        PowerupSystem.EnsureInitialized();
        ChangeState(PinballState.Charging);

        ui?.Init(this);
        if (invisWalls) invisWalls.SetActive(false);

        ballCount = 1;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RestoreFromSkillAim(false);
        Time.timeScale = 1f;
        Time.fixedDeltaTime = _defaultFixedDeltaTime;
    }

    private void OnValidate()
    {
        if (maxLives < 1) maxLives = 1;
        if (startingLives > maxLives) startingLives = maxLives;
        if (baseScoreMult < 1f) baseScoreMult = 1f;
        if (baseXPMult < 1f) baseXPMult = 1f;
        if (noBallResetGrace < 0f) noBallResetGrace = 0f;
        if (chargeToEnterPush < minChargeToLaunch)
            chargeToEnterPush = Mathf.Clamp01(Mathf.Max(minChargeToLaunch, chargeToEnterPush));
    }

    private void Update()
    {
        HandlePendingLevelUps();
        HandlePaddles();
        ClampBaseMultipliers();
        MaybeEnableInvisibleWalls();
        HandleGameOverRestart();
        UpdateMultipliersTimers();

        switch (currentState)
        {
            case PinballState.Play:
                HandlePlayStateDrain();
                break;
            case PinballState.Charging:
            case PinballState.Push:
                HandleChargingAndPush();
                break;
        }

        if (CurrentState == PinballState.SkillAim)
        {
            UpdateSkillAim();
            return;
        }

        if (Input.GetKeyDown("p"))
            AddXP(50f);

        TickChargeTimer();

        if (Time.timeScale < 0.99f &&
            (currentState == PinballState.Play || currentState == PinballState.Charging || currentState == PinballState.Push) &&
            !TimeScaleHub.IsAnyActive &&
            currentState != PinballState.SkillAim &&
            currentState != PinballState.LevelUp &&
            currentState != PinballState.PaddleSelect)
        {
            if (Time.unscaledTime - _lastTimeScaleCheck > 0.25f)
            {
                _lastTimeScaleCheck = Time.unscaledTime;
                NormalizeTimeScaleIfNeeded();
            }
        }
        EnsureNormalTimescaleIfNotAiming();
    }
    #endregion

    #region State machine
    public void ChangeState(PinballState newState)
    {
        if (currentState == newState) return;
        ExitState(currentState);
        currentState = newState;
        EnterState(newState);
    }

    private void EnterState(PinballState state)
    {
        switch (state)
        {
            case PinballState.Charging:
                ResetChargeState(max: chargeMaxCharging);
                NormalizeTimeScaleIfNeeded();
                break;
            case PinballState.Push:
                ResetChargeState(max: chargeMaxPush);
                NormalizeTimeScaleIfNeeded();
                break;
            case PinballState.Play:
                ui?.DefaultUI();
                chargePercentage = 0;
                chargeTimer = 0;
                Time.timeScale = 1f;
                Time.fixedDeltaTime = _defaultFixedDeltaTime;
                wallTimerStart = true;
                break;
            case PinballState.LevelUp:
                CancelAllPortalSlowmo();
                TimeScaleHub.BeginPause(this);
                LevelUp();
                var choices = GetRewardChoices();
                ui?.ShowRewardPopup(choices);
                Time.timeScale = 0f;
                Time.fixedDeltaTime = _defaultFixedDeltaTime;
                break;
            case PinballState.PaddleSelect:
                CancelAllPortalSlowmo();
                TimeScaleHub.BeginPause(this);
                ui?.PaddleSelect();
                Time.timeScale = 0f;
                Time.fixedDeltaTime = _defaultFixedDeltaTime;
                break;
            case PinballState.SkillAim:
                break;
            case PinballState.ResetBall:
                lives = Mathf.Max(0, lives - 1);
                OnLivesChanged();
                ResetBallAndFlow();
                break;
            case PinballState.GameOver:
                leftPaddle = null;
                rightPaddle = null;
                NormalizeTimeScaleIfNeeded();
                break;
        }
    }

    private void ExitState(PinballState state)
    {
        switch (state)
        {
            case PinballState.LevelUp:
            case PinballState.PaddleSelect:
                TimeScaleHub.EndPause(this);
                NormalizeTimeScaleIfNeeded();
                break;
            case PinballState.SkillAim:
                RestoreFromSkillAim();
                NormalizeTimeScaleIfNeeded();
                break;
        }
    }

    private void ResetChargeState(float max)
    {
        isHoldingCharge = false;
        chargePercentage = 0f;
        chargeTimer = 0f;
        chargeMax = Mathf.Max(0.05f, max);
    }
    #endregion

    #region UI & Lives
    private void OnLivesChanged()
    {
        ui?.UpdateLives(lives, maxLives);
    }
    #endregion

    #region Input / helpers
    private void HandlePendingLevelUps()
    {
        while (curXP >= maxXP)
        {
            pendingLevelUps++;
            curXP -= maxXP;
        }

        // Don't interrupt SkillAim; queue LevelUp until aim completes.
        if (pendingLevelUps > 0)
        {
            if (CurrentState == PinballState.SkillAim)
            {
                _queueLevelUpAfterAim = true;
                return;
            }

            if (CurrentState != PinballState.LevelUp && CurrentState != PinballState.PaddleSelect)
                StartNextLevelUp();
        }
    }

    private void HandlePaddles()
    {
        if (leftPaddle != null)
        {
            leftPaddle.PaddleMovement(Input.GetKey(leftPaddleKey));
            var elem = leftPaddle.GetComponent<PaddleElementalState>();
            bool hasElem = elem != null && elem.CurrentState != PaddleState.None;

            if (hasElem && Input.GetKeyDown(leftPaddleKey))
                HitCheck(leftPaddleKey);

            var targetBall = GetPaddleTarget();
            if (hasElem && targetBall != null && targetBall.IsTouchingPaddles &&
                (Input.GetKey(leftPaddleKey) || canHitL) && !hasBeenHitL)
            {
                hasBeenHitL = true;
                StartCoroutine(ResetLBumper(0.4f));
                targetBall.OnPaddleHit(elem.GetEffectData());
            }
        }

        if (rightPaddle != null)
        {
            rightPaddle.PaddleMovement(Input.GetKey(rightPaddleKey));
            var elem = rightPaddle.GetComponent<PaddleElementalState>();
            bool hasElem = elem != null && elem.CurrentState != PaddleState.None;

            if (hasElem && Input.GetKeyDown(rightPaddleKey))
                HitCheck(rightPaddleKey);

            var targetBall = GetPaddleTarget();
            if (hasElem && targetBall != null && targetBall.IsTouchingPaddles &&
                (Input.GetKey(rightPaddleKey) || canHitR) && !hasBeenHitR)
            {
                hasBeenHitR = true;
                StartCoroutine(ResetRBumper(0.4f));
                targetBall.OnPaddleHit(elem.GetEffectData());
            }
        }
    }

    private void ClampBaseMultipliers()
    {
        if (!Mathf.Approximately(baseXPMult, lastBaseXPMultApplied))
        {
            float gap = xpMultiplier - lastBaseXPMultApplied;
            xpMultiplier = Mathf.Max(baseXPMult, baseXPMult + gap);
            lastBaseXPMultApplied = baseXPMult;
        }
        else if (xpMultiplier < baseXPMult)
        {
            xpMultiplier = baseXPMult;
        }

        if (!Mathf.Approximately(baseScoreMult, lastBaseScoreMultApplied))
        {
            float gapS = scoreMultiplier - lastBaseScoreMultApplied;
            scoreMultiplier = Mathf.Max(baseScoreMult, baseScoreMult + gapS);
            lastBaseScoreMultApplied = baseScoreMult;
        }
        else if (scoreMultiplier < baseScoreMult)
        {
            scoreMultiplier = baseScoreMult;
        }

        // Round floors and active multipliers (global rounding control)
        baseXPMult = Round2(baseXPMult);
        baseScoreMult = Round2(baseScoreMult);
        xpMultiplier = Round2(xpMultiplier);
        scoreMultiplier = Round2(scoreMultiplier);
    }

    private void MaybeEnableInvisibleWalls()
    {
        if (!wallTimerStart) return;
        wallTimer += Time.deltaTime;
        if (wallTimer >= 0.2f)
            EnableInvisibleWalls();
    }

    private void HandleGameOverRestart()
    {
        if (currentState == PinballState.GameOver && Input.GetKey(chargeKey))
            SceneManager.LoadScene(0);
    }

    private void UpdateMultipliersTimers()
    {
        if (IsScoreMultiplierActive && scoreBonusTimer > 0f)
        {
            scoreBonusTimer -= Time.unscaledDeltaTime;
            if (scoreBonusTimer <= 0f)
                scoreBonusTimer = 0f;
        }

        if (IsXPMultiplierActive && xpBonusTimer > 0f)
        {
            xpBonusTimer -= Time.unscaledDeltaTime;
            if (xpBonusTimer <= 0f)
                xpBonusTimer = 0f;
        }
    }

    private void HandleChargingAndPush()
    {
        if (Input.GetKey(chargeKey))
            isHoldingCharge = true;

        chargePercentage = Mathf.Min(1f, chargeMax > 0f ? (chargeTimer / chargeMax) : 0f);

        if (Input.GetKeyUp(chargeKey))
        {
            isHoldingCharge = false;

            if (chargePercentage > minChargeToLaunch)
            {
                if (currentState == PinballState.Charging)
                {
                    ball?.Launch(chargePercentage);
                    if (chargePercentage > chargeToEnterPush)
                        ChangeState(PinballState.Push);
                }
                else if (currentState == PinballState.Push)
                {
                    if (ball != null && ball.IsInLaunchTube)
                    {
                        ball.Push(chargePercentage);
                        ChangeState(PinballState.Play);
                    }
                }
            }
        }

        if (currentState == PinballState.Push && (ball == null || (!ball.IsInLaunchTube && ball.GetComponent<Rigidbody>()?.velocity.z < 0f)))
        {
            ChangeState(PinballState.Charging);
        }
    }

    private void TickChargeTimer()
    {
        if (!isHoldingCharge)
        {
            chargeTimer -= Time.deltaTime;
            if (chargeTimer < 0f) chargeTimer = 0f;
        }
        else
        {
            chargeTimer += Time.deltaTime;
            if (chargeTimer > chargeMax) chargeTimer = chargeMax;
        }
    }

    private void HandlePlayStateDrain()
    {
        bool any = HasAnyUsableBalls() || IsBallUsable(ball);

        if (!any)
        {
            _noBallTimer += Time.unscaledDeltaTime;
            if (_noBallTimer >= noBallResetGrace)
            {
                ChangeState(PinballState.ResetBall);
            }
        }
        else
        {
            _noBallTimer = 0f;
        }
    }

    public void DisableBall(Ball ball)
    {
        var go = ball.gameObject;
        go.SetActive(false);
        ballCount--;
    }
    #endregion

    #region Ball registry
    public void RegisterBall(Ball b)
    {
        if (b != null && !liveBalls.Contains(b))
            liveBalls.Add(b);
    }

    public void UnregisterBall(Ball b)
    {
        if (b != null)
            liveBalls.Remove(b);
    }

    private static bool IsBallUsable(Ball b)
    {
        return b != null && b.isActiveAndEnabled && b.gameObject.activeInHierarchy && b.IsActive;
    }

    private IEnumerable<Ball> GetUsableBalls()
    {
        for (int i = liveBalls.Count - 1; i >= 0; i--)
        {
            var b = liveBalls[i];
            if (b == null)
            {
                liveBalls.RemoveAt(i);
                continue;
            }
            if (IsBallUsable(b)) yield return b;
        }
    }

    private void ForEachUsableBall(System.Action<Ball> action)
    {
        foreach (var b in GetUsableBalls())
            action(b);
    }

    private bool TryGetAnchorBall(out Ball anchor)
    {
        for (int i = 0; i < liveBalls.Count; i++)
        {
            var candidate = liveBalls[i];
            if (candidate == null) { liveBalls.RemoveAt(i); i--; continue; }
            if (IsBallUsable(candidate)) { anchor = candidate; return true; }
        }
        anchor = null;
        return false;
    }

    private Ball GetPaddleTarget()
    {
        for (int i = 0; i < liveBalls.Count; i++)
        {
            var b = liveBalls[i];
            if (b == null) { liveBalls.RemoveAt(i); i--; continue; }
            if (IsBallUsable(b) && b.IsTouchingPaddles)
                return b;
        }
        return TryGetAnchorBall(out var anchor) ? anchor : null;
    }

    private void EnsurePrimaryBallRef()
    {
        if (ball != null && ball.GetInstanceID() == _primaryBallId)
            return;

        for (int i = 0; i < liveBalls.Count; i++)
        {
            var b = liveBalls[i];
            if (b != null && b.GetInstanceID() == _primaryBallId)
            {
                ball = b;
                return;
            }
        }

        if (ball == null && liveBalls.Count > 0 && liveBalls[0] != null)
        {
            ball = liveBalls[0];
            _primaryBallId = ball.GetInstanceID();
        }
    }

    private void FreezeAndTeleportToStart(Ball target)
    {
        if (target == null) return;
        var t = target.transform;
        var rb = target.GetComponent<Rigidbody>();

        DOTween.Kill(t, false);

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            rb.position = _ballStartPos;
            rb.rotation = _ballStartRot;
            rb.Sleep();
            rb.isKinematic = false;
        }
        else
        {
            t.position = _ballStartPos;
            t.rotation = _ballStartRot;
        }
        target.ResetRb();
    }
    #endregion

    #region Walls
    public void EnableInvisibleWalls()
    {
        if (invisWalls) invisWalls.SetActive(true);
    }

    public void DisableInvisibleWalls()
    {
        if (invisWalls) invisWalls.SetActive(false);
        wallTimer = 0f;
        wallTimerStart = false;
    }
    #endregion

    #region Ball duplication
    public void DupeBall()
    {
        if (!TryGetAnchorBall(out var anchor))
        {
            if (IsBallUsable(ball)) anchor = ball;
        }
        if (anchor == null || ballClonePrefab == null)
            return;

        var dupedBallGO = Instantiate(ballClonePrefab, anchor.transform.position, Quaternion.identity);
        var dupedBall = dupedBallGO.GetComponent<Ball>();
        if (dupedBall != null)
        {
            var anchorCol = anchor.GetComponent<Collider>();
            var dupedCol = dupedBall.GetComponent<Collider>();
            if (anchorCol != null && dupedCol != null && anchorCol.material != null)
                dupedCol.material = Instantiate(anchorCol.material);

            dupedBall.CopyFrom(anchor);
            dupedBall.ResetRb();
            dupedBall.RandomizeGlowColor();
        }
        ballCount++;
    }

    private void MergeAnchorFromActiveClone()
    {
        if (ball == null) return;
        foreach (var b in GetUsableBalls())
        {
            if (b != null && b != ball)
            {
                ball.CopyFrom(b);
                break;
            }
        }
    }
    #endregion

    #region Score / XP
    public void AddScore(int gameScore, int bumpCount, int bumpCountConsec, float damageFactor, int ScoreMultPortal = 1)
    {
        // Multipliers already rounded to 2 decimals; final points consistent.
        int finalPoints = Mathf.RoundToInt(gameScore * scoreMultiplier);
        finalPoints = Mathf.RoundToInt(finalPoints * ScoreMultPortal);

        if (extraHitsS)
        {
            bumperBouncesS++;
            if (bumperBouncesS > bouncesForBonusS)
            {
                finalPoints = Mathf.RoundToInt(finalPoints * bonusHitsS);
                bumperBouncesS = 0;
            }
        }
        if (extraHitsG)
        {
            bumperBouncesG++;
            if (bumperBouncesG > bouncesForBonusG)
            {
                xpCount = Mathf.RoundToInt(xpCount * bonusHitsG);
                bumperBouncesG = -1;
            }
            if (bumperBouncesG == 0)
                xpCount = Mathf.RoundToInt(xpCount / bonusHitsG);
        }

        if (destroyedBumperBonusActive)
        {
            score += Mathf.RoundToInt(finalPoints * destroyedBumperScoreMult);
            destroyedBumperBonusActive = false;
        }
        else score += finalPoints;

        ui?.UpdateScore(score, bumpCount, bumpCountConsec);
    }

    private bool HasAnyUsableBalls()
    {
        foreach (var _ in GetUsableBalls()) return true;
        if (IsBallUsable(ball))
        {
            if (!liveBalls.Contains(ball))
                RegisterBall(ball);
            return true;
        }
        return false;
    }

    private void ResetBallAndFlow()
    {
        EnsurePrimaryBallRef();
        _noBallTimer = 0f;
        MergeAnchorFromActiveClone();
        DisableInvisibleWalls();

        for (int i = liveBalls.Count - 1; i >= 0; i--)
        {
            var b = liveBalls[i];
            if (b == null) { liveBalls.RemoveAt(i); continue; }
            if (b != ball)
            {
                liveBalls.RemoveAt(i);
                Destroy(b.gameObject);
            }
        }

        if (ball != null)
        {
            ball.gameObject.SetActive(true);
            var rb = ball.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            FreezeAndTeleportToStart(ball);
            if (!liveBalls.Contains(ball)) RegisterBall(ball);
        }

        if (portalResetDebugCooldown > 0f)
        {
            var portalRuntime = FindFirstObjectByType<PortalWarpRewardRuntime>();
            portalRuntime?.ForceGlobalCooldown(portalResetDebugCooldown);
            var grenadeRuntime = FindFirstObjectByType<GrenadeRewardRuntime>();
            grenadeRuntime?.ForceGlobalCooldown(portalResetDebugCooldown);
        }

        isHoldingCharge = false;
        chargeTimer = 0f;
        chargePercentage = 0f;

        ballCount = 1;

        ChangeState(lives > 0 ? PinballState.Charging : PinballState.GameOver);
    }

    public void AddXP(float xp)
    {
        finalXPCalculated = xp * xpMultiplier;
        curXP += finalXPCalculated;
        ballXPScript?.UpdateXP(Mathf.RoundToInt(curXP), Mathf.RoundToInt(maxXP), level);
    }

    public void SpawnXP(Vector3 pos, bool isDead, bool isTakingElemDamage, float damageFactor, int mult = 1)
    {
        if (!xpFXPrefab) return;

        int finalXP = Mathf.RoundToInt(xpCount * Mathf.Max(1f, damageFactor * .35f));
        finalXP *= Mathf.Max(1, mult);

        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);

        int emitted;
        if (isDead) emitted = finalXP * 2;
        else if (isTakingElemDamage) emitted = Mathf.RoundToInt(finalXP / 3f);
        else emitted = finalXP;

        xpFX.Emit(emitted);
    }

    public void SpawnBonusWaterXP(Vector3 pos, float waterBonusXP, float damageFactor, int mult = 1)
    {
        if (!xpFXPrefab) return;

        int baseXP = Mathf.RoundToInt(xpCount * Mathf.Max(1f, damageFactor));
        baseXP *= Mathf.Max(1, mult);

        float bonus = Mathf.Max(0f, waterBonusXP) / 100f;
        int emitted = Mathf.RoundToInt(baseXP * (1f + bonus));

        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(emitted);
    }

    public void SpawnBonusEarthXP(Vector3 pos, float earthBonusXP, float damageFactor, int mult = 1)
    {
        if (!xpFXPrefab) return;

        int baseXP = Mathf.RoundToInt(xpCount * Mathf.Max(1f, damageFactor));
        baseXP *= Mathf.Max(1, mult);

        float bonus = Mathf.Max(0f, earthBonusXP) / 100f;
        int emitted = Mathf.RoundToInt(baseXP * (1f + bonus));

        Vector3 position = new Vector3(pos.x, pos.y + 1f, pos.z);
        var xpFX = Instantiate(xpFXPrefab, position, xpFXPrefab.transform.rotation);
        xpFX.Emit(emitted);
    }

    private void StartNextLevelUp()
    {
        if (pendingLevelUps <= 0) return;
        // Defer LevelUp if player is aiming a skill shot
        if (CurrentState == PinballState.SkillAim)
        {
            _queueLevelUpAfterAim = true;
            return;
        }
        ChangeState(PinballState.LevelUp);
    }

    public void LevelUp()
    {
        level++;
        maxXP = XPFormula.XpReq(level);
        ballXPScript?.UpdateXP(curXP, maxXP, level);
        ApplyLevelBonuses();
    }

    private void ShowNextLevelUpOrResume()
    {
        if (pendingLevelUps > 0)
        {
            LevelUp();
            var choices = GetRewardChoices();
            ui?.ShowRewardPopup(choices);
            Time.timeScale = 0f;
        }
        else
        {
            // Resume to Play with a short slow‑mo ramp using the TimeScaleHub (safe).
            StartCoroutine(ResumeFromLevelUpFlow());
        }
    }

    private IEnumerator ResumeFromLevelUpFlow()
    {
        ChangeState(PinballState.Play);
        // One frame to ensure UI/pause fully released
        yield return null;
        StartPostLevelUpSlowMo();
    }

    private void StartPostLevelUpSlowMo()
    {
        if (!enableResumeSlowMo) return;

        _resumeSlowmoTween?.Kill(false);

        float start = Mathf.Clamp(resumeSlowMoStartScale, 0.05f, 1f);
        // Begin strong slow-mo
        TimeScaleHub.Begin(_postLevelUpSlowmoToken, start, affectFixedDelta: true);

        var seq = DOTween.Sequence().SetUpdate(true);
        if (resumeSlowMoHold > 0f)
            seq.AppendInterval(resumeSlowMoHold);

        // Ease back to normal (1.0) and release the token
        seq.Append(DOTween.To(() => start, s =>
        {
            // Update current slow-mo scale through the hub (min-of-active is enforced there)
            TimeScaleHub.Begin(_postLevelUpSlowmoToken, s, affectFixedDelta: true);
        }, 1f, resumeSlowMoEase).SetEase(Ease.OutQuad))
        .OnComplete(() =>
        {
            TimeScaleHub.End(_postLevelUpSlowmoToken);
        });

        _resumeSlowmoTween = seq;
    }

    public void ApplyLevelBonuses()
    {
        if (level % 5 == 0)
        {
            baseScoreMult += 0.5f;
            baseXPMult += 0.5f;
        }
        else
        {
            baseScoreMult += 0.15f;
            baseXPMult += 0.15f;
        }
        baseScoreMult = Round2(baseScoreMult);
        baseXPMult = Round2(baseXPMult);
        lastBaseScoreMultApplied = baseScoreMult;
        lastBaseXPMultApplied = baseXPMult;
        // Keep active multipliers >= floors and rounded
        scoreMultiplier = Round2(Mathf.Max(scoreMultiplier, baseScoreMult));
        xpMultiplier = Round2(Mathf.Max(xpMultiplier, baseXPMult));
    }
    #endregion

    #region Green Pad Skill Aim
    public void EnterGreenPadAim(Ball ball, Transform focus, float slowMo, float windowUnscaled, float minSpeed, float maxSpeed, float nextHitFactor, int nextHitBounces)
    {
        var req = new SkillAimRequest(
            ball,
            focus,
            Mathf.Clamp(slowMo, 0.05f, 0.5f),
            Mathf.Max(0.15f, windowUnscaled),
            Mathf.Max(1f, minSpeed),
            Mathf.Max(Mathf.Max(1f, minSpeed), maxSpeed),
            Mathf.Max(1f, nextHitFactor),
            Mathf.Max(1, nextHitBounces)
        );

        if (CurrentState == PinballState.SkillAim || _aimQueue.Count > 0)
        {
            if (_aimBall == ball) return;
            if (_aimQueue.Any(r => r.ball == ball)) return;
            _aimQueue.Enqueue(req);
            return;
        }

        StartSkillAim(req);
    }

    private void StartSkillAim(SkillAimRequest req)
    {
        if (!req.ball) return;

        // Ensure no leftover portal slow-mo interferes with SkillAim
        CancelAllPortalSlowmo();

        // (existing)
        HardResetCameraToDefaults(true);
        currentState = PinballState.SkillAim;

        _aimBall = req.ball;
        _aimFocus = req.focus;
        _aimWindowDur = req.windowUnscaled;
        _aimTimeLeft = _aimWindowDur;
        _aimMinSpeed = req.minSpeed;
        _aimMaxSpeed = req.maxSpeed;
        _targetSlowMo = req.slowMo;
        _nextHitFactor = req.nextHitFactor;
        _nextHitBounces = req.nextHitBounces;
        _lastAimDir = _aimBall ? _aimBall.transform.forward : Vector3.forward;

        // Keep portals on cooldown for the duration of the aim window (prevents edge cases)
        var portalRuntime = FindFirstObjectByType<PortalWarpRewardRuntime>();
        if (portalRuntime)
            portalRuntime.ForceGlobalCooldown(_aimWindowDur * 0.20f);

        if (!mainCam) mainCam = Camera.main;

        _camDistTween?.Kill(false);
        _camPosTween?.Kill(false);
        _camRotTween?.Kill(false);

        _preAimCamPos = mainCam ? mainCam.transform.position : _preAimCamPos;
        _preAimCamRot = mainCam ? mainCam.transform.rotation : _preAimCamRot;

        TimeScaleHub.Begin(_skillAimSlowmoToken, _targetSlowMo, true);

        if (mainCam)
        {
            _fovTween?.Kill(false);
            _preAimFov = mainCam.fieldOfView;
            _fovTween = DOTween.To(() => mainCam.fieldOfView, v => mainCam.fieldOfView = v, camAimFov, camFovTween).SetUpdate(true);
        }

        if (camFollow && _aimBall)
        {
            _preAimCamTarget = camFollow.Target;
            _preAimFollowDist = camFollow.FollowDistance;

            if (_aimCamTarget == null) _aimCamTarget = new GameObject("AimCamTarget").transform;
            _aimCamTarget.SetParent(_aimBall.transform, false);
            _aimCamTarget.localPosition = new Vector3(0f, 0.4f, 0f);
            _aimCamTarget.localRotation = Quaternion.identity;

            camFollow.Target = _aimCamTarget;
            camFollow.SnapToTarget();
            _camDistTween = DOTween
                .To(() => camFollow.FollowDistance, v => camFollow.FollowDistance = v, aimFollowDistance, aimFollowTween)
                .SetUpdate(true);
        }

        if (postFX)
        {
            postFX.VignetteMax = vignetteMax;
            postFX.SetVignette(0f);
            postFX.FadeVignette(0.25f, 0.08f);
        }

        var rb = _aimBall.GetComponent<Rigidbody>();
        if (rb) rb.velocity *= .5f;

        if (aimLine)
        {
            aimLine.enabled = true;
            aimLine.positionCount = 2;
            var p = _aimBall.transform.position;
            aimLine.SetPosition(0, p);
            aimLine.SetPosition(1, p);
        }
    }

    private void UpdateSkillAim()
    {
        if (CurrentState != PinballState.SkillAim || _aimBall == null) return;

        _aimTimeLeft -= Time.unscaledDeltaTime;
        var rb = _aimBall.GetComponent<Rigidbody>();
        Vector3 ballPos = _aimBall.transform.position;

        Vector3 aimDir = Vector3.zero;

        if (mainCam)
        {
            Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0, ballPos.y, 0));
            if (plane.Raycast(ray, out float dist))
            {
                Vector3 hit = ray.GetPoint(dist);
                Vector3 delta = (hit - ballPos);
                delta.y = 0f;
                if (delta.sqrMagnitude > 0.0001f) aimDir = delta.normalized;
            }
        }

        Vector3 dir = (aimDir.sqrMagnitude > 1e-4f ? aimDir : _lastAimDir).normalized;
        _lastAimDir = dir;

        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        Vector3 stick = new Vector3(x, 0f, z);
        if (stick.sqrMagnitude > 0.0001f) aimDir = stick.normalized;

        bool charging = Input.GetMouseButton(0) || Input.GetKey(KeyCode.JoystickButton0);
        float held = Mathf.Clamp01(1f - (_aimTimeLeft / Mathf.Max(0.0001f, _aimWindowDur)));
        float power = charging ? held : held * 0.75f;

        if (postFX)
        {
            float frac = 1f - Mathf.Clamp01(_aimTimeLeft / Mathf.Max(0.0001f, _aimWindowDur));
            postFX.SetVignette(Mathf.SmoothStep(0f, 1f, frac));
        }

        float projectedSpeed = Mathf.Lerp(_aimMinSpeed, _aimMaxSpeed, power);
        float desiredLen = projectedSpeed * 0.05f * Mathf.Clamp01(power);
        float length = Mathf.Min(aimLineMaxLen, desiredLen);
        length = Mathf.Max(length, 0.05f * aimLineMaxLen);

        Vector3 origin = ballPos;
        Vector3 tip = origin + dir * length;

        if (aimLine)
        {
            aimLine.positionCount = 2;
            aimLine.SetPosition(0, origin);
            aimLine.SetPosition(1, tip);
        }

        bool release = Input.GetMouseButtonUp(0) || Input.GetKeyUp(KeyCode.JoystickButton0);
        bool timedOut = _aimTimeLeft <= 0f;

        if (release || timedOut)
        {
            if (aimDir.sqrMagnitude < 0.001f)
                aimDir = (_aimFocus ? (_aimBall.transform.position - _aimFocus.position) : _aimBall.transform.forward);
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 0.001f) aimDir = Vector3.forward;
            aimDir.Normalize();

            float speed = Mathf.Lerp(_aimMinSpeed, _aimMaxSpeed, power);
            _aimBall.AddTempDamageForBounce(_nextHitFactor, _nextHitBounces);

            if (rb)
            {
                rb.velocity = Vector3.zero;
                rb.AddForce(aimDir * speed, ForceMode.VelocityChange);
            }

            if (postFX) postFX.ChromaticPulse(0.25f, 0.06f, 0.14f);
            ScreenShake();

            if (_aimBall)
                _aimBall.TemporarilyUncapMaxSpeed(SkillAimUncapDuration, SkillAimUncapMaxSpeed, true);

            ExitGreenPadAim();
        }
    }

    private void ExitGreenPadAim()
    {
        RestoreFromSkillAim();

        while (_aimQueue.Count > 0)
        {
            var next = _aimQueue.Dequeue();
            if (next.ball && next.ball.isActiveAndEnabled && next.ball.IsActive)
            {
                StartSkillAim(next);
                return;
            }
        }

        currentState = PinballState.Play;

        // If leveling is queued or still pending, show shortly after aim completes.
        if (_queueLevelUpAfterAim || pendingLevelUps > 0)
            StartCoroutine(InvokeLevelUpAfterSkillAim());
    }

    private IEnumerator InvokeLevelUpAfterSkillAim()
    {
        _queueLevelUpAfterAim = false;
        yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, postAimLevelUpDelay));
        if (pendingLevelUps > 0 && CurrentState == PinballState.Play)
            StartNextLevelUp();
    }

    private void RestoreFromSkillAim(bool tweenCamFollowDist = true)
    {
        TimeScaleHub.End(_skillAimSlowmoToken);
        HardResetCameraToDefaults(true);

        if (aimLine)
        {
            aimLine.enabled = false;
            aimLine.positionCount = 0;
        }

        _aimBall = null;
        _aimFocus = null;
        _aimTimeLeft = 0f;
        Time.fixedDeltaTime = _defaultFixedDeltaTime;
    }
    #endregion

    #region Paddle elemental windows
    public bool AreBothPaddlesElemental()
    {
        var leftElem = leftPaddle ? leftPaddle.GetComponent<PaddleElementalState>() : null;
        var rightElem = rightPaddle ? rightPaddle.GetComponent<PaddleElementalState>() : null;

        bool leftHas = leftElem != null && leftElem.CurrentState != PaddleState.None;
        bool rightHas = rightElem != null && rightElem.CurrentState != PaddleState.None;
        return leftHas && rightHas;
    }

    private void HitCheck(KeyCode paddle)
    {
        if (paddle == leftPaddleKey) StartCoroutine(RegCheck(leftPaddleKey));
        else StartCoroutine(RegCheck(rightPaddleKey));
    }

    private IEnumerator RegCheck(KeyCode paddle)
    {
        if (paddle == leftPaddleKey) canHitL = true; else canHitR = true;
        yield return new WaitForSeconds(0.6f);
        if (paddle == leftPaddleKey) canHitL = false; else canHitR = false;
    }

    private IEnumerator ResetLBumper(float cd) { yield return new WaitForSeconds(cd); hasBeenHitL = false; }
    private IEnumerator ResetRBumper(float cd) { yield return new WaitForSeconds(cd); hasBeenHitR = false; }
    #endregion

    #region Camera shake
    public void ScreenShake()
    {
        if (mainCam == null) return;
        if (camFollow != null)
            camFollow.StartShake(shakeDuration, shakeStrength, shakeVibrato, shakeRandomness);
    }

    public void ScreenShakeGrenade()
    {
        if (mainCam == null || camFollow == null) return;
        // Roughly 1.6x strength/duration, tighter vibrato
        camFollow.StartShake(shakeDuration * 1.6f, shakeStrength * 1.75f, Mathf.Max(8, shakeVibrato - 2), shakeRandomness);
    }

    #endregion

    #region Bumper respawn
    public IEnumerator RespawnRoutine(Bumper bumper)
    {
        if (!bumper || !ball) yield break;

        float f = explosionForce;
        float r = explosionRadius;
        if (bumper.type == BumperType.Small) { f *= 0.5f; r *= 0.5f; }
        else if (bumper.type == BumperType.Large) { f *= 1.5f; r *= 1.5f; }

        var rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
            rb.AddExplosionForce(f, bumper.transform.position, r, 0f, ForceMode.Impulse);

        var col = bumper.GetComponent<Collider>();
        var mr = bumper.GetComponent<MeshRenderer>();
        if (col) col.enabled = false;
        if (mr) mr.enabled = false;

        yield return new WaitForSeconds(bumper.cooldown);

        bumper.Revive();
        bumper.gameObject.SetActive(true);
        if (col) col.enabled = true;
        if (mr) mr.enabled = true;
    }
    #endregion

    #region Reward application (IRunContext)
    public void ApplyXPMultiplier(float multiplier, bool cursed)
    {
        if (cursed)
        {
            xpCount += 1;
            if (xpBonusTimer > 5) xpBonusTimer = 5;
            xpMultiplier *= 2f;
        }
        else
        {
            float pct = multiplier >= 1f ? multiplier * 0.01f : multiplier;
            xpMultiplier *= (1f + pct);
        }
        xpMultiplier = Round2(xpMultiplier);
    }

    public void ApplyXPBonusTime(float time, bool cursed)
    {
        if (!IsXPMultiplierActive) return;
        if (cursed) { if (xpBonusTimer > 3) xpBonusTimer = 3; }
        else
        {
            xpBonusTimer += time;
            if (xpBonusTimer > 30) xpBonusTimer = 30f;
        }
    }

    public void ApplyScoreMultiplier(float multiplier, bool cursed)
    {
        if (Mathf.Approximately(multiplier, X2_MULT_MAGIC))
        {
            scoreMultiplier *= 2f;
        }
        else if (cursed)
        {
            scoreMultiplier *= 4f;
        }
        else
        {
            float pct = multiplier >= 1f ? multiplier * 0.01f : multiplier;
            scoreMultiplier *= (1f + pct);
        }
        scoreMultiplier = Round2(scoreMultiplier);
    }

    public void ApplyScoreBonusTime(float time, bool cursed)
    {
        if (!IsScoreMultiplierActive) return;
        if (cursed) scoreBonusTimer *= 0.1f;
        else scoreBonusTimer += time;
    }

    public void ApplyShrinkFX(float size, float speed, float bounciness, float scoreMult, float bonusBounces, int bounces, bool bonus, bool cursed)
    {
        float Size = (100f + size) / 100f;
        float Speed = (100f + speed) / 1000f;
        float Mult = (100f + scoreMult) / 100f;
        float Bounciness = ((100f * bounciness) / 10000f);

        if (scoreMult != 0)
        {
            baseScoreMult *= Mult;
            baseScoreMult = Round2(baseScoreMult);
            scoreMultiplier = Round2(scoreMultiplier);
        }

        if (bonus)
        {
            extraHitsS = true;
            bouncesForBonusS = bounces;
            bonusHitsS = bonusBounces;
        }

        ForEachUsableBall(b =>
        {
            if (bounciness != 0) b.AdjustBounciness(1f + Bounciness);
            if (size != 0) b.transform.localScale *= Size;
            if (speed != 0) b.maxSpeed *= 1f + Speed;
        });
    }

    public void ApplyGrowFX(float size, float speed, float bounciness, float xpMult, float bonusBounces, int bounces, bool bonus, bool cursed)
    {
        float Size = (100f + size) / 100f;
        float Speed = (100f + speed) / 1000f;
        float Mult = (100f + xpMult) / 100f;
        float Bounciness = ((100f * bounciness) / 10000f);

        if (xpMult != 0)
        {
            baseXPMult *= Mult;
            baseXPMult = Round2(baseXPMult);
            xpMultiplier = Round2(xpMultiplier);
        }

        if (bonus)
        {
            extraHitsG = true;
            bouncesForBonusG = bounces;
            bonusHitsG = bonusBounces;
        }

        ForEachUsableBall(b =>
        {
            if (bounciness != 0) b.AdjustBounciness(1f + Bounciness);
            if (size != 0) b.transform.localScale *= Size;
            if (speed != 0) b.maxSpeed *= 1f - Speed;
        });
    }

    public void ApplyXPForcefield(float amount)
    {
        float deltaScale = (100f + amount) / 100f;
        xpBaseRadiusScale *= Mathf.Max(0.0001f, deltaScale);
        RefreshAllBallForcefields();
    }

    public void RefreshAllBallForcefields()
    {
        ForEachUsableBall(b => b.RefreshForcefieldFromContext());
    }

    public void ApplyAdditionalBalls(int additionalBalls)
    {
        if (additionalBalls != 100)
        {
            for (int i = 0; i < additionalBalls; i++) { DupeBall(); }
        }
        else
        {
            int curBallCount = ballCount;
            for (int i = 0; i < curBallCount; i++) { DupeBall(); }
        }
    }

    public void ApplyGrantedLives(int amount)
    {
        lives = Mathf.Clamp(lives + amount, 0, maxLives);
        OnLivesChanged();
    }

    public void ApplyDamageFX(float amount)
    {
        float Amount = 0.01f * amount;
        ForEachUsableBall(b => b.AddDamageMultiplier(Amount));
    }

    public void ApplyDmgPerBounceFX(float amount, int bounces)
    {
        float Amount = 0.01f * amount;
        ForEachUsableBall(b => b.AddTempDamageMultiplier(Amount, bounces));
    }

    public void SetPaddleState(bool isLeft)
    {
        var paddle = isLeft ? leftPaddle : rightPaddle;
        var paddleElem = paddle ? paddle.GetComponent<PaddleElementalState>() : null;

        if (paddleElem == null || pendingPaddleReward == null)
        {
            if (pendingLevelUps > 0)
            {
                var choices = GetRewardChoices();
                ui?.ShowRewardPopup(choices);
                ui?.ClosePaddleSelect(true);
            }
            else
            {
                ui?.ClosePaddleSelect(false);
                // Resume with slow‑mo ramp to avoid abrupt return
                StartCoroutine(ResumeFromLevelUpFlow());
            }
            return;
        }

        pendingPaddleReward.ApplyToPaddle(paddleElem);
        pendingPaddleReward = null;

        if (pendingLevelUps > 0)
        {
            var choices = GetRewardChoices();
            ui?.ShowRewardPopup(choices);
            ui?.ClosePaddleSelect(true);
        }
        else
        {
            ui?.ClosePaddleSelect(false);
            // Resume with slow‑mo ramp
            StartCoroutine(ResumeFromLevelUpFlow());
        }
    }
    #endregion

    #region Reward selection flow
    public void OnRewardChosen(RewardSO reward)
    {
        if (reward == null) { ChangeState(PinballState.Play); return; }

        if (reward.isPaddleReward)
        {
            var leftElem = leftPaddle ? leftPaddle.GetComponent<PaddleElementalState>() : null;
            var rightElem = rightPaddle ? rightPaddle.GetComponent<PaddleElementalState>() : null;

            bool leftHasElem = leftElem != null && leftElem.CurrentState != PaddleState.None;
            bool rightHasElem = rightElem != null && rightElem.CurrentState != PaddleState.None;

            pendingPaddleReward = reward;

            if (leftHasElem && !rightHasElem) { pendingLevelUps--; SetPaddleState(false); reward.Apply(this); return; }
            if (!leftHasElem && rightHasElem) { pendingLevelUps--; SetPaddleState(true); reward.Apply(this); return; }

            reward.Apply(this);
            pendingLevelUps--;
            ChangeState(PinballState.PaddleSelect);
            return;
        }

        if (reward.Scalable && reward.ReplacesReward != null)
        {
            var old = reward.ReplacesReward;
            if (Owns(old.Id))
            {
                active.Remove(old.Id);
                owned.Remove(old.Id);

                reward.Apply(this);
                pendingLevelUps--;
                ShowNextLevelUpOrResume(); return;
            }
        }

        reward.Apply(this);
        pendingLevelUps--;
        ShowNextLevelUpOrResume();
    }

    private RewardSO PickOneWeighted(List<RewardSO> pool, RewardRarity rarity)
    {
        var candidates = pool.Where(r => r.Rarity == rarity).ToList();
        if (candidates.Count == 0) return null;
        int index = UnityEngine.Random.Range(0, candidates.Count);
        return candidates[index];
    }

    private RewardRarity RollRarity()
    {
        float total = rarityWeights.Values.Sum();
        float roll = UnityEngine.Random.Range(0f, total);

        float cumulative = 0f;
        foreach (var kv in rarityWeights)
        {
            cumulative += kv.Value;
            if (roll <= cumulative) return kv.Key;
        }
        return RewardRarity.Common;
    }

    private List<RewardSO> GetRewardChoices()
    {
        var eligible = rewardPool.Where(r => r.IsEligible(this)).ToList();
        if (eligible.Count == 0) return new List<RewardSO>();

        var picks = new List<RewardSO>();
        var localEligible = new List<RewardSO>(eligible);

        for (int i = 0; i < 4; i++)
        {
            RewardSO choice = null;
            for (int j = 0; j < 10 && choice == null; j++)
            {
                var tier = RollRarity();
                choice = PickOneWeighted(localEligible, tier);
            }
            if (choice == null) break;
            picks.Add(choice);
            localEligible.Remove(choice);
            if (localEligible.Count == 0) break;
        }
        return picks;
    }
    #endregion

    #region Timescale / camera utilities
    private void NormalizeTimeScaleIfNeeded()
    {
        if (Time.timeScale < 0.99f)
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _defaultFixedDeltaTime;
        }
    }

    private void HardResetCameraToDefaults(bool snapPositionRotation = true)
    {
        _camDistTween?.Kill(false); _camDistTween = null;
        _camPosTween?.Kill(false); _camPosTween = null;
        _camRotTween?.Kill(false); _camRotTween = null;
        _fovTween?.Kill(false); _fovTween = null;

        if (_aimCamTarget != null)
        {
            Destroy(_aimCamTarget.gameObject);
            _aimCamTarget = null;
        }

        if (camFollow)
        {
            camFollow.Target = _camDefaultTarget;
            camFollow.FollowDistance = _camDefaultFollowDist;
            camFollow.Height = _camDefaultHeight;

            if (snapPositionRotation)
            {
                if (camFollow.Target != null)
                {
                    camFollow.SnapToTarget();
                }
                else if (mainCam)
                {
                    mainCam.transform.SetPositionAndRotation(_camDefaultCamPos, _camDefaultCamRot);
                }
            }
        }

        if (mainCam) mainCam.fieldOfView = _camDefaultFov;
        postFX?.ClearVignette(0f);
    }

    private void CancelAllPortalSlowmo()
    {
        var runtime = FindFirstObjectByType<PortalWarpRewardRuntime>();
        runtime?.CancelAllSlowmo();
        NormalizeTimeScaleIfNeeded();
    }

    private void EnsureNormalTimescaleIfNotAiming()
    {
        if (currentState == PinballState.SkillAim ||
            currentState == PinballState.LevelUp ||
            currentState == PinballState.PaddleSelect)
            return;

        if (TimeScaleHub.IsPaused)
            return;

        if (TimeScaleHub.IsAnyActive)
            return;

        if (Time.timeScale < 0.999f)
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _defaultFixedDeltaTime;
        }
    }
    #endregion
}
```

## Assets/Scripts/PinballFlipper.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PinballFlipper : MonoBehaviour
{
    public HingeJoint hinge;
    public float flipSpeed = 1500f;      // how fast the motor tries to move
    public float returnSpeed = -200f;    // how fast it returns down
    public float maxForce = 10000f;      // motor strength

    void Awake()
    {
        // cache your HingeJoint reference
        hinge = GetComponent<HingeJoint>();
        hinge.useMotor = true;
    }

    public void PaddleMovement(bool isPressed)
    {
        var motor = hinge.motor;
        motor.force = maxForce;
        motor.targetVelocity = isPressed ? flipSpeed : returnSpeed;
        hinge.motor = motor;
    }
}

```

## Assets/Scripts/PinballMusic.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class PinballMusic : MonoBehaviour
{
    [Header("Clips")]
    [Tooltip("Plays once, then hands off to Loop.")]
    public AudioClip introClip;

    [Tooltip("Seamlessly loops forever after Intro finishes.")]
    public AudioClip loopClip;

    [Header("Routing (optional)")]
    [Tooltip("Optional mixer routing for both sources.")]
    public AudioMixerGroup outputMixerGroup;

    [Header("Behavior")]
    [Tooltip("Automatically start music on Start().")]
    public bool playOnStart = true;

    [Tooltip("Safety lead time before scheduled start, in seconds.")]
    [Min(0.01f)]
    public double scheduleLeadIn = 0.08; // small buffer for preload

    private AudioSource _introSource;
    private AudioSource _loopSource;
    private bool _started;



    void Awake()
    {
        _introSource = gameObject.AddComponent<AudioSource>();
        _loopSource = gameObject.AddComponent<AudioSource>();

        ConfigureSource(_introSource, false);
        ConfigureSource(_loopSource, true);
    }


    void Start()
    {
        if (playOnStart)
            StartMusic();
    }

    public void StartMusic()
    {
        if (_started)
            return;

        double dspStart = AudioSettings.dspTime + scheduleLeadIn;

        if (introClip != null)
        {
            _introSource.clip = introClip;
            _introSource.loop = false;
            _introSource.PlayScheduled(dspStart);

            double introDuration = (double)introClip.samples / introClip.frequency;

            if (loopClip != null)
            {
                _loopSource.clip = loopClip;
                _loopSource.loop = true;
                _loopSource.PlayScheduled(dspStart + introDuration);
            }
        }

        _started = true;

    }

    private void ConfigureSource(AudioSource source, bool loop)
    {
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = false;
        if(outputMixerGroup != null)
            source.outputAudioMixerGroup = outputMixerGroup;
    }


    // Update is called once per frame
    void Update()
    {
        
    }
}

```

## Assets/Scripts/PinballUIM.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PinballUIM : MonoBehaviour
{
    [System.Serializable]
    public class RewardSlot
    {
        public Button button;
        public TMP_Text titleText;
        public TMP_Text descText;
    }

    [Header("Per-Ball Combo Row")]
    [SerializeField] private Transform ballRowParent;
    [SerializeField] private GameObject ballEntryPrefab;

    private readonly Dictionary<Ball, BallUIEntry> _ballEntries = new();

    [Header("UI Slots(6 buttons)")]
    [SerializeField] private List<RewardSlot> slots = new();

    [Header("Lives UI")]
    [SerializeField] private List<Image> lifeIcons = new();
    [SerializeField] private Color32 lifeOnColor = new Color32(75, 202, 107, 255);
    [SerializeField] private Color32 lifeOffColor = new Color32(75, 202, 107, 11);

    [Header("Ability: Portal Cooldown")]
    [SerializeField] private Transform portalCooldownGroup;
    [SerializeField] private Image portalCooldownTemplate;
    [SerializeField] private Color32 portalReadyFallback = new Color32(170, 85, 255, 255);
    [SerializeField] private Color32 portalCooldownColor = new Color32(255, 255, 255, 255); // retained in case other UI needs it

    [Header("Ability: Grenade Cooldown")]
    [SerializeField] private Transform grenadeCooldownGroup;
    [SerializeField] private Image grenadeCooldownTemplate;
    private readonly Dictionary<Ball, Image> _grenadeIconByBall = new();

    private readonly Dictionary<Ball, Image> _portalIconByBall = new();

    public Image ChargingSlider;

    public GameObject gamePanel;
    public GameObject paddleSelectPanel;
    public GameObject levelUpPanel;

    public TMP_Text gameScore;
    public TMP_Text bc;
    public TMP_Text bcc;
    public TMP_Text xpText;

    private Pinball pm;
    private List<RewardSO> currentRewards = new();

    void Start()
    {
        levelUpPanel.SetActive(false);
        paddleSelectPanel.SetActive(false);
        gamePanel.SetActive(true);

        // Fallbacks: if grenade row not set, reuse portal row/template
        if (!grenadeCooldownTemplate && portalCooldownTemplate)
            grenadeCooldownTemplate = portalCooldownTemplate;
        if (!grenadeCooldownGroup && portalCooldownGroup)
            grenadeCooldownGroup = portalCooldownGroup;
    }

    void Update()
    {
        if (pm != null)
        {
            ChargingSlider.fillAmount = pm.chargePercentage;
            bc.text = $"Score Mult: {pm.ScoreMultiplier:F2} | Timer: {pm.ScoreBonusTimeRemaining:F2}";
            bcc.text = $"XP Mult: {pm.XPMultiplier:F2} | Timer: {pm.XPBonusTimeRemaining:F2}";
            if (Mathf.RoundToInt(pm.curXP) >= Mathf.RoundToInt(pm.maxXP))
                xpText.text = $"{Mathf.RoundToInt(pm.curXP - 1)} / {pm.maxXP}";
            else
                xpText.text = $"{Mathf.RoundToInt(pm.curXP)} / {pm.maxXP}";
        }
    }

    public void Init(Pinball manager)
    {
        pm = manager;
        Ball.OnBallActivated -= HandleBallActivated;
        Ball.OnBallActivated += HandleBallActivated;
        Ball.OnBallDeactivated -= HandleBallDeactivated;
        Ball.OnBallDeactivated += HandleBallDeactivated;

        var existing = GameObject.FindObjectsOfType<Ball>();
        for (int i = 0; i < existing.Length; i++)
            if (existing[i].isActiveAndEnabled && existing[i].IsActive)
                HandleBallActivated(existing[i]);
    }

    public void InitLives(int maxLives)
    {
        for (int i = 0; i < lifeIcons.Count; i++)
            lifeIcons[i].gameObject.SetActive(i < maxLives);
    }

    public void UpdateLives(int lives, int maxLives)
    {
        InitLives(maxLives);
        for (int i = 0; i < lifeIcons.Count && i < maxLives; i++)
            lifeIcons[i].color = i < lives ? lifeOnColor : lifeOffColor;
    }

    public void ShowRewardPopup(List<RewardSO> rewards)
    {
        gamePanel.SetActive(false);
        levelUpPanel.SetActive(true);
        currentRewards = rewards ?? new List<RewardSO>();

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            slot.button.onClick.RemoveAllListeners();

            if (i < currentRewards.Count && currentRewards[i] != null)
            {
                var reward = currentRewards[i];
                slot.titleText.text = reward.Name;
                slot.titleText.color = RewardSO.GetRarityColor(reward.Rarity);
                slot.descText.text = reward.Description;
                slot.button.interactable = true;
                slot.button.gameObject.SetActive(true);
                slot.button.onClick.AddListener(() => OnRewardClicked(reward));
            }
            else
            {
                slot.titleText.text = string.Empty;
                slot.titleText.color = Color.white;
                slot.descText.text = string.Empty;
                slot.button.gameObject.SetActive(false);
            }
        }
    }

    private void OnRewardClicked(RewardSO reward)
    {
        pm.OnRewardChosen(reward);
    }

    public void DefaultUI()
    {
        levelUpPanel.SetActive(false);
        paddleSelectPanel.SetActive(false);
        gamePanel.SetActive(true);
    }

    public void UpdateScore(int score, int bumpCount, int bumpCountConsec)
    {
        gameScore.text = score.ToString();
    }

    public void PaddleSelect()
    {
        gamePanel.SetActive(false);

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            slot.button.interactable = false;
        }
        paddleSelectPanel.SetActive(true);
    }

    public void ClosePaddleSelect(bool hasMoreLevels)
    {
        paddleSelectPanel.SetActive(false);
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            slot.button.interactable = true;
        }

        if (hasMoreLevels)
            levelUpPanel.SetActive(true);
    }

    // ================= PORTAL WARP UI (TEMPLATE-BASED) =================
    // These methods now operate on the main ball's per-ball icon instead of a single Image field.
    public void SetPortalWarpReady(bool ready)
    {
        var mainBall = Pinball.Instance ? Pinball.Instance.ball : null;
        if (!mainBall) return;

        EnsurePortalIcon(mainBall);
        var img = _portalIconByBall[mainBall];
        img.enabled = true;
        img.color = Boost(mainBall.GlowColor, mainBall.EmissionIntensityUI);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = 2;
        img.fillClockwise = true;
        img.fillAmount = 1f; // filled when ready
    }

    public void SetPortalWarpCooldown(float normalizedRemaining)
    {
        var mainBall = Pinball.Instance ? Pinball.Instance.ball : null;
        if (!mainBall) return;

        EnsurePortalIcon(mainBall);
        var img = _portalIconByBall[mainBall];
        img.enabled = true;
        img.color = Boost(mainBall.GlowColor, mainBall.EmissionIntensityUI);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = 2;
        img.fillClockwise = true;
        img.fillAmount = Mathf.Clamp01(normalizedRemaining);
    }

    public void RegisterPortalIcon(Ball b)
    {
        if (!b || _portalIconByBall.ContainsKey(b)) return;
        if (!portalCooldownTemplate || !portalCooldownGroup) return;

        var clone = Instantiate(portalCooldownTemplate, portalCooldownGroup);
        clone.gameObject.name = $"PortalCooldown_{b.name}";
        clone.enabled = true;
        clone.color = Boost(b.GlowColor, b.EmissionIntensityUI); // emission color from start
        clone.type = Image.Type.Filled;
        clone.fillMethod = Image.FillMethod.Radial360;
        clone.fillOrigin = 2;
        clone.fillClockwise = true;
        clone.fillAmount = 1f; // start as filled (ready)
        _portalIconByBall[b] = clone;
    }

    public void UnregisterPortalIcon(Ball b)
    {
        if (!b) return;
        if (_portalIconByBall.TryGetValue(b, out var img))
        {
            if (img) Destroy(img.gameObject);
        }
        _portalIconByBall.Remove(b);
    }

    public void SetBallPortalReady(Ball b, bool ready)
    {
        EnsurePortalIcon(b);
        var img = _portalIconByBall[b];
        img.enabled = true;
        img.color = Boost(b.GlowColor, b.EmissionIntensityUI);
        img.fillAmount = 1f; // filled when ready
    }

    public void SetBallPortalCooldown(Ball b, float normalizedRemaining)
    {
        EnsurePortalIcon(b);
        var img = _portalIconByBall[b];
        img.enabled = true;
        img.color = Boost(b.GlowColor, b.EmissionIntensityUI);
        img.fillAmount = Mathf.Clamp01(normalizedRemaining);
    }

    private void EnsurePortalIcon(Ball b)
    {
        if (!b) return;
        if (!_portalIconByBall.ContainsKey(b))
            RegisterPortalIcon(b);
    }
    // ================================================================

    private void HandleBallActivated(Ball b)
    {
        if (!ballRowParent || !b || _ballEntries.ContainsKey(b)) return;

        var go = ballEntryPrefab
            ? Instantiate(ballEntryPrefab, ballRowParent)
            : CreateFallbackEntry(ballRowParent);

        var entry = go.GetComponent<BallUIEntry>();
        entry.Init(b);
        _ballEntries[b] = entry;

        b.OnComboChanged -= OnBallComboChanged;
        b.OnComboChanged += OnBallComboChanged;

        entry.Refresh(b);
    }

    private void HandleBallDeactivated(Ball b)
    {
        if (!b) return;

        b.OnComboChanged -= OnBallComboChanged;

        if (_ballEntries.TryGetValue(b, out var entry))
        {
            if (entry) Destroy(entry.gameObject);
            _ballEntries.Remove(b);
        }

        UnregisterPortalIcon(b);
        UnregisterGrenadeIcon(b); // also clean grenade icon
    }

    private void OnBallComboChanged(Ball b)
    {
        if (b != null && _ballEntries.TryGetValue(b, out var entry) && entry != null)
            entry.Refresh(b);
    }

    private GameObject CreateFallbackEntry(Transform parent)
    {
        var root = new GameObject("BallEntry", typeof(RectTransform));
        root.transform.SetParent(parent, false);

        var imgGO = new GameObject("Dot", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        imgGO.transform.SetParent(root.transform, false);

        var txtGO = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        txtGO.transform.SetParent(root.transform, false);

        var entry = root.AddComponent<BallUIEntry>();
        entry.BindRuntime(imgGO.GetComponent<UnityEngine.UI.Image>(), txtGO.GetComponent<TMPro.TextMeshProUGUI>());
        return root;
    }

    // ===== Grenade row =====
    public void RegisterGrenadeIcon(Ball b)
    {
        if (!b || _grenadeIconByBall.ContainsKey(b)) return;
        if (!grenadeCooldownTemplate || !grenadeCooldownGroup) return;

        var clone = Instantiate(grenadeCooldownTemplate, grenadeCooldownGroup);
        clone.gameObject.name = $"GrenadeCooldown_{b.name}";
        clone.enabled = true;
        clone.type = Image.Type.Filled;
        clone.fillMethod = Image.FillMethod.Radial360;
        clone.fillOrigin = 2;
        clone.fillClockwise = true;
        clone.color = Boost(b.GlowColor, b.EmissionIntensityUI);
        clone.fillAmount = 0f;
        _grenadeIconByBall[b] = clone;
    }

    public void UnregisterGrenadeIcon(Ball b)
    {
        if (!b) return;
        if (_grenadeIconByBall.TryGetValue(b, out var img))
        {
            if (img) Destroy(img.gameObject);
        }
        _grenadeIconByBall.Remove(b);
    }

    public void SetBallGrenadeReady(Ball b, bool ready)
    {
        EnsureGrenadeIcon(b);
        var img = _grenadeIconByBall[b];
        img.enabled = true;
        img.color = Boost(b.GlowColor, b.EmissionIntensityUI);
        img.fillAmount = ready ? 1f : 0f; // filled when ready
    }

    public void SetBallGrenadeCooldown(Ball b, float normalizedRemaining)
    {
        EnsureGrenadeIcon(b);
        var img = _grenadeIconByBall[b];
        img.enabled = true;
        img.color = Boost(b.GlowColor, b.EmissionIntensityUI);
        img.fillAmount = Mathf.Clamp01(normalizedRemaining);
    }

    private void EnsureGrenadeIcon(Ball b)
    {
        if (!b) return;
        if (!_grenadeIconByBall.ContainsKey(b))
            RegisterGrenadeIcon(b);
    }

    private static Color Boost(Color c, float intensity)
    {
        float k = Mathf.Clamp(intensity, 0.5f, 2.5f);
        return new Color(
            Mathf.Clamp01(c.r * k),
            Mathf.Clamp01(c.g * k),
            Mathf.Clamp01(c.b * k),
            1f);
    }

    void OnDestroy()
    {
        Ball.OnBallActivated -= HandleBallActivated;
        Ball.OnBallDeactivated -= HandleBallDeactivated;

        foreach (var kv in _ballEntries)
            if (kv.Key != null)
                kv.Key.OnComboChanged -= OnBallComboChanged;

        foreach (var kv in _portalIconByBall)
            if (kv.Value) Destroy(kv.Value.gameObject);
        _portalIconByBall.Clear();

        foreach (var kv in _grenadeIconByBall)
            if (kv.Value) Destroy(kv.Value.gameObject);
        _grenadeIconByBall.Clear();
    }
}
```

## Assets/Scripts/PortalWarpController.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class PortalWarpController : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject PortalVisualPrefab;

    [Header("Raycast Layers")]
    public LayerMask LeftPlaneLayer;
    public LayerMask RightPlaneLayer;
    public LayerMask TopPlaneLayer;

    [Header("Distances")]
    [Min(0.1f)] public float MaxActiveDistance = 12f;
    [Min(0.01f)] public float TriggerDistance = 1.25f;
    [Min(0.01f)] public float InsideMargin = 0.35f;

    [Header("Impulse")]
    [Min(1f)] public float LateralImpulse = 28f;
    [Min(1f)] public float TopImpulse = 30f;

    [Header("Low-X Exit Boost (Left/Right Portals)")]
    [Tooltip("If lateral X speed magnitude is below this on exit, add extra X impulse away from the portal wall.")]
    [Min(0f)] public float LowXBoostThreshold = 3f;
    [Tooltip("Extra X direction impulse applied (VelocityChange) when below threshold.")]
    [Min(0f)] public float LowXBoostImpulse = 30f;

    private static int s_activeSlowmo;
    public static bool IsAnySlowmoActive => s_activeSlowmo > 0;

    [Header("Cooldown")]
    [Min(0.1f)] public float CooldownSeconds = 20f;

    private Pinball _pm;
    private Ball _ball;
    private Rigidbody _rb;
    [SerializeField] private Ball targetOverride;

    private GameObject _leftPortal;
    private GameObject _rightPortal;
    private GameObject _topPortal;

    private Coroutine _slowmoCR;

    private float _preScale = 1f;
    private float _preFixed = 0.02f;
    private bool _ownsSlowmo;

    [Header("Post FX (PPSv2)")]
    [SerializeField] public PostFXController postFX;

    private float _cooldownRemain;
    private bool _isReady = true;
    private float _debugCooldownTotal;

    private PinballUIM _ui;
    [SerializeField] public bool UseUi = false;

    void Awake()
    {
        _pm = Pinball.Instance;
        _ui = FindFirstObjectByType<PinballUIM>();
    }

    public void SetTarget(Ball ball)
    {
        targetOverride = ball;
        _ball = targetOverride;
        _rb = _ball ? _ball.GetComponent<Rigidbody>() : null;
        if (_ball)
            transform.SetParent(_ball.transform, false);
    }

    void OnEnable()
    {
        if (!_ball && targetOverride) SetTarget(targetOverride);
        if (!_ball)
        {
            var parentBall = GetComponentInParent<Ball>();
            if (parentBall) SetTarget(parentBall);
        }
        if (_ball) transform.SetParent(_ball.transform, false);

        if (_ui && !UseUi && _ball)
            _ui.RegisterPortalIcon(_ball);

        SetUiReady(true);
    }

    void OnDisable()
    {
        DestroyPortals();
        SetUiReady(false);

        if (_slowmoCR != null)
        {
            StopCoroutine(_slowmoCR);
            _slowmoCR = null;
        }

        // Restore timescale if this controller owns a slowmo
        if (_ownsSlowmo)
        {
            _ownsSlowmo = false;
            TimeScaleHub.End(this);            // was: manual Time.timeScale / fixedDelta restore
        }

        if (_ui && !UseUi && _ball)
            _ui.UnregisterPortalIcon(_ball);
    }

    void Update()
    {
        // BLOCK teleport logic unless actively playing (prevents reward-select overlap)
        if (_pm != null && _pm.CurrentState != PinballState.Play)
            return;

        if (_ball == null || !_ball.isActiveAndEnabled || !_ball.IsActive || _rb == null)
            return;

        transform.position = _ball.transform.position;
        transform.rotation = Quaternion.identity;

        TickCooldownUI();

        if (!_isReady)
            return;

        var pos = transform.position;

        if (Physics.Raycast(pos, Vector3.left, out var hitL, MaxActiveDistance, LeftPlaneLayer, QueryTriggerInteraction.Collide))
        {
            HandlePortalVisual(ref _leftPortal, hitL, AxisSide.Left);
            if (hitL.distance <= TriggerDistance)
                TryTeleportLeftToRight(pos, hitL);
        }
        else DestroyPortal(ref _leftPortal);

        if (Physics.Raycast(pos, Vector3.right, out var hitR, MaxActiveDistance, RightPlaneLayer, QueryTriggerInteraction.Collide))
        {
            HandlePortalVisual(ref _rightPortal, hitR, AxisSide.Right);
            if (hitR.distance <= TriggerDistance)
                TryTeleportRightToLeft(pos, hitR);
        }
        else DestroyPortal(ref _rightPortal);

        if (Physics.Raycast(pos, Vector3.forward, out var hitT, MaxActiveDistance, TopPlaneLayer, QueryTriggerInteraction.Collide))
        {
            HandlePortalVisual(ref _topPortal, hitT, AxisSide.Top);
            if (hitT.distance <= TriggerDistance)
                TryTeleportTopMirrorX(hitT);
        }
        else DestroyPortal(ref _topPortal);
    }

    private enum AxisSide { Left, Right, Top }

    private void HandlePortalVisual(ref GameObject portal, RaycastHit hit, AxisSide side)
    {
        if (!PortalVisualPrefab) return;
        float t = Mathf.InverseLerp(MaxActiveDistance, TriggerDistance, hit.distance);
        if (t <= 0f)
        {
            DestroyPortal(ref portal);
            return;
        }
        if (!portal) portal = Instantiate(PortalVisualPrefab);
        portal.transform.position = hit.point;
        portal.transform.rotation = Quaternion.identity;
        float s = Mathf.Lerp(.25f, 1.75f, t);
        Vector3 baseScale = side == AxisSide.Top ? new Vector3(1.6f, 1.6f, 0.12f) : new Vector3(0.12f, 1.6f, 1.6f);
        portal.transform.localScale = baseScale * s;
        if (_ball != null) TintPortalToGlowColor(portal, _ball.GlowColor);
    }

    private static readonly int _Color_PROP = Shader.PropertyToID("_Color");
    private static readonly int _BaseColor_PROP = Shader.PropertyToID("_BaseColor");
    private static readonly int _EmissionColor_PROP = Shader.PropertyToID("_EmissionColor");

    private void TintPortalToGlowColor(GameObject portal, Color c)
    {
        var rends = portal.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (!r) continue;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(_Color_PROP, c);
            mpb.SetColor(_BaseColor_PROP, c);
            var emissive = (Color)(c * Mathf.LinearToGammaSpace(1.2f));
            mpb.SetColor(_EmissionColor_PROP, emissive);
            r.SetPropertyBlock(mpb);
        }
    }

    private void DestroyPortal(ref GameObject portal)
    {
        if (portal) Destroy(portal);
        portal = null;
    }

    private void DestroyPortals()
    {
        DestroyPortal(ref _leftPortal);
        DestroyPortal(ref _rightPortal);
        DestroyPortal(ref _topPortal);
    }

    private void StartCooldown()
    {
        _debugCooldownTotal = 0f;
        _cooldownRemain = CooldownSeconds;
        _isReady = false;
        SetUiReady(false);
        SetUiCooldown(1f);
    }

    public void ForceCooldown(float seconds)
    {
        seconds = Mathf.Max(0.01f, seconds);
        _debugCooldownTotal = seconds;
        _cooldownRemain = seconds;
        _isReady = false;
        SetUiReady(false);
        SetUiCooldown(1f);
    }

    private void TickCooldownUI()
    {
        if (_isReady) return;
        if (_cooldownRemain > 0f)
        {
            _cooldownRemain -= Time.deltaTime;
            if (_cooldownRemain <= 0f)
                _cooldownRemain = 0f;

            float denom = _debugCooldownTotal > 0f ? _debugCooldownTotal : CooldownSeconds;
            float norm = denom > 0f ? (_cooldownRemain / denom) : 0f;
            SetUiCooldown(norm);

            if (_cooldownRemain <= 0f)
            {
                _isReady = true;
                _debugCooldownTotal = 0f;
                SetUiReady(true);
            }
        }
    }

    private void SetUiReady(bool ready)
    {
        if (!_ui) return;
        if (UseUi) _ui.SetPortalWarpReady(ready);
        else if (_ball) _ui.SetBallPortalReady(_ball, ready);
    }

    private void SetUiCooldown(float normalizedRemaining)
    {
        if (!_ui) return;
        if (UseUi) _ui.SetPortalWarpCooldown(normalizedRemaining);
        else if (_ball) _ui.SetBallPortalCooldown(_ball, normalizedRemaining);
    }

    private void TryTeleportLeftToRight(Vector3 origin, RaycastHit hitL)
    {
        if (Physics.Raycast(origin, Vector3.right, out var hitR, Mathf.Infinity, RightPlaneLayer, QueryTriggerInteraction.Collide))
        {
            var destR = hitR.point - Vector3.right * InsideMargin;
            DoTeleport(destR, Vector3.right, LateralImpulse);
        }
    }
    private void TryTeleportRightToLeft(Vector3 origin, RaycastHit hitR)
    {
        if (Physics.Raycast(origin, Vector3.left, out var hitL, Mathf.Infinity, LeftPlaneLayer, QueryTriggerInteraction.Collide))
        {
            var destL = hitL.point + Vector3.right * InsideMargin;
            DoTeleport(destL, Vector3.left, LateralImpulse);
        }
    }
    private void TryTeleportTopMirrorX(RaycastHit hitT)
    {
        var col = hitT.collider;
        if (!col) return;
        var center = col.bounds.center;
        float dx = hitT.point.x - center.x;
        float mirroredX = center.x - dx;
        Vector3 dest = new Vector3(mirroredX, _ball.transform.position.y, hitT.point.z);
        dest -= hitT.normal * InsideMargin;
        DoTeleport(dest, Vector3.back, TopImpulse);
    }

    private void DoTeleport(Vector3 destPosition, Vector3 exitDir, float exitImpulse)
    {
        if (_ball == null || _rb == null) return;
        StartCooldown();
        DestroyPortals();

        Vector3 prevVel = _rb.velocity; prevVel.y = 0f;
        Vector3 inward = exitDir.sqrMagnitude > 1e-6f ? exitDir.normalized : Vector3.forward;
        inward.y = 0f;
        float ballR = GetBallRadius();
        float need = (ballR + 0.05f) - InsideMargin;
        float extra = need > 0f ? need : 0f;
        Vector3 proposed = destPosition + inward * extra;
        Vector3 safePos = ResolvePenetrationXZ(proposed, 5, 0.003f);
        _rb.position = safePos;
        Physics.SyncTransforms();

        bool isLateral = Mathf.Abs(inward.x) > 0.5f;
        Vector3 preferredDir;
        float targetMinSpeed;
        float preserveXAbs = -1f;
        float xSign = 0f;

        if (isLateral)
        {
            const float axisEps = 0.05f;
            float zAbs = Mathf.Abs(prevVel.z);
            float xAbs = Mathf.Abs(prevVel.x);
            if (zAbs > axisEps)
            {
                float zSign = Mathf.Sign(prevVel.z);
                targetMinSpeed = zAbs + Mathf.Max(0f, exitImpulse);
                preferredDir = (zSign >= 0f) ? Vector3.forward : Vector3.back;
                _rb.velocity = new Vector3(prevVel.x, 0f, zSign * targetMinSpeed);
            }
            else
            {
                float xSgn = (xAbs > 0.0001f) ? Mathf.Sign(prevVel.x) : 1f;
                targetMinSpeed = xAbs + Mathf.Max(0f, exitImpulse);
                preferredDir = (xSgn >= 0f) ? Vector3.right : Vector3.left;
                _rb.velocity = new Vector3(xSgn * targetMinSpeed, 0f, prevVel.z);
            }

            // NEW: ensure lateral X movement away from the portal wall when nearly stalled in X
            if (LowXBoostImpulse > 0f && Mathf.Abs(_rb.velocity.x) < LowXBoostThreshold)
            {
                // exitDir.x > 0 means we're at the Right wall; kick leftwards (negative X).
                // exitDir.x < 0 means we're at the Left wall; kick rightwards (positive X).
                float kickSign = (inward.x > 0f) ? -1f : 1f;
                _rb.AddForce(new Vector3(kickSign * LowXBoostImpulse, 0f, 0f), ForceMode.VelocityChange);
            }
        }
        else
        {
            float prevZAbs = Mathf.Abs(prevVel.z);
            float targetDown = prevZAbs + Mathf.Max(0f, exitImpulse);
            _rb.velocity = new Vector3(prevVel.x, 0f, -targetDown);
            preferredDir = Vector3.back;
            targetMinSpeed = targetDown;
            preserveXAbs = Mathf.Abs(prevVel.x);
            xSign = Mathf.Sign(prevVel.x);
        }

        StartCoroutine(TempIgnorePortalWalls(0.12f));
        _pm?.ScreenShake();
        _ball.ActivatePortalBoost();

        float targetScale = 0.05f, pulseHold = 0.55f, easeOut = 0.04f;
        if (_slowmoCR != null) StopCoroutine(_slowmoCR);
        _slowmoCR = StartCoroutine(SlowMoPulseRealtime(
            targetScale, pulseHold, easeOut,
            targetMinSpeed, preferredDir,
            preserveXAbs, xSign
        ));

        if (postFX)
        {
            postFX.VignetteMax = 0.55f;
            postFX.SetVignette(0f);
            postFX.FadeVignette(0.28f, 0.06f);
            postFX.ChromaticPulse(0.30f, 0.06f, 0.14f);
        }
    }

    private Vector3 ResolvePenetrationXZ(Vector3 proposed, int maxIterations, float skin)
    {
        var ballCol = _ball.GetComponent<Collider>();
        if (!ballCol) return proposed;

        Vector3 pos = proposed;
        for (int iter = 0; iter < maxIterations; iter++)
        {
            bool penetrated = false;
            float r = GetBallRadius() * 1.02f;
            var hits = Physics.OverlapSphere(pos, r, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                var other = hits[i];
                if (!other || other.transform == _ball.transform) continue;

                if (Physics.ComputePenetration(
                    ballCol, pos, _ball.transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 depenDir, out float depenDist))
                {
                    depenDir.y = 0f;
                    if (depenDir.sqrMagnitude < 1e-6f) continue;
                    depenDir.Normalize();
                    pos += depenDir * (depenDist + skin);
                    penetrated = true;
                }
            }

            if (!penetrated) break;
        }
        return pos;
    }

    private float GetBallRadius()
    {
        var col = _ball.GetComponent<Collider>();
        if (!col) return 0.25f;
        var e = col.bounds.extents;
        return Mathf.Max(e.x, e.z);
    }

    private IEnumerator TempIgnorePortalWalls(float seconds)
    {
        int ballLayer = _ball.gameObject.layer;
        for (int i = 0; i < 32; i++)
        {
            bool isPortalLayer =
                ((LeftPlaneLayer.value & (1 << i)) != 0) ||
                ((RightPlaneLayer.value & (1 << i)) != 0) ||
                ((TopPlaneLayer.value & (1 << i)) != 0);
            if (isPortalLayer)
                Physics.IgnoreLayerCollision(ballLayer, i, true);
        }

        yield return new WaitForSeconds(seconds);

        for (int i = 0; i < 32; i++)
        {
            bool isPortalLayer =
                ((LeftPlaneLayer.value & (1 << i)) != 0) ||
                ((RightPlaneLayer.value & (1 << i)) != 0) ||
                ((TopPlaneLayer.value & (1 << i)) != 0);
            if (isPortalLayer)
                Physics.IgnoreLayerCollision(ballLayer, i, false);
        }
    }

    private IEnumerator SlowMoPulseRealtime(float targetScale, float holdRealtime, float easeOutRealtime,
                                            float targetMinSpeed, Vector3 preferredDir,
                                            float preserveXAbs, float xSign)
    {
        targetScale = Mathf.Clamp(targetScale, 0.05f, 1f);
        holdRealtime = Mathf.Max(0f, holdRealtime);
        easeOutRealtime = Mathf.Max(0.01f, easeOutRealtime);

        // Acquire hub slow-mo (handles fixedDelta)
        _ownsSlowmo = true;
        TimeScaleHub.Begin(this, targetScale, affectFixedDelta: true);

        float end = Time.realtimeSinceStartup + holdRealtime;
        while (Time.realtimeSinceStartup < end)
        {
            if (_pm != null && _pm.CurrentState != PinballState.Play)
                break; // abort if state changed
            yield return null;
        }

        // Simple wait for ease-out duration (cosmetic, not tweening back manually)
        yield return new WaitForSecondsRealtime(easeOutRealtime);

        // Release slow-mo
        _ownsSlowmo = false;
        TimeScaleHub.End(this);

        // Post-teleport velocity normalization (unchanged)
        if (_rb != null && _ball != null && _ball.isActiveAndEnabled)
        {
            Vector3 v = _rb.velocity; v.y = 0f;
            Vector3 dirN = preferredDir.sqrMagnitude > 1e-6f ? preferredDir.normalized : Vector3.forward;
            float curAlong = Vector3.Dot(v, dirN);
            float curAlongAbs = Mathf.Abs(curAlong);
            if (curAlongAbs < targetMinSpeed)
            {
                float delta = targetMinSpeed - curAlongAbs;
                _rb.AddForce(dirN * delta, ForceMode.VelocityChange);
            }
            if (preserveXAbs >= 0.0f && Mathf.Abs(v.x) < preserveXAbs && xSign != 0f)
            {
                float deltaX = preserveXAbs - Mathf.Abs(v.x);
                _rb.AddForce(new Vector3(xSign * deltaX, 0f, 0f), ForceMode.VelocityChange);
            }
        }

        _slowmoCR = null;
    }

    // ForceStopSlowmo(): simplified
    public void ForceStopSlowmo()
    {
        if (_slowmoCR != null)
        {
            StopCoroutine(_slowmoCR);
            _slowmoCR = null;
        }
        if (_ownsSlowmo)
        {
            _ownsSlowmo = false;
            TimeScaleHub.End(this);           // was: decrement s_activeSlowmo & manual restore
        }
    }
}
```

## Assets/Scripts/PortalWarpRewardRuntime.cs

```csharp
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PortalWarpRewardRuntime : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject PortalVisualPrefab;

    [Header("Raycast Layers")]
    public LayerMask LeftPlaneLayer;
    public LayerMask RightPlaneLayer;
    public LayerMask TopPlaneLayer;

    [Header("Distances")]
    [Min(0.1f)] public float MaxActiveDistance = 12f;
    [Min(0.01f)] public float TriggerDistance = 1.25f;
    [Min(0.01f)] public float InsideMargin = 0.35f;

    [Header("Impulse")]
    [Min(1f)] public float LateralImpulse = 28f;
    [Min(1f)] public float TopImpulse = 30f;

    [Header("Cooldown")]
    [Min(0.1f)] public float CooldownSeconds = 20f;

    [Header("Post FX (PPSv2)")]
    public PostFXController postFX;

    private readonly Dictionary<Ball, PortalWarpController> _controllers = new();
    private Pinball _pm;

    void Awake() => _pm = Pinball.Instance;

    void OnEnable()
    {
        Ball.OnBallActivated += HandleBallActivated;
        Ball.OnBallDeactivated += HandleBallDeactivated;
        EnsureControllersForExistingBalls();
    }

    void OnDisable()
    {
        Ball.OnBallActivated -= HandleBallActivated;
        Ball.OnBallDeactivated -= HandleBallDeactivated;
        DestroyAllControllers();
    }

    public void RebindAll()
    {
        DestroyAllControllers();
        EnsureControllersForExistingBalls();
    }

    private void EnsureControllersForExistingBalls()
    {
        var balls = FindObjectsByType<Ball>(FindObjectsSortMode.None);
        for (int i = 0; i < balls.Length; i++)
            if (balls[i] && balls[i].isActiveAndEnabled && balls[i].IsActive)
                TryAddController(balls[i]);
    }

    private void HandleBallActivated(Ball b)
    {
        if (!b) return;
        TryAddController(b);
    }

    private void HandleBallDeactivated(Ball b)
    {
        if (!b) return;
        TryRemoveController(b);
    }

    private void TryAddController(Ball ball)
    {
        if (!ball || _controllers.ContainsKey(ball)) return;

        var go = new GameObject("PortalWarpController (Ball)");
        go.transform.SetParent(ball.transform, false);
        var ctrl = go.AddComponent<PortalWarpController>();

        ctrl.PortalVisualPrefab = PortalVisualPrefab;
        ctrl.LeftPlaneLayer = LeftPlaneLayer;
        ctrl.RightPlaneLayer = RightPlaneLayer;
        ctrl.TopPlaneLayer = TopPlaneLayer;
        ctrl.MaxActiveDistance = MaxActiveDistance;
        ctrl.TriggerDistance = TriggerDistance;
        ctrl.InsideMargin = InsideMargin;
        ctrl.LateralImpulse = LateralImpulse;
        ctrl.TopImpulse = TopImpulse;
        ctrl.CooldownSeconds = CooldownSeconds;
        ctrl.postFX = postFX;
        ctrl.SetTarget(ball);

        // Only main ball uses anchor HUD
        ctrl.UseUi = (_pm != null && _pm.ball == ball);

        _controllers[ball] = ctrl;
    }

    private void TryRemoveController(Ball ball)
    {
        if (!ball) return;
        if (_controllers.TryGetValue(ball, out var ctrl))
        {
            if (ctrl) Destroy(ctrl.gameObject);
            _controllers.Remove(ball);
        }
    }

    public void ForceGlobalCooldown(float seconds)
    {
        foreach (var kv in _controllers)
            if (kv.Value) kv.Value.ForceCooldown(seconds);
    }

    public void CancelAllSlowmo() // NEW: used when entering paused states
    {
        foreach (var kv in _controllers)
            if (kv.Value) kv.Value.ForceStopSlowmo();
    }

    private void DestroyAllControllers()
    {
        foreach (var kv in _controllers)
            if (kv.Value) Destroy(kv.Value.gameObject);
        _controllers.Clear();
    }
}
```

## Assets/Scripts/PostFXController.cs

```csharp
using UnityEngine;
using DG.Tweening;
using UnityEngine.Rendering.PostProcessing; // PPSv2

[DisallowMultipleComponent]
public class PostFXController : MonoBehaviour
{
    [Header("Volume / Profile")]
    [SerializeField] private PostProcessVolume volume;

    [Header("Vignette")]
    [SerializeField, Range(0f, 1f)] private float vignetteMax = 0.55f;
    [SerializeField] private float vignetteSmoothness = 0.65f;
    [SerializeField] private bool vignetteRounded = false;

    [Header("Chromatic Aberration")]
    [SerializeField, Range(0f, 1f)] private float chromaMax = 0.35f;

    [Header("Bloom (Explosion Pulse)")]
    [SerializeField, Range(0f, 15f)] private float bloomMax = 6f;
    [SerializeField] private float bloomBaseIntensity = 0f;

    private Vignette _vig;
    private ChromaticAberration _ca;
    private Bloom _bloom;                     // NEW

    private float _vigLogical;                // 0..1 logical vignette
    private Tween _vigTween, _caTween, _bloomTween;

    void Awake()
    {
        if (!volume) volume = GetComponent < PostProcessVolume>();
        if (!volume || !volume.profile)
        {
            Debug.LogWarning("[PostFXController_PPSv2] Assign a PostProcessVolume with a profile.");
            return;
        }

        volume.profile.TryGetSettings(out _vig);
        volume.profile.TryGetSettings(out _ca);
        volume.profile.TryGetSettings(out _bloom); // NEW

        if (_vig != null)
        {
            _vig.intensity.overrideState = true;
            _vig.smoothness.overrideState = true;
            _vig.rounded.overrideState = true;
            _vig.smoothness.value = vignetteSmoothness;
            _vig.rounded.value = vignetteRounded;
            SetVignette(0f);
        }

        if (_ca != null)
        {
            _ca.intensity.overrideState = true;
            _ca.intensity.value = 0f;
        }

        if (_bloom != null)
        {
            _bloom.intensity.overrideState = true;
            _bloom.intensity.value = bloomBaseIntensity;
        }
    }

    public void SetVignette(float logical01)
    {
        _vigLogical = Mathf.Clamp01(logical01);
        if (_vig != null) _vig.intensity.value = _vigLogical * vignetteMax;
    }

    public void FadeVignette(float logical01, float seconds)
    {
        logical01 = Mathf.Clamp01(logical01);
        _vigTween?.Kill(false);
        float start = _vigLogical;
        _vigTween = DOTween.To(() => start, v => { start = v; SetVignette(v); },
                               logical01, seconds).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void ClearVignette(float seconds = 0.12f) => FadeVignette(0f, seconds);

    public void ChromaticPulse(float peak = 1.25f, float up = 0.06f, float down = 0.14f)
    {
        if (_ca == null) return;
        peak = Mathf.Clamp(peak, 0f, chromaMax);
        _caTween?.Kill(false);

        _caTween = DOTween.To(() => _ca.intensity.value, v => _ca.intensity.value = v,
                              peak, up).SetEase(Ease.OutQuad).SetUpdate(true)
            .OnComplete(() =>
            {
                _caTween = DOTween.To(() => _ca.intensity.value, v => _ca.intensity.value = v,
                                      0f, down).SetEase(Ease.InQuad).SetUpdate(true);
            });
    }

    // NEW: bloom pulse for explosions
    public void BloomPulse(float peakFraction, float upTime, float downTime)
    {
        if (_bloom == null) return;
        peakFraction = Mathf.Clamp01(peakFraction);
        float peak = bloomBaseIntensity + bloomMax * peakFraction;

        _bloomTween?.Kill(false);
        _bloomTween = DOTween.Sequence()
            .Append(DOTween.To(() => _bloom.intensity.value, v => _bloom.intensity.value = v,
                               peak, upTime).SetEase(Ease.OutQuad))
            .Append(DOTween.To(() => _bloom.intensity.value, v => _bloom.intensity.value = v,
                               bloomBaseIntensity, downTime).SetEase(Ease.InQuad))
            .SetUpdate(true);
    }

    // Exposed tweakables
    public float VignetteMax { get => vignetteMax; set => vignetteMax = Mathf.Clamp01(value); }
    public float ChromaMax { get => chromaMax; set => chromaMax = Mathf.Clamp01(value); }
    public float BloomMax { get => bloomMax; set => bloomMax = Mathf.Max(0f, value); }
    public float BloomBaseIntensity { get => bloomBaseIntensity; set => bloomBaseIntensity = Mathf.Max(0f, value); }
}
```

## Assets/Scripts/PowerupPickup.cs

```csharp
using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public sealed class PowerupPickup : MonoBehaviour
{
    [Tooltip("Powerup Id carried by this pickup (e.g., 'collect-all-xp').")]
    public string powerupId;

    private bool _collected; // prevents double-trigger

    [Header("Behaviour")]
    [Tooltip("Seconds before auto-despawn if not collected.")]
    public float lifetime = 10f;

    [Tooltip("Optional initial impulse to make the pickup pop out.")]
    public float spawnImpulse = 2.5f;

    [Header("Feedback")]
    public ParticleSystem pickupVfx;
    public AudioSource audioSource;
    public AudioClip pickupSfx;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void OnEnable()
    {
        if (lifetime > 0f)
            Destroy(gameObject, lifetime);

        // Tiny outward push
        var rb = GetComponent<Rigidbody>();
        if (rb && spawnImpulse > 0f)
        {
            var dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y); // slight up bias
            rb.AddForce(dir.normalized * spawnImpulse, ForceMode.Impulse);
        }

        // Spawn animation
        var tween = GetComponent<PowerupPickupTween>();
        if (tween) tween.PlaySpawn();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_collected) return;

        var pinball = Pinball.Instance;
        if (!pinball) return;

        // Any Ball collects
        var ball = other.GetComponentInParent<Ball>();
        if (!ball || !ball.isActiveAndEnabled || !ball.IsActive) return;

        if (string.IsNullOrEmpty(powerupId))
        {
            Debug.LogWarning("[PowerupPickup] powerupId is empty. Ensure PowerupSystem assigned it at spawn.");
            return;
        }

        bool ok = PowerupSystem.TryTriggerById(pinball, powerupId, transform.position);
        if (!ok) return;

        _collected = true;

        // Collect feedback
        var tween = GetComponent<PowerupPickupTween>();
        if (tween) tween.PlayCollect();
        if (pickupVfx) Instantiate(pickupVfx, transform.position, Quaternion.identity);

        var col = GetComponent<Collider>();
        if (col) col.enabled = false;

        float delay = 0.05f; // minimum so tween is visible
        if (tween) delay = Mathf.Max(delay, tween.GetCollectDuration());
        if (audioSource && pickupSfx)
        {
            audioSource.PlayOneShot(pickupSfx);
            delay = Mathf.Max(delay, pickupSfx.length);
        }

        Destroy(gameObject, delay);
    }
}
```

## Assets/Scripts/PowerupSystem.cs

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class PowerupSystem
{
    private static readonly List<IPowerup> _registry = new();
    private static bool _initialized;

    private const string PickupResourcePath = "Powerups/PowerupPickup"; // Resources/Powerups/PowerupPickup.prefab
    private static GameObject _pickupPrefab; // cached after first load

    // Scans assemblies once to auto-register IPowerup implementations with parameterless constructors.
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int ai = 0; ai < asms.Length; ai++)
            {
                var asm = asms[ai];
                if (asm == null || asm.IsDynamic) continue;

                Type[] types = null;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }

                if (types == null) continue;
                for (int ti = 0; ti < types.Length; ti++)
                {
                    var t = types[ti];
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(IPowerup).IsAssignableFrom(t)) continue;

                    var ctor = t.GetConstructor(Type.EmptyTypes);
                    if (ctor == null) continue;

                    try
                    {
                        var instance = (IPowerup)Activator.CreateInstance(t);
                        Register(instance);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PowerupSystem] Failed to instantiate {t?.FullName}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PowerupSystem] Reflection scan failed: {ex}");
        }

        Debug.Log($"[PowerupSystem] Registered {_registry.Count} powerups.");
    }

    // Adds a powerup to the registry if not already present by id.
    public static void Register(IPowerup powerup)
    {
        if (powerup == null) return;
        if (_registry.Exists(p => p.Id == powerup.Id)) return;
        _registry.Add(powerup);
    }

    // Rolls whether a pickup should drop using the context�s configured chance.
    public static bool TryRoll(IRunContext ctx)
    {
        float chance = GetDropChance(ctx);
        return UnityEngine.Random.value < chance;
    }

    // Returns the clamped drop chance from the Pinball singleton or a default.
    public static float GetDropChance(IRunContext ctx)
    {
        float baseChance = Pinball.Instance != null ? Pinball.Instance.PowerupDropChance : 0.03f;
        return Mathf.Clamp01(baseChance);
    }

    // Triggers a specific powerup by id if eligible for the given Pinball.
    public static bool TryTriggerById(Pinball pm, string id, Vector3 triggerPos)
    {
        EnsureInitialized();
        for (int i = 0; i < _registry.Count; i++)
        {
            var p = _registry[i];
            if (p == null) continue;
            if (p.Id == id && p.CanTrigger(pm))
            {
                Debug.Log($"[PowerupSystem] Triggering: {p.DebugLabel} @ {triggerPos}");
                p.Execute(pm, triggerPos);
                return true;
            }
        }
        return false;
    }

    // Rolls, picks a weighted eligible powerup, and spawns a pickup at the given position.
    public static bool TrySpawnPickupOnHit(Pinball pm, Vector3 pos, IRunContext ctx)
    {
        EnsureInitialized();

        if (!TryRoll(ctx))
            return false;

        var eligibles = ListPool<IPowerup>.Get();
        try
        {
            for (int i = 0; i < _registry.Count; i++)
            {
                var p = _registry[i];
                if (p != null && p.CanTrigger(pm))
                    eligibles.Add(p);
            }
            if (eligibles.Count == 0)
                return false;

            var picked = PickWeighted(eligibles);
            return SpawnPickup(picked.Id, pos);
        }
        finally
        {
            ListPool<IPowerup>.Release(eligibles);
        }
    }

    // Instantiates the pickup prefab from Resources and sets its powerup id.
    private static bool SpawnPickup(string powerupId, Vector3 pos)
    {
        if (_pickupPrefab == null)
        {
            _pickupPrefab = Resources.Load<GameObject>(PickupResourcePath);
            if (_pickupPrefab == null)
            {
                Debug.LogWarning($"[PowerupSystem] Failed to load pickup prefab at Resources/{PickupResourcePath}");
                return false;
            }
        }

        var go = UnityEngine.Object.Instantiate(_pickupPrefab, pos, Quaternion.identity);
        var pickup = go.GetComponent<PowerupPickup>();
        if (pickup == null)
        {
            Debug.LogWarning("[PowerupSystem] Instantiated pickup prefab is missing PowerupPickup component.");
            return false;
        }
        pickup.powerupId = powerupId;
        return true;
    }

    // Picks a powerup from a list using their Weight properties.
    private static IPowerup PickWeighted(List<IPowerup> items)
    {
        float total = 0f;
        for (int i = 0; i < items.Count; i++)
            total += Mathf.Max(.0001f, items[i].Weight);

        float r = UnityEngine.Random.value * total;
        float accum = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            accum += Mathf.Max(.0001f, items[i].Weight);
            if (r <= accum)
                return items[i];
        }
        return items[items.Count - 1];
    }

    // Small GC-free list pool for temporary allocations.
    private static class ListPool<T>
    {
        private static readonly Stack<List<T>> Pool = new();

        public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>();

        public static void Release(List<T> list)
        {
            list.Clear();
            Pool.Push(list);
        }
    }
}
```

## Assets/Scripts/RewardCatalogSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Reward Catalog")]
public class RewardCatalogSO : ScriptableObject
{
    [Tooltip("All reward assets available for this mode")]
    public List<RewardSO> allRewards = new List<RewardSO>();
}

```

## Assets/Scripts/RewardCategory.cs

```csharp
public enum RewardCategory
{
    ScoreMultiplier,
    XPMultiplier,
    PhysicsFX,
    BallFX,
    PaddleFX,
    BumperFX,
    LifeFX,
    Abilities
}

```

## Assets/Scripts/RewardRarity.cs

```csharp
public enum RewardRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
    Artifact,
    Cursed
}

```

## Assets/Scripts/RewardSO.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class RewardSO : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string rewardID;
    [SerializeField] private string displayName;
    [TextArea, SerializeField] private string description;

    [Header("Classification")]
    [SerializeField] private RewardCategory category;
    [SerializeField] private RewardRarity rarity;
    [SerializeField] public bool isPaddleReward;

    [Header("Behavior")]
    [Tooltip("If true, this reward should not be offered if an instance is currently active")]
    [SerializeField] private bool blockWhenActive = true;
    [SerializeField] private bool canStack = true;

    [Header("Scaling")]
    [SerializeField] private bool scalable = false;
    [SerializeField] private RewardSO replacesReward;

    [Tooltip("Exclusive group key. Rewards sharing same key cannot co-exist")]
    [SerializeField] private string exclusivityKey;
    [SerializeField] private List<string> blockedKeys;

    public string Id => rewardID;
    public string Name => displayName;
    public string Description => description;
    public RewardCategory Category => category;
    public RewardRarity Rarity => rarity;
    public bool BlockWhenActive => blockWhenActive;
    public bool CanStack => canStack;
    public string ExclusivityKey => exclusivityKey;
    public List<string> BlockedKeys => blockedKeys;
    public bool Scalable => scalable;
    public RewardSO ReplacesReward => replacesReward;

    // Rarity color map (kept simple & centralized)
    public static Color GetRarityColor(RewardRarity r)
    {
        // Common gray, Uncommon green, Rare blue, Epic magenta-purple,
        // Legendary orange, Artifact pink, Cursed dark purple.
        return r switch
        {
            RewardRarity.Common => new Color32(160, 160, 160, 255),
            RewardRarity.Uncommon => new Color32(80, 200, 120, 255),
            RewardRarity.Rare => new Color32(80, 120, 255, 255),
            RewardRarity.Epic => new Color32(180, 0, 200, 255),
            RewardRarity.Legendary => new Color32(255, 140, 0, 255),
            RewardRarity.Artifact => new Color32(255, 105, 180, 255),
            RewardRarity.Cursed => new Color32(80, 0, 120, 255),
            _ => Color.white
        };
    }

    public virtual bool IsEligible(IRunContext ctx)
    {
        if (!Scalable)
        {
            if (BlockWhenActive && ctx.IsActive(Id))
                return false;
            if (ctx.IsAvailable(Id) && !CanStack)
                return false;
        }

        if (Scalable && ReplacesReward != null && !ctx.Owns(ReplacesReward.Id))
            return false;

        if (Scalable && Rarity == RewardRarity.Common && ctx.Owns(Id))
            return false;

        if (blockedKeys != null && blockedKeys.Count > 0)
        {
            foreach (var activeKey in ctx.ActiveKeys)
                if (blockedKeys.Contains(activeKey)) return false;
        }

        if (!string.IsNullOrEmpty(ExclusivityKey) && ctx.HasExclusiveKeyActive(ExclusivityKey))
            return false;

        return true;
    }

    public abstract void Apply(IRunContext ctx);

    public virtual void ApplyToPaddle(PaddleElementalState state) { }
}
```

## Assets/Scripts/RicochetPowerup.cs

```csharp
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class RicochetPowerup : IPowerup
{
    public string Id => "ricochet";
    public float Weight => 1f;
    public string DebugLabel => "Ricochet";
    public bool CanTrigger(IRunContext ctx) => true;

    public void Execute(Pinball pinball, Vector3 triggerPos)
    {
        if (!pinball) return;

        var balls = Object.FindObjectsOfType<Ball>();
        Ball picked = null;
        float best = float.PositiveInfinity;
        for (int i = 0; i < balls.Length; i++)
        {
            var b = balls[i];
            if (!b || !b.isActiveAndEnabled || !b.IsActive) continue;
            float d2 = (b.transform.position - triggerPos).sqrMagnitude;
            if (d2 < best) { best = d2; picked = b; }
        }

        if (!picked) return;

        var assist = picked.GetComponent<RicochetAssist>();
        if (!assist) assist = picked.gameObject.AddComponent<RicochetAssist>();

        assist.Arm();
        pinball.ScreenShake();
    }
}

[DisallowMultipleComponent]
public sealed class RicochetAssist : MonoBehaviour
{
    [Header("Ricochet Timing")]
    [SerializeField] private float windowSeconds = 1.2f;

    [Header("Speed")]
    [SerializeField] private float initialRicochetSpeed = 20f;
    [SerializeField] private float speedIncrementPerHit = 2f;
    [SerializeField] private float redirectMaxSpeed = 40f;
    [SerializeField] private bool smoothAcceleration = true;
    [SerializeField, Range(0f, 1f)] private float accelerationLerp = 0.35f;
    [SerializeField] private bool clampEveryFrame = true;

    [Header("Directional / Safety")]
    [SerializeField] private float postRedirectNudge = 0.18f;
    [SerializeField] private float ignorePrevColliderSeconds = 0.10f;
    [SerializeField] private float realignAngleThreshold = 25f;

    private Rigidbody _rb;
    private Collider _ballCol;

    private bool _armed;
    private float _activeUntil;

    private readonly HashSet<Bumper> _affected = new();
    private Coroutine _windowCR;

    private int _ricochetHitCount;
    private float _desiredSpeed;

    // Deferred redirect
    private bool _redirectPending;
    private Bumper _pendingTarget;
    private Vector3 _pendingDir;
    private float _pendingSpeed;

    // All bumpers physically hit during window (NEVER revisit)
    private readonly HashSet<Bumper> _visited = new();

    public bool IsActive => !_armed && Time.time < _activeUntil;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _ballCol = GetComponent<Collider>();
    }

    public void Arm()
    {
        _armed = true;
        _activeUntil = 0f;
        _ricochetHitCount = 0;
        _desiredSpeed = initialRicochetSpeed;
        _redirectPending = false;
        _pendingTarget = null;
        _visited.Clear();
        if (_windowCR != null) { StopCoroutine(_windowCR); _windowCR = null; }
        _affected.Clear();
    }

    public void EndRicochet()
    {
        if (!IsActive && !_armed && _affected.Count == 0) return;
        _activeUntil = 0f;
        EndAllRicochetLights();
        _redirectPending = false;
        if (_windowCR != null) { StopCoroutine(_windowCR); _windowCR = null; }
    }

    void OnDisable() => EndRicochet();

    private void EndAllRicochetLights()
    {
        foreach (var b in _affected)
            if (b) b.EndRicochetLight();
        _affected.Clear();
    }

    void OnCollisionEnter(Collision c)
    {
        var bumper = c.collider ? c.collider.GetComponentInParent<Bumper>() : null;
        if (!bumper) return;

        // First hit arms ricochet
        if (_armed)
        {
            _armed = false;
            _activeUntil = Time.time + Mathf.Max(0.05f, windowSeconds);
            _windowCR = StartCoroutine(WindowWatcher());
            _ricochetHitCount = 0;
            _desiredSpeed = initialRicochetSpeed;
        }

        if (!IsActive) return;

        // Record visited bumper (never return to it)
        _visited.Add(bumper);
        _affected.Add(bumper);

        PrepareRedirect(bumper);
    }

    private void PrepareRedirect(Bumper justHit)
    {
        if (_rb == null || !justHit) return;

        // Select next unique, alive bumper not yet visited
        Bumper target = FindNextUniqueTarget(excludeCurrent: justHit);

        // If none left -> end ricochet immediately
        if (!target)
        {
            EndRicochet();
            return;
        }

        Vector3 pos = transform.position;
        Vector3 dir = (target.transform.position - pos);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0004f) dir = Vector3.forward;
        dir.Normalize();

        _ricochetHitCount++;
        _desiredSpeed = initialRicochetSpeed + _ricochetHitCount * speedIncrementPerHit;

        float current = _rb.velocity.magnitude;
        float nextSpeed = _desiredSpeed;
        if (smoothAcceleration && current < _desiredSpeed)
            nextSpeed = Mathf.Lerp(current, _desiredSpeed, accelerationLerp);
        if (redirectMaxSpeed > 0f && nextSpeed > redirectMaxSpeed)
            nextSpeed = redirectMaxSpeed;

        _pendingTarget = target;
        _pendingDir = dir;
        _pendingSpeed = nextSpeed;
        _redirectPending = true;

        // Ignore collider just hit briefly to prevent deflection back into it
        if (_ballCol)
        {
            var justCol = justHit.GetComponent<Collider>();
            if (justCol)
            {
                Physics.IgnoreCollision(_ballCol, justCol, true);
                StartCoroutine(RestoreCollision(_ballCol, justCol, ignorePrevColliderSeconds));
            }
        }
    }

    private Bumper FindNextUniqueTarget(Bumper excludeCurrent)
    {
        Vector3 pos = transform.position;
        Bumper chosen = null;
        float best = float.PositiveInfinity;

        foreach (var b in Bumper.EnumerateAll())
        {
            if (b == null || b.IsDead) continue;
            if (b == excludeCurrent) continue;
            if (_visited.Contains(b)) continue; // NEVER revisit

            float d2 = (b.transform.position - pos).sqrMagnitude;
            if (d2 < best)
            {
                best = d2;
                chosen = b;
            }
        }

        return chosen;
    }

    private IEnumerator WindowWatcher()
    {
        while (Time.time < _activeUntil)
            yield return null;
        EndRicochet();
        _windowCR = null;
    }

    private IEnumerator RestoreCollision(Collider a, Collider b, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (a && b) Physics.IgnoreCollision(a, b, false);
    }

    void FixedUpdate()
    {
        if (_redirectPending && IsActive)
        {
            // Target died or despawned -> recalc
            if (_pendingTarget == null || _pendingTarget.IsDead || !_pendingTarget.isActiveAndEnabled)
            {
                _pendingTarget = FindNextUniqueTarget(excludeCurrent: null);
                if (!_pendingTarget)
                {
                    EndRicochet();
                    _redirectPending = false;
                    return;
                }

                Vector3 pos = transform.position;
                _pendingDir = (_pendingTarget.transform.position - pos);
                _pendingDir.y = 0f;
                if (_pendingDir.sqrMagnitude < 0.0004f) _pendingDir = Vector3.forward;
                _pendingDir.Normalize();
            }

            if (_pendingDir != Vector3.zero)
            {
                _rb.velocity = _pendingDir * _pendingSpeed;
                _rb.position += _pendingDir * postRedirectNudge;
            }
            _redirectPending = false;
        }

        if (IsActive && clampEveryFrame && redirectMaxSpeed > 0f && _rb != null)
        {
            var v = _rb.velocity;
            float s = v.magnitude;
            if (s > redirectMaxSpeed)
                _rb.velocity = v.normalized * redirectMaxSpeed;

            // Realign mid-flight if drifting off the pending target
            if (_pendingTarget != null && !_redirectPending)
            {
                Vector3 toTarget = (_pendingTarget.transform.position - transform.position);
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0004f)
                {
                    toTarget.Normalize();
                    float angle = Vector3.Angle(_rb.velocity.normalized, toTarget);
                    if (angle > realignAngleThreshold)
                    {
                        float speed = _rb.velocity.magnitude;
                        _rb.velocity = toTarget * Mathf.Min(speed, redirectMaxSpeed > 0f ? redirectMaxSpeed : speed);
                    }
                }
            }
        }
    }
}
```

## Assets/Scripts/SpeedTrailFollower.cs

```csharp
using UnityEngine;

[DisallowMultipleComponent]
public class SpeedTrailFollower : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Rigidbody targetRb;      // Ball rigidbody
    [SerializeField] private Transform target;        // Ball transform (optional)
    private Ball _ball;                               // Cached Ball component (for glow/ emission)

    [Header("Line Renderer")]
    [SerializeField] private LineRenderer line;       // Assign in inspector or we will auto-create
    [Tooltip("Template material (will be instanced at runtime so emission changes don't affect shared asset).")]
    [SerializeField] private Material lineTemplateMaterial;

    [Header("Placement")]
    [SerializeField] private Vector3 startOffset = new(0f, 0.05f, 0f); // small lift for visibility
    [SerializeField] private float backwardLift = 0f;                  // additional Y offset for tail segment
    [SerializeField] private bool usePlanarXZ = true;                  // ignore Y component (pinball table axis)

    [Header("Speed Mapping")]
    [SerializeField] private float minSpeedToShow = 0.5f;
    [SerializeField] private float maxSpeedForMax = 50f;
    [SerializeField] private float minLength = 0.08f;
    [SerializeField] private float maxLength = 1.6f;
    [SerializeField] private float minWidth = 0.02f;
    [SerializeField] private float maxWidth = 0.20f;

    [Header("Smoothing")]
    [SerializeField, Tooltip("Higher = snappier response.")] private float lengthRiseRate = 8f;
    [SerializeField, Tooltip("Smooth time for length decay.")] private float lengthFallSmooth = 0.18f;
    [SerializeField, Tooltip("Higher = snappier yaw alignment.")] private float yawSmoothing = 20f;
    [SerializeField, Tooltip("Higher = snappier width adaptation.")] private float widthRiseRate = 10f;
    [SerializeField, Tooltip("Smooth time for width decay.")] private float widthFallSmooth = 0.15f;

    [Header("Emission Sync")]
    [Tooltip("Multiplier applied to ball emission intensity when mapping to line emission.")]
    [SerializeField] private float emissionIntensityScale = 1.0f;
    [Tooltip("Update color/emission only when change exceeds this delta (reduces material set calls).")]
    [SerializeField] private float colorUpdateThreshold = 0.02f;

    private Vector3 _lastDir = Vector3.forward;
    private float _curLength;
    private float _velLength;
    private float _curWidth;
    private float _velWidth;
    private bool _wasVisible;

    // Runtime material instance
    private Material _runtimeMat;
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    private Color _lastBallGlowColor;
    private float _lastBallEmission;

    void Awake()
    {
        if (!target && targetRb) target = targetRb.transform;
        if (!targetRb && target) targetRb = target.GetComponent<Rigidbody>();
        if (!targetRb && !target)
        {
            Debug.LogWarning("[SpeedTrailFollower] No target assigned.");
            enabled = false;
            return;
        }

        _ball = targetRb ? targetRb.GetComponent<Ball>() : null;

        EnsureLine();

        if (_ball != null)
        {
            _lastBallGlowColor = _ball.GlowColor;
            _lastBallEmission = _ball.EmissionIntensityUI;
            ApplyEmissionToMaterial(force: true);
        }
    }

    void EnsureLine()
    {
        if (!line)
        {
            line = GetComponent<LineRenderer>();
            if (!line) line = gameObject.AddComponent<LineRenderer>();
        }

        line.positionCount = 2;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;

        if (lineTemplateMaterial)
        {
            _runtimeMat = Instantiate(lineTemplateMaterial);
            _runtimeMat.name = lineTemplateMaterial.name + " (Runtime Trail)";
        }
        else
        {
            // fallback simple material
            _runtimeMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _runtimeMat.enableInstancing = true;
        }
        line.sharedMaterial = _runtimeMat;
    }

    private void LateUpdate()
    {
        if (!targetRb || !target)
        {
            if (line) line.enabled = false;
            _wasVisible = false;
            return;
        }

        if (!line) return;
        line.enabled = true; // keep renderer on so positions stay in sync even when hidden

        Vector3 v = targetRb.velocity;
        Vector3 planar = usePlanarXZ ? new Vector3(v.x, 0f, v.z) : v;
        float speed = planar.magnitude;

        // Direction (retain previous if near zero to avoid jitter)
        if (planar.sqrMagnitude > 0.0004f)
            _lastDir = planar.normalized;

        // Normalized speed
        float max = Mathf.Max(0.01f, maxSpeedForMax);
        float t = Mathf.Clamp01(speed / max);

        // Target length & width
        float targetLength = Mathf.Lerp(minLength, maxLength, t);
        float targetWidth = Mathf.Lerp(minWidth, maxWidth, t);

        // Compute head position
        Vector3 head = target.position + startOffset;

        bool visibleNow = speed >= minSpeedToShow;

        if (!visibleNow)
        {
            // Hidden state: keep the line snapped to the head and width zero
            _curLength = 0f;
            _curWidth = 0f;
            line.widthMultiplier = 0f;

            line.SetPosition(0, head);
            line.SetPosition(1, head);

            _wasVisible = false;

            // Still keep emission in sync to avoid a pop when reappearing
            if (_ball != null) MaybeUpdateEmission();
            return;
        }

        // Visible: smooth length & width (fast rise, smooth fall)
        if (targetLength >= _curLength)
            _curLength = Mathf.MoveTowards(_curLength, targetLength, lengthRiseRate * Time.deltaTime);
        else
            _curLength = Mathf.SmoothDamp(_curLength, targetLength, ref _velLength, lengthFallSmooth, Mathf.Infinity, Time.deltaTime);

        if (targetWidth >= _curWidth)
            _curWidth = Mathf.MoveTowards(_curWidth, targetWidth, widthRiseRate * Time.deltaTime);
        else
            _curWidth = Mathf.SmoothDamp(_curWidth, targetWidth, ref _velWidth, widthFallSmooth, Mathf.Infinity, Time.deltaTime);

        line.widthMultiplier = _curWidth;

        // Compute tail and apply optional yaw smoothing
        Vector3 targetTail = head - _lastDir * _curLength;
        targetTail.y += backwardLift;

        // If we were hidden last frame, snap immediately (no smoothing from stale tail)
        Vector3 prevTail = _wasVisible ? line.GetPosition(line.positionCount - 1) : head;
        float yawLerp = _wasVisible ? 1f - Mathf.Exp(-yawSmoothing * Time.deltaTime) : 1f;
        Vector3 tail = Vector3.Lerp(prevTail, targetTail, yawLerp);

        line.SetPosition(0, head);
        line.SetPosition(1, tail);

        if (_ball != null) MaybeUpdateEmission();

        _wasVisible = true;
    }

    private void MaybeUpdateEmission()
    {
        // Ball provides GlowColor & EmissionIntensityUI
        var glowColor = _ball.GlowColor;
        var intensity = _ball.EmissionIntensityUI * emissionIntensityScale;

        // Only update if change exceeds threshold
        if (_runtimeMat != null &&
            (Mathf.Abs(intensity - _lastBallEmission) > colorUpdateThreshold
             || ColorDistance(glowColor, _lastBallGlowColor) > colorUpdateThreshold))
        {
            ApplyEmission(glowColor, intensity);
            _lastBallGlowColor = glowColor;
            _lastBallEmission = intensity;
        }
    }

    private void ApplyEmissionToMaterial(bool force = false)
    {
        if (_ball == null || _runtimeMat == null) return;
        ApplyEmission(_ball.GlowColor, _ball.EmissionIntensityUI * emissionIntensityScale, force);
    }

    private void ApplyEmission(Color baseColor, float intensity, bool force = false)
    {
        intensity = Mathf.Clamp(intensity, 0f, 8f);
        // Convert to gamma-corrected emissive
        Color emissive = baseColor * Mathf.LinearToGammaSpace(intensity);

        if (force)
        {
            _runtimeMat.EnableKeyword("_EMISSION");
        }

        if (_runtimeMat.HasProperty(EmissionColorID))
            _runtimeMat.SetColor(EmissionColorID, emissive);
        if (_runtimeMat.HasProperty(BaseColorID))
            _runtimeMat.SetColor(BaseColorID, baseColor);
        if (_runtimeMat.HasProperty(ColorID))
            _runtimeMat.SetColor(ColorID, baseColor);
    }

    private static float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return Mathf.Abs(dr) + Mathf.Abs(dg) + Mathf.Abs(db);
    }

    // Public API if you want to swap target at runtime
    public void SetTarget(Rigidbody rb)
    {
        targetRb = rb;
        target = rb ? rb.transform : null;
        _ball = rb ? rb.GetComponent<Ball>() : null;
    }

    public void SetLineRenderer(LineRenderer lr)
    {
        line = lr;
        EnsureLine();
        ApplyEmissionToMaterial(force: true);
    }
}
```

## Assets/Scripts/StatModifier.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum StatModType
{
    Flat = 100,
    PercentAdd = 200,
    PercentMult = 300,
}

public class StatModifier
{
    public readonly float Value;
    public readonly StatModType Type;
    public readonly int Order;
    public readonly object Source;

    public StatModifier(float value, StatModType type, int order, object source)
    {
        Value = value;
        Type = type;
        Order = order;
        Source = source;
    }

    public StatModifier(float value, StatModType type) : this(value, type, (int)type, null) { }

    public StatModifier(float value, StatModType type, int order) : this(value, type, order, null) { }

    public StatModifier(float value, StatModType type, object source) : this(value, type, (int)type, source) { }

}

```

## Assets/Scripts/Tank.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Tank : BaseCharacter
{

    protected float hpLvlIncrease = 11.3f;
    protected float minAtkLvlIncrease = 1.7f;
    protected float maxAtkLvlIncrease = 2.04f;

    protected float endLVLinc = 4.94f;
    protected float strLVLinc = 2.68f;
    protected float agiLVLinc = 1.08f;
    protected float witLVLinc = 1.93f;
    protected float chaLVLinc = 3.27f;


    public Tank(string name, int level)
        : base(name, "Tank", level, 624.9f, 100f, 44.8f, 49.9f, 98f)
    {
        traits[TraitType.Endurance] = 10;
        traits[TraitType.Strength] = 5;
        traits[TraitType.Agility] = 2;
        traits[TraitType.Wit] = 5;
        traits[TraitType.Charm] = 7;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();

    }
    public Tank()
    : base("", "Tank", 5, 624.9f, 100f, 44.8f, 49.9f, 98f)
    {
        traits[TraitType.Endurance] = 10;
        traits[TraitType.Strength] = 5;
        traits[TraitType.Agility] = 2;
        traits[TraitType.Wit] = 5;
        traits[TraitType.Charm] = 7;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);
    }

}

```

## Assets/Scripts/Test.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Test : MonoBehaviour
{
    public Warrior warrior;
    public Mage mage;
    public Druid druid;
    public Assassin assassin;
    public Tank tank;

    protected Item sword;

    public CharacterUI ui;


    // Start is called before the first frame update
    void Start()
    {
        warrior = new Warrior("Jacque", 5);
        mage = new Mage("Jill", 4);

        ui.SetCharacterUI(warrior, true);
        ui.SetCharacterUI(mage, false);


        warrior.PrintStats();
        mage.PrintStats();
        warrior.Health.AddModifier(new StatModifier(5f, StatModType.Flat));
        warrior.Health.AddModifier(new StatModifier(2.5f, StatModType.Flat));
        warrior.Health.AddModifier(new StatModifier(1.9f, StatModType.PercentMult));
        warrior.Health.baseValue = warrior.Health.Value;

        Debug.Log("After Sword Equip Strength Value: " + warrior.Health.Value);
        Debug.Log("After Sword Equip Strength base Value: " + warrior.Health.baseValue);
        ui.SetCharacterUI(warrior, true);
        /*
        sword.Unequip(myCharacter.MaxAtk);
        Debug.Log("After Sword Unequip Strength Value: " + myCharacter.MaxAtk.Value);
        */
    }

    // Update is called once per frame
    void Update()
    {

    }
}

```

## Assets/Scripts/TimeScaleHub.cs

```csharp
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class TimeScaleHub : MonoBehaviour
{
    public static TimeScaleHub I { get; private set; }

    // If any active → IsAnyActive = true (used by Pinball guards)
    public static bool IsAnyActive => I != null && I._active.Count > 0;
    // NEW: if any pause lock is active, timeScale is forced to 0
    public static bool IsPaused => I != null && I._pauseOwners.Count > 0;

    // Default fixed delta used when scale = 1 (Pinball sets this on Awake)
    private float _defaultFixedDelta = 0.02f;

    // Owner -> request
    private readonly Dictionary<object, Req> _active = new();

    // NEW: owners that hold a hard pause (timeScale = 0)
    private readonly HashSet<object> _pauseOwners = new();

    private struct Req
    {
        public float scale;
        public bool affectFixedDelta;
    }

    void Awake()
    {
        if (I && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
        // Use current engine baseline until Pinball provides its baseline
        _defaultFixedDelta = Time.fixedDeltaTime;
        Recompute();
    }

    public static void EnsureInitialized(float defaultFixedDelta)
    {
        if (!I)
        {
            var go = new GameObject("TimeScaleHub");
            I = go.AddComponent<TimeScaleHub>();
        }
        I._defaultFixedDelta = Mathf.Max(0.0005f, defaultFixedDelta);
        I.Recompute();
    }

    // Begin or update a slow‑mo request
    public static void Begin(object owner, float scale, bool affectFixedDelta = true)
    {
        if (!I) EnsureInitialized(Time.fixedDeltaTime);
        scale = Mathf.Clamp(scale, 0.05f, 1f);
        I._active[owner ?? I] = new Req { scale = scale, affectFixedDelta = affectFixedDelta };
        I.Recompute();
    }

    // End a slow‑mo request
    public static void End(object owner)
    {
        if (!I) return;
        I._active.Remove(owner ?? I);
        I.Recompute();
    }

    // NEW: begin a hard pause (timeScale = 0, physics frozen)
    public static void BeginPause(object owner)
    {
        if (!I) EnsureInitialized(Time.fixedDeltaTime);
        I._pauseOwners.Add(owner ?? I);
        I.Recompute();
    }

    // NEW: end a hard pause
    public static void EndPause(object owner)
    {
        if (!I) return;
        I._pauseOwners.Remove(owner ?? I);
        I.Recompute();
    }

    // Nuke everything: resets slow‑mo to normal immediately (does not clear pauses)
    public static void ForceClearAll()
    {
        if (!I) return;
        I._active.Clear();
        I.Recompute();
    }

    // NEW: if you ever need to clear all pause locks (rare)
    public static void ForceClearAllPauses()
    {
        if (!I) return;
        I._pauseOwners.Clear();
        I.Recompute();
    }

    // When you suspect drift, re-apply computed timescale
    public static void ForceRecompute() => I?.Recompute();

    private void Recompute()
    {
        // Hard freeze takes priority over any slow‑mo requests
        if (_pauseOwners.Count > 0)
        {
            Time.timeScale = 0f;
            // Physics stays frozen at ts=0; keep fixedDelta at default for when we unpause
            Time.fixedDeltaTime = _defaultFixedDelta;
            return;
        }

        // Resolve effective scale: slowest wins
        float scale = 1f;
        bool anyAffectFixed = false;

        foreach (var kv in _active)
        {
            scale = Mathf.Min(scale, kv.Value.scale);
            anyAffectFixed |= kv.Value.affectFixedDelta;
        }

        Time.timeScale = scale;

        // Keep physics consistent with visual time when any request wants that
        if (anyAffectFixed || _active.Count == 0)
            Time.fixedDeltaTime = _defaultFixedDelta * scale;
    }
}
```

## Assets/Scripts/TweenDamageNumberSystem.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DG.Tweening;
using UnityEngine.Pool;

public class TweenDamageNumberSystem : MonoBehaviour, IDamageNumberSystem
{
    [Header("Basics")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float duration = 0.9f;
    [SerializeField] private float riseDistance = 1.25f;
    [SerializeField] private float surfaceOffset = 0.08f;
    [SerializeField] private bool useUnscaledTime = false;
    [SerializeField] private DamageNumberStyleSO style;
    [SerializeField] private float cameraForwardOffset = 0.02f;

    [Header("Typography")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Material fontMaterial;
    [SerializeField] private float baseFontSize = 4f;
    [SerializeField] private Color defaultColor = Color.white;

    [Header("Rendering")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 500;
    [Tooltip("Increase sorting order each spawn so newest is always on top.")]
    [SerializeField] private bool incrementalSorting = true;
    [Tooltip("Maximum extra ordering range before wrapping back to base order.")]
    [SerializeField] private int sortingOrderWrapRange = 5000;
    private int _nextSortingOrder;

    [Header("Outline")]
    [SerializeField] private bool enableOutline = true;
    [SerializeField, Range(0f, 1f)] private float outlineWidth = 0.35f;
    [SerializeField] private Color outlineColor = Color.black;
    [SerializeField, Range(0f, 1f)] private float faceDilate = 0.05f;

    [Header("Damage -> Size Mapping")]
    [SerializeField] private float minDamageForScale = 1f;
    [SerializeField] private float maxDamageForScale = 100f;
    [SerializeField] private float minScale = 0.75f;
    [SerializeField] private float maxScale = 1.75f;
    [SerializeField] private bool useLogScale = true;
    [SerializeField] private float logBase = 10f;
    [SerializeField] private AnimationCurve sizeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Pop & Shrink Behaviour")]
    [Tooltip("Seconds to wait before doing the fast pop (so number is visible first).")]
    [SerializeField, Range(0f, 0.25f)] private float popDelaySeconds = 0.08f;
    [Tooltip("Fraction of total duration used for the pop tween itself (fast scale up).")]
    [SerializeField, Range(0.05f, 0.5f)] private float popDurationFraction = 0.15f;
    [Tooltip("Final scale multiplier (relative to popped max scale) the number shrinks to. 1 = no shrink.")]
    [SerializeField, Range(0.1f, 1f)] private float endShrinkScaleMultiplier = 0.75f;

    [Header("Z Axis Shake")]
    [Tooltip("Z axis tilt angle during pop (visual shake).")]
    [SerializeField, Range(0f, 45f)] private float tiltAngle = 12f;
    [Tooltip("Fraction of total duration used for tilt shake (starts at pop).")]
    [SerializeField, Range(0.02f, 0.6f)] private float tiltDurationFraction = 0.15f;
    [SerializeField] private bool enableTilt = true;

    [Header("Pool")]
    [SerializeField] private int defaultCapacity = 32;
    [SerializeField] private int maxSize = 256;

    private IObjectPool<GameObject> pool;

    void Awake()
    {
        if (!targetCamera) targetCamera = Camera.main;
        DamageNumbers.Register(this);
        _nextSortingOrder = style ? style.sortingOrder : sortingOrder;
        pool = new ObjectPool<GameObject>(Create, OnGet, OnRelease, OnDestroyPooled, true, defaultCapacity, maxSize);
        Prewarm(48);
        DOTween.SetTweensCapacity(200, 50);
    }

    public void Prewarm(int count)
    {
        count = Mathf.Max(0, count);
        var arr = new GameObject[count];
        for (int i = 0; i < count; i++) arr[i] = pool.Get();
        for (int i = 0; i < count; i++) pool.Release(arr[i]);
    }

    private GameObject Create()
    {
        var go = new GameObject("DamageNumber", typeof(TextMeshPro));
        go.transform.SetParent(transform, false);
        var tmp = go.GetComponent<TextMeshPro>();
        tmp.enableWordWrapping = false;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
        tmp.extraPadding = true;

        var useFont = style ? style.font : font;
        var useMat = style ? style.fontMaterial : fontMaterial;
        var useSize = style ? style.baseFontSize : baseFontSize;
        var useColor = style ? style.defaultColor : defaultColor;
        if (useFont) tmp.font = useFont;

        Material baseMat = null;
        if (IsTMPMaterial(useMat)) baseMat = useMat;
        else if (tmp.font) baseMat = tmp.font.material;
        if (baseMat) tmp.fontSharedMaterial = baseMat;

        tmp.fontMaterial = new Material(tmp.fontSharedMaterial);
        ApplyOutlineAndRefresh(tmp);

        tmp.fontSize = useSize;
        var mr = go.GetComponent<MeshRenderer>();
        var layerName = style ? style.sortingLayerName : sortingLayerName;
        var baseOrder = style ? style.sortingOrder : sortingOrder;
        if (mr)
        {
            mr.sortingLayerName = layerName;
            mr.sortingOrder = baseOrder;
        }

        tmp.color = new Color(useColor.r, useColor.g, useColor.b, 0f);
        go.SetActive(false);
        return go;
    }

    private static bool IsTMPMaterial(Material m) => m && m.shader && m.shader.name.StartsWith("TextMeshPro/");

    private void ApplyOutlineAndRefresh(TextMeshPro tmp)
    {
        if (!tmp || !enableOutline) return;
        if (!IsTMPMaterial(tmp.fontMaterial) && tmp.font && IsTMPMaterial(tmp.font.material))
        {
            tmp.fontSharedMaterial = tmp.font.material;
            tmp.fontMaterial = new Material(tmp.fontSharedMaterial);
        }
        var mat = tmp.fontMaterial;
        if (!mat) return;
        mat.EnableKeyword("OUTLINE_ON");
        if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
        {
            var col = outlineColor; if (col.a <= 0f) col.a = 1f;
            mat.SetColor(ShaderUtilities.ID_OutlineColor, col);
        }
        if (mat.HasProperty(ShaderUtilities.ID_FaceDilate))
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, faceDilate);
        tmp.extraPadding = true;
        tmp.UpdateMeshPadding();
        tmp.SetMaterialDirty();
    }

    private float ComputeScale(float damage)
    {
        float d = Mathf.Max(damage, 0f);
        if (useLogScale)
        {
            float minL = Mathf.Log(minDamageForScale + 1f, logBase);
            float maxL = Mathf.Log(maxDamageForScale + 1f, logBase);
            float valL = Mathf.Log(d + 1f, logBase);
            float tL = Mathf.InverseLerp(minL, maxL, valL);
            return Mathf.Lerp(minScale, maxScale, sizeCurve.Evaluate(tL));
        }
        float t = Mathf.InverseLerp(minDamageForScale, maxDamageForScale, d);
        return Mathf.Lerp(minScale, maxScale, sizeCurve.Evaluate(t));
    }

    public void Spawn(float amount, Vector3 position, Color? overrideColor = null)
    {
        var go = pool.Get();
        DOTween.Kill(go.transform, false);
        var tmp = go.GetComponent<TextMeshPro>();
        DOTween.Kill(tmp, false);
        ApplyOutlineAndRefresh(tmp);

        tmp.text = amount.ToString("0.#");
        var fallbackColor = style ? style.defaultColor : defaultColor;
        var col = overrideColor ?? fallbackColor;
        tmp.color = new Color(col.r, col.g, col.b, 0f);

        float baseSize = style ? style.baseFontSize : baseFontSize;
        float sizeScale = ComputeScale(amount);
        tmp.fontSize = baseSize * sizeScale;

        if (incrementalSorting)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr)
            {
                int baseOrder = style ? style.sortingOrder : sortingOrder;
                if (_nextSortingOrder < baseOrder) _nextSortingOrder = baseOrder;
                mr.sortingOrder = _nextSortingOrder++;
                if (_nextSortingOrder - baseOrder > sortingOrderWrapRange)
                    _nextSortingOrder = baseOrder;
            }
        }

        var cam = targetCamera;
        var basePos = position;
        if (cam) basePos -= cam.transform.forward * cameraForwardOffset;
        go.transform.position = position;

        var dur = Mathf.Max(0.05f, style ? style.duration : duration);
        var rise = style ? style.riseDistance : riseDistance;
        var fadeIn = Mathf.Clamp01(style ? style.fadeInFraction : 0.2f) * dur;
        var fadeOut = Mathf.Clamp01(style ? style.fadeOutFraction : 0.25f) * dur;
        var popFrom = style ? style.popFromScale : 0.6f;
        var popTo = style ? style.popToScale : 1.1f;
        var useUnscaled = style ? style.useUnscaledTime : useUnscaledTime;

        float popTime = Mathf.Clamp(popDurationFraction, 0.05f, 0.5f) * dur;
        float delay = Mathf.Min(popDelaySeconds, dur - 0.05f);
        float shrinkTime = Mathf.Max(0.0001f, dur - (delay + popTime));
        float endScale = popTo * endShrinkScaleMultiplier;

        float tiltZ = 0f;
        go.transform.localScale = Vector3.one * (popFrom * sizeScale);

        var seq = DOTween.Sequence().SetUpdate(useUnscaled).SetRecyclable(true);
        seq.Join(go.transform.DOMoveY(basePos.y + rise, dur).SetEase(Ease.OutCubic));
        seq.Insert(0f, tmp.DOFade(1f, fadeIn).SetEase(Ease.OutCubic));
        seq.Insert(dur - fadeOut, tmp.DOFade(0f, fadeOut).SetEase(Ease.InCubic));

        // POP after delay
        seq.Insert(delay, go.transform.DOScale(popTo * sizeScale, popTime).SetEase(Ease.OutCubic));

        // Shrink only if endShrink < 1
        if (endShrinkScaleMultiplier < 0.999f)
            seq.Insert(delay + popTime, go.transform.DOScale(endScale * sizeScale, shrinkTime).SetEase(Ease.InQuad));

        // Z-axis shake (starts with pop)
        if (enableTilt && tiltAngle > 0f)
        {
            float tiltTime = Mathf.Clamp(tiltDurationFraction, 0.02f, 0.9f) * dur;
            float effectiveTiltTime = Mathf.Min(tiltTime, dur - delay);
            var tiltSeq = DOTween.Sequence().SetUpdate(useUnscaled);
            tiltSeq.Append(DOVirtual.Float(0f, tiltAngle, effectiveTiltTime * 0.33f, v => tiltZ = v).SetEase(Ease.OutCubic));
            tiltSeq.Append(DOVirtual.Float(tiltAngle, -tiltAngle, effectiveTiltTime * 0.33f, v => tiltZ = v).SetEase(Ease.InOutCubic));
            tiltSeq.Append(DOVirtual.Float(-tiltAngle, 0f, effectiveTiltTime * 0.34f, v => tiltZ = v).SetEase(Ease.InCubic));
            seq.Insert(delay, tiltSeq);
        }

        seq.OnUpdate(() =>
        {
            if (!targetCamera) return;
            var face = Quaternion.LookRotation(targetCamera.transform.forward, Vector3.up);
            go.transform.rotation = face * Quaternion.Euler(0f, 0f, tiltZ);
        });

        seq.OnComplete(() =>
        {
            ResetVisual(go);
            pool.Release(go);
        });
    }

    private void OnGet(GameObject go) => go.SetActive(true);
    private void OnRelease(GameObject go)
    {
        ResetVisual(go);
        go.SetActive(false);
    }
    private void OnDestroyPooled(GameObject go)
    {
        if (go) Destroy(go);
    }

    private void ResetVisual(GameObject go)
    {
        var tmp = go.GetComponent<TextMeshPro>();
        float baseSize = style ? style.baseFontSize : baseFontSize;
        tmp.fontSize = baseSize;
        tmp.enableAutoSizing = false;
        tmp.text = string.Empty;
        var c = tmp.color; c.a = 0f; tmp.color = c;
        go.transform.localScale = Vector3.one;
        go.transform.localRotation = Quaternion.identity;
    }
}
```

## Assets/Scripts/TweenXPNumberSystem.cs

```csharp
using UnityEngine;
using TMPro;
using DG.Tweening;
using UnityEngine.Pool;

public class TweenXPNumberSystem : MonoBehaviour, IXPNumberSystem
{
    [Header("Basics")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private XPNumberStyleSO style;
    [SerializeField] private float cameraForwardOffset = 0.02f;

    [Header("Fallbacks")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Material fontMaterial;
    [SerializeField] private float baseFontSize = 3.5f;
    [SerializeField] private Color defaultColor = new Color(0.4f, 0.9f, 1f, 1f);

    [Header("Render")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 600;
    [SerializeField] private bool incrementalSorting = true;
    [SerializeField] private int sortingOrderWrapRange = 5000;
    private int _nextSortingOrder;

    [Header("Outline")]
    [SerializeField] private bool enableOutline = true;
    [SerializeField, Range(0f, 1f)] private float outlineWidth = 0.35f;
    [SerializeField] private Color outlineColor = Color.black;
    [SerializeField, Range(0f, 1f)] private float faceDilate = 0.05f;

    [Header("Pop & Shrink Behaviour")]
    [Tooltip("Seconds to wait before doing the fast pop.")]
    [SerializeField, Range(0f, 0.25f)] private float popDelaySeconds = 0.08f;
    [Tooltip("Fraction of total duration used for the pop tween.")]
    [SerializeField, Range(0.05f, 0.5f)] private float popDurationFraction = 0.15f;
    [Tooltip("End scale multiplier relative to popped size (1 = hold).")]
    [SerializeField, Range(0.1f, 1f)] private float endShrinkScaleMultiplier = 0.75f;

    [Header("Z Axis Shake")]
    [SerializeField, Range(0f, 45f)] private float tiltAngle = 10f;
    [SerializeField, Range(0.02f, 0.6f)] private float tiltDurationFraction = 0.15f;
    [SerializeField] private bool enableTilt = true;

    [Header("Pool")]
    [SerializeField] private int defaultCapacity = 24;
    [SerializeField] private int maxSize = 128;

    private IObjectPool<GameObject> pool;

    void Awake()
    {
        if (!targetCamera) targetCamera = Camera.main;
        XPNumbers.Register(this);
        _nextSortingOrder = style ? style.sortingOrder : sortingOrder;
        pool = new ObjectPool<GameObject>(Create, OnGet, OnRelease, OnDestroyPooled, true, defaultCapacity, maxSize);
        Prewarm(24);
    }

    public void Spawn(int amount, Vector3 position, Color? overrideColor = null)
    {
        var go = pool.Get();
        DOTween.Kill(go.transform, false);
        var tmp = go.GetComponent<TextMeshPro>();
        DOTween.Kill(tmp, false);
        EnsureTMPOutline(tmp);

        tmp.text = $"+{amount} XP";
        Color useColor = overrideColor ?? (style ? style.defaultColor : defaultColor);
        tmp.color = new Color(useColor.r, useColor.g, useColor.b, 0f);

        float size = style ? style.baseFontSize : baseFontSize;
        tmp.fontSize = size;

        if (incrementalSorting)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr)
            {
                int baseOrder = style ? style.sortingOrder : sortingOrder;
                if (_nextSortingOrder < baseOrder) _nextSortingOrder = baseOrder;
                mr.sortingLayerName = style ? style.sortingLayerName : sortingLayerName;
                mr.sortingOrder = _nextSortingOrder++;
                if (_nextSortingOrder - baseOrder > sortingOrderWrapRange)
                    _nextSortingOrder = baseOrder;
            }
        }

        var cam = targetCamera;
        var basePos = position;
        if (cam) basePos -= cam.transform.forward * cameraForwardOffset;
        go.transform.position = basePos;

        float dur = Mathf.Max(0.05f, style ? style.duration : 0.8f);
        float fadeIn = Mathf.Clamp01(style ? style.fadeInFraction : 0.08f) * dur;
        float fadeOut = Mathf.Clamp01(style ? style.fadeOutFraction : 0.22f) * dur;
        float rise = style ? style.riseDistance : 1.0f;
        float fromS = style ? style.popFromScale : 0.6f;
        float toS = style ? style.popToScale : 1.05f;
        bool unscaled = style ? style.useUnscaledTime : false;

        float popTime = Mathf.Clamp(popDurationFraction, 0.05f, 0.5f) * dur;
        float delay = Mathf.Min(popDelaySeconds, dur - 0.05f);
        float shrinkTime = Mathf.Max(0.0001f, dur - (delay + popTime));
        float endScale = toS * endShrinkScaleMultiplier;

        float tiltZ = 0f;
        go.transform.localScale = Vector3.one * fromS;

        var seq = DOTween.Sequence().SetUpdate(unscaled).SetRecyclable(true);
        seq.Join(go.transform.DOMoveY(basePos.y + rise, dur).SetEase(Ease.OutCubic));
        seq.Insert(0f, tmp.DOFade(1f, fadeIn).SetEase(Ease.OutCubic));
        seq.Insert(dur - fadeOut, tmp.DOFade(0f, fadeOut).SetEase(Ease.InCubic));

        seq.Insert(delay, go.transform.DOScale(toS, popTime).SetEase(Ease.OutCubic));
        if (endShrinkScaleMultiplier < 0.999f)
            seq.Insert(delay + popTime, go.transform.DOScale(endScale, shrinkTime).SetEase(Ease.InQuad));

        if (enableTilt && tiltAngle > 0f)
        {
            float tiltTime = Mathf.Clamp(tiltDurationFraction, 0.02f, 0.9f) * dur;
            float effectiveTiltTime = Mathf.Min(tiltTime, dur - delay);
            var tiltSeq = DOTween.Sequence().SetUpdate(unscaled);
            tiltSeq.Append(DOVirtual.Float(0f, tiltAngle, effectiveTiltTime * 0.33f, v => tiltZ = v).SetEase(Ease.OutCubic));
            tiltSeq.Append(DOVirtual.Float(tiltAngle, -tiltAngle, effectiveTiltTime * 0.33f, v => tiltZ = v).SetEase(Ease.InOutCubic));
            tiltSeq.Append(DOVirtual.Float(-tiltAngle, 0f, effectiveTiltTime * 0.34f, v => tiltZ = v).SetEase(Ease.InCubic));
            seq.Insert(delay, tiltSeq);
        }

        seq.OnUpdate(() =>
        {
            if (!targetCamera) return;
            var face = Quaternion.LookRotation(targetCamera.transform.forward, Vector3.up);
            go.transform.rotation = face * Quaternion.Euler(0f, 0f, tiltZ);
        });

        seq.OnComplete(() =>
        {
            OnRelease(go);
            pool.Release(go);
        });
    }

    public void SpawnFollow(int amount, Transform follow, Vector3 worldOffset, Color? overrideColor = null)
    {
        if (!follow) { Spawn(amount, Vector3.zero, overrideColor); return; }

        var go = pool.Get();
        DOTween.Kill(go.transform, false);
        var tmp = go.GetComponent<TextMeshPro>();
        DOTween.Kill(tmp, false);
        EnsureTMPOutline(tmp);

        tmp.text = $"+{amount}";
        Color useColor = overrideColor ?? (style ? style.defaultColor : defaultColor);
        tmp.color = new Color(useColor.r, useColor.g, useColor.b, 0f);

        float size = style ? style.baseFontSize : baseFontSize;
        tmp.fontSize = size;

        if (incrementalSorting)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr)
            {
                int baseOrder = style ? style.sortingOrder : sortingOrder;
                if (_nextSortingOrder < baseOrder) _nextSortingOrder = baseOrder;
                mr.sortingLayerName = style ? style.sortingLayerName : sortingLayerName;
                mr.sortingOrder = _nextSortingOrder++;
                if (_nextSortingOrder - baseOrder > sortingOrderWrapRange)
                    _nextSortingOrder = baseOrder;
            }
        }

        var cam = targetCamera;
        float dur = Mathf.Max(0.05f, style ? style.duration : 0.8f);
        float fadeIn = Mathf.Clamp01(style ? style.fadeInFraction : 0.08f) * dur;
        float fadeOut = Mathf.Clamp01(style ? style.fadeOutFraction : 0.22f) * dur;
        float rise = style ? style.riseDistance : 1.0f;
        float fromS = style ? style.popFromScale : 0.6f;
        float toS = style ? style.popToScale : 1.05f;
        bool unscaled = style ? style.useUnscaledTime : false;

        float popTime = Mathf.Clamp(popDurationFraction, 0.05f, 0.5f) * dur;
        float delay = Mathf.Min(popDelaySeconds, dur - 0.05f);
        float shrinkTime = Mathf.Max(0.0001f, dur - (delay + popTime));
        float endScale = toS * endShrinkScaleMultiplier;

        Vector3 baseOrigin = follow.position + worldOffset;
        float followWeight = 1f;
        float riseAmt = 0f;
        float tiltZ = 0f;

        Vector3 basePos = follow.position + worldOffset;
        if (cam) basePos -= cam.transform.forward * cameraForwardOffset;
        go.transform.position = basePos;
        go.transform.localScale = Vector3.one * fromS;

        var seq = DOTween.Sequence().SetUpdate(unscaled).SetRecyclable(true);
        seq.Join(DOVirtual.Float(0f, rise, dur, v => riseAmt = v).SetEase(Ease.OutCubic));
        seq.Join(DOVirtual.Float(1f, 0f, dur, v => followWeight = v).SetEase(Ease.InCubic));

        seq.Insert(0f, tmp.DOFade(1f, fadeIn).SetEase(Ease.OutCubic));
        seq.Insert(dur - fadeOut, tmp.DOFade(0f, fadeOut).SetEase(Ease.InCubic));

        seq.Insert(delay, go.transform.DOScale(toS, popTime).SetEase(Ease.OutCubic));
        if (endShrinkScaleMultiplier < 0.999f)
            seq.Insert(delay + popTime, go.transform.DOScale(endScale, shrinkTime).SetEase(Ease.InQuad));

        if (enableTilt && tiltAngle > 0f)
        {
            float tiltTime = Mathf.Clamp(tiltDurationFraction, 0.02f, 0.9f) * dur;
            float effectiveTiltTime = Mathf.Min(tiltTime, dur - delay);
            var tiltSeq = DOTween.Sequence().SetUpdate(unscaled);
            tiltSeq.Append(DOVirtual.Float(0f, tiltAngle, effectiveTiltTime * 0.33f, v => tiltZ = v).SetEase(Ease.OutCubic));
            tiltSeq.Append(DOVirtual.Float(tiltAngle, -tiltAngle, effectiveTiltTime * 0.33f, v => tiltZ = v).SetEase(Ease.InOutCubic));
            tiltSeq.Append(DOVirtual.Float(-tiltAngle, 0f, effectiveTiltTime * 0.34f, v => tiltZ = v).SetEase(Ease.InCubic));
            seq.Insert(delay, tiltSeq);
        }

        seq.OnUpdate(() =>
        {
            var unfollowPos = baseOrigin + Vector3.up * riseAmt;
            var targetFollowPos = follow.position + worldOffset + Vector3.up * riseAmt;
            var pos = Vector3.Lerp(unfollowPos, targetFollowPos, followWeight);
            if (targetCamera) pos -= targetCamera.transform.forward * cameraForwardOffset;
            go.transform.position = pos;

            if (targetCamera)
            {
                var face = Quaternion.LookRotation(targetCamera.transform.forward, Vector3.up);
                go.transform.rotation = face * Quaternion.Euler(0f, 0f, tiltZ);
            }
        })
        .OnComplete(() =>
        {
            OnRelease(go);
            pool.Release(go);
        });
    }

    void Prewarm(int count)
    {
        var arr = new GameObject[count];
        for (int i = 0; i < count; i++) arr[i] = pool.Get();
        for (int i = 0; i < count; i++) pool.Release(arr[i]);
    }

    GameObject Create()
    {
        var go = new GameObject("XPNumber", typeof(TextMeshPro));
        go.transform.SetParent(transform, false);
        var tmp = go.GetComponent<TextMeshPro>();
        tmp.enableWordWrapping = false;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
        tmp.extraPadding = true;

        var useFont = style ? style.font : font;
        var useMat = style ? style.fontMaterial : fontMaterial;
        var useSize = style ? style.baseFontSize : baseFontSize;
        var useColor = style ? style.defaultColor : defaultColor;
        if (useFont) tmp.font = useFont;

        Material baseMat = null;
        if (IsTMPMaterial(useMat)) baseMat = useMat;
        else if (tmp.font) baseMat = tmp.font.material;
        if (baseMat) tmp.fontSharedMaterial = baseMat;

        tmp.fontMaterial = new Material(tmp.fontSharedMaterial);
        ApplyOutlineAndRefresh(tmp);

        tmp.fontSize = useSize;
        tmp.color = new Color(useColor.r, useColor.g, useColor.b, 0f);

        var mr = go.GetComponent<MeshRenderer>();
        var layer = style ? style.sortingLayerName : sortingLayerName;
        var order = style ? style.sortingOrder : sortingOrder;
        if (mr)
        {
            mr.sortingLayerName = layer;
            mr.sortingOrder = order;
        }

        go.SetActive(false);
        return go;
    }

    private static bool IsTMPMaterial(Material m) => m && m.shader && m.shader.name.StartsWith("TextMeshPro/");
    private void EnsureTMPOutline(TextMeshPro tmp)
    {
        if (!tmp) return;
        if (!IsTMPMaterial(tmp.fontMaterial))
        {
            if (tmp.font && IsTMPMaterial(tmp.font.material))
                tmp.fontSharedMaterial = tmp.font.material;
            tmp.fontMaterial = new Material(tmp.fontSharedMaterial);
        }
        ApplyOutlineAndRefresh(tmp);
    }
    private void ApplyOutlineAndRefresh(TextMeshPro tmp)
    {
        if (!enableOutline) return;
        var mat = tmp.fontMaterial;
        if (!mat) return;
        mat.EnableKeyword("OUTLINE_ON");
        if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        if (mat.HasProperty(ShaderUtilities.ID_OutlineColor))
        {
            var col = outlineColor; if (col.a <= 0f) col.a = 1f;
            mat.SetColor(ShaderUtilities.ID_OutlineColor, col);
        }
        if (mat.HasProperty(ShaderUtilities.ID_FaceDilate))
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, faceDilate);
        tmp.extraPadding = true;
        tmp.UpdateMeshPadding();
        tmp.SetMaterialDirty();
    }

    void OnGet(GameObject go) => go.SetActive(true);
    void OnRelease(GameObject go)
    {
        var tmp = go.GetComponent<TextMeshPro>();
        tmp.text = string.Empty;
        var c = tmp.color; c.a = 0f; tmp.color = c;
        go.transform.localScale = Vector3.one;
        go.transform.localRotation = Quaternion.identity;
        go.transform.SetParent(transform, false);
        go.SetActive(false);
    }
    void OnDestroyPooled(GameObject go) { if (go) Destroy(go); }
}
```

## Assets/Scripts/Warrior.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Warrior : BaseCharacter
{

    protected float hpLvlIncrease = 7.1f;
    protected float minAtkLvlIncrease = 2.1f;
    protected float maxAtkLvlIncrease = 2.2f;

    protected float endLVLinc = 4.57f;
    protected float strLVLinc = 3.93f;
    protected float agiLVLinc = 2.36f;
    protected float witLVLinc = 1.68f;
    protected float chaLVLinc = 2.07f;


    public Warrior(string name, int level)
        : base(name, "Warrior", level, 539.4f, 100f, 52.3f, 60.1f, 100f)
    {
        traits[TraitType.Endurance] = 9;
        traits[TraitType.Strength] = 8;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 3;
        traits[TraitType.Charm] = 5;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public Warrior()
    : base("", "Warrior", 5, 539.4f, 100f, 52.3f, 60.1f, 100f)
    {
        traits[TraitType.Endurance] = 9;
        traits[TraitType.Strength] = 8;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 3;
        traits[TraitType.Charm] = 5;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);
    }
}

```

## Assets/Scripts/XPCollectorRegistry.cs

```csharp
using System.Collections.Generic;
using System;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class XPCollectorRegistry : MonoBehaviour
{
    public static XPCollectorRegistry I { get; private set; }
    public readonly List<Collider> collectors = new();

    public static event Action OnChanged;

    void Awake() => I = this;

    public void Register(Collider c)
    {
        if (c && !collectors.Contains(c))
        {
            collectors.Add(c);
            OnChanged?.Invoke();
        }
    }

    public void Unregister(Collider c)
    {
        if (c && collectors.Remove(c))
            OnChanged?.Invoke();
    }

    // NEW: safe external notification hook (powerups restore registry)
    public void NotifyChanged() => OnChanged?.Invoke();
}
```

## Assets/Scripts/XPFormula.cs

```csharp
using System;
using UnityEngine;

public static class XPFormula
{
    // Tunables
    public static double A = 10.0;   // base; sets Lv1->2 XP
    public static double p = 1.3;    // base growth
    public static double bumpEvery = 5.0;
    public static double bumpWidth = 0.8;    // narrow = "brick wall"
    public static double bumpHeight = 0.20;  // 0.40 => +40% at the wall center

    // Logistic hump centered at `center`
    static double Bump(double n, double center, double width, double height)
    {
        // bounds/guard: avoid division by ~0 if someone sets width too tiny
        width = Math.Max(0.5, width);

        double x = (n - center) / width;
        double s = 1.0 / (1.0 + Math.Exp(-x));        // 0..1 sigmoid
        double hump = s * (1.0 - s) * 4.0;            // 0..1 bell, peak at center
        return 1.0 + height * hump;                   // 1.0 away from center, 1+height at center
    }

    // XP required to go from level n -> n+1; n >= 1
    public static int XpReq(int n)
    {
        double baseReq = A * Math.Pow(n, p);

        // With narrow width, only the nearest centers meaningfully affect n.
        // We multiply the nearest three (center, +/- 1 period) for clean tails.
        int center = (int)Math.Round(n / bumpEvery) * (int)bumpEvery;

        double mul =
            Bump(n, center - bumpEvery, bumpWidth, bumpHeight) *
            Bump(n, center, bumpWidth, bumpHeight) *
            Bump(n, center + bumpEvery, bumpWidth, bumpHeight);

        // Round to integer for final result
        return Mathf.Max(1, (int)Math.Round(baseReq * mul));
    }
}
```

## Assets/Scripts/XPNumberStyleSO.cs

```csharp
using UnityEngine;
using TMPro;

[CreateAssetMenu(menuName = "UI/XP Numbers/Style", fileName = "XPNumberStyle")]
public class XPNumberStyleSO : ScriptableObject
{
    [Header("Typography")]
    public TMP_FontAsset font;
    public Material fontMaterial;
    public float baseFontSize = 3.5f;
    public Color defaultColor = new Color(0.4f, 0.9f, 1f, 1f); // cyan-ish

    [Header("Timing")]
    public float duration = 0.8f;
    [Range(0.01f, 0.3f)] public float fadeInFraction = 0.08f;
    [Range(0.1f, 0.6f)] public float fadeOutFraction = 0.22f;

    [Header("Motion")]
    public float riseDistance = 1.0f;

    [Header("Scale Pop")]
    public float popFromScale = 0.6f;
    public float popToScale = 1.05f;

    [Header("Rendering")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 600;

    [Header("Update")]
    public bool useUnscaledTime = false;
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/Benchmark01.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class Benchmark01 : MonoBehaviour
    {

        public int BenchmarkType = 0;

        public TMP_FontAsset TMProFont;
        public Font TextMeshFont;

        private TextMeshPro m_textMeshPro;
        private TextContainer m_textContainer;
        private TextMesh m_textMesh;

        private const string label01 = "The <#0050FF>count is: </color>{0}";
        private const string label02 = "The <color=#0050FF>count is: </color>";

        //private string m_string;
        //private int m_frame;

        private Material m_material01;
        private Material m_material02;



        IEnumerator Start()
        {



            if (BenchmarkType == 0) // TextMesh Pro Component
            {
                m_textMeshPro = gameObject.AddComponent<TextMeshPro>();
                m_textMeshPro.autoSizeTextContainer = true;

                //m_textMeshPro.anchorDampening = true;

                if (TMProFont != null)
                    m_textMeshPro.font = TMProFont;

                //m_textMeshPro.font = Resources.Load("Fonts & Materials/Anton SDF", typeof(TextMeshProFont)) as TextMeshProFont; // Make sure the Anton SDF exists before calling this...
                //m_textMeshPro.fontSharedMaterial = Resources.Load("Fonts & Materials/Anton SDF", typeof(Material)) as Material; // Same as above make sure this material exists.

                m_textMeshPro.fontSize = 48;
                m_textMeshPro.alignment = TextAlignmentOptions.Center;
                //m_textMeshPro.anchor = AnchorPositions.Center;
                m_textMeshPro.extraPadding = true;
                //m_textMeshPro.outlineWidth = 0.25f;
                //m_textMeshPro.fontSharedMaterial.SetFloat("_OutlineWidth", 0.2f);
                //m_textMeshPro.fontSharedMaterial.EnableKeyword("UNDERLAY_ON");
                //m_textMeshPro.lineJustification = LineJustificationTypes.Center;
                m_textMeshPro.enableWordWrapping = false;    
                //m_textMeshPro.lineLength = 60;          
                //m_textMeshPro.characterSpacing = 0.2f;
                //m_textMeshPro.fontColor = new Color32(255, 255, 255, 255);

                m_material01 = m_textMeshPro.font.material;
                m_material02 = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - Drop Shadow"); // Make sure the LiberationSans SDF exists before calling this...  


            }
            else if (BenchmarkType == 1) // TextMesh
            {
                m_textMesh = gameObject.AddComponent<TextMesh>();

                if (TextMeshFont != null)
                {
                    m_textMesh.font = TextMeshFont;
                    m_textMesh.GetComponent<Renderer>().sharedMaterial = m_textMesh.font.material;
                }
                else
                {
                    m_textMesh.font = Resources.Load("Fonts/ARIAL", typeof(Font)) as Font;
                    m_textMesh.GetComponent<Renderer>().sharedMaterial = m_textMesh.font.material;
                }

                m_textMesh.fontSize = 48;
                m_textMesh.anchor = TextAnchor.MiddleCenter;

                //m_textMesh.color = new Color32(255, 255, 0, 255);
            }



            for (int i = 0; i <= 1000000; i++)
            {
                if (BenchmarkType == 0)
                {
                    m_textMeshPro.SetText(label01, i % 1000);
                    if (i % 1000 == 999)
                        m_textMeshPro.fontSharedMaterial = m_textMeshPro.fontSharedMaterial == m_material01 ? m_textMeshPro.fontSharedMaterial = m_material02 : m_textMeshPro.fontSharedMaterial = m_material01;



                }
                else if (BenchmarkType == 1)
                    m_textMesh.text = label02 + (i % 1000).ToString();

                yield return null;
            }


            yield return null;
        }


        /*
        void Update()
        {
            if (BenchmarkType == 0)
            {
                m_textMeshPro.text = (m_frame % 1000).ToString();
            }
            else if (BenchmarkType == 1)
            {
                m_textMesh.text = (m_frame % 1000).ToString();
            }

            m_frame += 1;
        }
        */
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/Benchmark01_UGUI.cs

```csharp
using UnityEngine;
using System.Collections;
using UnityEngine.UI;


namespace TMPro.Examples
{
    
    public class Benchmark01_UGUI : MonoBehaviour
    {

        public int BenchmarkType = 0;

        public Canvas canvas;
        public TMP_FontAsset TMProFont;
        public Font TextMeshFont;

        private TextMeshProUGUI m_textMeshPro;
        //private TextContainer m_textContainer;
        private Text m_textMesh;

        private const string label01 = "The <#0050FF>count is: </color>";
        private const string label02 = "The <color=#0050FF>count is: </color>";

        //private const string label01 = "TextMesh <#0050FF>Pro!</color>  The count is: {0}";
        //private const string label02 = "Text Mesh<color=#0050FF>        The count is: </color>";

        //private string m_string;
        //private int m_frame;

        private Material m_material01;
        private Material m_material02;



        IEnumerator Start()
        {



            if (BenchmarkType == 0) // TextMesh Pro Component
            {
                m_textMeshPro = gameObject.AddComponent<TextMeshProUGUI>();
                //m_textContainer = GetComponent<TextContainer>();


                //m_textMeshPro.anchorDampening = true;

                if (TMProFont != null)
                    m_textMeshPro.font = TMProFont;

                //m_textMeshPro.font = Resources.Load("Fonts & Materials/Anton SDF", typeof(TextMeshProFont)) as TextMeshProFont; // Make sure the Anton SDF exists before calling this...           
                //m_textMeshPro.fontSharedMaterial = Resources.Load("Fonts & Materials/Anton SDF", typeof(Material)) as Material; // Same as above make sure this material exists.

                m_textMeshPro.fontSize = 48;
                m_textMeshPro.alignment = TextAlignmentOptions.Center;
                //m_textMeshPro.anchor = AnchorPositions.Center;
                m_textMeshPro.extraPadding = true;
                //m_textMeshPro.outlineWidth = 0.25f;
                //m_textMeshPro.fontSharedMaterial.SetFloat("_OutlineWidth", 0.2f);
                //m_textMeshPro.fontSharedMaterial.EnableKeyword("UNDERLAY_ON");
                //m_textMeshPro.lineJustification = LineJustificationTypes.Center;
                //m_textMeshPro.enableWordWrapping = true;    
                //m_textMeshPro.lineLength = 60;          
                //m_textMeshPro.characterSpacing = 0.2f;
                //m_textMeshPro.fontColor = new Color32(255, 255, 255, 255);

                m_material01 = m_textMeshPro.font.material;
                m_material02 = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - BEVEL"); // Make sure the LiberationSans SDF exists before calling this...  


            }
            else if (BenchmarkType == 1) // TextMesh
            {
                m_textMesh = gameObject.AddComponent<Text>();

                if (TextMeshFont != null)
                {
                    m_textMesh.font = TextMeshFont;
                    //m_textMesh.renderer.sharedMaterial = m_textMesh.font.material;
                }
                else
                {
                    //m_textMesh.font = Resources.Load("Fonts/ARIAL", typeof(Font)) as Font;
                    //m_textMesh.renderer.sharedMaterial = m_textMesh.font.material;
                }

                m_textMesh.fontSize = 48;
                m_textMesh.alignment = TextAnchor.MiddleCenter;

                //m_textMesh.color = new Color32(255, 255, 0, 255);    
            }



            for (int i = 0; i <= 1000000; i++)
            {
                if (BenchmarkType == 0)
                {
                    m_textMeshPro.text = label01 + (i % 1000);
                    if (i % 1000 == 999)
                        m_textMeshPro.fontSharedMaterial = m_textMeshPro.fontSharedMaterial == m_material01 ? m_textMeshPro.fontSharedMaterial = m_material02 : m_textMeshPro.fontSharedMaterial = m_material01;



                }
                else if (BenchmarkType == 1)
                    m_textMesh.text = label02 + (i % 1000).ToString();

                yield return null;
            }


            yield return null;
        }


        /*
        void Update()
        {
            if (BenchmarkType == 0)
            {
                m_textMeshPro.text = (m_frame % 1000).ToString();            
            }
            else if (BenchmarkType == 1)
            {
                m_textMesh.text = (m_frame % 1000).ToString();
            }

            m_frame += 1;
        }
        */
    }

}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/Benchmark02.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class Benchmark02 : MonoBehaviour
    {

        public int SpawnType = 0;
        public int NumberOfNPC = 12;

        public bool IsTextObjectScaleStatic;
        private TextMeshProFloatingText floatingText_Script;


        void Start()
        {

            for (int i = 0; i < NumberOfNPC; i++)
            {


                if (SpawnType == 0)
                {
                    // TextMesh Pro Implementation
                    GameObject go = new GameObject();
                    go.transform.position = new Vector3(Random.Range(-95f, 95f), 0.25f, Random.Range(-95f, 95f));

                    TextMeshPro textMeshPro = go.AddComponent<TextMeshPro>();

                    textMeshPro.autoSizeTextContainer = true;
                    textMeshPro.rectTransform.pivot = new Vector2(0.5f, 0);

                    textMeshPro.alignment = TextAlignmentOptions.Bottom;
                    textMeshPro.fontSize = 96;
                    textMeshPro.enableKerning = false;

                    textMeshPro.color = new Color32(255, 255, 0, 255);
                    textMeshPro.text = "!";
                    textMeshPro.isTextObjectScaleStatic = IsTextObjectScaleStatic;

                    // Spawn Floating Text
                    floatingText_Script = go.AddComponent<TextMeshProFloatingText>();
                    floatingText_Script.SpawnType = 0;
                    floatingText_Script.IsTextObjectScaleStatic = IsTextObjectScaleStatic;
                }
                else if (SpawnType == 1)
                {
                    // TextMesh Implementation
                    GameObject go = new GameObject();
                    go.transform.position = new Vector3(Random.Range(-95f, 95f), 0.25f, Random.Range(-95f, 95f));

                    TextMesh textMesh = go.AddComponent<TextMesh>();
                    textMesh.font = Resources.Load<Font>("Fonts/ARIAL");
                    textMesh.GetComponent<Renderer>().sharedMaterial = textMesh.font.material;

                    textMesh.anchor = TextAnchor.LowerCenter;
                    textMesh.fontSize = 96;

                    textMesh.color = new Color32(255, 255, 0, 255);
                    textMesh.text = "!";

                    // Spawn Floating Text
                    floatingText_Script = go.AddComponent<TextMeshProFloatingText>();
                    floatingText_Script.SpawnType = 1;
                }
                else if (SpawnType == 2)
                {
                    // Canvas WorldSpace Camera
                    GameObject go = new GameObject();
                    Canvas canvas = go.AddComponent<Canvas>();
                    canvas.worldCamera = Camera.main;

                    go.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                    go.transform.position = new Vector3(Random.Range(-95f, 95f), 5f, Random.Range(-95f, 95f));

                    TextMeshProUGUI textObject = new GameObject().AddComponent<TextMeshProUGUI>();
                    textObject.rectTransform.SetParent(go.transform, false);

                    textObject.color = new Color32(255, 255, 0, 255);
                    textObject.alignment = TextAlignmentOptions.Bottom;
                    textObject.fontSize = 96;
                    textObject.text = "!";

                    // Spawn Floating Text
                    floatingText_Script = go.AddComponent<TextMeshProFloatingText>();
                    floatingText_Script.SpawnType = 0;
                }



            }
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/Benchmark03.cs

```csharp
using UnityEngine;
using System.Collections;
using UnityEngine.TextCore.LowLevel;


namespace TMPro.Examples
{

    public class Benchmark03 : MonoBehaviour
    {
        public enum BenchmarkType { TMP_SDF_MOBILE = 0, TMP_SDF__MOBILE_SSD = 1, TMP_SDF = 2, TMP_BITMAP_MOBILE = 3, TEXTMESH_BITMAP = 4 }

        public int NumberOfSamples = 100;
        public BenchmarkType Benchmark;

        public Font SourceFont;


        void Awake()
        {

        }


        void Start()
        {
            TMP_FontAsset fontAsset = null;

            // Create Dynamic Font Asset for the given font file.
            switch (Benchmark)
            {
                case BenchmarkType.TMP_SDF_MOBILE:
                    fontAsset = TMP_FontAsset.CreateFontAsset(SourceFont, 90, 9, GlyphRenderMode.SDFAA, 256, 256, AtlasPopulationMode.Dynamic);
                    break;
                case BenchmarkType.TMP_SDF__MOBILE_SSD:
                    fontAsset = TMP_FontAsset.CreateFontAsset(SourceFont, 90, 9, GlyphRenderMode.SDFAA, 256, 256, AtlasPopulationMode.Dynamic);
                    fontAsset.material.shader = Shader.Find("TextMeshPro/Mobile/Distance Field SSD");
                    break;
                case BenchmarkType.TMP_SDF:
                    fontAsset = TMP_FontAsset.CreateFontAsset(SourceFont, 90, 9, GlyphRenderMode.SDFAA, 256, 256, AtlasPopulationMode.Dynamic);
                    fontAsset.material.shader = Shader.Find("TextMeshPro/Distance Field");
                    break;
                case BenchmarkType.TMP_BITMAP_MOBILE:
                    fontAsset = TMP_FontAsset.CreateFontAsset(SourceFont, 90, 9, GlyphRenderMode.SMOOTH, 256, 256, AtlasPopulationMode.Dynamic);
                    break;
            }

            for (int i = 0; i < NumberOfSamples; i++)
            {
                switch (Benchmark)
                {
                    case BenchmarkType.TMP_SDF_MOBILE:
                    case BenchmarkType.TMP_SDF__MOBILE_SSD:
                    case BenchmarkType.TMP_SDF:
                    case BenchmarkType.TMP_BITMAP_MOBILE:
                        {
                            GameObject go = new GameObject();
                            go.transform.position = new Vector3(0, 1.2f, 0);

                            TextMeshPro textComponent = go.AddComponent<TextMeshPro>();
                            textComponent.font = fontAsset;
                            textComponent.fontSize = 128;
                            textComponent.text = "@";
                            textComponent.alignment = TextAlignmentOptions.Center;
                            textComponent.color = new Color32(255, 255, 0, 255);

                            if (Benchmark == BenchmarkType.TMP_BITMAP_MOBILE)
                                textComponent.fontSize = 132;

                        }
                        break;
                    case BenchmarkType.TEXTMESH_BITMAP:
                        {
                            GameObject go = new GameObject();
                            go.transform.position = new Vector3(0, 1.2f, 0);

                            TextMesh textMesh = go.AddComponent<TextMesh>();
                            textMesh.GetComponent<Renderer>().sharedMaterial = SourceFont.material;
                            textMesh.font = SourceFont;
                            textMesh.anchor = TextAnchor.MiddleCenter;
                            textMesh.fontSize = 130;

                            textMesh.color = new Color32(255, 255, 0, 255);
                            textMesh.text = "@";
                        }
                        break;
                }
            }
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/Benchmark04.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class Benchmark04 : MonoBehaviour
    {

        public int SpawnType = 0;

        public int MinPointSize = 12;
        public int MaxPointSize = 64;
        public int Steps = 4;

        private Transform m_Transform;
        //private TextMeshProFloatingText floatingText_Script;
        //public Material material;


        void Start()
        {
            m_Transform = transform;

            float lineHeight = 0;
            float orthoSize = Camera.main.orthographicSize = Screen.height / 2;
            float ratio = (float)Screen.width / Screen.height;

            for (int i = MinPointSize; i <= MaxPointSize; i += Steps)
            {
                if (SpawnType == 0)
                {
                    // TextMesh Pro Implementation
                    GameObject go = new GameObject("Text - " + i + " Pts");

                    if (lineHeight > orthoSize * 2) return;

                    go.transform.position = m_Transform.position + new Vector3(ratio * -orthoSize * 0.975f, orthoSize * 0.975f - lineHeight, 0);

                    TextMeshPro textMeshPro = go.AddComponent<TextMeshPro>();

                    //textMeshPro.fontSharedMaterial = material;
                    //textMeshPro.font = Resources.Load("Fonts & Materials/LiberationSans SDF", typeof(TextMeshProFont)) as TextMeshProFont;
                    //textMeshPro.anchor = AnchorPositions.Left;
                    textMeshPro.rectTransform.pivot = new Vector2(0, 0.5f);

                    textMeshPro.enableWordWrapping = false;
                    textMeshPro.extraPadding = true;
                    textMeshPro.isOrthographic = true;
                    textMeshPro.fontSize = i;

                    textMeshPro.text = i + " pts - Lorem ipsum dolor sit...";
                    textMeshPro.color = new Color32(255, 255, 255, 255);

                    lineHeight += i;
                }
                else
                {
                    // TextMesh Implementation
                    // Causes crashes since atlas needed exceeds 4096 X 4096
                    /*
                    GameObject go = new GameObject("Arial " + i);

                    //if (lineHeight > orthoSize * 2 * 0.9f) return;

                    go.transform.position = m_Transform.position + new Vector3(ratio * -orthoSize * 0.975f, orthoSize * 0.975f - lineHeight, 1);
                                       
                    TextMesh textMesh = go.AddComponent<TextMesh>();
                    textMesh.font = Resources.Load("Fonts/ARIAL", typeof(Font)) as Font;
                    textMesh.renderer.sharedMaterial = textMesh.font.material;
                    textMesh.anchor = TextAnchor.MiddleLeft;
                    textMesh.fontSize = i * 10;

                    textMesh.color = new Color32(255, 255, 255, 255);
                    textMesh.text = i + " pts - Lorem ipsum dolor sit...";

                    lineHeight += i;
                    */
                }
            }
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/CameraController.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class CameraController : MonoBehaviour
    {
        public enum CameraModes { Follow, Isometric, Free }

        private Transform cameraTransform;
        private Transform dummyTarget;

        public Transform CameraTarget;

        public float FollowDistance = 30.0f;
        public float MaxFollowDistance = 100.0f;
        public float MinFollowDistance = 2.0f;

        public float ElevationAngle = 30.0f;
        public float MaxElevationAngle = 85.0f;
        public float MinElevationAngle = 0f;

        public float OrbitalAngle = 0f;

        public CameraModes CameraMode = CameraModes.Follow;

        public bool MovementSmoothing = true;
        public bool RotationSmoothing = false;
        private bool previousSmoothing;

        public float MovementSmoothingValue = 25f;
        public float RotationSmoothingValue = 5.0f;

        public float MoveSensitivity = 2.0f;

        private Vector3 currentVelocity = Vector3.zero;
        private Vector3 desiredPosition;
        private float mouseX;
        private float mouseY;
        private Vector3 moveVector;
        private float mouseWheel;

        // Controls for Touches on Mobile devices
        //private float prev_ZoomDelta;


        private const string event_SmoothingValue = "Slider - Smoothing Value";
        private const string event_FollowDistance = "Slider - Camera Zoom";


        void Awake()
        {
            if (QualitySettings.vSyncCount > 0)
                Application.targetFrameRate = 60;
            else
                Application.targetFrameRate = -1;

            if (Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.Android)
                Input.simulateMouseWithTouches = false;

            cameraTransform = transform;
            previousSmoothing = MovementSmoothing;
        }


        // Use this for initialization
        void Start()
        {
            if (CameraTarget == null)
            {
                // If we don't have a target (assigned by the player, create a dummy in the center of the scene).
                dummyTarget = new GameObject("Camera Target").transform;
                CameraTarget = dummyTarget;
            }
        }

        // Update is called once per frame
        void LateUpdate()
        {
            GetPlayerInput();


            // Check if we still have a valid target
            if (CameraTarget != null)
            {
                if (CameraMode == CameraModes.Isometric)
                {
                    desiredPosition = CameraTarget.position + Quaternion.Euler(ElevationAngle, OrbitalAngle, 0f) * new Vector3(0, 0, -FollowDistance);
                }
                else if (CameraMode == CameraModes.Follow)
                {
                    desiredPosition = CameraTarget.position + CameraTarget.TransformDirection(Quaternion.Euler(ElevationAngle, OrbitalAngle, 0f) * (new Vector3(0, 0, -FollowDistance)));
                }
                else
                {
                    // Free Camera implementation
                }

                if (MovementSmoothing == true)
                {
                    // Using Smoothing
                    cameraTransform.position = Vector3.SmoothDamp(cameraTransform.position, desiredPosition, ref currentVelocity, MovementSmoothingValue * Time.fixedDeltaTime);
                    //cameraTransform.position = Vector3.Lerp(cameraTransform.position, desiredPosition, Time.deltaTime * 5.0f);
                }
                else
                {
                    // Not using Smoothing
                    cameraTransform.position = desiredPosition;
                }

                if (RotationSmoothing == true)
                    cameraTransform.rotation = Quaternion.Lerp(cameraTransform.rotation, Quaternion.LookRotation(CameraTarget.position - cameraTransform.position), RotationSmoothingValue * Time.deltaTime);
                else
                {
                    cameraTransform.LookAt(CameraTarget);
                }

            }

        }



        void GetPlayerInput()
        {
            moveVector = Vector3.zero;

            // Check Mouse Wheel Input prior to Shift Key so we can apply multiplier on Shift for Scrolling
            mouseWheel = Input.GetAxis("Mouse ScrollWheel");

            float touchCount = Input.touchCount;

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || touchCount > 0)
            {
                mouseWheel *= 10;

                if (Input.GetKeyDown(KeyCode.I))
                    CameraMode = CameraModes.Isometric;

                if (Input.GetKeyDown(KeyCode.F))
                    CameraMode = CameraModes.Follow;

                if (Input.GetKeyDown(KeyCode.S))
                    MovementSmoothing = !MovementSmoothing;


                // Check for right mouse button to change camera follow and elevation angle
                if (Input.GetMouseButton(1))
                {
                    mouseY = Input.GetAxis("Mouse Y");
                    mouseX = Input.GetAxis("Mouse X");

                    if (mouseY > 0.01f || mouseY < -0.01f)
                    {
                        ElevationAngle -= mouseY * MoveSensitivity;
                        // Limit Elevation angle between min & max values.
                        ElevationAngle = Mathf.Clamp(ElevationAngle, MinElevationAngle, MaxElevationAngle);
                    }

                    if (mouseX > 0.01f || mouseX < -0.01f)
                    {
                        OrbitalAngle += mouseX * MoveSensitivity;
                        if (OrbitalAngle > 360)
                            OrbitalAngle -= 360;
                        if (OrbitalAngle < 0)
                            OrbitalAngle += 360;
                    }
                }

                // Get Input from Mobile Device
                if (touchCount == 1 && Input.GetTouch(0).phase == TouchPhase.Moved)
                {
                    Vector2 deltaPosition = Input.GetTouch(0).deltaPosition;

                    // Handle elevation changes
                    if (deltaPosition.y > 0.01f || deltaPosition.y < -0.01f)
                    {
                        ElevationAngle -= deltaPosition.y * 0.1f;
                        // Limit Elevation angle between min & max values.
                        ElevationAngle = Mathf.Clamp(ElevationAngle, MinElevationAngle, MaxElevationAngle);
                    }


                    // Handle left & right 
                    if (deltaPosition.x > 0.01f || deltaPosition.x < -0.01f)
                    {
                        OrbitalAngle += deltaPosition.x * 0.1f;
                        if (OrbitalAngle > 360)
                            OrbitalAngle -= 360;
                        if (OrbitalAngle < 0)
                            OrbitalAngle += 360;
                    }

                }

                // Check for left mouse button to select a new CameraTarget or to reset Follow position
                if (Input.GetMouseButton(0))
                {
                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    RaycastHit hit;

                    if (Physics.Raycast(ray, out hit, 300, 1 << 10 | 1 << 11 | 1 << 12 | 1 << 14))
                    {
                        if (hit.transform == CameraTarget)
                        {
                            // Reset Follow Position
                            OrbitalAngle = 0;
                        }
                        else
                        {
                            CameraTarget = hit.transform;
                            OrbitalAngle = 0;
                            MovementSmoothing = previousSmoothing;
                        }

                    }
                }


                if (Input.GetMouseButton(2))
                {
                    if (dummyTarget == null)
                    {
                        // We need a Dummy Target to anchor the Camera
                        dummyTarget = new GameObject("Camera Target").transform;
                        dummyTarget.position = CameraTarget.position;
                        dummyTarget.rotation = CameraTarget.rotation;
                        CameraTarget = dummyTarget;
                        previousSmoothing = MovementSmoothing;
                        MovementSmoothing = false;
                    }
                    else if (dummyTarget != CameraTarget)
                    {
                        // Move DummyTarget to CameraTarget
                        dummyTarget.position = CameraTarget.position;
                        dummyTarget.rotation = CameraTarget.rotation;
                        CameraTarget = dummyTarget;
                        previousSmoothing = MovementSmoothing;
                        MovementSmoothing = false;
                    }


                    mouseY = Input.GetAxis("Mouse Y");
                    mouseX = Input.GetAxis("Mouse X");

                    moveVector = cameraTransform.TransformDirection(mouseX, mouseY, 0);

                    dummyTarget.Translate(-moveVector, Space.World);

                }

            }

            // Check Pinching to Zoom in - out on Mobile device
            if (touchCount == 2)
            {
                Touch touch0 = Input.GetTouch(0);
                Touch touch1 = Input.GetTouch(1);

                Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
                Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;

                float prevTouchDelta = (touch0PrevPos - touch1PrevPos).magnitude;
                float touchDelta = (touch0.position - touch1.position).magnitude;

                float zoomDelta = prevTouchDelta - touchDelta;

                if (zoomDelta > 0.01f || zoomDelta < -0.01f)
                {
                    FollowDistance += zoomDelta * 0.25f;
                    // Limit FollowDistance between min & max values.
                    FollowDistance = Mathf.Clamp(FollowDistance, MinFollowDistance, MaxFollowDistance);
                }


            }

            // Check MouseWheel to Zoom in-out
            if (mouseWheel < -0.01f || mouseWheel > 0.01f)
            {

                FollowDistance -= mouseWheel * 5.0f;
                // Limit FollowDistance between min & max values.
                FollowDistance = Mathf.Clamp(FollowDistance, MinFollowDistance, MaxFollowDistance);
            }


        }
    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/ChatController.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ChatController : MonoBehaviour {


    public TMP_InputField ChatInputField;

    public TMP_Text ChatDisplayOutput;

    public Scrollbar ChatScrollbar;

    void OnEnable()
    {
        ChatInputField.onSubmit.AddListener(AddToChatOutput);
    }

    void OnDisable()
    {
        ChatInputField.onSubmit.RemoveListener(AddToChatOutput);
    }


    void AddToChatOutput(string newText)
    {
        // Clear Input Field
        ChatInputField.text = string.Empty;

        var timeNow = System.DateTime.Now;

        string formattedInput = "[<#FFFF80>" + timeNow.Hour.ToString("d2") + ":" + timeNow.Minute.ToString("d2") + ":" + timeNow.Second.ToString("d2") + "</color>] " + newText;

        if (ChatDisplayOutput != null)
        {
            // No special formatting for first entry
            // Add line feed before each subsequent entries
            if (ChatDisplayOutput.text == string.Empty)
                ChatDisplayOutput.text = formattedInput;
            else
                ChatDisplayOutput.text += "\n" + formattedInput;
        }

        // Keep Chat input field active
        ChatInputField.ActivateInputField();

        // Set the scrollbar to the bottom when next text is submitted.
        ChatScrollbar.value = 0;
    }

}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/DropdownSample.cs

```csharp
using TMPro;
using UnityEngine;

public class DropdownSample: MonoBehaviour
{
	[SerializeField]
	private TextMeshProUGUI text = null;

	[SerializeField]
	private TMP_Dropdown dropdownWithoutPlaceholder = null;

	[SerializeField]
	private TMP_Dropdown dropdownWithPlaceholder = null;

	public void OnButtonClick()
	{
		text.text = dropdownWithPlaceholder.value > -1 ? "Selected values:\n" + dropdownWithoutPlaceholder.value + " - " + dropdownWithPlaceholder.value : "Error: Please make a selection";
	}
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/EnvMapAnimator.cs

```csharp
using UnityEngine;
using System.Collections;
using TMPro;

public class EnvMapAnimator : MonoBehaviour {

    //private Vector3 TranslationSpeeds;
    public Vector3 RotationSpeeds;
    private TMP_Text m_textMeshPro;
    private Material m_material;
    

    void Awake()
    {
        //Debug.Log("Awake() on Script called.");
        m_textMeshPro = GetComponent<TMP_Text>();
        m_material = m_textMeshPro.fontSharedMaterial;
    }

    // Use this for initialization
	IEnumerator Start ()
    {
        Matrix4x4 matrix = new Matrix4x4(); 
        
        while (true)
        {
            //matrix.SetTRS(new Vector3 (Time.time * TranslationSpeeds.x, Time.time * TranslationSpeeds.y, Time.time * TranslationSpeeds.z), Quaternion.Euler(Time.time * RotationSpeeds.x, Time.time * RotationSpeeds.y , Time.time * RotationSpeeds.z), Vector3.one);
             matrix.SetTRS(Vector3.zero, Quaternion.Euler(Time.time * RotationSpeeds.x, Time.time * RotationSpeeds.y , Time.time * RotationSpeeds.z), Vector3.one);

            m_material.SetMatrix("_EnvMatrix", matrix);

            yield return null;
        }
	}
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/ObjectSpin.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class ObjectSpin : MonoBehaviour
    {

#pragma warning disable 0414

        public float SpinSpeed = 5;
        public int RotationRange = 15;
        private Transform m_transform;

        private float m_time;
        private Vector3 m_prevPOS;
        private Vector3 m_initial_Rotation;
        private Vector3 m_initial_Position;
        private Color32 m_lightColor;
        private int frames = 0;

        public enum MotionType { Rotation, BackAndForth, Translation };
        public MotionType Motion;

        void Awake()
        {
            m_transform = transform;
            m_initial_Rotation = m_transform.rotation.eulerAngles;
            m_initial_Position = m_transform.position;

            Light light = GetComponent<Light>();
            m_lightColor = light != null ? light.color : Color.black;
        }


        // Update is called once per frame
        void Update()
        {
            if (Motion == MotionType.Rotation)
            {
                m_transform.Rotate(0, SpinSpeed * Time.deltaTime, 0);
            }
            else if (Motion == MotionType.BackAndForth)
            {
                m_time += SpinSpeed * Time.deltaTime;
                m_transform.rotation = Quaternion.Euler(m_initial_Rotation.x, Mathf.Sin(m_time) * RotationRange + m_initial_Rotation.y, m_initial_Rotation.z);
            }
            else
            {
                m_time += SpinSpeed * Time.deltaTime;

                float x = 15 * Mathf.Cos(m_time * .95f);
                float y = 10; // *Mathf.Sin(m_time * 1f) * Mathf.Cos(m_time * 1f);
                float z = 0f; // *Mathf.Sin(m_time * .9f);    

                m_transform.position = m_initial_Position + new Vector3(x, z, y);

                // Drawing light patterns because they can be cool looking.
                //if (frames > 2)
                //    Debug.DrawLine(m_transform.position, m_prevPOS, m_lightColor, 100f);

                m_prevPOS = m_transform.position;
                frames += 1;
            }
        }
    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/ShaderPropAnimator.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class ShaderPropAnimator : MonoBehaviour
    {

        private Renderer m_Renderer;
        private Material m_Material;

        public AnimationCurve GlowCurve;

        public float m_frame;

        void Awake()
        {
            // Cache a reference to object's renderer
            m_Renderer = GetComponent<Renderer>();

            // Cache a reference to object's material and create an instance by doing so.
            m_Material = m_Renderer.material;
        }

        void Start()
        {
            StartCoroutine(AnimateProperties());
        }

        IEnumerator AnimateProperties()
        {
            //float lightAngle;
            float glowPower;
            m_frame = Random.Range(0f, 1f);

            while (true)
            {
                //lightAngle = (m_Material.GetFloat(ShaderPropertyIDs.ID_LightAngle) + Time.deltaTime) % 6.2831853f;
                //m_Material.SetFloat(ShaderPropertyIDs.ID_LightAngle, lightAngle);

                glowPower = GlowCurve.Evaluate(m_frame);
                m_Material.SetFloat(ShaderUtilities.ID_GlowPower, glowPower);

                m_frame += Time.deltaTime * Random.Range(0.2f, 0.3f);
                yield return new WaitForEndOfFrame();
            }
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/SimpleScript.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class SimpleScript : MonoBehaviour
    {

        private TextMeshPro m_textMeshPro;
        //private TMP_FontAsset m_FontAsset;

        private const string label = "The <#0050FF>count is: </color>{0:2}";
        private float m_frame;


        void Start()
        {
            // Add new TextMesh Pro Component
            m_textMeshPro = gameObject.AddComponent<TextMeshPro>();

            m_textMeshPro.autoSizeTextContainer = true;

            // Load the Font Asset to be used.
            //m_FontAsset = Resources.Load("Fonts & Materials/LiberationSans SDF", typeof(TMP_FontAsset)) as TMP_FontAsset;
            //m_textMeshPro.font = m_FontAsset;

            // Assign Material to TextMesh Pro Component
            //m_textMeshPro.fontSharedMaterial = Resources.Load("Fonts & Materials/LiberationSans SDF - Bevel", typeof(Material)) as Material;
            //m_textMeshPro.fontSharedMaterial.EnableKeyword("BEVEL_ON");
            
            // Set various font settings.
            m_textMeshPro.fontSize = 48;

            m_textMeshPro.alignment = TextAlignmentOptions.Center;
            
            //m_textMeshPro.anchorDampening = true; // Has been deprecated but under consideration for re-implementation.
            //m_textMeshPro.enableAutoSizing = true;

            //m_textMeshPro.characterSpacing = 0.2f;
            //m_textMeshPro.wordSpacing = 0.1f;

            //m_textMeshPro.enableCulling = true;
            m_textMeshPro.enableWordWrapping = false;

            //textMeshPro.fontColor = new Color32(255, 255, 255, 255);
        }


        void Update()
        {
            m_textMeshPro.SetText(label, m_frame % 1000);
            m_frame += 1 * Time.deltaTime;
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/SkewTextExample.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class SkewTextExample : MonoBehaviour
    {

        private TMP_Text m_TextComponent;

        public AnimationCurve VertexCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.25f, 2.0f), new Keyframe(0.5f, 0), new Keyframe(0.75f, 2.0f), new Keyframe(1, 0f));
        //public float AngleMultiplier = 1.0f;
        //public float SpeedMultiplier = 1.0f;
        public float CurveScale = 1.0f;
        public float ShearAmount = 1.0f;

        void Awake()
        {
            m_TextComponent = gameObject.GetComponent<TMP_Text>();
        }


        void Start()
        {
            StartCoroutine(WarpText());
        }


        private AnimationCurve CopyAnimationCurve(AnimationCurve curve)
        {
            AnimationCurve newCurve = new AnimationCurve();

            newCurve.keys = curve.keys;

            return newCurve;
        }


        /// <summary>
        ///  Method to curve text along a Unity animation curve.
        /// </summary>
        /// <param name="textComponent"></param>
        /// <returns></returns>
        IEnumerator WarpText()
        {
            VertexCurve.preWrapMode = WrapMode.Clamp;
            VertexCurve.postWrapMode = WrapMode.Clamp;

            //Mesh mesh = m_TextComponent.textInfo.meshInfo[0].mesh;

            Vector3[] vertices;
            Matrix4x4 matrix;

            m_TextComponent.havePropertiesChanged = true; // Need to force the TextMeshPro Object to be updated.
            CurveScale *= 10;
            float old_CurveScale = CurveScale;
            float old_ShearValue = ShearAmount;
            AnimationCurve old_curve = CopyAnimationCurve(VertexCurve);

            while (true)
            {
                if (!m_TextComponent.havePropertiesChanged && old_CurveScale == CurveScale && old_curve.keys[1].value == VertexCurve.keys[1].value && old_ShearValue == ShearAmount)
                {
                    yield return null;
                    continue;
                }

                old_CurveScale = CurveScale;
                old_curve = CopyAnimationCurve(VertexCurve);
                old_ShearValue = ShearAmount;

                m_TextComponent.ForceMeshUpdate(); // Generate the mesh and populate the textInfo with data we can use and manipulate.

                TMP_TextInfo textInfo = m_TextComponent.textInfo;
                int characterCount = textInfo.characterCount;


                if (characterCount == 0) continue;

                //vertices = textInfo.meshInfo[0].vertices;
                //int lastVertexIndex = textInfo.characterInfo[characterCount - 1].vertexIndex;

                float boundsMinX = m_TextComponent.bounds.min.x;  //textInfo.meshInfo[0].mesh.bounds.min.x;
                float boundsMaxX = m_TextComponent.bounds.max.x;  //textInfo.meshInfo[0].mesh.bounds.max.x;



                for (int i = 0; i < characterCount; i++)
                {
                    if (!textInfo.characterInfo[i].isVisible)
                        continue;

                    int vertexIndex = textInfo.characterInfo[i].vertexIndex;

                    // Get the index of the mesh used by this character.
                    int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;

                    vertices = textInfo.meshInfo[materialIndex].vertices;

                    // Compute the baseline mid point for each character
                    Vector3 offsetToMidBaseline = new Vector2((vertices[vertexIndex + 0].x + vertices[vertexIndex + 2].x) / 2, textInfo.characterInfo[i].baseLine);
                    //float offsetY = VertexCurve.Evaluate((float)i / characterCount + loopCount / 50f); // Random.Range(-0.25f, 0.25f);

                    // Apply offset to adjust our pivot point.
                    vertices[vertexIndex + 0] += -offsetToMidBaseline;
                    vertices[vertexIndex + 1] += -offsetToMidBaseline;
                    vertices[vertexIndex + 2] += -offsetToMidBaseline;
                    vertices[vertexIndex + 3] += -offsetToMidBaseline;

                    // Apply the Shearing FX
                    float shear_value = ShearAmount * 0.01f;
                    Vector3 topShear = new Vector3(shear_value * (textInfo.characterInfo[i].topRight.y - textInfo.characterInfo[i].baseLine), 0, 0);
                    Vector3 bottomShear = new Vector3(shear_value * (textInfo.characterInfo[i].baseLine - textInfo.characterInfo[i].bottomRight.y), 0, 0);

                    vertices[vertexIndex + 0] += -bottomShear;
                    vertices[vertexIndex + 1] += topShear;
                    vertices[vertexIndex + 2] += topShear;
                    vertices[vertexIndex + 3] += -bottomShear;


                    // Compute the angle of rotation for each character based on the animation curve
                    float x0 = (offsetToMidBaseline.x - boundsMinX) / (boundsMaxX - boundsMinX); // Character's position relative to the bounds of the mesh.
                    float x1 = x0 + 0.0001f;
                    float y0 = VertexCurve.Evaluate(x0) * CurveScale;
                    float y1 = VertexCurve.Evaluate(x1) * CurveScale;

                    Vector3 horizontal = new Vector3(1, 0, 0);
                    //Vector3 normal = new Vector3(-(y1 - y0), (x1 * (boundsMaxX - boundsMinX) + boundsMinX) - offsetToMidBaseline.x, 0);
                    Vector3 tangent = new Vector3(x1 * (boundsMaxX - boundsMinX) + boundsMinX, y1) - new Vector3(offsetToMidBaseline.x, y0);

                    float dot = Mathf.Acos(Vector3.Dot(horizontal, tangent.normalized)) * 57.2957795f;
                    Vector3 cross = Vector3.Cross(horizontal, tangent);
                    float angle = cross.z > 0 ? dot : 360 - dot;

                    matrix = Matrix4x4.TRS(new Vector3(0, y0, 0), Quaternion.Euler(0, 0, angle), Vector3.one);

                    vertices[vertexIndex + 0] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 0]);
                    vertices[vertexIndex + 1] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 1]);
                    vertices[vertexIndex + 2] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 2]);
                    vertices[vertexIndex + 3] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 3]);

                    vertices[vertexIndex + 0] += offsetToMidBaseline;
                    vertices[vertexIndex + 1] += offsetToMidBaseline;
                    vertices[vertexIndex + 2] += offsetToMidBaseline;
                    vertices[vertexIndex + 3] += offsetToMidBaseline;
                }


                // Upload the mesh with the revised information
                m_TextComponent.UpdateVertexData();

                yield return null; // new WaitForSeconds(0.025f);
            }
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TeleType.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class TeleType : MonoBehaviour
    {


        //[Range(0, 100)]
        //public int RevealSpeed = 50;

        private string label01 = "Example <sprite=2> of using <sprite=7> <#ffa000>Graphics Inline</color> <sprite=5> with Text in <font=\"Bangers SDF\" material=\"Bangers SDF - Drop Shadow\">TextMesh<#40a0ff>Pro</color></font><sprite=0> and Unity<sprite=1>";
        private string label02 = "Example <sprite=2> of using <sprite=7> <#ffa000>Graphics Inline</color> <sprite=5> with Text in <font=\"Bangers SDF\" material=\"Bangers SDF - Drop Shadow\">TextMesh<#40a0ff>Pro</color></font><sprite=0> and Unity<sprite=2>";


        private TMP_Text m_textMeshPro;


        void Awake()
        {
            // Get Reference to TextMeshPro Component
            m_textMeshPro = GetComponent<TMP_Text>();
            m_textMeshPro.text = label01;
            m_textMeshPro.enableWordWrapping = true;
            m_textMeshPro.alignment = TextAlignmentOptions.Top;



            //if (GetComponentInParent(typeof(Canvas)) as Canvas == null)
            //{
            //    GameObject canvas = new GameObject("Canvas", typeof(Canvas));
            //    gameObject.transform.SetParent(canvas.transform);
            //    canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            //    // Set RectTransform Size
            //    gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 300);
            //    m_textMeshPro.fontSize = 48;
            //}


        }


        IEnumerator Start()
        {

            // Force and update of the mesh to get valid information.
            m_textMeshPro.ForceMeshUpdate();


            int totalVisibleCharacters = m_textMeshPro.textInfo.characterCount; // Get # of Visible Character in text object
            int counter = 0;
            int visibleCount = 0;

            while (true)
            {
                visibleCount = counter % (totalVisibleCharacters + 1);

                m_textMeshPro.maxVisibleCharacters = visibleCount; // How many characters should TextMeshPro display?

                // Once the last character has been revealed, wait 1.0 second and start over.
                if (visibleCount >= totalVisibleCharacters)
                {
                    yield return new WaitForSeconds(1.0f);
                    m_textMeshPro.text = label02;
                    yield return new WaitForSeconds(1.0f);
                    m_textMeshPro.text = label01;
                    yield return new WaitForSeconds(1.0f);
                }

                counter += 1;

                yield return new WaitForSeconds(0.05f);
            }

            //Debug.Log("Done revealing the text.");
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TextConsoleSimulator.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    public class TextConsoleSimulator : MonoBehaviour
    {
        private TMP_Text m_TextComponent;
        private bool hasTextChanged;

        void Awake()
        {
            m_TextComponent = gameObject.GetComponent<TMP_Text>();
        }


        void Start()
        {
            StartCoroutine(RevealCharacters(m_TextComponent));
            //StartCoroutine(RevealWords(m_TextComponent));
        }


        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        // Event received when the text object has changed.
        void ON_TEXT_CHANGED(Object obj)
        {
            hasTextChanged = true;
        }


        /// <summary>
        /// Method revealing the text one character at a time.
        /// </summary>
        /// <returns></returns>
        IEnumerator RevealCharacters(TMP_Text textComponent)
        {
            textComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = textComponent.textInfo;

            int totalVisibleCharacters = textInfo.characterCount; // Get # of Visible Character in text object
            int visibleCount = 0;

            while (true)
            {
                if (hasTextChanged)
                {
                    totalVisibleCharacters = textInfo.characterCount; // Update visible character count.
                    hasTextChanged = false; 
                }

                if (visibleCount > totalVisibleCharacters)
                {
                    yield return new WaitForSeconds(1.0f);
                    visibleCount = 0;
                }

                textComponent.maxVisibleCharacters = visibleCount; // How many characters should TextMeshPro display?

                visibleCount += 1;

                yield return null;
            }
        }


        /// <summary>
        /// Method revealing the text one word at a time.
        /// </summary>
        /// <returns></returns>
        IEnumerator RevealWords(TMP_Text textComponent)
        {
            textComponent.ForceMeshUpdate();

            int totalWordCount = textComponent.textInfo.wordCount;
            int totalVisibleCharacters = textComponent.textInfo.characterCount; // Get # of Visible Character in text object
            int counter = 0;
            int currentWord = 0;
            int visibleCount = 0;

            while (true)
            {
                currentWord = counter % (totalWordCount + 1);

                // Get last character index for the current word.
                if (currentWord == 0) // Display no words.
                    visibleCount = 0;
                else if (currentWord < totalWordCount) // Display all other words with the exception of the last one.
                    visibleCount = textComponent.textInfo.wordInfo[currentWord - 1].lastCharacterIndex + 1;
                else if (currentWord == totalWordCount) // Display last word and all remaining characters.
                    visibleCount = totalVisibleCharacters;

                textComponent.maxVisibleCharacters = visibleCount; // How many characters should TextMeshPro display?

                // Once the last character has been revealed, wait 1.0 second and start over.
                if (visibleCount >= totalVisibleCharacters)
                {
                    yield return new WaitForSeconds(1.0f);
                }

                counter += 1;

                yield return new WaitForSeconds(0.1f);
            }
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TextMeshProFloatingText.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class TextMeshProFloatingText : MonoBehaviour
    {
        public Font TheFont;

        private GameObject m_floatingText;
        private TextMeshPro m_textMeshPro;
        private TextMesh m_textMesh;

        private Transform m_transform;
        private Transform m_floatingText_Transform;
        private Transform m_cameraTransform;

        Vector3 lastPOS = Vector3.zero;
        Quaternion lastRotation = Quaternion.identity;

        public int SpawnType;
        public bool IsTextObjectScaleStatic;

        //private int m_frame = 0;

        static WaitForEndOfFrame k_WaitForEndOfFrame = new WaitForEndOfFrame();
        static WaitForSeconds[] k_WaitForSecondsRandom = new WaitForSeconds[]
        {
            new WaitForSeconds(0.05f), new WaitForSeconds(0.1f), new WaitForSeconds(0.15f), new WaitForSeconds(0.2f), new WaitForSeconds(0.25f),
            new WaitForSeconds(0.3f), new WaitForSeconds(0.35f), new WaitForSeconds(0.4f), new WaitForSeconds(0.45f), new WaitForSeconds(0.5f),
            new WaitForSeconds(0.55f), new WaitForSeconds(0.6f), new WaitForSeconds(0.65f), new WaitForSeconds(0.7f), new WaitForSeconds(0.75f),
            new WaitForSeconds(0.8f), new WaitForSeconds(0.85f), new WaitForSeconds(0.9f), new WaitForSeconds(0.95f), new WaitForSeconds(1.0f),
        };

        void Awake()
        {
            m_transform = transform;
            m_floatingText = new GameObject(this.name + " floating text");

            // Reference to Transform is lost when TMP component is added since it replaces it by a RectTransform.
            //m_floatingText_Transform = m_floatingText.transform;
            //m_floatingText_Transform.position = m_transform.position + new Vector3(0, 15f, 0);

            m_cameraTransform = Camera.main.transform;
        }

        void Start()
        {
            if (SpawnType == 0)
            {
                // TextMesh Pro Implementation
                m_textMeshPro = m_floatingText.AddComponent<TextMeshPro>();
                m_textMeshPro.rectTransform.sizeDelta = new Vector2(3, 3);

                m_floatingText_Transform = m_floatingText.transform;
                m_floatingText_Transform.position = m_transform.position + new Vector3(0, 15f, 0);

                //m_textMeshPro.fontAsset = Resources.Load("Fonts & Materials/JOKERMAN SDF", typeof(TextMeshProFont)) as TextMeshProFont; // User should only provide a string to the resource.
                //m_textMeshPro.fontSharedMaterial = Resources.Load("Fonts & Materials/LiberationSans SDF", typeof(Material)) as Material;

                m_textMeshPro.alignment = TextAlignmentOptions.Center;
                m_textMeshPro.color = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);
                m_textMeshPro.fontSize = 24;
                //m_textMeshPro.enableExtraPadding = true;
                //m_textMeshPro.enableShadows = false;
                m_textMeshPro.enableKerning = false;
                m_textMeshPro.text = string.Empty;
                m_textMeshPro.isTextObjectScaleStatic = IsTextObjectScaleStatic;

                StartCoroutine(DisplayTextMeshProFloatingText());
            }
            else if (SpawnType == 1)
            {
                //Debug.Log("Spawning TextMesh Objects.");

                m_floatingText_Transform = m_floatingText.transform;
                m_floatingText_Transform.position = m_transform.position + new Vector3(0, 15f, 0);

                m_textMesh = m_floatingText.AddComponent<TextMesh>();
                m_textMesh.font = Resources.Load<Font>("Fonts/ARIAL");
                m_textMesh.GetComponent<Renderer>().sharedMaterial = m_textMesh.font.material;
                m_textMesh.color = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);
                m_textMesh.anchor = TextAnchor.LowerCenter;
                m_textMesh.fontSize = 24;

                StartCoroutine(DisplayTextMeshFloatingText());
            }
            else if (SpawnType == 2)
            {

            }

        }


        //void Update()
        //{
        //    if (SpawnType == 0)
        //    {
        //        m_textMeshPro.SetText("{0}", m_frame);
        //    }
        //    else
        //    {
        //        m_textMesh.text = m_frame.ToString();
        //    }
        //    m_frame = (m_frame + 1) % 1000;

        //}


        public IEnumerator DisplayTextMeshProFloatingText()
        {
            float CountDuration = 2.0f; // How long is the countdown alive.
            float starting_Count = Random.Range(5f, 20f); // At what number is the counter starting at.
            float current_Count = starting_Count;

            Vector3 start_pos = m_floatingText_Transform.position;
            Color32 start_color = m_textMeshPro.color;
            float alpha = 255;
            int int_counter = 0;


            float fadeDuration = 3 / starting_Count * CountDuration;

            while (current_Count > 0)
            {
                current_Count -= (Time.deltaTime / CountDuration) * starting_Count;

                if (current_Count <= 3)
                {
                    //Debug.Log("Fading Counter ... " + current_Count.ToString("f2"));
                    alpha = Mathf.Clamp(alpha - (Time.deltaTime / fadeDuration) * 255, 0, 255);
                }

                int_counter = (int)current_Count;
                m_textMeshPro.text = int_counter.ToString();
                //m_textMeshPro.SetText("{0}", (int)current_Count);

                m_textMeshPro.color = new Color32(start_color.r, start_color.g, start_color.b, (byte)alpha);

                // Move the floating text upward each update
                m_floatingText_Transform.position += new Vector3(0, starting_Count * Time.deltaTime, 0);

                // Align floating text perpendicular to Camera.
                if (!lastPOS.Compare(m_cameraTransform.position, 1000) || !lastRotation.Compare(m_cameraTransform.rotation, 1000))
                {
                    lastPOS = m_cameraTransform.position;
                    lastRotation = m_cameraTransform.rotation;
                    m_floatingText_Transform.rotation = lastRotation;
                    Vector3 dir = m_transform.position - lastPOS;
                    m_transform.forward = new Vector3(dir.x, 0, dir.z);
                }

                yield return k_WaitForEndOfFrame;
            }

            //Debug.Log("Done Counting down.");

            yield return k_WaitForSecondsRandom[Random.Range(0, 19)];

            m_floatingText_Transform.position = start_pos;

            StartCoroutine(DisplayTextMeshProFloatingText());
        }


        public IEnumerator DisplayTextMeshFloatingText()
        {
            float CountDuration = 2.0f; // How long is the countdown alive.
            float starting_Count = Random.Range(5f, 20f); // At what number is the counter starting at.
            float current_Count = starting_Count;

            Vector3 start_pos = m_floatingText_Transform.position;
            Color32 start_color = m_textMesh.color;
            float alpha = 255;
            int int_counter = 0;

            float fadeDuration = 3 / starting_Count * CountDuration;

            while (current_Count > 0)
            {
                current_Count -= (Time.deltaTime / CountDuration) * starting_Count;

                if (current_Count <= 3)
                {
                    //Debug.Log("Fading Counter ... " + current_Count.ToString("f2"));
                    alpha = Mathf.Clamp(alpha - (Time.deltaTime / fadeDuration) * 255, 0, 255);
                }

                int_counter = (int)current_Count;
                m_textMesh.text = int_counter.ToString();
                //Debug.Log("Current Count:" + current_Count.ToString("f2"));

                m_textMesh.color = new Color32(start_color.r, start_color.g, start_color.b, (byte)alpha);

                // Move the floating text upward each update
                m_floatingText_Transform.position += new Vector3(0, starting_Count * Time.deltaTime, 0);

                // Align floating text perpendicular to Camera.
                if (!lastPOS.Compare(m_cameraTransform.position, 1000) || !lastRotation.Compare(m_cameraTransform.rotation, 1000))
                {
                    lastPOS = m_cameraTransform.position;
                    lastRotation = m_cameraTransform.rotation;
                    m_floatingText_Transform.rotation = lastRotation;
                    Vector3 dir = m_transform.position - lastPOS;
                    m_transform.forward = new Vector3(dir.x, 0, dir.z);
                }

                yield return k_WaitForEndOfFrame;
            }

            //Debug.Log("Done Counting down.");

            yield return k_WaitForSecondsRandom[Random.Range(0, 20)];

            m_floatingText_Transform.position = start_pos;

            StartCoroutine(DisplayTextMeshFloatingText());
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TextMeshSpawner.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class TextMeshSpawner : MonoBehaviour
    {

        public int SpawnType = 0;
        public int NumberOfNPC = 12;

        public Font TheFont;

        private TextMeshProFloatingText floatingText_Script;

        void Awake()
        {

        }

        void Start()
        {

            for (int i = 0; i < NumberOfNPC; i++)
            {
                if (SpawnType == 0)
                {
                    // TextMesh Pro Implementation     
                    //go.transform.localScale = new Vector3(2, 2, 2);
                    GameObject go = new GameObject(); //"NPC " + i);
                    go.transform.position = new Vector3(Random.Range(-95f, 95f), 0.5f, Random.Range(-95f, 95f));

                    //go.transform.position = new Vector3(0, 1.01f, 0);
                    //go.renderer.castShadows = false;
                    //go.renderer.receiveShadows = false;
                    //go.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

                    TextMeshPro textMeshPro = go.AddComponent<TextMeshPro>();
                    //textMeshPro.FontAsset = Resources.Load("Fonts & Materials/LiberationSans SDF", typeof(TextMeshProFont)) as TextMeshProFont;
                    //textMeshPro.anchor = AnchorPositions.Bottom;
                    textMeshPro.fontSize = 96;

                    textMeshPro.text = "!";
                    textMeshPro.color = new Color32(255, 255, 0, 255);
                    //textMeshPro.Text = "!";


                    // Spawn Floating Text
                    floatingText_Script = go.AddComponent<TextMeshProFloatingText>();
                    floatingText_Script.SpawnType = 0;
                }
                else
                {
                    // TextMesh Implementation
                    GameObject go = new GameObject(); //"NPC " + i);
                    go.transform.position = new Vector3(Random.Range(-95f, 95f), 0.5f, Random.Range(-95f, 95f));

                    //go.transform.position = new Vector3(0, 1.01f, 0);

                    TextMesh textMesh = go.AddComponent<TextMesh>();
                    textMesh.GetComponent<Renderer>().sharedMaterial = TheFont.material;
                    textMesh.font = TheFont;
                    textMesh.anchor = TextAnchor.LowerCenter;
                    textMesh.fontSize = 96;

                    textMesh.color = new Color32(255, 255, 0, 255);
                    textMesh.text = "!";

                    // Spawn Floating Text
                    floatingText_Script = go.AddComponent<TextMeshProFloatingText>();
                    floatingText_Script.SpawnType = 1;
                }
            }
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMPro_InstructionOverlay.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class TMPro_InstructionOverlay : MonoBehaviour
    {

        public enum FpsCounterAnchorPositions { TopLeft, BottomLeft, TopRight, BottomRight };

        public FpsCounterAnchorPositions AnchorPosition = FpsCounterAnchorPositions.BottomLeft;

        private const string instructions = "Camera Control - <#ffff00>Shift + RMB\n</color>Zoom - <#ffff00>Mouse wheel.";

        private TextMeshPro m_TextMeshPro;
        private TextContainer m_textContainer;
        private Transform m_frameCounter_transform;
        private Camera m_camera;

        //private FpsCounterAnchorPositions last_AnchorPosition;

        void Awake()
        {
            if (!enabled)
                return;

            m_camera = Camera.main;

            GameObject frameCounter = new GameObject("Frame Counter");
            m_frameCounter_transform = frameCounter.transform;
            m_frameCounter_transform.parent = m_camera.transform;
            m_frameCounter_transform.localRotation = Quaternion.identity;


            m_TextMeshPro = frameCounter.AddComponent<TextMeshPro>();
            m_TextMeshPro.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            m_TextMeshPro.fontSharedMaterial = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - Overlay");

            m_TextMeshPro.fontSize = 30;

            m_TextMeshPro.isOverlay = true;
            m_textContainer = frameCounter.GetComponent<TextContainer>();

            Set_FrameCounter_Position(AnchorPosition);
            //last_AnchorPosition = AnchorPosition;

            m_TextMeshPro.text = instructions;

        }




        void Set_FrameCounter_Position(FpsCounterAnchorPositions anchor_position)
        {

            switch (anchor_position)
            {
                case FpsCounterAnchorPositions.TopLeft:
                    //m_TextMeshPro.anchor = AnchorPositions.TopLeft;
                    m_textContainer.anchorPosition = TextContainerAnchors.TopLeft;
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(0, 1, 100.0f));
                    break;
                case FpsCounterAnchorPositions.BottomLeft:
                    //m_TextMeshPro.anchor = AnchorPositions.BottomLeft;
                    m_textContainer.anchorPosition = TextContainerAnchors.BottomLeft;
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(0, 0, 100.0f));
                    break;
                case FpsCounterAnchorPositions.TopRight:
                    //m_TextMeshPro.anchor = AnchorPositions.TopRight;
                    m_textContainer.anchorPosition = TextContainerAnchors.TopRight;
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(1, 1, 100.0f));
                    break;
                case FpsCounterAnchorPositions.BottomRight:
                    //m_TextMeshPro.anchor = AnchorPositions.BottomRight;
                    m_textContainer.anchorPosition = TextContainerAnchors.BottomRight;
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(1, 0, 100.0f));
                    break;
            }
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_DigitValidator.cs

```csharp
using UnityEngine;
using System;


namespace TMPro
{
    /// <summary>
    /// EXample of a Custom Character Input Validator to only allow digits from 0 to 9.
    /// </summary>
    [Serializable]
    //[CreateAssetMenu(fileName = "InputValidator - Digits.asset", menuName = "TextMeshPro/Input Validators/Digits", order = 100)]
    public class TMP_DigitValidator : TMP_InputValidator
    {
        // Custom text input validation function
        public override char Validate(ref string text, ref int pos, char ch)
        {
            if (ch >= '0' && ch <= '9')
            {
                text += ch;
                pos += 1;
                return ch;
            }

            return (char)0;
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_ExampleScript_01.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;


namespace TMPro.Examples
{

    public class TMP_ExampleScript_01 : MonoBehaviour
    {
        public enum objectType { TextMeshPro = 0, TextMeshProUGUI = 1 };

        public objectType ObjectType;
        public bool isStatic;

        private TMP_Text m_text;

        //private TMP_InputField m_inputfield;


        private const string k_label = "The count is <#0080ff>{0}</color>";
        private int count;

        void Awake()
        {
            // Get a reference to the TMP text component if one already exists otherwise add one.
            // This example show the convenience of having both TMP components derive from TMP_Text. 
            if (ObjectType == 0)
                m_text = GetComponent<TextMeshPro>() ?? gameObject.AddComponent<TextMeshPro>();
            else
                m_text = GetComponent<TextMeshProUGUI>() ?? gameObject.AddComponent<TextMeshProUGUI>();

            // Load a new font asset and assign it to the text object.
            m_text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Anton SDF");

            // Load a new material preset which was created with the context menu duplicate.
            m_text.fontSharedMaterial = Resources.Load<Material>("Fonts & Materials/Anton SDF - Drop Shadow");

            // Set the size of the font.
            m_text.fontSize = 120;

            // Set the text
            m_text.text = "A <#0080ff>simple</color> line of text.";

            // Get the preferred width and height based on the supplied width and height as opposed to the actual size of the current text container.
            Vector2 size = m_text.GetPreferredValues(Mathf.Infinity, Mathf.Infinity);

            // Set the size of the RectTransform based on the new calculated values.
            m_text.rectTransform.sizeDelta = new Vector2(size.x, size.y);
        }


        void Update()
        {
            if (!isStatic)
            {
                m_text.SetText(k_label, count % 1000);
                count += 1;
            }
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_FrameRateCounter.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class TMP_FrameRateCounter : MonoBehaviour
    {
        public float UpdateInterval = 5.0f;
        private float m_LastInterval = 0;
        private int m_Frames = 0;

        public enum FpsCounterAnchorPositions { TopLeft, BottomLeft, TopRight, BottomRight };

        public FpsCounterAnchorPositions AnchorPosition = FpsCounterAnchorPositions.TopRight;

        private string htmlColorTag;
        private const string fpsLabel = "{0:2}</color> <#8080ff>FPS \n<#FF8000>{1:2} <#8080ff>MS";

        private TextMeshPro m_TextMeshPro;
        private Transform m_frameCounter_transform;
        private Camera m_camera;

        private FpsCounterAnchorPositions last_AnchorPosition;

        void Awake()
        {
            if (!enabled)
                return;

            m_camera = Camera.main;
            Application.targetFrameRate = 9999;

            GameObject frameCounter = new GameObject("Frame Counter");

            m_TextMeshPro = frameCounter.AddComponent<TextMeshPro>();
            m_TextMeshPro.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            m_TextMeshPro.fontSharedMaterial = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - Overlay");


            m_frameCounter_transform = frameCounter.transform;
            m_frameCounter_transform.SetParent(m_camera.transform);
            m_frameCounter_transform.localRotation = Quaternion.identity;

            m_TextMeshPro.enableWordWrapping = false;
            m_TextMeshPro.fontSize = 24;
            //m_TextMeshPro.FontColor = new Color32(255, 255, 255, 128);
            //m_TextMeshPro.edgeWidth = .15f;
            //m_TextMeshPro.isOverlay = true;

            //m_TextMeshPro.FaceColor = new Color32(255, 128, 0, 0);
            //m_TextMeshPro.EdgeColor = new Color32(0, 255, 0, 255);
            //m_TextMeshPro.FontMaterial.renderQueue = 4000;

            //m_TextMeshPro.CreateSoftShadowClone(new Vector2(1f, -1f));

            Set_FrameCounter_Position(AnchorPosition);
            last_AnchorPosition = AnchorPosition;


        }

        void Start()
        {
            m_LastInterval = Time.realtimeSinceStartup;
            m_Frames = 0;
        }

        void Update()
        {
            if (AnchorPosition != last_AnchorPosition)
                Set_FrameCounter_Position(AnchorPosition);

            last_AnchorPosition = AnchorPosition;

            m_Frames += 1;
            float timeNow = Time.realtimeSinceStartup;

            if (timeNow > m_LastInterval + UpdateInterval)
            {
                // display two fractional digits (f2 format)
                float fps = m_Frames / (timeNow - m_LastInterval);
                float ms = 1000.0f / Mathf.Max(fps, 0.00001f);

                if (fps < 30)
                    htmlColorTag = "<color=yellow>";
                else if (fps < 10)
                    htmlColorTag = "<color=red>";
                else
                    htmlColorTag = "<color=green>";

                //string format = System.String.Format(htmlColorTag + "{0:F2} </color>FPS \n{1:F2} <#8080ff>MS",fps, ms);
                //m_TextMeshPro.text = format;

                m_TextMeshPro.SetText(htmlColorTag + fpsLabel, fps, ms);

                m_Frames = 0;
                m_LastInterval = timeNow;
            }
        }


        void Set_FrameCounter_Position(FpsCounterAnchorPositions anchor_position)
        {
            //Debug.Log("Changing frame counter anchor position.");
            m_TextMeshPro.margin = new Vector4(1f, 1f, 1f, 1f);

            switch (anchor_position)
            {
                case FpsCounterAnchorPositions.TopLeft:
                    m_TextMeshPro.alignment = TextAlignmentOptions.TopLeft;
                    m_TextMeshPro.rectTransform.pivot = new Vector2(0, 1);
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(0, 1, 100.0f));
                    break;
                case FpsCounterAnchorPositions.BottomLeft:
                    m_TextMeshPro.alignment = TextAlignmentOptions.BottomLeft;
                    m_TextMeshPro.rectTransform.pivot = new Vector2(0, 0);
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(0, 0, 100.0f));
                    break;
                case FpsCounterAnchorPositions.TopRight:
                    m_TextMeshPro.alignment = TextAlignmentOptions.TopRight;
                    m_TextMeshPro.rectTransform.pivot = new Vector2(1, 1);
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(1, 1, 100.0f));
                    break;
                case FpsCounterAnchorPositions.BottomRight:
                    m_TextMeshPro.alignment = TextAlignmentOptions.BottomRight;
                    m_TextMeshPro.rectTransform.pivot = new Vector2(1, 0);
                    m_frameCounter_transform.position = m_camera.ViewportToWorldPoint(new Vector3(1, 0, 100.0f));
                    break;
            }
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_PhoneNumberValidator.cs

```csharp
using UnityEngine;
using System.Collections;
using System;

namespace TMPro
{
    /// <summary>
    /// Example of a Custom Character Input Validator to only allow phone number in the (800) 555-1212 format.
    /// </summary>
    [Serializable]
    //[CreateAssetMenu(fileName = "InputValidator - Phone Numbers.asset", menuName = "TextMeshPro/Input Validators/Phone Numbers")]
    public class TMP_PhoneNumberValidator : TMP_InputValidator
    {
        // Custom text input validation function
        public override char Validate(ref string text, ref int pos, char ch)
        {
            Debug.Log("Trying to validate...");
            
            // Return unless the character is a valid digit
            if (ch < '0' && ch > '9') return (char)0;

            int length = text.Length;

            // Enforce Phone Number format for every character input.
            for (int i = 0; i < length + 1; i++)
            {
                switch (i)
                {
                    case 0:
                        if (i == length)
                            text = "(" + ch;
                        pos = 2;
                        break;
                    case 1:
                        if (i == length)
                            text += ch;
                        pos = 2;
                        break;
                    case 2:
                        if (i == length)
                            text += ch;
                        pos = 3;
                        break;
                    case 3:
                        if (i == length)
                            text += ch + ") ";
                        pos = 6;
                        break;
                    case 4:
                        if (i == length)
                            text += ") " + ch;
                        pos = 7;
                        break;
                    case 5:
                        if (i == length)
                            text += " " + ch;
                        pos = 7;
                        break;
                    case 6:
                        if (i == length)
                            text += ch;
                        pos = 7;
                        break;
                    case 7:
                        if (i == length)
                            text += ch;
                        pos = 8;
                        break;
                    case 8:
                        if (i == length)
                            text += ch + "-";
                        pos = 10;
                        break;
                    case 9:
                        if (i == length)
                            text += "-" + ch;
                        pos = 11;
                        break;
                    case 10:
                        if (i == length)
                            text += ch;
                        pos = 11;
                        break;
                    case 11:
                        if (i == length)
                            text += ch;
                        pos = 12;
                        break;
                    case 12:
                        if (i == length)
                            text += ch;
                        pos = 13;
                        break;
                    case 13:
                        if (i == length)
                            text += ch;
                        pos = 14;
                        break;
                }
            }

            return ch;
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_TextEventCheck.cs

```csharp
using UnityEngine;


namespace TMPro.Examples
{
    public class TMP_TextEventCheck : MonoBehaviour
    {

        public TMP_TextEventHandler TextEventHandler;

        private TMP_Text m_TextComponent;

        void OnEnable()
        {
            if (TextEventHandler != null)
            {
                // Get a reference to the text component
                m_TextComponent = TextEventHandler.GetComponent<TMP_Text>();
                
                TextEventHandler.onCharacterSelection.AddListener(OnCharacterSelection);
                TextEventHandler.onSpriteSelection.AddListener(OnSpriteSelection);
                TextEventHandler.onWordSelection.AddListener(OnWordSelection);
                TextEventHandler.onLineSelection.AddListener(OnLineSelection);
                TextEventHandler.onLinkSelection.AddListener(OnLinkSelection);
            }
        }


        void OnDisable()
        {
            if (TextEventHandler != null)
            {
                TextEventHandler.onCharacterSelection.RemoveListener(OnCharacterSelection);
                TextEventHandler.onSpriteSelection.RemoveListener(OnSpriteSelection);
                TextEventHandler.onWordSelection.RemoveListener(OnWordSelection);
                TextEventHandler.onLineSelection.RemoveListener(OnLineSelection);
                TextEventHandler.onLinkSelection.RemoveListener(OnLinkSelection);
            }
        }


        void OnCharacterSelection(char c, int index)
        {
            Debug.Log("Character [" + c + "] at Index: " + index + " has been selected.");
        }

        void OnSpriteSelection(char c, int index)
        {
            Debug.Log("Sprite [" + c + "] at Index: " + index + " has been selected.");
        }

        void OnWordSelection(string word, int firstCharacterIndex, int length)
        {
            Debug.Log("Word [" + word + "] with first character index of " + firstCharacterIndex + " and length of " + length + " has been selected.");
        }

        void OnLineSelection(string lineText, int firstCharacterIndex, int length)
        {
            Debug.Log("Line [" + lineText + "] with first character index of " + firstCharacterIndex + " and length of " + length + " has been selected.");
        }

        void OnLinkSelection(string linkID, string linkText, int linkIndex)
        {
            if (m_TextComponent != null)
            {
                TMP_LinkInfo linkInfo = m_TextComponent.textInfo.linkInfo[linkIndex];
            }
            
            Debug.Log("Link Index: " + linkIndex + " with ID [" + linkID + "] and Text \"" + linkText + "\" has been selected.");
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_TextEventHandler.cs

```csharp
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using System;


namespace TMPro
{

    public class TMP_TextEventHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Serializable]
        public class CharacterSelectionEvent : UnityEvent<char, int> { }

        [Serializable]
        public class SpriteSelectionEvent : UnityEvent<char, int> { }

        [Serializable]
        public class WordSelectionEvent : UnityEvent<string, int, int> { }

        [Serializable]
        public class LineSelectionEvent : UnityEvent<string, int, int> { }

        [Serializable]
        public class LinkSelectionEvent : UnityEvent<string, string, int> { }


        /// <summary>
        /// Event delegate triggered when pointer is over a character.
        /// </summary>
        public CharacterSelectionEvent onCharacterSelection
        {
            get { return m_OnCharacterSelection; }
            set { m_OnCharacterSelection = value; }
        }
        [SerializeField]
        private CharacterSelectionEvent m_OnCharacterSelection = new CharacterSelectionEvent();


        /// <summary>
        /// Event delegate triggered when pointer is over a sprite.
        /// </summary>
        public SpriteSelectionEvent onSpriteSelection
        {
            get { return m_OnSpriteSelection; }
            set { m_OnSpriteSelection = value; }
        }
        [SerializeField]
        private SpriteSelectionEvent m_OnSpriteSelection = new SpriteSelectionEvent();


        /// <summary>
        /// Event delegate triggered when pointer is over a word.
        /// </summary>
        public WordSelectionEvent onWordSelection
        {
            get { return m_OnWordSelection; }
            set { m_OnWordSelection = value; }
        }
        [SerializeField]
        private WordSelectionEvent m_OnWordSelection = new WordSelectionEvent();


        /// <summary>
        /// Event delegate triggered when pointer is over a line.
        /// </summary>
        public LineSelectionEvent onLineSelection
        {
            get { return m_OnLineSelection; }
            set { m_OnLineSelection = value; }
        }
        [SerializeField]
        private LineSelectionEvent m_OnLineSelection = new LineSelectionEvent();


        /// <summary>
        /// Event delegate triggered when pointer is over a link.
        /// </summary>
        public LinkSelectionEvent onLinkSelection
        {
            get { return m_OnLinkSelection; }
            set { m_OnLinkSelection = value; }
        }
        [SerializeField]
        private LinkSelectionEvent m_OnLinkSelection = new LinkSelectionEvent();



        private TMP_Text m_TextComponent;

        private Camera m_Camera;
        private Canvas m_Canvas;

        private int m_selectedLink = -1;
        private int m_lastCharIndex = -1;
        private int m_lastWordIndex = -1;
        private int m_lastLineIndex = -1;

        void Awake()
        {
            // Get a reference to the text component.
            m_TextComponent = gameObject.GetComponent<TMP_Text>();

            // Get a reference to the camera rendering the text taking into consideration the text component type.
            if (m_TextComponent.GetType() == typeof(TextMeshProUGUI))
            {
                m_Canvas = gameObject.GetComponentInParent<Canvas>();
                if (m_Canvas != null)
                {
                    if (m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                        m_Camera = null;
                    else
                        m_Camera = m_Canvas.worldCamera;
                }
            }
            else
            {
                m_Camera = Camera.main;
            }
        }


        void LateUpdate()
        {
            if (TMP_TextUtilities.IsIntersectingRectTransform(m_TextComponent.rectTransform, Input.mousePosition, m_Camera))
            {
                #region Example of Character or Sprite Selection
                int charIndex = TMP_TextUtilities.FindIntersectingCharacter(m_TextComponent, Input.mousePosition, m_Camera, true);
                if (charIndex != -1 && charIndex != m_lastCharIndex)
                {
                    m_lastCharIndex = charIndex;

                    TMP_TextElementType elementType = m_TextComponent.textInfo.characterInfo[charIndex].elementType;

                    // Send event to any event listeners depending on whether it is a character or sprite.
                    if (elementType == TMP_TextElementType.Character)
                        SendOnCharacterSelection(m_TextComponent.textInfo.characterInfo[charIndex].character, charIndex);
                    else if (elementType == TMP_TextElementType.Sprite)
                        SendOnSpriteSelection(m_TextComponent.textInfo.characterInfo[charIndex].character, charIndex);
                }
                #endregion


                #region Example of Word Selection
                // Check if Mouse intersects any words and if so assign a random color to that word.
                int wordIndex = TMP_TextUtilities.FindIntersectingWord(m_TextComponent, Input.mousePosition, m_Camera);
                if (wordIndex != -1 && wordIndex != m_lastWordIndex)
                {
                    m_lastWordIndex = wordIndex;

                    // Get the information about the selected word.
                    TMP_WordInfo wInfo = m_TextComponent.textInfo.wordInfo[wordIndex];

                    // Send the event to any listeners.
                    SendOnWordSelection(wInfo.GetWord(), wInfo.firstCharacterIndex, wInfo.characterCount);
                }
                #endregion


                #region Example of Line Selection
                // Check if Mouse intersects any words and if so assign a random color to that word.
                int lineIndex = TMP_TextUtilities.FindIntersectingLine(m_TextComponent, Input.mousePosition, m_Camera);
                if (lineIndex != -1 && lineIndex != m_lastLineIndex)
                {
                    m_lastLineIndex = lineIndex;

                    // Get the information about the selected word.
                    TMP_LineInfo lineInfo = m_TextComponent.textInfo.lineInfo[lineIndex];

                    // Send the event to any listeners.
                    char[] buffer = new char[lineInfo.characterCount];
                    for (int i = 0; i < lineInfo.characterCount && i < m_TextComponent.textInfo.characterInfo.Length; i++)
                    {
                        buffer[i] = m_TextComponent.textInfo.characterInfo[i + lineInfo.firstCharacterIndex].character;
                    }

                    string lineText = new string(buffer);
                    SendOnLineSelection(lineText, lineInfo.firstCharacterIndex, lineInfo.characterCount);
                }
                #endregion


                #region Example of Link Handling
                // Check if mouse intersects with any links.
                int linkIndex = TMP_TextUtilities.FindIntersectingLink(m_TextComponent, Input.mousePosition, m_Camera);

                // Handle new Link selection.
                if (linkIndex != -1 && linkIndex != m_selectedLink)
                {
                    m_selectedLink = linkIndex;

                    // Get information about the link.
                    TMP_LinkInfo linkInfo = m_TextComponent.textInfo.linkInfo[linkIndex];

                    // Send the event to any listeners.
                    SendOnLinkSelection(linkInfo.GetLinkID(), linkInfo.GetLinkText(), linkIndex);
                }
                #endregion
            }
            else
            {
                // Reset all selections given we are hovering outside the text container bounds.
                m_selectedLink = -1;
                m_lastCharIndex = -1;
                m_lastWordIndex = -1;
                m_lastLineIndex = -1;
            }
        }


        public void OnPointerEnter(PointerEventData eventData)
        {
            //Debug.Log("OnPointerEnter()");
        }


        public void OnPointerExit(PointerEventData eventData)
        {
            //Debug.Log("OnPointerExit()");
        }


        private void SendOnCharacterSelection(char character, int characterIndex)
        {
            if (onCharacterSelection != null)
                onCharacterSelection.Invoke(character, characterIndex);
        }

        private void SendOnSpriteSelection(char character, int characterIndex)
        {
            if (onSpriteSelection != null)
                onSpriteSelection.Invoke(character, characterIndex);
        }

        private void SendOnWordSelection(string word, int charIndex, int length)
        {
            if (onWordSelection != null)
                onWordSelection.Invoke(word, charIndex, length);
        }

        private void SendOnLineSelection(string line, int charIndex, int length)
        {
            if (onLineSelection != null)
                onLineSelection.Invoke(line, charIndex, length);
        }

        private void SendOnLinkSelection(string linkID, string linkText, int linkIndex)
        {
            if (onLinkSelection != null)
                onLinkSelection.Invoke(linkID, linkText, linkIndex);
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_TextInfoDebugTool.cs

```csharp
using System;
using UnityEngine;
using System.Collections;
using UnityEditor;


namespace TMPro.Examples
{

    public class TMP_TextInfoDebugTool : MonoBehaviour
    {
        // Since this script is used for debugging, we exclude it from builds.
        // TODO: Rework this script to make it into an editor utility.
        #if UNITY_EDITOR
        public bool ShowCharacters;
        public bool ShowWords;
        public bool ShowLinks;
        public bool ShowLines;
        public bool ShowMeshBounds;
        public bool ShowTextBounds;
        [Space(10)]
        [TextArea(2, 2)]
        public string ObjectStats;

        [SerializeField]
        private TMP_Text m_TextComponent;

        private Transform m_Transform;
        private TMP_TextInfo m_TextInfo;

        private float m_ScaleMultiplier;
        private float m_HandleSize;


        void OnDrawGizmos()
        {
            if (m_TextComponent == null)
            {
                m_TextComponent = GetComponent<TMP_Text>();

                if (m_TextComponent == null)
                    return;
            }

            m_Transform = m_TextComponent.transform;

            // Get a reference to the text object's textInfo
            m_TextInfo = m_TextComponent.textInfo;

            // Update Text Statistics
            ObjectStats = "Characters: " + m_TextInfo.characterCount + "   Words: " + m_TextInfo.wordCount + "   Spaces: " + m_TextInfo.spaceCount + "   Sprites: " + m_TextInfo.spriteCount + "   Links: " + m_TextInfo.linkCount
                          + "\nLines: " + m_TextInfo.lineCount + "   Pages: " + m_TextInfo.pageCount;

            // Get the handle size for drawing the various
            m_ScaleMultiplier = m_TextComponent.GetType() == typeof(TextMeshPro) ? 1 : 0.1f;
            m_HandleSize = HandleUtility.GetHandleSize(m_Transform.position) * m_ScaleMultiplier;

            // Draw line metrics
            #region Draw Lines
            if (ShowLines)
                DrawLineBounds();
            #endregion

            // Draw word metrics
            #region Draw Words
            if (ShowWords)
                DrawWordBounds();
            #endregion

            // Draw character metrics
            #region Draw Characters
            if (ShowCharacters)
                DrawCharactersBounds();
            #endregion

            // Draw Quads around each of the words
            #region Draw Links
            if (ShowLinks)
                DrawLinkBounds();
            #endregion

            // Draw Quad around the bounds of the text
            #region Draw Bounds
            if (ShowMeshBounds)
                DrawBounds();
            #endregion

            // Draw Quad around the rendered region of the text.
            #region Draw Text Bounds
            if (ShowTextBounds)
                DrawTextBounds();
            #endregion
        }


        /// <summary>
        /// Method to draw a rectangle around each character.
        /// </summary>
        /// <param name="text"></param>
        void DrawCharactersBounds()
        {
            int characterCount = m_TextInfo.characterCount;

            for (int i = 0; i < characterCount; i++)
            {
                // Draw visible as well as invisible characters
                TMP_CharacterInfo characterInfo = m_TextInfo.characterInfo[i];

                bool isCharacterVisible = i < m_TextComponent.maxVisibleCharacters &&
                                          characterInfo.lineNumber < m_TextComponent.maxVisibleLines &&
                                          i >= m_TextComponent.firstVisibleCharacter;

                if (m_TextComponent.overflowMode == TextOverflowModes.Page)
                    isCharacterVisible = isCharacterVisible && characterInfo.pageNumber + 1 == m_TextComponent.pageToDisplay;

                if (!isCharacterVisible)
                    continue;

                float dottedLineSize = 6;

                // Get Bottom Left and Top Right position of the current character
                Vector3 bottomLeft = m_Transform.TransformPoint(characterInfo.bottomLeft);
                Vector3 topLeft = m_Transform.TransformPoint(new Vector3(characterInfo.topLeft.x, characterInfo.topLeft.y, 0));
                Vector3 topRight = m_Transform.TransformPoint(characterInfo.topRight);
                Vector3 bottomRight = m_Transform.TransformPoint(new Vector3(characterInfo.bottomRight.x, characterInfo.bottomRight.y, 0));

                // Draw character bounds
                if (characterInfo.isVisible)
                {
                    Color color = Color.green;
                    DrawDottedRectangle(bottomLeft, topRight, color);
                }
                else
                {
                    Color color = Color.grey;

                    float whiteSpaceAdvance = Math.Abs(characterInfo.origin - characterInfo.xAdvance) > 0.01f ? characterInfo.xAdvance : characterInfo.origin + (characterInfo.ascender - characterInfo.descender) * 0.03f;
                    DrawDottedRectangle(m_Transform.TransformPoint(new Vector3(characterInfo.origin, characterInfo.descender, 0)), m_Transform.TransformPoint(new Vector3(whiteSpaceAdvance, characterInfo.ascender, 0)), color, 4);
                }

                float origin = characterInfo.origin;
                float advance = characterInfo.xAdvance;
                float ascentline = characterInfo.ascender;
                float baseline = characterInfo.baseLine;
                float descentline = characterInfo.descender;

                //Draw Ascent line
                Vector3 ascentlineStart = m_Transform.TransformPoint(new Vector3(origin, ascentline, 0));
                Vector3 ascentlineEnd = m_Transform.TransformPoint(new Vector3(advance, ascentline, 0));

                Handles.color = Color.cyan;
                Handles.DrawDottedLine(ascentlineStart, ascentlineEnd, dottedLineSize);

                // Draw Cap Height & Mean line
                float capline = characterInfo.fontAsset == null ? 0 : baseline + characterInfo.fontAsset.faceInfo.capLine * characterInfo.scale;
                Vector3 capHeightStart = new Vector3(topLeft.x, m_Transform.TransformPoint(new Vector3(0, capline, 0)).y, 0);
                Vector3 capHeightEnd = new Vector3(topRight.x, m_Transform.TransformPoint(new Vector3(0, capline, 0)).y, 0);

                float meanline = characterInfo.fontAsset == null ? 0 : baseline + characterInfo.fontAsset.faceInfo.meanLine * characterInfo.scale;
                Vector3 meanlineStart = new Vector3(topLeft.x, m_Transform.TransformPoint(new Vector3(0, meanline, 0)).y, 0);
                Vector3 meanlineEnd = new Vector3(topRight.x, m_Transform.TransformPoint(new Vector3(0, meanline, 0)).y, 0);

                if (characterInfo.isVisible)
                {
                    // Cap line
                    Handles.color = Color.cyan;
                    Handles.DrawDottedLine(capHeightStart, capHeightEnd, dottedLineSize);

                    // Mean line
                    Handles.color = Color.cyan;
                    Handles.DrawDottedLine(meanlineStart, meanlineEnd, dottedLineSize);
                }

                //Draw Base line
                Vector3 baselineStart = m_Transform.TransformPoint(new Vector3(origin, baseline, 0));
                Vector3 baselineEnd = m_Transform.TransformPoint(new Vector3(advance, baseline, 0));

                Handles.color = Color.cyan;
                Handles.DrawDottedLine(baselineStart, baselineEnd, dottedLineSize);

                //Draw Descent line
                Vector3 descentlineStart = m_Transform.TransformPoint(new Vector3(origin, descentline, 0));
                Vector3 descentlineEnd = m_Transform.TransformPoint(new Vector3(advance, descentline, 0));

                Handles.color = Color.cyan;
                Handles.DrawDottedLine(descentlineStart, descentlineEnd, dottedLineSize);

                // Draw Origin
                Vector3 originPosition = m_Transform.TransformPoint(new Vector3(origin, baseline, 0));
                DrawCrosshair(originPosition, 0.05f / m_ScaleMultiplier, Color.cyan);

                // Draw Horizontal Advance
                Vector3 advancePosition = m_Transform.TransformPoint(new Vector3(advance, baseline, 0));
                DrawSquare(advancePosition, 0.025f / m_ScaleMultiplier, Color.yellow);
                DrawCrosshair(advancePosition, 0.0125f / m_ScaleMultiplier, Color.yellow);

                // Draw text labels for metrics
               if (m_HandleSize < 0.5f)
               {
                   GUIStyle style = new GUIStyle(GUI.skin.GetStyle("Label"));
                   style.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1.0f);
                   style.fontSize = 12;
                   style.fixedWidth = 200;
                   style.fixedHeight = 20;

                   Vector3 labelPosition;
                   float center = (origin + advance) / 2;

                   //float baselineMetrics = 0;
                   //float ascentlineMetrics = ascentline - baseline;
                   //float caplineMetrics = capline - baseline;
                   //float meanlineMetrics = meanline - baseline;
                   //float descentlineMetrics = descentline - baseline;

                   // Ascent Line
                   labelPosition = m_Transform.TransformPoint(new Vector3(center, ascentline, 0));
                   style.alignment = TextAnchor.UpperCenter;
                   Handles.Label(labelPosition, "Ascent Line", style);
                   //Handles.Label(labelPosition, "Ascent Line (" + ascentlineMetrics.ToString("f3") + ")" , style);

                   // Base Line
                   labelPosition = m_Transform.TransformPoint(new Vector3(center, baseline, 0));
                   Handles.Label(labelPosition, "Base Line", style);
                   //Handles.Label(labelPosition, "Base Line (" + baselineMetrics.ToString("f3") + ")" , style);

                   // Descent line
                   labelPosition = m_Transform.TransformPoint(new Vector3(center, descentline, 0));
                   Handles.Label(labelPosition, "Descent Line", style);
                   //Handles.Label(labelPosition, "Descent Line (" + descentlineMetrics.ToString("f3") + ")" , style);

                   if (characterInfo.isVisible)
                   {
                       // Cap Line
                       labelPosition = m_Transform.TransformPoint(new Vector3(center, capline, 0));
                       style.alignment = TextAnchor.UpperCenter;
                       Handles.Label(labelPosition, "Cap Line", style);
                       //Handles.Label(labelPosition, "Cap Line (" + caplineMetrics.ToString("f3") + ")" , style);

                       // Mean Line
                       labelPosition = m_Transform.TransformPoint(new Vector3(center, meanline, 0));
                       style.alignment = TextAnchor.UpperCenter;
                       Handles.Label(labelPosition, "Mean Line", style);
                       //Handles.Label(labelPosition, "Mean Line (" + ascentlineMetrics.ToString("f3") + ")" , style);

                       // Origin
                       labelPosition = m_Transform.TransformPoint(new Vector3(origin, baseline, 0));
                       style.alignment = TextAnchor.UpperRight;
                       Handles.Label(labelPosition, "Origin ", style);

                       // Advance
                       labelPosition = m_Transform.TransformPoint(new Vector3(advance, baseline, 0));
                       style.alignment = TextAnchor.UpperLeft;
                       Handles.Label(labelPosition, "  Advance", style);
                   }
               }
            }
        }


        /// <summary>
        /// Method to draw rectangles around each word of the text.
        /// </summary>
        /// <param name="text"></param>
        void DrawWordBounds()
        {
            for (int i = 0; i < m_TextInfo.wordCount; i++)
            {
                TMP_WordInfo wInfo = m_TextInfo.wordInfo[i];

                bool isBeginRegion = false;

                Vector3 bottomLeft = Vector3.zero;
                Vector3 topLeft = Vector3.zero;
                Vector3 bottomRight = Vector3.zero;
                Vector3 topRight = Vector3.zero;

                float maxAscender = -Mathf.Infinity;
                float minDescender = Mathf.Infinity;

                Color wordColor = Color.green;

                // Iterate through each character of the word
                for (int j = 0; j < wInfo.characterCount; j++)
                {
                    int characterIndex = wInfo.firstCharacterIndex + j;
                    TMP_CharacterInfo currentCharInfo = m_TextInfo.characterInfo[characterIndex];
                    int currentLine = currentCharInfo.lineNumber;

                    bool isCharacterVisible = characterIndex > m_TextComponent.maxVisibleCharacters ||
                                              currentCharInfo.lineNumber > m_TextComponent.maxVisibleLines ||
                                             (m_TextComponent.overflowMode == TextOverflowModes.Page && currentCharInfo.pageNumber + 1 != m_TextComponent.pageToDisplay) ? false : true;

                    // Track Max Ascender and Min Descender
                    maxAscender = Mathf.Max(maxAscender, currentCharInfo.ascender);
                    minDescender = Mathf.Min(minDescender, currentCharInfo.descender);

                    if (isBeginRegion == false && isCharacterVisible)
                    {
                        isBeginRegion = true;

                        bottomLeft = new Vector3(currentCharInfo.bottomLeft.x, currentCharInfo.descender, 0);
                        topLeft = new Vector3(currentCharInfo.bottomLeft.x, currentCharInfo.ascender, 0);

                        //Debug.Log("Start Word Region at [" + currentCharInfo.character + "]");

                        // If Word is one character
                        if (wInfo.characterCount == 1)
                        {
                            isBeginRegion = false;

                            topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                            bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                            bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                            topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                            // Draw Region
                            DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, wordColor);

                            //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                        }
                    }

                    // Last Character of Word
                    if (isBeginRegion && j == wInfo.characterCount - 1)
                    {
                        isBeginRegion = false;

                        topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                        bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                        bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                        topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                        // Draw Region
                        DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, wordColor);

                        //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                    }
                    // If Word is split on more than one line.
                    else if (isBeginRegion && currentLine != m_TextInfo.characterInfo[characterIndex + 1].lineNumber)
                    {
                        isBeginRegion = false;

                        topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                        bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                        bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                        topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                        // Draw Region
                        DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, wordColor);
                        //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                        maxAscender = -Mathf.Infinity;
                        minDescender = Mathf.Infinity;

                    }
                }

                //Debug.Log(wInfo.GetWord(m_TextMeshPro.textInfo.characterInfo));
            }


        }


        /// <summary>
        /// Draw rectangle around each of the links contained in the text.
        /// </summary>
        /// <param name="text"></param>
        void DrawLinkBounds()
        {
            TMP_TextInfo textInfo = m_TextComponent.textInfo;

            for (int i = 0; i < textInfo.linkCount; i++)
            {
                TMP_LinkInfo linkInfo = textInfo.linkInfo[i];

                bool isBeginRegion = false;

                Vector3 bottomLeft = Vector3.zero;
                Vector3 topLeft = Vector3.zero;
                Vector3 bottomRight = Vector3.zero;
                Vector3 topRight = Vector3.zero;

                float maxAscender = -Mathf.Infinity;
                float minDescender = Mathf.Infinity;

                Color32 linkColor = Color.cyan;

                // Iterate through each character of the link text
                for (int j = 0; j < linkInfo.linkTextLength; j++)
                {
                    int characterIndex = linkInfo.linkTextfirstCharacterIndex + j;
                    TMP_CharacterInfo currentCharInfo = textInfo.characterInfo[characterIndex];
                    int currentLine = currentCharInfo.lineNumber;

                    bool isCharacterVisible = characterIndex > m_TextComponent.maxVisibleCharacters ||
                                              currentCharInfo.lineNumber > m_TextComponent.maxVisibleLines ||
                                             (m_TextComponent.overflowMode == TextOverflowModes.Page && currentCharInfo.pageNumber + 1 != m_TextComponent.pageToDisplay) ? false : true;

                    // Track Max Ascender and Min Descender
                    maxAscender = Mathf.Max(maxAscender, currentCharInfo.ascender);
                    minDescender = Mathf.Min(minDescender, currentCharInfo.descender);

                    if (isBeginRegion == false && isCharacterVisible)
                    {
                        isBeginRegion = true;

                        bottomLeft = new Vector3(currentCharInfo.bottomLeft.x, currentCharInfo.descender, 0);
                        topLeft = new Vector3(currentCharInfo.bottomLeft.x, currentCharInfo.ascender, 0);

                        //Debug.Log("Start Word Region at [" + currentCharInfo.character + "]");

                        // If Link is one character
                        if (linkInfo.linkTextLength == 1)
                        {
                            isBeginRegion = false;

                            topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                            bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                            bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                            topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                            // Draw Region
                            DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, linkColor);

                            //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                        }
                    }

                    // Last Character of Link
                    if (isBeginRegion && j == linkInfo.linkTextLength - 1)
                    {
                        isBeginRegion = false;

                        topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                        bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                        bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                        topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                        // Draw Region
                        DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, linkColor);

                        //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                    }
                    // If Link is split on more than one line.
                    else if (isBeginRegion && currentLine != textInfo.characterInfo[characterIndex + 1].lineNumber)
                    {
                        isBeginRegion = false;

                        topLeft = m_Transform.TransformPoint(new Vector3(topLeft.x, maxAscender, 0));
                        bottomLeft = m_Transform.TransformPoint(new Vector3(bottomLeft.x, minDescender, 0));
                        bottomRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, minDescender, 0));
                        topRight = m_Transform.TransformPoint(new Vector3(currentCharInfo.topRight.x, maxAscender, 0));

                        // Draw Region
                        DrawRectangle(bottomLeft, topLeft, topRight, bottomRight, linkColor);

                        maxAscender = -Mathf.Infinity;
                        minDescender = Mathf.Infinity;
                        //Debug.Log("End Word Region at [" + currentCharInfo.character + "]");
                    }
                }

                //Debug.Log(wInfo.GetWord(m_TextMeshPro.textInfo.characterInfo));
            }
        }


        /// <summary>
        /// Draw Rectangles around each lines of the text.
        /// </summary>
        /// <param name="text"></param>
        void DrawLineBounds()
        {
            int lineCount = m_TextInfo.lineCount;

            for (int i = 0; i < lineCount; i++)
            {
                TMP_LineInfo lineInfo = m_TextInfo.lineInfo[i];
                TMP_CharacterInfo firstCharacterInfo = m_TextInfo.characterInfo[lineInfo.firstCharacterIndex];
                TMP_CharacterInfo lastCharacterInfo = m_TextInfo.characterInfo[lineInfo.lastCharacterIndex];

                bool isLineVisible = (lineInfo.characterCount == 1 && (firstCharacterInfo.character == 10 || firstCharacterInfo.character == 11 || firstCharacterInfo.character == 0x2028 || firstCharacterInfo.character == 0x2029)) ||
                                      i > m_TextComponent.maxVisibleLines ||
                                     (m_TextComponent.overflowMode == TextOverflowModes.Page && firstCharacterInfo.pageNumber + 1 != m_TextComponent.pageToDisplay) ? false : true;

                if (!isLineVisible) continue;

                float lineBottomLeft = firstCharacterInfo.bottomLeft.x;
                float lineTopRight = lastCharacterInfo.topRight.x;

                float ascentline = lineInfo.ascender;
                float baseline = lineInfo.baseline;
                float descentline = lineInfo.descender;

                float dottedLineSize = 12;

                // Draw line extents
                DrawDottedRectangle(m_Transform.TransformPoint(lineInfo.lineExtents.min), m_Transform.TransformPoint(lineInfo.lineExtents.max), Color.green, 4);

                // Draw Ascent line
                Vector3 ascentlineStart = m_Transform.TransformPoint(new Vector3(lineBottomLeft, ascentline, 0));
                Vector3 ascentlineEnd = m_Transform.TransformPoint(new Vector3(lineTopRight, ascentline, 0));

                Handles.color = Color.yellow;
                Handles.DrawDottedLine(ascentlineStart, ascentlineEnd, dottedLineSize);

                // Draw Base line
                Vector3 baseLineStart = m_Transform.TransformPoint(new Vector3(lineBottomLeft, baseline, 0));
                Vector3 baseLineEnd = m_Transform.TransformPoint(new Vector3(lineTopRight, baseline, 0));

                Handles.color = Color.yellow;
                Handles.DrawDottedLine(baseLineStart, baseLineEnd, dottedLineSize);

                // Draw Descent line
                Vector3 descentLineStart = m_Transform.TransformPoint(new Vector3(lineBottomLeft, descentline, 0));
                Vector3 descentLineEnd = m_Transform.TransformPoint(new Vector3(lineTopRight, descentline, 0));

                Handles.color = Color.yellow;
                Handles.DrawDottedLine(descentLineStart, descentLineEnd, dottedLineSize);

                // Draw text labels for metrics
                if (m_HandleSize < 1.0f)
                {
                    GUIStyle style = new GUIStyle();
                    style.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1.0f);
                    style.fontSize = 12;
                    style.fixedWidth = 200;
                    style.fixedHeight = 20;
                    Vector3 labelPosition;

                    // Ascent Line
                    labelPosition = m_Transform.TransformPoint(new Vector3(lineBottomLeft, ascentline, 0));
                    style.padding = new RectOffset(0, 10, 0, 5);
                    style.alignment = TextAnchor.MiddleRight;
                    Handles.Label(labelPosition, "Ascent Line", style);

                    // Base Line
                    labelPosition = m_Transform.TransformPoint(new Vector3(lineBottomLeft, baseline, 0));
                    Handles.Label(labelPosition, "Base Line", style);

                    // Descent line
                    labelPosition = m_Transform.TransformPoint(new Vector3(lineBottomLeft, descentline, 0));
                    Handles.Label(labelPosition, "Descent Line", style);
                }
            }
        }


        /// <summary>
        /// Draw Rectangle around the bounds of the text object.
        /// </summary>
        void DrawBounds()
        {
            Bounds meshBounds = m_TextComponent.bounds;

            // Get Bottom Left and Top Right position of each word
            Vector3 bottomLeft = m_TextComponent.transform.position + meshBounds.min;
            Vector3 topRight = m_TextComponent.transform.position + meshBounds.max;

            DrawRectangle(bottomLeft, topRight, new Color(1, 0.5f, 0));
        }


        void DrawTextBounds()
        {
            Bounds textBounds = m_TextComponent.textBounds;

            Vector3 bottomLeft = m_TextComponent.transform.position + (textBounds.center - textBounds.extents);
            Vector3 topRight = m_TextComponent.transform.position + (textBounds.center + textBounds.extents);

            DrawRectangle(bottomLeft, topRight, new Color(0f, 0.5f, 0.5f));
        }


        // Draw Rectangles
        void DrawRectangle(Vector3 BL, Vector3 TR, Color color)
        {
            Gizmos.color = color;

            Gizmos.DrawLine(new Vector3(BL.x, BL.y, 0), new Vector3(BL.x, TR.y, 0));
            Gizmos.DrawLine(new Vector3(BL.x, TR.y, 0), new Vector3(TR.x, TR.y, 0));
            Gizmos.DrawLine(new Vector3(TR.x, TR.y, 0), new Vector3(TR.x, BL.y, 0));
            Gizmos.DrawLine(new Vector3(TR.x, BL.y, 0), new Vector3(BL.x, BL.y, 0));
        }

        void DrawDottedRectangle(Vector3 bottomLeft, Vector3 topRight, Color color, float size = 5.0f)
        {
            Handles.color = color;
            Handles.DrawDottedLine(bottomLeft, new Vector3(bottomLeft.x, topRight.y, bottomLeft.z), size);
            Handles.DrawDottedLine(new Vector3(bottomLeft.x, topRight.y, bottomLeft.z), topRight, size);
            Handles.DrawDottedLine(topRight, new Vector3(topRight.x, bottomLeft.y, bottomLeft.z), size);
            Handles.DrawDottedLine(new Vector3(topRight.x, bottomLeft.y, bottomLeft.z), bottomLeft, size);
        }

        void DrawSolidRectangle(Vector3 bottomLeft, Vector3 topRight, Color color, float size = 5.0f)
        {
            Handles.color = color;
            Rect rect = new Rect(bottomLeft, topRight - bottomLeft);
            Handles.DrawSolidRectangleWithOutline(rect, color, Color.black);
        }

        void DrawSquare(Vector3 position, float size, Color color)
        {
            Handles.color = color;
            Vector3 bottomLeft = new Vector3(position.x - size, position.y - size, position.z);
            Vector3 topLeft = new Vector3(position.x - size, position.y + size, position.z);
            Vector3 topRight = new Vector3(position.x + size, position.y + size, position.z);
            Vector3 bottomRight = new Vector3(position.x + size, position.y - size, position.z);

            Handles.DrawLine(bottomLeft, topLeft);
            Handles.DrawLine(topLeft, topRight);
            Handles.DrawLine(topRight, bottomRight);
            Handles.DrawLine(bottomRight, bottomLeft);
        }

        void DrawCrosshair(Vector3 position, float size, Color color)
        {
            Handles.color = color;

            Handles.DrawLine(new Vector3(position.x - size, position.y, position.z), new Vector3(position.x + size, position.y, position.z));
            Handles.DrawLine(new Vector3(position.x, position.y - size, position.z), new Vector3(position.x, position.y + size, position.z));
        }


        // Draw Rectangles
        void DrawRectangle(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Color color)
        {
            Gizmos.color = color;

            Gizmos.DrawLine(bl, tl);
            Gizmos.DrawLine(tl, tr);
            Gizmos.DrawLine(tr, br);
            Gizmos.DrawLine(br, bl);
        }


        // Draw Rectangles
        void DrawDottedRectangle(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Color color)
        {
            var cam = Camera.current;
            float dotSpacing = (cam.WorldToScreenPoint(br).x - cam.WorldToScreenPoint(bl).x) / 75f;
            UnityEditor.Handles.color = color;

            UnityEditor.Handles.DrawDottedLine(bl, tl, dotSpacing);
            UnityEditor.Handles.DrawDottedLine(tl, tr, dotSpacing);
            UnityEditor.Handles.DrawDottedLine(tr, br, dotSpacing);
            UnityEditor.Handles.DrawDottedLine(br, bl, dotSpacing);
        }
        #endif
    }
}


```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_TextSelector_A.cs

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;


namespace TMPro.Examples
{

    public class TMP_TextSelector_A : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private TextMeshPro m_TextMeshPro;

        private Camera m_Camera;

        private bool m_isHoveringObject;
        private int m_selectedLink = -1;
        private int m_lastCharIndex = -1;
        private int m_lastWordIndex = -1;

        void Awake()
        {
            m_TextMeshPro = gameObject.GetComponent<TextMeshPro>();
            m_Camera = Camera.main;

            // Force generation of the text object so we have valid data to work with. This is needed since LateUpdate() will be called before the text object has a chance to generated when entering play mode.
            m_TextMeshPro.ForceMeshUpdate();
        }


        void LateUpdate()
        {
            m_isHoveringObject = false;

            if (TMP_TextUtilities.IsIntersectingRectTransform(m_TextMeshPro.rectTransform, Input.mousePosition, Camera.main))
            {
                m_isHoveringObject = true;
            }

            if (m_isHoveringObject)
            {
                #region Example of Character Selection
                int charIndex = TMP_TextUtilities.FindIntersectingCharacter(m_TextMeshPro, Input.mousePosition, Camera.main, true);
                if (charIndex != -1 && charIndex != m_lastCharIndex && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                {
                    //Debug.Log("[" + m_TextMeshPro.textInfo.characterInfo[charIndex].character + "] has been selected.");

                    m_lastCharIndex = charIndex;

                    int meshIndex = m_TextMeshPro.textInfo.characterInfo[charIndex].materialReferenceIndex;

                    int vertexIndex = m_TextMeshPro.textInfo.characterInfo[charIndex].vertexIndex;

                    Color32 c = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);

                    Color32[] vertexColors = m_TextMeshPro.textInfo.meshInfo[meshIndex].colors32;

                    vertexColors[vertexIndex + 0] = c;
                    vertexColors[vertexIndex + 1] = c;
                    vertexColors[vertexIndex + 2] = c;
                    vertexColors[vertexIndex + 3] = c;

                    //m_TextMeshPro.mesh.colors32 = vertexColors;
                    m_TextMeshPro.textInfo.meshInfo[meshIndex].mesh.colors32 = vertexColors;
                }
                #endregion

                #region Example of Link Handling
                // Check if mouse intersects with any links.
                int linkIndex = TMP_TextUtilities.FindIntersectingLink(m_TextMeshPro, Input.mousePosition, m_Camera);

                // Clear previous link selection if one existed.
                if ((linkIndex == -1 && m_selectedLink != -1) || linkIndex != m_selectedLink)
                {
                    //m_TextPopup_RectTransform.gameObject.SetActive(false);
                    m_selectedLink = -1;
                }

                // Handle new Link selection.
                if (linkIndex != -1 && linkIndex != m_selectedLink)
                {
                    m_selectedLink = linkIndex;

                    TMP_LinkInfo linkInfo = m_TextMeshPro.textInfo.linkInfo[linkIndex];

                    // The following provides an example of how to access the link properties.
                    //Debug.Log("Link ID: \"" + linkInfo.GetLinkID() + "\"   Link Text: \"" + linkInfo.GetLinkText() + "\""); // Example of how to retrieve the Link ID and Link Text.

                    Vector3 worldPointInRectangle;

                    RectTransformUtility.ScreenPointToWorldPointInRectangle(m_TextMeshPro.rectTransform, Input.mousePosition, m_Camera, out worldPointInRectangle);

                    switch (linkInfo.GetLinkID())
                    {
                        case "id_01": // 100041637: // id_01
                                      //m_TextPopup_RectTransform.position = worldPointInRectangle;
                                      //m_TextPopup_RectTransform.gameObject.SetActive(true);
                                      //m_TextPopup_TMPComponent.text = k_LinkText + " ID 01";
                            break;
                        case "id_02": // 100041638: // id_02
                                      //m_TextPopup_RectTransform.position = worldPointInRectangle;
                                      //m_TextPopup_RectTransform.gameObject.SetActive(true);
                                      //m_TextPopup_TMPComponent.text = k_LinkText + " ID 02";
                            break;
                    }
                }
                #endregion


                #region Example of Word Selection
                // Check if Mouse intersects any words and if so assign a random color to that word.
                int wordIndex = TMP_TextUtilities.FindIntersectingWord(m_TextMeshPro, Input.mousePosition, Camera.main);
                if (wordIndex != -1 && wordIndex != m_lastWordIndex)
                {
                    m_lastWordIndex = wordIndex;

                    TMP_WordInfo wInfo = m_TextMeshPro.textInfo.wordInfo[wordIndex];

                    Vector3 wordPOS = m_TextMeshPro.transform.TransformPoint(m_TextMeshPro.textInfo.characterInfo[wInfo.firstCharacterIndex].bottomLeft);
                    wordPOS = Camera.main.WorldToScreenPoint(wordPOS);

                    //Debug.Log("Mouse Position: " + Input.mousePosition.ToString("f3") + "  Word Position: " + wordPOS.ToString("f3"));

                    Color32[] vertexColors = m_TextMeshPro.textInfo.meshInfo[0].colors32;

                    Color32 c = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);
                    for (int i = 0; i < wInfo.characterCount; i++)
                    {
                        int vertexIndex = m_TextMeshPro.textInfo.characterInfo[wInfo.firstCharacterIndex + i].vertexIndex;

                        vertexColors[vertexIndex + 0] = c;
                        vertexColors[vertexIndex + 1] = c;
                        vertexColors[vertexIndex + 2] = c;
                        vertexColors[vertexIndex + 3] = c;
                    }

                    m_TextMeshPro.mesh.colors32 = vertexColors;
                }
                #endregion
            }
        }


        public void OnPointerEnter(PointerEventData eventData)
        {
            Debug.Log("OnPointerEnter()");
            m_isHoveringObject = true;
        }


        public void OnPointerExit(PointerEventData eventData)
        {
            Debug.Log("OnPointerExit()");
            m_isHoveringObject = false;
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_TextSelector_B.cs

```csharp
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;


#pragma warning disable 0618 // Disabled warning due to SetVertices being deprecated until new release with SetMesh() is available.

namespace TMPro.Examples
{

    public class TMP_TextSelector_B : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IPointerUpHandler
    {
        public RectTransform TextPopup_Prefab_01;

        private RectTransform m_TextPopup_RectTransform;
        private TextMeshProUGUI m_TextPopup_TMPComponent;
        private const string k_LinkText = "You have selected link <#ffff00>";
        private const string k_WordText = "Word Index: <#ffff00>";


        private TextMeshProUGUI m_TextMeshPro;
        private Canvas m_Canvas;
        private Camera m_Camera;

        // Flags
        private bool isHoveringObject;
        private int m_selectedWord = -1;
        private int m_selectedLink = -1;
        private int m_lastIndex = -1;

        private Matrix4x4 m_matrix;

        private TMP_MeshInfo[] m_cachedMeshInfoVertexData;

        void Awake()
        {
            m_TextMeshPro = gameObject.GetComponent<TextMeshProUGUI>();


            m_Canvas = gameObject.GetComponentInParent<Canvas>();

            // Get a reference to the camera if Canvas Render Mode is not ScreenSpace Overlay.
            if (m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                m_Camera = null;
            else
                m_Camera = m_Canvas.worldCamera;

            // Create pop-up text object which is used to show the link information.
            m_TextPopup_RectTransform = Instantiate(TextPopup_Prefab_01) as RectTransform;
            m_TextPopup_RectTransform.SetParent(m_Canvas.transform, false);
            m_TextPopup_TMPComponent = m_TextPopup_RectTransform.GetComponentInChildren<TextMeshProUGUI>();
            m_TextPopup_RectTransform.gameObject.SetActive(false);
        }


        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            // UnSubscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        void ON_TEXT_CHANGED(Object obj)
        {
            if (obj == m_TextMeshPro)
            {
                // Update cached vertex data.
                m_cachedMeshInfoVertexData = m_TextMeshPro.textInfo.CopyMeshInfoVertexData();
            }
        }


        void LateUpdate()
        {
            if (isHoveringObject)
            {
                // Check if Mouse Intersects any of the characters. If so, assign a random color.
                #region Handle Character Selection
                int charIndex = TMP_TextUtilities.FindIntersectingCharacter(m_TextMeshPro, Input.mousePosition, m_Camera, true);

                // Undo Swap and Vertex Attribute changes.
                if (charIndex == -1 || charIndex != m_lastIndex)
                {
                    RestoreCachedVertexAttributes(m_lastIndex);
                    m_lastIndex = -1;
                }

                if (charIndex != -1 && charIndex != m_lastIndex && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                {
                    m_lastIndex = charIndex;

                    // Get the index of the material / sub text object used by this character.
                    int materialIndex = m_TextMeshPro.textInfo.characterInfo[charIndex].materialReferenceIndex;

                    // Get the index of the first vertex of the selected character.
                    int vertexIndex = m_TextMeshPro.textInfo.characterInfo[charIndex].vertexIndex;

                    // Get a reference to the vertices array.
                    Vector3[] vertices = m_TextMeshPro.textInfo.meshInfo[materialIndex].vertices;

                    // Determine the center point of the character.
                    Vector2 charMidBasline = (vertices[vertexIndex + 0] + vertices[vertexIndex + 2]) / 2;

                    // Need to translate all 4 vertices of the character to aligned with middle of character / baseline.
                    // This is needed so the matrix TRS is applied at the origin for each character.
                    Vector3 offset = charMidBasline;

                    // Translate the character to the middle baseline.
                    vertices[vertexIndex + 0] = vertices[vertexIndex + 0] - offset;
                    vertices[vertexIndex + 1] = vertices[vertexIndex + 1] - offset;
                    vertices[vertexIndex + 2] = vertices[vertexIndex + 2] - offset;
                    vertices[vertexIndex + 3] = vertices[vertexIndex + 3] - offset;

                    float zoomFactor = 1.5f;

                    // Setup the Matrix for the scale change.
                    m_matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * zoomFactor);

                    // Apply Matrix operation on the given character.
                    vertices[vertexIndex + 0] = m_matrix.MultiplyPoint3x4(vertices[vertexIndex + 0]);
                    vertices[vertexIndex + 1] = m_matrix.MultiplyPoint3x4(vertices[vertexIndex + 1]);
                    vertices[vertexIndex + 2] = m_matrix.MultiplyPoint3x4(vertices[vertexIndex + 2]);
                    vertices[vertexIndex + 3] = m_matrix.MultiplyPoint3x4(vertices[vertexIndex + 3]);

                    // Translate the character back to its original position.
                    vertices[vertexIndex + 0] = vertices[vertexIndex + 0] + offset;
                    vertices[vertexIndex + 1] = vertices[vertexIndex + 1] + offset;
                    vertices[vertexIndex + 2] = vertices[vertexIndex + 2] + offset;
                    vertices[vertexIndex + 3] = vertices[vertexIndex + 3] + offset;

                    // Change Vertex Colors of the highlighted character
                    Color32 c = new Color32(255, 255, 192, 255);

                    // Get a reference to the vertex color
                    Color32[] vertexColors = m_TextMeshPro.textInfo.meshInfo[materialIndex].colors32;

                    vertexColors[vertexIndex + 0] = c;
                    vertexColors[vertexIndex + 1] = c;
                    vertexColors[vertexIndex + 2] = c;
                    vertexColors[vertexIndex + 3] = c;


                    // Get a reference to the meshInfo of the selected character.
                    TMP_MeshInfo meshInfo = m_TextMeshPro.textInfo.meshInfo[materialIndex];

                    // Get the index of the last character's vertex attributes.
                    int lastVertexIndex = vertices.Length - 4;

                    // Swap the current character's vertex attributes with those of the last element in the vertex attribute arrays.
                    // We do this to make sure this character is rendered last and over other characters.
                    meshInfo.SwapVertexData(vertexIndex, lastVertexIndex);

                    // Need to update the appropriate 
                    m_TextMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
                }
                #endregion


                #region Word Selection Handling
                //Check if Mouse intersects any words and if so assign a random color to that word.
                int wordIndex = TMP_TextUtilities.FindIntersectingWord(m_TextMeshPro, Input.mousePosition, m_Camera);

                // Clear previous word selection.
                if (m_TextPopup_RectTransform != null && m_selectedWord != -1 && (wordIndex == -1 || wordIndex != m_selectedWord))
                {
                    TMP_WordInfo wInfo = m_TextMeshPro.textInfo.wordInfo[m_selectedWord];

                    // Iterate through each of the characters of the word.
                    for (int i = 0; i < wInfo.characterCount; i++)
                    {
                        int characterIndex = wInfo.firstCharacterIndex + i;

                        // Get the index of the material / sub text object used by this character.
                        int meshIndex = m_TextMeshPro.textInfo.characterInfo[characterIndex].materialReferenceIndex;

                        // Get the index of the first vertex of this character.
                        int vertexIndex = m_TextMeshPro.textInfo.characterInfo[characterIndex].vertexIndex;

                        // Get a reference to the vertex color
                        Color32[] vertexColors = m_TextMeshPro.textInfo.meshInfo[meshIndex].colors32;

                        Color32 c = vertexColors[vertexIndex + 0].Tint(1.33333f);

                        vertexColors[vertexIndex + 0] = c;
                        vertexColors[vertexIndex + 1] = c;
                        vertexColors[vertexIndex + 2] = c;
                        vertexColors[vertexIndex + 3] = c;
                    }

                    // Update Geometry
                    m_TextMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.All);

                    m_selectedWord = -1;
                }


                // Word Selection Handling
                if (wordIndex != -1 && wordIndex != m_selectedWord && !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                {
                    m_selectedWord = wordIndex;

                    TMP_WordInfo wInfo = m_TextMeshPro.textInfo.wordInfo[wordIndex];

                    // Iterate through each of the characters of the word.
                    for (int i = 0; i < wInfo.characterCount; i++)
                    {
                        int characterIndex = wInfo.firstCharacterIndex + i;

                        // Get the index of the material / sub text object used by this character.
                        int meshIndex = m_TextMeshPro.textInfo.characterInfo[characterIndex].materialReferenceIndex;

                        int vertexIndex = m_TextMeshPro.textInfo.characterInfo[characterIndex].vertexIndex;

                        // Get a reference to the vertex color
                        Color32[] vertexColors = m_TextMeshPro.textInfo.meshInfo[meshIndex].colors32;

                        Color32 c = vertexColors[vertexIndex + 0].Tint(0.75f);

                        vertexColors[vertexIndex + 0] = c;
                        vertexColors[vertexIndex + 1] = c;
                        vertexColors[vertexIndex + 2] = c;
                        vertexColors[vertexIndex + 3] = c;
                    }

                    // Update Geometry
                    m_TextMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.All);

                }
                #endregion


                #region Example of Link Handling
                // Check if mouse intersects with any links.
                int linkIndex = TMP_TextUtilities.FindIntersectingLink(m_TextMeshPro, Input.mousePosition, m_Camera);

                // Clear previous link selection if one existed.
                if ((linkIndex == -1 && m_selectedLink != -1) || linkIndex != m_selectedLink)
                {
                    m_TextPopup_RectTransform.gameObject.SetActive(false);
                    m_selectedLink = -1;
                }

                // Handle new Link selection.
                if (linkIndex != -1 && linkIndex != m_selectedLink)
                {
                    m_selectedLink = linkIndex;

                    TMP_LinkInfo linkInfo = m_TextMeshPro.textInfo.linkInfo[linkIndex];

                    // Debug.Log("Link ID: \"" + linkInfo.GetLinkID() + "\"   Link Text: \"" + linkInfo.GetLinkText() + "\""); // Example of how to retrieve the Link ID and Link Text.

                    Vector3 worldPointInRectangle;
                    RectTransformUtility.ScreenPointToWorldPointInRectangle(m_TextMeshPro.rectTransform, Input.mousePosition, m_Camera, out worldPointInRectangle);

                    switch (linkInfo.GetLinkID())
                    {
                        case "id_01": // 100041637: // id_01
                            m_TextPopup_RectTransform.position = worldPointInRectangle;
                            m_TextPopup_RectTransform.gameObject.SetActive(true);
                            m_TextPopup_TMPComponent.text = k_LinkText + " ID 01";
                            break;
                        case "id_02": // 100041638: // id_02
                            m_TextPopup_RectTransform.position = worldPointInRectangle;
                            m_TextPopup_RectTransform.gameObject.SetActive(true);
                            m_TextPopup_TMPComponent.text = k_LinkText + " ID 02";
                            break;
                    }
                }
                #endregion

            }
            else
            {
                // Restore any character that may have been modified
                if (m_lastIndex != -1)
                {
                    RestoreCachedVertexAttributes(m_lastIndex);
                    m_lastIndex = -1;
                }
            }
            
        }


        public void OnPointerEnter(PointerEventData eventData)
        {
            //Debug.Log("OnPointerEnter()");
            isHoveringObject = true;
        }


        public void OnPointerExit(PointerEventData eventData)
        {
            //Debug.Log("OnPointerExit()");
            isHoveringObject = false;
        }


        public void OnPointerClick(PointerEventData eventData)
        {
            //Debug.Log("Click at POS: " + eventData.position + "  World POS: " + eventData.worldPosition);

            // Check if Mouse Intersects any of the characters. If so, assign a random color.
            #region Character Selection Handling
            /*
            int charIndex = TMP_TextUtilities.FindIntersectingCharacter(m_TextMeshPro, Input.mousePosition, m_Camera, true);
            if (charIndex != -1 && charIndex != m_lastIndex)
            {
                //Debug.Log("Character [" + m_TextMeshPro.textInfo.characterInfo[index].character + "] was selected at POS: " + eventData.position);
                m_lastIndex = charIndex;

                Color32 c = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);
                int vertexIndex = m_TextMeshPro.textInfo.characterInfo[charIndex].vertexIndex;

                UIVertex[] uiVertices = m_TextMeshPro.textInfo.meshInfo.uiVertices;

                uiVertices[vertexIndex + 0].color = c;
                uiVertices[vertexIndex + 1].color = c;
                uiVertices[vertexIndex + 2].color = c;
                uiVertices[vertexIndex + 3].color = c;

                m_TextMeshPro.canvasRenderer.SetVertices(uiVertices, uiVertices.Length);
            }
            */
            #endregion


            #region Word Selection Handling
            //Check if Mouse intersects any words and if so assign a random color to that word.
            /*
            int wordIndex = TMP_TextUtilities.FindIntersectingWord(m_TextMeshPro, Input.mousePosition, m_Camera);

            // Clear previous word selection.
            if (m_TextPopup_RectTransform != null && m_selectedWord != -1 && (wordIndex == -1 || wordIndex != m_selectedWord))
            {
                TMP_WordInfo wInfo = m_TextMeshPro.textInfo.wordInfo[m_selectedWord];

                // Get a reference to the uiVertices array.
                UIVertex[] uiVertices = m_TextMeshPro.textInfo.meshInfo.uiVertices;

                // Iterate through each of the characters of the word.
                for (int i = 0; i < wInfo.characterCount; i++)
                {
                    int vertexIndex = m_TextMeshPro.textInfo.characterInfo[wInfo.firstCharacterIndex + i].vertexIndex;

                    Color32 c = uiVertices[vertexIndex + 0].color.Tint(1.33333f);

                    uiVertices[vertexIndex + 0].color = c;
                    uiVertices[vertexIndex + 1].color = c;
                    uiVertices[vertexIndex + 2].color = c;
                    uiVertices[vertexIndex + 3].color = c;
                }

                m_TextMeshPro.canvasRenderer.SetVertices(uiVertices, uiVertices.Length);

                m_selectedWord = -1;
            }

            // Handle word selection
            if (wordIndex != -1 && wordIndex != m_selectedWord)
            {
                m_selectedWord = wordIndex;

                TMP_WordInfo wInfo = m_TextMeshPro.textInfo.wordInfo[wordIndex];

                // Get a reference to the uiVertices array.
                UIVertex[] uiVertices = m_TextMeshPro.textInfo.meshInfo.uiVertices;

                // Iterate through each of the characters of the word.
                for (int i = 0; i < wInfo.characterCount; i++)
                {
                    int vertexIndex = m_TextMeshPro.textInfo.characterInfo[wInfo.firstCharacterIndex + i].vertexIndex;

                    Color32 c = uiVertices[vertexIndex + 0].color.Tint(0.75f);

                    uiVertices[vertexIndex + 0].color = c;
                    uiVertices[vertexIndex + 1].color = c;
                    uiVertices[vertexIndex + 2].color = c;
                    uiVertices[vertexIndex + 3].color = c;
                }

                m_TextMeshPro.canvasRenderer.SetVertices(uiVertices, uiVertices.Length);
            }
            */
            #endregion


            #region Link Selection Handling
            /*
            // Check if Mouse intersects any words and if so assign a random color to that word.
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(m_TextMeshPro, Input.mousePosition, m_Camera);
            if (linkIndex != -1)
            {
                TMP_LinkInfo linkInfo = m_TextMeshPro.textInfo.linkInfo[linkIndex];
                int linkHashCode = linkInfo.hashCode;

                //Debug.Log(TMP_TextUtilities.GetSimpleHashCode("id_02"));

                switch (linkHashCode)
                {
                    case 291445: // id_01
                        if (m_LinkObject01 == null)
                            m_LinkObject01 = Instantiate(Link_01_Prefab);
                        else
                        {
                            m_LinkObject01.gameObject.SetActive(true);
                        }

                        break;
                    case 291446: // id_02
                        break;

                }

                // Example of how to modify vertex attributes like colors
                #region Vertex Attribute Modification Example
                UIVertex[] uiVertices = m_TextMeshPro.textInfo.meshInfo.uiVertices;

                Color32 c = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);
                for (int i = 0; i < linkInfo.characterCount; i++)
                {
                    TMP_CharacterInfo cInfo = m_TextMeshPro.textInfo.characterInfo[linkInfo.firstCharacterIndex + i];

                    if (!cInfo.isVisible) continue; // Skip invisible characters.

                    int vertexIndex = cInfo.vertexIndex;

                    uiVertices[vertexIndex + 0].color = c;
                    uiVertices[vertexIndex + 1].color = c;
                    uiVertices[vertexIndex + 2].color = c;
                    uiVertices[vertexIndex + 3].color = c;
                }

                m_TextMeshPro.canvasRenderer.SetVertices(uiVertices, uiVertices.Length);
                #endregion
            }
            */
            #endregion
        }


        public void OnPointerUp(PointerEventData eventData)
        {
            //Debug.Log("OnPointerUp()");
        }


        void RestoreCachedVertexAttributes(int index)
        {
            if (index == -1 || index > m_TextMeshPro.textInfo.characterCount - 1) return;

            // Get the index of the material / sub text object used by this character.
            int materialIndex = m_TextMeshPro.textInfo.characterInfo[index].materialReferenceIndex;

            // Get the index of the first vertex of the selected character.
            int vertexIndex = m_TextMeshPro.textInfo.characterInfo[index].vertexIndex;

            // Restore Vertices
            // Get a reference to the cached / original vertices.
            Vector3[] src_vertices = m_cachedMeshInfoVertexData[materialIndex].vertices;

            // Get a reference to the vertices that we need to replace.
            Vector3[] dst_vertices = m_TextMeshPro.textInfo.meshInfo[materialIndex].vertices;

            // Restore / Copy vertices from source to destination
            dst_vertices[vertexIndex + 0] = src_vertices[vertexIndex + 0];
            dst_vertices[vertexIndex + 1] = src_vertices[vertexIndex + 1];
            dst_vertices[vertexIndex + 2] = src_vertices[vertexIndex + 2];
            dst_vertices[vertexIndex + 3] = src_vertices[vertexIndex + 3];

            // Restore Vertex Colors
            // Get a reference to the vertex colors we need to replace.
            Color32[] dst_colors = m_TextMeshPro.textInfo.meshInfo[materialIndex].colors32;

            // Get a reference to the cached / original vertex colors.
            Color32[] src_colors = m_cachedMeshInfoVertexData[materialIndex].colors32;

            // Copy the vertex colors from source to destination.
            dst_colors[vertexIndex + 0] = src_colors[vertexIndex + 0];
            dst_colors[vertexIndex + 1] = src_colors[vertexIndex + 1];
            dst_colors[vertexIndex + 2] = src_colors[vertexIndex + 2];
            dst_colors[vertexIndex + 3] = src_colors[vertexIndex + 3];

            // Restore UV0S
            // UVS0
            Vector2[] src_uv0s = m_cachedMeshInfoVertexData[materialIndex].uvs0;
            Vector2[] dst_uv0s = m_TextMeshPro.textInfo.meshInfo[materialIndex].uvs0;
            dst_uv0s[vertexIndex + 0] = src_uv0s[vertexIndex + 0];
            dst_uv0s[vertexIndex + 1] = src_uv0s[vertexIndex + 1];
            dst_uv0s[vertexIndex + 2] = src_uv0s[vertexIndex + 2];
            dst_uv0s[vertexIndex + 3] = src_uv0s[vertexIndex + 3];

            // UVS2
            Vector2[] src_uv2s = m_cachedMeshInfoVertexData[materialIndex].uvs2;
            Vector2[] dst_uv2s = m_TextMeshPro.textInfo.meshInfo[materialIndex].uvs2;
            dst_uv2s[vertexIndex + 0] = src_uv2s[vertexIndex + 0];
            dst_uv2s[vertexIndex + 1] = src_uv2s[vertexIndex + 1];
            dst_uv2s[vertexIndex + 2] = src_uv2s[vertexIndex + 2];
            dst_uv2s[vertexIndex + 3] = src_uv2s[vertexIndex + 3];


            // Restore last vertex attribute as we swapped it as well
            int lastIndex = (src_vertices.Length / 4 - 1) * 4;

            // Vertices
            dst_vertices[lastIndex + 0] = src_vertices[lastIndex + 0];
            dst_vertices[lastIndex + 1] = src_vertices[lastIndex + 1];
            dst_vertices[lastIndex + 2] = src_vertices[lastIndex + 2];
            dst_vertices[lastIndex + 3] = src_vertices[lastIndex + 3];

            // Vertex Colors
            src_colors = m_cachedMeshInfoVertexData[materialIndex].colors32;
            dst_colors = m_TextMeshPro.textInfo.meshInfo[materialIndex].colors32;
            dst_colors[lastIndex + 0] = src_colors[lastIndex + 0];
            dst_colors[lastIndex + 1] = src_colors[lastIndex + 1];
            dst_colors[lastIndex + 2] = src_colors[lastIndex + 2];
            dst_colors[lastIndex + 3] = src_colors[lastIndex + 3];

            // UVS0
            src_uv0s = m_cachedMeshInfoVertexData[materialIndex].uvs0;
            dst_uv0s = m_TextMeshPro.textInfo.meshInfo[materialIndex].uvs0;
            dst_uv0s[lastIndex + 0] = src_uv0s[lastIndex + 0];
            dst_uv0s[lastIndex + 1] = src_uv0s[lastIndex + 1];
            dst_uv0s[lastIndex + 2] = src_uv0s[lastIndex + 2];
            dst_uv0s[lastIndex + 3] = src_uv0s[lastIndex + 3];

            // UVS2
            src_uv2s = m_cachedMeshInfoVertexData[materialIndex].uvs2;
            dst_uv2s = m_TextMeshPro.textInfo.meshInfo[materialIndex].uvs2;
            dst_uv2s[lastIndex + 0] = src_uv2s[lastIndex + 0];
            dst_uv2s[lastIndex + 1] = src_uv2s[lastIndex + 1];
            dst_uv2s[lastIndex + 2] = src_uv2s[lastIndex + 2];
            dst_uv2s[lastIndex + 3] = src_uv2s[lastIndex + 3];

            // Need to update the appropriate 
            m_TextMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
        }
    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/TMP_UiFrameRateCounter.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{
    
    public class TMP_UiFrameRateCounter : MonoBehaviour
    {
        public float UpdateInterval = 5.0f;
        private float m_LastInterval = 0;
        private int m_Frames = 0;

        public enum FpsCounterAnchorPositions { TopLeft, BottomLeft, TopRight, BottomRight };

        public FpsCounterAnchorPositions AnchorPosition = FpsCounterAnchorPositions.TopRight;

        private string htmlColorTag;
        private const string fpsLabel = "{0:2}</color> <#8080ff>FPS \n<#FF8000>{1:2} <#8080ff>MS";

        private TextMeshProUGUI m_TextMeshPro;
        private RectTransform m_frameCounter_transform;

        private FpsCounterAnchorPositions last_AnchorPosition;

        void Awake()
        {
            if (!enabled)
                return;

            Application.targetFrameRate = 1000;

            GameObject frameCounter = new GameObject("Frame Counter");
            m_frameCounter_transform = frameCounter.AddComponent<RectTransform>();

            m_frameCounter_transform.SetParent(this.transform, false);

            m_TextMeshPro = frameCounter.AddComponent<TextMeshProUGUI>();
            m_TextMeshPro.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            m_TextMeshPro.fontSharedMaterial = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - Overlay");

            m_TextMeshPro.enableWordWrapping = false;
            m_TextMeshPro.fontSize = 36;

            m_TextMeshPro.isOverlay = true;

            Set_FrameCounter_Position(AnchorPosition);
            last_AnchorPosition = AnchorPosition;
        }


        void Start()
        {
            m_LastInterval = Time.realtimeSinceStartup;
            m_Frames = 0;
        }


        void Update()
        {
            if (AnchorPosition != last_AnchorPosition)
                Set_FrameCounter_Position(AnchorPosition);

            last_AnchorPosition = AnchorPosition;

            m_Frames += 1;
            float timeNow = Time.realtimeSinceStartup;

            if (timeNow > m_LastInterval + UpdateInterval)
            {
                // display two fractional digits (f2 format)
                float fps = m_Frames / (timeNow - m_LastInterval);
                float ms = 1000.0f / Mathf.Max(fps, 0.00001f);

                if (fps < 30)
                    htmlColorTag = "<color=yellow>";
                else if (fps < 10)
                    htmlColorTag = "<color=red>";
                else
                    htmlColorTag = "<color=green>";

                m_TextMeshPro.SetText(htmlColorTag + fpsLabel, fps, ms);

                m_Frames = 0;
                m_LastInterval = timeNow;
            }
        }


        void Set_FrameCounter_Position(FpsCounterAnchorPositions anchor_position)
        {
            switch (anchor_position)
            {
                case FpsCounterAnchorPositions.TopLeft:
                    m_TextMeshPro.alignment = TextAlignmentOptions.TopLeft;
                    m_frameCounter_transform.pivot = new Vector2(0, 1);
                    m_frameCounter_transform.anchorMin = new Vector2(0.01f, 0.99f);
                    m_frameCounter_transform.anchorMax = new Vector2(0.01f, 0.99f);
                    m_frameCounter_transform.anchoredPosition = new Vector2(0, 1);
                    break;
                case FpsCounterAnchorPositions.BottomLeft:
                    m_TextMeshPro.alignment = TextAlignmentOptions.BottomLeft;
                    m_frameCounter_transform.pivot = new Vector2(0, 0);
                    m_frameCounter_transform.anchorMin = new Vector2(0.01f, 0.01f);
                    m_frameCounter_transform.anchorMax = new Vector2(0.01f, 0.01f);
                    m_frameCounter_transform.anchoredPosition = new Vector2(0, 0);
                    break;
                case FpsCounterAnchorPositions.TopRight:
                    m_TextMeshPro.alignment = TextAlignmentOptions.TopRight;
                    m_frameCounter_transform.pivot = new Vector2(1, 1);
                    m_frameCounter_transform.anchorMin = new Vector2(0.99f, 0.99f);
                    m_frameCounter_transform.anchorMax = new Vector2(0.99f, 0.99f);
                    m_frameCounter_transform.anchoredPosition = new Vector2(1, 1);
                    break;
                case FpsCounterAnchorPositions.BottomRight:
                    m_TextMeshPro.alignment = TextAlignmentOptions.BottomRight;
                    m_frameCounter_transform.pivot = new Vector2(1, 0);
                    m_frameCounter_transform.anchorMin = new Vector2(0.99f, 0.01f);
                    m_frameCounter_transform.anchorMax = new Vector2(0.99f, 0.01f);
                    m_frameCounter_transform.anchoredPosition = new Vector2(1, 0);
                    break;
            }
        }
    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/VertexColorCycler.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class VertexColorCycler : MonoBehaviour
    {

        private TMP_Text m_TextComponent;

        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
        }


        void Start()
        {
            StartCoroutine(AnimateVertexColors());
        }


        /// <summary>
        /// Method to animate vertex colors of a TMP Text object.
        /// </summary>
        /// <returns></returns>
        IEnumerator AnimateVertexColors()
        {
            // Force the text object to update right away so we can have geometry to modify right from the start.
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;
            int currentCharacter = 0;

            Color32[] newVertexColors;
            Color32 c0 = m_TextComponent.color;

            while (true)
            {
                int characterCount = textInfo.characterCount;

                // If No Characters then just yield and wait for some text to be added
                if (characterCount == 0)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                // Get the index of the material used by the current character.
                int materialIndex = textInfo.characterInfo[currentCharacter].materialReferenceIndex;

                // Get the vertex colors of the mesh used by this text element (character or sprite).
                newVertexColors = textInfo.meshInfo[materialIndex].colors32;

                // Get the index of the first vertex used by this text element.
                int vertexIndex = textInfo.characterInfo[currentCharacter].vertexIndex;

                // Only change the vertex color if the text element is visible.
                if (textInfo.characterInfo[currentCharacter].isVisible)
                {
                    c0 = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 255), (byte)Random.Range(0, 255), 255);

                    newVertexColors[vertexIndex + 0] = c0;
                    newVertexColors[vertexIndex + 1] = c0;
                    newVertexColors[vertexIndex + 2] = c0;
                    newVertexColors[vertexIndex + 3] = c0;

                    // New function which pushes (all) updated vertex data to the appropriate meshes when using either the Mesh Renderer or CanvasRenderer.
                    m_TextComponent.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);

                    // This last process could be done to only update the vertex data that has changed as opposed to all of the vertex data but it would require extra steps and knowing what type of renderer is used.
                    // These extra steps would be a performance optimization but it is unlikely that such optimization will be necessary.
                }

                currentCharacter = (currentCharacter + 1) % characterCount;

                yield return new WaitForSeconds(0.05f);
            }
        }

    }
}

```

## Assets/TextMesh Pro/Examples & Extras/Scripts/VertexJitter.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class VertexJitter : MonoBehaviour
    {

        public float AngleMultiplier = 1.0f;
        public float SpeedMultiplier = 1.0f;
        public float CurveScale = 1.0f;

        private TMP_Text m_TextComponent;
        private bool hasTextChanged;

        /// <summary>
        /// Structure to hold pre-computed animation data.
        /// </summary>
        private struct VertexAnim
        {
            public float angleRange;
            public float angle;
            public float speed;
        }

        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
        }

        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        void Start()
        {
            StartCoroutine(AnimateVertexColors());
        }


        void ON_TEXT_CHANGED(Object obj)
        {
            if (obj == m_TextComponent)
                hasTextChanged = true;
        }

        /// <summary>
        /// Method to animate vertex colors of a TMP Text object.
        /// </summary>
        /// <returns></returns>
        IEnumerator AnimateVertexColors()
        {

            // We force an update of the text object since it would only be updated at the end of the frame. Ie. before this code is executed on the first frame.
            // Alternatively, we could yield and wait until the end of the frame when the text object will be generated.
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;

            Matrix4x4 matrix;

            int loopCount = 0;
            hasTextChanged = true;

            // Create an Array which contains pre-computed Angle Ranges and Speeds for a bunch of characters.
            VertexAnim[] vertexAnim = new VertexAnim[1024];
            for (int i = 0; i < 1024; i++)
            {
                vertexAnim[i].angleRange = Random.Range(10f, 25f);
                vertexAnim[i].speed = Random.Range(1f, 3f);
            }

            // Cache the vertex data of the text object as the Jitter FX is applied to the original position of the characters.
            TMP_MeshInfo[] cachedMeshInfo = textInfo.CopyMeshInfoVertexData();

            while (true)
            {
                // Get new copy of vertex data if the text has changed.
                if (hasTextChanged)
                {
                    // Update the copy of the vertex data for the text object.
                    cachedMeshInfo = textInfo.CopyMeshInfoVertexData();

                    hasTextChanged = false;
                }

                int characterCount = textInfo.characterCount;

                // If No Characters then just yield and wait for some text to be added
                if (characterCount == 0)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }


                for (int i = 0; i < characterCount; i++)
                {
                    TMP_CharacterInfo charInfo = textInfo.characterInfo[i];

                    // Skip characters that are not visible and thus have no geometry to manipulate.
                    if (!charInfo.isVisible)
                        continue;

                    // Retrieve the pre-computed animation data for the given character.
                    VertexAnim vertAnim = vertexAnim[i];

                    // Get the index of the material used by the current character.
                    int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;

                    // Get the index of the first vertex used by this text element.
                    int vertexIndex = textInfo.characterInfo[i].vertexIndex;

                    // Get the cached vertices of the mesh used by this text element (character or sprite).
                    Vector3[] sourceVertices = cachedMeshInfo[materialIndex].vertices;

                    // Determine the center point of each character at the baseline.
                    //Vector2 charMidBasline = new Vector2((sourceVertices[vertexIndex + 0].x + sourceVertices[vertexIndex + 2].x) / 2, charInfo.baseLine);
                    // Determine the center point of each character.
                    Vector2 charMidBasline = (sourceVertices[vertexIndex + 0] + sourceVertices[vertexIndex + 2]) / 2;

                    // Need to translate all 4 vertices of each quad to aligned with middle of character / baseline.
                    // This is needed so the matrix TRS is applied at the origin for each character.
                    Vector3 offset = charMidBasline;

                    Vector3[] destinationVertices = textInfo.meshInfo[materialIndex].vertices;

                    destinationVertices[vertexIndex + 0] = sourceVertices[vertexIndex + 0] - offset;
                    destinationVertices[vertexIndex + 1] = sourceVertices[vertexIndex + 1] - offset;
                    destinationVertices[vertexIndex + 2] = sourceVertices[vertexIndex + 2] - offset;
                    destinationVertices[vertexIndex + 3] = sourceVertices[vertexIndex + 3] - offset;

                    vertAnim.angle = Mathf.SmoothStep(-vertAnim.angleRange, vertAnim.angleRange, Mathf.PingPong(loopCount / 25f * vertAnim.speed, 1f));
                    Vector3 jitterOffset = new Vector3(Random.Range(-.25f, .25f), Random.Range(-.25f, .25f), 0);

                    matrix = Matrix4x4.TRS(jitterOffset * CurveScale, Quaternion.Euler(0, 0, Random.Range(-5f, 5f) * AngleMultiplier), Vector3.one);

                    destinationVertices[vertexIndex + 0] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 0]);
                    destinationVertices[vertexIndex + 1] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 1]);
                    destinationVertices[vertexIndex + 2] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 2]);
                    destinationVertices[vertexIndex + 3] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 3]);

                    destinationVertices[vertexIndex + 0] += offset;
                    destinationVertices[vertexIndex + 1] += offset;
                    destinationVertices[vertexIndex + 2] += offset;
                    destinationVertices[vertexIndex + 3] += offset;

                    vertexAnim[i] = vertAnim;
                }

                // Push changes into meshes
                for (int i = 0; i < textInfo.meshInfo.Length; i++)
                {
                    textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
                    m_TextComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
                }

                loopCount += 1;

                yield return new WaitForSeconds(0.1f);
            }
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/VertexShakeA.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class VertexShakeA : MonoBehaviour
    {

        public float AngleMultiplier = 1.0f;
        public float SpeedMultiplier = 1.0f;
        public float ScaleMultiplier = 1.0f;
        public float RotationMultiplier = 1.0f;

        private TMP_Text m_TextComponent;
        private bool hasTextChanged;


        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
        }

        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        void Start()
        {
            StartCoroutine(AnimateVertexColors());
        }


        void ON_TEXT_CHANGED(Object obj)
        {
            if (obj = m_TextComponent)
                hasTextChanged = true;
        }

        /// <summary>
        /// Method to animate vertex colors of a TMP Text object.
        /// </summary>
        /// <returns></returns>
        IEnumerator AnimateVertexColors()
        {

            // We force an update of the text object since it would only be updated at the end of the frame. Ie. before this code is executed on the first frame.
            // Alternatively, we could yield and wait until the end of the frame when the text object will be generated.
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;

            Matrix4x4 matrix;
            Vector3[][] copyOfVertices = new Vector3[0][];

            hasTextChanged = true;

            while (true)
            {
                // Allocate new vertices 
                if (hasTextChanged)
                {
                    if (copyOfVertices.Length < textInfo.meshInfo.Length)
                        copyOfVertices = new Vector3[textInfo.meshInfo.Length][];

                    for (int i = 0; i < textInfo.meshInfo.Length; i++)
                    {
                        int length = textInfo.meshInfo[i].vertices.Length;
                        copyOfVertices[i] = new Vector3[length];
                    }

                    hasTextChanged = false;
                }

                int characterCount = textInfo.characterCount;

                // If No Characters then just yield and wait for some text to be added
                if (characterCount == 0)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                int lineCount = textInfo.lineCount;

                // Iterate through each line of the text.
                for (int i = 0; i < lineCount; i++)
                {

                    int first = textInfo.lineInfo[i].firstCharacterIndex;
                    int last = textInfo.lineInfo[i].lastCharacterIndex;

                    // Determine the center of each line
                    Vector3 centerOfLine = (textInfo.characterInfo[first].bottomLeft + textInfo.characterInfo[last].topRight) / 2;
                    Quaternion rotation = Quaternion.Euler(0, 0, Random.Range(-0.25f, 0.25f) * RotationMultiplier);

                    // Iterate through each character of the line.
                    for (int j = first; j <= last; j++)
                    {
                        // Skip characters that are not visible and thus have no geometry to manipulate.
                        if (!textInfo.characterInfo[j].isVisible)
                            continue;

                        // Get the index of the material used by the current character.
                        int materialIndex = textInfo.characterInfo[j].materialReferenceIndex;

                        // Get the index of the first vertex used by this text element.
                        int vertexIndex = textInfo.characterInfo[j].vertexIndex;

                        // Get the vertices of the mesh used by this text element (character or sprite).
                        Vector3[] sourceVertices = textInfo.meshInfo[materialIndex].vertices;

                        // Need to translate all 4 vertices of each quad to aligned with center of character.
                        // This is needed so the matrix TRS is applied at the origin for each character.
                        copyOfVertices[materialIndex][vertexIndex + 0] = sourceVertices[vertexIndex + 0] - centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 1] = sourceVertices[vertexIndex + 1] - centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 2] = sourceVertices[vertexIndex + 2] - centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 3] = sourceVertices[vertexIndex + 3] - centerOfLine;

                        // Determine the random scale change for each character.
                        float randomScale = Random.Range(0.995f - 0.001f * ScaleMultiplier, 1.005f + 0.001f * ScaleMultiplier);

                        // Setup the matrix rotation.
                        matrix = Matrix4x4.TRS(Vector3.one, rotation, Vector3.one * randomScale);

                        // Apply the matrix TRS to the individual characters relative to the center of the current line.
                        copyOfVertices[materialIndex][vertexIndex + 0] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 0]);
                        copyOfVertices[materialIndex][vertexIndex + 1] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 1]);
                        copyOfVertices[materialIndex][vertexIndex + 2] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 2]);
                        copyOfVertices[materialIndex][vertexIndex + 3] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 3]);

                        // Revert the translation change.
                        copyOfVertices[materialIndex][vertexIndex + 0] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 1] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 2] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 3] += centerOfLine;
                    }
                }

                // Push changes into meshes
                for (int i = 0; i < textInfo.meshInfo.Length; i++)
                {
                    textInfo.meshInfo[i].mesh.vertices = copyOfVertices[i];
                    m_TextComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
                }

                yield return new WaitForSeconds(0.1f);
            }
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/VertexShakeB.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class VertexShakeB : MonoBehaviour
    {

        public float AngleMultiplier = 1.0f;
        public float SpeedMultiplier = 1.0f;
        public float CurveScale = 1.0f;

        private TMP_Text m_TextComponent;
        private bool hasTextChanged;


        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
        }

        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        void Start()
        {
            StartCoroutine(AnimateVertexColors());
        }


        void ON_TEXT_CHANGED(Object obj)
        {
            if (obj = m_TextComponent)
                hasTextChanged = true;
        }

        /// <summary>
        /// Method to animate vertex colors of a TMP Text object.
        /// </summary>
        /// <returns></returns>
        IEnumerator AnimateVertexColors()
        {

            // We force an update of the text object since it would only be updated at the end of the frame. Ie. before this code is executed on the first frame.
            // Alternatively, we could yield and wait until the end of the frame when the text object will be generated.
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;

            Matrix4x4 matrix;
            Vector3[][] copyOfVertices = new Vector3[0][];

            hasTextChanged = true;

            while (true)
            {
                // Allocate new vertices 
                if (hasTextChanged)
                {
                    if (copyOfVertices.Length < textInfo.meshInfo.Length)
                        copyOfVertices = new Vector3[textInfo.meshInfo.Length][];

                    for (int i = 0; i < textInfo.meshInfo.Length; i++)
                    {
                        int length = textInfo.meshInfo[i].vertices.Length;
                        copyOfVertices[i] = new Vector3[length];
                    }

                    hasTextChanged = false;
                }

                int characterCount = textInfo.characterCount;

                // If No Characters then just yield and wait for some text to be added
                if (characterCount == 0)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                int lineCount = textInfo.lineCount;

                // Iterate through each line of the text.
                for (int i = 0; i < lineCount; i++)
                {

                    int first = textInfo.lineInfo[i].firstCharacterIndex;
                    int last = textInfo.lineInfo[i].lastCharacterIndex;

                    // Determine the center of each line
                    Vector3 centerOfLine = (textInfo.characterInfo[first].bottomLeft + textInfo.characterInfo[last].topRight) / 2;
                    Quaternion rotation = Quaternion.Euler(0, 0, Random.Range(-0.25f, 0.25f));

                    // Iterate through each character of the line.
                    for (int j = first; j <= last; j++)
                    {
                        // Skip characters that are not visible and thus have no geometry to manipulate.
                        if (!textInfo.characterInfo[j].isVisible)
                            continue;

                        // Get the index of the material used by the current character.
                        int materialIndex = textInfo.characterInfo[j].materialReferenceIndex;

                        // Get the index of the first vertex used by this text element.
                        int vertexIndex = textInfo.characterInfo[j].vertexIndex;

                        // Get the vertices of the mesh used by this text element (character or sprite).
                        Vector3[] sourceVertices = textInfo.meshInfo[materialIndex].vertices;

                        // Determine the center point of each character at the baseline.
                        Vector3 charCenter = (sourceVertices[vertexIndex + 0] + sourceVertices[vertexIndex + 2]) / 2;

                        // Need to translate all 4 vertices of each quad to aligned with center of character.
                        // This is needed so the matrix TRS is applied at the origin for each character.
                        copyOfVertices[materialIndex][vertexIndex + 0] = sourceVertices[vertexIndex + 0] - charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 1] = sourceVertices[vertexIndex + 1] - charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 2] = sourceVertices[vertexIndex + 2] - charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 3] = sourceVertices[vertexIndex + 3] - charCenter;

                        // Determine the random scale change for each character.
                        float randomScale = Random.Range(0.95f, 1.05f);

                        // Setup the matrix for the scale change.
                        matrix = Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.one * randomScale);

                        // Apply the scale change relative to the center of each character.
                        copyOfVertices[materialIndex][vertexIndex + 0] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 0]);
                        copyOfVertices[materialIndex][vertexIndex + 1] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 1]);
                        copyOfVertices[materialIndex][vertexIndex + 2] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 2]);
                        copyOfVertices[materialIndex][vertexIndex + 3] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 3]);

                        // Revert the translation change.
                        copyOfVertices[materialIndex][vertexIndex + 0] += charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 1] += charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 2] += charCenter;
                        copyOfVertices[materialIndex][vertexIndex + 3] += charCenter;

                        // Need to translate all 4 vertices of each quad to aligned with the center of the line.
                        // This is needed so the matrix TRS is applied from the center of the line.
                        copyOfVertices[materialIndex][vertexIndex + 0] -= centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 1] -= centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 2] -= centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 3] -= centerOfLine;

                        // Setup the matrix rotation.
                        matrix = Matrix4x4.TRS(Vector3.one, rotation, Vector3.one);

                        // Apply the matrix TRS to the individual characters relative to the center of the current line.
                        copyOfVertices[materialIndex][vertexIndex + 0] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 0]);
                        copyOfVertices[materialIndex][vertexIndex + 1] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 1]);
                        copyOfVertices[materialIndex][vertexIndex + 2] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 2]);
                        copyOfVertices[materialIndex][vertexIndex + 3] = matrix.MultiplyPoint3x4(copyOfVertices[materialIndex][vertexIndex + 3]);

                        // Revert the translation change.
                        copyOfVertices[materialIndex][vertexIndex + 0] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 1] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 2] += centerOfLine;
                        copyOfVertices[materialIndex][vertexIndex + 3] += centerOfLine;
                    }
                }

                // Push changes into meshes
                for (int i = 0; i < textInfo.meshInfo.Length; i++)
                {
                    textInfo.meshInfo[i].mesh.vertices = copyOfVertices[i];
                    m_TextComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
                }

                yield return new WaitForSeconds(0.1f);
            }
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/VertexZoom.cs

```csharp
using UnityEngine;
using System.Linq;
using System.Collections;
using System.Collections.Generic;


namespace TMPro.Examples
{

    public class VertexZoom : MonoBehaviour
    {
        public float AngleMultiplier = 1.0f;
        public float SpeedMultiplier = 1.0f;
        public float CurveScale = 1.0f;

        private TMP_Text m_TextComponent;
        private bool hasTextChanged;


        void Awake()
        {
            m_TextComponent = GetComponent<TMP_Text>();
        }

        void OnEnable()
        {
            // Subscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(ON_TEXT_CHANGED);
        }

        void OnDisable()
        {
            // UnSubscribe to event fired when text object has been regenerated.
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(ON_TEXT_CHANGED);
        }


        void Start()
        {
            StartCoroutine(AnimateVertexColors());
        }


        void ON_TEXT_CHANGED(Object obj)
        {
            if (obj == m_TextComponent)
                hasTextChanged = true;
        }

        /// <summary>
        /// Method to animate vertex colors of a TMP Text object.
        /// </summary>
        /// <returns></returns>
        IEnumerator AnimateVertexColors()
        {

            // We force an update of the text object since it would only be updated at the end of the frame. Ie. before this code is executed on the first frame.
            // Alternatively, we could yield and wait until the end of the frame when the text object will be generated.
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;

            Matrix4x4 matrix;
            TMP_MeshInfo[] cachedMeshInfoVertexData = textInfo.CopyMeshInfoVertexData();

            // Allocations for sorting of the modified scales
            List<float> modifiedCharScale = new List<float>();
            List<int> scaleSortingOrder = new List<int>();

            hasTextChanged = true;

            while (true)
            {
                // Allocate new vertices 
                if (hasTextChanged)
                {
                    // Get updated vertex data
                    cachedMeshInfoVertexData = textInfo.CopyMeshInfoVertexData();

                    hasTextChanged = false;
                }

                int characterCount = textInfo.characterCount;

                // If No Characters then just yield and wait for some text to be added
                if (characterCount == 0)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                // Clear list of character scales
                modifiedCharScale.Clear();
                scaleSortingOrder.Clear();

                for (int i = 0; i < characterCount; i++)
                {
                    TMP_CharacterInfo charInfo = textInfo.characterInfo[i];

                    // Skip characters that are not visible and thus have no geometry to manipulate.
                    if (!charInfo.isVisible)
                        continue;

                    // Get the index of the material used by the current character.
                    int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;

                    // Get the index of the first vertex used by this text element.
                    int vertexIndex = textInfo.characterInfo[i].vertexIndex;

                    // Get the cached vertices of the mesh used by this text element (character or sprite).
                    Vector3[] sourceVertices = cachedMeshInfoVertexData[materialIndex].vertices;

                    // Determine the center point of each character at the baseline.
                    //Vector2 charMidBasline = new Vector2((sourceVertices[vertexIndex + 0].x + sourceVertices[vertexIndex + 2].x) / 2, charInfo.baseLine);
                    // Determine the center point of each character.
                    Vector2 charMidBasline = (sourceVertices[vertexIndex + 0] + sourceVertices[vertexIndex + 2]) / 2;

                    // Need to translate all 4 vertices of each quad to aligned with middle of character / baseline.
                    // This is needed so the matrix TRS is applied at the origin for each character.
                    Vector3 offset = charMidBasline;

                    Vector3[] destinationVertices = textInfo.meshInfo[materialIndex].vertices;

                    destinationVertices[vertexIndex + 0] = sourceVertices[vertexIndex + 0] - offset;
                    destinationVertices[vertexIndex + 1] = sourceVertices[vertexIndex + 1] - offset;
                    destinationVertices[vertexIndex + 2] = sourceVertices[vertexIndex + 2] - offset;
                    destinationVertices[vertexIndex + 3] = sourceVertices[vertexIndex + 3] - offset;

                    //Vector3 jitterOffset = new Vector3(Random.Range(-.25f, .25f), Random.Range(-.25f, .25f), 0);

                    // Determine the random scale change for each character.
                    float randomScale = Random.Range(1f, 1.5f);
                    
                    // Add modified scale and index
                    modifiedCharScale.Add(randomScale);
                    scaleSortingOrder.Add(modifiedCharScale.Count - 1);

                    // Setup the matrix for the scale change.
                    //matrix = Matrix4x4.TRS(jitterOffset, Quaternion.Euler(0, 0, Random.Range(-5f, 5f)), Vector3.one * randomScale);
                    matrix = Matrix4x4.TRS(new Vector3(0, 0, 0), Quaternion.identity, Vector3.one * randomScale);

                    destinationVertices[vertexIndex + 0] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 0]);
                    destinationVertices[vertexIndex + 1] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 1]);
                    destinationVertices[vertexIndex + 2] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 2]);
                    destinationVertices[vertexIndex + 3] = matrix.MultiplyPoint3x4(destinationVertices[vertexIndex + 3]);

                    destinationVertices[vertexIndex + 0] += offset;
                    destinationVertices[vertexIndex + 1] += offset;
                    destinationVertices[vertexIndex + 2] += offset;
                    destinationVertices[vertexIndex + 3] += offset;

                    // Restore Source UVS which have been modified by the sorting
                    Vector2[] sourceUVs0 = cachedMeshInfoVertexData[materialIndex].uvs0;
                    Vector2[] destinationUVs0 = textInfo.meshInfo[materialIndex].uvs0;

                    destinationUVs0[vertexIndex + 0] = sourceUVs0[vertexIndex + 0];
                    destinationUVs0[vertexIndex + 1] = sourceUVs0[vertexIndex + 1];
                    destinationUVs0[vertexIndex + 2] = sourceUVs0[vertexIndex + 2];
                    destinationUVs0[vertexIndex + 3] = sourceUVs0[vertexIndex + 3];

                    // Restore Source Vertex Colors
                    Color32[] sourceColors32 = cachedMeshInfoVertexData[materialIndex].colors32;
                    Color32[] destinationColors32 = textInfo.meshInfo[materialIndex].colors32;

                    destinationColors32[vertexIndex + 0] = sourceColors32[vertexIndex + 0];
                    destinationColors32[vertexIndex + 1] = sourceColors32[vertexIndex + 1];
                    destinationColors32[vertexIndex + 2] = sourceColors32[vertexIndex + 2];
                    destinationColors32[vertexIndex + 3] = sourceColors32[vertexIndex + 3];
                }

                // Push changes into meshes
                for (int i = 0; i < textInfo.meshInfo.Length; i++)
                {
                    //// Sort Quads based modified scale
                    scaleSortingOrder.Sort((a, b) => modifiedCharScale[a].CompareTo(modifiedCharScale[b]));

                    textInfo.meshInfo[i].SortGeometry(scaleSortingOrder);

                    // Updated modified vertex attributes
                    textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
                    textInfo.meshInfo[i].mesh.uv = textInfo.meshInfo[i].uvs0;
                    textInfo.meshInfo[i].mesh.colors32 = textInfo.meshInfo[i].colors32;

                    m_TextComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
                }

                yield return new WaitForSeconds(0.1f);
            }
        }

    }
}
```

## Assets/TextMesh Pro/Examples & Extras/Scripts/WarpTextExample.cs

```csharp
using UnityEngine;
using System.Collections;


namespace TMPro.Examples
{

    public class WarpTextExample : MonoBehaviour
    {

        private TMP_Text m_TextComponent;

        public AnimationCurve VertexCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.25f, 2.0f), new Keyframe(0.5f, 0), new Keyframe(0.75f, 2.0f), new Keyframe(1, 0f));
        public float AngleMultiplier = 1.0f;
        public float SpeedMultiplier = 1.0f;
        public float CurveScale = 1.0f;

        void Awake()
        {
            m_TextComponent = gameObject.GetComponent<TMP_Text>();
        }


        void Start()
        {
            StartCoroutine(WarpText());
        }


        private AnimationCurve CopyAnimationCurve(AnimationCurve curve)
        {
            AnimationCurve newCurve = new AnimationCurve();

            newCurve.keys = curve.keys;

            return newCurve;
        }


        /// <summary>
        ///  Method to curve text along a Unity animation curve.
        /// </summary>
        /// <param name="textComponent"></param>
        /// <returns></returns>
        IEnumerator WarpText()
        {
            VertexCurve.preWrapMode = WrapMode.Clamp;
            VertexCurve.postWrapMode = WrapMode.Clamp;

            //Mesh mesh = m_TextComponent.textInfo.meshInfo[0].mesh;

            Vector3[] vertices;
            Matrix4x4 matrix;

            m_TextComponent.havePropertiesChanged = true; // Need to force the TextMeshPro Object to be updated.
            CurveScale *= 10;
            float old_CurveScale = CurveScale;
            AnimationCurve old_curve = CopyAnimationCurve(VertexCurve);

            while (true)
            {
                if (!m_TextComponent.havePropertiesChanged && old_CurveScale == CurveScale && old_curve.keys[1].value == VertexCurve.keys[1].value)
                {
                    yield return null;
                    continue;
                }

                old_CurveScale = CurveScale;
                old_curve = CopyAnimationCurve(VertexCurve);

                m_TextComponent.ForceMeshUpdate(); // Generate the mesh and populate the textInfo with data we can use and manipulate.

                TMP_TextInfo textInfo = m_TextComponent.textInfo;
                int characterCount = textInfo.characterCount;


                if (characterCount == 0) continue;

                //vertices = textInfo.meshInfo[0].vertices;
                //int lastVertexIndex = textInfo.characterInfo[characterCount - 1].vertexIndex;

                float boundsMinX = m_TextComponent.bounds.min.x;  //textInfo.meshInfo[0].mesh.bounds.min.x;
                float boundsMaxX = m_TextComponent.bounds.max.x;  //textInfo.meshInfo[0].mesh.bounds.max.x;



                for (int i = 0; i < characterCount; i++)
                {
                    if (!textInfo.characterInfo[i].isVisible)
                        continue;

                    int vertexIndex = textInfo.characterInfo[i].vertexIndex;

                    // Get the index of the mesh used by this character.
                    int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;

                    vertices = textInfo.meshInfo[materialIndex].vertices;

                    // Compute the baseline mid point for each character
                    Vector3 offsetToMidBaseline = new Vector2((vertices[vertexIndex + 0].x + vertices[vertexIndex + 2].x) / 2, textInfo.characterInfo[i].baseLine);
                    //float offsetY = VertexCurve.Evaluate((float)i / characterCount + loopCount / 50f); // Random.Range(-0.25f, 0.25f);

                    // Apply offset to adjust our pivot point.
                    vertices[vertexIndex + 0] += -offsetToMidBaseline;
                    vertices[vertexIndex + 1] += -offsetToMidBaseline;
                    vertices[vertexIndex + 2] += -offsetToMidBaseline;
                    vertices[vertexIndex + 3] += -offsetToMidBaseline;

                    // Compute the angle of rotation for each character based on the animation curve
                    float x0 = (offsetToMidBaseline.x - boundsMinX) / (boundsMaxX - boundsMinX); // Character's position relative to the bounds of the mesh.
                    float x1 = x0 + 0.0001f;
                    float y0 = VertexCurve.Evaluate(x0) * CurveScale;
                    float y1 = VertexCurve.Evaluate(x1) * CurveScale;

                    Vector3 horizontal = new Vector3(1, 0, 0);
                    //Vector3 normal = new Vector3(-(y1 - y0), (x1 * (boundsMaxX - boundsMinX) + boundsMinX) - offsetToMidBaseline.x, 0);
                    Vector3 tangent = new Vector3(x1 * (boundsMaxX - boundsMinX) + boundsMinX, y1) - new Vector3(offsetToMidBaseline.x, y0);

                    float dot = Mathf.Acos(Vector3.Dot(horizontal, tangent.normalized)) * 57.2957795f;
                    Vector3 cross = Vector3.Cross(horizontal, tangent);
                    float angle = cross.z > 0 ? dot : 360 - dot;

                    matrix = Matrix4x4.TRS(new Vector3(0, y0, 0), Quaternion.Euler(0, 0, angle), Vector3.one);

                    vertices[vertexIndex + 0] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 0]);
                    vertices[vertexIndex + 1] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 1]);
                    vertices[vertexIndex + 2] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 2]);
                    vertices[vertexIndex + 3] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 3]);

                    vertices[vertexIndex + 0] += offsetToMidBaseline;
                    vertices[vertexIndex + 1] += offsetToMidBaseline;
                    vertices[vertexIndex + 2] += offsetToMidBaseline;
                    vertices[vertexIndex + 3] += offsetToMidBaseline;
                }


                // Upload the mesh with the revised information
                m_TextComponent.UpdateVertexData();

                yield return new WaitForSeconds(0.025f);
            }
        }
    }
}

```

## Assets/UIRaycastInspector.cs

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class UIRaycastInspector : MonoBehaviour
{
    void Update()
    {
        if (!EventSystem.current) return;

        var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, results);

        if (results.Count == 0) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder("UI Raycast stack:\n");
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"{i,2}. {GetPath(r.gameObject)} " +
                          $"(Canvas:{r.module?.transform?.name}  SortingLayer:{r.sortingLayer}  Order:{r.sortingOrder})");
        }
        Debug.Log($"pen {sb.ToString()}");
    }

    string GetPath(GameObject go)
    {
        var t = go.transform; var path = go.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
```

