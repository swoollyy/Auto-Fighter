using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class HPPickup : MonoBehaviour
{
    [Header("HP")]
    [SerializeField, Min(0f)] private float baseAmount = 20f;

    [Header("Visuals (optional)")]
    [SerializeField] private PowerupPickupTween tween;

    private bool hasBeenCollected = false;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        if (!tween) tween = GetComponentInChildren<PowerupPickupTween>();
        if (tween) tween.PlaySpawn();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent<CarController>(out var car))
            return;

        float amount = baseAmount;
        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null)
            amount = mgr.GetHPPickupAmount(baseAmount);

        float added = 0f;
        if (!hasBeenCollected) { added = car.AddHP(amount); hasBeenCollected = true; }

        if (added <= 0.001f)
        {
            Destroy(gameObject);
            return;
        }

        if (tween)
        {
            tween.PlayCollect();
            Destroy(gameObject, tween.GetCollectDuration());
        }
        else
        {
            Destroy(gameObject);
        }
    }
}