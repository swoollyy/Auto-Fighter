using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Vertex color animation: rainbow cycle on &lt;link="rainbow"&gt;...&lt;/link&gt; (or whole line if link tag empty).
/// Only modifies colors in textInfo.meshInfo. Uses coordinator rest cache when present.
/// Never uploads if TMPEffectUploader is present (uploader pushes vertices + colors).
/// </summary>
[DefaultExecutionOrder(50)]
[RequireComponent(typeof(TMP_Text))]
public class TMPRainbowColorEffect : MonoBehaviour
{
    [Header("Which words get this effect")]
    [SerializeField] private string linkTag = "rainbow";

    [Header("Rainbow")]
    [SerializeField] private float cycleSpeed = 0.5f;
    [SerializeField] private float huePerCharacter = 0.05f;
    [SerializeField, Range(0f, 1f)] private float saturation = 1f;
    [SerializeField, Range(0f, 1f)] private float value = 1f;
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Blend")]
    [SerializeField] private bool overrideBaseColor = true;

    private TMP_Text _text;
    private TMP_MeshInfo[] _ownCache;
    private bool _textChanged = true;
    private float _time;
    private float[] _hueMul;
    private float[] _charHueOffset;
    private float[] _phaseOffset;
    private float[] _direction;

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

        _time += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float baseHue = Mathf.Repeat(_time * cycleSpeed, 1f);
        int maxVisible = _text.maxVisibleCharacters;
        bool useMaxVisible = maxVisible != int.MaxValue;

        for (int i = 0; i < characterCount; i++)
        {
            if (useMaxVisible && i >= maxVisible) break;

            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;
            if (!TMPLinkEffectHelper.IsCharacterInLink(_text, i, linkTag)) continue;

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Color32[] destColors = textInfo.meshInfo[materialIndex].colors32;
            if (destColors == null || vertexIndex + 3 >= destColors.Length) continue;

            float charOffset = (_charHueOffset != null && i < _charHueOffset.Length)
                ? _charHueOffset[i]
                : i * huePerCharacter;
            float phase = (_phaseOffset != null && i < _phaseOffset.Length) ? _phaseOffset[i] : 0f;
            float dir = (_direction != null && i < _direction.Length) ? _direction[i] : 1f;
            float hue = Mathf.Repeat(baseHue * dir + charOffset + phase, 1f);
            Color rgb = Color.HSVToRGB(hue, saturation, value);
            rgb.r = Mathf.Clamp01(rgb.r);
            rgb.g = Mathf.Clamp01(rgb.g);
            rgb.b = Mathf.Clamp01(rgb.b);
            Color32 rainbow = rgb;

            Color32[] baseColors = rest[materialIndex].colors32;
            if (baseColors == null || vertexIndex + 3 >= baseColors.Length) continue;

            if (overrideBaseColor)
            {
                destColors[vertexIndex + 0] = new Color32(rainbow.r, rainbow.g, rainbow.b, baseColors[vertexIndex + 0].a);
                destColors[vertexIndex + 1] = new Color32(rainbow.r, rainbow.g, rainbow.b, baseColors[vertexIndex + 1].a);
                destColors[vertexIndex + 2] = new Color32(rainbow.r, rainbow.g, rainbow.b, baseColors[vertexIndex + 2].a);
                destColors[vertexIndex + 3] = new Color32(rainbow.r, rainbow.g, rainbow.b, baseColors[vertexIndex + 3].a);
            }
            else
            {
                destColors[vertexIndex + 0] = MultiplyKeepAlpha(baseColors[vertexIndex + 0], rainbow);
                destColors[vertexIndex + 1] = MultiplyKeepAlpha(baseColors[vertexIndex + 1], rainbow);
                destColors[vertexIndex + 2] = MultiplyKeepAlpha(baseColors[vertexIndex + 2], rainbow);
                destColors[vertexIndex + 3] = MultiplyKeepAlpha(baseColors[vertexIndex + 3], rainbow);
            }
        }

