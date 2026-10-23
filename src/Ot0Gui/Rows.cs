using System.IO;
using Ot0Save;

namespace Ot0Gui;

sealed class SlotRow(string path)
{
    public string Path { get; } = path;
    public string Display => $"{System.IO.Path.GetFileName(Path)}  ({File.GetLastWriteTime(Path):MMM d, h:mm tt})";
}

sealed class CharacterRow : Observable
{
    readonly Character c;
    readonly Action changed;

    public CharacterRow(Character c, string name, bool inParty, Action changed)
    {
        this.c = c;
        this.changed = changed;
        Name = name;
        InParty = inParty;
        // Order matches the Stat enum, which the grid columns index into.
        Stats = Enum.GetValues<Stat>().Select(s => new StatRow(c, s, changed)).ToList();
    }

    public string Name { get; }
    public int Id => c.Id;
    public int Level => c.Level;
    public bool InParty { get; }
    public IReadOnlyList<StatRow> Stats { get; }

    public int Jp
    {
        get => c.Jp;
        set
        {
            if (c.Jp == value) return;
            c.Jp = Math.Max(0, value);
            Raise();
            changed();
        }
    }

    public void SetAll(bool max)
    {
        foreach (var s in Stats)
            s.Value = max ? s.Cap : 0;
    }
}

sealed class StatRow(Character c, Stat stat, Action changed) : Observable
{
    public Stat Stat { get; } = stat;

    // HP's bonus stops at +3000 in game, everything else shows at most 999.
    public int Cap => Stat == Stat.HP ? 3000 : 999;

    public int Value
    {
        get => c.Bonus(Stat);
        set
        {
            if (c.Bonus(Stat) == value) return;
            c.SetBonus(Stat, Math.Max(0, value));
            Raise();
            changed();
        }
    }
}

// One row per known item, owned or not. Typing a count into a row you don't own adds the
// item, after canAdd gets a say.
sealed class ItemRow(Inventory inventory, int id, GameData data, Action changed, Func<ItemRow, bool> canAdd) : Observable
{
    public int Id { get; } = id;
    public string Name { get; } = data.ItemName(id);
    public string Type { get; } = data.ItemType(id);
    public string Notes { get; } = data.ItemNote(id);

    public bool Owned => inventory.Has(Id);

    public int Count
    {
        get => inventory.Count(Id);
        set
        {
            value = Math.Max(0, value);
            if (inventory.Count(Id) == value) return;
            if (!Owned && (value == 0 || !canAdd(this)))
            {
                Raise(); // puts the cell back to 0
                return;
            }
            inventory.Set(Id, value);
            Raise();
            Raise(nameof(Owned));
            changed();
        }
    }
}
