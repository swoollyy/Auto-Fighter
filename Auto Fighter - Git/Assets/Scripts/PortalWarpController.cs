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