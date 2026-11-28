using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>"Unobtainable uniques acquirable?": uniques the server has no working source for (unobtainable.json: no normal
/// drop, no boss or content that gives them, no card that drops) come from the normal drops, bosses, Rogue Exiles and
/// strongboxes instead. A unique the server later drops normally (not encounter-only, on a base that drops) leaves the
/// list by itself. Groups: heist (Replicas and Curio Display items), sanctum (Lycia and the relics), ultimatum, race
/// (race and event rewards), idol (the Idols part drops these already), other.</summary>
sealed record UnobtainableChoices(bool Enabled = true, int InPlaceOf = 25, int MapBosses = 40, int Pinnacles = 5, int RogueExiles = 30,
    int Strongboxes = 60, bool MapsOnly = false, bool Heist = true, bool Sanctum = true, bool Ultimatum = true, bool Race = true,
    bool Idols = false, bool Other = true, string[]? Blacklist = null, string[]? Extra = null, bool RarityWeights = true,
    double DefaultWeight = 0.1, bool Announce = true);

public sealed partial class ExcitingItemsMod
{
    readonly List<(string Name, string Group)> _unobtainable = [];

    void LoadUnobtainable(IModContext mod)
    {
        string file = Path.Combine(mod.Folder, "unobtainable.json");
        if (!File.Exists(file)) { mod.Log("unobtainable.json is missing: no unobtainable uniques"); return; }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var u in doc.RootElement.GetProperty("uniques").EnumerateArray())
                _unobtainable.Add((u.GetProperty("name").GetString() ?? "", u.GetProperty("group").GetString() ?? "other"));
            mod.Log($"{_unobtainable.Count} unobtainable uniques listed");
        }
        catch (Exception e) { mod.Log($"unobtainable.json not read: {e.Message}"); }
    }

    bool UnobtainableGroupOn(UnobtainableChoices c, string group) => group switch
    {
        "heist" => c.Heist, "sanctum" => c.Sanctum, "ultimatum" => c.Ultimatum, "race" => c.Race, "idol" => c.Idols, _ => c.Other,
    };

    /// <summary>The uniques this part may give now, each with its weight.</summary>
    List<(UniqueCatalog.Definition Unique, double Weight)> UnobtainablePool(Player player)
    {
        var c = _choices.Unobtainable ?? new();
        var uniques = Game.UniquesFor(player);
        var drops = Game.DropsFor(player);
        var items = Game.ItemsFor(player);
        var black = new HashSet<string>(c.Blacklist ?? [], StringComparer.OrdinalIgnoreCase);
        var names = _unobtainable.Where(u => UnobtainableGroupOn(c, u.Group)).Select(u => u.Name).Concat(c.Extra ?? [])
            .Where(n => !black.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase);
        var pool = new List<(UniqueCatalog.Definition, double)>();
        foreach (string name in names)
        {
            if (uniques.Find(name) is not { } u || items.Find(u.BaseTypeId) is not { Supported: true } type) continue;
            double w = drops.UniqueWeight(u.Name);
            // The server drops it normally now: not this part's any more (unless named in Extra).
            bool extra = c.Extra?.Contains(name, StringComparer.OrdinalIgnoreCase) == true;
            if (!extra && !u.EncounterOnly && w > 0 && ItemDrops.Droppable(type)) continue;
            pool.Add((u, c.RarityWeights && w > 0 ? w : Math.Max(0.0001, c.DefaultWeight)));
        }
        return pool;
    }

    /// <summary>One of them, made (null: none could be made).</summary>
    (Item Item, string Name)? MakeUnobtainable(Player player, Random rng, string? want = null)
    {
        var pool = UnobtainablePool(player);
        if (want is { Length: > 0 })
            pool = pool.Where(p => p.Unique.Name.Contains(want, StringComparison.OrdinalIgnoreCase)).ToList();
        var uniques = Game.UniquesFor(player);
        for (int tries = 0; tries < 5 && pool.Count > 0; tries++)
        {
            double at = rng.NextDouble() * pool.Sum(p => p.Weight);
            int i = 0;
            while (i < pool.Count - 1 && (at -= pool[i].Weight) > 0) i++;
            if (uniques.Create(pool[i].Unique, rng) is { } item) return (item, pool[i].Unique.Name);
            pool.RemoveAt(i);
        }
        return null;
    }

    static bool IsPinnacle(MonsterInfo monster) => monster.Rarity == MonsterRarity.Unique && (monster.VarietyId.Contains("AtlasBosses", StringComparison.Ordinal)
        || monster.VarietyId.Contains("AtlasInvaders", StringComparison.Ordinal) || monster.VarietyId.Contains("AtlasExiles", StringComparison.Ordinal)
        || monster.VarietyId.Contains("MavenBoss", StringComparison.Ordinal) || monster.VarietyId.Contains("Atziri", StringComparison.Ordinal)
        || monster.VarietyId.Contains("Uber", StringComparison.OrdinalIgnoreCase));

    void UnobtainableDrops(DropRoll d)
    {
        var c = _choices.Unobtainable ?? new();
        if (!c.Enabled || _unobtainable.Count == 0 && (c.Extra?.Length ?? 0) == 0) return;
        var player = d.Player;
        // In the normal drops: now and then a unique that dropped is one of these instead.
        if (c.InPlaceOf > 0)
            d.Changes.Add((drop, rng) => drop.Item.Rarity == ItemRarity.Unique && rng.Next(c.InPlaceOf) == 0
                && MakeUnobtainable(player, rng) is { } other && Game.ItemsFor(player).ByHash(other.Item.TypeHash) is { } type
                    ? [Announced(player, c, new ItemDrops.Drop(other.Item with { Identified = false }, type), other.Name)] : [drop]);
        if (c.MapsOnly && Game.MapRunOf(player) is not { Atlas: true }) return;
        int chance = IsRogueExile(d.Monster) ? c.RogueExiles
            : IsPinnacle(d.Monster) ? c.Pinnacles
            : d.Monster.Rarity == MonsterRarity.Unique ? c.MapBosses : 0;
        if (chance > 0 && Random.Shared.NextDouble() * chance < Math.Max(0, d.Quantity) && MakeUnobtainable(player, Random.Shared) is { } made)
        {
            d.Made.Add(made.Item);
            if (c.Announce) Tell(player, $"* An unobtainable unique dropped: {made.Name}.");
        }
    }

    ItemDrops.Drop Announced(Player player, UnobtainableChoices c, ItemDrops.Drop drop, string name)
    {
        if (c.Announce) Tell(player, $"* An unobtainable unique dropped in place of a unique: {name}.");
        return drop;
    }

    void UnobtainableChests(Player player, string kind, string id, List<ItemDrops.Drop> drops)
    {
        var c = _choices.Unobtainable ?? new();
        if (!c.Enabled || c.Strongboxes <= 0 || !kind.EndsWith("strongbox", StringComparison.Ordinal) || Random.Shared.Next(c.Strongboxes) != 0) return;
        if (c.MapsOnly && Game.MapRunOf(player) is not { Atlas: true }) return;
        if (MakeUnobtainable(player, Random.Shared) is not { } made || Game.ItemsFor(player).ByHash(made.Item.TypeHash) is not { } type) return;
        drops.Add(new ItemDrops.Drop(made.Item, type));
        if (c.Announce) Tell(player, $"* An unobtainable unique came from the strongbox: {made.Name}.");
    }
}
