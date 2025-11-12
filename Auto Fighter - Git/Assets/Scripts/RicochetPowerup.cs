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