using GameServer.Core.Protocol.Instance;

/// <summary>Idols (Legacy of Phrecia) from more places, with chances of their own: monsters by rarity, bosses, Rogue
/// Exiles and strongboxes, in maps only or everywhere. Now and then an Idol has a scarab's effect as an implicit (the
/// scarab's own MapFragmentMods modifiers), which adds to the map as the Idol's other modifiers do, beside any scarabs in
/// the device. The server's Idols module (Legacy/Idols) gives the Idol inventory and their effect on maps; it is on in a
/// league that lists it (data/league-modules.json can add it to Standard).</summary>
sealed record IdolChoices(bool Enabled = true, bool MapsOnly = true, int Normal = 0, int Magic = 0, int Rare = 600, int Bosses = 40,
    int RogueExiles = 15, int Strongboxes = 25, int RareShare = 25, int UniqueShare = 50, int ScarabImplicit = 100, int ScarabImplicitMost = 1,
    bool Unidentified = true, bool Announce = true, int ExcitingImplicit = 60, int ExcitingImplicitMost = 1, string[]? ExcitingBlacklist = null,
    bool ExcitingWeights = true);

public sealed partial class ExcitingItemsMod
{
    const string IdolClass = "AtlasRelic";
    static readonly (string Id, int Weight)[] IdolBases =
    [
        ("Metadata/Items/Relics/AtlasRelic1x1", 40), ("Metadata/Items/Relics/AtlasRelic1x2", 20), ("Metadata/Items/Relics/AtlasRelic2x1", 20),
        ("Metadata/Items/Relics/AtlasRelic1x3", 8), ("Metadata/Items/Relics/AtlasRelic3x1", 8), ("Metadata/Items/Relics/AtlasRelic2x2", 4),
    ];
    List<(string Scarab, ModCatalog.Mod[] Mods)>? _scarabEffects;
    List<(string Stat, string Mod, string Text, int Weight)> _idolImplicits = [];

