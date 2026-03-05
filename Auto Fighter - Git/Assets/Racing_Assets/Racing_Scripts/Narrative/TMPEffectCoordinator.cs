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

        if (_textChanged || _restCache == null)
        {
            _text.ForceMeshUpdate();
            TMP_TextInfo textInfo = _text.textInfo;
            if (textInfo.characterCount == 0)
            {
                _restCache = null;
                _textChanged = false;
                return;
            }
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
