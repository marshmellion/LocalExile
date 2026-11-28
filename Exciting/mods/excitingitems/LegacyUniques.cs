using System.Text.Json;
using System.Text.Json.Nodes;
using GameServer.Core.Protocol.Instance;

/// <summary>Legacy uniques: old versions of uniques (Kaom's Heart before 1.0.0, Bramblejack before 3.19.0 ...) and uniques
/// the game removed, from Path of Building's export (legacyuniques.json, tools/make_legacy_uniques.py). Bosses (unique
/// monsters) drop one at 1 in BossChance; the version is the oldest by default, one named in Overrides, or a random old
/// one. They can carry the Legacy mark, so loot filters show them.</summary>
sealed record LegacyUniqueChoices(bool Enabled = true, int BossChance = 25, int RareChance = 0, string Version = "oldest",
    Dictionary<string, string>? Overrides = null, bool OldVersions = true, bool Removed = true, string[]? Kinds = null,
    bool Mark = true, string[]? Blacklist = null, bool Announce = true);

public sealed partial class ExcitingItemsMod
{
    sealed record LegacyMod(string Id, bool Implicit, int[][] Ranges);
    sealed record LegacyVersion(string Name, string Base, LegacyMod[] Mods);
    sealed record LegacyUnique(string Name, string Kind, bool Removed, bool Corrupted, bool InGame, LegacyVersion[] Versions);
    List<LegacyUnique> _legacyUniques = [];

    void LoadLegacyUniques(IModContext mod)
    {
        string file = Path.Combine(mod.Folder, "legacyuniques.json");
        if (!File.Exists(file)) { mod.Log("legacyuniques.json is missing: no legacy uniques"); return; }
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(file))!;
            _legacyUniques = [.. (root["uniques"] as JsonArray ?? []).OfType<JsonObject>().Select(u => new LegacyUnique(
                (string)u["name"]!, (string?)u["kind"] ?? "", (bool?)u["removed"] ?? false, (bool?)u["corrupted"] ?? false, (bool?)u["inGame"] ?? false,
                [.. (u["variants"] as JsonArray ?? []).OfType<JsonObject>().Select(v => new LegacyVersion((string)v["name"]!, (string)v["base"]!,
                    [.. (v["mods"] as JsonArray ?? []).OfType<JsonObject>().Select(m => new LegacyMod((string)m["id"]!, (bool?)m["implicit"] ?? false,
                        [.. (m["ranges"] as JsonArray ?? []).OfType<JsonArray>().Select(r => new[] { (int)r[0]!, (int)r[1]! })]))]))]))];
            mod.Log($"{_legacyUniques.Count} legacy uniques");
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException or FormatException or NullReferenceException)
        { mod.Log($"legacyuniques.json not read: {e.Message}"); }
    }

    /// <summary>The old versions a unique can drop as: a unique still in the game without its current (last) version.</summary>
    static LegacyVersion[] OldOf(LegacyUnique u) => u.InGame && u.Versions.Length > 1 ? u.Versions[..^1] : u.Versions;

    IEnumerable<LegacyUnique> LegacyUniquePool(LegacyUniqueChoices c) => _legacyUniques.Where(u =>
        (u.Removed ? c.Removed : c.OldVersions && u.Versions.Length > 1)
        && (c.Kinds is null || c.Kinds.Contains(u.Kind))
        && !(c.Blacklist ?? []).Any(b => u.Name.Contains(b, StringComparison.OrdinalIgnoreCase)));

    LegacyVersion PickVersion(LegacyUnique u, LegacyUniqueChoices c, Random rng)
    {
        var old = OldOf(u);
        if (c.Overrides is { } overrides && overrides.TryGetValue(u.Name, out string? wanted))
        {
            if (wanted.Equals("random", StringComparison.OrdinalIgnoreCase)) return old[rng.Next(old.Length)];
            if (wanted.Equals("newest", StringComparison.OrdinalIgnoreCase)) return old[^1];
            if (u.Versions.FirstOrDefault(v => v.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) is { } named) return named;
        }
        return c.Version switch
        {
            "random" => old[rng.Next(old.Length)],
            "newest" => old[^1],
            _ => old[0],
        };
    }

    /// <summary>The item of one version of a legacy unique, or null when the server cannot make it (its base or its
    /// name is not in the client's data).</summary>
    Item? MakeLegacyUnique(Player player, LegacyUnique u, LegacyVersion v, Random rng, bool mark)
    {
        var uniques = Game.UniquesFor(player);
        var implicits = v.Mods.Where(m => m.Implicit).Select(m => m.Id).ToArray();
        var explicits = v.Mods.Where(m => !m.Implicit).Select(m => m.Id).ToArray();
        var ranges = v.Mods.SelectMany(m => m.Ranges.Select((r, stat) => new UniqueCatalog.RollRange(m.Id, stat, r[0], r[1], m.Implicit))).ToArray();
        var known = uniques.Find(u.Name);
        var definition = known is not null
            ? known with { BaseTypeId = v.Base, Mods = explicits, Implicits = implicits.Length > 0 ? implicits : known.Implicits, Ranges = ranges,
                Variants = null, CreationVariant = null, Corrupted = known.Corrupted || u.Corrupted }
            : new UniqueCatalog.Definition(u.Name, v.Base, explicits, 86, u.Corrupted, implicits.Length > 0 ? implicits : null, ranges);
        Item? item;
        try { item = uniques.Create(definition, rng); }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or ArgumentException) { _log($"legacy unique {u.Name} ({v.Name}): {e.Message}"); return null; }
        if (item is null) return null;
        if (mark && LegacyMarker(Game.ItemsFor(player)) is { } marker)
            item = item with { Mods = [.. item.Mods, new ItemMod(ModSection.Enchant, marker.Hash, [])] };
        return item;
    }

    void LegacyUniqueDrops(DropRoll d)
    {
        var c = _choices.LegacyUniques ?? new();
        if (!c.Enabled || _legacyUniques.Count == 0) return;
        int chance = d.Monster.Rarity switch { MonsterRarity.Unique => c.BossChance, MonsterRarity.Rare => c.RareChance, _ => 0 };
        if (chance <= 0 || Random.Shared.Next(chance) != 0) return;
        if (RandomLegacyUnique(d.Player, c, Random.Shared, out string label) is not { } item) return;
        d.Made.Add(item);
        if (c.Announce) Tell(d.Player, $"* A legacy unique dropped: {label}.");
    }

    Item? RandomLegacyUnique(Player player, LegacyUniqueChoices c, Random rng, out string label, string? name = null, string? version = null)
    {
        label = "";
        var pool = (name is null ? LegacyUniquePool(c) : _legacyUniques.Where(u => u.Name.Contains(name, StringComparison.OrdinalIgnoreCase))).ToList();
        // A unique the server cannot make (a base or name the client no longer has) gives way to another.
        for (int tries = 0; tries < 20 && pool.Count > 0; tries++)
        {
            var u = pool[rng.Next(pool.Count)];
            var v = version is not null ? u.Versions.FirstOrDefault(x => x.Name.Contains(version, StringComparison.OrdinalIgnoreCase)) ?? PickVersion(u, c, rng)
                : PickVersion(u, c, rng);
            if (MakeLegacyUnique(player, u, v, rng, c.Mark) is { } item) { label = $"{u.Name} ({v.Name})"; return item; }
            pool.Remove(u);
        }
        return null;
    }
}
