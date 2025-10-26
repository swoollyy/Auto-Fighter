using UnityEngine;

[DisallowMultipleComponent]
public sealed class RandomFlingPowerup : IPowerup
{
    public string Id => "random-fling";
    public float Weight => 0.8f;
    public string DebugLabel => "Random Fling";

    // Always eligible; pickup roll ensures pacing.
    public bool CanTrigger(IRunContext ctx) => true;

    // Applies a random horizontal impulse to each active ball for chaotic repositioning.
    public void Execute(Pinball pinball, Vector3 triggerPos)
    {
        if (!pinball) return;

        var balls = Object.FindObjectsOfType<Ball>();
        for (int i = 0; i < balls.Length; i++)
        {
            var b = balls[i];
            if (!b || !b.isActiveAndEnabled || !b.IsActive) continue;

            var rb = b.GetComponent<Rigidbody>();
            if (!rb) continue;

            Vector3 dir = Random.onUnitSphere;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
            dir.Normalize();

            float strength = Random.Range(100f, 250f);
            rb.AddForce(dir * strength, ForceMode.Impulse);
        }

        pinball.ScreenShake();
    }
}