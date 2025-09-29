using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Test : MonoBehaviour
{
    public Warrior warrior;
    public Mage mage;
    public Druid druid;
    public Assassin assassin;
    public Tank tank;

    protected Item sword;

    public CharacterUI ui;


    // Start is called before the first frame update
    void Start()
    {
        warrior = new Warrior("Jacque", 5);
        mage = new Mage("Jill", 4);

        ui.SetCharacterUI(warrior, true);
        ui.SetCharacterUI(mage, false);


        warrior.PrintStats();
        mage.PrintStats();
        warrior.Health.AddModifier(new StatModifier(5f, StatModType.Flat));
        warrior.Health.AddModifier(new StatModifier(2.5f, StatModType.Flat));
        warrior.Health.AddModifier(new StatModifier(1.9f, StatModType.PercentMult));
        warrior.Health.baseValue = warrior.Health.Value;

        Debug.Log("After Sword Equip Strength Value: " + warrior.Health.Value);
        Debug.Log("After Sword Equip Strength base Value: " + warrior.Health.baseValue);
        ui.SetCharacterUI(warrior, true);
        /*
        sword.Unequip(myCharacter.MaxAtk);
        Debug.Log("After Sword Unequip Strength Value: " + myCharacter.MaxAtk.Value);
        */
    }

    // Update is called once per frame
    void Update()
    {

    }
}
