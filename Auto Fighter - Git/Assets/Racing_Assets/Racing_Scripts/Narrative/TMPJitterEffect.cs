using UnityEngine;
using TMPro;

/// <summary>
/// Vertex animation: jitter/shake on the whole text. Always roots letters to rest (cache) so they don't drift.
/// Uses unscaled time when paused. Run before Parabola (order: Jitter -200, Parabola -100, Zoom 0).
/// </summary>
[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(TMP_Text))]
public class TMPJitterEffect : MonoBehaviour
{
    [Header("Jitter")]
    [SerializeField] private float offsetScale = 2f;
    [SerializeField] private float speed = 5f;
    [SerializeField] private bool useUnscaledTime = true;

    private TMP_Text _text;
    private TMP_MeshInfo[] _cachedMeshInfo;
    private bool _textChanged = true;
    private float _time;

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

        if (_textChanged || _cachedMeshInfo == null)
        {
            _text.ForceMeshUpdate();
            textInfo = _text.textInfo;
            characterCount = textInfo.characterCount;
            if (characterCount == 0) return;
            _cachedMeshInfo = textInfo.CopyMeshInfoVertexData();
            _textChanged = false;
        }

        if (_cachedMeshInfo == null) return;

        _time += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        // Scale offset by transform so effect is visible at any canvas scale
        float scale = _text.transform.lossyScale.y;
        if (scale < 0.001f) scale = 1f;
        float effectiveScale = offsetScale * scale;

        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Vector3[] sourceVertices = _cachedMeshInfo[materialIndex].vertices;
            Vector3[] destVertices = textInfo.meshInfo[materialIndex].vertices;

            float px = Mathf.PerlinNoise(i * 0.5f, _time * speed) * 2f - 1f;
            float py = Mathf.PerlinNoise(i * 0.5f + 100f, _time * speed) * 2f - 1f;
            Vector3 jitter = new Vector3(px, py, 0) * effectiveScale;

            // Always root to rest (cache) so letters don't drift; Parabola runs after and adds wave on top
            destVertices[vertexIndex + 0] = sourceVertices[vertexIndex + 0] + jitter;
            destVertices[vertexIndex + 1] = sourceVertices[vertexIndex + 1] + jitter;
            destVertices[vertexIndex + 2] = sourceVertices[vertexIndex + 2] + jitter;
            destVertices[vertexIndex + 3] = sourceVertices[vertexIndex + 3] + jitter;
        }

        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            _text.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }
}
