using System.Buffers.Binary;
using GameServer.Data.Ggpk;
using LocalExile.ContentPatch;

// LocalExile content patcher: puts mods' content packs (Mods/*/<id>/content/*.json) into a standalone client.
// Run from the repository root, with the client closed:
//   inspect [client]                    what the client's index looks like (and whether it is patched)
//   export  [client] <out> <path>...    copies files out of the client as it was before any patch (tables, art)
//   apply   [client] [--mods <folder>]  applies every enabled mod's content packs (always from the original index)
//   restore [client]                    puts the client's original index back
// <client> is the client folder or its Content.ggpk (default Client).
const string Schema = "data/schema/schema.min.json";
const string Backup = "data/content-patch";

if (args.Length == 0) { Console.WriteLine("usage: inspect | export <out> <path>... | apply [--mods <folder>] | restore   [client first, default Client]"); return 2; }
string command = args[0];
var rest = args.Skip(1).ToList();
string client = rest.Count > 0 && !rest[0].StartsWith("--") && (File.Exists(rest[0]) || Directory.Exists(Path.Combine(rest[0], "Bundles2")) || File.Exists(Path.Combine(rest[0], "Content.ggpk")))
    ? Take() : "Client";
string ggpk = File.Exists(client) ? client : Path.Combine(client, "Content.ggpk");
if (!File.Exists(ggpk)) { Console.Error.WriteLine($"[content] {ggpk} not found: this patcher works on a standalone client (Content.ggpk)"); return 1; }
void Log(string s) => Console.WriteLine($"[content] {s}");

try
{
    switch (command)
    {
        case "inspect": return Inspect();
        case "export":
        {
            string outFolder = Take();
            using var p = new Patcher(ggpk, Schema, Backup, Log);
            foreach (string path in rest)
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
            Log($"{packs.Count} content pack(s) in {mods}");
            byte[] root;
            using (var p = new Patcher(ggpk, Schema, Backup, Log))
            {
                foreach (var pack in packs) p.Apply(pack);
                root = p.Commit();
            }
            SaveRootHash(root);
            return 0;
        }
        case "restore":
            if (Patcher.Restore(ggpk, Backup, Log) is { } restored) SaveRootHash(restored);
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

string Take() { string v = rest[0]; rest.RemoveAt(0); return v; }

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
    byte[] content = RawBundle.TryReadRaw(raw) ?? BundleFile.DecompressWhole(raw, new ManagedOodleDecoder());
    var index = IndexEditor.Parse(content);
    Log($"{index.Bundles.Count:N0} bundles, {index.FileCount:N0} files, records sorted by hash: {index.SortedByHash}; ours: {(index.Bundles.Any(b => b.Name == Patcher.OurBundle) ? "present" : "absent")}");
    bool same = index.Write().AsSpan().SequenceEqual(content);
    Log($"index written back unchanged: {(same ? "identical" : "DIFFERENT (do not apply)")}");
    Log($"first bundles: {string.Join(", ", index.Bundles.Take(3).Select(b => b.Name))}");
    Log($"original index kept: {(File.Exists(Path.Combine(Backup, "original_index.bin")) ? "yes" : "no")}");
    return same ? 0 : 1;
}
