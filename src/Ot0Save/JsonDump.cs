using System.Text.Json.Nodes;
using Ot0Save.Gvas;

namespace Ot0Save;

// Read-only view of the whole save, for poking around. Not meant to be edited and loaded back.
public static class JsonDump
{
    public static JsonNode? ToJson(object? v)
    {
        switch (v)
        {
            case null:
                return null;
            case PropertyList props:
                var obj = new JsonObject();
                foreach (var p in props)
                {
                    var key = p.ArrayIndex > 0 ? $"{p.Name}[{p.ArrayIndex}]" : p.Name;
                    for (int n = 2; obj.ContainsKey(key); n++)
                        key = $"{p.Name}#{n}";
                    obj[key] = ToJson(p.Value);
                }
                return obj;
            case ArrayValue a:
                return new JsonArray(a.Items.Select(ToJson).ToArray());
            case SetValue s:
                return new JsonArray(s.Items.Select(ToJson).ToArray());
            case MapValue m:
                return new JsonArray(m.Entries
                    .Select(e => (JsonNode)new JsonObject { ["key"] = ToJson(e.Key), ["value"] = ToJson(e.Value) })
                    .ToArray());
            case ByteArray b:
                return JsonValue.Create(Convert.ToHexString(b.Bytes));
            case RawValue raw:
                return JsonValue.Create("raw:" + Convert.ToHexString(raw.Bytes));
            case FString s:
                return JsonValue.Create(s.Value);
            case bool b: return JsonValue.Create(b);
            case int i: return JsonValue.Create(i);
            case uint u: return JsonValue.Create(u);
            case long l: return JsonValue.Create(l);
            case ulong ul: return JsonValue.Create(ul);
            case short sh: return JsonValue.Create(sh);
            case ushort us: return JsonValue.Create(us);
            case sbyte sb: return JsonValue.Create(sb);
            case byte by: return JsonValue.Create(by);
            case float f: return JsonValue.Create(f);
            case double d: return JsonValue.Create(d);
            default:
                return JsonValue.Create(v.ToString());
        }
    }
}
