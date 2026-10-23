namespace Ot0Save.Gvas;

// UE 5.4 tagged properties. Each tag carries its complete type, so arrays and maps of
// structs don't repeat an inner tag like older engine versions did.
internal static class Serializer
{
    const byte HasArrayIndex = 0x01;
    const byte HasGuid = 0x02;
    const byte HasExtensions = 0x04;
    const byte BoolTrue = 0x10;

    // Structs with their own binary layout instead of nested tags.
    static readonly Dictionary<string, int> NativeStructs = new()
    {
        ["Vector"] = 24, ["Vector2D"] = 16, ["Rotator"] = 24, ["Quat"] = 32,
        ["Guid"] = 16, ["DateTime"] = 8, ["Timespan"] = 8,
        ["LinearColor"] = 16, ["Color"] = 4, ["IntPoint"] = 8, ["IntVector"] = 12,
    };

    static int? NativeSize(PropertyType t) =>
        t.Params.Count > 0 && NativeStructs.TryGetValue(t.Params[0].Name, out var n) ? n : null;

    static bool IsEnumByte(PropertyType t) => t.Params.Count > 0 && t.Params[0].Name != "None";

    public static PropertyList ReadProps(Reader r, int end)
    {
        var list = new PropertyList();
        while (r.Pos < end)
        {
            var name = r.Str().Value;
            if (name == "None")
                break;

            var p = new Property { Name = name, Type = ReadType(r) };
            int size = r.I32();
            p.Flags = r.U8();
            if ((p.Flags & HasArrayIndex) != 0) p.ArrayIndex = r.I32();
            if ((p.Flags & HasGuid) != 0) p.Guid = r.Bytes(16);
            if ((p.Flags & HasExtensions) != 0) p.Extensions = ReadExtensions(r);

            p.Value = p.Type.Name == "BoolProperty"
                ? (p.Flags & BoolTrue) != 0
                : ReadTagged(r, p.Type, size);
            list.Add(p);
        }
        return list;
    }

    public static void WriteProps(Writer w, PropertyList list)
    {
        foreach (var p in list)
            WriteProperty(w, p);
        w.Str(FString.Of("None"));
    }

    static PropertyType ReadType(Reader r)
    {
        var t = new PropertyType(r.Str().Value);
        int n = r.I32();
        for (int i = 0; i < n; i++)
            t.Params.Add(ReadType(r));
        return t;
    }

    static void WriteType(Writer w, PropertyType t)
    {
        w.Str(FString.Of(t.Name));
        w.I32(t.Params.Count);
        foreach (var p in t.Params)
            WriteType(w, p);
    }

    // Untested: none of the OT0 saves I've looked at use property extensions.
    static byte[] ReadExtensions(Reader r)
    {
        int start = r.Pos;
        byte ext = r.U8();
        if ((ext & 0x02) != 0)
        {
            r.U8();  // overridable operation
            r.I32(); // experimental overridable logic (bool, serialized as 4 bytes)
        }
        int len = r.Pos - start;
        r.Pos = start;
        return r.Bytes(len);
    }

    static object? ReadTagged(Reader r, PropertyType t, int size)
    {
        int start = r.Pos;
        try
        {
            var v = ReadValue(r, t, size);
            if (r.Pos == start + size)
                return v;
        }
        catch (Exception e) when (e is NotSupportedException or InvalidDataException
                                       or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
        }
        r.Pos = start;
        return new RawValue(r.Bytes(size), Fallback: true);
    }

    static object? ReadValue(Reader r, PropertyType t, int size)
    {
        switch (t.Name)
        {
            case "StructProperty":
                return NativeSize(t) is not null ? new RawValue(r.Bytes(size)) : ReadProps(r, r.Pos + size);

            case "ArrayProperty":
            {
                var inner = t.Params[0];
                int n = r.I32();
                if (inner.Name == "ByteProperty" && !IsEnumByte(inner))
                    return new ByteArray(r.Bytes(n));
                var arr = new ArrayValue();
                for (int i = 0; i < n; i++)
                    arr.Items.Add(ReadElement(r, inner));
                return arr;
            }

            case "SetProperty":
            {
                var set = new SetValue();
                int removed = r.I32();
                for (int i = 0; i < removed; i++) set.Removed.Add(ReadElement(r, t.Params[0]));
                int n = r.I32();
                for (int i = 0; i < n; i++) set.Items.Add(ReadElement(r, t.Params[0]));
                return set;
            }

            case "MapProperty":
            {
                var map = new MapValue();
                int removed = r.I32();
                for (int i = 0; i < removed; i++) map.Removed.Add(ReadElement(r, t.Params[0]));
                int n = r.I32();
                for (int i = 0; i < n; i++)
                {
                    var key = ReadElement(r, t.Params[0]);
                    var value = ReadElement(r, t.Params[1]);
                    map.Entries.Add(new(key, value));
                }
                return map;
            }

            case "TextProperty":
            case "SoftObjectProperty":
                return new RawValue(r.Bytes(size));

            default:
                return ReadElement(r, t);
        }
    }

