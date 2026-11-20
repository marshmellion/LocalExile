using System.Buffers.Binary;
using System.Text;
using GameServer.Data.Ggpk;

namespace LocalExile.ContentPatch;

/// <summary>
/// The decompressed <c>Bundles2/_.index.bin</c>, kept whole so it can be written back (GameIndex reads the same layout
/// for lookups): i32 bundle count, per bundle (i32 name length, UTF-8 name without "Bundles2/" or ".bundle.bin", i32
/// size); i32 file count, per file (u64 hash of the lower-case path, i32 bundle, i32 offset, i32 size); i32 directory
/// count, 20 bytes each; then the path table, a bundle of its own, left as it is.
/// <para>A changed file keeps its hash and points into our bundle; a new file gets a record of its own (its path is not
/// added to the path table: the client finds files by hash, and the table is only for listing them).</para>
/// </summary>
public sealed class IndexEditor
{
    readonly List<(string Name, int Size)> _bundles = [];
    readonly List<(ulong Hash, int Bundle, int Offset, int Size)> _files = [];
    readonly Dictionary<ulong, int> _byHash = [];
    byte[] _directories = [];
    byte[] _tail = [];
    // Whether the client's own index lists its files by hash: then new records go in that order too, in case the client
    // searches them by halving.
    bool _sortedByHash;

    public IReadOnlyList<(string Name, int Size)> Bundles => _bundles;
    public int FileCount => _files.Count;
    public bool SortedByHash => _sortedByHash;

    public static IndexEditor Parse(byte[] content)
    {
        var e = new IndexEditor();
        var r = content.AsSpan();
        int at = 0;
        int bundles = BinaryPrimitives.ReadInt32LittleEndian(r[at..]); at += 4;
        for (int i = 0; i < bundles; i++)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(r[at..]); at += 4;
            string name = Encoding.UTF8.GetString(r.Slice(at, length)); at += length;
            int size = BinaryPrimitives.ReadInt32LittleEndian(r[at..]); at += 4;
            e._bundles.Add((name, size));
        }
        int files = BinaryPrimitives.ReadInt32LittleEndian(r[at..]); at += 4;
        for (int i = 0; i < files; i++)
        {
            var f = r.Slice(at + i * 20, 20);
            ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(f);
            e._files.Add((hash, BinaryPrimitives.ReadInt32LittleEndian(f[8..]), BinaryPrimitives.ReadInt32LittleEndian(f[12..]),
                BinaryPrimitives.ReadInt32LittleEndian(f[16..])));
            e._byHash.TryAdd(hash, i);
        }
        at += files * 20;
        e._sortedByHash = true;
        for (int i = 1; i < e._files.Count && e._sortedByHash; i++) e._sortedByHash = e._files[i - 1].Hash <= e._files[i].Hash;
        int directories = BinaryPrimitives.ReadInt32LittleEndian(r[at..]);
        e._directories = r.Slice(at, 4 + directories * 20).ToArray();
        at += 4 + directories * 20;
        e._tail = r[at..].ToArray();
        return e;
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        var four = new byte[4];
        void I32(int v) { BinaryPrimitives.WriteInt32LittleEndian(four, v); ms.Write(four); }
        I32(_bundles.Count);
        foreach (var (name, size) in _bundles)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            I32(bytes.Length);
            ms.Write(bytes);
            I32(size);
        }
        I32(_files.Count);
        var files = _sortedByHash ? _files.OrderBy(f => f.Hash).ToList() : _files;
        Span<byte> record = stackalloc byte[20];
        foreach (var (hash, bundle, offset, size) in files)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(record, hash);
            BinaryPrimitives.WriteInt32LittleEndian(record[8..], bundle);
            BinaryPrimitives.WriteInt32LittleEndian(record[12..], offset);
            BinaryPrimitives.WriteInt32LittleEndian(record[16..], size);
            ms.Write(record);
        }
        ms.Write(_directories);
        ms.Write(_tail);
        return ms.ToArray();
    }

    public static ulong PathHash(string path) => MurmurHash64A.Hash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));

    /// <summary>The bundle's index, adding it (name as the index stores it: "LocalExile/Content").</summary>
    public int Bundle(string name, int size)
    {
        int i = _bundles.FindIndex(b => b.Name == name);
        if (i >= 0) { _bundles[i] = (name, size); return i; }
        _bundles.Add((name, size));
        return _bundles.Count - 1;
    }

    public bool Has(string path) => _byHash.ContainsKey(PathHash(path));

    /// <summary>Points a path at a slice of a bundle: its record when it has one, else a new record.</summary>
    public void Set(string path, int bundle, int offset, int size)
    {
        ulong hash = PathHash(path);
        if (_byHash.TryGetValue(hash, out int i)) _files[i] = (hash, bundle, offset, size);
        else
        {
            _files.Add((hash, bundle, offset, size));
            _byHash[hash] = _files.Count - 1;
        }
    }
}
