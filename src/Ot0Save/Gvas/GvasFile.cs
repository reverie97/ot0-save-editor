namespace Ot0Save.Gvas;

public sealed class GvasFile
{
    // Everything before the first property (versions, custom version table, class name)
    // and whatever follows the root's "None" are copied through untouched.
    readonly byte[] header;
    readonly byte[] trailer;

    public string SaveClass { get; }
    public PropertyList Root { get; }

    GvasFile(byte[] header, string saveClass, PropertyList root, byte[] trailer)
    {
        this.header = header;
        this.trailer = trailer;
        SaveClass = saveClass;
        Root = root;
    }

    public static GvasFile Parse(byte[] data)
    {
        if (data.Length < 32 || !data.AsSpan(0, 4).SequenceEqual("GVAS"u8))
            throw new InvalidDataException("Not a GVAS save.");

        var r = new Reader(data) { Pos = 4 };
        r.I32(); // save game version
        r.I32(); // UE4 package version
        r.I32(); // UE5 package version
        r.Pos += 10; // engine major, minor, patch (u16 each) + changelist (u32)
        r.Str(); // branch
        r.I32(); // custom version format
        int customVersions = r.I32();
        r.Pos += customVersions * 20; // guid + int each
        string saveClass = r.Str().Value;

        // 5.4 writes a single zero byte after the class name. Only skip it if what follows
        // actually looks like the first property name.
        if (data[r.Pos] == 0 && LooksLikeName(data, r.Pos + 1))
            r.Pos++;

        var header = data[..r.Pos];
        var root = Serializer.ReadProps(r, data.Length);
        var trailer = data[r.Pos..];
        return new GvasFile(header, saveClass, root, trailer);
    }

    public byte[] ToBytes()
    {
        var w = new Writer();
        w.Bytes(header);
        Serializer.WriteProps(w, Root);
        w.Bytes(trailer);
        return w.ToArray();
    }

    static bool LooksLikeName(byte[] data, int pos)
    {
        if (pos + 4 > data.Length) return false;
        int n = BitConverter.ToInt32(data, pos);
        return n is > 1 and < 256 && pos + 4 + n <= data.Length && data[pos + 4 + n - 1] == 0;
    }
}
