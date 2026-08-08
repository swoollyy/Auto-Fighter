using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public class CarVelocityAndPitch : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional. If empty will search GetComponentInParent<CarController>()")]
    public CarController car;

    [Tooltip("Primary AudioSource used for the idle/drone loop. If left empty the script will add one.")]
    public AudioSource primarySource;

    [Tooltip("Optional secondary AudioSource. If empty one will be created at runtime for crossfading.")]
    public AudioSource secondarySource;

    [Header("Clips")]
    public AudioClip idleClip;
    public AudioClip driveClip;

    [Header("Volumes")]
    [Range(0f, 1f)] public float idleVolume = 0.5f;
    [Range(0f, 1f)] public float driveVolume = 0.6f;

    [Header("Pitch")]
    [Tooltip("Pitch at zero speed")]
    public float driveMinPitch = 0.9f;
    [Tooltip("Pitch at or above SpeedForMaxPitch")]
    public float driveMaxPitch = 1.6f;
    public float idlePitch = 1f;
   
    [Header("Speed → Pitch")]
    [Tooltip("Speed (m/s) mapped to max pitch. Use CarController.EffectiveMaxSpeed or your desired top speed.")]
    public float speedForMaxPitch = 30f;
    [Tooltip("Optional curve for non-linear pitch mapping")]
    public AnimationCurve pitchCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Crossfade")]
    [Tooltip("Time (seconds) to crossfade volumes")]
    public float crossfadeTime = 0.35f;
    [Tooltip("Blend curve from idle (0) → drive (1) based on normalized speed")]
    public AnimationCurve blendCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Audio Settings")]
    public bool spatialize = true;
    [Range(0f, 1f)] public float spatialBlend = 1f; // 1 = 3D
    public AudioMixerGroup outputMixerGroup;

    // runtime
    private AudioSource _a;
    private AudioSource _b;
    private bool _usingAisPrimary = true;
    private float _targetIdleVol;
    private float _targetDriveVol;
    private float _curIdleVol;
    private float _curDriveVol;

    void Awake()
    {
        if (car == null)
            car = GetComponentInParent<CarController>();

        // Ensure primary audio source
        if (primarySource != null)
            _a = primarySource;
        else
            _a = gameObject.AddComponent<AudioSource>();

        // ensure secondary
        if (secondarySource != null)
            _b = secondarySource;
        else
            _b = gameObject.AddComponent<AudioSource>();

        SetupSource(_a);
        SetupSource(_b);

        // Assign clips
        _a.clip = idleClip ?? _a.clip;
        _b.clip = driveClip ?? _b.clip;

        // Start both loops but with appropriate initial volumes
        _a.loop = true;
        _b.loop = true;

        // Start playback if clips exist
        if (_a.clip != null && !_a.isPlaying) _a.Play();
        if (_b.clip != null && !_b.isPlaying) _b.Play();

        // initialize volumes
        _curIdleVol = idleVolume;
        _curDriveVol = 0f;
        _a.volume = _curIdleVol;
        _b.volume = 0f;
    }

    private void SetupSource(AudioSource s)
    {
        s.playOnAwake = false;
        s.spatialBlend = spatialize ? spatialBlend : 0f;
        s.dopplerLevel = 0f;
        s.outputAudioMixerGroup = outputMixerGroup;
        s.rolloffMode = AudioRolloffMode.Linear;
    }

    void Update()
    {
        if (car == null)
            car = GetComponentInParent<CarController>();

        float speed = car != null ? car.CurrentSpeed : 0f;

        // Normalize speed to 0..1 using speedForMaxPitch
        float norm = (speedForMaxPitch > 0f) ? Mathf.Clamp01(speed / speedForMaxPitch) : 0f;
        float blend = Mathf.Clamp01(blendCurve.Evaluate(norm));

        // targets
        _targetIdleVol = Mathf.Clamp01((1f - blend) * idleVolume);
        _targetDriveVol = Mathf.Clamp01(blend * driveVolume);

        // smooth volumes (time-based)
        float step = (crossfadeTime > 0f) ? (Time.deltaTime / Mathf.Max(0.0001f, crossfadeTime)) : 1f;
        _curIdleVol = Mathf.MoveTowards(_curIdleVol, _targetIdleVol, step);
        _curDriveVol = Mathf.MoveTowards(_curDriveVol, _targetDriveVol, step);

        // assign volumes to sources — keep drive clip on one source, idle on the other.
        // Prefer primary as idle for backwards compatibility.
        if (_a.clip == idleClip || (_a.clip == null && _b.clip == driveClip))
        {
            _a.volume = _curIdleVol;
            _b.volume = _curDriveVol;
        }
        else
        {
            _a.volume = _curDriveVol;
            _b.volume = _curIdleVol;
        }

        // Pitch for driving source (drive pitch maps with curve)
        float pitchT = Mathf.Clamp01(pitchCurve.Evaluate(norm));
        float drivePitch = Mathf.Lerp(driveMinPitch, driveMaxPitch, pitchT);

        // apply pitch: only on the clip(s) that are driving (if both clips are same, apply both)
        if (_a.clip == driveClip) _a.pitch = drivePitch;
        else _a.pitch = idlePitch;
        if (_b.clip == driveClip) _b.pitch = drivePitch;
        else _b.pitch = idlePitch;
    }

    // Public helpers to set clips at runtime
    public void SetIdleClip(AudioClip clip)
    {
        idleClip = clip;
        if (_a != null)
        {
            if (_a.isPlaying) _a.Stop();
            _a.clip = idleClip;
            if (idleClip != null) _a.Play();
        }
    }

    public void SetDriveClip(AudioClip clip)
    {
        driveClip = clip;
        if (_b != null)
        {
            if (_b.isPlaying) _b.Stop();
            _b.clip = driveClip;
            if (driveClip != null) _b.Play();
        }
    }
}