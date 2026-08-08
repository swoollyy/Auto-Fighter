using UnityEngine;
using TMPro;

/// <summary>
/// Single place that calls ForceMeshUpdate and holds the "rest" mesh cache.
/// Add this to the same GameObject as your TMP_Text and effect components.
/// Effects ask the coordinator for the rest cache instead of calling ForceMeshUpdate themselves,
/// so no effect can accidentally reset the mesh and break the others.
/// Runs first (order -400); update the cache only when text has changed.
/// </summary>
[DefaultExecutionOrder(-400)]
[RequireComponent(typeof(TMP_Text))]
public class TMPEffectCoordinator : MonoBehaviour
{
    private TMP_Text _text;
    private TMP_MeshInfo[] _restCache;
    private bool _textChanged = true;
    private string _cachedText;
    private int _cachedCharacterCount = -1;

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

        TMP_TextInfo liveInfo = _text.textInfo;
        int liveCount = liveInfo != null ? liveInfo.characterCount : 0;
        // Also rebuild if TMP skipped TEXT_CHANGED but the string / glyph count changed
        // (common with rapid dialogue line advances + vertex effects).
        bool contentChanged = _textChanged || _restCache == null
            || _cachedText != _text.text
            || _cachedCharacterCount != liveCount;

        if (contentChanged)
        {
            _text.ForceMeshUpdate();
            TMP_TextInfo textInfo = _text.textInfo;
            _cachedText = _text.text;
            _cachedCharacterCount = textInfo != null ? textInfo.characterCount : 0;

            if (textInfo == null || textInfo.characterCount == 0)
            {
                // Wipe leftover quads from the previous (longer) line so they cannot ghost-render.
                ClearUnusedMeshVertices(textInfo);
                PushMeshToGpu(textInfo);
                _restCache = null;
                _textChanged = false;
                return;
            }
            // TMP keeps oversized vertex buffers across text changes. Zero unused slots BEFORE
            // caching rest — otherwise a jittered glyph (e.g. trailing '*') from a prior line
            // stays in the buffer and draws as phantom text on every following line.
            ClearUnusedMeshVertices(textInfo);
            _restCache = textInfo.CopyMeshInfoVertexData();
            _textChanged = false;
        }

        // Every frame: reset mesh to rest so all effects start from the same base and stack correctly.
        TMP_TextInfo ti = _text.textInfo;
        if (_restCache != null && ti.meshInfo != null)
        {
            for (int i = 0; i < _restCache.Length && i < ti.meshInfo.Length; i++)
            {
                CopyVertices(_restCache[i].vertices, ti.meshInfo[i].vertices);
                CopyColors(_restCache[i].colors32, ti.meshInfo[i].colors32);
            }
            ClearUnusedMeshVertices(ti);
        }
    }

    /// <summary>
    /// TMP meshInfo vertex arrays are capacity buffers. Glyphs past <see cref="TMP_MeshInfo.vertexCount"/>
    /// are stale leftover quads from longer previous strings — zero them so uploads cannot draw ghosts.
    /// </summary>
    public static void ClearUnusedMeshVertices(TMP_TextInfo textInfo)
    {
        if (textInfo?.meshInfo == null) return;

        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            TMP_MeshInfo mi = textInfo.meshInfo[i];
            Vector3[] verts = mi.vertices;
            if (verts == null) continue;

            int used = Mathf.Clamp(mi.vertexCount, 0, verts.Length);
            for (int v = used; v < verts.Length; v++)
                verts[v] = Vector3.zero;

            Color32[] colors = mi.colors32;
            if (colors == null) continue;
            int colorUsed = Mathf.Min(used, colors.Length);
            for (int c = colorUsed; c < colors.Length; c++)
                colors[c] = new Color32(0, 0, 0, 0);
        }
    }

    private static void PushMeshToGpu(TMP_TextInfo textInfo)
    {
        if (textInfo?.meshInfo == null) return;
        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            TMP_MeshInfo mi = textInfo.meshInfo[i];
            if (mi.mesh == null) continue;
            if (mi.vertices != null) mi.mesh.vertices = mi.vertices;
            if (mi.colors32 != null) mi.mesh.colors32 = mi.colors32;
        }
    }

    private static void CopyVertices(Vector3[] source, Vector3[] dest)
    {
        if (source == null || dest == null) return;
        int len = Mathf.Min(source.Length, dest.Length);
        for (int i = 0; i < len; i++) dest[i] = source[i];
    }

    private static void CopyColors(Color32[] source, Color32[] dest)
    {
        if (source == null || dest == null) return;
        int len = Mathf.Min(source.Length, dest.Length);
        for (int i = 0; i < len; i++) dest[i] = source[i];
    }

    /// <summary>Returns the cached "rest" mesh (positions and colors). Only updated when text changes. Null if not ready.</summary>
    public TMP_MeshInfo[] GetRestCache()
    {
        return _restCache;
    }

    /// <summary>True if we have a valid rest cache (so effects can use it).</summary>
    public bool HasRestCache()
    {
        return _restCache != null;
    }
}
