using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BallElementalState : MonoBehaviour
{
    [SerializeField]
    private ElementalState initialState = ElementalState.None;

    [Header("Element Overlay Materials (optional)")]
    [Tooltip("1st overlay material for Fire state.")]
    [SerializeField] private Material fireMaterial1;
    [Tooltip("2nd overlay material for Fire state.")]
    [SerializeField] private Material fireMaterial2;
    [SerializeField] private Material waterMaterial;
    [SerializeField] private Material earthMaterial;
    [SerializeField] private Material electricMaterial;

    [Tooltip("Instantiate a unique copy of the overlay material(s) per ball (safe if you mutate).")]
    [SerializeField] private bool instantiateElementMaterials = true;

    [Tooltip("Primary slot index where elemental overlay starts (Fire will also use the next slot).")]
    [SerializeField] private int elementMaterialSlot = 1;

    [Header("Fire Helper")]
    [Tooltip("Fire velocity feeder script (auto-added if missing on Fire).")]
    [SerializeField] private bool autoAddFireVelocityFeeder = true;

    Pinball PM;

    public ElementalState CurrentState = ElementalState.None;

    private Renderer _rend;
    private FireVelocityFeeder _fireFeeder;

    // Track currently applied element overlay materials (Fire uses 2)
    private readonly List<Material> _activeElementMaterials = new List<Material>();
    // Cache of original (base) materials so we can restore cleanly
    private Material[] _baseMaterials;
    private bool _cachedBase;

    // For clean up when we instantiate
    private readonly List<Material> _instancedOverlayMaterials = new();

    private Ball ball;
    private float originalMaxSpeed;

    private static readonly Dictionary<(ElementalState, ElementalState), ElementalState> combinations =
        new()
        {
            {(ElementalState.Fire, ElementalState.Water), ElementalState.Steam},
            {(ElementalState.Water, ElementalState.Fire), ElementalState.Steam},
            {(ElementalState.Fire, ElementalState.Earth), ElementalState.Magma},
            {(ElementalState.Earth, ElementalState.Fire), ElementalState.Magma},
            {(ElementalState.Fire, ElementalState.Air), ElementalState.Wildfire},
            {(ElementalState.Air, ElementalState.Fire), ElementalState.Wildfire},
            {(ElementalState.Water, ElementalState.Earth), ElementalState.Sludge},
            {(ElementalState.Earth, ElementalState.Water), ElementalState.Sludge},
            {(ElementalState.Water, ElementalState.Air), ElementalState.Vapor},
            {(ElementalState.Air, ElementalState.Water), ElementalState.Vapor},
            {(ElementalState.Air, ElementalState.Earth), ElementalState.Whirlwind},
            {(ElementalState.Earth, ElementalState.Air), ElementalState.Whirlwind},
        };

    private float fireTempDamage;
    private float fireBurnDamage;
    private float fireBurnDuration;
    private bool fireExplode;
    private float fireExplosionSize;
    private int fireExplosionDamage;
    private bool fireEffectActive;
    private bool fireIsCursed;

    private float waterBonusXP;
    private int waterBonusDamage;
    private float waterDrenchDuration;
    private bool waterExplode;
    private float waterBurstSize;
    private int waterExplosionDamage;
    private bool waterEffectActive;
    private bool waterIsCursed;

    private int earthFissureDamage;
    private float earthCrustDuration;
    private float earthBonusXP;
    private float earthBonusScore;
    private bool earthEffectActive;
    private bool earthIsCursed;

    private int electricShockDamage;
    private int electricChainCount;
    private float electricBonusXP;
    private float electricBonusScore;
    private bool electricEffectActive;
    private bool electricIsCursed;

    private bool areEffectsActive => fireEffectActive || waterEffectActive || earthEffectActive || electricEffectActive;

    private int fireBouncesRemaining;
    private int waterBouncesRemaining;
    private int earthBouncesRemaining;
    private int electricBouncesRemaining;

    public float FireActiveTempDamage => fireTempDamage;
    public float FireBurnDamage => fireBurnDamage;
    public float FireBurnDuration => fireBurnDuration;
    public bool FireExplode => fireExplode;
    public float FireExplosionSize => fireExplosionSize;
    public int FireExplosionDamage => fireExplosionDamage;
    public int FireBouncesRemaining => fireBouncesRemaining;
    public bool FireEffectActive => fireEffectActive;
    public bool FireIsCursed => fireIsCursed;

    public float WaterBonusXP => waterBonusXP;
    public int WaterBonusDamage => waterBonusDamage;
    public float WaterDrenchDuration => waterDrenchDuration;
    public bool WaterExplode => waterExplode;
    public float WaterBurstSize => waterBurstSize;
    public int WaterExplosionDamage => waterExplosionDamage;
    public int WaterBouncesRemaining => waterBouncesRemaining;
    public bool WaterEffectActive => waterEffectActive;
    public bool WaterIsCursed => waterIsCursed;

    public int EarthFissureDamage => earthFissureDamage;
    public float EarthCrustDuration => earthCrustDuration;
    public float EarthBonusXP => earthBonusXP;
    public float EarthBonusScore => earthBonusScore;
    public bool EarthEffectActive => earthEffectActive;
    public bool EarthIsCursed => earthIsCursed;
    public int EarthBouncesRemaining => earthBouncesRemaining;

    public int ElectricShockDamage => electricShockDamage;
    public int ElectricChainCount => electricChainCount;
    public float ElectricBonusXP => electricBonusXP;
    public float ElectricBonusScore => electricBonusScore;
    public bool ElectricEffectActive => electricEffectActive;
    public bool ElectricIsCursed => electricIsCursed;
    public int ElectricBouncesRemaining => electricBouncesRemaining;

    private void Awake()
    {
        ball = GetComponent<Ball>();
        if (ball == null)
            Debug.LogWarning("BallElementalState requires a Ball component on the same GameObject.");

        _rend = GetComponent<Renderer>();
        if (_rend == null)
            Debug.LogWarning("BallElementalState requires a Renderer component.");

        _fireFeeder = GetComponent<FireVelocityFeeder>();
        if (_fireFeeder) _fireFeeder.enabled = false;

        PM = GameObject.FindWithTag("PinballManager")?.GetComponent<Pinball>();
    }

    void Start()
    {
        CurrentState = initialState;
        if (ball != null)
            originalMaxSpeed = ball.maxSpeed;

        CacheBaseMaterialsOnce();

        if (CurrentState != ElementalState.None)
            ApplyElementMaterial(CurrentState);
    }

    private void CacheBaseMaterialsOnce()
    {
        if (_cachedBase || _rend == null) return;
        // Use .materials so we get instances consistent with runtime modifications (not shared).
        _baseMaterials = _rend.materials.ToArray();
        _cachedBase = true;
    }

    public void SetState(ElementalState newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;
        ApplyStateEffects();
        ApplyElementMaterial(newState);
        // TODO: VFX / SFX
    }

    public void CombineWith(ElementalState newElement)
    {
        var combined = CombineElements(CurrentState, newElement);
        SetState(combined);
    }

    public ElementalState CombineElements(ElementalState existing, ElementalState incoming)
    {
        return combinations.TryGetValue((existing, incoming), out var result) ? result : incoming;
    }

    private void ApplyStateEffects()
    {
        if (ball == null) return;
        switch (CurrentState)
        {
            case ElementalState.Fire:
                break;
            case ElementalState.Water:
                break;
            case ElementalState.Earth:
                break;
            case ElementalState.Air:
                break;
            default:
                ball.maxSpeed = originalMaxSpeed;
                break;
        }
    }

    public void ClearState()
    {
        CurrentState = ElementalState.None;
        RemoveElementMaterials();
        if (_fireFeeder) _fireFeeder.enabled = false;
        // TODO: remove VFX / SFX
    }

    private void ApplyElementMaterial(ElementalState state)
    {
        if (_rend == null) return;
        CacheBaseMaterialsOnce();

        // Remove previous overlays first.
        RemoveElementMaterials();

        if (_fireFeeder) _fireFeeder.enabled = false;

        var mats = _rend.materials.ToList(); // current stack (should now equal _baseMaterials after removal)

        // Ensure slot index is not negative
        if (elementMaterialSlot < 0) elementMaterialSlot = 0;

        // Local creation helper
        Material Make(Material src)
        {
            if (src == null) return null;
            if (!instantiateElementMaterials) return src;
            var inst = new Material(src);
            _instancedOverlayMaterials.Add(inst);
            return inst;
        }

        switch (state)
        {
            case ElementalState.Fire:
                if (fireMaterial1 == null || fireMaterial2 == null) return;
                EnsureCapacity(mats, elementMaterialSlot + 2);
                var fireMat1 = Make(fireMaterial1);
                var fireMat2 = Make(fireMaterial2);
                mats[elementMaterialSlot] = fireMat1;
                mats[elementMaterialSlot + 1] = fireMat2;
                _activeElementMaterials.Add(fireMat1);
                _activeElementMaterials.Add(fireMat2);
                if (!_fireFeeder && autoAddFireVelocityFeeder)
                    _fireFeeder = gameObject.AddComponent<FireVelocityFeeder>();
                if (_fireFeeder) _fireFeeder.enabled = true;
                break;

            case ElementalState.Water:
                if (waterMaterial == null) return;
                EnsureCapacity(mats, elementMaterialSlot + 1);
                var wMat = Make(waterMaterial);
                mats[elementMaterialSlot] = wMat;
                _activeElementMaterials.Add(wMat);
                break;

            case ElementalState.Earth:
                if (earthMaterial == null) return;
                EnsureCapacity(mats, elementMaterialSlot + 1);
                var eMat = Make(earthMaterial);
                mats[elementMaterialSlot] = eMat;
                _activeElementMaterials.Add(eMat);
                break;

            case ElementalState.Electric:
                if (electricMaterial == null) return;
                EnsureCapacity(mats, elementMaterialSlot + 1);
                var elMat = Make(electricMaterial);
                mats[elementMaterialSlot] = elMat;
                _activeElementMaterials.Add(elMat);
                break;

            default:
                return;
        }

        _rend.materials = mats.ToArray();
    }

    // Ensure list has at least 'requiredCount' items by extending with base material clones (not overlay)
    private void EnsureCapacity(List<Material> mats, int requiredCount)
    {
        if (!_cachedBase || _baseMaterials == null || _baseMaterials.Length == 0) return;
        var baseRef = _baseMaterials[0];
        while (mats.Count < requiredCount)
        {
            // Use the first base material reference (extra slots will still render using that material until replaced)
            mats.Add(baseRef);
        }
    }

    private void RemoveElementMaterials()
    {
        if (_rend == null) return;

        // If nothing active, still restore to base if we previously modified length.
        if (_activeElementMaterials.Count == 0)
        {
            if (_cachedBase)
                _rend.materials = _baseMaterials.ToArray();
            return;
        }

        var before = _rend.materials;
        // Restore original base set (fast & deterministic) instead of trying to surgically remove.
        if (_cachedBase)
        {
            _rend.materials = _baseMaterials.ToArray();
        }
        else
        {
            // Fallback: rebuild removing overlays by reference
            var mats = before.Where(m => !_activeElementMaterials.Contains(m)).ToArray();
            _rend.materials = mats;
        }

        // Clean up instanced overlay materials (avoid leaking)
        if (instantiateElementMaterials && _instancedOverlayMaterials.Count > 0)
        {
            foreach (var inst in _instancedOverlayMaterials)
            {
                if (inst != null)
                    Destroy(inst);
            }
            _instancedOverlayMaterials.Clear();
        }

        _activeElementMaterials.Clear();
        // Debug (optional): Uncomment if you need verification
        // Debug.Log($"[BallElementalState] Cleared overlays. Before count={before.Length}, After count={_rend.materials.Length}");
    }

    public void OnBounce(Bumper bumper)
    {
        if (!areEffectsActive) return;

        var elem = bumper.gameObject.GetComponent<BumperElementalState>();

        if (fireEffectActive && bumper != null)
        {
            elem.ClearElement();
            elem.ApplyBurn(fireBurnDamage * ball.CurrentMultipliers, fireBurnDuration);
        }
        if (waterEffectActive && bumper != null)
        {
            elem.ClearElement();
            elem.ApplyDrenched(waterDrenchDuration, waterBonusXP);
        }
        if (earthEffectActive && bumper != null)
        {
            elem.ClearElement();
            elem.ApplyCrusted(earthFissureDamage * ball.CurrentMultipliers, earthCrustDuration, earthBonusXP, earthBonusScore);
        }
        if (electricEffectActive && bumper != null)
        {
            elem.ClearElement();
            elem.ApplyShocked(electricShockDamage * ball.CurrentMultipliers, electricBonusXP, electricBonusScore);
        }

        switch (CurrentState)
        {
            case ElementalState.Fire:
                fireBouncesRemaining--;
                if (fireBouncesRemaining <= 0) { fireEffectActive = false; ClearState(); }
                break;
            case ElementalState.Water:
                waterBouncesRemaining--;
                if (waterBouncesRemaining <= 0) { waterEffectActive = false; ClearState(); }
                break;
            case ElementalState.Earth:
                earthBouncesRemaining--;
                if (earthBouncesRemaining <= 0) { earthEffectActive = false; ClearState(); }
                break;
            case ElementalState.Electric:
                electricBouncesRemaining--;
                if (electricBouncesRemaining <= 0) { electricEffectActive = false; ClearState(); }
                break;
        }
    }

    #region Elemental State Methods

    public void SetFireState(int bonusDamage, float burnDamage, float burnDuration, int bounceDuration, bool canExplode, float explosionRadius, int explosionDamageFlat, bool cursed)
    {
        waterEffectActive = earthEffectActive = electricEffectActive = false;
        fireEffectActive = true;

        fireTempDamage = bonusDamage;
        fireBurnDamage = burnDamage;
        fireBurnDuration = burnDuration;
        fireBouncesRemaining += bounceDuration;
        if (fireBouncesRemaining > bounceDuration) fireBouncesRemaining = bounceDuration;
        fireExplode = canExplode;
        fireExplosionSize = explosionRadius;
        fireExplosionDamage = explosionDamageFlat;
        fireIsCursed = cursed;

        SetState(ElementalState.Fire);
    }

    public void SetWaterState(float bonusXP, int bonusDamage, float drenchDuration, int bounceDuration, bool canBurst, float burstRadius, int burstDamageFlat, bool cursed)
    {
        electricEffectActive = fireEffectActive = earthEffectActive = false;
        waterEffectActive = true;

        waterBonusXP = bonusXP;
        waterBonusDamage = bonusDamage;
        waterDrenchDuration = drenchDuration;
        waterBouncesRemaining += bounceDuration;
        if (waterBouncesRemaining > bounceDuration) waterBouncesRemaining = bounceDuration;
        waterExplode = canBurst;
        waterBurstSize = burstRadius;
        waterExplosionDamage = burstDamageFlat;
        waterIsCursed = cursed;

        SetState(ElementalState.Water);
    }

    public void SetEarthState(int fissureDamage, float crustDuration, float bonusXP, float bonusScore, int bounceDuration, bool cursed)
    {
        fireEffectActive = waterEffectActive = electricEffectActive = false;
        earthEffectActive = true;

        earthFissureDamage = fissureDamage;
        earthCrustDuration = crustDuration;
        earthBonusXP = bonusXP;
        earthBonusScore = bonusScore;
        earthBouncesRemaining += bounceDuration;
        if (earthBouncesRemaining > bounceDuration) earthBouncesRemaining = bounceDuration;
        earthIsCursed = cursed;

        SetState(ElementalState.Earth);
    }

    public void SetElectricState(int shockDamage, int chainCount, float bonusXP, float bonusScore, int bounceDuration, bool cursed)
    {
        fireEffectActive = waterEffectActive = earthEffectActive = false;
        electricEffectActive = true;

        electricShockDamage = shockDamage;
        electricChainCount = chainCount;
        electricBonusXP = bonusXP;
        electricBonusScore = bonusScore;
        electricBouncesRemaining += bounceDuration;
        if (electricBouncesRemaining > bounceDuration) electricBouncesRemaining = bounceDuration;
        electricIsCursed = cursed;

        SetState(ElementalState.Electric);
    }

    #endregion
}