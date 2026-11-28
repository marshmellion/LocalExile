using GameServer.Core;
using GameServer.Core.Protocol.Instance;

/// <summary>Ascendant jewels: a dropped jewel can come with implicits that allocate ascendancy passives (the game's own
/// "Allocates {0}" modifiers GrantedPassive to GrantedPassive5, whose value is the node). The server's jewel-allocates
/// part gives the node while the jewel sits in an allocated tree socket: its stats, buffs, auras and granted skill,
/// on any class. Groups: the normal ascendancies, the Phrecia ones (their own nodes, whatever the league), Reliquarian
/// and Luminary, and nodes the game no longer uses. A node that asks you to pick (a multiple-choice node) is never
/// rolled itself: one of its options is, and the jewel names that option.</summary>
/// <summary>One source's own drop: a new jewel with one, two or three nodes, each "1 in N" (0: never), from map tier
/// MinTier up (0: anywhere; above 0 only in maps).</summary>
sealed record AscendantSource(int One = 0, int Two = 0, int Three = 0, int MinTier = 0);

/// <summary>Where ascendant jewels drop of their own, each source with its chances for one, two and three nodes.</summary>
sealed record AscendantSources(AscendantSource? NormalMonsters = null, AscendantSource? MagicMonsters = null,
    AscendantSource? RareMonsters = null, AscendantSource? MapBosses = null, AscendantSource? PinnacleBosses = null,
    AscendantSource? UberBosses = null, AscendantSource? RogueExiles = null, AscendantSource? Izaro = null,
    AscendantSource? LabChests = null, AscendantSource? Strongboxes = null, AscendantSource? CampaignBosses = null,
    bool Quantity = true, bool LabChestsAtIzaro = true, int LabChestCount = 2, string[]? Bases = null);

sealed record AscendantJewelChoices(bool Enabled = true, double JewelChance = 0.02, double AbyssChance = 0.01,
    double ClusterChance = 0, double SecondChance = 0.15, double ThirdChance = 0.03, bool Normal = true, bool Phrecia = true,
    bool Special = true, bool Unused = false, bool NotablesOnly = true, bool Uniques = false, bool MapsOnly = false,
    bool Chests = true, bool Announce = true, string[]? Blacklist = null, Dictionary<string, double>? Weights = null,
    double NormalWeight = 1, double PhreciaWeight = 1, double SpecialWeight = 1, double UnusedWeight = 0.5,
    AscendantSources? Sources = null);

public sealed partial class ExcitingItemsMod
{
    sealed record AscendantNode(ushort Graph, string Name, string Ascendancy, string Group, bool Notable, string? ChoiceOf);

    static readonly string[] GrantedPassiveMods = ["GrantedPassive", "GrantedPassive2", "GrantedPassive3", "GrantedPassive4", "GrantedPassive5"];
    List<AscendantNode> _ascendantNodes = [];

