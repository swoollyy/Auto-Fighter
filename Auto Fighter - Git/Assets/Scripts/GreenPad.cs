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