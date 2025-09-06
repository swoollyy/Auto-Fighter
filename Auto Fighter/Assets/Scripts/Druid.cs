using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Druid : BaseCharacter
{

    protected float hpLvlIncrease = 5.8f;
    protected float minAtkLvlIncrease = 1.8f;
    protected float maxAtkLvlIncrease = 2.5f;

    protected float endLVLinc = 2.43f;
    protected float strLVLinc = 2.22f;
    protected float agiLVLinc = 1.65f;
    protected float witLVLinc = 4.31f;
    protected float chaLVLinc = 3.86f;

    public Druid(string name, int level)
        : base(name, "Druid", level, 498.2f, 110f, 47.9f, 49.8f, 100f)
    {
        traits[TraitType.Endurance] = 6;
        traits[TraitType.Strength] = 5;
        traits[TraitType.Agility] = 3;
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

    public Druid()
    : base("", "Druid", 5, 498.2f, 110f, 47.9f, 49.8f, 100f)
    {
        traits[TraitType.Endurance] = 7;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 3;
        traits[TraitType.Wit] = 6;
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
