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
        // Only react to the car
        if (!other.TryGetComponent<CarController>(out var car))
            return;

        var mgr = RacingSkillTreeManager.Instance;
        if (mgr != null)
        {
            mgr.AddCurrency(value);
        }

        // NEW: notify the GameManager so it can track pickups separately
        if (GameManager_Racing.Instance != null)
        {
            GameManager_Racing.Instance.RegisterCoinPickup(value);
        }

        // TODO: play SFX / VFX here if you want before destroying
        Destroy(gameObject);
    }
}
