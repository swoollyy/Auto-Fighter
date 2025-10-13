using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BallXPBar : MonoBehaviour
{

    public Image xpBar;
    public Image xpBarHolder;


    public TMP_Text levelText;

    float target;
    public float reduceSpeed;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        xpBar.fillAmount = Mathf.MoveTowards(xpBar.fillAmount, target, reduceSpeed * Time.deltaTime);

    }

    public void UpdateXP(float currentXP, float maxXP, int level)
    {
        Debug.Log($"Start max XP {maxXP}");
        target = currentXP / maxXP;
        levelText.text = $"Level: {level}";
    }

}
