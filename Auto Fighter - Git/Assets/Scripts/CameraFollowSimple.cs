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