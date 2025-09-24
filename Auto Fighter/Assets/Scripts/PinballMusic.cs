using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class PinballMusic : MonoBehaviour
{
    [Header("Clips")]
    [Tooltip("Plays once, then hands off to Loop.")]
    public AudioClip introClip;

    [Tooltip("Seamlessly loops forever after Intro finishes.")]
    public AudioClip loopClip;

    [Header("Routing (optional)")]
    [Tooltip("Optional mixer routing for both sources.")]
    public AudioMixerGroup outputMixerGroup;

    [Header("Behavior")]
    [Tooltip("Automatically start music on Start().")]
    public bool playOnStart = true;

    [Tooltip("Safety lead time before scheduled start, in seconds.")]
    [Min(0.01f)]
    public double scheduleLeadIn = 0.08; // small buffer for preload

    private AudioSource _introSource;
    private AudioSource _loopSource;
    private bool _started;



    void Awake()
    {
        _introSource = gameObject.AddComponent<AudioSource>();
        _loopSource = gameObject.AddComponent<AudioSource>();

        ConfigureSource(_introSource, false);
        ConfigureSource(_loopSource, true);
    }


    void Start()
    {
        if (playOnStart)
            StartMusic();
    }

    public void StartMusic()
    {
        if (_started)
            return;

        double dspStart = AudioSettings.dspTime + scheduleLeadIn;

        if (introClip != null)
        {
            _introSource.clip = introClip;
            _introSource.loop = false;
            _introSource.PlayScheduled(dspStart);

            double introDuration = (double)introClip.samples / introClip.frequency;

            if (loopClip != null)
            {
                _loopSource.clip = loopClip;
                _loopSource.loop = true;
                _loopSource.PlayScheduled(dspStart + introDuration);
            }
        }

        _started = true;

    }

    private void ConfigureSource(AudioSource source, bool loop)
    {
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = false;
        if(outputMixerGroup != null)
            source.outputAudioMixerGroup = outputMixerGroup;
    }


    // Update is called once per frame
    void Update()
    {
        
    }
}
