using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Reward Catalog")]
public class RewardCatalogSO : ScriptableObject
{
    [Tooltip("All reward assets available for this mode")]
    public List<RewardSO> allRewards = new List<RewardSO>();
}
