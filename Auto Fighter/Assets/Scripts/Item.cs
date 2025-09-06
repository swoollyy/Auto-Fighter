public class Item
{
    public void Equip(CharacterStat c)
    {
        c.AddModifier(new StatModifier(10, StatModType.Flat, this));
        c.AddModifier(new StatModifier(.1f, StatModType.PercentMult, this));
    }

    public void Unequip(CharacterStat c)
    {
        c.RemoveAllModifiersFromSource(this);
    }

}
