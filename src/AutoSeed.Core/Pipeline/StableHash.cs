using System.Buffers.Binary;
using System.Text;

namespace EFCore.AutoSeed.Pipeline;

internal static class StableHash
{
    internal const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private const byte StringTag = 1;
    private const byte IntTag = 2;
    private const byte LongTag = 3;

    internal static ulong Combine(ulong hash, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            hash ^= value;
            hash *= Prime;
        }

        return hash;
    }

    internal static ulong Combine(ulong hash, long value)
    {
        hash = CombineByte(hash, LongTag);
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        return Combine(hash, buffer);
    }

    internal static ulong Combine(ulong hash, int value)
    {
        hash = CombineByte(hash, IntTag);
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        return Combine(hash, buffer);
    }

    internal static ulong Combine(ulong hash, string value)
    {
        hash = CombineByte(hash, StringTag);
        int byteCount = Encoding.UTF8.GetByteCount(value);
        hash = Combine(hash, byteCount);
        Span<byte> buffer = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(value, buffer);
        return Combine(hash, buffer);
    }

    private static ulong CombineByte(ulong hash, byte value)
    {
        hash ^= value;
        return hash * Prime;
    }
}
