using Ot0Edit;

const string Usage = """
    usage: ot0edit <command> [args] [options]

    commands
      info                          play time, money, party, JP and stat bonuses
      money <amount>                set money
      jp <who> <amount>             set unspent JP
      stat <who> <stat> <value>     set a permanent stat bonus (what nuts add to)
      item <id|name> [count]        show or set an item count (adds it if you don't have it)
      items                         list the inventory
      find <text>                   search every known item by name, type or ID
      verify                        check the save rebuilds byte for byte
      dump <out.json>               write the whole save as JSON
      unpack <out.gvas>             write the decompressed save
      pack <in.gvas>                compress a .gvas back into the save

      <who>   party, all, a name (Viator) or a character ID (10101)
      <stat>  hp sp patk pdef eatk edef spd crit acc eva, or all (everything but hp)

    options
      --slot <n>      edit SaveData<n>.sav instead of the newest slot
      --file <path>   edit a specific .sav file
      --oodle <path>  path to oo2core*.dll if it isn't found automatically
      --dry-run       do everything except write the file
      --no-backup     skip the backup copy (not recommended)
      --force         allow adding key items, recipes, Wishvale items or unknown IDs
    """;

try
{
    var o = Options.Parse(args);
    return o.Command switch
    {
        null or "help" or "-h" or "--help" => PrintUsage(),
        "info" => Commands.Info(o),
        "money" => Commands.Money(o),
        "jp" => Commands.Jp(o),
        "stat" => Commands.StatCmd(o),
        "item" => Commands.Item(o),
        "items" => Commands.Items(o),
        "find" => Commands.Find(o),
        "verify" => Commands.Verify(o),
        "dump" => Commands.Dump(o),
        "unpack" => Commands.Unpack(o),
        "pack" => Commands.Pack(o),
        _ => throw new UsageException($"unknown command '{o.Command}'"),
    };
}
catch (UsageException e)
{
    Console.Error.WriteLine($"ot0edit: {e.Message}");
    Console.Error.WriteLine("run 'ot0edit help' for usage");
    return 2;
}
catch (Exception e)
{
    Console.Error.WriteLine($"ot0edit: {e.Message}");
    if (args.Contains("-v") || args.Contains("--verbose"))
        Console.Error.WriteLine(e);
    return 1;
}

static int PrintUsage()
{
    Console.WriteLine(Usage);
    return 0;
}
