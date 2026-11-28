using System.Buffers.Binary;
using GameServer.Data.Ggpk;
using LocalExile.ContentPatch;

// LocalExile content patcher: puts mods' content packs (Mods/*/<id>/content/*.json) into a standalone client, or, for a
// Steam install, into Mods/Client/.merged-cache, the files NativeMods (Client\version.dll) hands the game.
// Run from the repository root, with the client closed:
//   inspect [client]                    what the client's index looks like (and whether it is patched)
//   export  [client] <out> <path>...    copies files out of the client as it was before any patch (tables, art)
//   apply   [client] [--mods <folder>] [--if-changed]
//                                       applies every enabled mod's content packs (always from the original index);
//                                       --if-changed does nothing when the packs, their files, this patcher and the
//                                       client's state are what the last apply saw (live.ps1 runs it at every start)
//   restore [client]                    puts the client's original index back
//   paths   [client] <out.txt>          writes every path in the client's index (to find art, tables, UI files)
// <client> is the client folder or its Content.ggpk (default Client).
const string Schema = "data/schema/schema.min.json";
const string Backup = "data/content-patch";
const string ClientDb = "data/client.db";

if (args.Length == 0) { Console.WriteLine("usage: inspect | export <out> <path>... | apply [--mods <folder>] | restore   [client first, default Client]"); return 2; }
string command = args[0];
var rest = args.Skip(1).ToList();
string client = rest.Count > 0 && !rest[0].StartsWith("--") && (File.Exists(rest[0]) || Directory.Exists(Path.Combine(rest[0], "Bundles2")) || File.Exists(Path.Combine(rest[0], "Content.ggpk")))
    ? Take() : "Client";
string ggpk = File.Exists(client) ? client : Path.Combine(client, "Content.ggpk");
// A Steam install has no Content.ggpk: its loose Bundles2\ files are read, never written, and the changed files go to
// the folder NativeMods hands the game (Mods/Client/.merged-cache, or --out <folder>).
string clientFolder = File.Exists(client) ? Path.GetDirectoryName(Path.GetFullPath(client))! : client;
bool steam = !File.Exists(ggpk) && File.Exists(Path.Combine(clientFolder, "Bundles2", "_.index.bin"));
int outAt = rest.IndexOf("--out");
string mergedCache = outAt >= 0 ? rest[outAt + 1] : Path.Combine("Mods", "Client", ".merged-cache");
if (!File.Exists(ggpk) && !steam) { Console.Error.WriteLine($"[content] neither {ggpk} nor {Path.Combine(clientFolder, "Bundles2", "_.index.bin")} found"); return 1; }
void Log(string s) => Console.WriteLine($"[content] {s}");
Patcher Open(ServerMirror? mirror = null, Func<string, long>? rows = null) => steam
    ? new Patcher(new DirectoryInfo(clientFolder), mergedCache, Schema, Log, mirror, rows)
    : new Patcher(ggpk, Schema, Backup, Log, mirror, rows);

