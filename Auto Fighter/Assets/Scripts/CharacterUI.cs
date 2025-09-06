using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class CharacterUI : MonoBehaviour
{

    [Header("UI Canvases")]
    public GameObject homeHUD;
    public GameObject mainMenuHUD;
    public GameObject pinballHUD;
    public GameObject mainStoryHUD;
    public GameObject battleHUD;
    public GameObject preBattleHUD;
    public GameObject winHUD;
    public GameObject loseHUD;
    public GameObject initGameHUD;
    public GameObject initNameHUD;

    [Header("Player 1 UI")]
    public TMP_Text player1Name;
    public TMP_Text player1Class;
    public TMP_Text player1Level;
    public TMP_Text player1Stats;
    public TMP_Text player1Traits;
    [Header("Player 1 Init UI")]
    public TMP_Text player1StatsInit;
    public TMP_Text player1TraitsInit;
    public TMP_Text playerNameInput;

    [Header("Player 2 UI")]
    public TMP_Text player2Name;
    public TMP_Text player2Class;
    public TMP_Text player2Level;
    public TMP_Text player2Stats;
    public TMP_Text player2Traits;

    [Header("Combat System Information")]
    public bool winner = false;

    public void SetCharacterUI(BaseCharacter character, bool isPlayer1)
    {
        string stats = "";
        string traits = "";
        foreach (var stat in character.GetStats())
        {
            if (stat.Value == character.Health || stat.Value == character.Mana)
            {
                stats += $"{stat.Key}: {Mathf.Max(0f, stat.Value.BaseValue)} / {stat.Value.Value}\n";
            }
            else if (stat.Value == character.MinAtk)
            {
                stats += $"Min-Max Atk: {character.MinAtk.Value} - {character.MaxAtk.Value}\n";
            }
            else if (stat.Value == character.MaxAtk)
                stats += "";
            else
                stats += $"{stat.Key}: {stat.Value.Value}\n";
        }
        foreach (var trait in character.GetTraits())
        {
            traits += $"{trait.Key}: {trait.Value}\n";
        }

        if (isPlayer1)
        {
            player1Name.text = character.name;
            player1Class.text = character.charClass;
            player1Level.text = $"Lv {character.level}";
            player1Stats.text = stats;
            player1Traits.text = traits;
        }
        else
        {
            player2Name.text = character.name;
            player2Class.text = character.charClass;
            player2Level.text = $"Lv {character.level}";
            player2Stats.text = stats;
            player2Traits.text = traits;

        }

    }

    public void SetInitCharacterUI(BaseCharacter character)
    {
        string stats = "";
        string traits = "";
        foreach (var stat in character.GetStats())
        {
            if (stat.Value == character.Health || stat.Value == character.Mana)
            {
                stats += $"{stat.Key}: {stat.Value.BaseValue} / {stat.Value.Value}\n";
            }
            else if (stat.Value == character.MinAtk)
            {
                stats += $"Min-Max Atk: {character.MinAtk.Value} - {character.MaxAtk.Value}\n";
            }
            else if (stat.Value == character.MaxAtk)
                stats += "";
            else
                stats += $"{stat.Key}: {stat.Value.Value}\n";
        }
        foreach (var trait in character.GetTraits())
        {
            traits += $"{trait.Key}: {trait.Value}\n";
        }

            player1StatsInit.text = stats;
            player1TraitsInit.text = traits;
    }

    public void UpdateUI()
    {

    }

    public void HandleInitLoad()
    {
        homeHUD.SetActive(true);

        battleHUD.SetActive(false);
        preBattleHUD.SetActive(false);
        initGameHUD.SetActive(false);
        initNameHUD.SetActive(false);
        winHUD.SetActive(false);
        loseHUD.SetActive(false);
        mainStoryHUD.SetActive(false);
        mainMenuHUD.SetActive(false);
    }
    public void HandleChooseCharacter()
    {
        initGameHUD.SetActive(true);

        homeHUD.SetActive(false);
    }

    public void HandlePreBattle()
    {
        battleHUD.SetActive(true);
        preBattleHUD.SetActive(true);

        initNameHUD.SetActive(false);
        mainStoryHUD.SetActive(false);
    }

    public void HandlePinball()
    {
        pinballHUD.SetActive(true);

        mainMenuHUD.SetActive(false);
    }

    public void HandleInitName()
    {
        initNameHUD.SetActive(true);

        initGameHUD.SetActive(false);
    }

    public void HandleMainMenu()
    {
        mainMenuHUD.SetActive(true);

        DisableAllButMM();
    }

    public void HandleBackToMM()
    {
        mainMenuHUD.SetActive(true);

        mainStoryHUD.SetActive(false);
    }

    public void HandleMainStory()
    {
        mainStoryHUD.SetActive(true);

        mainMenuHUD.SetActive(false);
    }

    public void HandleBattle()
    {
        battleHUD.SetActive(true);

        mainMenuHUD.SetActive(false);
        preBattleHUD.SetActive(false);
    }

    public void HandleBattleFinished()
    {
        if (winner)
            winHUD.SetActive(true);
        else
            loseHUD.SetActive(true);

        battleHUD.SetActive(false);
        preBattleHUD.SetActive(false);
    }

    public void DisableMenu()
    {
            winHUD.SetActive(false);
        loseHUD.SetActive(false);
    }

    public void DisableAllButMM()
    {
        battleHUD.SetActive(false);
        preBattleHUD.SetActive(false);
        initGameHUD.SetActive(false);
        initNameHUD.SetActive(false);
        winHUD.SetActive(false);
        loseHUD.SetActive(false);
        mainStoryHUD.SetActive(false);
        homeHUD.SetActive(false);
    }

    public void SetPlayerName(BaseCharacter player)
    {
        player.name = playerNameInput.text;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
