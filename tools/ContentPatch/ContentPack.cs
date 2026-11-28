using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalExile.ContentPatch;

/// <summary>
/// A mod's changes to the client's data, from JSON files in its <c>content/</c> folder:
/// <code>
/// {
///   "rows":  [ { "table": "Mods", "copyOf": { "Id": "IncreasedLife1" },
///                "set": { "Id": "LocalExileMarker", "Name": "Exciting", "Stats": [], "Domain": 1 } } ],
///   "edits": [ { "table": "BaseItemTypes", "where": { "Id": "Metadata/Items/Currency/CurrencyRerollRare" },
///                "set": { "Name": "Chaos Orb (local)" } } ],
///   "files": [ { "path": "Art/2DItems/Currency/LocalExile/Marker.dds", "from": "art/marker.dds" } ]
/// }
/// </code>
/// A row is a copy of the first row matching <c>copyOf</c>, with <c>set</c> written over it. An edit changes the rows
/// matching <c>where</c>. A value is a string, a number, true/false, an array of numbers, or a reference to a row:
/// <c>{ "row": { "table": "BaseItemTypes", "Id": "…" } }</c> (also inside arrays), which may name a row this pack or an
/// earlier one adds. <c>files</c> adds a file or replaces one the client has (a texture, a .ot, a passive tree file).
/// </summary>
public sealed record ContentPack(string Source, string Folder, JsonObject Json)
{
    public static ContentPack Load(string file) =>
        new(file, Path.GetDirectoryName(Path.GetFullPath(file))!, JsonNode.Parse(File.ReadAllText(file),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject());

    /// <summary>The packs of every enabled mod: <c>Mods/Binaries/&lt;id&gt;/content/*.json</c> and
    /// <c>Mods/Source/&lt;id&gt;/content/*.json</c>, in id order, skipping mods <c>Mods/Binaries/mod-state.txt</c> turns off.</summary>
    public static List<ContentPack> FromMods(string modsFolder)
    {
        var off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string state = Path.Combine(modsFolder, "Binaries", "mod-state.txt");
        if (File.Exists(state))
            foreach (var line in File.ReadAllLines(state))
                if (line.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var id, "off"]) off.Add(id);
        var packs = new List<ContentPack>();
        foreach (string kind in new[] { "Binaries", "Source" })
        {
            string root = Path.Combine(modsFolder, kind);
            if (!Directory.Exists(root)) continue;
            foreach (string mod in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (off.Contains(Path.GetFileName(mod))) continue;
                string content = Path.Combine(mod, "content");
                if (!Directory.Exists(content)) continue;
                foreach (string file in Directory.GetFiles(content, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                    packs.Add(Load(file));
            }
        }
        return packs;
    }
}
