using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Warrior : BaseCharacter
{

    protected float hpLvlIncrease = 7.1f;
    protected float minAtkLvlIncrease = 2.1f;
    protected float maxAtkLvlIncrease = 2.2f;

    protected float endLVLinc = 4.57f;
    protected float strLVLinc = 3.93f;
    protected float agiLVLinc = 2.36f;
    protected float witLVLinc = 1.68f;
    protected float chaLVLinc = 2.07f;


    public Warrior(string name, int level)
        : base(name, "Warrior", level, 539.4f, 100f, 52.3f, 60.1f, 100f)
    {
        traits[TraitType.Endurance] = 9;
        traits[TraitType.Strength] = 8;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 3;
        traits[TraitType.Charm] = 5;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }
    public Warrior()
    : base("", "Warrior", 5, 539.4f, 100f, 52.3f, 60.1f, 100f)
    {
        traits[TraitType.Endurance] = 9;
        traits[TraitType.Strength] = 8;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 3;
        traits[TraitType.Charm] = 5;
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
