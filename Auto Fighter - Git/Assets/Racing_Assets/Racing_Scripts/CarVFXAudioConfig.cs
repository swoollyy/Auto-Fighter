using UnityEngine;

/// <summary>
/// Death VFX/SFX, screen shake, crash SFX, surface fuel modifiers, and boost ramp-down fractions. CarController reads these at Start.
/// </summary>
public class CarVFXAudioConfig : MonoBehaviour
{
    [Header("Screen Shake (Receiver)")]
    [SerializeField] private Transform cameraShakeTarget;
    [SerializeField] private float screenShakeGlobalMultiplier = 1f;
    [SerializeField] private float screenShakeReturnSpeed = 18f;

    [Header("Death VFX")]
    [SerializeField] private GameObject deathVFX;
    [SerializeField] private float deathVFXLifetime = 8f;

    [Header("Death Explosion SFX")]
    [SerializeField] private AudioClip deathExplodeClip;
    [SerializeField, Range(0f, 1f)] private float deathExplodeVolume = 1f;
    [SerializeField] private bool deathExplodeUseSpatial = true;
    [SerializeField, Range(0f, 1f)] private float deathExplodeSpatialBlend = 1f;
    [SerializeField] private AudioRolloffMode deathExplodeRolloff = AudioRolloffMode.Logarithmic;
    [SerializeField] private float deathExplodeMinDistance = 2f;
    [SerializeField] private float deathExplodeMaxDistance = 70f;
    [SerializeField, Range(0f, 3f)] private float deathExplodeVolumeMultiplier = 1.6f;
    [SerializeField, Range(0.5f, 2f)] private float deathExplodePitchMin = 0.7f;
    [SerializeField, Range(0.5f, 2f)] private float deathExplodePitchMax = 1f;
    [SerializeField] private float deathExplodeSfxCooldown = 0.08f;

    [Header("Boost Ramp-Down Fractions")]
    [SerializeField] private float defaultBoostRampDownFraction = 0.35f;
    [SerializeField] private float closeCallBoostRampDownFraction = 0.5f;
    [SerializeField] private float regularBoostRampDownFraction = 0.25f;

    [Header("Fuel Modifiers by Surface")]
    [SerializeField] private float grassFuelUseMultiplier = 1.5f;

    [Header("Debug (Surface Read-Only)")]
    [SerializeField] private float offDefaultFraction = 0f;
    [SerializeField] private float grassFraction = 0f;

    [Header("Crash Sound Effects")]
    [SerializeField] private AudioClip crashClipDefault;
    [SerializeField] private AudioClip crashClipHonk;
    [SerializeField, Range(0f, 1f)] private float crashSfxVolume = 1f;
    [SerializeField] private bool crashUseSpatial = true;
    [SerializeField, Range(0f, 1f)] private float crashSpatialBlend = 0.38f;
    [SerializeField] private AudioRolloffMode crashRolloff = AudioRolloffMode.Logarithmic;
    [SerializeField] private float crashMinDistance = 1f;
    [SerializeField] private float crashMaxDistance = 50f;
    [SerializeField, Range(0f, 3f)] private float crashVolumeMultiplier = 2.63f;
    [SerializeField, Range(0.5f, 2f)] private float crashPitchMin = 0.976f;
    [SerializeField, Range(0.5f, 2f)] private float crashPitchMax = 1.141f;

    public Transform CameraShakeTarget => cameraShakeTarget;
    public float ScreenShakeGlobalMultiplier => screenShakeGlobalMultiplier;
    public float ScreenShakeReturnSpeed => screenShakeReturnSpeed;
    public GameObject DeathVFX => deathVFX;
    public float DeathVFXLifetime => deathVFXLifetime;
    public AudioClip DeathExplodeClip => deathExplodeClip;
    public float DeathExplodeVolume => deathExplodeVolume;
    public bool DeathExplodeUseSpatial => deathExplodeUseSpatial;
    public float DeathExplodeSpatialBlend => deathExplodeSpatialBlend;
    public AudioRolloffMode DeathExplodeRolloff => deathExplodeRolloff;
    public float DeathExplodeMinDistance => deathExplodeMinDistance;
    public float DeathExplodeMaxDistance => deathExplodeMaxDistance;
    public float DeathExplodeVolumeMultiplier => deathExplodeVolumeMultiplier;
    public float DeathExplodePitchMin => deathExplodePitchMin;
    public float DeathExplodePitchMax => deathExplodePitchMax;
    public float DeathExplodeSfxCooldown => deathExplodeSfxCooldown;
    public float DefaultBoostRampDownFraction => defaultBoostRampDownFraction;
    public float CloseCallBoostRampDownFraction => closeCallBoostRampDownFraction;
    public float RegularBoostRampDownFraction => regularBoostRampDownFraction;
    public float GrassFuelUseMultiplier => grassFuelUseMultiplier;
    public float OffDefaultFraction => offDefaultFraction;
    public float GrassFraction => grassFraction;
    public AudioClip CrashClipDefault => crashClipDefault;
    public AudioClip CrashClipHonk => crashClipHonk;
    public float CrashSfxVolume => crashSfxVolume;
    public bool CrashUseSpatial => crashUseSpatial;
    public float CrashSpatialBlend => crashSpatialBlend;
    public AudioRolloffMode CrashRolloff => crashRolloff;
    public float CrashMinDistance => crashMinDistance;
    public float CrashMaxDistance => crashMaxDistance;
    public float CrashVolumeMultiplier => crashVolumeMultiplier;
    public float CrashPitchMin => crashPitchMin;
    public float CrashPitchMax => crashPitchMax;
}
