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

    /// <summary>
    /// Lists new paths in the path table, the way the client's own files are (2026-11-20, from the real index): one
    /// directory record per directory, u64 MurmurHash64A of the lower-case directory path without a trailing slash, the
    /// offset and size of its block in the decompressed path table, and its subtree's size. A block is a stream of
    /// (u32 n, NUL-terminated lower-case UTF-8 string); n = 0 toggles base mode (entering it clears the bases), else the
    /// string is appended to base n − 1 when there is one. A new directory gets a block of its own at the end, its paths
    /// written whole (n = 1, no bases); a directory the client already has gets a copy of its block at the end with the
    /// new paths after it (its base mode left and its bases cleared first), and its record points there.
    /// </summary>
    public int AddToPathTable(IEnumerable<string> paths, Func<byte[], byte[]> decompress)
    {
        var table = new List<byte>(decompress(_tail));
        int count = BinaryPrimitives.ReadInt32LittleEndian(_directories);
        var records = new List<(ulong Hash, int Offset, int Size, int Recursive)>();
        for (int i = 0; i < count; i++)
        {
            var d = _directories.AsSpan(4 + i * 20);
            records.Add((BinaryPrimitives.ReadUInt64LittleEndian(d), BinaryPrimitives.ReadInt32LittleEndian(d[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(d[12..]), BinaryPrimitives.ReadInt32LittleEndian(d[16..])));
        }
        var four = new byte[4];
        void Number(List<byte> to, int v) { BinaryPrimitives.WriteInt32LittleEndian(four, v); to.AddRange(four); }
        int added = 0;
        foreach (var group in paths.Select(p => p.Replace('\\', '/').ToLowerInvariant()).Distinct()
            .GroupBy(p => p.Contains('/') ? p[..p.LastIndexOf('/')] : ""))
        {
            ulong hash = MurmurHash64A.Hash(Encoding.UTF8.GetBytes(group.Key));
            int known = records.FindIndex(r => r.Hash == hash);
            var block = new List<byte>();
            if (known >= 0)
            {
                var (_, offset, size, recursive) = records[known];
                block.AddRange(table.GetRange(offset, size));
                // Leave base mode if the block ends in it, then enter and leave it once: no bases are left.
                bool baseMode = false;
                for (int pos = offset; pos + 4 <= offset + size;)
                {
                    int n = BinaryPrimitives.ReadInt32LittleEndian(table.GetRange(pos, 4).ToArray());
                    pos += 4;
                    if (n == 0) { baseMode = !baseMode; continue; }
                    pos = table.IndexOf(0, pos) + 1;
                }
                if (baseMode) Number(block, 0);
                Number(block, 0);
                Number(block, 0);
            }
            foreach (string path in group)
            {
                Number(block, 1);
                block.AddRange(Encoding.UTF8.GetBytes(path));
                block.Add(0);
                added++;
            }
            int at = table.Count;
            table.AddRange(block);
            if (known >= 0)
            {
                var r = records[known];
                records[known] = (r.Hash, at, block.Count, r.Recursive + block.Count - r.Size);
            }
            else records.Add((hash, at, block.Count, block.Count));
        }
        var directories = new byte[4 + records.Count * 20];
        BinaryPrimitives.WriteInt32LittleEndian(directories, records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            var d = directories.AsSpan(4 + i * 20);
            BinaryPrimitives.WriteUInt64LittleEndian(d, records[i].Hash);
            BinaryPrimitives.WriteInt32LittleEndian(d[8..], records[i].Offset);
            BinaryPrimitives.WriteInt32LittleEndian(d[12..], records[i].Size);
            BinaryPrimitives.WriteInt32LittleEndian(d[16..], records[i].Recursive);
        }
        _directories = directories;
        _tail = RawBundle.Write(table.ToArray());
        return added;
    }

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
