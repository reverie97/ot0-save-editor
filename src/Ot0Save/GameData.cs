using System.Text.Json;

namespace Ot0Save;

public record ItemInfo(string? Name, string? Type, string? Note);

// Names come from data/*.json. The game's own tables are encrypted, so these are built
// from community lists plus what was confirmed by testing on real saves.
public sealed class GameData
{
    // Adding one of these when you don't already have it can skip or confuse story and
    // Wishvale progress, so both tools ask first.
    public static readonly HashSet<string> RiskyTypes = ["Key item", "Recipe", "Wishvale", "Unused"];

    public Dictionary<int, string> Characters { get; } = [];
    public Dictionary<int, ItemInfo> Items { get; } = [];

    public static GameData Load()
    {
        var data = new GameData();
        var dir = Path.Combine(AppContext.BaseDirectory, "data");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

        var chars = Read<Dictionary<string, string>>(Path.Combine(dir, "characters.json"), opts);
        foreach (var (k, v) in chars ?? [])
            if (int.TryParse(k, out var id)) data.Characters[id] = v;

        var items = Read<Dictionary<string, ItemInfo>>(Path.Combine(dir, "items.json"), opts);
        foreach (var (k, v) in items ?? [])
            if (int.TryParse(k, out var id)) data.Items[id] = v;

        return data;
    }

    public string CharacterName(SaveGame save, int id) =>
        id == SaveGame.ProtagonistId ? save.ProtagonistName
        : Characters.TryGetValue(id, out var n) ? n
        : id.ToString();

    public string ItemName(int id) => Items.TryGetValue(id, out var i) ? i.Name ?? "" : "";
    public string ItemType(int id) => Items.TryGetValue(id, out var i) ? i.Type ?? "" : "";
    public string ItemNote(int id) => Items.TryGetValue(id, out var i) ? i.Note ?? "" : "";

    public bool IsRiskyToAdd(int id) => !Items.TryGetValue(id, out var i) || RiskyTypes.Contains(i.Type ?? "");

    // Exact name matches win (case doesn't matter); otherwise anything containing the text.
    public IReadOnlyList<int> FindItems(string text)
    {
        text = text.Trim();
        if (int.TryParse(text, out var id))
            return [id];

        var exact = Items.Where(kv => string.Equals(kv.Value.Name, text, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key).Order().ToList();
        if (exact.Count > 0)
            return exact;

        return Items.Where(kv => kv.Value.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)
            .Select(kv => kv.Key).Order().ToList();
    }

    static T? Read<T>(string path, JsonSerializerOptions opts) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), opts) : default;
}
