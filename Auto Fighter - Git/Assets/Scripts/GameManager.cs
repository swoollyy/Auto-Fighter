using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Game.Combat;

public enum GameState
{
    InitLoad,
    ChooseCharacter,
    BeginTutorial,
    MainMenu,
    Pinball,
    MainStory,
    PreBattle,
    Battle,
    BattleFinished
}

public enum GameMode
{
    None,
    Tutorial,
    MainStory,
    Pinball
}


public class GameManager : MonoBehaviour
{

    protected CombatSystem combatSystem;

    private GameState currentState;
    public GameState CurrentState => currentState;

    private GameMode currentMode;
    public GameMode CurrentMode => currentMode;

    public Warrior warrior;
    public Mage mage;
    public Druid druid;
    public Assassin assassin;
    public Tank tank;
    public Brawler brawler;


    protected BaseCharacter currentTurn;
    protected BaseCharacter tempChar;

    public CharacterUI ui;

    bool firstTurn;
    bool stopIt;

    protected int turnCount;

    private float actionTimer = 2f;
    public float actionDelay = 2f;

    public BaseCharacter player1;
    public BaseCharacter player2;


    // Start is called before the first frame update
    void Start()
    {
        ChangeState(GameState.InitLoad);
        ChangeMode(GameMode.None);

        /*warrior = new Warrior("Jacque", 5);
        mage = new Mage("Jill", 4);
        druid = new Druid("Lacroix", 6);
        assassin = new Assassin("Jinga", 5);
        tank = new Tank("Ronald", 7);
        player1 = warrior;
        */





    }

    // Update is called once per frame
    void Update()
    {

        //auto-battling function
        if(currentState == GameState.Battle)
        {
            if (combatSystem != null)
            {
                actionTimer += Time.deltaTime;
                if (actionTimer >= actionDelay)
                {
                    AutoBattle();
                    actionTimer = 0;
                }

                if (player1 != null && player2 != null)
                if (combatSystem.upcomingTurns.Count < 8 && (player1.Health.BaseValue > 0 && player2.Health.BaseValue > 0))
                {
                    combatSystem.DetermineTurns();
                }



                if (stopIt)
                    for (int i = 0; i < combatSystem.upcomingTurns.Count; i++)
                    {
                        Debug.Log($"Turn {i} - {combatSystem.upcomingTurns[i].name}");
                        if (i == 7)
                            stopIt = false;
                    }
            }
        }
        else if(currentState == GameState.BattleFinished)
        {
            actionTimer = 2;
            stopIt = false;
        }
    }

    public void ChangeState(GameState newState)
    {
        currentState = newState;

        switch (newState)
        {
            case GameState.InitLoad:
                HandleInitLoad();
                break;
            case GameState.ChooseCharacter:
                HandleChooseCharacter();
                break;
            case GameState.MainMenu:
                HandleMainMenu();
                break;
            case GameState.Pinball:
                HandlePinball();
                break;
            case GameState.MainStory:
                HandleMainStory();
                break;
            case GameState.PreBattle:
                HandlePreBattle();
                break;
            case GameState.Battle:
                HandleBattle();
                break;
            case GameState.BattleFinished:
                HandleBattleFinished();
                break;
        }
    }

    public void ChangeMode(GameMode newMode)
    {
        currentMode = newMode;

        switch (newMode)
        {
            case GameMode.None:
                break;
            case GameMode.Tutorial:
                break;
            case GameMode.MainStory:
                break;
            case GameMode.Pinball:
                break;
        }
    }

    public void HandleInitLoad()
    {
        ui.HandleInitLoad();
    }
    public void HandleChooseCharacter()
    {
        ui.HandleChooseCharacter();
    }
    public void HandleMainMenu()
    {
        ui.DisableMenu();
        ui.HandleMainMenu();
    }

    public void HandleMainStory()
    {
        ui.HandleMainStory();
    }

    public void HandleBattleFinished()
    {
        ui.HandleBattleFinished();
    }

