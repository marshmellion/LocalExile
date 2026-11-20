using GameServer.Data.Ggpk;
using PoEDataExtractor;

namespace LocalExile.ContentPatch;

/// <summary>
/// The decoder the patcher reads bundles with. A chunk we wrote (a raw Kraken block, RawBundle) is copied out here; any
/// other goes to the extractor's ooz-wasm worker in Node (Tools/ooz/ooz_worker.mjs), started on first use: the client's
/// own bundles use Oodle stream types OozSharp does not decode (the 3.29.3c index is encoder 12 and has Selkie blocks).
/// </summary>
public sealed class Oodle : IOodleDecoder, IDisposable
{
    NodeOodleDecoder? _node;

    public byte[] Decompress(ReadOnlyMemory<byte> compressed, int uncompressedSize)
    {
        var c = compressed.Span;
        if (c.Length == uncompressedSize + 2 && c[0] == 0xCC && c[1] == 0x06) return c[2..].ToArray();
        _node ??= Start();
        return _node.Decompress(compressed, uncompressedSize);
    }

    static NodeOodleDecoder Start()
    {
        string worker = Path.GetFullPath(Environment.GetEnvironmentVariable("POE_OOZ_WORKER") ?? "Tools/ooz/ooz_worker.mjs");
        if (!File.Exists(worker)) throw new FileNotFoundException($"{worker} is missing: run from the repository root", worker);
        if (Environment.GetEnvironmentVariable("POE_NODE") is { } node) return new NodeOodleDecoder(node, worker);
        try { return new NodeOodleDecoder("node", worker); }
        catch (System.ComponentModel.Win32Exception) { return new NodeOodleDecoder(@"C:\Program Files\nodejs\node.exe", worker); }
    }

    public void Dispose() => _node?.Dispose();
}
