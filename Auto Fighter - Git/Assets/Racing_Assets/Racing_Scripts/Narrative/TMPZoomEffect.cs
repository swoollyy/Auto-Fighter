using UnityEngine;
using TMPro;

/// <summary>
/// Vertex animation: shrink/enlarge (pulse) only on words wrapped in &lt;link="pop"&gt;...&lt;/link&gt; (or the tag you set).
/// Leave Link Tag empty to pulse the whole text. Uses unscaled time so it runs when game is paused.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class TMPZoomEffect : MonoBehaviour
{
    [Header("Which words get this effect")]
    [Tooltip("Link tag in your text, e.g. pop. Only characters inside <link=\"pop\">word</link> scale. Empty = whole line.")]
    [SerializeField] private string linkTag = "pop";

    [Header("Scale / pulse")]
    [Tooltip("Scale at rest (1 = normal).")]
    [SerializeField] private float baseScale = 1f;
    [Tooltip("How much to scale up at peak (add to base). e.g. 0.3 = 1.3x at peak.")]
    [SerializeField] private float scaleAmplitude = 0.25f;
    [Tooltip("Speed of the pulse in cycles per second.")]
    [SerializeField] private float pulseSpeed = 2f;
    [Tooltip("Use unscaled time so pulse runs when timeScale is 0.")]
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

        if (_textChanged)
        {
            _text.ForceMeshUpdate();
            _cachedMeshInfo = _text.textInfo.CopyMeshInfoVertexData();
            _textChanged = false;
        }
        TMP_TextInfo textInfo = _text.textInfo;
        int characterCount = textInfo.characterCount;

        if (characterCount == 0) return;

        if (_cachedMeshInfo == null) return;

        _time += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float wave = (Mathf.Sin(_time * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f; // 0..1
        float scale = baseScale + scaleAmplitude * wave;

        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Vector3[] sourceVertices = _cachedMeshInfo[materialIndex].vertices;
            Vector3[] destVertices = textInfo.meshInfo[materialIndex].vertices;

            if (!TMPLinkEffectHelper.IsCharacterInLink(textInfo, i, linkTag))
                continue; // leave mesh unchanged so other effects (wave, jitter) aren't overwritten

            Vector3 center = (sourceVertices[vertexIndex + 0] + sourceVertices[vertexIndex + 2]) * 0.5f;
            destVertices[vertexIndex + 0] = center + (sourceVertices[vertexIndex + 0] - center) * scale;
            destVertices[vertexIndex + 1] = center + (sourceVertices[vertexIndex + 1] - center) * scale;
            destVertices[vertexIndex + 2] = center + (sourceVertices[vertexIndex + 2] - center) * scale;
            destVertices[vertexIndex + 3] = center + (sourceVertices[vertexIndex + 3] - center) * scale;
        }

        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            _text.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }
}
