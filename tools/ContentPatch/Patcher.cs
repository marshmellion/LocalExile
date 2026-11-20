using System.Text.Json.Nodes;
using GameServer.Data.Dat;
using GameServer.Data.Ggpk;
using LibGGPK3;
using LibGGPK3.Records;

namespace LocalExile.ContentPatch;

/// <summary>
/// Applies content packs to a standalone client (<c>Content.ggpk</c>). Always from the client's original index, kept
/// the first time in <c>data/content-patch/original_index.bin</c>: a second apply starts over instead of patching a
/// patched client, and <see cref="Restore"/> puts the original back. Every changed or added file goes into one bundle of
/// ours, <c>Bundles2/LocalExile/Content.bundle.bin</c>, written with raw blocks (RawBundle); the index points those paths
/// there, and nothing of GGG's bundles is touched. Never while the client runs: it holds the archive open.
/// </summary>
public sealed class Patcher : IDisposable
{
    public const string OurBundle = "LocalExile/Content";
    const string OurBundleFile = "Bundles2/" + OurBundle + ".bundle.bin";
    readonly string _ggpkPath;
    readonly string _backup;
    readonly GGPK _ggpk;
    readonly IReadOnlyDictionary<string, SchemaTable> _schema;
    readonly IOodleDecoder _oodle = new ManagedOodleDecoder();
    readonly Dictionary<int, byte[]> _bundleCache = [];
    GameIndex _original = null!;
    IndexEditor _index = null!;
    readonly Dictionary<string, DatEditor> _tables = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly Action<string> _log;

    public Patcher(string ggpkPath, string schemaFile, string backupFolder, Action<string> log)
    {
        _ggpkPath = ggpkPath;
        _backup = Path.Combine(backupFolder, "original_index.bin");
        _schema = DatSchema.LoadFile(schemaFile);
        _ggpk = new GGPK(ggpkPath);
        _log = log;
        LoadOriginalIndex();
    }

    public void Dispose() => _ggpk.Dispose();

    byte[] ReadGgpk(string path) =>
        _ggpk.Root.TryFindNode(path, out var node) && node is FileRecord fr ? fr.Read() : throw new FileNotFoundException(path);

