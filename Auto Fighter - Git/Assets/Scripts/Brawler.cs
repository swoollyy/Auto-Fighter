using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Brawler : BaseCharacter
{

    protected float hpLvlIncrease = 6.7f;
    protected float minAtkLvlIncrease = 2.4f;
    protected float maxAtkLvlIncrease = 2.9f;

    protected float endLVLinc = 2.48f;
    protected float strLVLinc = 4.37f;
    protected float agiLVLinc = 2.26f;
    protected float witLVLinc = .74f;
    protected float chaLVLinc = 1.82f;


    public Brawler(string name, int level)
        : base(name, "Brawler", level, 514.9f, 95f, 56.2f, 62.8f, 100f)
    {
        traits[TraitType.Endurance] = 7;
        traits[TraitType.Strength] = 10;
        traits[TraitType.Agility] = 6;
        traits[TraitType.Wit] = 2;
        traits[TraitType.Charm] = 4;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public Brawler()
    : base("", "Brawler", 5, 514.9f, 95f, 56.2f, 62.8f, 100f)
    {
        traits[TraitType.Endurance] = 7;
        traits[TraitType.Strength] = 10;
        traits[TraitType.Agility] = 6;
        traits[TraitType.Wit] = 2;
        traits[TraitType.Charm] = 4;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];

        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public override void ApplyLevelScaling()
    {
        stats[StatType.Health].baseValue += ((level) * hpLvlIncrease);
        stats[StatType.MinAtk].baseValue += ((level) * minAtkLvlIncrease);
        stats[StatType.MaxAtk].baseValue += ((level) * maxAtkLvlIncrease);

        traits[TraitType.Endurance] += ((level) * endLVLinc);
        traits[TraitType.Strength] += ((level) * strLVLinc);
        traits[TraitType.Agility] += ((level) * agiLVLinc);
        traits[TraitType.Wit] += ((level) * witLVLinc);
        traits[TraitType.Charm] += ((level) * chaLVLinc);

    }
}
