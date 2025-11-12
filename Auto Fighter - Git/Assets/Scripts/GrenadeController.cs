using UnityEngine;

[DisallowMultipleComponent]
public sealed class GrenadeController : MonoBehaviour
{
    [Header("Bindings")]
    [SerializeField] private Ball ball;
    [SerializeField] private Rigidbody ballRb;

    [Header("Config")]
    [SerializeField] private float cooldown = 10f;
    [SerializeField] private float fuseSeconds = 2f;
    [SerializeField] private float radius = 6f;
    [SerializeField, Range(0.01f, 1f)] private float maxPctAtCenter = 0.80f;
    [SerializeField, Range(0.01f, 1f)] private float minPctAtEdge = 0.60f;
    [SerializeField, Range(0f, 1f)] private float inheritVelFactor = 0.75f;
    [SerializeField] private float upArcMin = 2.5f;
    [SerializeField] private float upArcMax = 8.0f;
    [SerializeField, Range(0f, 1f)] private float linearDrag = 0.28f;
    [SerializeField, Range(0f, 1f)] private float angularDrag = 0.15f;
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.35f;
    [SerializeField] private float customGravityY = -14f;

    private float _cooldownRemain;
    private Pinball _pm;
    private PinballUIM _ui;

    public void Bind(Ball b,
        float cd, float fuse, float rad, float maxPct, float minPct,
        float inheritFactor, float arcMin, float arcMax,
        float linDrag, float angDrag, float bounce, float gravityY)
    {
        ball = b;
        ballRb = b ? b.GetComponent<Rigidbody>() : null;
        _pm = Pinball.Instance;
        _ui = FindFirstObjectByType<PinballUIM>();

        cooldown = Mathf.Max(0.05f, cd);
        fuseSeconds = Mathf.Max(0.05f, fuse);
        radius = Mathf.Max(0.05f, rad);
        maxPctAtCenter = Mathf.Clamp01(maxPct);
        minPctAtEdge = Mathf.Clamp01(minPct);
        inheritVelFactor = Mathf.Clamp01(inheritFactor);
        upArcMin = Mathf.Max(0f, arcMin);
        upArcMax = Mathf.Max(arcMin, arcMax);
        linearDrag = Mathf.Clamp01(linDrag);
        angularDrag = Mathf.Clamp01(angDrag);
        bounciness = Mathf.Clamp01(bounce);
        customGravityY = gravityY;

        if (_ui && ball) _ui.RegisterGrenadeIcon(ball);
        SetUiReady(true);
    }

    public void ForceCooldown(float seconds)
    {
        _cooldownRemain = Mathf.Max(_cooldownRemain, seconds);
        SetUiReady(false);
        SetUiCooldown(1f);
    }

    void OnDisable()
    {
        if (_ui && ball) _ui.UnregisterGrenadeIcon(ball);
    }

    void Update()
    {
        if (!_pm || !ball || !ball.isActiveAndEnabled || !ball.IsActive) return;

        if (_cooldownRemain > 0f)
        {
            _cooldownRemain -= Time.deltaTime;
            if (_cooldownRemain <= 0f)
            {
                _cooldownRemain = 0f;
                SetUiReady(true);
            }
            else
            {
                float norm = Mathf.Clamp01(_cooldownRemain / cooldown);
                SetUiCooldown(norm);
            }
        }

        if (_pm.CurrentState != PinballState.Play) return;

        if (Input.GetKeyDown(KeyCode.Space) && _cooldownRemain <= 0f)
            DropGrenade();
    }

    private void DropGrenade()
    {
        if (!ball || !ballRb) return;

        var go = new GameObject("GrenadeProjectile");
        go.layer = LayerMask.NameToLayer("Projectile");
        go.transform.position = ball.transform.position + new Vector3(0f, 0.2f, 0f);
        var proj = go.AddComponent<GrenadeProjectile>();
        proj.Init(new GrenadeProjectile.Params
        {
            fuseSeconds = fuseSeconds,
            radius = radius,
            maxPctAtCenter = maxPctAtCenter,
            minPctAtEdge = minPctAtEdge,
            inheritVelocityFactor = inheritVelFactor,
            upArcMin = upArcMin,
            upArcMax = upArcMax,
            linearDrag = linearDrag,
            angularDrag = angularDrag,
            bounciness = bounciness,
            customGravityY = customGravityY,
            ownerBall = ball
        });

        _cooldownRemain = cooldown;
        SetUiReady(false);
        SetUiCooldown(1f);
    }

    private void SetUiReady(bool ready)
    {
        if (_ui && ball) _ui.SetBallGrenadeReady(ball, ready);
    }

    private void SetUiCooldown(float normalizedRemaining)
    {
        if (_ui && ball) _ui.SetBallGrenadeCooldown(ball, normalizedRemaining);
    }
}