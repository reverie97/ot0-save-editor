using System.Text.Json;
using Ot0Save;
using Ot0Save.Gvas;

namespace Ot0Edit;

static class Commands
{
    static readonly Dictionary<string, Stat> StatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hp"] = Stat.HP, ["sp"] = Stat.SP,
        ["patk"] = Stat.PhysAtk, ["pdef"] = Stat.PhysDef,
        ["eatk"] = Stat.ElemAtk, ["edef"] = Stat.ElemDef,
        ["spd"] = Stat.Speed, ["speed"] = Stat.Speed,
        ["crit"] = Stat.Critical, ["acc"] = Stat.Accuracy, ["eva"] = Stat.Evasion,
    };

    static SaveSession Open(Options o)
    {
        var path = o.File
            ?? (o.Slot is int n ? SaveLocator.Slot(n) ?? throw new UsageException($"no SaveData{n}.sav found")
                                : SaveLocator.Newest() ?? throw new UsageException("couldn't find any OT0 saves; use --file"));
        if (!File.Exists(path))
            throw new UsageException($"{path} doesn't exist");
        return SaveSession.Open(path, o.Oodle);
    }

    static void Commit(SaveSession s, Options o)
    {
        if (o.DryRun)
        {
            s.PackChecked();
            Console.WriteLine("dry run, nothing written");
            return;
        }
        var backup = s.Commit(backup: !o.NoBackup);
        if (backup is not null)
            Console.WriteLine($"backup: {backup}");
        Console.WriteLine($"saved {Path.GetFileName(s.Path)}");
    }

    public static int Info(Options o)
    {
        using var s = Open(o);
        var save = s.Save;
        var data = GameData.Load();
        var t = save.PlayTime;

        Console.WriteLine(s.Path);
        Console.WriteLine($"play time {(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}   money {save.Money:N0}");

        var party = save.Party;
        Console.WriteLine();
        Console.WriteLine("party");
        foreach (var id in party)
            if (save.Character(id) is { } c)
                PrintCharacter(save, data, c);

        var bench = save.Characters.Where(c => !party.Contains(c.Id) && !c.IsGuest && !c.IsHidden).ToList();
        if (bench.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("not in party");
            foreach (var c in bench)
                PrintCharacter(save, data, c);
        }
        return 0;
    }

    static void PrintCharacter(SaveGame save, GameData data, Character c)
    {
        var bonuses = Enum.GetValues<Stat>()
            .Where(st => c.Bonus(st) != 0)
            .Select(st => $"{StatNames.First(kv => kv.Value == st).Key}+{c.Bonus(st)}");
        var name = data.CharacterName(save, c.Id);
        if (name == c.Id.ToString())
            name = "?";
        Console.WriteLine($"  {name,-10} {c.Id,6}  Lv {c.Level,-3} JP {c.Jp,7:N0}  {string.Join(" ", bonuses)}");
    }

    public static int Money(Options o)
    {
        int amount = o.NumberArg(1, "amount");
        if (amount < 0)
            throw new UsageException("money can't be negative");

        using var s = Open(o);
        Console.WriteLine($"money {s.Save.Money:N0} -> {amount:N0}");
        s.Save.Money = amount;
        Commit(s, o);
        return 0;
    }

    public static int Jp(Options o)
    {
        string who = o.Arg(1, "who (party, all, a name or an ID)");
        int amount = o.NumberArg(2, "amount");

        using var s = Open(o);
        var data = GameData.Load();
        foreach (var c in Resolve(s.Save, data, who))
        {
            Console.WriteLine($"{data.CharacterName(s.Save, c.Id)}: JP {c.Jp:N0} -> {amount:N0}");
            c.Jp = amount;
        }
        Commit(s, o);
        return 0;
    }

    public static int StatCmd(Options o)
    {
        string who = o.Arg(1, "who (party, all, a name or an ID)");
        string which = o.Arg(2, "stat");
        int value = o.NumberArg(3, "value");

        // "all" leaves HP out on purpose: its bonus caps at +3000, the others at 999.
        var stats = which.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? Enum.GetValues<Stat>().Where(st => st != Stat.HP).ToList()
            : [StatNames.TryGetValue(which, out var st) ? st : throw new UsageException($"unknown stat '{which}'")];

        using var s = Open(o);
        var data = GameData.Load();
        foreach (var c in Resolve(s.Save, data, who))
        {
            foreach (var stat in stats)
                c.SetBonus(stat, value);
            Console.WriteLine($"{data.CharacterName(s.Save, c.Id)}: {which} bonus set to {value}");
        }
        Commit(s, o);
        return 0;
    }

    public static int Item(Options o)
    {
        var data = GameData.Load();
        int id = ResolveItem(data, o.Arg(1, "item ID or name"));
        using var s = Open(o);
        var items = s.Save.Items;

        if (o.Args.Count < 3)
        {
            Console.WriteLine($"{id} {data.ItemName(id)}: {items.Count(id)}");
            return 0;
        }

        int count = o.NumberArg(2, "count");
        if (!items.Has(id) && !o.Force)
        {
            if (!data.Items.ContainsKey(id))
                throw new UsageException(
                    $"item {id} isn't in this save or the item list. An ID the game doesn't have can break the save. " +
                    "Pass --force if you're sure.");
            if (data.IsRiskyToAdd(id))
                throw new UsageException(
                    $"{data.ItemName(id)} is a {data.ItemType(id).ToLowerInvariant()}. Adding one you haven't earned can " +
                    "mess with story or Wishvale progress. Pass --force if you're sure.");
        }
        if (count > 99)
            Console.WriteLine("note: most items stop at 99 in game");

        Console.WriteLine($"{id} {data.ItemName(id)}: {items.Count(id)} -> {count}" + (items.Has(id) ? "" : " (new)"));
        items.Set(id, count);
        Commit(s, o);
        return 0;
    }

    public static int Items(Options o)
    {
        using var s = Open(o);
        var data = GameData.Load();
        foreach (var (id, count) in s.Save.Items.All.OrderBy(i => i.Id))
            Console.WriteLine($"{id,7}  x{count,-4} {data.ItemType(id),-15} {data.ItemName(id)}{Note(data, id)}");
        return 0;
    }

    // Searches the item list, not the save, so it also shows things you don't have yet.
    public static int Find(Options o)
    {
        var data = GameData.Load();
        string text = o.Arg(1, "text to search for");
        var hits = data.Items
            .Where(kv => kv.Key.ToString().Contains(text)
                         || kv.Value.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
                         || kv.Value.Type?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)
            .Select(kv => kv.Key).Order().ToList();
        if (hits.Count == 0)
        {
            Console.WriteLine($"nothing matches '{text}'");
            return 1;
        }

        Inventory? inventory = null;
        if (SaveLocator.Newest() is not null || o.File is not null || o.Slot is not null)
        {
            using var s = Open(o);
            inventory = s.Save.Items;
            foreach (var id in hits)
                Console.WriteLine($"{id,7}  {(inventory.Has(id) ? "x" + inventory.Count(id) : "-"),-5} {data.ItemType(id),-15} {data.ItemName(id)}{Note(data, id)}");
        }
        else
        {
            foreach (var id in hits)
                Console.WriteLine($"{id,7}  {data.ItemType(id),-15} {data.ItemName(id)}{Note(data, id)}");
        }
        return 0;
    }

    static string Note(GameData data, int id) => data.ItemNote(id) is { Length: > 0 } n ? $" ({n})" : "";

    static int ResolveItem(GameData data, string text)
    {
        var hits = data.FindItems(text);
        if (hits.Count == 1)
            return hits[0];
        if (hits.Count == 0)
            throw new UsageException($"no item called '{text}'. Try 'ot0edit find {text}'");

        var list = string.Join(", ", hits.Take(8).Select(id => $"{id} {data.ItemName(id)}"));
        throw new UsageException($"'{text}' matches {hits.Count} items ({list}{(hits.Count > 8 ? ", ..." : "")}). Use the ID instead");
    }

    public static int Verify(Options o)
    {
        using var s = Open(o);
        if (s.CheckRoundTrip() is { } problem)
        {
            Console.WriteLine($"FAIL: {problem}");
            return 1;
        }

        Console.WriteLine($"ok: {s.OriginalGvas.Length:N0} bytes rebuild exactly");
        int fallbacks = CountFallbacks(s.Save.Gvas.Root);
        if (fallbacks > 0)
            Console.WriteLine($"{fallbacks} values couldn't be parsed and were kept as raw bytes");
        Console.WriteLine($"oodle: {s.Oodle.DllPath}");
        return 0;
    }

    public static int Dump(Options o)
    {
        string outPath = o.Arg(1, "output .json path");
        using var s = Open(o);
        var json = JsonDump.ToJson(s.Save.Gvas.Root)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outPath, json);
        Console.WriteLine($"wrote {outPath}");
        return 0;
    }

    public static int Unpack(Options o)
    {
        string outPath = o.Arg(1, "output .gvas path");
        using var s = Open(o);
        File.WriteAllBytes(outPath, s.OriginalGvas);
        Console.WriteLine($"wrote {outPath} ({s.OriginalGvas.Length:N0} bytes)");
        return 0;
    }

    public static int Pack(Options o)
    {
        string inPath = o.Arg(1, "input .gvas path");
        using var s = Open(o);
        s.Save = new SaveGame(GvasFile.Parse(File.ReadAllBytes(inPath)));
        Commit(s, o);
        return 0;
    }

    static List<Character> Resolve(SaveGame save, GameData data, string who)
    {
        if (who.Equals("party", StringComparison.OrdinalIgnoreCase))
            return save.Party.Select(save.Character).OfType<Character>().ToList();
        if (who.Equals("all", StringComparison.OrdinalIgnoreCase))
            return save.Characters.Where(c => !c.IsGuest && !c.IsHidden).ToList();
        if (int.TryParse(who, out var id))
            return [save.Character(id) ?? throw new UsageException($"no character {id} in this save")];

        var match = save.Characters.FirstOrDefault(c => data.CharacterName(save, c.Id).Equals(who, StringComparison.OrdinalIgnoreCase));
        return match is not null ? [match] : throw new UsageException($"no character called '{who}'");
    }

    static int CountFallbacks(object? v) => v switch
    {
        RawValue raw => raw.Fallback ? 1 : 0,
        PropertyList props => props.Sum(p => CountFallbacks(p.Value)),
        ArrayValue a => a.Items.Sum(CountFallbacks),
        SetValue set => set.Items.Sum(CountFallbacks),
        MapValue m => m.Entries.Sum(e => CountFallbacks(e.Key) + CountFallbacks(e.Value)),
        _ => 0,
    };
}
