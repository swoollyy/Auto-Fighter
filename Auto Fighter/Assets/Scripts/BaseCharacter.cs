using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum TraitType
{
    Endurance, //HP, Def, Res
    Strength, //MinAtk, MaxAtk, Break
    Agility, //Speed, Evasion, Crit
    Wit, //Intelligence, Mana, Luck
    Charm, // Luck, Lifesteal
}
public enum StatType
{
    Health, //E
    Mana, //W
    MinAtk, //S
    MaxAtk, //S
    Accuracy,
    Speed, // A
    Defense, //E
    Resistance, //E
    Evasion, //A
    Critical, // A
    Break, // S
    Intelligence, // W
    Luck, // C
    Lifesteal // C
}

public class BaseCharacter
{



    public string name;
    public string charClass { get; private set; }
    public int level { get; private set; }

    public float TurnMeter { get; private set; } = 0f;
    public void ConsumeTurnMeter() => TurnMeter = 0;
    public bool IsTurnReady => TurnMeter >= 100f * (((previousHits + 1) * 1.35f) * 2f);
    public int previousHits = 0;
    public void FillTurnMeter() => TurnMeter += Speed.Value;

    public  Dictionary<StatType, CharacterStat> stats = new();
    public  Dictionary<TraitType, float> traits = new();

    public CharacterStat Health => stats[StatType.Health];
    public CharacterStat Mana => stats[StatType.Mana];
    public CharacterStat MinAtk => stats[StatType.MinAtk];
    public CharacterStat MaxAtk => stats[StatType.MaxAtk];
    public CharacterStat Speed=> stats[StatType.Speed];
    public CharacterStat Defense => stats[StatType.Defense];
    public CharacterStat Accuracy => stats[StatType.Accuracy];
    public CharacterStat Critical => stats[StatType.Critical];
    public CharacterStat Break => stats[StatType.Break];
    public CharacterStat Evasion => stats[StatType.Evasion];
    public CharacterStat Resistance => stats[StatType.Resistance];
    public CharacterStat Luck => stats[StatType.Luck];

    public float Endurance;
    public float Strength;
    public float Agility;
    public float Wit;
    public float Charm;



    public BaseCharacter()
    {
        name = string.Empty;
        charClass = string.Empty;
        level = 0;
        InitializeStats();
        InitializeTraits();
    }

    public BaseCharacter(string characterName, string characterClass, int characterLevel, float hp, float minAtk, float maxAtk)
    {
        name = characterName;
        charClass = characterClass;
        level = characterLevel;

        InitializeStats();
        InitializeTraits();

        stats[StatType.Health].baseValue = hp;
        stats[StatType.MinAtk].baseValue = minAtk;
        stats[StatType.MaxAtk].baseValue = maxAtk;
    }

    public BaseCharacter(string characterName, string characterClass, int characterLevel, float hp, float mp, float minAtk, float maxAtk)
    {
        name = characterName;
        charClass = characterClass;
        level = characterLevel;

        InitializeStats();
        InitializeTraits();

        stats[StatType.Health].baseValue = hp;
        stats[StatType.Mana].baseValue = mp;
        stats[StatType.MinAtk].baseValue = minAtk;
        stats[StatType.MaxAtk].baseValue = maxAtk;
    }

    public BaseCharacter(string characterName, string characterClass, int characterLevel, float hp, float mp, float minAtk, float maxAtk, float spd)
    {
        name = characterName;
        charClass = characterClass;
        level = characterLevel;

        InitializeStats();
        InitializeTraits();
        stats[StatType.Health].baseValue = hp;
        stats[StatType.Mana].baseValue = mp;
        stats[StatType.MinAtk].baseValue = minAtk;
        stats[StatType.MaxAtk].baseValue = maxAtk;
        stats[StatType.Speed].baseValue = spd;
    }

    public static BaseCharacter CreateCharacterFromClass(string className)
    {
        switch (className)
        {
            case "Warrior": return new Warrior();
            case "Mage": return new Mage();
            case "Druid": return new Druid();
            case "Assassin": return new Assassin();
            case "Tank": return new Tank();
            default: return new BaseCharacter();
        }
    }


    public virtual void InitializeStats()
    {

        foreach(StatType type in System.Enum.GetValues(typeof(StatType)))
        {
            stats[type] = new CharacterStat(0f);
        }

        stats[StatType.Health].baseValue = 100f;
        stats[StatType.Mana].baseValue = 100f;
        stats[StatType.MinAtk].baseValue = 50f;
        stats[StatType.MaxAtk].baseValue = 50f;
        stats[StatType.Speed].baseValue = 100f;
        stats[StatType.Defense].baseValue = 0f;
    }

