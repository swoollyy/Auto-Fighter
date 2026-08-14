using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-authored finish portal director. Place via Racing → Setup Finish Portal System.
/// Does not create portal/VFX at runtime — only repositions the authored portal on track gen.
/// </summary>
[DisallowMultipleComponent]
public class FinishPortalDirector : MonoBehaviour
{
    public static FinishPortalDirector Instance { get; private set; }

    [Header("Scene Objects (assign / use Setup menu)")]
    [SerializeField] private FinishPortalGate portal;
    [SerializeField] private FinishHyperTunnelVfx tunnelVfx;

    [Header("References")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;
    [SerializeField] private TrackDistanceMeter distanceMeter;
    [SerializeField] private GameManager_Racing gameManager;
    [SerializeField] private UIManager_Racing uiManager;
    [SerializeField] private CameraFollow cameraFollow;
    [SerializeField] private ForcefieldPostFXController postFx;

    [Header("Portal Placement")]
    [SerializeField] private bool placePortalOnTrackGenerated = true;
    [Tooltip("TEST: place portal early for debugging. OFF = place near track end (normal play).")]
    [SerializeField] private bool testPlacePortalEarly = false;
    [SerializeField, Range(0.01f, 1f)] private float testPortalNormalized = 0.03f;
    [Tooltip("Where the end portal sits on the path (1.0 is often past driveable road — keep slightly under).")]
    [SerializeField, Range(0.85f, 1f)] private float endPortalNormalized = 0.97f;
    [Tooltip("Extra trigger length along the approach so the car cannot miss a thin end gate.")]
    [SerializeField, Min(1f)] private float endPortalApproachCatchMeters = 14f;
    [Tooltip("Only used when Test Place Portal Early is ON (anti-cheat for the early gate).")]
    [SerializeField, Range(0f, 1f)] private float minProgressToAccept = 0.02f;
    [Tooltip("TEST early-portal only: also fire from distance meter at Auto Trigger Progress.")]
    [SerializeField] private bool enableAutoProgressTrigger = false;
    [SerializeField, Range(0f, 1f)] private float autoTriggerProgress = 0.035f;
    [Tooltip("Normal play failsafe: start finish when distance meter reaches this (even if trigger is missed).")]
    [SerializeField, Range(0.9f, 1f)] private float endProgressFailsafe = 0.985f;
    [Tooltip("Also start finish when the active car is this close to the portal (meters).")]
    [SerializeField, Min(1f)] private float portalProximityFailsafeMeters = 10f;

    [Header("Sequence Timing")]
    [Tooltip("Total time from entry until blackout (should be >= fade delay + fade in). Spark plays once black fully covers.")]
    [SerializeField, Min(0.5f)] private float preResultsTunnelSeconds = 4.5f;
    [Tooltip("Hold with world still visible (FOV can start) before the void begins covering.")]
    [SerializeField, Min(0f)] private float environmentFadeDelaySeconds = 1.25f;
    [Tooltip("Void transparent → fully opaque after the delay.")]
    [SerializeField, Min(0.1f)] private float environmentFadeInSeconds = 2.4f;
    [SerializeField, Min(0.1f)] private float portalExitDuration = 1.1f;
    [SerializeField, Range(60f, 179f)] private float tunnelFov = 148f;
    [SerializeField, Min(0.05f)] private float fovRampIn = 2.2f;
    [SerializeField, Min(0f)] private float postFxBloomHold = 0.9f;
    [Header("Portal Volume Punch")]
    [SerializeField] private bool fxPortalVolumePunch = true;
    [Tooltip("When on, bloom/saturation ease in at double speed vs the void engulf window (full kick at the midpoint).")]
    [SerializeField] private bool portalVolumePunchSyncToVoidEngulf = true;
    [Tooltip("Only used when Sync To Void Engulf is off.")]
    [SerializeField, Min(0f)] private float portalVolumePunchDelaySeconds = 0.2f;
    [Tooltip("Only used when Sync To Void Engulf is off.")]
    [SerializeField, Min(0.01f)] private float portalVolumePunchFadeInSeconds = 0.4f;
    [SerializeField, Range(0f, 800f)] private float portalBloomIntensity = 80f;
    [Tooltip("Color Adjustments saturation (-100..100).")]
    [SerializeField, Range(-100f, 100f)] private float portalSaturation = 60f;
    [Tooltip("Color Adjustments hue shift min (-180..180). Scrolls between min and max.")]
    [SerializeField, Range(-180f, 180f)] private float portalHueShiftMin = -55f;
    [Tooltip("Color Adjustments hue shift max (-180..180).")]
    [SerializeField, Range(-180f, 180f)] private float portalHueShiftMax = 55f;
    [Tooltip("How fast hue scrolls min↔max (full cycles per second).")]
    [SerializeField, Min(0f)] private float portalHueScrollSpeed = 2.75f;
    [Tooltip("Main directional light to brighten during the portal punch (auto-finds RenderSettings.sun if empty).")]
    [SerializeField] private Light portalDirectionalLight;
    [Tooltip("Peak directional light intensity during the portal sequence.")]
    [SerializeField, Min(0f)] private float portalDirectionalLightIntensity = 3.5f;
    [SerializeField] private bool hideGameplayUiDuringIntro = true;
    [SerializeField, Min(0f)] private float hideHudAfterSeconds = 1.5f;
    [SerializeField] private bool lockCarInputDuringSequence = true;
    [SerializeField] private bool pullCarForwardDuringTunnel = true;
    [SerializeField, Min(0f)] private float pullAcceleration = 55f;
    [SerializeField, Min(0f)] private float persistentShakeStrength = 0.35f;
    [Tooltip("0 = start shake as soon as the tunnel begins; 1 = only after void fade completes.")]
    [SerializeField, Range(0f, 1f)] private float shakeStartNormalizedThroughFade = 0.05f;
    [SerializeField, Min(1)] private int persistentShakeVibrato = 28;
    [SerializeField, Range(0f, 180f)] private float persistentShakeRandomness = 120f;

    [Header("Sequence FX Toggles (debug — uncheck to isolate glitches)")]
    [SerializeField] private bool fxPortalExitAnim = true;
    [SerializeField] private bool fxForcedFov = true;
    [SerializeField] private bool fxPostChromatic = true;
    [SerializeField] private bool fxPostLens = true;
    [SerializeField] private bool fxPostBloom = true;
    [SerializeField] private bool fxHyperTunnelUi = true;
    [SerializeField] private bool fxFinalBlackout = true;
    [SerializeField] private bool fxPersistentShake = true;
    [SerializeField] private bool fxCarForwardPull = true;
    [SerializeField] private bool fxHideGameplayHud = true;

    private Coroutine _sequenceCr;
    private Coroutine _portalPunchCr;
    private bool _sequenceActive;
    private bool _portalConsumed;
    private bool _portalLightBoostActive;
    private float _portalLightBaseIntensity = 1f;
    private ProceduralTrackGenerator _wiredGenerator;
    private readonly List<Vector3> _pathScratch = new List<Vector3>(256);
    private float[] _cumScratch;

    public bool IsSequenceActive => _sequenceActive;
    public FinishPortalGate Portal => portal;
    public FinishHyperTunnelVfx TunnelVfx => tunnelVfx;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        ResolveRefs();
        if (portal != null)
            portal.Initialize(this);
        TryWireTrackGenerator();
    }
  
