using UnityEngine;
using TMPro;

/// <summary>
/// Vertex animation: scale/pulse on &lt;link="pop"&gt;...&lt;/link&gt; (or whole line if link tag empty).
/// Uses coordinator rest cache when present; otherwise manages its own. Never uploads if TMPEffectUploader is present.
/// </summary>
[DefaultExecutionOrder(0)]
[RequireComponent(typeof(TMP_Text))]
public class TMPZoomEffect : MonoBehaviour
{
    [Header("Which words get this effect")]
    [SerializeField] private string linkTag = "pop";

    [Header("Scale / pulse")]
    [SerializeField] private float baseScale = 1f;
    [SerializeField] private float scaleAmplitude = 0.25f;
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private bool useUnscaledTime = true;

    private TMP_Text _text;
    private TMP_MeshInfo[] _ownCache;
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

        TMP_MeshInfo[] rest = GetRestCache(textInfo, ref characterCount);
        if (rest == null) return;

        _time += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float wave = (Mathf.Sin(_time * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        float scale = baseScale + scaleAmplitude * wave;

        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;
            if (!TMPLinkEffectHelper.IsCharacterInLink(_text, i, linkTag)) continue;

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Vector3[] destVertices = textInfo.meshInfo[materialIndex].vertices;

            // Scale from current vertices so wave/jitter are preserved
            Vector3 center = (destVertices[vertexIndex + 0] + destVertices[vertexIndex + 2]) * 0.5f;
            destVertices[vertexIndex + 0] = center + (destVertices[vertexIndex + 0] - center) * scale;
            destVertices[vertexIndex + 1] = center + (destVertices[vertexIndex + 1] - center) * scale;
            destVertices[vertexIndex + 2] = center + (destVertices[vertexIndex + 2] - center) * scale;
            destVertices[vertexIndex + 3] = center + (destVertices[vertexIndex + 3] - center) * scale;
        }

        if (GetComponent<TMPEffectUploader>() == null)
            UploadMesh(textInfo);
    }

    private TMP_MeshInfo[] GetRestCache(TMP_TextInfo textInfo, ref int characterCount)
    {
        var coord = GetComponent<TMPEffectCoordinator>();
        if (coord != null && coord.HasRestCache())
            return coord.GetRestCache();

        if (_textChanged)
        {
            _text.ForceMeshUpdate();
            textInfo = _text.textInfo;
            characterCount = textInfo.characterCount;
            if (characterCount == 0) return null;
            _ownCache = textInfo.CopyMeshInfoVertexData();
            _textChanged = false;
        }
        return _ownCache;
    }

    private void UploadMesh(TMP_TextInfo textInfo)
    {
        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            _text.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }
}
