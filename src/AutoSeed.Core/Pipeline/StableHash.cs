using System.Buffers.Binary;
using System.Text;

namespace EFCore.AutoSeed.Pipeline;

internal static class StableHash
{
    internal const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

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
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        return Combine(hash, buffer);
    }

    internal static ulong Combine(ulong hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        return Combine(hash, buffer);
    }

    internal static ulong Combine(ulong hash, string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> buffer = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(value, buffer);
        return Combine(hash, buffer);
    }
}