    public virtual void InitializeRandomStats(Dictionary<StatType, CharacterStat> p1Stats)
    {

        foreach (StatType type in System.Enum.GetValues(typeof(StatType)))
        {
            float offset1 = Random.Range(-.05f, .05f);
            float offset2 = Random.Range(-.05f, .05f);
            float min = Mathf.Min(offset1, offset2);
            float max = Mathf.Max(offset1, offset2);
            float randomizedValue = p1Stats[type].Value * Random.Range(min, max);
            float finalValue = Mathf.Round((p1Stats[type].Value + randomizedValue) * 10f) / 10f;
            this.stats[type] = new CharacterStat(Mathf.Max(0.1f, finalValue));
            Debug.Log($"Random Value Generated - {randomizedValue} : New Stat Value {this.stats[type].Value}\nMin # - {min} : Max # - {max}");

        }
        this.RefillAllVitals(); 
    }

    public virtual void InitializeTraits()
    {
        foreach (TraitType trait in System.Enum.GetValues(typeof(TraitType)))
        {
            traits[trait] = 0;
        }
    }

    public virtual void InitializeRandomTraits(Dictionary<TraitType, float> p1Traits)
    {

        foreach (TraitType trait in System.Enum.GetValues(typeof(TraitType)))
        {
            float offset1 = Random.Range(-.05f, .05f);
            float offset2 = Random.Range(-.05f, .05f);

            float min = Mathf.Min(offset1, offset2);
            float max = Mathf.Max(offset1, offset2);

            float randomizedValue = p1Traits[trait] * Random.Range(min, max);
            float finalValue = Mathf.Round(p1Traits[trait] + randomizedValue);
            this.traits[trait] = Mathf.Max(0, finalValue);
        }
    }

    public virtual void ApplyTraitBonuses()
    {
        stats[StatType.Health].baseValue += traits[TraitType.Endurance] * .01f; //1% endurance value
        stats[StatType.Defense].baseValue += traits[TraitType.Endurance] * 0.5f; //half endurance value
        stats[StatType.Resistance].baseValue += traits[TraitType.Endurance] * 0.3f; //30% endurance value... etc.

        stats[StatType.MinAtk].baseValue += traits[TraitType.Strength] * 1f;
        stats[StatType.MaxAtk].baseValue += traits[TraitType.Strength] * 2f;
        stats[StatType.Break].baseValue += traits[TraitType.Strength] * 0.4f;

        stats[StatType.Speed].baseValue += traits[TraitType.Agility] * 1.2f;
        stats[StatType.Evasion].baseValue += traits[TraitType.Agility] * 0.5f;
        stats[StatType.Critical].baseValue += traits[TraitType.Agility] * 0.25f;

        stats[StatType.Intelligence].baseValue += traits[TraitType.Wit] * 1.5f;
        stats[StatType.Luck].baseValue += traits[TraitType.Wit] * 0.5f;
        stats[StatType.Accuracy].baseValue += traits[TraitType.Wit] * 2f;


        stats[StatType.Luck].baseValue += traits[TraitType.Charm] * 0.05f;
        stats[StatType.Lifesteal].baseValue += traits[TraitType.Charm] * 0.4f;
        stats[StatType.Accuracy].baseValue += traits[TraitType.Charm] * .8f;

        stats[StatType.Critical].baseValue += stats[StatType.Luck].baseValue * 0.25f;

    }

    public virtual void ApplyLevelScaling() { }

    public Dictionary<StatType, CharacterStat> GetStats()
    {
        return stats;
    }

    public Dictionary<TraitType, float> GetTraits()
    {
        return traits;
    }

    public virtual void PrintStats()
    {
        Debug.Log($"Name: {name}");
        Debug.Log($"Class: {charClass}");
        Debug.Log($"Level: {level}");
        Debug.Log($"base health: {stats[StatType.Health].baseValue}");

        foreach( var stat in stats)
        {
            Debug.Log($"{stat.Key} Value: {stat.Value.Value}");
        }
    }

    public void RefillAllVitals()
    {
        this.stats[StatType.Health].RefillToMax();
        this.stats[StatType.Mana].RefillToMax();
    }

    public void RandomizeCharacter(int playerLevel, Dictionary<StatType, CharacterStat> playerStats, Dictionary<TraitType, float> playerTraits)
    {
        this.name = "Generated";
        this.level = Random.Range(playerLevel - 2, playerLevel + 3);
        InitializeRandomStats(playerStats);
        Debug.Log($"Base Value - {this.Health.baseValue}");
        InitializeRandomTraits(playerTraits);
        Debug.Log($"Base Value - {this.Health.baseValue}");
        this.ApplyLevelScaling();
        Debug.Log($"Base Value - {this.Health.baseValue}");
        this.ApplyTraitBonuses();
        Debug.Log($"Base Value - {this.Health.baseValue}");
    }





}
