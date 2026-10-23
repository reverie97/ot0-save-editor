using System.Globalization;

namespace Ot0Edit;

sealed class Options
{
    public List<string> Args { get; } = [];
    public int? Slot { get; private set; }
    public string? File { get; private set; }
    public string? Oodle { get; private set; }
    public bool DryRun { get; private set; }
    public bool NoBackup { get; private set; }
    public bool Force { get; private set; }
    public bool Verbose { get; private set; }

    public string? Command => Args.Count > 0 ? Args[0].ToLowerInvariant() : null;

    public static Options Parse(string[] argv)
    {
        var o = new Options();
        for (int i = 0; i < argv.Length; i++)
        {
            string a = argv[i];
            switch (a)
            {
                case "--slot": o.Slot = Number(Next()); break;
                case "--file": o.File = Next(); break;
                case "--oodle": o.Oodle = Next(); break;
                case "--dry-run": o.DryRun = true; break;
                case "--no-backup": o.NoBackup = true; break;
                case "--force": o.Force = true; break;
                case "-v" or "--verbose": o.Verbose = true; break;
                default:
                    if (a.StartsWith("--"))
                        throw new UsageException($"unknown option {a}");
                    o.Args.Add(a);
                    break;
            }

            string Next() => i + 1 < argv.Length ? argv[++i] : throw new UsageException($"{a} needs a value");
        }
        return o;
    }

    public string Arg(int index, string what) =>
        index < Args.Count ? Args[index] : throw new UsageException($"missing {what}");

    public int NumberArg(int index, string what) => Number(Arg(index, what));

    // Accepts 9999999, 9,999,999 and 9_999_999.
    public static int Number(string s)
    {
        var clean = s.Replace(",", "").Replace("_", "");
        if (!int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            throw new UsageException($"'{s}' isn't a number");
        return n;
    }
}

sealed class UsageException(string message) : Exception(message);
