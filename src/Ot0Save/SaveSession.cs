using Ot0Save.Gvas;

namespace Ot0Save;

// An open save plus what's needed to write it back without wrecking anything.
public sealed class SaveSession : IDisposable
{
    DateTime readStamp;

    public string Path { get; }
    public Oodle Oodle { get; }
    public byte[] OriginalGvas { get; }
    public SaveGame Save { get; set; }

    SaveSession(string path, Oodle oodle, byte[] gvas, DateTime stamp)
    {
        Path = path;
        Oodle = oodle;
        OriginalGvas = gvas;
        Save = new SaveGame(GvasFile.Parse(gvas));
        readStamp = stamp;
    }

    public static SaveSession Open(string path, string? oodlePath = null)
    {
        path = System.IO.Path.GetFullPath(path);
        var oodle = Oodle.Load(oodlePath);
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            var gvas = SaveContainer.Unpack(File.ReadAllBytes(path), oodle);
            return new SaveSession(path, oodle, gvas, stamp);
        }
        catch
        {
            oodle.Dispose();
            throw;
        }
    }

    // Serializes and compresses the current state, then reads it all back before anyone
    // gets to write it.
    public byte[] PackChecked()
    {
        var gvas = Save.Gvas.ToBytes();
        var packed = SaveContainer.Pack(gvas, Oodle);
        var back = SaveContainer.Unpack(packed, Oodle);
        if (!back.AsSpan().SequenceEqual(gvas))
            throw new InvalidDataException("Compressed save didn't decompress to the same bytes. Nothing was written.");
        GvasFile.Parse(back);
        return packed;
    }

    // Returns the backup folder, or null when backup is off.
    public string? Commit(bool backup = true)
    {
        var packed = PackChecked();
        if (SaveLocator.GameIsRunning())
            throw new InvalidOperationException("Octopath Traveler 0 is running. Close it first or it'll overwrite the edit.");
        if (File.GetLastWriteTimeUtc(Path) != readStamp)
            throw new InvalidOperationException($"{System.IO.Path.GetFileName(Path)} changed on disk after it was opened. Reload it first.");

        var backupDir = backup ? SaveLocator.Backup(Path) : null;
        var tmp = Path + ".tmp";
        File.WriteAllBytes(tmp, packed);
        File.Move(tmp, Path, overwrite: true);
        readStamp = File.GetLastWriteTimeUtc(Path);
        return backupDir;
    }

    // null when the save rebuilds byte for byte, otherwise what went wrong.
    public string? CheckRoundTrip()
    {
        var rebuilt = Save.Gvas.ToBytes();
        int n = Math.Min(rebuilt.Length, OriginalGvas.Length);
        for (int i = 0; i < n; i++)
            if (rebuilt[i] != OriginalGvas[i])
                return $"rebuilt save differs from the original at byte {i:N0}";
        if (rebuilt.Length != OriginalGvas.Length)
            return $"rebuilt save is {rebuilt.Length:N0} bytes, original was {OriginalGvas.Length:N0}";

        var back = SaveContainer.Unpack(SaveContainer.Pack(rebuilt, Oodle), Oodle);
        return back.AsSpan().SequenceEqual(rebuilt) ? null : "recompressed save doesn't decompress to the same bytes";
    }

    public void Dispose() => Oodle.Dispose();
}
