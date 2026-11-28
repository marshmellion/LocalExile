using GameServer.Core.Protocol.Instance;

/// <summary>The Defiled Vaal Orb: corrupts a corrupted item a second time (private build). A gem can go a second level
/// over its cap (22 for a gem of 20) or get its quality moved again; gear can get a second corrupted implicit or one more
/// Vaal outcome; a map, jewel or flask that dropped corrupted gets a Vaal Orb's outcome. It drops in Vaal side areas
/// (monsters and the vessel at the end) and, while those do not work, in corrupted maps and from corrupted strongboxes.</summary>
sealed record DefiledChoices(bool Enabled = true, bool AnyItem = false, int SideAreaMonsters = 400, int Vessel = 4,
    int CorruptedMapMonsters = 2000, int CorruptedStrongbox = 10, int LevelsOver = 2, int QualityCap = 23,
    int Nothing = 25, int Level = 25, int Quality = 25, int VaalGem = 25,
    int SecondImplicit = 34, int VaalOutcome = 33, int GearNothing = 33, int MostCorruptedImplicits = 2);

public sealed partial class ExcitingItemsMod
{
    const string DefiledId = "Metadata/Items/Currency/LocalExileDefiledVaalOrb";
    const int CorruptedGeneration = 5;

    static bool InVaalSideArea(Player player) => player.Layout.Id.Contains("SideArea", StringComparison.OrdinalIgnoreCase);

    void DefiledDrops(DropRoll d)
    {
        var c = _choices.DefiledOrb ?? new();
        if (!c.Enabled || Game.ItemsFor(d.Player).Find(DefiledId) is not { Supported: true }) return;
        int chance = InVaalSideArea(d.Player) ? c.SideAreaMonsters : Game.MapModsOf(d.Player) is { Corrupted: true } ? c.CorruptedMapMonsters : 0;
        if (chance <= 0) return;
        double weight = d.Monster.Rarity switch { MonsterRarity.Magic => 3, MonsterRarity.Rare => 6, MonsterRarity.Unique => 10, _ => 1 };
        if (Random.Shared.NextDouble() * chance < weight * Math.Max(0, d.Quantity))
        {
            d.Stacks.Add((DefiledId, 1));
            if (_choices.Announce) Tell(d.Player, "* A Defiled Vaal Orb dropped.");
        }
    }

    /// <summary>From the private hand-over for chests and strongboxes: the vessel at the end of a Vaal side area (any chest
    /// there) and corrupted strongboxes.</summary>
    void DefiledChest(Player player, string kind, string id, List<ItemDrops.Drop> drops)
    {
        var c = _choices.DefiledOrb ?? new();
        if (!c.Enabled || Game.ItemsFor(player).Find(DefiledId) is not { Supported: true } orb) return;
        int chance = kind == "corrupted strongbox" ? c.CorruptedStrongbox : kind == "chest" && InVaalSideArea(player) ? c.Vessel : 0;
        if (chance <= 0 || Random.Shared.Next(chance) != 0) return;
        drops.Add(new ItemDrops.Drop(ItemCatalog.Create(orb) with { StackSize = 1 }, orb));
        if (_choices.Announce) Tell(player, "* A Defiled Vaal Orb dropped.");
    }