    private void OnEnable()
    {
        ResolveRefs();
        TryWireTrackGenerator();
    }

    private void OnDisable()
    {
        UnwireTrackGenerator();
        RestorePortalDirectionalLight();
        if (postFx != null)
            postFx.ResetAllEffectsImmediate();
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        UpdatePortalDirectionalLight();
    }

    private void TryWireTrackGenerator()
    {
        ResolveRefs();
        if (trackGenerator == null || _wiredGenerator == trackGenerator)
            return;

        UnwireTrackGenerator();
        trackGenerator.OnTrackGeneratedSuccessfully += HandleTrackGenerated;
        _wiredGenerator = trackGenerator;
    }

    private void UnwireTrackGenerator()
    {
        if (_wiredGenerator != null)
        {
            _wiredGenerator.OnTrackGeneratedSuccessfully -= HandleTrackGenerated;
            _wiredGenerator = null;
        }
    }

    private void Update()
    {
        if (_wiredGenerator == null)
            TryWireTrackGenerator();

        if (_sequenceActive || _portalConsumed) return;
        ResolveRefs();
        if (gameManager == null || !gameManager.IsGameplayLive) return;

        // If track-gen event was missed, ensure the end portal exists while a run is live.
        if (!testPlacePortalEarly
            && placePortalOnTrackGenerated
            && portal != null
            && !portal.gameObject.activeInHierarchy
            && trackGenerator != null)
        {
            PlacePortal();
        }

        // Early-test optional meter trigger.
        if (testPlacePortalEarly && enableAutoProgressTrigger && distanceMeter != null
            && distanceMeter.Normalized >= autoTriggerProgress)
        {
            BeginFinishSequence("auto-progress");
            return;
        }

        // Normal end-of-track failsafes (thin/missed trigger, portal past cliff, etc.).
        if (!testPlacePortalEarly)
        {
            if (distanceMeter != null && distanceMeter.Normalized >= endProgressFailsafe)
            {
                BeginFinishSequence("end-progress-failsafe");
                return;
            }

            if (TryProximityFinish())
                return;
        }
    }

