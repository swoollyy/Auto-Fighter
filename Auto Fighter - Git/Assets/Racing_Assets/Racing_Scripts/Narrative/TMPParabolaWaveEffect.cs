using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Vertex animation: a traveling sine wave across each &lt;link="wave"&gt; span
/// (or the whole line if link tag is empty). Letters form one continuous ripple
/// that moves across the word — not independent up/down bobs.
/// </summary>
[DefaultExecutionOrder(-150)]
[RequireComponent(typeof(TMP_Text))]
public class TMPParabolaWaveEffect : MonoBehaviour
{
    [Header("Which words get this effect")]
    [Tooltip("Link tag in your text, e.g. wave. Only characters inside <link=\"wave\">word</link>. Empty = whole line.")]
    [SerializeField] private string linkTag = "wave";

    [Header("Wave shape")]
    [SerializeField] private float amplitude = 5f;
    [Tooltip("How many full sine cycles fit across one linked span. 1 = one smooth hump traveling across the word.")]
    [SerializeField, Min(0.1f)] private float cyclesAcrossSpan = 1f;
    [Tooltip("If enabled, uses Wavelength in mesh units instead of Cycles Across Span.")]
    [SerializeField] private bool useWorldWavelength = false;
    [SerializeField] private float wavelength = 40f;

    [Header("Motion")]
    [SerializeField] private float speed = 2f;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private bool leftToRight = true;

    private TMP_Text _text;
    private TMP_MeshInfo[] _ownCache;
    private bool _textChanged = true;
    private float _time;
    private float[] _ampMul;

    /// <summary>Per visible character: normalized 0..1 position within its wave span (or -1 if not in a span).</summary>
    private float[] _spanPhase01;
    /// <summary>World-space X of each character center (rest), used only for wavelength mode.</summary>
    private float[] _charCenterX;
    private float _legacyMinX;
    private float _legacyMaxX;

    private readonly List<TMPLinkEffectHelper.LinkRange> _rangesScratch = new List<TMPLinkEffectHelper.LinkRange>(8);

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

        bool useLegacyWavelength = useWorldWavelength && wavelength > 0.01f;
        float legacyK = useLegacyWavelength ? (Mathf.PI * 2f) / wavelength : 0f;

        float cycles = Mathf.Max(0.1f, cyclesAcrossSpan);

        for (int i = 0; i < characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;
            if (_spanPhase01 == null || i >= _spanPhase01.Length || _spanPhase01[i] < 0f)
                continue;

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Vector3[] destVertices = textInfo.meshInfo[materialIndex].vertices;

            float ampMul = (_ampMul != null && i < _ampMul.Length) ? _ampMul[i] : 1f;

            float phase;
            if (useLegacyWavelength && _charCenterX != null && i < _charCenterX.Length)
            {
                phase = (_charCenterX[i] - _legacyMinX) * legacyK + _time;
            }
            else
            {
                // One continuous ripple across the span: phase from span-local 0..1.
                phase = _spanPhase01[i] * (Mathf.PI * 2f * cycles) + _time;
            }

            float offsetY = Mathf.Sin(phase) * amplitude * ampMul;
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
            if (_textChanged)
            {
                RebuildSpanPhases(textInfo, rest, characterCount);
                RebuildMultipliers(characterCount);
                _textChanged = false;
                _time = 0f;
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
            RebuildSpanPhases(textInfo, _ownCache, characterCount);
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

    /// <summary>
    /// Builds a 0..1 phase coordinate per character within each wave link span,
    /// so letters in one tag share one traveling wave.
    /// </summary>
    private void RebuildSpanPhases(TMP_TextInfo textInfo, TMP_MeshInfo[] meshInfo, int characterCount)
    {
        if (_spanPhase01 == null || _spanPhase01.Length != characterCount)
            _spanPhase01 = new float[characterCount];
        if (_charCenterX == null || _charCenterX.Length != characterCount)
            _charCenterX = new float[characterCount];

        for (int i = 0; i < characterCount; i++)
        {
            _spanPhase01[i] = -1f;
            _charCenterX[i] = 0f;
        }

        _legacyMinX = float.MaxValue;
        _legacyMaxX = float.MinValue;

        // Collect wave spans: either whole line, or each matching link range.
        if (string.IsNullOrEmpty(linkTag))
        {
            AssignSpanPhase(textInfo, meshInfo, 0, characterCount);
            return;
        }

        _rangesScratch.Clear();
        TMPLinkEffectHelper.ParseAllLinkRanges(_text.text, _rangesScratch);

        bool any = false;
        for (int r = 0; r < _rangesScratch.Count; r++)
        {
            TMPLinkEffectHelper.LinkRange range = _rangesScratch[r];
            if (!TMPLinkEffectHelper.MatchesBaseTag(range.id, linkTag))
                continue;

            int start = Mathf.Clamp(range.start, 0, characterCount);
            int end = Mathf.Clamp(range.end, 0, characterCount);
            if (end <= start) continue;

            AssignSpanPhase(textInfo, meshInfo, start, end);
            any = true;
        }

        // Fallback: TMP linkInfo if raw parse found nothing.
        if (!any && textInfo.linkCount > 0)
        {
            for (int li = 0; li < textInfo.linkCount; li++)
            {
                TMP_LinkInfo link = textInfo.linkInfo[li];
                string id = link.GetLinkID() ?? "";
                if (!TMPLinkEffectHelper.MatchesBaseTag(id, linkTag))
                    continue;

                int start = link.linkTextfirstCharacterIndex;
                int end = start + link.linkTextLength;
                start = Mathf.Clamp(start, 0, characterCount);
                end = Mathf.Clamp(end, 0, characterCount);
                if (end <= start) continue;
                AssignSpanPhase(textInfo, meshInfo, start, end);
            }
        }
    }

    private void AssignSpanPhase(TMP_TextInfo textInfo, TMP_MeshInfo[] meshInfo, int start, int end)
    {
        // Gather visible glyph centers in this span (stable order = reading order).
        int visibleCount = 0;
        for (int i = start; i < end; i++)
        {
            if (!textInfo.characterInfo[i].isVisible) continue;
            visibleCount++;
        }
        if (visibleCount <= 0) return;

        // First pass: store centers + legacy bounds.
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        for (int i = start; i < end; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;

            int mat = charInfo.materialReferenceIndex;
            int vi = charInfo.vertexIndex;
            Vector3[] v = meshInfo[mat].vertices;
            float cx = (v[vi + 0].x + v[vi + 2].x) * 0.5f;
            _charCenterX[i] = cx;
            if (cx < minX) minX = cx;
            if (cx > maxX) maxX = cx;
        }

        if (minX < _legacyMinX) _legacyMinX = minX;
        if (maxX > _legacyMaxX) _legacyMaxX = maxX;

        // Phase by glyph index within the span so spacing quirks don't break the wave shape.
        int visibleIndex = 0;
        float denom = Mathf.Max(visibleCount - 1, 1);
        for (int i = start; i < end; i++)
        {
            if (!textInfo.characterInfo[i].isVisible) continue;
            _spanPhase01[i] = visibleCount == 1 ? 0f : (visibleIndex / denom);
            visibleIndex++;
        }
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