    // A value inside an array, set or map: no tag and no size, so the type has to be enough.
    static object? ReadElement(Reader r, PropertyType t) => t.Name switch
    {
        "IntProperty" => r.I32(),
        "UInt32Property" => r.U32(),
        "Int64Property" => r.I64(),
        "UInt64Property" => r.U64(),
        "Int16Property" => r.I16(),
        "UInt16Property" => r.U16(),
        "Int8Property" => r.I8(),
        "FloatProperty" => r.F32(),
        "DoubleProperty" => r.F64(),
        "BoolProperty" => r.U8() != 0,
        "ByteProperty" => IsEnumByte(t) ? r.Str() : r.U8(),
        "EnumProperty" or "NameProperty" or "StrProperty" or "ObjectProperty" => r.Str(),
        "StructProperty" => NativeSize(t) is int n ? new RawValue(r.Bytes(n)) : ReadProps(r, r.Length),
        _ => throw new NotSupportedException($"{t.Name} inside a container"),
    };

    static void WriteProperty(Writer w, Property p)
    {
        w.Str(FString.Of(p.Name));
        WriteType(w, p.Type);

        byte flags = p.Flags;
        byte[] body = [];
        if (p.Type.Name == "BoolProperty")
        {
            flags = p.Value is true ? (byte)(flags | BoolTrue) : (byte)(flags & ~BoolTrue);
        }
        else
        {
            var bw = new Writer();
            WriteValue(bw, p.Type, p.Value);
            body = bw.ToArray();
        }

        w.I32(body.Length);
        w.U8(flags);
        if ((flags & HasArrayIndex) != 0) w.I32(p.ArrayIndex);
        if ((flags & HasGuid) != 0) w.Bytes(p.Guid!);
        if ((flags & HasExtensions) != 0) w.Bytes(p.Extensions!);
        w.Bytes(body);
    }

    static void WriteValue(Writer w, PropertyType t, object? v)
    {
        if (v is RawValue raw)
        {
            w.Bytes(raw.Bytes);
            return;
        }

        switch (t.Name)
        {
            case "ArrayProperty":
                if (v is ByteArray bytes)
                {
                    w.I32(bytes.Bytes.Length);
                    w.Bytes(bytes.Bytes);
                    break;
                }
                var arr = (ArrayValue)v!;
                w.I32(arr.Items.Count);
                foreach (var item in arr.Items)
                    WriteElement(w, t.Params[0], item);
                break;

            case "SetProperty":
                var set = (SetValue)v!;
                w.I32(set.Removed.Count);
                foreach (var item in set.Removed) WriteElement(w, t.Params[0], item);
                w.I32(set.Items.Count);
                foreach (var item in set.Items) WriteElement(w, t.Params[0], item);
                break;

            case "MapProperty":
                var map = (MapValue)v!;
                w.I32(map.Removed.Count);
                foreach (var key in map.Removed) WriteElement(w, t.Params[0], key);
                w.I32(map.Entries.Count);
                foreach (var (key, value) in map.Entries)
                {
                    WriteElement(w, t.Params[0], key);
                    WriteElement(w, t.Params[1], value);
                }
                break;

            default:
                WriteElement(w, t, v);
                break;
        }
    }

    static void WriteElement(Writer w, PropertyType t, object? v)
    {
        switch (v)
        {
            case RawValue raw: w.Bytes(raw.Bytes); return;
            case PropertyList props: WriteProps(w, props); return;
            case FString s: w.Str(s); return;
        }

        switch (t.Name)
        {
            case "IntProperty": w.I32(Convert.ToInt32(v)); break;
            case "UInt32Property": w.U32(Convert.ToUInt32(v)); break;
            case "Int64Property": w.I64(Convert.ToInt64(v)); break;
            case "UInt64Property": w.U64(Convert.ToUInt64(v)); break;
            case "Int16Property": w.I16(Convert.ToInt16(v)); break;
            case "UInt16Property": w.U16(Convert.ToUInt16(v)); break;
            case "Int8Property": w.I8(Convert.ToSByte(v)); break;
            case "FloatProperty": w.F32(Convert.ToSingle(v)); break;
            case "DoubleProperty": w.F64(Convert.ToDouble(v)); break;
            case "BoolProperty": w.U8(v is true ? (byte)1 : (byte)0); break;
            case "ByteProperty": w.U8(Convert.ToByte(v)); break;
            default: throw new NotSupportedException($"Don't know how to write {t.Name}.");
        }
    }
}