    private bool TryProximityFinish()
    {
        if (portal == null || !portal.gameObject.activeInHierarchy) return false;
        if (portalProximityFailsafeMeters <= 0f) return false;

        var car = gameManager != null ? gameManager.ActiveCar : null;
        if (car == null) return false;

        float dist = Vector3.Distance(car.transform.position, portal.transform.position);
        if (dist > portalProximityFailsafeMeters) return false;

        BeginFinishSequence("portal-proximity");
        return true;
    }

    private void ResolveRefs()
    {
        if (portal == null)
            portal = GetComponentInChildren<FinishPortalGate>(true);
        if (tunnelVfx == null)
            tunnelVfx = GetComponentInChildren<FinishHyperTunnelVfx>(true);
        if (trackGenerator == null)
            trackGenerator = FindObjectOfType<ProceduralTrackGenerator>(true);
        if (distanceMeter == null)
            distanceMeter = FindObjectOfType<TrackDistanceMeter>(true);
        if (gameManager == null)
            gameManager = GameManager_Racing.Instance != null
                ? GameManager_Racing.Instance
                : FindObjectOfType<GameManager_Racing>(true);
        if (uiManager == null)
            uiManager = FindObjectOfType<UIManager_Racing>(true);
        if (cameraFollow == null)
            cameraFollow = FindObjectOfType<CameraFollow>(true);
        if (postFx == null)
            postFx = FindObjectOfType<ForcefieldPostFXController>(true);
        if (portalDirectionalLight == null)
            portalDirectionalLight = ResolveMainDirectionalLight();
    }

    private static Light ResolveMainDirectionalLight()
    {
        if (RenderSettings.sun != null)
            return RenderSettings.sun;

        var lights = FindObjectsOfType<Light>(true);
        Light best = null;
        for (int i = 0; i < lights.Length; i++)
        {
            var l = lights[i];
            if (l == null || l.type != LightType.Directional || !l.enabled)
                continue;
            if (best == null || l.intensity > best.intensity)
                best = l;
        }
        return best;
    }

    private void BeginPortalDirectionalLightBoost()
    {
        if (portalDirectionalLight == null)
            portalDirectionalLight = ResolveMainDirectionalLight();
        if (portalDirectionalLight == null)
            return;

        if (!_portalLightBoostActive)
            _portalLightBaseIntensity = portalDirectionalLight.intensity;
        _portalLightBoostActive = true;
        UpdatePortalDirectionalLight();
    }

    private void UpdatePortalDirectionalLight()
    {
        if (!_portalLightBoostActive || portalDirectionalLight == null)
            return;

        float w = postFx != null ? Mathf.Clamp01(postFx.PortalHoldWeight) : 0f;
        portalDirectionalLight.intensity = Mathf.Lerp(
            _portalLightBaseIntensity,
            Mathf.Max(_portalLightBaseIntensity, portalDirectionalLightIntensity),
            w);
    }

