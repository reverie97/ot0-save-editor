using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Ot0Save;

// The game links Oodle statically, so we borrow oo2core from somewhere else on the machine.
// It can't be redistributed, which is why there's no copy in this repo.
public sealed unsafe class Oodle : IDisposable
{
    const int Kraken = 8;
    const int LevelNormal = 4;

    readonly IntPtr lib;
    readonly delegate* unmanaged<byte*, long, byte*, long, int, int, int, void*, long, void*, void*, void*, long, int, long> decompress;
    readonly delegate* unmanaged<int, byte*, long, byte*, int, void*, void*, void*, void*, long, long> compress;
    readonly delegate* unmanaged<int, long, long> compressBound;

    public string DllPath { get; }

    Oodle(string path)
    {
        DllPath = path;
        lib = NativeLibrary.Load(path);
        decompress = (delegate* unmanaged<byte*, long, byte*, long, int, int, int, void*, long, void*, void*, void*, long, int, long>)
            NativeLibrary.GetExport(lib, "OodleLZ_Decompress").ToPointer();
        compress = (delegate* unmanaged<int, byte*, long, byte*, int, void*, void*, void*, void*, long, long>)
            NativeLibrary.GetExport(lib, "OodleLZ_Compress").ToPointer();
        compressBound = (delegate* unmanaged<int, long, long>)
            NativeLibrary.GetExport(lib, "OodleLZ_GetCompressedBufferSizeNeeded").ToPointer();
    }

    public static Oodle Load(string? path = null)
    {
        path ??= Environment.GetEnvironmentVariable("OT0_OODLE") ?? Find();
        if (path is null)
            throw new FileNotFoundException(
                "Couldn't find an Oodle DLL (oo2core*.dll). Pass --oodle <path> or set OT0_OODLE.");
        return new Oodle(path);
    }

    public byte[] Decompress(ReadOnlySpan<byte> src, int rawSize)
    {
        var dst = new byte[rawSize];
        long got;
        fixed (byte* s = src)
        fixed (byte* d = dst)
            got = decompress(s, src.Length, d, rawSize, 1, 0, 0, null, 0, null, null, null, 0, 3);
        if (got != rawSize)
            throw new InvalidDataException($"Oodle returned {got} bytes, expected {rawSize}.");
        return dst;
    }

    public byte[] Compress(ReadOnlySpan<byte> src)
    {
        var dst = new byte[compressBound(Kraken, src.Length)];
        long n;
        fixed (byte* s = src)
        fixed (byte* d = dst)
            n = compress(Kraken, s, src.Length, d, LevelNormal, null, null, null, null, 0);
        if (n <= 0)
            throw new InvalidOperationException("Oodle compression failed.");
        return dst[..(int)n];
    }

    public void Dispose() => NativeLibrary.Free(lib);

    public static string? Find() => Candidates().FirstOrDefault(File.Exists);

    static IEnumerable<string> Candidates()
    {
        foreach (var f in Files(AppContext.BaseDirectory, "oo2core*.dll"))
            yield return f;

        // Unreal Engine installs ship a copy for their .NET tooling.
        var epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games");
        foreach (var ue in Dirs(epic, "UE_*").OrderDescending())
        {
            var shared = Path.Combine(ue, "Engine", "Source", "Programs", "Shared", "EpicGames.Oodle");
            foreach (var f in Files(shared, "oo2core.dll", SearchOption.AllDirectories))
                if (f.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
                    yield return f;
        }

        // Plenty of Steam games keep oo2core_N_win64.dll next to the exe.
        foreach (var game in SteamGameFolders())
        {
            foreach (var f in Files(game, "oo2core_*_win64.dll"))
                yield return f;
            foreach (var sub in Dirs(game, "*"))
                foreach (var f in Files(sub, "oo2core_*_win64.dll"))
                    yield return f;
        }
    }

    static IEnumerable<string> SteamGameFolders()
    {
        var steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(l => Dirs(Path.Combine(l, "steamapps", "common"), "*"));
    }

    static string[] Files(string dir, string pattern, SearchOption opt = SearchOption.TopDirectoryOnly)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern, opt) : []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    static string[] Dirs(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir, pattern) : []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
