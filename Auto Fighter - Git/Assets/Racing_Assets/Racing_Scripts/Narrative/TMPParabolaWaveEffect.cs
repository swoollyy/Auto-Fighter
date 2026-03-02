using UnityEngine;
using TMPro;

/// <summary>
/// Vertex animation: a parabolic "wave" that travels left-to-right over the line (rainbow/arc effect).
/// Applies to the whole line. Uses unscaled time when paused. Run after Jitter (order: Jitter -200, Parabola -100, Zoom 0).
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(TMP_Text))]
public class TMPParabolaWaveEffect : MonoBehaviour
{
    [Header("Wave shape")]
    [SerializeField] private float amplitude = 3f;
    [SerializeField] private float waveHalfWidth = 25f;

    [Header("Motion")]
    [SerializeField] private float speed = 80f;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private bool leftToRight = true;

    private TMP_Text _text;
    private TMP_MeshInfo[] _cachedMeshInfo;
    private bool _textChanged = true;
    private float _textMinX, _textMaxX;
    private float _waveCenter;
    private float _accumulatedTime;

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
    }

    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
    }

    private void OnTextChanged(Object obj)
    {
        if (obj == _text)
            _textChanged = true;
    }

    private void LateUpdate()
    {
        if (_text == null) return;

        TMP_TextInfo textInfo = _text.textInfo;
        int characterCount = textInfo.characterCount;

        if (characterCount == 0) return;

        // When Jitter is present it runs first and writes rest+jitter; we add wave on top. When alone, we reset to rest then add wave.
        bool jitterActive = TryGetComponent<TMPJitterEffect>(out var jitter) && jitter.enabled;
        if (!jitterActive)
        {
            _text.ForceMeshUpdate();
            textInfo = _text.textInfo;
            characterCount = textInfo.characterCount;
            if (characterCount == 0) return;
        }

        if (_textChanged || _cachedMeshInfo == null)
        {
            _text.ForceMeshUpdate();
            textInfo = _text.textInfo;
            characterCount = textInfo.characterCount;
            if (characterCount == 0) return;
            _cachedMeshInfo = textInfo.CopyMeshInfoVertexData();
            ComputeTextBounds(textInfo, _cachedMeshInfo, out _textMinX, out _textMaxX);
            _textChanged = false;
            _accumulatedTime = 0f;
        }

        if (_cachedMeshInfo == null) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float textSpan = _textMaxX - _textMinX;
        float travel = textSpan + 2f * waveHalfWidth;
        if (travel <= 0f) travel = 1f;

        _accumulatedTime += dt * speed;
        float t = Mathf.Repeat(_accumulatedTime, travel);
        _waveCenter = leftToRight
            ? _textMinX - waveHalfWidth + t
            : _textMaxX + waveHalfWidth - t;

        // Scale offset by transform so effect is visible at any canvas scale
        float scale = _text.transform.lossyScale.y;
        if (scale < 0.001f) scale = 1f;
        float effectiveAmplitude = amplitude * scale;

        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Vector3[] sourceVertices = _cachedMeshInfo[materialIndex].vertices;
            Vector3[] destVertices = textInfo.meshInfo[materialIndex].vertices;

            // centerX from rest (cache) so wave position is consistent
            float centerX = (sourceVertices[vertexIndex + 0].x + sourceVertices[vertexIndex + 2].x) * 0.5f;
            float dx = centerX - _waveCenter;
            float normalized = waveHalfWidth > 0.0001f ? (dx / waveHalfWidth) : 0f;
            float factor = Mathf.Clamp01(1f - normalized * normalized);
            float offsetY = effectiveAmplitude * factor;
            Vector3 waveOffset = new Vector3(0, offsetY, 0);

            if (jitterActive)
            {
                // Add wave on top of current mesh (rest + jitter)
                destVertices[vertexIndex + 0] = destVertices[vertexIndex + 0] + waveOffset;
                destVertices[vertexIndex + 1] = destVertices[vertexIndex + 1] + waveOffset;
                destVertices[vertexIndex + 2] = destVertices[vertexIndex + 2] + waveOffset;
                destVertices[vertexIndex + 3] = destVertices[vertexIndex + 3] + waveOffset;
            }
            else
            {
                // Parabola alone: write rest + wave
                destVertices[vertexIndex + 0] = sourceVertices[vertexIndex + 0] + waveOffset;
                destVertices[vertexIndex + 1] = sourceVertices[vertexIndex + 1] + waveOffset;
                destVertices[vertexIndex + 2] = sourceVertices[vertexIndex + 2] + waveOffset;
                destVertices[vertexIndex + 3] = sourceVertices[vertexIndex + 3] + waveOffset;
            }
        }

        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            _text.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }

    private void ComputeTextBounds(TMP_TextInfo textInfo, TMP_MeshInfo[] meshInfo, out float minX, out float maxX)
    {
        minX = float.MaxValue;
        maxX = float.MinValue;
        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;
            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Vector3[] verts = meshInfo[materialIndex].vertices;
            float cx = (verts[vertexIndex + 0].x + verts[vertexIndex + 2].x) * 0.5f;
            if (cx < minX) minX = cx;
            if (cx > maxX) maxX = cx;
        }
        if (minX > maxX) { minX = 0f; maxX = 0f; }
    }
}
