using UnityEngine;
using TMPro;

/// <summary>
/// Vertex animation: jitter/shake. Only words in &lt;link="jitter"&gt;...&lt;/link&gt; (or whole line if link tag empty).
/// Uses coordinator rest cache when present; otherwise manages its own. Never uploads if TMPEffectUploader is present.
/// </summary>
[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(TMP_Text))]
public class TMPJitterEffect : MonoBehaviour
{
    [Header("Which words get this effect")]
    [Tooltip("Link tag in your text, e.g. jitter. Only characters inside <link=\"jitter\">word</link> jitter. Empty = whole line.")]
    [SerializeField] private string linkTag = "jitter";

    [Header("Jitter")]
    [SerializeField] private float offsetScale = 2f;
    [SerializeField] private float speed = 5f;
    [SerializeField] private bool useUnscaledTime = true;

    private TMP_Text _text;
    private TMP_MeshInfo[] _ownCache;
    private bool _textChanged = true;
    private float _time;
    private float[] _ampMul;
    private float[] _spdMul;

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
        float scale = Mathf.Max(_text.transform.lossyScale.y, 0.001f);
        float effectiveScale = offsetScale * scale;

        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;
            if (!TMPLinkEffectHelper.IsCharacterInLink(_text, i, linkTag)) continue;

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Vector3[] sourceVertices = rest[materialIndex].vertices;
            Vector3[] destVertices = textInfo.meshInfo[materialIndex].vertices;

            float ampMul = (_ampMul != null && i < _ampMul.Length) ? _ampMul[i] : 1f;
            float spdMul = (_spdMul != null && i < _spdMul.Length) ? _spdMul[i] : 1f;

            float px = Mathf.PerlinNoise(i * 0.5f, _time * speed * spdMul) * 2f - 1f;
            float py = Mathf.PerlinNoise(i * 0.5f + 100f, _time * speed * spdMul) * 2f - 1f;
            Vector3 jitter = new Vector3(px, py, 0) * (effectiveScale * ampMul);

            destVertices[vertexIndex + 0] = sourceVertices[vertexIndex + 0] + jitter;
            destVertices[vertexIndex + 1] = sourceVertices[vertexIndex + 1] + jitter;
            destVertices[vertexIndex + 2] = sourceVertices[vertexIndex + 2] + jitter;
            destVertices[vertexIndex + 3] = sourceVertices[vertexIndex + 3] + jitter;
        }

        if (GetComponent<TMPEffectUploader>() == null)
            UploadMesh(textInfo);
    }

    private TMP_MeshInfo[] GetRestCache(TMP_TextInfo textInfo, ref int characterCount)
    {
        var coord = GetComponent<TMPEffectCoordinator>();
        if (coord != null && coord.HasRestCache())
        {
            if (_textChanged)
            {
                RebuildMultipliers(characterCount);
                _textChanged = false;
            }
            return coord.GetRestCache();
        }

        if (_textChanged || _ownCache == null)
        {
            _text.ForceMeshUpdate();
            textInfo = _text.textInfo;
            characterCount = textInfo.characterCount;
            if (characterCount == 0) return null;
            _ownCache = textInfo.CopyMeshInfoVertexData();
            RebuildMultipliers(characterCount);
            _textChanged = false;
        }
        return _ownCache;
    }

    private void RebuildMultipliers(int characterCount)
    {
        if (string.IsNullOrEmpty(linkTag)) { _ampMul = null; _spdMul = null; return; }
        TMPLinkEffectHelper.BuildPerCharMultipliers(_text.text, characterCount, linkTag, "amp", usePositionalFallback: true, ref _ampMul);
        TMPLinkEffectHelper.BuildPerCharMultipliers(_text.text, characterCount, linkTag, "spd", usePositionalFallback: false, ref _spdMul);
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
