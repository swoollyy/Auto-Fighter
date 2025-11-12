using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class GrenadeProjectile : MonoBehaviour
{
    public struct Params
    {
        public float fuseSeconds;
        public float radius;
        public float maxPctAtCenter;
        public float minPctAtEdge;
        public float inheritVelocityFactor;
        public float upArcMin;
        public float upArcMax;
        public float linearDrag;
        public float angularDrag;
        public float bounciness;
        public float customGravityY;
        public Ball ownerBall;
    }

    private Params P;
    private Rigidbody rb;
    private float bornAt;
    private bool exploded;

    // Cached runtime material (for color / emission)
    private Material _mat;

    public void Init(Params p)
    {
        P = p;

        // Visual sphere (bigger & matches ball glow color)
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "GrenadeMesh";
        sphere.transform.SetParent(transform, false);
        sphere.transform.localScale = Vector3.one * 0.6f; // was 0.25f (larger now)
        var col = sphere.GetComponent<Collider>(); if (col) Destroy(col);

        var rend = sphere.GetComponent<MeshRenderer>();
        if (rend)
        {
            _mat = new Material(Shader.Find("Standard"));
            Color glowBase = (P.ownerBall && P.ownerBall.isActiveAndEnabled)
                ? P.ownerBall.GlowColor
                : new Color(1f, 0.35f, 0.15f);

            float emissiveIntensity = (P.ownerBall && P.ownerBall.isActiveAndEnabled)
                ? Mathf.Clamp(P.ownerBall.EmissionIntensityUI * 1.2f, 0.2f, 5f)
                : 1.5f;

            ConfigureStandardFade(_mat, new Color(glowBase.r, glowBase.g, glowBase.b, 0.85f));
            if (_mat.HasProperty("_EmissionColor"))
            {
                _mat.EnableKeyword("_EMISSION");
                _mat.SetColor("_EmissionColor", glowBase * Mathf.LinearToGammaSpace(emissiveIntensity));
            }
            rend.sharedMaterial = _mat;
        }

        var rootCol = gameObject.AddComponent<SphereCollider>();
        rootCol.radius = 0.3f;
        rootCol.material = new PhysicMaterial("GrenadePhysMat")
        {
            bounciness = Mathf.Clamp01(P.bounciness),
            bounceCombine = PhysicMaterialCombine.Maximum,
            frictionCombine = PhysicMaterialCombine.Average,
            dynamicFriction = 0.4f,
            staticFriction = 0.5f
        };

        rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.drag = Mathf.Clamp01(P.linearDrag);
        rb.angularDrag = Mathf.Clamp01(P.angularDrag);

        Vector3 inherit = Vector3.zero;
        if (P.ownerBall && P.ownerBall.isActiveAndEnabled)
        {
            var brb = P.ownerBall.GetComponent<Rigidbody>();
            if (brb) inherit = brb.velocity * Mathf.Clamp01(P.inheritVelocityFactor);
        }

        var planar = new Vector3(inherit.x, 0f, inherit.z);
        float speed = planar.magnitude;
        float refSpeed = P.ownerBall ? Mathf.Max(0.01f, P.ownerBall.maxSpeed) : 50f;
        float t = Mathf.InverseLerp(0f, refSpeed, speed);
        float up = Mathf.Lerp(P.upArcMin, P.upArcMax, t);
        rb.velocity = new Vector3(inherit.x, up, inherit.z);

        bornAt = Time.time;
        StartCoroutine(FuseCoroutine());
    }

    void FixedUpdate()
    {
        if (!rb) return;
        rb.AddForce(new Vector3(0f, P.customGravityY, 0f), ForceMode.Acceleration);
    }

    private IEnumerator FuseCoroutine()
    {
        float end = Time.time + Mathf.Max(0.05f, P.fuseSeconds);
        while (Time.time < end) yield return null;
        if (!exploded) Explode();
    }

    private void Explode()
    {
        exploded = true;

        float currentDamage = (P.ownerBall && P.ownerBall.isActiveAndEnabled) ? P.ownerBall.CurrentDamage : 0f;
        float currentFactor = (P.ownerBall && P.ownerBall.isActiveAndEnabled) ? P.ownerBall.ScoreXpDamageFactor : 1f;

        var pm = Pinball.Instance;
        if (pm)
        {
            pm.ScreenShakeGrenade();
            pm.PostFX?.BloomPulse(0.5f, 0.05f, 0.30f); // NEW bloom pulse
            pm.PostFX?.ChromaticPulse(0.30f, 0.05f, 0.22f);
        }

        SpawnRingVfx();
        SpawnExplosionLight(); // NEW yellow light flash

        var hits = Physics.OverlapSphere(transform.position, P.radius, ~0, QueryTriggerInteraction.Collide);
        if (hits != null && hits.Length > 0)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                var bumper = hits[i].GetComponent<Bumper>() ?? hits[i].GetComponentInParent<Bumper>();
                if (!bumper || !bumper.gameObject.activeInHierarchy || bumper.IsDead) continue;

                float d = Vector3.Distance(transform.position, bumper.transform.position);
                float nt = Mathf.Clamp01(d / Mathf.Max(0.0001f, P.radius));
                float pct = Mathf.Lerp(P.maxPctAtCenter, P.minPctAtEdge, nt);

                // CHANGED: 3x ball damage baseline, then apply falloff
                float dmg = (currentDamage * 3f) * pct;

                float xpFactor = currentFactor * 0.8f;

                bumper.TakeDamage(dmg, elemDmg: false, damageFactor: xpFactor);

                if (pm)
                {
                    int baseScore = bumper.type == BumperType.Small ? 50 : 100;
                    int scaled = Mathf.RoundToInt(baseScore * 0.8f);
                    pm.AddScore(Mathf.Max(1, scaled), 0, 0, xpFactor);
                }
            }
        }

        Destroy(gameObject);
    }

    private void SpawnRingVfx()
    {
        var ring = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ring.name = "GrenadeRingVFX";
        ring.transform.position = transform.position;
        var col = ring.GetComponent<Collider>(); if (col) col.isTrigger = true;

        var rend = ring.GetComponent<MeshRenderer>();
        if (rend)
        {
            var mat = new Material(Shader.Find("Standard"));
            ConfigureStandardFade(mat, new Color(1f, 0.85f, 0.2f, 0.75f));
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.2f) * Mathf.LinearToGammaSpace(2f));
            }
            rend.sharedMaterial = mat;
        }

        ring.transform.localScale = Vector3.one * 0.4f;
        var seq = DOTween.Sequence().SetUpdate(true);
        seq.Join(ring.transform.DOScale(Vector3.one * (P.radius * 2f), 0.25f).SetEase(Ease.OutQuad));
        if (rend) seq.Join(rend.material.DOFade(0f, 0.25f));
        seq.OnComplete(() => Destroy(ring));
    }

    // NEW: temporary yellow light flash
    private void SpawnExplosionLight()
    {
        var lightGO = new GameObject("GrenadeExplosionLight");
        lightGO.transform.position = transform.position + Vector3.up * 0.25f;
        var l = lightGO.AddComponent<Light>();
        l.color = new Color(1f, 0.9f, 0.3f);
        l.intensity = 0f;
        l.range = P.radius * 2.2f;
        l.shadows = LightShadows.None;

        DOTween.Sequence().SetUpdate(true)
            .Append(DOTween.To(() => l.intensity, v => l.intensity = v, 9f, 0.10f).SetEase(Ease.OutQuad))
            .Append(DOTween.To(() => l.intensity, v => l.intensity = v, 0f, 0.30f).SetEase(Ease.InQuad))
            .OnComplete(() => Destroy(lightGO));
    }

    private static void ConfigureStandardFade(Material m, Color c)
    {
        if (!m) return;
        m.SetFloat("_Mode", 2f);
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        m.SetColor("_Color", c);
    }
}