    Item? DefiledUse(Player player, Item target, Random random)
    {
        var c = _choices.DefiledOrb ?? new();
        if (target.Foreseeing) return target;
        if (!c.Enabled) { Tell(player, "Defiled Vaal Orb: turned off in Exciting Items' settings."); return target; }
        if (!target.Corrupted) { Tell(player, "Defiled Vaal Orb: it works only on corrupted items (use a Vaal Orb first)."); return target; }
        if (target.Mirrored && !c.AnyItem) { Tell(player, "Defiled Vaal Orb: not on mirrored items."); return target; }
        var catalog = Game.ItemsFor(player);
        if (catalog.ByHash(target.TypeHash) is not { Supported: true } type) return target;
        if (target.Components.Contains("SkillGem")) return DefiledGem(player, catalog, type, target, c, random);
        Item? Vaal() => VaalCorruption.Apply(catalog, Game.RollsFor(player), Game.UniquesFor(player), Game.MapDevices,
            Game.DropsFor(player).UniqueWeight, target with { Corrupted = false }, random) is { } done ? done with { Corrupted = true } : null;
        bool gear = type.Components.Contains("Mods") && !CurrencyEffects.IsMap(target, type) && type.Class is not ("Jewel" or "AbyssJewel")
            && !target.Components.Contains("Flask") && (type.CanBeMirrored || type.CanBeCorrupted);
        if (!gear)
        {
            if (Vaal() is not { } other) { Tell(player, "Defiled Vaal Orb: nothing can corrupt that item further."); return target; }
            Tell(player, "Defiled Vaal Orb: corrupted again.");
            return other;
        }
        var outcomes = new List<(string Name, int Weight)> { ("implicit", c.SecondImplicit), ("vaal", c.VaalOutcome), ("nothing", c.GearNothing) };
        var left = outcomes.Where(o => o.Weight > 0).ToList();
        while (left.Count > 0)
        {
            int at = random.Next(left.Sum(o => o.Weight));
            var o = left.First(x => (at -= x.Weight) < 0);
            left.Remove(o);
            switch (o.Name)
            {
                case "nothing":
                    Tell(player, "Defiled Vaal Orb: the corruption held; nothing changed.");
                    return target with { RequiredLevel = target.RequiredLevel };
                case "vaal":
                    if (Vaal() is { } vaal) { Tell(player, "Defiled Vaal Orb: a Vaal Orb's outcome."); return vaal; }
                    break;
                case "implicit":
                    int have = target.Mods.Count(m => m.Section == ModSection.Implicit && catalog.Mods.ByHash(m.Hash)?.GenerationType == CorruptedGeneration);
                    if (have >= Math.Max(1, c.MostCorruptedImplicits)) break;
                    var pool = Game.RollsFor(player).CorruptedImplicitPool(type, target.ItemLevel)
                        .Where(p => p.Weight > 0 && target.Mods.All(m => catalog.Mods.ByHash(m.Hash) is not { } d || !d.Families.Intersect(p.Mod.Families).Any())).ToList();
                    if (pool.Count == 0) break;
                    int w = random.Next(pool.Sum(p => p.Weight));
                    var mod = pool.First(p => (w -= p.Weight) < 0).Mod.Roll(ModSection.Implicit, random);
                    var mods = target.Mods.ToList();
                    int after = mods.FindLastIndex(m => m.Section == ModSection.Implicit);
                    mods.Insert(after + 1, mod);
                    Tell(player, $"Defiled Vaal Orb: a second corrupted implicit, \"{catalog.Mods.Describe(mod)}\".");
                    return target with { Mods = mods, RequiredLevel = null };
            }
        }
        Tell(player, "Defiled Vaal Orb: nothing can corrupt that item further.");
        return target;
    }

    /// <summary>A corrupted gem's second corruption: no change, a level up or down (up to the cap plus LevelsOver; above
    /// the cap the server keeps -n as n levels over it), quality up or down by 1 to 10 (up to QualityCap), or its Vaal gem.</summary>
    Item? DefiledGem(Player player, ItemCatalog catalog, ItemCatalog.BaseType type, Item gemItem, DefiledChoices c, Random random)
    {
        if (catalog.Gem(type) is not { MaxLevel: > 0 } gem) return gemItem;
        int level = gem.Level(gemItem.GemExperience);
        var outcomes = new List<(string Name, int Weight)> { ("nothing", c.Nothing), ("level", c.Level), ("quality", c.Quality), ("vaal", c.VaalGem) };
        var left = outcomes.Where(o => o.Weight > 0).ToList();
        while (left.Count > 0)
        {
            int at = random.Next(left.Sum(o => o.Weight));
            var o = left.First(x => (at -= x.Weight) < 0);
            left.Remove(o);
            switch (o.Name)
            {
                case "nothing":
                    Tell(player, "Defiled Vaal Orb: the corruption held; nothing changed.");
                    return gemItem with { Quality = gemItem.Quality };
                case "level":
                    int next = Math.Clamp(level + (random.Next(2) == 0 ? -1 : 1), 1, gem.MaxLevel + Math.Max(0, c.LevelsOver));
                    if (next == level) break;
                    Tell(player, $"Defiled Vaal Orb: level {level} → {next}.");
                    return gemItem with { GemExperience = next > gem.MaxLevel ? gem.MaxLevel - next : gem.ExperienceFor(next) };
                case "quality":
                    int quality = Math.Clamp(gemItem.Quality + random.Next(1, 11) * (random.Next(2) == 0 ? -1 : 1), 0, Math.Max(0, c.QualityCap));
                    if (quality == gemItem.Quality) break;
                    Tell(player, $"Defiled Vaal Orb: quality {gemItem.Quality}% → {quality}%.");
                    return gemItem with { Quality = (byte)quality };
                case "vaal":
                    // A gem that is not a Vaal gem yet: the Vaal Orb's own gem outcome does it, so it is asked until it
                    // gives the Vaal gem (or a few times).
                    if (gem.VaalBaseRow is null) break;
                    for (int i = 0; i < 16; i++)
                        if (VaalCorruption.Apply(catalog, Game.RollsFor(player), Game.UniquesFor(player), Game.MapDevices,
                            Game.DropsFor(player).UniqueWeight, gemItem with { Corrupted = false }, random) is { } vaal && vaal.TypeHash != gemItem.TypeHash)
                        {
                            Tell(player, "Defiled Vaal Orb: it became its Vaal gem.");
                            return vaal with { Corrupted = true, GemExperience = gemItem.GemExperience < 0 ? gemItem.GemExperience : vaal.GemExperience, Quality = gemItem.Quality };
                        }
                    break;
            }
        }
        Tell(player, "Defiled Vaal Orb: nothing can corrupt that gem further.");
        return gemItem;
    }
}
