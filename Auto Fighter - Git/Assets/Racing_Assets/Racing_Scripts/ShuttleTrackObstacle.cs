using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class ShuttleTrackObstacle : MonoBehaviour
{
    [Header("Track Width")]
    [Tooltip("If > 0, use this value instead of ProceduralTrackGenerator.RoadWidth.")]
    [SerializeField] private float overrideRoadWidth = 0f;
    [Tooltip("Extra safety inset from each road edge.")]
    [SerializeField] private float edgeMargin = 0.35f;

    [Header("Obstacle Size (optional)")]
    [Tooltip("If true, tries to estimate half-width from renderers. Otherwise uses manualHalfWidth.")]
    [SerializeField] private bool autoHalfWidthFromRenderer = true;
    [SerializeField] private float manualHalfWidth = 0.5f;

    [Header("Motion")]
    [SerializeField] private float speed = 5f;
    [Tooltip("Start from left bound heading right (if false, starts from right heading left).")]
    [SerializeField] private bool startOnLeft = true;
    [Tooltip("Wait at each end before reversing direction.")]
    [SerializeField] private float waitAtEndSeconds = 0.25f;

    [Header("Track Binding (optional)")]
    [SerializeField] private ProceduralTrackGenerator trackGenerator;

    // runtime
    private Vector3 _originWS;
    private Vector3 _leftWS, _rightWS;
    private Vector3 _targetWS;
    private bool _waiting;
    private float _halfRoad, _selfHalf;

    public void SetGenerator(ProceduralTrackGenerator gen) => trackGenerator = gen;

    private void Awake()
    {
        if (!trackGenerator)
            trackGenerator = FindObjectOfType<ProceduralTrackGenerator>();
    }

    private void Start()
    {
        _originWS = transform.position;

        _halfRoad = DetermineHalfRoadWidth();
        _selfHalf = DetermineSelfHalfWidth();
        ComputeEdgeWorldPositions(out _leftWS, out _rightWS);

        // Place at start and set initial target
        float usableLeft = -(_halfRoad - edgeMargin - _selfHalf);
        float usableRight = +(_halfRoad - edgeMargin - _selfHalf);
        Vector3 right = transform.right;

        Vector3 startWS = _originWS + right * (startOnLeft ? usableLeft : usableRight);
        _targetWS = _originWS + right * (startOnLeft ? usableRight : usableLeft);

        transform.position = startWS;
    }

    private void Update()
    {
        if (_waiting) return;

        float step = Mathf.Max(0.01f, speed) * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, _targetWS, step);

        if ((transform.position - _targetWS).sqrMagnitude <= 0.0001f)
        {
            // Flip target (ping-pong)
            _targetWS = (_targetWS == _leftWS) ? _rightWS : _leftWS;
            if (waitAtEndSeconds > 0f)
                StartCoroutine(WaitThenResume(waitAtEndSeconds));
        }
    }

    private IEnumerator WaitThenResume(float seconds)
    {
        _waiting = true;
        yield return new WaitForSeconds(seconds);
        _waiting = false;
    }

    private float DetermineHalfRoadWidth()
    {
        float roadWidth = (overrideRoadWidth > 0f)
            ? overrideRoadWidth
            : (trackGenerator ? trackGenerator.RoadWidth : 8f); // fallback
        return Mathf.Max(0.1f, roadWidth) * 0.5f;
    }

    private float DetermineSelfHalfWidth()
    {
        if (!autoHalfWidthFromRenderer)
            return Mathf.Max(0f, manualHalfWidth);

        float approx = 0f;
        var rends = GetComponentsInChildren<Renderer>();
        if (rends != null && rends.Length > 0)
        {
            Vector3 r = transform.right;
            Bounds wb = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) wb.Encapsulate(rends[i].bounds);
            float widthAlongRight = Vector3.Project(wb.size, r).magnitude;
            approx = widthAlongRight * 0.5f;
        }
        return Mathf.Max(0f, approx);
    }

    private void ComputeEdgeWorldPositions(out Vector3 leftWS, out Vector3 rightWS)
    {
        Vector3 right = transform.right;
        float usableLeft = -(_halfRoad - edgeMargin - _selfHalf);
        float usableRight = +(_halfRoad - edgeMargin - _selfHalf);
        leftWS = _originWS + right * usableLeft;
        rightWS = _originWS + right * usableRight;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_leftWS + Vector3.up * 0.05f, 0.12f);
            Gizmos.DrawWireSphere(_rightWS + Vector3.up * 0.05f, 0.12f);
            Gizmos.DrawLine(_leftWS + Vector3.up * 0.05f, _rightWS + Vector3.up * 0.05f);
        }
        else
        {
            var gen = trackGenerator ? trackGenerator : FindObjectOfType<ProceduralTrackGenerator>();
            float halfRoad = (overrideRoadWidth > 0f ? overrideRoadWidth : (gen ? gen.RoadWidth : 8f)) * 0.5f;
            float selfHalf = autoHalfWidthFromRenderer ? 0.3f : Mathf.Max(0f, manualHalfWidth);
            float usableLeft = -(halfRoad - edgeMargin - selfHalf);
            float usableRight = +(halfRoad - edgeMargin - selfHalf);

            Vector3 origin = transform.position;
            Vector3 right = transform.right;
            Vector3 l = origin + right * usableLeft;
            Vector3 r = origin + right * usableRight;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(l + Vector3.up * 0.05f, r + Vector3.up * 0.05f);
        }
    }
#endif
}