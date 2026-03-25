using UnityEngine;

/// <summary>
/// Companion that periodically fetches the furthest in-range coin and returns it to the player.
/// </summary>
public class CoinCollectingFriend : MonoBehaviour
{
    private const float CarMaxSpeedMultiplier = 1.3f;

    [Header("References")]
    [SerializeField] private CarController playerCar;
    [SerializeField] private Transform carryAnchor;

    [Header("Behavior")]
    [SerializeField, Min(0.1f)] private float collectionCooldown = 3f;
    [SerializeField, Min(0.1f)] private float searchRangeFromPlayer = 30f;
    [SerializeField, Min(0.1f)] private float fallbackMoveSpeed = 14f;
    [SerializeField, Min(0.01f)] private float grabDistance = 1.2f;
    [SerializeField, Min(0.01f)] private float deliverDistance = 2f;

    [Header("Idle Follow")]
    [Tooltip("If true and this object has a parent, idle returns to the exact local pose authored in the prefab.")]
    [SerializeField] private bool idleUsesInitialLocalPose = true;
    [SerializeField] private Vector3 idleOffset = new Vector3(1.5f, 1.2f, -2.5f);
    [SerializeField, Min(0.1f)] private float idleFollowLerp = 8f;

    private float _cooldownRemaining;
    private CoinPickup _targetCoin;
    private bool _returningWithCoin;
    private int _deliveryValueBonus;
    private Transform _initialParent;
    private Vector3 _initialLocalPosition;
    private Quaternion _initialLocalRotation;
    private bool _pendingImmediateRelaunch;

    public void ResetCooldownNow()
    {
        // Avoid disrupting in-flight collection/return state; relaunch as soon as current cycle completes.
        if (_targetCoin != null || _returningWithCoin)
        {
            _pendingImmediateRelaunch = true;
            return;
        }

        _cooldownRemaining = 0f;
    }

    private void Awake()
    {
        if (carryAnchor == null) carryAnchor = transform;
        _cooldownRemaining = 0f;
        _initialParent = transform.parent;
        _initialLocalPosition = transform.localPosition;
        _initialLocalRotation = transform.localRotation;
    }

    private void Update()
    {
        EnsurePlayerCar();
        if (playerCar == null) return;

        if (_returningWithCoin)
        {
            UpdateReturn();
            return;
        }

        if (_targetCoin != null)
        {
            UpdateOutbound();
            return;
        }

        UpdateIdleFollow();

        _cooldownRemaining -= Time.deltaTime;
        if (_cooldownRemaining > 0f) return;

        _targetCoin = FindFurthestCoinInRange(playerCar.transform.position, searchRangeFromPlayer);
        if (_targetCoin == null)
        {
            _cooldownRemaining = 0.25f;
            return;
        }
    }

    private void UpdateOutbound()
    {
        if (_targetCoin == null || !_targetCoin.IsAvailableForCollection)
        {
            ClearTargetAndStartCooldown();
            return;
        }

        Vector3 targetPos = _targetCoin.transform.position;
        MoveTowards(targetPos);

        if (Vector3.Distance(transform.position, targetPos) <= grabDistance)
        {
            _targetCoin.SetCarriedState(true, carryAnchor);
            _returningWithCoin = true;
        }
    }

    private void UpdateReturn()
    {
        if (_targetCoin == null)
        {
            ClearTargetAndStartCooldown();
            return;
        }

        Vector3 playerPos = playerCar.transform.position;
        MoveTowards(playerPos);

        if (Vector3.Distance(transform.position, playerPos) <= deliverDistance)
        {
            _targetCoin.SetCarriedState(false, null);
            _targetCoin.TryCollect(playerCar, _deliveryValueBonus);
            ClearTargetAndStartCooldown();
        }
    }

    private void UpdateIdleFollow()
    {
        float t = 1f - Mathf.Exp(-idleFollowLerp * Time.deltaTime);

        if (idleUsesInitialLocalPose && transform.parent != null && transform.parent == _initialParent)
        {
            transform.localPosition = Vector3.Lerp(transform.localPosition, _initialLocalPosition, t);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, _initialLocalRotation, t);
            return;
        }

        Vector3 targetPos = playerCar.transform.TransformPoint(idleOffset);
        transform.position = Vector3.Lerp(transform.position, targetPos, t);
    }

    private void MoveTowards(Vector3 worldTarget)
    {
        Vector3 dir = worldTarget - transform.position;
        float dist = dir.magnitude;
        if (dist <= 0.0001f) return;

        float runtimeMoveSpeed = GetRuntimeMoveSpeed();
        Vector3 step = dir / dist * runtimeMoveSpeed * Time.deltaTime;
        if (step.magnitude > dist) step = dir;
        transform.position += step;
    }

    private void OnDisable()
    {
        // Safety: if disabled while carrying a coin, release it so it doesn't stay "stuck carried".
        if (_targetCoin != null && _returningWithCoin)
            _targetCoin.SetCarriedState(false, null);

        _targetCoin = null;
        _returningWithCoin = false;
    }

    private void ClearTargetAndStartCooldown()
    {
        _targetCoin = null;
        _returningWithCoin = false;
        _cooldownRemaining = _pendingImmediateRelaunch ? 0f : collectionCooldown;
        _pendingImmediateRelaunch = false;
    }

    private CoinPickup FindFurthestCoinInRange(Vector3 center, float range)
    {
        float rangeSq = range * range;
        CoinPickup best = null;
        float bestDistSq = -1f;

        var allCoins = FindObjectsOfType<CoinPickup>();
        for (int i = 0; i < allCoins.Length; i++)
        {
            var coin = allCoins[i];
            if (coin == null || !coin.IsAvailableForCollection) continue;

            float dSq = (coin.transform.position - center).sqrMagnitude;
            if (dSq > rangeSq) continue;
            if (dSq > bestDistSq)
            {
                bestDistSq = dSq;
                best = coin;
            }
        }

        return best;
    }

    private void EnsurePlayerCar()
    {
        if (playerCar != null) return;

        var gm = GameManager_Racing.Instance;
        if (gm != null && gm.ActiveCar != null)
            playerCar = gm.ActiveCar;
    }

    private float GetRuntimeMoveSpeed()
    {
        if (playerCar != null)
            return Mathf.Max(0.1f, playerCar.EffectiveMaxSpeed * CarMaxSpeedMultiplier);
        return Mathf.Max(0.1f, fallbackMoveSpeed);
    }

    public void ApplySkillStats(float collectionRange, float cooldownSeconds, int valueBonusPerCoin)
    {
        searchRangeFromPlayer = Mathf.Max(0.1f, collectionRange);
        collectionCooldown = Mathf.Max(0.05f, cooldownSeconds);
        _deliveryValueBonus = Mathf.Max(0, valueBonusPerCoin);
    }

    public void SetPlayerCar(CarController car)
    {
        if (car != null) playerCar = car;
    }

    public float AuthoredBaseCooldown => Mathf.Max(0.05f, collectionCooldown);
    public float AuthoredBaseRange => Mathf.Max(0.1f, searchRangeFromPlayer);
}