try
{
    switch (command)
    {
        case "inspect": if (steam) { Log($"a Steam install: its files are not changed; content goes to {mergedCache}"); return 0; } return Inspect();
        case "paths":
        {
            string outFile = Take();
            using var oodle = new Oodle();
            using var fs = GameFileSystem.Open(client, oodle);
            using var w = new StreamWriter(outFile);
            int n = 0;
            foreach (string path in fs.EnumeratePaths().Order(StringComparer.Ordinal)) { w.WriteLine(path); n++; }
            Log($"{n:N0} paths written to {outFile}");
            return 0;
        }
        case "export":
        {
            string outFolder = Take();
            using var p = Open();
            foreach (string path in rest.Where(r => r != "--out" && r != mergedCache))
            {
                var data = p.ReadOriginal(path);
                if (data is null) { Log($"{path}: not in the client"); continue; }
                string to = Path.Combine(outFolder, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.WriteAllBytes(to, data);
                Log($"{path}: {data.Length:N0} bytes");
            }
            return 0;
        }
        case "apply":
        {
            int m = rest.IndexOf("--mods");
            string mods = m >= 0 ? rest[m + 1] : "Mods";
            var packs = ContentPack.FromMods(mods);
            Log(ClientHooks.Apply(PatchedExe(), ItemSymbols(packs)));
            string stamp = Path.Combine(Backup, "applied.txt");
            string print = Fingerprint(packs);
            if (rest.Contains("--if-changed"))
            {
                bool patched = IsPatched();
                if (packs.Count == 0 && !patched) { Log("no content packs, client not patched: nothing to do"); return 0; }
                if (packs.Count == 0)
                {
                    Log("no content packs any more: restoring the client and client.db");
                    if (File.Exists(ClientDb)) new ServerMirror(ClientDb).Restore(Log);
                    if (steam) { if (Directory.Exists(mergedCache)) Directory.Delete(mergedCache, true); }
                    else if (Patcher.Restore(ggpk, Backup, Log) is { } back) SaveRootHash(back);
                    if (File.Exists(stamp)) File.Delete(stamp);
                    return 0;
                }
                if (patched && File.Exists(stamp) && File.ReadAllText(stamp).Trim() == print)
                { Log($"{packs.Count} content pack(s) unchanged since the last apply: nothing to do"); return 0; }
            }
            Log($"{packs.Count} content pack(s) in {mods}");
            // The first start of a new checkout: the Gateway has not built client.db from sql/ yet; build it here, as it
            // would, so the content goes in now instead of at the next start.
            if (!File.Exists(ClientDb))
            {
                Log($"building {ClientDb} from sql/ (the first start) ...");
                GameServer.Storage.SqlUpdater.Update(ClientDb, "client", log: Log);
            }
            byte[] root;
            var mirror = new ServerMirror(ClientDb);
            using (var p = Open(mirror, ServerRows))
            {
                foreach (var pack in packs) p.Apply(pack);
                root = p.Commit();
            }
            if (!steam) SaveRootHash(root);
            Directory.CreateDirectory(Backup);
            File.WriteAllText(stamp, print);
            return 0;
        }
        case "restore":
            if (File.Exists(Path.Combine(Backup, "applied.txt"))) File.Delete(Path.Combine(Backup, "applied.txt"));
            Log(ClientHooks.Apply(PatchedExe(), []));
            if (File.Exists(ClientDb)) new ServerMirror(ClientDb).Restore(Log);
            if (steam) { if (Directory.Exists(mergedCache)) Directory.Delete(mergedCache, true); Log($"{mergedCache} removed"); }
            else if (Patcher.Restore(ggpk, Backup, Log) is { } restored) SaveRootHash(restored);
            return 0;
        default:
            Console.Error.WriteLine($"[content] unknown command {command}");
            return 2;
    }
}
catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or FileNotFoundException or KeyNotFoundException or IOException)
{
    Console.Error.WriteLine($"[content] {ex.Message}");
    return 1;
}

// client.db's rows of a table as built from sql/ (the patcher's own rows from an earlier apply are not counted).
static long ServerRows(string table)
{
    using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={ClientDb};Mode=ReadOnly;Pooling=False");
    db.Open();
    using var c = db.CreateCommand();
    c.CommandText = "SELECT name FROM sqlite_master WHERE name = 'localexile_content_tables'";
    bool tracked = c.ExecuteScalar() is not null;
    c.CommandText = tracked ? $"SELECT original_count FROM localexile_content_tables WHERE tbl = '{table.Replace("'", "''")}'" : "SELECT NULL";
    if (c.ExecuteScalar() is long kept) return kept;
    c.CommandText = $"SELECT COALESCE(MAX(_row) + 1, 0) FROM \"{table.Replace("\"", "")}\"";
    return c.ExecuteScalar() is long n ? n : 0;
}

string Take() { string v = rest[0]; rest.RemoveAt(0); return v; }

// The copy of the client LocalExile starts (GameServer.Auth patch), beside Content.ggpk.
string PatchedExe() => Path.Combine(Path.GetFullPath(clientFolder), "Game.patched.exe");