    public void HandlePreBattle()
    {
        if (currentMode == GameMode.Tutorial)
        {
            player2 = new BaseCharacter();
            player2.name = "Dummy";
            ui.SetCharacterUI(player2, false);

        }
        else if (currentMode == GameMode.MainStory)
        {
            player2 = new BaseCharacter();
            player2.name = "1-1";
            ui.SetCharacterUI(player2, false);
        }
        else Debug.Log($"Bozo");
            ui.SetCharacterUI(player1, true);
        ui.HandlePreBattle();
        combatSystem = new CombatSystem();
    }
    public void HandleBattle()
    {
        ui.HandleBattle();
    }

    public void HandlePinball()
    {
        ui.HandlePinball();
    }



    public void OnBeginButtonPressed()
    {
        ChangeState(GameState.ChooseCharacter);
    }

    public void OnHomeButtonPressed()
    {
        ChangeMode(GameMode.None);
        ChangeState(GameState.MainMenu);
    }
    public void OnMainStoryButtonPressed()
    {
        ChangeState(GameState.MainStory);
    }
    public void OnMSBattleButtonPressed()
    {
        ChangeMode(GameMode.MainStory);
        ChangeState(GameState.PreBattle);
    }

    public void OnWarriorButtonPressed()
    {
        tempChar = new Warrior();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnMageButtonPressed()
    {
        tempChar = new Mage();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnDruidButtonPressed()
    {
        tempChar = new Druid();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnTankButtonPressed()
    {
        tempChar = new Tank();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnAssassinButtonPressed()
    {
        tempChar = new Assassin();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnBrawlerButtonPressed()
    {
        tempChar = new Brawler();
        ui.SetInitCharacterUI(tempChar);
    }
    public void OnCharConfirmButtonPressed()
    {
        if(tempChar != null)
        {
            player1 = tempChar;
            ui.HandleInitName();
        }
    }
    public void OnNameConfirmButtonPressed()
    {
        ui.SetPlayerName(player1);
        ChangeMode(GameMode.Tutorial);
        ChangeState(GameState.PreBattle);
    }

    public void OnStartBattleButtonPressed()
    {
        ChangeState(GameState.Battle);
    }

    public void StartBattle()
    {
        ChangeState(GameState.Battle);
    }


    public void AutoBattle()
    {
        turnCount++;
        if(!firstTurn)
        {
            //player2 = GenerateEnemy(player1);
            combatSystem.Initialize(player1, player2);
            currentTurn = combatSystem.DetermineFirstTurn();
            firstTurn = true;
        }


        stopIt = true;
        HandleTurn();
    }

    public void FinishBattle(BaseCharacter winner)
    {
        //StopAutoBattle();
        if(winner == player1)
            ui.winner = true;
        else
            ui.winner = false;
        ChangeState(GameState.BattleFinished);
        turnCount = 0;
    }


    public void HandleTurn()
    {
        if(turnCount >= 2)
        {
            currentTurn = combatSystem.upcomingTurns[0];
            combatSystem.upcomingTurns.RemoveAt(0);
        }

        BaseCharacter attacker = currentTurn;
        BaseCharacter defender = currentTurn == player1 ? player2 : player1;

        combatSystem.ExecuteAttack(attacker, defender);
        ui.SetCharacterUI(player1, true);
        ui.SetCharacterUI(player2, false);

        //the attacker killed the defender first
        if(defender.Health.BaseValue <= 0)
        {
            FinishBattle(attacker);
        }
        //the attacker hit the defender, but the defender survived and the attacker died to recoil dmg at some point
        else if(attacker.Health.BaseValue <= 0 && defender.Health.BaseValue > 0)
        {
            FinishBattle(defender);
        }


    }



    private BaseCharacter GenerateEnemy(BaseCharacter player1)
    {
        string[] allClasses = new string[] { "Warrior", "Mage", "Assassin", "Druid", "Tank" };
        string chosenClass = allClasses[Random.Range(0, allClasses.Length)];
        BaseCharacter player2 = BaseCharacter.CreateCharacterFromClass(chosenClass);

        player2.RandomizeCharacter(player1.level, player1.stats, player1.traits);

        Debug.Log(player2.Health.BaseValue);
        ui.SetCharacterUI(player2, false);

        return player2;

    }

}
