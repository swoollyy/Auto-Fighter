using UnityEngine;
using System.Collections;

[DisallowMultipleComponent]
public class RacingUISoundManager : MonoBehaviour
{
    public static RacingUISoundManager Instance { get; private set; }

    [Header("UI SFX (assign in inspector)")]
    [SerializeField] private AudioClip buttonHoverClip;
    [SerializeField] private AudioClip buttonSelectClip;
    [SerializeField] private AudioClip buttonDeselectClip;
    [SerializeField] private AudioClip purchaseSkillClip;
    [SerializeField] private AudioClip purchaseCurrencyClip;

    [Header("Volumes")]
    [Range(0f, 1f)][SerializeField] private float hoverVolume = 0.7f;
    [Range(0f, 1f)][SerializeField] private float selectVolume = 0.9f;
    [Range(0f, 1f)][SerializeField] private float purchaseVolume = 1f;
    [Range(0f, 1f)][SerializeField] private float currencyVolume = 1f;

    private Transform _sfxRoot;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Create a small parent so created SFX objects are organized under the UI root
        _sfxRoot = new GameObject("SFX_UI_Root").transform;
        _sfxRoot.SetParent(transform, false);
    }

    public void PlayHover()
    {
        Play2DClip(buttonHoverClip, hoverVolume);
    }

    public void PlaySelect()
    {
        Play2DClip(buttonSelectClip, selectVolume);
    }

    public void PlayDeselect()
    {
        Play2DClip(buttonDeselectClip, selectVolume * 0.8f);
    }

    public void PlayPurchaseSkill()
    {
        Play2DClip(purchaseSkillClip, purchaseVolume);
    }

    public void PlayPurchaseCurrency()
    {
        Play2DClip(purchaseCurrencyClip, currencyVolume);
    }

    private void Play2DClip(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        StartCoroutine(SpawnAndPlay(clip, Mathf.Clamp01(volume)));
    }

    private IEnumerator SpawnAndPlay(AudioClip clip, float volume)
    {
        var go = new GameObject("SFX_UI_" + (clip ? clip.name : "null"));
        go.transform.SetParent(_sfxRoot, false);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f; // 2D UI
        src.volume = volume;
        src.dopplerLevel = 0f;
        src.Play();
        float t = clip.length / Mathf.Max(0.01f, Mathf.Abs(src.pitch));
        yield return new WaitForSeconds(t);
        if (go) Destroy(go);
    }
}