    private void RestorePortalDirectionalLight()
    {
        if (_portalLightBoostActive && portalDirectionalLight != null)
            portalDirectionalLight.intensity = _portalLightBaseIntensity;
        _portalLightBoostActive = false;
    }

    private void HandleTrackGenerated(ProceduralTrackGenerator gen)
    {
        trackGenerator = gen;
        _portalConsumed = false;
        _sequenceActive = false;
        if (_sequenceCr != null)
        {
            StopCoroutine(_sequenceCr);
            _sequenceCr = null;
        }

        StopPresentation();

        if (!placePortalOnTrackGenerated) return;
        PlacePortal();
    }

    private void PlacePortal()
    {
        if (trackGenerator == null) return;
        if (portal == null)
        {
            Debug.LogError("[FinishPortalDirector] No FinishPortalGate assigned. Run Racing → Setup Finish Portal System.");
            return;
        }

        // End portal sits slightly before path tip so the trigger is still on driveable road.
        float norm = testPlacePortalEarly ? testPortalNormalized : Mathf.Clamp(endPortalNormalized, 0.85f, 1f);
        if (!TryGetPointAtNormalized(norm, out Vector3 pos, out Vector3 fwd))
            trackGenerator.GetEndPoint(out pos, out fwd);

        float approachCatch = testPlacePortalEarly ? -1f : endPortalApproachCatchMeters;
        portal.Initialize(this);
        portal.PlaceAtTrackEnd(pos, fwd, trackGenerator.RoadWidth, approachCatch);
        Debug.Log(
            $"[FinishPortalDirector] Portal placed at normalized={norm:F3} " +
            $"(testEarly={testPlacePortalEarly}, catch={approachCatch:F1}m) pos={pos}.");
    }

    private bool TryGetPointAtNormalized(float normalized, out Vector3 pos, out Vector3 fwd)
    {
        pos = Vector3.zero;
        fwd = Vector3.forward;
        if (trackGenerator == null) return false;
        if (!TrackPathSampling.RebuildPathFromRoadCenterline(trackGenerator, _pathScratch, ref _cumScratch, out float total)
            || total <= 1e-4f)
            return false;

        float dist = Mathf.Clamp01(normalized) * total;
        TrackPathSampling.SampleAlongPath(_pathScratch, _cumScratch, total, dist, out pos, out fwd);
        return true;
    }

    public void NotifyPortalEntered(FinishPortalGate gate)
    {
        if (_sequenceActive || _portalConsumed) return;
        if (gameManager == null)
        {
            Debug.LogWarning("[FinishPortalDirector] Portal entered but GameManager is null.");
            return;
        }
        if (!gameManager.IsGameplayLive)
        {
            Debug.LogWarning(
                "[FinishPortalDirector] Portal entered but gameplay is not live " +
                $"(runStarted/loading/ended). Ignoring.");
            return;
        }

        // Early test portal: require nearby progress so you can't trigger it from a wrong spawn.
        // End portal: trust the physical gate.
        if (testPlacePortalEarly)
        {
            float progress = distanceMeter != null ? distanceMeter.Normalized : 1f;
            float acceptFloor = Mathf.Max(0f, testPortalNormalized - 0.025f);
            acceptFloor = Mathf.Max(acceptFloor, minProgressToAccept);
            if (progress < acceptFloor)
            {
                Debug.LogWarning(
                    $"[FinishPortalDirector] Portal touch ignored — progress {progress:P0} < {acceptFloor:P0} " +
                    "(early test gate). Will retry via OnTriggerStay after rearm.");
                if (gate != null)
                    StartCoroutine(CoRearmPortal(gate, 0.35f));
                return;
            }
        }

        BeginFinishSequence("portal");
    }

    private IEnumerator CoRearmPortal(FinishPortalGate gate, float delay)
    {
        if (gate != null)
            gate.Disarm();
        yield return new WaitForSeconds(delay);
        if (!_sequenceActive && !_portalConsumed && gate != null)
            gate.Rearm();
    }

