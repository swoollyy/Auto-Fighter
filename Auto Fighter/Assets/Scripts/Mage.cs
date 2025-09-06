using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mage : BaseCharacter
{

    protected float hpLvlIncrease = 4.9f;
    protected float minAtkLvlIncrease = 2.6f;
    protected float maxAtkLvlIncrease = 2.9f;

    protected float endLVLinc = 2.54f;
    protected float strLVLinc = 2.26f;
    protected float agiLVLinc = 1.69f;
    protected float witLVLinc = 4.65f;
    protected float chaLVLinc = 3.84f;

    public Mage(string name, int level)
        : base(name, "Mage", level, 487.8f, 120f, 51.9f, 56.2f, 100f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 10;
        traits[TraitType.Charm] = 6;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public Mage()
    : base("", "Mage", 5, 487.8f, 120f, 51.9f, 56.2f, 100f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 5;
        traits[TraitType.Wit] = 9;
        traits[TraitType.Charm] = 6;
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
