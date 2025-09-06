using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Tank : BaseCharacter
{

    protected float hpLvlIncrease = 11.3f;
    protected float minAtkLvlIncrease = 1.7f;
    protected float maxAtkLvlIncrease = 2.04f;

    protected float endLVLinc = 4.94f;
    protected float strLVLinc = 2.68f;
    protected float agiLVLinc = 1.08f;
    protected float witLVLinc = 1.93f;
    protected float chaLVLinc = 3.27f;


    public Tank(string name, int level)
        : base(name, "Tank", level, 624.9f, 100f, 44.8f, 49.9f, 98f)
    {
        traits[TraitType.Endurance] = 10;
        traits[TraitType.Strength] = 5;
        traits[TraitType.Agility] = 2;
        traits[TraitType.Wit] = 5;
        traits[TraitType.Charm] = 7;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();

    }
    public Tank()
    : base("", "Tank", 5, 624.9f, 100f, 44.8f, 49.9f, 98f)
    {
        traits[TraitType.Endurance] = 10;
        traits[TraitType.Strength] = 5;
        traits[TraitType.Agility] = 2;
        traits[TraitType.Wit] = 5;
        traits[TraitType.Charm] = 7;
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
