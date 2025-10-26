using UnityEngine;

[DisallowMultipleComponent]
public sealed class NukeBumpersPowerup : IPowerup
{
    public string Id => "nuke-bumpers";
    public float Weight => 0.6f;
    public string DebugLabel => "Nuke Bumpers";

    // Always eligible; pickup roll occurs only during active play.
    public bool CanTrigger(IRunContext ctx) => true;

    // Deals percentage damage to all bumpers; awards score and XP at 75% of the ball’s current damage factor for balance.
    public void Execute(Pinball pinball, Vector3 triggerPos)
    {
        if (!pinball) return;

        // Use any active ball’s factor; fall back to 1x. Then apply the 0.75 debuff for balance.
        float damageFactor = 1f;
        var anchor = pinball.ball;
        if (anchor && anchor.isActiveAndEnabled && anchor.IsActive) damageFactor = anchor.ScoreXpDamageFactor;
        else
        {
            var any = Object.FindObjectsOfType<Ball>();
            for (int i = 0; i < any.Length; i++)
            {
                if (any[i] && any[i].isActiveAndEnabled && any[i].IsActive)
                {
                    damageFactor = any[i].ScoreXpDamageFactor;
                    break;
                }
            }
        }
        const float NUKE_DEBUFF = 0.75f;
        float awardFactor = Mathf.Max(0f, damageFactor * NUKE_DEBUFF);

        const float percent = 0.35f;  // 35% of current HP
        const float minDamage = 10f;

        foreach (var bumper in Bumper.EnumerateAll())
        {
            if (!bumper) continue;

            // Apply damage (XP handled inside TakeDamage via passed factor).
            float amount = Mathf.Max(minDamage, bumper.curHealth * percent);
            bumper.TakeDamage(amount, elemDmg: false, damageFactor: awardFactor);

            // Award score per affected bumper, using tiered base similar to bumpers, scaled by awardFactor.
            int baseScore = bumper.type == BumperType.Small ? 50 : 100;
            pinball.AddScore(baseScore, bumpCount: 0, bumpCountConsec: 0, damageFactor: awardFactor);
        }

        pinball.ScreenShake();
    }
}