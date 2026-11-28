using GameServer.Core.Protocol.Instance;

/// <summary>Legacy currencies: drops for currencies the game no longer drops (Harbinger's and Scourge's, shards, Orb of
/// Dominance), each with its own chance from monsters and from strongboxes, and uses for the ones the server has none
/// for: Orb of Horizons, Harbinger's Orb, Tainted Mythic Orb, Tainted Divine Teardrop and Orb of Dominance (private
/// build: they come through the CurrencyOnItem hand-over). The server's own uses stay for the rest (Orb of Binding,
/// Ancient Orb, the Tainted orbs of sockets, quality and rerolls).</summary>
sealed record LegacyCurrencyChoices(bool Enabled = true, Dictionary<string, int>? Monsters = null, Dictionary<string, int>? Strongboxes = null,
    bool Uses = true, int HarbingerMaxTier = 16, bool HorizonsKeepsMods = true, int TeardropDowngrade = 25, bool MythicSameClass = true,
    bool DominanceRemoves = true, bool Announce = false);

sealed class LegacyCurrencies(Func<LegacyCurrencyChoices> choices, Action<Player, string> tell)
{
    public const string Horizons = "Metadata/Items/Currency/CurrencyRerollMapType", HarbingersOrb = "Metadata/Items/Currency/CurrencyUpgradeMapTier",
        TaintedMythic = "Metadata/Items/Currency/CurrencyHellscapeUpgradeToUnique", TaintedTeardrop = "Metadata/Items/Currency/CurrencyHellscapeUpgradeModTier",
        Dominance = "Metadata/Items/Currency/CurrencyUpgradeInfluenceMod";

    /// <summary>The defaults: 1 in this many monsters (magic 3 times as likely, rare 6, unique 10), and strongboxes.</summary>
    public static readonly Dictionary<string, int> DefaultMonsters = new()
    {
        ["Orb of Binding"] = 4000, ["Ancient Orb"] = 15000, ["Orb of Horizons"] = 4000, ["Harbinger's Orb"] = 8000,
        ["Exalted Shard"] = 8000, ["Annulment Shard"] = 8000, ["Mirror Shard"] = 400000,
        ["Tainted Chaos Orb"] = 3000, ["Tainted Exalted Orb"] = 20000, ["Tainted Mythic Orb"] = 50000, ["Tainted Divine Teardrop"] = 20000,
        ["Tainted Jeweller's Orb"] = 3000, ["Tainted Orb of Fusing"] = 4000, ["Tainted Chromatic Orb"] = 3000,
        ["Tainted Armourer's Scrap"] = 3000, ["Tainted Blacksmith's Whetstone"] = 3000, ["Orb of Dominance"] = 60000,
    };
    public static readonly Dictionary<string, int> DefaultStrongboxes = new()
    {
        ["Orb of Binding"] = 12, ["Ancient Orb"] = 60, ["Orb of Horizons"] = 12, ["Harbinger's Orb"] = 30, ["Exalted Shard"] = 25,
        ["Annulment Shard"] = 25, ["Mirror Shard"] = 2000, ["Tainted Chaos Orb"] = 10, ["Tainted Exalted Orb"] = 60, ["Tainted Mythic Orb"] = 200,
        ["Tainted Divine Teardrop"] = 60, ["Tainted Jeweller's Orb"] = 10, ["Tainted Orb of Fusing"] = 12, ["Tainted Chromatic Orb"] = 10,
        ["Tainted Armourer's Scrap"] = 10, ["Tainted Blacksmith's Whetstone"] = 10, ["Orb of Dominance"] = 250,
    };

