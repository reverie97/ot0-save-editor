using System.Diagnostics;

namespace Ot0Save;

public static class SaveLocator
{
    // Documents\My Games\Octopath_Traveler0\Steam\<steam id>\SaveGames
    public static IEnumerable<string> SaveFolders()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var root = Path.Combine(docs, "My Games", "Octopath_Traveler0", "Steam");
        if (!Directory.Exists(root))
            yield break;
        foreach (var user in Directory.GetDirectories(root))
        {
            var dir = Path.Combine(user, "SaveGames");
            if (Directory.Exists(dir))
                yield return dir;
        }
    }

    public static IEnumerable<string> Slots() =>
        SaveFolders().SelectMany(d => Directory.GetFiles(d, "SaveData*.sav"));

    public static string? Newest() =>
        Slots().OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

    public static string? Slot(int n) =>
        SaveFolders().Select(d => Path.Combine(d, $"SaveData{n}.sav")).Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

    public static bool GameIsRunning()
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.ProcessName.StartsWith("Octopath_Traveler0", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    // Copies the whole folder (both slots plus SystemData.sav) next to itself.
    public static string Backup(string savePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(savePath))!;
        var dest = $"{dir}_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(dir))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        return dest;
    }
}
