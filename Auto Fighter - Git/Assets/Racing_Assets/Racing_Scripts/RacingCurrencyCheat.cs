using UnityEngine;

public class RacingCurrencyCheat : MonoBehaviour
{
    [SerializeField] private KeyCode addCurrencyKey = KeyCode.K;
    [SerializeField] private int addCurrencyAmount = 100;

    void Update()
    {
        if (Input.GetKeyDown(addCurrencyKey))
            RacingSkillTreeManager.Instance?.AddCurrency(addCurrencyAmount);
    }
}