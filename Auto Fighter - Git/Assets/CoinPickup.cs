using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class CoinPickup : MonoBehaviour
{
    [Header("Coin Value")]
    [SerializeField] private int value = 1;

    [Header("Simple Visuals")]
    [SerializeField] private float rotateSpeed = 90f; // optional little spin

    [Header("FX")]
    [Tooltip("Optional VFX prefab to spawn when the coin is collected.")]
    [SerializeField] private GameObject coinPickupVFX;
    [Tooltip("Lifetime (seconds) for the spawned VFX when instantiated or returned to pool.")]
    [SerializeField] private float coinPickupVFXLifetime = 2f;

    [Header("VFX Color Mapping")]
    [Tooltip("Map coin values to VFX colors. The system will try an exact match first; if none found it will use the highest mapped value <= coin value.")]
    [SerializeField]
    private VFXColorEntry[] vfxColorEntries =
    {
        // default entries: 1 = bronze, 2 = silver, 3 = gold
        new VFXColorEntry { coinValue = 1, color = new Color(205f/255f, 127f/255f, 50f/255f, 1f) }, // bronze
        new VFXColorEntry { coinValue = 2, color = new Color(192f/255f, 192f/255f, 192f/255f, 1f) }, // silver
        new VFXColorEntry { coinValue = 3, color = new Color(1f, 215f/255f, 0f, 1f) } // gold
    };

    [SerializeField] private AudioClip[] coinCollectClips = new AudioClip[2]; // assign two coin sounds
    [SerializeField, Range(0f, 0.25f)] private float coinPitchVariance = 0.06f;
    [SerializeField, Range(0f, 1f)] private float coinCollectVolume = 1f;



    [System.Serializable]
    private class VFXColorEntry
    {
        public int coinValue = 1;
        public Color color = Color.white;
    }

    private void Reset()
    {
        // Make sure collider is trigger by default
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    private void Update()
    {
        // Optional spinning so it's more readable in world
        if (rotateSpeed != 0f)
        {
            transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f, Space.World);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent<CarController>(out var car))
            return;

        var mgr = RacingSkillTreeManager.Instance;
        int finalValue = value;

        if (mgr != null)
        {
            int baseAdd = mgr.GetCoinBaseAdd();
            if (baseAdd > 0)
                finalValue += baseAdd;
        }

        // NEW: play coin SFX (random selection + slight pitch variance)
        PlayRandomCoinSfx(transform.position);

        // NEW: double-value chance skill
        if (mgr != null)
        {
            float dblChance = mgr.GetCoinDoubleChance();
            if (dblChance > 0f && Random.value < dblChance)
                finalValue *= 2;
            mgr.AddCurrency(finalValue);
        }
        else
        {
            // Fallback
            RacingSkillTreeManager.Instance?.AddCurrency(finalValue);
        }

        if (GameManager_Racing.Instance != null)
        {
            GameManager_Racing.Instance.RegisterCoinPickup(finalValue);
        }

        // Spawn VFX at the collision/closest point before destroying the coin
        Vector3 spawnPos = transform.position;
        // try to use the collider's closest contact point as a nicer VFX origin
        try
        {
            spawnPos = other.ClosestPoint(transform.position);
        }
        catch { /* ignore and use transform.position */ }

        // Pass the finalValue so the VFX color matches the collected coin value
        SpawnPickupVFX(spawnPos, finalValue);

        Destroy(gameObject);
    }

    // Add helper methods (inside the same class)
    private void PlayRandomCoinSfx(Vector3 worldPos)
    {
        if (coinCollectClips == null || coinCollectClips.Length == 0) return;

        // pick a non-null clip
        AudioClip clip = null;
        for (int i = 0; i < 8; i++) // try a few times (in case some array entries are null)
        {
            var candidate = coinCollectClips[Random.Range(0, coinCollectClips.Length)];
            if (candidate != null) { clip = candidate; break; }
        }
        if (clip == null) return;

        float pitch = 1f + UnityEngine.Random.Range(-coinPitchVariance, coinPitchVariance);
        PlayClipAtPointWithPitch(clip, worldPos, coinCollectVolume, pitch);
    }

    private void PlayClipAtPointWithPitch(AudioClip clip, Vector3 pos, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        GameObject go = new GameObject("SFX_OneShot");
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 1f; // 3D
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.pitch = Mathf.Max(0.01f, pitch);
        src.Play();
        Destroy(go, clip.length / Mathf.Max(0.01f, Mathf.Abs(src.pitch)));
    }

    // Spawns the assigned VFX prefab. Uses ProjectilePool when available, otherwise Instantiate.
    // Keeps a fallback lifetime destroy for safety.
    private void SpawnPickupVFX(Vector3 worldPos, int coinValue)
    {
        if (coinPickupVFX == null) return;

        // get color for this coin value
        Color col = GetColorForValue(coinValue);

        // Desired rotation: -90° X, 0° Y, 0° Z
        Quaternion desiredRot = Quaternion.Euler(-90f, 0f, 0f);

        // Try using ProjectilePool (preferred). Pool returns inactive instances ready to position.
        try
        {
            if (ProjectilePool.Instance != null)
            {
                GameObject inst = ProjectilePool.Instance.Get(coinPickupVFX);
                if (inst != null)
                {
                    inst.transform.position = worldPos;
                    inst.transform.rotation = desiredRot; // apply correct Euler rotation
                    ApplyVFXColor(inst, col); // apply color before activation
                    inst.SetActive(true);
                    // Schedule return to pool
                    StartCoroutine(ReturnPooledVFXLater(coinPickupVFX, inst, Mathf.Max(0.01f, coinPickupVFXLifetime)));
                    return;
                }
            }
        }
        catch
        {
            // ignore pool errors and fallback to Instantiate
        }

        // Fallback: instantiate and destroy after lifetime, with correct rotation and color
        var go = Instantiate(coinPickupVFX, worldPos, desiredRot);
        ApplyVFXColor(go, col);
        Destroy(go, Mathf.Max(0.01f, coinPickupVFXLifetime));
    }

    private IEnumerator ReturnPooledVFXLater(GameObject prefab, GameObject instance, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (instance != null && prefab != null && ProjectilePool.Instance != null)
            ProjectilePool.Instance.Return(prefab, instance);
    }

    // Finds the best color entry for the given coin value.
    // Strategy: exact match; else highest entry.coinValue <= value; else fallback to first entry or white.
    private Color GetColorForValue(int coinValue)
    {
        if (vfxColorEntries == null || vfxColorEntries.Length == 0)
            return Color.white;

        // Try exact match
        for (int i = 0; i < vfxColorEntries.Length; i++)
        {
            if (vfxColorEntries[i].coinValue == coinValue)
                return vfxColorEntries[i].color;
        }

        // Find highest <= coinValue
        VFXColorEntry best = null;
        for (int i = 0; i < vfxColorEntries.Length; i++)
        {
            if (vfxColorEntries[i].coinValue <= coinValue)
            {
                if (best == null || vfxColorEntries[i].coinValue > best.coinValue)
                    best = vfxColorEntries[i];
            }
        }

        if (best != null) return best.color;

        // fallback to first entry
        return vfxColorEntries[0].color;
    }

    // Try to apply color to a VFX instance. Covers common cases:
    // - ParticleSystem.main.startColor (applies to all child ParticleSystems)
    // - Material color properties ("_BaseColor", "_Color", "_TintColor")
    // - VisualEffect via reflection (attempts SetVector4("Color", color))
    private void ApplyVFXColor(GameObject go, Color col)
    {
        if (go == null) return;

        // 1) ParticleSystems
        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        if (systems != null && systems.Length > 0)
        {
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.startColor = col;
            }
            return;
        }

        // 2) Renderer materials
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            foreach (var r in renderers)
            {
                // Use sharedMaterials to avoid creating garbage if not necessary;
                // but copying material arrays can instantiate instance materials which is OK for VFX.
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) continue;
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", col);
                    else if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", col);
                    else if (mat.HasProperty("_TintColor"))
                        mat.SetColor("_TintColor", col);
                    // else: can't set color generically for this material
                }
            }
            // don't return here — renderer coloring is often sufficient, but also try VFX
        }

        // 3) Try VisualEffect (VFX Graph) via reflection to avoid hard compile dependency
        var veType = System.Type.GetType("UnityEngine.VFX.VisualEffect, Unity.VisualEffectGraph");
        if (veType == null)
        {
            // Some Unity versions use another assembly name; try fallback
            veType = System.Type.GetType("UnityEngine.VFX.VisualEffect, UnityEngine.VFX");
        }

        if (veType != null)
        {
            var ves = go.GetComponentsInChildren(veType, true);
            foreach (var ve in ves)
            {
                // Try common parameter names
                var setVector4 = veType.GetMethod("SetVector4", new[] { typeof(string), typeof(Vector4) });
                if (setVector4 != null)
                {
                    // Try "Color" and "color"
                    setVector4.Invoke(ve, new object[] { "Color", (Vector4)col });
                    setVector4.Invoke(ve, new object[] { "color", (Vector4)col });
                }
                else
                {
                    // Try SetVector3
                    var setVector3 = veType.GetMethod("SetVector3", new[] { typeof(string), typeof(Vector3) });
                    if (setVector3 != null)
                    {
                        setVector3.Invoke(ve, new object[] { "Color", (Color32)col });
                        setVector3.Invoke(ve, new object[] { "color", (Color32)col });
                    }
                }
            }
        }
    }
}