    /// <summary>Every ascendancy node a jewel can allocate, from the client's PassiveSkills and Ascendancy tables.</summary>
    void LoadAscendantNodes(Action<string> log)
    {
        var nodes = new List<AscendantNode>();
        try
        {
            if (!ClientTables.Has("PassiveSkills") || !ClientTables.Has("Ascendancy")) return;
            var ascendancies = ClientTables.Table("Ascendancy").ToList();
            var rows = ClientTables.Table("PassiveSkills").ToList();
            // A multiple-choice node's options are linked to it in the tree: each option names its parent.
            var parents = rows.Where(r => !r.IsNull("AscendancyKey") && r.BoolOrNull("IsMultipleChoice") == true)
                .ToDictionary(r => (ushort)r.Int("PassiveSkillGraphId"), r => r.Text("Name") ?? "");
            var parentOf = new Dictionary<ushort, string>();
            if (PassiveGraphs.Available)
                foreach (string graphName in new[] { "normal", "phrecia" })
                    if (PassiveGraphs.Graph(graphName) is not null)
                        foreach (var (node, links) in PassiveGraphs.Links(graphName))
                            if (parents.TryGetValue(node, out string? parentName))
                                foreach (ushort linked in links) parentOf.TryAdd(linked, parentName);
            foreach (var r in rows)
            {
                if (r.IsNull("AscendancyKey")) continue;
                int graph = r.Int("PassiveSkillGraphId");
                string id = r.Text("Id") ?? "", name = r.Text("Name") ?? "";
                if (r.BoolOrNull("IsMultipleChoice") == true) continue;
                bool option = r.BoolOrNull("IsMultipleChoiceOption") == true;
                string? parent = option && graph is > 0 and <= ushort.MaxValue ? parentOf.GetValueOrDefault((ushort)graph) : null;
                if (graph is <= 0 or > ushort.MaxValue || r.BoolOrNull("IsAscendancyStartingNode") == true || r.BoolOrNull("IsJewelSocket") == true) continue;
                if (name.Length == 0 || name.StartsWith("<<", StringComparison.Ordinal)) continue;
                // Nodes that only give passive points, or nothing at all, allocate nothing a jewel could use.
                bool skill = r.IntOrNull("GrantedEffectsPerLevel") is int;
                bool anyStat = r.Ints("Stats").Length > 0;
                if (!anyStat && !skill) continue;
                if (name.Contains("Passive Point", StringComparison.OrdinalIgnoreCase)) continue;
                int key = r.Int("AscendancyKey");
                var asc = key >= 0 && key < ascendancies.Count ? ascendancies[key] : default;
                string ascName = key >= 0 && key < ascendancies.Count ? asc.Text("Name") ?? "" : "";
                bool custom = id.StartsWith("AscendancyCustom", StringComparison.Ordinal);
                if (custom && key >= 0 && key < ascendancies.Count && asc.Text("LeagueName") is { Length: > 0 } league) ascName = league;
                // The Phrecia Harbinger's companions are summoned by the Phrecia module alone (in its league).
                if (custom && skill && ascName == "Harbinger") continue;
                string group = name.Contains("[UNUSED]", StringComparison.OrdinalIgnoreCase) || name.Contains("[DNT]", StringComparison.OrdinalIgnoreCase) ? "unused"
                    : custom ? "phrecia" : ascName is "Reliquarian" or "Luminary" ? "special" : "normal";
                nodes.Add(new((ushort)graph, name, ascName, group, r.BoolOrNull("IsNotable") == true || r.BoolOrNull("IsKeystone") == true || option,
                    option ? parent : null));
            }
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or InvalidDataException or FormatException)
        {
            log($"ascendant jewels: the client's passive tables could not be read ({e.Message})");
        }
        _ascendantNodes = nodes;
        log($"ascendant jewels: {nodes.Count} ascendancy nodes ({string.Join(", ", nodes.GroupBy(n => n.Group).Select(g => $"{g.Key} {g.Count()}"))})");
    }

    IEnumerable<AscendantNode> AscendantPool(AscendantJewelChoices c)
    {
        var blacklist = new HashSet<string>(c.Blacklist ?? [], StringComparer.OrdinalIgnoreCase);
        return _ascendantNodes.Where(n => (!c.NotablesOnly || n.Notable)
            && n.Group switch { "normal" => c.Normal, "phrecia" => c.Phrecia, "special" => c.Special, _ => c.Unused }
            && !blacklist.Contains(n.Name) && !blacklist.Contains(n.Ascendancy) && AscendantWeight(c, n) > 0);
    }

    static double AscendantWeight(AscendantJewelChoices c, AscendantNode n)
    {
        double group = n.Group switch { "normal" => c.NormalWeight, "phrecia" => c.PhreciaWeight, "special" => c.SpecialWeight, _ => c.UnusedWeight };
        if (c.Weights is { } w)
        {
            if (w.FirstOrDefault(p => p.Key.Equals(n.Name, StringComparison.OrdinalIgnoreCase)) is { Key: not null } own) return Math.Max(0, own.Value);
            if (w.FirstOrDefault(p => p.Key.Equals(n.Ascendancy, StringComparison.OrdinalIgnoreCase)) is { Key: not null } asc) group *= Math.Max(0, asc.Value);
        }
        return Math.Max(0, group);
    }