        if (GetComponent<TMPEffectUploader>() == null)
            _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    private TMP_MeshInfo[] GetRestCache(TMP_TextInfo textInfo, ref int characterCount)
    {
        var coord = GetComponent<TMPEffectCoordinator>();
        if (coord != null && coord.HasRestCache())
        {
            if (_textChanged)
            {
                RebuildHueOffsets(characterCount);
                _textChanged = false;
            }
            return coord.GetRestCache();
        }

        if (_textChanged || _ownCache == null)
        {
            _ownCache = textInfo.CopyMeshInfoVertexData();
            RebuildHueOffsets(characterCount);
            _textChanged = false;
            _time = 0f;
        }
        return _ownCache;
    }

    private void RebuildHueOffsets(int characterCount)
    {
        int alloc = Mathf.Max(characterCount, 1);
        if (_charHueOffset == null || _charHueOffset.Length < alloc)
            _charHueOffset = new float[alloc];
        if (_phaseOffset == null || _phaseOffset.Length < alloc)
            _phaseOffset = new float[alloc];
        if (_direction == null || _direction.Length < alloc)
            _direction = new float[alloc];

        for (int i = 0; i < characterCount; i++)
        {
            _phaseOffset[i] = 0f;
            _direction[i] = 1f;
        }

        if (!string.IsNullOrEmpty(linkTag))
            TMPLinkEffectHelper.BuildPerCharMultipliers(_text.text, characterCount, linkTag, "hue", usePositionalFallback: true, ref _hueMul);
        else
            _hueMul = null;

        float cumulative = 0f;
        for (int i = 0; i < characterCount; i++)
        {
            _charHueOffset[i] = cumulative;
            float mul = (_hueMul != null && i < _hueMul.Length) ? _hueMul[i] : 1f;
            cumulative += huePerCharacter * mul;
        }

        if (string.IsNullOrEmpty(linkTag)) return;

        // Per-span phase and direction overrides:
        // - phase=P or offset=P shifts where hue starts (0..1 wraps the color wheel).
        // - rev=1 (or reverse=1 / dir=-1) runs that span backwards through hues.
        var ranges = new List<TMPLinkEffectHelper.LinkRange>();
        TMPLinkEffectHelper.ParseAllLinkRanges(_text.text, ranges);
        for (int r = 0; r < ranges.Count; r++)
        {
            TMPLinkEffectHelper.LinkRange range = ranges[r];
            if (!TMPLinkEffectHelper.MatchesBaseTag(range.id, linkTag))
                continue;

            TMPLinkEffectHelper.LinkParams parsed = TMPLinkEffectHelper.ParseLinkId(range.id);
            float phase = 0f;
            bool hasPhase = false;
            if (parsed.named != null)
            {
                if (parsed.named.TryGetValue("phase", out float p)) { phase = p; hasPhase = true; }
                else if (parsed.named.TryGetValue("offset", out float o)) { phase = o; hasPhase = true; }
            }

            bool reverse = false;
            if (parsed.named != null)
            {
                if (parsed.named.TryGetValue("rev", out float rev)) reverse = rev > 0f;
                else if (parsed.named.TryGetValue("reverse", out float rev2)) reverse = rev2 > 0f;
                else if (parsed.named.TryGetValue("dir", out float dir)) reverse = dir < 0f;
            }

            int start = Mathf.Clamp(range.start, 0, characterCount);
            int end = Mathf.Clamp(range.end, 0, characterCount);
            for (int i = start; i < end; i++)
            {
                if (hasPhase) _phaseOffset[i] = phase;
                if (reverse) _direction[i] = -1f;
            }
        }
    }

    private static Color32 MultiplyKeepAlpha(Color32 baseColor, Color32 tint)
    {
        return new Color32(
            (byte)((baseColor.r * tint.r) / 255),
            (byte)((baseColor.g * tint.g) / 255),
            (byte)((baseColor.b * tint.b) / 255),
            baseColor.a);
    }
}
