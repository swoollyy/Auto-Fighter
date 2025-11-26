using UnityEngine;

[RequireComponent(typeof(Collider))]
public class CoinPickup : MonoBehaviour
{
    [Header("Coin Value")]
    [SerializeField] private int value = 1;

    [Header("Simple Visuals")]
    [SerializeField] private float rotateSpeed = 90f; // optional little spin

    private void Reset()
    {
        // Make sure collider is trigger by default
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    private void Update()
    {
        // Optional spinning so it's more readable in world
        if (rotateSpeed != 0f)
        {
            transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f, Space.World);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent<CarController>(out var car))
            return;

        var mgr = RacingSkillTreeManager.Instance;
        int finalValue = value;

        // NEW: double-value chance skill
        if (mgr != null)
        {
            float dblChance = mgr.GetCoinDoubleChance();
            if (dblChance > 0f && Random.value < dblChance)
                finalValue *= 2;
            mgr.AddCurrency(finalValue);
        }
        else
        {
            // Fallback
            RacingSkillTreeManager.Instance?.AddCurrency(finalValue);
        }

        if (GameManager_Racing.Instance != null)
        {
            GameManager_Racing.Instance.RegisterCoinPickup(finalValue);
        }

        Destroy(gameObject);
    }
}
