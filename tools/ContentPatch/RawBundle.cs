using System.Buffers.Binary;

namespace LocalExile.ContentPatch;

/// <summary>
/// Writes a <c>*.bundle.bin</c> the client reads without any compressor: every chunk is one Kraken stream holding a
/// single uncompressed block (a two-byte block header <c>CC 06</c>, then the bytes). The client's own OodleLZ_Decompress
/// (0x142c9d520, run in Unicorn by scripts/re/formats/oodle_client.py) returns such a chunk byte for byte, for 1 byte to
/// a full 256 KiB chunk; a chunk stored with its compressed size equal to its size (no header) is refused.
/// <para>Header, as BundleFile reads it: u32 size, u32 payload size, u32 head size, u32 first-file encoder (8 Kraken),
/// u32 1, u64 size, u64 payload size, u32 chunk count, u32 granularity, 4 × u32 0, u32 chunk sizes[count]. The word
/// at 16 is 1 as in the client's own bundles (its index has 1 there). With 0 there (the first version) the 3.29.3c
/// client closed during startup; this word is the first suspect.</para>
/// </summary>
public static class RawBundle
{
    public const int Granularity = 256 * 1024;
    const int Kraken = 8;
    static readonly byte[] RawBlockHeader = [0xCC, 0x06];

    public static byte[] Write(ReadOnlySpan<byte> data)
    {
        int chunks = Math.Max(1, (data.Length + Granularity - 1) / Granularity);
        if (data.Length == 0) chunks = 0;
        int payload = data.Length + chunks * RawBlockHeader.Length;
        int head = 48 + chunks * 4;
        var bundle = new byte[12 + head + payload];
        var w = bundle.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(w[0..], data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(w[4..], payload);
        BinaryPrimitives.WriteInt32LittleEndian(w[8..], head);
        BinaryPrimitives.WriteInt32LittleEndian(w[12..], Kraken);
        BinaryPrimitives.WriteInt32LittleEndian(w[16..], 1);
        BinaryPrimitives.WriteInt64LittleEndian(w[20..], data.Length);
        BinaryPrimitives.WriteInt64LittleEndian(w[28..], payload);
        BinaryPrimitives.WriteInt32LittleEndian(w[36..], chunks);
        BinaryPrimitives.WriteInt32LittleEndian(w[40..], Granularity);
        // 44..60: four zero words.
        int at = 60 + chunks * 4;
        for (int i = 0; i < chunks; i++)
        {
            int size = Math.Min(Granularity, data.Length - i * Granularity);
            BinaryPrimitives.WriteInt32LittleEndian(w[(60 + i * 4)..], size + RawBlockHeader.Length);
            RawBlockHeader.CopyTo(w[at..]);
            data.Slice(i * Granularity, size).CopyTo(w[(at + RawBlockHeader.Length)..]);
            at += size + RawBlockHeader.Length;
        }
        return bundle;
    }

    /// <summary>Decodes a bundle this class wrote (or any whose chunks are raw blocks): for the round-trip checks,
    /// without Oodle.</summary>
    public static byte[]? TryReadRaw(ReadOnlySpan<byte> bundle)
    {
        int size = BinaryPrimitives.ReadInt32LittleEndian(bundle);
        int chunks = BinaryPrimitives.ReadInt32LittleEndian(bundle[36..]);
        var data = new byte[size];
        int at = 60 + chunks * 4, to = 0;
        for (int i = 0; i < chunks; i++)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(bundle[(60 + i * 4)..]);
            if (bundle[at] != RawBlockHeader[0] || bundle[at + 1] != RawBlockHeader[1]) return null;
            bundle.Slice(at + 2, length - 2).CopyTo(data.AsSpan(to));
            to += length - 2;
            at += length;
        }
        return to == size ? data : null;
    }
}