    void LoadIdolImplicits(IModContext mod)
    {
        string file = Path.Combine(mod.Folder, "idolimplicits.json");
        if (!File.Exists(file)) return;
        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
            _idolImplicits = [.. (root["implicits"] as System.Text.Json.Nodes.JsonArray ?? []).OfType<System.Text.Json.Nodes.JsonObject>()
                .Select(n => ((string)n["stat"]!, (string)n["mod"]!, (string?)n["text"] ?? "", (int?)n["weight"] ?? 250))];
            mod.Log($"{_idolImplicits.Count} exciting Idol implicits");
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or InvalidOperationException) { mod.Log($"idolimplicits.json not read: {e.Message}"); }
    }

    /// <summary>Every scarab's map modifiers (MapFragmentMods rows of the Metadata/Items/Scarabs/ bases).</summary>
    List<(string Scarab, ModCatalog.Mod[] Mods)> ScarabEffects(ItemCatalog catalog)
    {
        if (_scarabEffects is not null) return _scarabEffects;
        var list = new List<(string, ModCatalog.Mod[])>();
        if (GameServer.Core.ClientTables.Has("MapFragmentMods"))
            foreach (var row in GameServer.Core.ClientTables.Table("MapFragmentMods"))
                if (row.IntOrNull("BaseItemTypesKey") is int baseRow && catalog.Row(baseRow) is { } scarab
                    && scarab.Id.StartsWith("Metadata/Items/Scarabs/", StringComparison.Ordinal))
                {
                    var mods = row.Ints("ModsKeys").Select(catalog.Mods.Row).OfType<ModCatalog.Mod>().ToArray();
                    if (mods.Length > 0) list.Add((scarab.Name, mods));
                }
        _log($"{list.Count} scarab effects for Idol implicits");
        return _scarabEffects = list;
    }

    /// <summary>A new Idol at this level: a unique one in UniqueShare, else a base the level has reached, rare RareShare %
    /// of the time, magic otherwise; now and then with a scarab's effect as an implicit.</summary>
    Item? MakeIdol(Player player, int level, Random rng, out string label)
    {
        var c = _choices.Idols ?? new();
        var catalog = Game.ItemsFor(player);
        label = "Idol";
        Item? idol = null;
        if (c.UniqueShare > 0 && rng.Next(c.UniqueShare) == 0)
        {
            var uniques = Game.UniquesFor(player);
            var pool = uniques.All.Where(u => catalog.Find(u.BaseTypeId) is { Supported: true, Class: IdolClass } b && b.DropLevel <= level).ToList();
            if (pool.Count > 0 && uniques.Create(pool[rng.Next(pool.Count)], rng) is { } unique) { idol = unique; label = "unique Idol"; }
        }
        if (idol is null)
        {
            var bases = IdolBases.Select(b => (Type: catalog.Find(b.Id), b.Weight)).Where(b => b.Type is { Supported: true } t && t.DropLevel <= level).ToList();
            int total = bases.Sum(b => b.Weight);
            if (total == 0) return null;
            int pick = rng.Next(total);
            var chosen = bases.First(b => (pick -= b.Weight) < 0).Type!;
            var item = ItemCatalog.Create(chosen, random: rng) with { ItemLevel = (byte)Math.Clamp(level, 1, 100) };
            bool rare = rng.Next(100) < Math.Clamp(c.RareShare, 0, 100);
            idol = Game.RollsFor(player).Roll(item, rare ? ItemRarity.Rare : ItemRarity.Magic, rng);
            label = rare ? "rare Idol" : "magic Idol";
        }
        if (c.ScarabImplicit > 0 && rng.Next(c.ScarabImplicit) == 0 && ScarabEffects(catalog) is { Count: > 0 } effects)
        {
            var mods = idol.Mods.ToList();
            var names = new List<string>();
            for (int i = 0; i < Math.Max(1, c.ScarabImplicitMost); i++)
            {
                var (scarab, rows) = effects[rng.Next(effects.Count)];
                foreach (var row in rows) mods.Insert(0, row.Roll(ModSection.Implicit, rng));
                names.Add(scarab);
            }
            idol = idol with { Mods = mods };
            label += $" with {string.Join(" and ", names)}'s effect";
        }
        // Exciting: a map modifier that changes maps in a fun way (more Rogue Exiles, Strongboxes with Rare Items, Shrines,
        // Harbingers, Essences ...) as an implicit, and the Exciting mark.
        if (c.ExcitingImplicit > 0 && rng.Next(c.ExcitingImplicit) == 0)
        {
            var options = _idolImplicits.Where(x => !(c.ExcitingBlacklist ?? []).Any(b => x.Stat.Contains(b, StringComparison.OrdinalIgnoreCase) || x.Text.Contains(b, StringComparison.OrdinalIgnoreCase)))
                .Select(x => (Mod: catalog.Mods.ById(x.Mod), Weight: c.ExcitingWeights ? Math.Max(1, x.Weight) : 1)).Where(x => x.Mod is not null).ToList();
            var mods = idol.Mods.ToList();
            for (int i = 0; i < Math.Max(1, c.ExcitingImplicitMost) && options.Count > 0; i++)
            {
                int at = rng.Next(options.Sum(x => x.Weight));
                var pick = options.First(x => (at -= x.Weight) < 0);
                options.Remove(pick);
                mods.Insert(0, pick.Mod!.Roll(ModSection.Implicit, rng));
            }
            if (mods.Count > idol.Mods.Count)
            {
                idol = idol with { Mods = mods };
                if (_choices.Mark) idol = Marked(catalog, idol);
                label = "exciting " + label;
            }
        }
        return c.Unidentified ? idol with { Identified = false } : idol;
    }

    static bool IsRogueExile(MonsterInfo monster) => monster.VarietyId.Contains("/Exiles/", StringComparison.OrdinalIgnoreCase)
        || monster.VarietyId.Contains("RogueExile", StringComparison.OrdinalIgnoreCase);

    void IdolDrops(DropRoll d)
    {
        var c = _choices.Idols ?? new();
        if (!c.Enabled || d.Cancel) return;
        if (c.MapsOnly && Game.MapRunOf(d.Player) is not { Atlas: true }) return;
        int chance = IsRogueExile(d.Monster) && c.RogueExiles > 0 ? c.RogueExiles : d.Monster.Rarity switch
        {
            MonsterRarity.Unique => c.Bosses, MonsterRarity.Rare => c.Rare, MonsterRarity.Magic => c.Magic, _ => c.Normal,
        };
        if (chance <= 0 || Random.Shared.NextDouble() * chance >= Math.Max(0, d.Quantity)) return;
        if (MakeIdol(d.Player, d.Monster.Level, Random.Shared, out string label) is not { } idol) return;
        d.Made.Add(idol);
        if (c.Announce) Tell(d.Player, $"* An {label} dropped.");
    }

    void IdolChests(Player player, string kind, string id, List<ItemDrops.Drop> drops)
    {
        var c = _choices.Idols ?? new();
        if (!c.Enabled || c.Strongboxes <= 0 || !kind.EndsWith("strongbox", StringComparison.Ordinal) || Random.Shared.Next(c.Strongboxes) != 0) return;
        if (c.MapsOnly && Game.MapRunOf(player) is not { Atlas: true }) return;
        int level = Game.MapRunOf(player)?.Area.Level ?? 68;
        if (MakeIdol(player, level, Random.Shared, out string label) is not { } idol || Game.ItemsFor(player).ByHash(idol.TypeHash) is not { } type) return;
        drops.Add(new ItemDrops.Drop(idol, type));
        if (c.Announce) Tell(player, $"* An {label} came from the strongbox.");
    }
}
