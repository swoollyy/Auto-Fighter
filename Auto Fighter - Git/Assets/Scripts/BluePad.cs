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

    [Header("Speed Uncap After Boost (CHANGED)")]
    [Tooltip("Uncapped ADDITIVE speed amount. Baseline maxSpeed is never mutated. e.g. base 45 + 35 = 80 for duration.")]
    public float uncapAddAmount = 35f;
    public float uncapDuration = .85f;
    [Tooltip("Legacy absolute field (ignored now).")]
    [HideInInspector] public float uncapMaxSpeed = 80f;
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

        // CHANGED: Use additive uncapped speed (baseline untouched)
        ball.TemporarilyUncapMaxSpeedAdd(uncapDuration, uncapAddAmount, restoreOriginal: restoreMaxOnExpire);

        if (enableSlowMo)
            StartSlowMo();

        Pinball.Instance?.ScreenShake();
        MarkApplied(id);

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
        if (_trigger && !_trigger.enabled) _trigger.enabled = true;
        CancelSlowMo();
    }
}