    private void BeginFinishSequence(string reason)
    {
        if (_sequenceActive || _portalConsumed) return;
        if (gameManager != null && !gameManager.TryBeginFinishPortalSequence())
        {
            Debug.LogWarning(
                $"[FinishPortalDirector] TryBeginFinishPortalSequence failed ({reason}). " +
                "Run may already be ending / not started.");
            return;
        }

        if (tunnelVfx == null)
        {
            Debug.LogError("[FinishPortalDirector] No FinishHyperTunnelVfx assigned. Run Racing → Setup Finish Portal System.");
            gameManager?.CompleteRunFromFinishPortal(keepTunnelPresentation: false);
            return;
        }

        _portalConsumed = true;
        _sequenceActive = true;
        if (portal != null)
            portal.Disarm();

        Debug.Log($"[FinishPortalDirector] Finish sequence started ({reason}).");
        if (_sequenceCr != null) StopCoroutine(_sequenceCr);
        _sequenceCr = StartCoroutine(CoFinishSequence());
    }

    private IEnumerator CoFinishSequence()
    {
        var car = gameManager != null ? gameManager.ActiveCar : null;
        if (lockCarInputDuringSequence && car != null)
            car.SetExternalInputLock(true);

        // Immune to crashes for the whole finish sequence — obstacles get flung instead.
        if (car != null)
            car.SetFinishPortalCrashShield(true);

        if (fxPortalExitAnim && portal != null)
            portal.PlayExitAnimation(portalExitDuration);

        float fadeDelay = Mathf.Max(0f, environmentFadeDelaySeconds);
        float fadeIn = Mathf.Max(0.1f, environmentFadeInSeconds);
        float fovTime = Mathf.Max(0.05f, fovRampIn);

        if (fxForcedFov && cameraFollow != null)
            cameraFollow.BeginForcedFov(tunnelFov, fovTime);

        // Short chroma/lens/bloom burst only when the sustained portal punch is off —
        // otherwise that burst fades out mid-engulf and looks like post FX "popping off".
        bool anyPostFx = fxPostChromatic || fxPostLens || fxPostBloom;
        if (anyPostFx && postFx != null && !fxPortalVolumePunch)
        {
            float chroma = fxPostChromatic ? 0.25f : 0f;
            float lens = fxPostLens ? 0.15f : 0f;
            postFx.PlayBurstCustom(
                chroma,
                lens,
                postFxBloomHold,
                0.1f,
                0.6f,
                allowWobble: false,
                includeBloom: fxPostBloom,
                lensScaleOverride: 1f);
        }

        float t0 = Time.unscaledTime;
        float minUntilBlackout = fadeDelay + fadeIn + 0.15f;
        float tunnelHold = Mathf.Max(preResultsTunnelSeconds, minUntilBlackout);
        float t1 = t0 + tunnelHold;

        if (fxHyperTunnelUi && tunnelVfx != null)
            tunnelVfx.PlayPersistent(null, fadeIn, fadeDelay, expectedDriveSeconds: tunnelHold);

        if (fxPortalVolumePunch && postFx != null)
        {
            if (_portalPunchCr != null)
                StopCoroutine(_portalPunchCr);
            // Double-time vs void engulf: full kick by the midpoint of the rings/void takeover.
            float punchDelay = portalVolumePunchSyncToVoidEngulf ? 0f : Mathf.Max(0f, portalVolumePunchDelaySeconds);
            float engulfWindow = Mathf.Max(0.1f, fadeDelay + fadeIn);
            float punchFade = portalVolumePunchSyncToVoidEngulf
                ? Mathf.Max(0.05f, engulfWindow * 0.5f)
                : Mathf.Max(0.01f, portalVolumePunchFadeInSeconds);
            _portalPunchCr = StartCoroutine(CoPortalVolumePunch(punchDelay, punchFade));
        }

        float hudHideAt = t0 + hideHudAfterSeconds;
        bool hudHidden = false;

        bool shakeStarted = false;
        float shakeAt = t0 + fadeDelay + fadeIn * Mathf.Clamp01(shakeStartNormalizedThroughFade);

        Rigidbody rb = car != null ? car.GetComponent<Rigidbody>() : null;
        Vector3 pullDir = car != null ? car.transform.forward : Vector3.forward;
        pullDir.y = 0f;
        if (pullDir.sqrMagnitude < 1e-4f) pullDir = Vector3.forward;
        pullDir.Normalize();

        while (Time.unscaledTime < t1)
        {
            if (!hudHidden && fxHideGameplayHud && hideGameplayUiDuringIntro && Time.unscaledTime >= hudHideAt)
            {
                uiManager?.SetGameCanvasVisible(false);
                hudHidden = true;
            }

            if (fxPersistentShake && !shakeStarted && cameraFollow != null && Time.unscaledTime >= shakeAt)
            {
                cameraFollow.BeginPersistentShake(persistentShakeStrength, persistentShakeVibrato, persistentShakeRandomness);
                shakeStarted = true;
            }

            if (fxCarForwardPull && pullCarForwardDuringTunnel && rb != null && !Mathf.Approximately(Time.timeScale, 0f))
            {
                Vector3 horiz = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
                float target = Mathf.Max(horiz.magnitude, 28f);
                Vector3 desired = pullDir * target;
                Vector3 delta = desired - horiz;
                rb.AddForce(new Vector3(delta.x, 0f, delta.z) + pullDir * pullAcceleration, ForceMode.Acceleration);
            }
            yield return null;
        }

        if (!hudHidden && fxHideGameplayHud && hideGameplayUiDuringIntro)
            uiManager?.SetGameCanvasVisible(false);
        if (fxPersistentShake && !shakeStarted && cameraFollow != null)
            cameraFollow.BeginPersistentShake(persistentShakeStrength, persistentShakeVibrato, persistentShakeRandomness);

        // Final black circle engulfs the tunnel (spark fires as it expands), then results.
        if (fxHyperTunnelUi && fxFinalBlackout && tunnelVfx != null)
            yield return tunnelVfx.StartCoroutine(tunnelVfx.CoFinalBlackout());

        if (fxHyperTunnelUi && tunnelVfx != null)
            tunnelVfx.SetResultsFriendlyOverlay(true);
        gameManager?.CompleteRunFromFinishPortal(keepTunnelPresentation: true);

        if (car != null)
            car.SetFinishPortalCrashShield(false);

        _sequenceActive = false;
        _sequenceCr = null;
    }

