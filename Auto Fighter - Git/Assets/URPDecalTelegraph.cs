using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class URPDecalTelegraph : MonoBehaviour
{
    [SerializeField] private DecalProjector projector;

    [Tooltip("Vertical thickness of the projection volume (Y of DecalProjector.size).")]
    [SerializeField, Min(0.01f)] private float projectionHeight = 2.0f;

    [Tooltip("Tiny lift to avoid z-fighting on perfectly flat surfaces.")]
    [SerializeField] private float yOffset = 0.02f;

    private Coroutine _co;

    private void Reset()
    {
        projector = GetComponent<DecalProjector>();
    }

    public void SetWorldPose(Vector3 worldPos)
    {
        transform.position = worldPos + Vector3.up * yOffset;
        transform.rotation = Quaternion.Euler(90f, 0f, 0f); // ALWAYS forced
    }

    public void Play(float radius, float seconds, Action onComplete)
    {
        if (projector == null) projector = GetComponent<DecalProjector>();
        if (projector == null)
        {
            onComplete?.Invoke();
            return;
        }

        float diameter = Mathf.Max(0.01f, radius * 2f);

        // URP DecalProjector uses size (X=width, Y=height, Z=projection depth)
        projector.size = new Vector3(diameter, Mathf.Max(0.01f, projectionHeight), diameter);

        // Make sure it renders
        projector.enabled = true;
        gameObject.SetActive(true);

        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(Life(seconds, onComplete));
    }

    private IEnumerator Life(float seconds, Action onComplete)
    {
        if (seconds <= 0f)
        {
            onComplete?.Invoke();
            yield break;
        }

        yield return new WaitForSeconds(seconds);
        onComplete?.Invoke();
    }
}