    static string AscendantLabel(AscendantNode n) =>
        n.ChoiceOf is { } parent ? $"{n.Name} ({n.Ascendancy}, a choice of {parent})" : $"{n.Name} ({n.Ascendancy})";

    /// <summary>The jewel with count "Allocates" implicits (different nodes), the named node first when given; null if
    /// it is not a jewel this part touches or nothing can be rolled.</summary>
    (Item Item, List<AscendantNode> Nodes)? MakeAscendant(Player player, Item item, Random rng, int count, AscendantNode? first = null)
    {
        var c = _choices.AscendantJewels ?? new();
        var catalog = Game.ItemsFor(player);
        if (catalog.ByHash(item.TypeHash) is not { Class: "Jewel" or "AbyssJewel" }) return null;
        if (item.Mods.Any(m => catalog.Mods.ByHash(m.Hash) is { } def && GrantedPassiveMods.Contains(def.Id))) return null;
        var pool = AscendantPool(c).ToList();
        var picked = new List<AscendantNode>();
        if (first is not null) picked.Add(first);
        while (picked.Count < count && pool.Count > 0)
        {
            var open = pool.Where(n => !picked.Contains(n)).ToList();
            if (open.Count == 0) break;
            double at = rng.NextDouble() * open.Sum(n => AscendantWeight(c, n));
            picked.Add(open.First(n => (at -= AscendantWeight(c, n)) <= 0 || ReferenceEquals(n, open[^1])));
        }
        if (picked.Count == 0) return null;
        var mods = item.Mods.ToList();
        for (int i = 0; i < picked.Count && i < GrantedPassiveMods.Length; i++)
        {
            if (catalog.Mods.ById(GrantedPassiveMods[i]) is not { } def) return null;
            int place = mods.FindLastIndex(m => m.Section == ModSection.Implicit) + 1;
            mods.Insert(place, new ItemMod(ModSection.Implicit, def.Hash, [picked[i].Graph]));
        }
        return (item with { Mods = mods, RequiredLevel = null }, picked);
    }

    int AscendantCount(AscendantJewelChoices c, Random rng) =>
        rng.NextDouble() < c.SecondChance ? (rng.NextDouble() < (c.SecondChance > 0 ? c.ThirdChance / c.SecondChance : 0) ? 3 : 2) : 1;

    IEnumerable<ItemDrops.Drop> AscendantChange(Player player, ItemDrops.Drop drop, Random rng)
    {
        var c = _choices.AscendantJewels ?? new();
        double chance = drop.Type.Class == "AbyssJewel" ? c.AbyssChance
            : drop.Type.Class != "Jewel" ? 0
            : drop.Type.Name.Contains("Cluster Jewel", StringComparison.Ordinal) ? c.ClusterChance : c.JewelChance;
        if (chance <= 0 || rng.NextDouble() >= chance) return [drop];
        if (c.MapsOnly && Game.MapRunOf(player) is not { Atlas: true }) return [drop];
        if (drop.Item.Rarity == ItemRarity.Unique && !c.Uniques) return [drop];
        if (MakeAscendant(player, drop.Item, rng, AscendantCount(c, rng)) is not { } made) return [drop];
        if (c.Announce) Tell(player, $"* An ascendant jewel dropped: allocates {string.Join(", ", made.Nodes.Select(AscendantLabel))}.");
        return [drop with { Item = made.Item }];
    }