// Every pack's "itemSymbols": [{ "mod": <Mods Id of an enchantment>, "sprite": <UI image name> }], in pack order.
static List<ClientHooks.ItemSymbol> ItemSymbols(List<ContentPack> packs) =>
    [.. packs.SelectMany(p => p.Json["itemSymbols"]?.AsArray() ?? [])
        .Select(n => new ClientHooks.ItemSymbol(n!["mod"]!.GetValue<string>(), n["sprite"]!.GetValue<string>()))];

// Whether the client's index is one this patcher wrote: raw blocks (GGG's never are) naming our bundle.
bool IsPatched() => steam ? Directory.Exists(mergedCache) : ClientFiles.TryReadFile(ggpk, ClientFiles.Index) is { } raw && RawBundle.TryReadRaw(raw) is { } index
    && index.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(Patcher.OurBundle)) >= 0;

// The packs, every file in their content folders, and this patcher's own code: what an apply depends on.
static string Fingerprint(List<ContentPack> packs)
{
    using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
    void Add(string text) => sha.AppendData(System.Text.Encoding.UTF8.GetBytes(text + "\n"));
    Add(typeof(Patcher).Assembly.ManifestModule.ModuleVersionId.ToString());
    foreach (string folder in packs.Select(p => p.Folder).Distinct().Order(StringComparer.Ordinal))
        foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            Add(Path.GetRelativePath(folder, file) + "@" + folder);
            sha.AppendData(File.ReadAllBytes(file));
        }
    return Convert.ToHexString(sha.GetHashAndReset());
}

// The patch server tells the client its data is current only when the client's root hash is the one in
// data/client-constants.json (GameServer.Patch's SaveRootHash does the same after its gateway-list write).
void SaveRootHash(byte[] root)
{
    if (!File.Exists(ClientConstants.DefaultFile)) { Log($"no {ClientConstants.DefaultFile} yet: the next extraction reads the root hash"); return; }
    (ClientConstants.Load() with { GgpkRootHash = Convert.ToHexString(root) }).Save();
    Log($"root hash {Convert.ToHexString(root)[..16]}… saved to {ClientConstants.DefaultFile}");
}

int Inspect()
{
    byte[] raw = ClientFiles.TryReadFile(ggpk, ClientFiles.Index) ?? throw new FileNotFoundException(ClientFiles.Index);
    var h = raw.AsSpan();
    Log($"index bundle: size {BinaryPrimitives.ReadInt32LittleEndian(h):N0}, payload {BinaryPrimitives.ReadInt32LittleEndian(h[4..]):N0}, head {BinaryPrimitives.ReadInt32LittleEndian(h[8..])}, " +
        $"encoder {BinaryPrimitives.ReadInt32LittleEndian(h[12..])}, word16 {BinaryPrimitives.ReadInt32LittleEndian(h[16..])}, chunks {BinaryPrimitives.ReadInt32LittleEndian(h[36..])}, " +
        $"granularity {BinaryPrimitives.ReadInt32LittleEndian(h[40..])}, words44-59 {Convert.ToHexString(h[44..60])}, raw blocks {(RawBundle.TryReadRaw(raw) is not null ? "yes (patched by us)" : "no")}");
    using var oodle = new Oodle();
    byte[] content = RawBundle.TryReadRaw(raw) ?? BundleFile.DecompressWhole(raw, oodle);
    var index = IndexEditor.Parse(content);
    Log($"{index.Bundles.Count:N0} bundles, {index.FileCount:N0} files, records sorted by hash: {index.SortedByHash}; ours: {(index.Bundles.Any(b => b.Name == Patcher.OurBundle) ? "present" : "absent")}");
    bool same = index.Write().AsSpan().SequenceEqual(content);
    Log($"index written back unchanged: {(same ? "identical" : "DIFFERENT (do not apply)")}");
    Log($"first bundles: {string.Join(", ", index.Bundles.Take(3).Select(b => b.Name))}");
    Log($"original index kept: {(File.Exists(Path.Combine(Backup, "original_index.bin")) ? "yes" : "no")}");
    return same ? 0 : 1;
}
