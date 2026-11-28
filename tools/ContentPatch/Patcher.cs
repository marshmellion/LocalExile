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
    readonly GGPK? _ggpk;
    // A Steam install (loose Bundles2\ files): our bundle and the patched index are written as files there, the
    // original index kept in the backup folder as original_index.steam.bin.
    readonly string? _steam;
    readonly IReadOnlyDictionary<string, SchemaTable> _schema;
    readonly Oodle _oodle = new();
    readonly Dictionary<int, byte[]> _bundleCache = [];
    GameIndex _original = null!;
    IndexEditor _index = null!;
    readonly Dictionary<string, DatEditor> _tables = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly Action<string> _log;
    readonly ServerMirror? _mirror;
    readonly Func<string, long>? _serverRows;

    /// <param name="serverRows">client.db's row count of a table, to check its rows line up with the client's; null to
    /// skip the server side (export, tests).</param>
    public Patcher(string ggpkPath, string schemaFile, string backupFolder, Action<string> log, ServerMirror? mirror = null,
        Func<string, long>? serverRows = null)
    {
        _mirror = mirror;
        _serverRows = serverRows;
        _ggpkPath = ggpkPath;
        _backup = Path.Combine(backupFolder, "original_index.bin");
        _schema = DatSchema.LoadFile(schemaFile);
        _ggpk = new GGPK(ggpkPath);
        _log = log;
        LoadOriginalIndex();
    }

    /// <summary>A Steam install (its folder holds Bundles2\_.index.bin, the client reads those loose files).</summary>
    public Patcher(DirectoryInfo steamFolder, string schemaFile, string backupFolder, Action<string> log, ServerMirror? mirror = null,
        Func<string, long>? serverRows = null)
    {
        _mirror = mirror;
        _serverRows = serverRows;
        _ggpkPath = "";
        _steam = steamFolder.FullName;
        _backup = Path.Combine(backupFolder, "original_index.steam.bin");
        _schema = DatSchema.LoadFile(schemaFile);
        _log = log;
        byte[] current = ReadGgpk(ClientFiles.Index);
        byte[] content = Decompress(current);
        if (!IndexEditor.Parse(content).Bundles.Any(b => b.Name == OurBundle))
        {
            // The index is the client's own (a new client, or one Steam or a copy replaced): it is the original now.
            Directory.CreateDirectory(Path.GetDirectoryName(_backup)!);
            if (!File.Exists(_backup) || !File.ReadAllBytes(_backup).AsSpan().SequenceEqual(current))
            {
                File.WriteAllBytes(_backup, current);
                _log($"kept the client's original index in {_backup}");
            }
        }
        else if (File.Exists(_backup)) content = Decompress(File.ReadAllBytes(_backup));
        else throw new InvalidOperationException($"the client's index is already patched and {_backup} is missing: put the client's own Bundles2\\_.index.bin back first");
        _original = GameIndex.Parse(content);
        _index = IndexEditor.Parse(content);
    }

    public void Dispose()
    {
        _ggpk?.Dispose();
        _oodle.Dispose();
    }

    byte[] ReadGgpk(string path) => _steam is not null
        ? File.ReadAllBytes(Path.Combine(_steam, path.Replace('/', Path.DirectorySeparatorChar)))
        : _ggpk!.Root.TryFindNode(path, out var node) && node is FileRecord fr ? fr.Read() : throw new FileNotFoundException(path);

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
        var editor = DatEditor.Parse(file, schema);
        // A row's number is its id on both sides (references, client.db's _row): the server's copy of the table must
        // have the client's rows, or every row this patch adds would land on another number there.
        if (_serverRows?.Invoke(schema.Name) is long server && server != editor.RowCount)
            throw new InvalidDataException($"{schema.Name}: the client has {editor.RowCount} rows, client.db {server}: they are not the same tables, so nothing is patched");
        return _tables[name] = editor;
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
                ? found : AddRow(table, template);
            Set(table, row, e["set"]?.AsObject(), label);
            UniqueHashes(table, row, template, e["set"]?.AsObject());
            _log($"{label}: {table.Name} row {row} from row {template}");
        }
        foreach (var node in pack.Json["serverRows"]?.AsArray() ?? [])
        {
            // Rows only client.db has (the servers' own tables, such as item_types): no client file changes.
            var e = node!.AsObject();
            string table = Str(e["table"], "serverRows[].table");
            if (_mirror is null) throw new InvalidOperationException($"{label}: serverRows need data/client.db");
            object? Sql(JsonNode? v) => v switch
            {
                null => null,
                JsonArray or JsonObject => v.ToJsonString(),
                JsonValue j when j.TryGetValue(out string? t) => t,
                JsonValue j when j.TryGetValue(out bool b) => b ? 1L : 0L,
                JsonValue j when j.TryGetValue(out long l) => l,
                JsonValue j => j.GetValue<double>(),
                _ => throw new InvalidDataException($"{label}: unsupported value {v.ToJsonString()}"),
            };
            var copyOf = (e["copyOf"] as JsonObject ?? throw new InvalidDataException($"{label}: serverRows[].copyOf missing"))
                .ToDictionary(kv => kv.Key, kv => Sql(kv.Value));
            var set = (e["set"] as JsonObject ?? new JsonObject()).ToDictionary(kv => kv.Key, kv => Sql(kv.Value));
            _mirror.AddServerRow(table, copyOf, set, label);
            _log($"{label}: server-only {table} row from {string.Join(", ", copyOf.Select(kv => $"{kv.Key}={kv.Value}"))}");
        }
        foreach (var node in pack.Json["files"]?.AsArray() ?? [])
        {
            var e = node!.AsObject();
            string path = Str(e["path"], "files[].path").Replace('\\', '/');
            string Kind(string p) => _files.ContainsKey(p) ? "changed again" : _original.TryGetFile(p, out _) ? "replaces the client's" : "new";
            if (e["image"] is { } || e["imageBase64"] is { })
            {
                // A PNG becomes the client's texture pair: path (.dds) and path.header (Texture). imageBase64 carries the PNG in
                // the pack's text itself, for packs copied where binary files do not travel well.
                if (!path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{label}: files[].image needs a path ending in .dds ({path})");
                byte[] png = e["imageBase64"] is { } inline ? Convert.FromBase64String(inline.GetValue<string>())
                    : File.ReadAllBytes(Path.Combine(pack.Folder, e["image"]!.GetValue<string>()));
                var (dds, header) = Texture.FromPng(png);
                _log($"{label}: texture {path} + .header ({Kind(path)})");
                _files[path] = dds;
                _files[path + ".header"] = header;
            }
            else if (e["appendLines"] is JsonArray append)
            {
                // Lines added at the end of a text file the client has (or an earlier entry wrote), in its own encoding:
                // UTF-16LE, with or without a byte order mark (art/uiimages1.txt has none), or UTF-8.
                byte[] current = _files.TryGetValue(path, out var had) ? had : ReadOriginal(path)
                    ?? throw new InvalidDataException($"{label}: {path} is not in the client, nothing to append to");
                bool bom = current.Length >= 2 && current[0] == 0xFF && current[1] == 0xFE;
                bool utf16 = bom || (current.Length >= 2 && current[0] != 0 && current[1] == 0);
                var enc = utf16 ? (System.Text.Encoding)new System.Text.UnicodeEncoding(false, false) : new System.Text.UTF8Encoding(false);
                string text = enc.GetString(current, bom ? 2 : 0, current.Length - (bom ? 2 : 0));
                if (text.Length > 0 && !text.EndsWith('\n')) text += "\r\n";
                foreach (var line in append) text += line!.GetValue<string>() + "\r\n";
                byte[] body = enc.GetBytes(text);
                _files[path] = bom ? [0xFF, 0xFE, .. body] : body;
                _log($"{label}: {append.Count} line(s) appended to {path}");
            }
            else
            {
                string from = Path.Combine(pack.Folder, Str(e["from"], "files[].from"));
                _log($"{label}: file {path} ({Kind(path)})");
                _files[path] = File.ReadAllBytes(from);
            }
        }
    }

    int AddRow(DatEditor table, int template)
    {
        int row = table.AddCopyOf(template);
        _mirror?.Add(table.Name, row, template);
        // A copy has its template's text until the pack sets its own: indexed under it meanwhile.
        foreach (var ((name, column), index) in _textIndex)
            if (name == table.Name) Indexed(index, table.GetString(row, column)).Add(row);
        return row;
    }

    // Rows by a text column's value (an Id), built the first time a pack looks a table up by it and kept up to date as
    // rows are added and set: a pack of thousands of rows that name other rows by Id stays one pass per lookup table.
    readonly Dictionary<(string Table, string Column), Dictionary<string, List<int>>> _textIndex = [];

    static List<int> Indexed(Dictionary<string, List<int>> index, string key) =>
        index.TryGetValue(key, out var rows) ? rows : index[key] = [];

    Dictionary<string, List<int>> TextIndex(DatEditor table, string column)
    {
        if (_textIndex.TryGetValue((table.Name, column), out var index)) return index;
        index = new(StringComparer.Ordinal);
        for (int r = 0; r < table.RowCount; r++) Indexed(index, table.GetString(r, column)).Add(r);
        return _textIndex[(table.Name, column)] = index;
    }

    void SetText(DatEditor table, int row, string column, string value)
    {
        if (_textIndex.TryGetValue((table.Name, column), out var index))
        {
            Indexed(index, table.GetString(row, column)).Remove(row);
            var rows = Indexed(index, value);
            rows.Add(row);
            rows.Sort();
        }
        table.SetString(row, column, value);
    }

    static string Str(JsonNode? n, string what) => n?.GetValue<string>() ?? throw new InvalidDataException($"missing {what}");

    IEnumerable<int> Matching(DatEditor table, JsonObject where)
    {
        // One text column (an Id): from the index, in row order.
        if (where.Count == 1 && where.First() is var (only, node) && node is JsonValue text && text.TryGetValue<string>(out var key)
            && table.HasColumn(only))
            return TextIndex(table, only).TryGetValue(key, out var hits) ? [.. hits] : [];
        return MatchingScan(table, where);
    }

    IEnumerable<int> MatchingScan(DatEditor table, JsonObject where)
    {
        var resolved = new Dictionary<JsonObject, long>(ReferenceEqualityComparer.Instance);
        long Resolved(JsonObject o) => resolved.TryGetValue(o, out long row) ? row : resolved[o] = RowOf(o, $"{table.Name} where");
        for (int r = 0; r < table.RowCount; r++)
        {
            bool all = true;
            foreach (var (column, value) in where)
            {
                all = value switch
                {
                    JsonValue v when v.TryGetValue<string>(out var s) => table.GetString(r, column) == s,
                    JsonValue v when v.TryGetValue<long>(out var l) => table.GetInt(r, column) == l,
                    // A row reference: { "row": { "table": T, column: value } } (resolved once per call below).
                    JsonObject o when o["row"] is not null => table.GetInt(r, column) == Resolved(o),
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
            void Mirror(object? sql) => _mirror?.Set(table.Name, row, column, sql);
            switch (value)
            {
                case null: table.SetInt(row, column, -1); Mirror(null); break;
                case JsonObject reference: { long target = RowOf(reference, label); table.SetInt(row, column, target); Mirror(target); break; }
                case JsonArray array when array.All(x => x is JsonValue v && v.TryGetValue<string>(out _)):
                {
                    var strings = array.Select(x => x!.GetValue<string>()).ToList();
                    if (strings.Count == 0) table.SetArray(row, column, []);
                    else table.SetStringArray(row, column, strings);
                    Mirror(System.Text.Json.JsonSerializer.Serialize(strings));
                    break;
                }
                case JsonArray array:
                {
                    var values = array.Select(x => x is JsonObject o ? RowOf(o, label) : x!.GetValue<long>()).ToList();
                    table.SetArray(row, column, values);
                    Mirror(System.Text.Json.JsonSerializer.Serialize(values));
                    break;
                }
                case JsonValue v when v.TryGetValue<string>(out var s): SetText(table, row, column, s); Mirror(s); break;
                case JsonValue v when v.TryGetValue<bool>(out var b): table.SetInt(row, column, b ? 1 : 0); Mirror(b ? 1L : 0L); break;
                case JsonValue v when v.TryGetValue<long>(out var l): table.SetInt(row, column, l); Mirror(l); break;
                case JsonValue v when v.TryGetValue<double>(out var d): table.SetFloat(row, column, (float)d); Mirror(d); break;
                default: throw new InvalidDataException($"{label}: {table.Name}.{column}: cannot write {value.ToJsonString()}");
            }
        }
    }

    /// <summary>
    /// A new row's protocol id: the client and the server name a base type by its HASH32 and a modifier, an area or a
    /// visual by its HASH16, so a copy keeping its template's would be shown as the template. Unless the pack sets one,
    /// the row gets a value of its own, from its Id (the same on every apply, so saved items keep it), moved on past
    /// any value another row has.
    /// </summary>
    void UniqueHashes(DatEditor table, int row, int template, JsonObject? set)
    {
        foreach (var (column, bits) in new[] { ("HASH16", 16), ("HASH32", 32) })
        {
            if (!table.HasColumn(column) || set?.ContainsKey(column) == true) continue;
            if (table.GetInt(row, column) != table.GetInt(template, column)) continue;
            var taken = new HashSet<long>();
            for (int r = 0; r < table.RowCount; r++) if (r != row) taken.Add(table.GetInt(r, column));
            string id = table.HasColumn("Id") ? table.GetString(row, "Id") : row.ToString();
            // A base type's HASH32 is MurmurHash2-32 (seed 0) of its Id's UTF-8, in every row of the client's own table,
            // and the client relies on it: a base type with another value made the client close as the item appeared
            // (2026-11-20). The HASH16 columns are not derived from the Id this way, so those only need to be unique.
            if (table.Name == "BaseItemTypes" && column == "HASH32")
            {
                long derived = MurmurHash2(System.Text.Encoding.UTF8.GetBytes(id));
                if (taken.Contains(derived)) throw new InvalidDataException($"BaseItemTypes: the Id {id} has the same HASH32 as another base type; choose another Id");
                table.SetInt(row, column, derived);
                _mirror?.Set(table.Name, row, column, derived);
                continue;
            }
            ulong seed = MurmurHash64A.Hash(System.Text.Encoding.UTF8.GetBytes(id));
            long mask = bits == 16 ? 0xffff : 0xffffffff;
            long value = (long)(seed & (ulong)mask);
            while (value == 0 || taken.Contains(value)) value = (value + 1) & mask;
            table.SetInt(row, column, value);
            _mirror?.Set(table.Name, row, column, value);
        }
    }

    /// <summary>MurmurHash2, 32-bit, seed 0.</summary>
    static uint MurmurHash2(ReadOnlySpan<byte> data)
    {
        const uint m = 0x5bd1e995;
        uint h = (uint)data.Length;
        int i = 0;
        for (; data.Length - i >= 4; i += 4)
        {
            uint k = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
            k *= m; k ^= k >> 24; k *= m;
            h *= m; h ^= k;
        }
        int rest = data.Length - i;
        if (rest == 3) h ^= (uint)data[i + 2] << 16;
        if (rest >= 2) h ^= (uint)data[i + 1] << 8;
        if (rest >= 1) { h ^= data[i]; h *= m; }
        h ^= h >> 13; h *= m; h ^= h >> 15;
        return h;
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
        // New paths also go into the index's path table (its directory records), as the client's own files are listed.
        var newPaths = places.Select(p => p.Path).Where(p => !_index.Has(p)).ToList();
        foreach (var (path, offset, size) in places) _index.Set(path, bundle, offset, size);
        if (newPaths.Count > 0) _log($"{_index.AddToPathTable(newPaths, Decompress)} path(s) added to the index's path table");
        byte[] ourBundle = RawBundle.Write(blob.ToArray());
        byte[] index = RawBundle.Write(_index.Write());

        if (_steam is not null)
        {
            string bundlePath = Path.Combine(_steam, OurBundleFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
            File.WriteAllBytes(bundlePath, ourBundle);
            File.WriteAllBytes(Path.Combine(_steam, ClientFiles.Index.Replace('/', Path.DirectorySeparatorChar)), index);
            _log($"wrote {places.Count} file(s), {blob.Length:N0} bytes, into {OurBundleFile}; the index has {_index.FileCount:N0} files in {_index.Bundles.Count} bundles");
            _mirror?.Commit(_log);
            return [];
        }
        if (!_ggpk!.Root.TryFindNode(OurBundleFile, out var node))
        {
            _ggpk.Root.FindOrAddDirectory("Bundles2", out var bundles);
            bundles.FindOrAddDirectory("LocalExile", out var ours);
            ours.AddFile("Content.bundle.bin", out var added, ourBundle.Length);
            node = added;
        }
        ((FileRecord)node!).Write(ourBundle);
        ((FileRecord)(_ggpk.Root.TryFindNode(ClientFiles.Index, out var ix) ? ix : throw new FileNotFoundException(ClientFiles.Index))!).Write(index);
        _log($"wrote {places.Count} file(s), {blob.Length:N0} bytes, into {OurBundleFile}; the index has {_index.FileCount:N0} files in {_index.Bundles.Count} bundles");
        _mirror?.Commit(_log);
        return RootHash();
    }

    byte[] RootHash()
    {
        _ggpk!.Dispose();
        GgpkMaintenance.RepairRootHash(_ggpkPath);
        return GgpkMaintenance.RootHash(_ggpkPath);
    }

    /// <summary>A Steam install: puts the client's original index back (our bundle stays, unused).</summary>
    public static void RestoreSteam(string steamFolder, string backupFolder, Action<string> log)
    {
        string backup = Path.Combine(backupFolder, "original_index.steam.bin");
        if (!File.Exists(backup)) { log("no original index kept: the client was never patched"); return; }
        File.WriteAllBytes(Path.Combine(steamFolder, ClientFiles.Index.Replace('/', Path.DirectorySeparatorChar)), File.ReadAllBytes(backup));
        log("the client's original index is back");
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