    private IEnumerator CoPortalVolumePunch(float delaySeconds, float fadeInSeconds)
    {
        float delay = Mathf.Max(0f, delaySeconds);
        if (delay > 0f)
        {
            float until = Time.unscaledTime + delay;
            while (Time.unscaledTime < until)
                yield return null;
        }

        _portalPunchCr = null;
        if (postFx == null)
            yield break;

        BeginPortalDirectionalLightBoost();
        postFx.BeginPortalDriveHold(
            portalBloomIntensity,
            portalSaturation,
            Mathf.Max(0.01f, fadeInSeconds),
            portalHueShiftMin,
            portalHueShiftMax,
            portalHueScrollSpeed);
    }

    public void StopPresentation()
    {
        if (_sequenceCr != null)
        {
            StopCoroutine(_sequenceCr);
            _sequenceCr = null;
        }
        if (_portalPunchCr != null)
        {
            StopCoroutine(_portalPunchCr);
            _portalPunchCr = null;
        }
        _sequenceActive = false;

        var car = gameManager != null ? gameManager.ActiveCar : null;
        if (car != null)
            car.SetFinishPortalCrashShield(false);

        if (tunnelVfx != null)
            tunnelVfx.Stop();

        if (cameraFollow != null)
        {
            cameraFollow.EndPersistentShake();
            cameraFollow.ClearForcedFovImmediate();
        }

        RestorePortalDirectionalLight();

        // Never leave bloom/saturation punched after portal teardown / quit mid-run / replay.
        if (postFx != null)
            postFx.ResetAllEffectsImmediate();
    }

    private void OnDestroy()
    {
        RestorePortalDirectionalLight();
        if (postFx != null)
            postFx.ResetAllEffectsImmediate();
        if (Instance == this)
            Instance = null;
    }
}
