using UnityEngine;

/// <summary>
/// Single on-track pickup that introduces a patron of The Help (e.g. The Taskmaster).
/// Collecting sets a story flag; intro dialogue plays when the player returns to the skill tree.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class HelpPatronPickup : MonoBehaviour
{
    [SerializeField] private HelpPatronId patronId = HelpPatronId.Taskmaster;
    [SerializeField] private string displayNameOverride = "";
    [SerializeField] private PowerupPickupTween tween;
    [SerializeField] private Color nameplateColor = new Color(0.925f, 0.706f, 0f, 1f);

    private bool _collected;
    private Transform _nameplate;

    public HelpPatronId PatronId => patronId;

    public void Setup(HelpPatronId id, string displayName = "")
    {
        patronId = id;
        if (!string.IsNullOrWhiteSpace(displayName))
            displayNameOverride = displayName.Trim();
        if (_nameplate != null)
        {
            var tm = _nameplate.GetComponent<TextMesh>();
            if (tm != null)
            {
                string label = string.IsNullOrWhiteSpace(displayNameOverride)
                    ? HelpPatronProgress.DisplayName(patronId)
                    : displayNameOverride;
                tm.text = label.ToUpperInvariant();
            }
        }
    }

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        if (!tween) tween = GetComponentInChildren<PowerupPickupTween>();
        if (tween) tween.PlaySpawn();
    }

    private void Start()
    {
        EnsureNameplate();
    }

    private void LateUpdate()
    {
        if (_nameplate == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        _nameplate.rotation = Quaternion.LookRotation(_nameplate.position - cam.transform.position, Vector3.up);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_collected) return;
        if (other.GetComponentInParent<CarController>() == null)
            return;

        _collected = true;
        HelpPatronProgress.MarkCollected(patronId);
        HelpPatronProgress.TryPlayPickupIntro();

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        if (_nameplate != null)
            _nameplate.gameObject.SetActive(false);

        if (tween)
        {
            tween.PlayCollect();
            Destroy(gameObject, tween.GetCollectDuration());
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void EnsureNameplate()
    {
        string label = string.IsNullOrWhiteSpace(displayNameOverride)
            ? HelpPatronProgress.DisplayName(patronId)
            : displayNameOverride.Trim();

        var go = new GameObject("Nameplate");
        go.transform.SetParent(transform, false);
        float parentScale = Mathf.Max(0.01f, transform.lossyScale.y);
        go.transform.localScale = Vector3.one / parentScale;
        go.transform.localPosition = new Vector3(0f, 1.45f / parentScale, 0f);

        var tm = go.AddComponent<TextMesh>();
        tm.text = label.ToUpperInvariant();
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.characterSize = 0.16f;
        tm.fontSize = 48;
        tm.color = nameplateColor;
        tm.fontStyle = FontStyle.Bold;

        _nameplate = go.transform;
    }
}
