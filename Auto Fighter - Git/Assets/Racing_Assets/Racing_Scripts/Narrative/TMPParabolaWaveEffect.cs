using UnityEngine;
using TMPro;

/// <summary>
/// Vertex animation: traveling wave. Only words in &lt;link="wave"&gt;...&lt;/link&gt; (or whole line if link tag empty).
/// Uses coordinator rest cache when present; otherwise manages its own. Never uploads if TMPEffectUploader is present.
/// </summary>
[DefaultExecutionOrder(-150)]
[RequireComponent(typeof(TMP_Text))]
public class TMPParabolaWaveEffect : MonoBehaviour
{
    [Header("Which words get this effect")]
    [Tooltip("Link tag in your text, e.g. wave. Only characters inside <link=\"wave\">word</link> wave. Empty = whole line.")]
    [SerializeField] private string linkTag = "wave";

    [Header("Wave shape")]
    [SerializeField] private float amplitude = 5f;
    [SerializeField] private float wavelength = 40f;

    [Header("Motion")]
    [SerializeField] private float speed = 2f;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private bool leftToRight = true;

    private TMP_Text _text;
    private TMP_MeshInfo[] _ownCache;
    private bool _textChanged = true;
    private float _time;
    private float _textMinX = float.MaxValue, _textMaxX = float.MinValue;
    private float[] _ampMul;

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        _textChanged = true;
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

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        _time += dt * speed * (leftToRight ? 1f : -1f);
        float span = Mathf.Max(_textMaxX - _textMinX, 1f);
        float k = (Mathf.PI * 2f) / (wavelength > 0.01f ? wavelength : span);

        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;
            if (!TMPLinkEffectHelper.IsCharacterInLink(_text, i, linkTag)) continue;

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Vector3[] restVerts = rest[materialIndex].vertices;
            Vector3[] destVertices = textInfo.meshInfo[materialIndex].vertices;

            // Phase from rest position so wave is consistent; add offset on top of CURRENT vertices so wave stacks with jitter etc.
            float centerX = (restVerts[vertexIndex + 0].x + restVerts[vertexIndex + 2].x) * 0.5f;
            float ampMul = (_ampMul != null && i < _ampMul.Length) ? _ampMul[i] : 1f;
            float offsetY = Mathf.Sin((centerX - _textMinX) * k + _time) * amplitude * ampMul;
            Vector3 offset = new Vector3(0f, offsetY, 0f);

            destVertices[vertexIndex + 0] = destVertices[vertexIndex + 0] + offset;
            destVertices[vertexIndex + 1] = destVertices[vertexIndex + 1] + offset;
            destVertices[vertexIndex + 2] = destVertices[vertexIndex + 2] + offset;
            destVertices[vertexIndex + 3] = destVertices[vertexIndex + 3] + offset;
        }

        if (GetComponent<TMPEffectUploader>() == null)
            UploadMesh(textInfo);
    }

    private TMP_MeshInfo[] GetRestCache(TMP_TextInfo textInfo, ref int characterCount)
    {
        var coord = GetComponent<TMPEffectCoordinator>();
        if (coord != null && coord.HasRestCache())
        {
            TMP_MeshInfo[] rest = coord.GetRestCache();
            if (rest != null && _textMinX > _textMaxX)
                ComputeTextBounds(textInfo, rest, out _textMinX, out _textMaxX);
            if (_textChanged)
            {
                RebuildMultipliers(characterCount);
                _textChanged = false;
            }
            return rest;
        }

        if (_textChanged || _ownCache == null)
        {
            _text.ForceMeshUpdate();
            textInfo = _text.textInfo;
            characterCount = textInfo.characterCount;
            if (characterCount == 0) return null;
            _ownCache = textInfo.CopyMeshInfoVertexData();
            ComputeTextBounds(textInfo, _ownCache, out _textMinX, out _textMaxX);
            RebuildMultipliers(characterCount);
            _textChanged = false;
            _time = 0f;
        }
        return _ownCache;
    }

    private void RebuildMultipliers(int characterCount)
    {
        if (string.IsNullOrEmpty(linkTag)) { _ampMul = null; return; }
        TMPLinkEffectHelper.BuildPerCharMultipliers(_text.text, characterCount, linkTag, "amp", usePositionalFallback: true, ref _ampMul);
    }

    private void ComputeTextBounds(TMP_TextInfo textInfo, TMP_MeshInfo[] meshInfo, out float minX, out float maxX)
    {
        minX = float.MaxValue;
        maxX = float.MinValue;
        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;
            int mat = charInfo.materialReferenceIndex;
            int vi = charInfo.vertexIndex;
            Vector3[] v = meshInfo[mat].vertices;
            float cx = (v[vi + 0].x + v[vi + 2].x) * 0.5f;
            if (cx < minX) minX = cx;
            if (cx > maxX) maxX = cx;
        }
        if (minX > maxX) { minX = 0f; maxX = 0f; }
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
