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
    [Tooltip("TEST: place portal early. Off = track end (1.0).")]
    [SerializeField] private bool testPlacePortalEarly = true;
    [SerializeField, Range(0.01f, 1f)] private float testPortalNormalized = 0.03f;
    [SerializeField, Range(0f, 1f)] private float minProgressToAccept = 0.02f;
    [SerializeField] private bool enableAutoProgressTrigger = false;
    [SerializeField, Range(0f, 1f)] private float autoTriggerProgress = 0.035f;

    [Header("Sequence Timing")]
    [Tooltip("Total time from entry until results (should be > fade delay + fade in).")]
    [SerializeField, Min(0.5f)] private float preResultsTunnelSeconds = 4.5f;
    [Tooltip("Hold with world still visible (FOV can start) before the void begins covering.")]
    [SerializeField, Min(0f)] private float environmentFadeDelaySeconds = 1.25f;
    [Tooltip("Void transparent → fully opaque after the delay.")]
    [SerializeField, Min(0.1f)] private float environmentFadeInSeconds = 2.4f;
    [SerializeField, Min(0.1f)] private float portalExitDuration = 1.1f;
    [SerializeField, Min(60f)] private float tunnelFov = 148f;
    [SerializeField, Min(0.05f)] private float fovRampIn = 2.2f;
    [SerializeField, Min(0f)] private float postFxBloomHold = 0.9f;
    [SerializeField] private bool hideGameplayUiDuringIntro = true;
    [SerializeField, Min(0f)] private float hideHudAfterSeconds = 1.5f;
    [SerializeField] private bool lockCarInputDuringSequence = true;
    [SerializeField] private bool pullCarForwardDuringTunnel = true;
    [SerializeField, Min(0f)] private float pullAcceleration = 55f;
    [SerializeField, Min(0f)] private float persistentShakeStrength = 0.22f;
    [SerializeField, Range(0f, 1f)] private float shakeStartNormalizedThroughFade = 0.55f;

    private Coroutine _sequenceCr;
    private bool _sequenceActive;
    private bool _portalConsumed;
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
        if (Instance == this)
            Instance = null;
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

        if (!enableAutoProgressTrigger) return;
        if (_sequenceActive || _portalConsumed) return;
        if (gameManager == null || !gameManager.IsGameplayLive) return;
        if (distanceMeter == null) return;
        if (distanceMeter.Normalized < autoTriggerProgress) return;
        BeginFinishSequence("auto-progress");
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

        float norm = testPlacePortalEarly ? testPortalNormalized : 1f;
        if (!TryGetPointAtNormalized(norm, out Vector3 pos, out Vector3 fwd))
            trackGenerator.GetEndPoint(out pos, out fwd);

        portal.Initialize(this);
        portal.PlaceAtTrackEnd(pos, fwd, trackGenerator.RoadWidth);
        Debug.Log($"[FinishPortalDirector] Portal placed at normalized={norm:F3} (testEarly={testPlacePortalEarly}).");
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
        if (gameManager == null || !gameManager.IsGameplayLive) return;

        float progress = distanceMeter != null ? distanceMeter.Normalized : 1f;
        float acceptFloor = testPlacePortalEarly
            ? Mathf.Max(0f, testPortalNormalized - 0.025f)
            : Mathf.Max(0.85f, minProgressToAccept);

        if (progress < acceptFloor)
        {
            if (gate != null)
                StartCoroutine(CoRearmPortal(gate, 0.35f));
            return;
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
            return;

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

        if (portal != null)
            portal.PlayExitAnimation(portalExitDuration);

        float fadeDelay = Mathf.Max(0f, environmentFadeDelaySeconds);
        float fadeIn = Mathf.Max(0.1f, environmentFadeInSeconds);
        float fovTime = Mathf.Max(0.05f, fovRampIn);

        if (cameraFollow != null)
            cameraFollow.BeginForcedFov(tunnelFov, fovTime);

        if (postFx != null)
            postFx.PlayBurstCustom(0.25f, 0.15f, postFxBloomHold, 0.1f, 0.6f);

        tunnelVfx.PlayPersistent(null, fadeIn, fadeDelay);

        float t0 = Time.unscaledTime;
        float minUntilResults = fadeDelay + fadeIn + 0.5f;
        float t1 = t0 + Mathf.Max(preResultsTunnelSeconds, minUntilResults);
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
            if (!hudHidden && hideGameplayUiDuringIntro && Time.unscaledTime >= hudHideAt)
            {
                uiManager?.SetGameCanvasVisible(false);
                hudHidden = true;
            }

            if (!shakeStarted && cameraFollow != null && Time.unscaledTime >= shakeAt)
            {
                cameraFollow.BeginPersistentShake(persistentShakeStrength, 22, 100f);
                shakeStarted = true;
            }

            if (pullCarForwardDuringTunnel && rb != null && !Mathf.Approximately(Time.timeScale, 0f))
            {
                Vector3 horiz = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
                float target = Mathf.Max(horiz.magnitude, 28f);
                Vector3 desired = pullDir * target;
                Vector3 delta = desired - horiz;
                rb.AddForce(new Vector3(delta.x, 0f, delta.z) + pullDir * pullAcceleration, ForceMode.Acceleration);
            }
            yield return null;
        }

        if (!hudHidden && hideGameplayUiDuringIntro)
            uiManager?.SetGameCanvasVisible(false);
        if (!shakeStarted && cameraFollow != null)
            cameraFollow.BeginPersistentShake(persistentShakeStrength, 22, 100f);

        tunnelVfx.SetResultsFriendlyOverlay(true);
        gameManager?.CompleteRunFromFinishPortal(keepTunnelPresentation: true);

        _sequenceActive = false;
        _sequenceCr = null;
    }

    public void StopPresentation()
    {
        if (_sequenceCr != null)
        {
            StopCoroutine(_sequenceCr);
            _sequenceCr = null;
        }
        _sequenceActive = false;

        if (tunnelVfx != null)
            tunnelVfx.Stop();

        if (cameraFollow != null)
        {
            cameraFollow.EndPersistentShake();
            cameraFollow.ClearForcedFovImmediate();
        }
    }
}