    public void Drops(DropRoll d)
    {
        var c = choices();
        if (!c.Enabled) return;
        double weight = d.Monster.Rarity switch { MonsterRarity.Magic => 3, MonsterRarity.Rare => 6, MonsterRarity.Unique => 10, _ => 1 };
        foreach (var (name, chance) in c.Monsters ?? DefaultMonsters)
            if (chance > 0 && Random.Shared.NextDouble() * chance < weight * Math.Max(0, d.Quantity))
            {
                d.Stacks.Add((name, 1));
                if (c.Announce) tell(d.Player, $"* A legacy currency dropped: {name}.");
            }
    }

    public void Chests(Player player, string kind, string id, List<ItemDrops.Drop> drops)
    {
        var c = choices();
        if (!c.Enabled || !kind.EndsWith("strongbox", StringComparison.Ordinal)) return;
        var catalog = Game.ItemsFor(player);
        foreach (var (name, chance) in c.Strongboxes ?? DefaultStrongboxes)
            if (chance > 0 && Random.Shared.Next(chance) == 0
                && catalog.All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.Supported) is { } type)
                drops.Add(new ItemDrops.Drop(ItemCatalog.Create(type) with { StackSize = 1 }, type));
    }

    /// <summary>A legacy currency used on an item: the new item, the target itself to refuse, or null when it is not one
    /// of these (the server's own use then runs).</summary>
    public Item? Use(Player player, Item target, Item used, ItemCatalog.BaseType currency, Random random)
    {
        var c = choices();
        if (currency.Id is not (Horizons or HarbingersOrb or TaintedMythic or TaintedTeardrop or Dominance)) return null;
        if (!c.Enabled || !c.Uses) { tell(player, $"{currency.Name}: turned off in Exciting Items' settings."); return target; }
        if (target.Foreseeing) return target;
        var catalog = Game.ItemsFor(player);
        if (catalog.ByHash(target.TypeHash) is not { } type) return target;
        return currency.Id switch
        {
            Horizons or HarbingersOrb => MapOrb(player, target, type, currency, random, c),
            TaintedMythic => Mythic(player, target, type, random, c),
            TaintedTeardrop => Teardrop(player, target, random, c),
            _ => DominanceUse(player, target, random, c),
        };
    }

    Item MapOrb(Player player, Item target, ItemCatalog.BaseType type, ItemCatalog.BaseType currency, Random random, LegacyCurrencyChoices c)
    {
        var devices = Game.MapDevices;
        if (type.Class != "Map" || devices.AreaOf(type.Hash) is not { } area || target.Rarity == ItemRarity.Unique)
        { tell(player, $"{currency.Name}: use it on a map that is not unique."); return target; }
        bool up = currency.Id == HarbingersOrb;
        int tier = up ? area.Tier + 1 : area.Tier;
        if (up && tier > Math.Clamp(c.HarbingerMaxTier, 1, 17)) { tell(player, "Harbinger's Orb: that map is at the highest tier already."); return target; }
        var options = Game.ItemsFor(player).All.Where(t => t.Class == "Map" && t.Supported && t.Hash != type.Hash
            && devices.AreaOf(t.Hash) is { } a && a.Tier == tier).ToList();
        if (options.Count == 0) { tell(player, $"{currency.Name}: no map of tier {tier} to turn it into."); return target; }
        var next = options[random.Next(options.Count)];
        var made = target with { TypeHash = next.Hash, VisualIdentity = next.VisualIdentity, Components = next.Components, RequiredLevel = null };
        if (!up && !c.HorizonsKeepsMods) made = made with { Mods = [.. made.Mods.Where(m => m.Section != ModSection.Explicit)] };
        tell(player, $"{currency.Name}: {next.Name} (tier {tier}).");
        return made;
    }

    Item Mythic(Player player, Item target, ItemCatalog.BaseType type, Random random, LegacyCurrencyChoices c)
    {
        if (!target.Corrupted || target.Rarity != ItemRarity.Rare) { tell(player, "Tainted Mythic Orb: use it on a corrupted rare item."); return target; }
        var catalog = Game.ItemsFor(player);
        var uniques = Game.UniquesFor(player);
        var pool = uniques.All.Where(u => catalog.Find(u.BaseTypeId) is { Supported: true } b
            && (c.MythicSameClass ? b.Class == type.Class : b.Hash == type.Hash)).ToList();
        for (int i = 0; i < 20 && pool.Count > 0; i++)
        {
            var u = pool[random.Next(pool.Count)];
            if (uniques.Create(u, random) is { } made)
            {
                tell(player, $"Tainted Mythic Orb: {u.Name}.");
                return made with { Corrupted = true, ItemLevel = target.ItemLevel };
            }
            pool.Remove(u);
        }
        tell(player, "Tainted Mythic Orb: no unique of that kind of item.");
        return target;
    }

    Item Teardrop(Player player, Item target, Random random, LegacyCurrencyChoices c)
    {
        if (!target.Corrupted || target.Rarity is not (ItemRarity.Magic or ItemRarity.Rare)) { tell(player, "Tainted Divine Teardrop: use it on a corrupted magic or rare item."); return target; }
        var mods = Game.ItemsFor(player).Mods;
        var list = target.Mods.ToList();
        var explicits = list.Select((m, i) => (m, i)).Where(p => p.m.Section == ModSection.Explicit && mods.ByHash(p.m.Hash) is { Stats.Count: > 0 }).ToList();
        if (explicits.Count == 0) { tell(player, "Tainted Divine Teardrop: the item has no modifier to change."); return target; }
        var (mod, at) = explicits[random.Next(explicits.Count)];
        var def = mods.ByHash(mod.Hash)!;
        int dir = random.Next(100) < Math.Clamp(c.TeardropDowngrade, 0, 100) ? -1 : 1;
        if (ExcitingItemsMod.TierNeighbour(mods, def, dir) is not { } shifted) { tell(player, "Tainted Divine Teardrop: that modifier has no other tier; nothing changed."); return target with { Quality = target.Quality }; }
        list[at] = shifted.Roll(ModSection.Explicit, random);
        tell(player, $"Tainted Divine Teardrop: a modifier went {(dir > 0 ? "up" : "down")} a tier.");
        return target with { Mods = list, RequiredLevel = null };
    }

    Item DominanceUse(Player player, Item target, Random random, LegacyCurrencyChoices c)
    {
        var mods = Game.ItemsFor(player).Mods;
        ModCatalog.Mod? Elevated(ModCatalog.Mod m) => m.Id.Contains("Maven", StringComparison.Ordinal) ? null
            : mods.ById(m.Id + "Maven") ?? mods.ById(m.Id.TrimEnd('_') + "Maven") ?? mods.ById(m.Id.TrimEnd('_') + "Maven_");
        var list = target.Mods.ToList();
        var influenced = list.Select((m, i) => (m, i, def: mods.ByHash(m.Hash))).Where(p => p.m.Section == ModSection.Explicit && p.def is not null
            && p.def.Id.Contains("Uber", StringComparison.Ordinal) && !p.def.Id.Contains("Maven", StringComparison.Ordinal)).ToList();
        var can = influenced.Where(p => Elevated(p.def!) is not null).ToList();
        if (target.Influences == ItemInfluence.None || can.Count == 0 || (c.DominanceRemoves && influenced.Count < 2))
        { tell(player, "Orb of Dominance: use it on an item with at least two influenced modifiers, one of which can be elevated."); return target; }
        var pick = can[random.Next(can.Count)];
        list[pick.i] = Elevated(pick.def!)!.Roll(ModSection.Explicit, random);
        if (c.DominanceRemoves)
        {
            var others = influenced.Where(p => p.i != pick.i).ToList();
            list.RemoveAt(others[random.Next(others.Count)].i);
        }
        tell(player, "Orb of Dominance: an influenced modifier was elevated" + (c.DominanceRemoves ? ", another was removed." : "."));
        return target with { Mods = list, RequiredLevel = null };
    }
}