    void LoadOriginalIndex()
    {
        byte[] current = ReadGgpk(ClientFiles.Index);
        var currentIndex = IndexEditor.Parse(Decompress(current));
        bool patched = currentIndex.Bundles.Any(b => b.Name == OurBundle);
        byte[] original;
        if (File.Exists(_backup)) original = File.ReadAllBytes(_backup);
        else if (patched) throw new InvalidOperationException($"the client is already patched and {_backup} is missing: restore the client's original index first");
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_backup)!);
            File.WriteAllBytes(_backup, current);
            original = current;
            _log($"kept the client's original index in {_backup}");
        }
        byte[] content = Decompress(original);
        _original = GameIndex.Parse(content);
        _index = IndexEditor.Parse(content);
    }

    byte[] Decompress(byte[] bundle) => RawBundle.TryReadRaw(bundle) ?? BundleFile.DecompressWhole(bundle, _oodle);

    /// <summary>A file as the unpatched client has it, or null.</summary>
    public byte[]? ReadOriginal(string path)
    {
        if (!_original.TryGetFile(path, out var at)) return null;
        if (!_bundleCache.TryGetValue(at.BundleIndex, out var bundle))
        {
            bundle = ReadGgpk(at.BundleFile);
            if (_bundleCache.Count > 64) _bundleCache.Clear();
            _bundleCache[at.BundleIndex] = bundle;
        }
        return BundleFile.DecompressSlice(bundle, at.Offset, at.Size, _oodle);
    }

    DatEditor Table(string name)
    {
        if (_tables.TryGetValue(name, out var t)) return t;
        if (!_schema.TryGetValue(name, out var schema)) throw new KeyNotFoundException($"no table {name} in the schema");
        byte[] file = ReadOriginal($"Data/{schema.Name}.datc64") ?? throw new FileNotFoundException($"Data/{schema.Name}.datc64");
        return _tables[name] = DatEditor.Parse(file, schema);
    }

    // ---- packs ---------------------------------------------------------------------------------------------------

    public void Apply(ContentPack pack)
    {
        string label = Path.GetRelativePath(Path.GetDirectoryName(pack.Folder)!, pack.Source);
        foreach (var node in pack.Json["edits"]?.AsArray() ?? [])
        {
            var e = node!.AsObject();
            var table = Table(Str(e["table"], "edits[].table"));
            var where = e["where"]?.AsObject() ?? throw new InvalidDataException($"{label}: an edit needs \"where\"");
            var rows = Matching(table, where).ToList();
            if (rows.Count == 0) throw new InvalidDataException($"{label}: no {table.Name} row matches {where.ToJsonString()}");
            foreach (int r in rows) Set(table, r, e["set"]?.AsObject(), label);
            _log($"{label}: edited {rows.Count} {table.Name} row(s)");
        }
        foreach (var node in pack.Json["rows"]?.AsArray() ?? [])
        {
            var e = node!.AsObject();
            var table = Table(Str(e["table"], "rows[].table"));
            var copyOf = e["copyOf"]?.AsObject() ?? throw new InvalidDataException($"{label}: a row needs \"copyOf\"");
            int template = Matching(table, copyOf).DefaultIfEmpty(-1).First();
            if (template < 0) throw new InvalidDataException($"{label}: no {table.Name} row matches {copyOf.ToJsonString()}");
            // A pack applied again (the same id) changes its own row instead of adding a second one.
            int row = e["set"]?["Id"] is JsonValue id && table.HasColumn("Id") && table.Find("Id", id.GetValue<string>()) is int found and >= 0
                ? found : table.AddCopyOf(template);
            Set(table, row, e["set"]?.AsObject(), label);
            _log($"{label}: {table.Name} row {row} from row {template}");
        }
        foreach (var node in pack.Json["files"]?.AsArray() ?? [])
        {
            var e = node!.AsObject();
            string path = Str(e["path"], "files[].path").Replace('\\', '/');
            string from = Path.Combine(pack.Folder, Str(e["from"], "files[].from"));
            _files[path] = File.ReadAllBytes(from);
            _log($"{label}: file {path} ({(_original.TryGetFile(path, out _) ? "replaces the client's" : "new")})");
        }
    }

    static string Str(JsonNode? n, string what) => n?.GetValue<string>() ?? throw new InvalidDataException($"missing {what}");

    IEnumerable<int> Matching(DatEditor table, JsonObject where)
    {
        for (int r = 0; r < table.RowCount; r++)
        {
            bool all = true;
            foreach (var (column, value) in where)
            {
                all = value switch
                {
                    JsonValue v when v.TryGetValue<string>(out var s) => table.GetString(r, column) == s,
                    JsonValue v when v.TryGetValue<long>(out var l) => table.GetInt(r, column) == l,
                    _ => false,
                };
                if (!all) break;
            }
            if (all) yield return r;
        }
    }

    long RowOf(JsonObject reference, string label)
    {
        var target = reference["row"]?.AsObject() ?? throw new InvalidDataException($"{label}: a reference is {{ \"row\": {{ \"table\": …, column: value }} }}");
        var table = Table(Str(target["table"], "row.table"));
        var where = new JsonObject();
        foreach (var (k, v) in target) if (k != "table") where[k] = v?.DeepClone();
        int r = Matching(table, where).DefaultIfEmpty(-1).First();
        return r >= 0 ? r : throw new InvalidDataException($"{label}: no {table.Name} row matches {where.ToJsonString()}");
    }

    void Set(DatEditor table, int row, JsonObject? set, string label)
    {
        if (set is null) return;
        foreach (var (column, value) in set)
        {
            switch (value)
            {
                case null: table.SetInt(row, column, -1); break;
                case JsonObject reference: table.SetInt(row, column, RowOf(reference, label)); break;
                case JsonArray array when array.All(x => x is JsonValue v && v.TryGetValue<string>(out _)):
                    if (array.Count == 0) table.SetArray(row, column, []);
                    else table.SetStringArray(row, column, array.Select(x => x!.GetValue<string>()).ToList());
                    break;
                case JsonArray array:
                    table.SetArray(row, column, array.Select(x => x is JsonObject o ? RowOf(o, label) : x!.GetValue<long>()).ToList());
                    break;
                case JsonValue v when v.TryGetValue<string>(out var s): table.SetString(row, column, s); break;
                case JsonValue v when v.TryGetValue<bool>(out var b): table.SetInt(row, column, b ? 1 : 0); break;
                case JsonValue v when v.TryGetValue<long>(out var l): table.SetInt(row, column, l); break;
                case JsonValue v when v.TryGetValue<double>(out var d): table.SetFloat(row, column, (float)d); break;
                default: throw new InvalidDataException($"{label}: {table.Name}.{column}: cannot write {value.ToJsonString()}");
            }
        }
    }

    // ---- writing -------------------------------------------------------------------------------------------------

    /// <summary>Writes our bundle and the patched index into the archive and gives the root a hash again. Returns the
    /// root hash (for data/client-constants.json).</summary>
    public byte[] Commit()
    {
        foreach (var (name, table) in _tables) _files[$"Data/{_schema[name].Name}.datc64"] = table.Write();
        using var blob = new MemoryStream();
        var places = new List<(string Path, int Offset, int Size)>();
        foreach (var (path, data) in _files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            places.Add((path, (int)blob.Length, data.Length));
            blob.Write(data);
        }
        int bundle = _index.Bundle(OurBundle, (int)blob.Length);
        foreach (var (path, offset, size) in places) _index.Set(path, bundle, offset, size);
        byte[] ourBundle = RawBundle.Write(blob.ToArray());
        byte[] index = RawBundle.Write(_index.Write());

        if (!_ggpk.Root.TryFindNode(OurBundleFile, out var node))
        {
            _ggpk.Root.FindOrAddDirectory("Bundles2", out var bundles);
            bundles.FindOrAddDirectory("LocalExile", out var ours);
            ours.AddFile("Content.bundle.bin", out var added, ourBundle.Length);
            node = added;
        }
        ((FileRecord)node!).Write(ourBundle);
        ((FileRecord)(_ggpk.Root.TryFindNode(ClientFiles.Index, out var ix) ? ix : throw new FileNotFoundException(ClientFiles.Index))!).Write(index);
        _log($"wrote {places.Count} file(s), {blob.Length:N0} bytes, into {OurBundleFile}; the index has {_index.FileCount:N0} files in {_index.Bundles.Count} bundles");
        return RootHash();
    }

    byte[] RootHash()
    {
        _ggpk.Dispose();
        GgpkMaintenance.RepairRootHash(_ggpkPath);
        return GgpkMaintenance.RootHash(_ggpkPath);
    }

    /// <summary>Puts the client's original index back (our bundle stays, unused).</summary>
    public static byte[]? Restore(string ggpkPath, string backupFolder, Action<string> log)
    {
        string backup = Path.Combine(backupFolder, "original_index.bin");
        if (!File.Exists(backup)) { log("no original index kept: the client was never patched"); return null; }
        using (var ggpk = new GGPK(ggpkPath))
        {
            if (!ggpk.Root.TryFindNode(ClientFiles.Index, out var ix) || ix is not FileRecord fr) throw new FileNotFoundException(ClientFiles.Index);
            fr.Write(File.ReadAllBytes(backup));
        }
        GgpkMaintenance.RepairRootHash(ggpkPath);
        log("the client's original index is back");
        return GgpkMaintenance.RootHash(ggpkPath);
    }
}
