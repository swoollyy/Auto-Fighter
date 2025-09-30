using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Assassin : BaseCharacter
{

    protected float hpLvlIncrease = 5.78f;
    protected float minAtkLvlIncrease = 2.4f;
    protected float maxAtkLvlIncrease = 2.7f;

    protected float endLVLinc = 2.04f;
    protected float strLVLinc = 3.44f;
    protected float agiLVLinc = 4.88f;
    protected float witLVLinc = 1.54f;
    protected float chaLVLinc = 3.67f;

    public Assassin(string name, int level)
        : base(name, "Assassin", level, 463.6f, 100f, 46.2f, 53.8f, 105f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 10;
        traits[TraitType.Wit] = 4;
        traits[TraitType.Charm] = 7;
        Endurance = traits[TraitType.Endurance];
        Strength = traits[TraitType.Strength];
        Agility = traits[TraitType.Agility];
        Wit = traits[TraitType.Wit];
        Charm = traits[TraitType.Charm];


        ApplyLevelScaling();
        ApplyTraitBonuses();
    }

    public Assassin()
        : base("", "Assassin", 5, 463.6f, 100f, 46.2f, 53.8f, 105f)
    {
        traits[TraitType.Endurance] = 5;
        traits[TraitType.Strength] = 6;
        traits[TraitType.Agility] = 10;
        traits[TraitType.Wit] = 4;
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
