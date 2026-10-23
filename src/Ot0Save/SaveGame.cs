using Ot0Save.Gvas;

namespace Ot0Save;

public enum Stat { HP, SP, PhysAtk, PhysDef, ElemAtk, ElemDef, Speed, Critical, Accuracy, Evasion }

public sealed class SaveGame(GvasFile gvas)
{
    public const int ProtagonistId = 900;

    public GvasFile Gvas { get; } = gvas;

    PropertyList User => Gvas.Root.Struct("SaveDataUser");

    public int Money
    {
        get => User.Int("Money");
        set => User.SetInt("Money", value);
    }

    // PlayTick counts seconds.
    public TimeSpan PlayTime => TimeSpan.FromSeconds(User.Int("PlayTick"));

    public string ProtagonistName =>
        Gvas.Root.Get("SaveNames")?.Value is PropertyList names
        && names.Get("MainCharacterName")?.Value is FString s ? s.Value : "Protagonist";

    // 1 Warrior, 2 Apothecary, 3 Thief, 4 Scholar, 5 Cleric, 6 Hunter, 7 Merchant, 8 Dancer.
    public int ProtagonistJob => Gvas.Root.Struct("SaveDataMainCharacter").Int("JobID");

    public IReadOnlyList<int> Party
    {
        get
        {
            var first = Gvas.Root.Array("SaveDataParty").Items.OfType<PropertyList>().FirstOrDefault();
            return first?.Array("PCMsdID").Items.OfType<int>().Where(id => id != 0).ToList() ?? [];
        }
    }

    // 901-909 are the protagonist's per-job records, not real party members.
    public IEnumerable<Character> Characters =>
        Gvas.Root.Array("SaveDataPC").Items.OfType<PropertyList>()
            .Select(pc => new Character(this, pc))
            .Where(c => c.Id != 0 && c.Id is < 901 or > 909);

    public Character? Character(int id) => Characters.FirstOrDefault(c => c.Id == id);

    public Inventory Items => new(Gvas.Root.Array("SaveDataItem"));

    internal PropertyList? SkillBoard(int id) =>
        Gvas.Root.Array("SaveDataPCSkillBoard").Items.OfType<PropertyList>()
            .FirstOrDefault(b => b.Int("PCMsdID") == id);
}

public sealed class Character
{
    readonly SaveGame save;
    readonly PropertyList pc;

    internal Character(SaveGame save, PropertyList pc)
    {
        this.save = save;
        this.pc = pc;
    }

    public int Id => pc.Int("PCMsdID");
    public int Level => pc.Int("NowLv");
    public bool IsGuest => pc.Int("IsGuest") != 0;
    public bool IsHidden => pc.Int("HideFlag") != 0;

    public int Jp
    {
        get => save.SkillBoard(Id)?.Int("NowJp") ?? 0;
        set => (save.SkillBoard(Id) ?? throw new InvalidOperationException($"Character {Id} has no skill board."))
            .SetInt("NowJp", value);
    }

    // The "Additional" fields are the permanent bonuses that stat nuts add to.
    public int Bonus(Stat stat) => pc.Int(Field(stat));
    public void SetBonus(Stat stat, int value) => pc.SetInt(Field(stat), value);

    static string Field(Stat stat) => "Additional" + stat switch
    {
        Stat.HP => "HP",
        Stat.SP => "SP",
        Stat.PhysAtk => "Atk",
        Stat.PhysDef => "Def",
        Stat.ElemAtk => "MagicAtk",
        Stat.ElemDef => "MagicDef",
        Stat.Speed => "Agi",
        Stat.Critical => "Crt",
        Stat.Accuracy => "Hit",
        Stat.Evasion => "Avd",
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };
}

public sealed class Inventory
{
    readonly ArrayValue items;

    internal Inventory(ArrayValue items) => this.items = items;

    public IEnumerable<(int Id, int Count)> All =>
        Entries.Select(e => (e.Int("ItemMsdID"), e.Int("Num")));

    public bool Has(int id) => Find(id) is not null;

    public int Count(int id) => Find(id)?.Int("Num") ?? 0;

    public void Set(int id, int count)
    {
        var entry = Find(id);
        if (entry is null)
        {
            // Copy an existing entry so the new one has exactly the tags the game writes.
            var template = Entries.FirstOrDefault()
                ?? throw new InvalidOperationException("Inventory is empty, so there's no entry to copy the layout from.");
            entry = template.Clone();
            entry.SetInt("ItemMsdID", id);
            entry.SetInt("StatusFlag", 1);
            items.Items.Add(entry);
        }
        entry.SetInt("Num", count);
    }

    IEnumerable<PropertyList> Entries => items.Items.OfType<PropertyList>();

    PropertyList? Find(int id) => Entries.FirstOrDefault(e => e.Int("ItemMsdID") == id);
}