    void AscendantDrops(DropRoll d)
    {
        var c = _choices.AscendantJewels ?? new();
        if (!c.Enabled) return;
        var player = d.Player;
        // Jewels that drop anyway can come with nodes (the jewel and abyss jewel chances).
        if (!c.MapsOnly || Game.MapRunOf(player) is { Atlas: true })
            d.Changes.Add((drop, rng) => AscendantChange(player, drop, rng));
        // And each source drops new ones of its own.
        var s = c.Sources ?? new();
        var m = d.Monster;
        string id = m.VarietyId;
        bool map = Game.MapRunOf(player) is { Atlas: true };
        var (source, label) = id.Contains("Monsters/Labyrinth/LabyrinthBoss", StringComparison.Ordinal) ? (s.Izaro, "Izaro")
            : IsRogueExile(m) ? (s.RogueExiles, "a rogue exile")
            : id.Contains("Uber", StringComparison.OrdinalIgnoreCase) && m.Rarity == MonsterRarity.Unique ? (s.UberBosses, "an uber boss")
            : IsPinnacle(m) ? (s.PinnacleBosses, "a pinnacle boss")
            : m.Rarity == MonsterRarity.Unique ? (map ? (s.MapBosses, "a map boss") : (s.CampaignBosses, "a boss"))
            : m.Rarity switch
            {
                MonsterRarity.Rare => (s.RareMonsters, "a rare monster"),
                MonsterRarity.Magic => (s.MagicMonsters, "a magic monster"),
                _ => (s.NormalMonsters, "a monster"),
            };
        double quantity = s.Quantity ? Math.Max(0, d.Quantity) : 1;
        if (AscendantSourceRoll(player, source, quantity, m.Level) is { } jewel) Dropped(d, jewel, label);
        // The Labyrinth's treasure chests are not in the server yet: their roll comes with Izaro's death instead.
        if (label == "Izaro" && s.LabChestsAtIzaro)
            for (int i = 0; i < Math.Clamp(s.LabChestCount, 0, 10); i++)
                if (AscendantSourceRoll(player, s.LabChests, 1, m.Level) is { } chestJewel) Dropped(d, chestJewel, "a Labyrinth chest");
    }

    void Dropped(DropRoll d, (Item Item, List<AscendantNode> Nodes) made, string from)
    {
        d.Made.Add(made.Item);
        if ((_choices.AscendantJewels ?? new()).Announce)
            Tell(d.Player, $"* An ascendant jewel dropped from {from}: allocates {string.Join(", ", made.Nodes.Select(AscendantLabel))}.");
    }

    /// <summary>A source's roll: three nodes, else two, else one, each at its own "1 in N"; null when none hits or the
    /// map tier is below the source's.</summary>
    (Item Item, List<AscendantNode> Nodes)? AscendantSourceRoll(Player player, AscendantSource? source, double quantity, int level)
    {
        if (source is null || source.One <= 0 && source.Two <= 0 && source.Three <= 0) return null;
        if (source.MinTier > 0 && (Game.MapRunOf(player) is not { Atlas: true } run || run.Area.Tier < source.MinTier)) return null;
        bool Hit(int n) => n > 0 && Random.Shared.NextDouble() * n < quantity;
        int count = Hit(source.Three) ? 3 : Hit(source.Two) ? 2 : Hit(source.One) ? 1 : 0;
        if (count == 0) return null;
        var s = (_choices.AscendantJewels ?? new()).Sources ?? new();
        string[] bases = s.Bases is { Length: > 0 } own ? own : ["Cobalt Jewel", "Crimson Jewel", "Viridian Jewel"];
        var catalog = Game.ItemsFor(player);
        string want = bases[Random.Shared.Next(bases.Length)];
        if (catalog.All.FirstOrDefault(t => t.Name.Equals(want, StringComparison.OrdinalIgnoreCase) && t.Supported) is not { } type) return null;
        var jewel = ItemCatalog.Create(type) with { ItemLevel = (byte)Math.Clamp(level, 1, 100) };
        return MakeAscendant(player, jewel, Random.Shared, count);
    }

