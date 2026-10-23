using System.Buffers.Binary;
using System.Text;

namespace Ot0Save.Gvas;

internal sealed class Reader(byte[] data)
{
    public int Pos;
    public int Length => data.Length;

    ReadOnlySpan<byte> Take(int n)
    {
        var s = data.AsSpan(Pos, n);
        Pos += n;
        return s;
    }

    public byte U8() => data[Pos++];
    public sbyte I8() => (sbyte)data[Pos++];
    public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public long I64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public double F64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
    public byte[] Bytes(int n) => Take(n).ToArray();

    // FString: positive length = Latin-1, negative = UTF-16, both null-terminated.
    public FString Str()
    {
        int n = I32();
        if (n == 0)
            return FString.Null;
        if (n > 0)
        {
            if (n > Length - Pos) throw new InvalidDataException($"String length {n} at {Pos - 4} runs off the end.");
            var s = Encoding.Latin1.GetString(Take(n - 1));
            Pos++;
            return new FString(s);
        }
        int chars = -n;
        if (chars * 2L > Length - Pos) throw new InvalidDataException($"String length {n} at {Pos - 4} runs off the end.");
        var w = Encoding.Unicode.GetString(Take((chars - 1) * 2));
        Pos += 2;
        return new FString(w, Wide: true);
    }
}

internal sealed class Writer
{
    readonly MemoryStream ms = new();

    public int Length => (int)ms.Length;
    public byte[] ToArray() => ms.ToArray();

    public void Bytes(ReadOnlySpan<byte> b) => ms.Write(b);
    public void U8(byte v) => ms.WriteByte(v);
    public void I8(sbyte v) => ms.WriteByte((byte)v);

    public void I16(short v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteInt16LittleEndian(b, v); ms.Write(b); }
    public void U16(ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); ms.Write(b); }
    public void I32(int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); ms.Write(b); }
    public void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); ms.Write(b); }
    public void I64(long v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, v); ms.Write(b); }
    public void U64(ulong v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, v); ms.Write(b); }
    public void F32(float v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b, v); ms.Write(b); }
    public void F64(double v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteDoubleLittleEndian(b, v); ms.Write(b); }

    public void Str(FString s)
    {
        if (s.IsNull)
        {
            I32(0);
        }
        else if (s.Wide)
        {
            I32(-(s.Value.Length + 1));
            Bytes(Encoding.Unicode.GetBytes(s.Value));
            U16(0);
        }
        else
        {
            I32(s.Value.Length + 1);
            Bytes(Encoding.Latin1.GetBytes(s.Value));
            U8(0);
        }
    }
}
