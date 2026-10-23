namespace Ot0Save.Gvas;

// Wide and IsNull only exist so strings are written back exactly as they were read.
public readonly record struct FString(string Value, bool Wide = false, bool IsNull = false)
{
    public static readonly FString Null = new("", IsNull: true);

    public static FString Of(string s) => new(s, Wide: s.Any(c => c > 0xFF));

    public override string ToString() => Value;
}

// e.g. ArrayProperty(StructProperty(SaveDataItem(/Script/Kingship)))
public sealed class PropertyType(string name)
{
    public string Name { get; } = name;
    public List<PropertyType> Params { get; } = [];

    public override string ToString() =>
        Params.Count == 0 ? Name : $"{Name}({string.Join(", ", Params)})";
}

public sealed class Property
{
    public required string Name { get; set; }
    public required PropertyType Type { get; set; }
    public object? Value { get; set; }

    internal byte Flags;
    internal int ArrayIndex;
    internal byte[]? Guid;
    internal byte[]? Extensions;

    public Property Clone() => new()
    {
        Name = Name,
        Type = Type,
        Value = Values.Clone(Value),
        Flags = Flags,
        ArrayIndex = ArrayIndex,
        Guid = Guid,
        Extensions = Extensions,
    };
}

public sealed class PropertyList : List<Property>
{
    public Property? Get(string name) => Find(p => p.Name == name);

    public int Int(string name, int fallback = 0) => Get(name)?.Value is int v ? v : fallback;

    // UE leaves out properties that still hold their default value, so a missing int is
    // added rather than treated as an error.
    public void SetInt(string name, int value)
    {
        var p = Get(name);
        if (p is null)
            Add(p = new Property { Name = name, Type = new PropertyType("IntProperty") });
        else if (p.Type.Name != "IntProperty")
            throw new InvalidOperationException($"{name} is a {p.Type.Name}, not an IntProperty.");
        p.Value = value;
    }

    public PropertyList Struct(string name) =>
        Get(name)?.Value as PropertyList ?? throw new KeyNotFoundException($"No struct named {name}.");

    public ArrayValue Array(string name) =>
        Get(name)?.Value as ArrayValue ?? throw new KeyNotFoundException($"No array named {name}.");

    public PropertyList Clone()
    {
        var copy = new PropertyList();
        copy.AddRange(this.Select(p => p.Clone()));
        return copy;
    }
}

// Opaque bytes: native structs and text by design, or (Fallback) something the parser
// couldn't model exactly. Either way they're written back verbatim.
public sealed record RawValue(byte[] Bytes, bool Fallback = false);

public sealed record ByteArray(byte[] Bytes);

public sealed class ArrayValue
{
    public List<object?> Items { get; } = [];
}

public sealed class SetValue
{
    public List<object?> Removed { get; } = [];
    public List<object?> Items { get; } = [];
}

public sealed class MapValue
{
    public List<object?> Removed { get; } = [];
    public List<KeyValuePair<object?, object?>> Entries { get; } = [];
}

internal static class Values
{
    public static object? Clone(object? v)
    {
        switch (v)
        {
            case PropertyList props:
                return props.Clone();
            case ArrayValue a:
                var arr = new ArrayValue();
                arr.Items.AddRange(a.Items.Select(Clone));
                return arr;
            case SetValue s:
                var set = new SetValue();
                set.Removed.AddRange(s.Removed.Select(Clone));
                set.Items.AddRange(s.Items.Select(Clone));
                return set;
            case MapValue m:
                var map = new MapValue();
                map.Removed.AddRange(m.Removed.Select(Clone));
                map.Entries.AddRange(m.Entries.Select(e => new KeyValuePair<object?, object?>(Clone(e.Key), Clone(e.Value))));
                return map;
            default:
                return v; // numbers, strings and byte blobs are never edited in place
        }
    }
}