    /// <summary>Chests and strongboxes too (the private build's hand-over).</summary>
    void AscendantChests(Player player, string kind, string id, List<ItemDrops.Drop> drops)
    {
        var c = _choices.AscendantJewels ?? new();
        if (!c.Enabled) return;
        if (c.Chests && (!c.MapsOnly || Game.MapRunOf(player) is { Atlas: true }))
        {
            var changed = drops.SelectMany(drop => AscendantChange(player, drop, Random.Shared)).ToList();
            drops.Clear();
            drops.AddRange(changed);
        }
        var s = c.Sources ?? new();
        // A real Labyrinth chest (once the server has them), or a strongbox.
        var (source, label) = id.Contains("Labyrinth", StringComparison.OrdinalIgnoreCase) ? (s.LabChests, "a Labyrinth chest")
            : kind.EndsWith("strongbox", StringComparison.Ordinal) ? (s.Strongboxes, "a strongbox") : (null, "");
        if (AscendantSourceRoll(player, source, 1, Game.MapRunOf(player)?.Area.Level ?? 68) is not { } made) return;
        if (Game.ItemsFor(player).ByHash(made.Item.TypeHash) is { } type) drops.Add(new ItemDrops.Drop(made.Item, type));
        if (c.Announce) Tell(player, $"* An ascendant jewel dropped from {label}: allocates {string.Join(", ", made.Nodes.Select(AscendantLabel))}.");
    }

    /// <summary>"/exciting ascendant [list [filter] | node or ascendancy name] [count]": a test jewel, or what can roll.</summary>
    async Task AscendantCommand(Player player, string[] args)
    {
        var who = Program.PlayerOf(player);
        var c = _choices.AscendantJewels ?? new();
        if (args.Length > 1 && args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            string filter = string.Join(' ', args.Skip(2));
            var pool = AscendantPool(c).Where(n => filter.Length == 0 || n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || n.Ascendancy.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            await who.Tell($"Exciting Items: {pool.Count} nodes can roll ({string.Join(", ", pool.GroupBy(n => n.Group).Select(g => $"{g.Key} {g.Count()}"))}).");
            foreach (var g in pool.GroupBy(n => n.Ascendancy).Take(filter.Length > 0 ? 40 : 0))
                await who.Tell($"  {g.Key}: {string.Join(", ", g.Select(n => n.ChoiceOf is { } p ? $"{n.Name} [{p}]" : n.Name))}");
            return;
        }
        int count = 1;
        var words = args.Skip(1).ToList();
        if (words.Count > 0 && int.TryParse(words[^1], out int n)) { count = Math.Clamp(n, 1, 3); words.RemoveAt(words.Count - 1); }
        string want = string.Join(' ', words);
        AscendantNode? first = null;
        if (want.Length > 0)
        {
            // Any node by name (the settings' groups do not limit a test jewel), else a random one of that ascendancy.
            first = _ascendantNodes.FirstOrDefault(x => x.Name.Equals(want, StringComparison.OrdinalIgnoreCase))
                ?? _ascendantNodes.FirstOrDefault(x => x.Name.Contains(want, StringComparison.OrdinalIgnoreCase));
            if (first is null && _ascendantNodes.Where(x => x.Ascendancy.Equals(want, StringComparison.OrdinalIgnoreCase) && x.Notable).ToList() is { Count: > 0 } of)
                first = of[Random.Shared.Next(of.Count)];
            if (first is null) { await who.Tell($"Exciting Items: no ascendancy node or ascendancy like \"{want}\"."); return; }
        }
        var catalog = Game.ItemsFor(player);
        string[] bases = ["Cobalt Jewel", "Crimson Jewel", "Viridian Jewel"];
        if (catalog.All.FirstOrDefault(t => t.Name == bases[Random.Shared.Next(bases.Length)] && t.Supported) is not { } type) { await who.Tell("Exciting Items: the client data has no jewel base."); return; }
        var jewel = ItemCatalog.Create(type);
        if (MakeAscendant(player, jewel, Random.Shared, count, first) is not { } made) { await who.Tell("Exciting Items: no ascendancy node can roll with these settings."); return; }
        await GiveItem(player, made.Item, type.Name);
        await who.Tell($"Exciting Items: a {type.Name} that allocates {string.Join(", ", made.Nodes.Select(AscendantLabel))}.");
    }
}
