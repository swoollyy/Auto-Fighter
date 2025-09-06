using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[RequireComponent(typeof(ParticleSystem))]
public class CollectXP : MonoBehaviour
{
    [Header("Trigger Binding")]
    [SerializeField, Min(1)] int maxTargets = 16;          // keep small (perf)
    [SerializeField, Range(0.05f, 1f)] float refreshInterval = 0.25f;

    [Header("XP Settings")]
    [SerializeField] int xpPerParticle = 2;

    [Header("References (drag in Inspector if available)")]
    [SerializeField] BallXPBar ballXPScript;              // where you display/accumulate XP

    ParticleSystem ps;
    ParticleSystem.TriggerModule trigger;

    // Reuse buffers to avoid GC allocations:
    static readonly List<ParticleSystem.Particle> enteredBuf = new(256);
    static readonly List<(Collider c, float d2)> sortBuf = new(64);

    float elapsed;

    void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        trigger = ps.trigger;
    }

    void OnEnable()
    {
        XPCollectorRegistry.OnChanged += RebindTargets;

        SceneManager.sceneLoaded += OnSceneLoaded;

        StartCoroutine(RebindNextFrame());
    }

    void OnDisable()
    {
        XPCollectorRegistry.OnChanged -= RebindTargets; 

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RebindTargets();
    }

    System.Collections.IEnumerator RebindNextFrame()
    {
        yield return null;
        RebindTargets();
    }

    void Start()
    {
        // Fallbacks if not wired in Inspector (still better to drag in!)
        if (!ballXPScript)
        {
            var go = GameObject.FindWithTag("BallXPHolder");
            if (go) ballXPScript = go.GetComponent<BallXPBar>();
        }

        // First binding pass
        RebindTargets();
    }

    void Update()
    {

        // Periodically refresh the set of tracked colliders (nearest few)
        elapsed += Time.deltaTime;
        if (elapsed >= refreshInterval)
        {
            elapsed = 0f;
            RebindTargets();
        }

        // Self-destroy once this system (and children) are done
        if (!ps.IsAlive(true))
            Destroy(gameObject);
    }

    void RebindTargets()
    {
        var regs = XPCollectorRegistry.I?.collectors;
        if (regs == null || regs.Count == 0) return;

        sortBuf.Clear();
        Vector3 p = transform.position;

        // Collect (collider, squaredDistance)
        for (int i = 0; i < regs.Count; i++)
        {
            var c = regs[i];
            if (!c) continue;
            var center = c.bounds.center;
            float d2 = (center - p).sqrMagnitude;
            sortBuf.Add((c, d2));
        }

        // Sort nearest first
        sortBuf.Sort((a, b) => a.d2.CompareTo(b.d2));

        // Assign nearest up to maxTargets
        int assignCount = Mathf.Min(maxTargets, sortBuf.Count);
        for (int i = 0; i < assignCount; i++)
            trigger.SetCollider(i, sortBuf[i].c);

        // Clear any leftover slots (prevents stale checks)
        for (int i = assignCount; i < maxTargets; i++)
            trigger.SetCollider(i, null);
    }

    void OnParticleTrigger()
    {
        // Pull only the particles that ENTERED a trigger this frame
        enteredBuf.Clear();
        int count = ps.GetTriggerParticles(ParticleSystemTriggerEventType.Enter, enteredBuf);

        for (int i = 0; i < count; i++)
        {
            // Award XP
            if (Pinball.Instance)
            {
                Pinball.Instance.AddXP(xpPerParticle);
            }




            // Kill ONLY the collected particle
            var p = enteredBuf[i];
            p.remainingLifetime = 0f;
            enteredBuf[i] = p;
        }

        // Write changes back
        if (count > 0)
            ps.SetTriggerParticles(ParticleSystemTriggerEventType.Enter, enteredBuf);
    }

}