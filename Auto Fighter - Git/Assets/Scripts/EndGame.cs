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