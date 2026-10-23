using System.Buffers.Binary;

namespace Ot0Save;

// A .sav file is an 8-byte uncompressed length followed by one Oodle Kraken stream.
// Decompressed, it's a plain Unreal GVAS save.
public static class SaveContainer
{
    const int MaxRawSize = 512 * 1024 * 1024;

    public static byte[] Unpack(byte[] sav, Oodle oodle)
    {
        if (sav.Length < 16)
            throw new InvalidDataException("File is too small to be an OT0 save.");

        long rawSize = BinaryPrimitives.ReadInt64LittleEndian(sav);
        if (rawSize <= 0 || rawSize > MaxRawSize)
            throw new InvalidDataException($"Bad size header ({rawSize}). Is this an OT0 save?");

        var raw = oodle.Decompress(sav.AsSpan(8), (int)rawSize);
        if (!raw.AsSpan(0, 4).SequenceEqual("GVAS"u8))
            throw new InvalidDataException("Decompressed data isn't a GVAS save.");
        return raw;
    }

    public static byte[] Pack(byte[] gvas, Oodle oodle)
    {
        var comp = oodle.Compress(gvas);
        var sav = new byte[8 + comp.Length];
        BinaryPrimitives.WriteInt64LittleEndian(sav, gvas.Length);
        comp.CopyTo(sav, 8);
        return sav;
    }
}
