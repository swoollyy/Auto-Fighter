using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;


public class PinballUIM : MonoBehaviour
{
    [System.Serializable]
    public class RewardSlot
    {
        public Button button;
        public TMP_Text titleText;
        public TMP_Text descText;
    }

    [Header("UI Slots(6 buttons)")]
    [SerializeField] private List<RewardSlot> slots = new();



    public Image ChargingSlider;

    public GameObject gamePanel;
    public GameObject paddleSelectPanel;
    public GameObject levelUpPanel;

    public TMP_Text gameScore;
    public TMP_Text bc;
    public TMP_Text bcc;
    public TMP_Text xpText;

    private Pinball pm;
    private List<RewardSO> currentRewards = new();

    // Start is called before the first frame update
    void Start()
    {
        levelUpPanel.SetActive(false);
        paddleSelectPanel.SetActive(false);

        gamePanel.SetActive(true);
    }

    // Update is called once per frame
    void Update()
    {
        if(pm != null)
        {
            ChargingSlider.fillAmount = pm.chargePercentage;
            bc.text = $"Score Mult: {pm.ScoreMultiplier.ToString()} | Timer: {pm.ScoreBonusTimeRemaining}";
            bcc.text = $"XP Mult: {pm.XPMultiplier.ToString()} | Timer: {pm.XPBonusTimeRemaining}";
            xpText.text = $"{Mathf.RoundToInt(pm.curXP)} / {pm.maxXP}";
        }


    }

    public void Init(Pinball manager)
    {
        pm = manager;
    }


    public void ShowRewardPopup(List<RewardSO> rewards)
    {
        gamePanel.SetActive(false);

        levelUpPanel.SetActive(true);
        currentRewards = rewards ?? new List<RewardSO>();

        for(int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];

            slot.button.onClick.RemoveAllListeners();

            if(i < currentRewards.Count && currentRewards[i] != null)
            {
                var reward = currentRewards[i];


                slot.titleText.text = reward.Name;
                slot.descText.text = reward.Description;

                slot.button.interactable = true;
                slot.button.gameObject.SetActive(true);


                slot.button.onClick.AddListener(() => OnRewardClicked(reward));
            }
            else
            {
                slot.titleText.text = string.Empty;
                slot.descText.text = string.Empty;
                slot.button.gameObject.SetActive(false);
            }


        }
    }

    private void OnRewardClicked(RewardSO reward)
    {
        pm.OnRewardChosen(reward);
    }

    public void DefaultUI()
    {
        levelUpPanel.SetActive(false);
        paddleSelectPanel.SetActive(false);

        gamePanel.SetActive(true);
    }

    public void UpdateScore(int score, int bumpCount, int bumpCountConsec)
    {
        gameScore.text = score.ToString();
    }

    public void PaddleSelect()
    {
        gamePanel.SetActive(false);

        paddleSelectPanel.SetActive(true);
    }

    public void ClosePaddleSelect(bool hasMoreLevels)
    {
        paddleSelectPanel.SetActive(false);

        if(hasMoreLevels)
            levelUpPanel.SetActive(true);
    }


}
