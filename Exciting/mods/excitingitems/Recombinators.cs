using System.Runtime.CompilerServices;
using GameServer.Core.Protocol.Instance;

/// <summary>The Sentinel league's Recombinators (Armour, Weapon, Jewellery), as they were in 3.18: used on one item, then
/// on a second of the same item class, they destroy both and make one from their properties. Every later change is a
/// switch of its own (recombinators in the settings), off by default. Private build: it needs the CurrencyOnItem hook.</summary>
sealed record RecombinatorChoices(bool Enabled = true, int ArmourDrop = 4000, int WeaponDrop = 4000, int JewelleryDrop = 6000,
    string[]? Rarities = null, bool Corrupted = true, bool Mirrored = true, bool AverageItemLevel = true,
    bool NativeOnly = false, bool NonNativeCount = true, bool OneExclusive = false, bool FracturedNeedsFracturedBase = false,
    bool KeepFractured = true, bool ModGroups = true, string Selection = "equal", int SentinelChance = 50,
    int TierUp = 5, int TierDown = 5);

public sealed partial class ExcitingItemsMod
{
    const string ArmourRecombinator = "Metadata/Items/Currency/SentinelCurrencyArmour";
    const string WeaponRecombinator = "Metadata/Items/Currency/SentinelCurrencyWeapon";
    const string JewelleryRecombinator = "Metadata/Items/Currency/SentinelCurrencyJewellery";

    static readonly string[] ArmourClasses = ["Body Armour", "Helmet", "Gloves", "Boots", "Shield"];
    static readonly string[] JewelleryClasses = ["Ring", "Amulet", "Belt"];
    static readonly string[] WeaponClasses = ["Claw", "Dagger", "Rune Dagger", "Wand", "One Hand Sword", "Thrusting One Hand Sword",
        "One Hand Axe", "One Hand Mace", "Sceptre", "Bow", "Staff", "Warstaff", "Two Hand Sword", "Two Hand Axe", "Two Hand Mace", "Quiver"];

    // The first use's item, until the second.
    sealed class Chosen { public uint Id; public byte Row; public Item Item = null!; public string Kind = ""; }
    readonly ConditionalWeakTable<Player, Chosen> _chosen = new();

    // The pre-3.30 modifier counts (poewiki, Recombinator: "Estimated modifier count distribution before 3.30"): for a
    // pool of 1 to 6 prefixes (or suffixes), the chances in % of 0, 1, 2 and 3 on the result.
    static readonly int[][] CountTable =
    [
        [100, 0, 0, 0], [41, 59, 0, 0], [0, 67, 33, 0], [0, 40, 50, 10], [0, 10, 60, 30], [0, 0, 43, 57], [0, 0, 30, 70],
    ];

    // The Sentinel-exclusive modifiers (3.18; poedb, Recombinator) and the items they went on: every tag of one list.
    static readonly (string Id, string[][] Tags)[] SentinelMods = BuildSentinelMods();

    static (string, string[][])[] BuildSentinelMods()
    {
        string[][] Str(string slot) => [[slot, "str_armour"], [slot, "str_dex_armour"], [slot, "str_int_armour"]];
        string[][] Dex(string slot) => [[slot, "dex_armour"], [slot, "str_dex_armour"], [slot, "dex_int_armour"]];
        string[][] Int(string slot) => [[slot, "int_armour"], [slot, "str_int_armour"], [slot, "dex_int_armour"]];
        string[][] One = [["one_hand_weapon"]], Two = [["two_hand_weapon"]], Body = [["body_armour"]];
        var list = new List<(string, string[][])>
        {
            ("RecombinatorSpecialAllColdDamageCanIgnite", Str("helmet")), ("RecombinatorSpecialAllFireDamageCanShock", Dex("helmet")),
            ("RecombinatorSpecialAllLightningDamageCanFreeze", Int("helmet")),
            ("RecombinatorSpecialArcaneSurgeEffect1H", One), ("RecombinatorSpecialArcaneSurgeEffect2H", Two),
            ("RecombinatorSpecialOnslaughtEffect1H", One), ("RecombinatorSpecialOnslaughtEffect2H", Two),
            ("RecombinatorSpecialIncreasedDamageVsWarcryTauntedEnemies1H", One), ("RecombinatorSpecialIncreasedDamageVsWarcryTauntedEnemies2H", Two),
            ("RecombinatorSpecialAuraEffect", [["amulet"]]), ("RecombinatorSpecialMagicUtilityFlaskEffect", [["belt"]]),
            ("RecombinatorSpecialMarkEffect", [["quiver"]]), ("RecombinatorSpecialTriggerSkillsDoubleDamage", [["shield"]]),
            ("RecombinatorSpecialMaximumEnduranceCharge", [["ring"]]), ("RecombinatorSpecialMaximumFrenzyCharge", [["ring"]]),
            ("RecombinatorSpecialMaximumPowerCharge", [["ring"]]),
            ("RecombinatorSpecialFireDamageToAttacksPerStrength", Str("boots")), ("RecombinatorSpecialColdDamageToAttacksPerDexterity", Dex("boots")),
            ("RecombinatorSpecialLightningDamageToAttacksPerIntelligence", Int("boots")),
            ("RecombinatorSpecialSupportedByLevelFourEmpower", Str("gloves")), ("RecombinatorSpecialSupportedByLevelFourEnhance", Dex("gloves")),
            ("RecombinatorSpecialSupportedByLevelFourEnlighten", Int("gloves")),
        };
        // Every keystone one is for body armours.
        foreach (string k in new[] { "MinionInstability", "ResoluteTechnique", "BloodMagic", "PainAttunement", "ElementalEquilibrium",
            "IronGrip", "PointBlank", "Acrobatics", "GhostReaver", "VaalPact", "ElementalOverload", "AvatarOfFire", "EldritchBattery",
            "AncestralBond", "CrimsonDance", "PerfectAgony", "Runebinder", "MortalConviction", "CallToArms", "TheAgnostic", "SupremeEgo",
            "TheImpaler", "Doomsday", "LetheShade1", "GhostDance", "VersatileCombatant", "Magebane", "Solipsism", "DivineShield", "IronWill" })
            list.Add(("RecombinatorSpecialKeystone" + k, Body));
        return [.. list];
    }

    static string[]? RecombinatorClasses(string id) => id switch
    {
        ArmourRecombinator => ArmourClasses, WeaponRecombinator => WeaponClasses, JewelleryRecombinator => JewelleryClasses, _ => null,
    };

    void RecombinatorDrops(DropRoll d)
    {
        var c = _choices.Recombinators ?? new();
        if (!c.Enabled) return;
        double weight = d.Monster.Rarity switch { MonsterRarity.Magic => 3, MonsterRarity.Rare => 6, MonsterRarity.Unique => 10, _ => 1 };
        foreach (var (id, chance) in new[] { (ArmourRecombinator, c.ArmourDrop), (WeaponRecombinator, c.WeaponDrop), (JewelleryRecombinator, c.JewelleryDrop) })
            if (chance > 0 && Random.Shared.NextDouble() * chance < weight * Math.Max(0, d.Quantity))
            {
                d.Stacks.Add((id, 1));
                if (_choices.Announce) Tell(d.Player, "* A Recombinator dropped.");
            }
    }

    /// <summary>A recombinator used on an item: the first use chooses it (nothing is spent), the second, on another item
    /// of the same class, makes the new item in that one's place and takes the first out of the inventory.</summary>
    Item? RecombinatorUse(Player player, Item target, string kind, Random random)
    {
        var c = _choices.Recombinators ?? new();
        if (target.Foreseeing) return target;
        if (!c.Enabled) { Tell(player, "Recombinator: turned off in Exciting Items' settings."); return target; }
        var catalog = Game.ItemsFor(player);
        if (catalog.ByHash(target.TypeHash) is not { } type || RecombinatorClasses(kind) is not { } classes || !classes.Contains(type.Class))
        { Tell(player, "Recombinator: that is not an item this recombinator combines."); return target; }
        var rarities = (c.Rarities ?? ["normal", "magic", "rare"]).Select(r => Enum.TryParse<ItemRarity>(r, true, out var v) ? v : (ItemRarity?)null).OfType<ItemRarity>().ToHashSet();
        if (!rarities.Contains(target.Rarity)) { Tell(player, $"Recombinator: it does not combine {target.Rarity.ToString().ToLowerInvariant()} items."); return target; }
        if (target.Corrupted && !c.Corrupted || target.Mirrored && !c.Mirrored) { Tell(player, "Recombinator: it does not combine corrupted or mirrored items (settings)."); return target; }
        var inventory = Game.ItemsOf(player);
        (byte Row, uint Id)? Where(Item item)
        {
            foreach (byte row in new[] { PlayerInventories.MainInventory })
                if (inventory.Items(row).FirstOrDefault(i => ReferenceEquals(i.Item, item)) is { } found) return (row, found.Id);
            return null;
        }
        if (_chosen.TryGetValue(player, out var first) && first.Kind == kind
            && inventory.Items(first.Row).FirstOrDefault(i => i.Id == first.Id && ReferenceEquals(i.Item, first.Item)) is not null)
        {
            if (ReferenceEquals(first.Item, target)) { _chosen.Remove(player); Tell(player, "Recombinator: choice cleared."); return target; }
            if (catalog.ByHash(first.Item.TypeHash)?.Class != type.Class)
            { Tell(player, $"Recombinator: the second item must be a {catalog.ByHash(first.Item.TypeHash)?.Class} too."); return target; }
            var log = new List<string>();
            var made = Recombine(player, first.Item, target, c, random, log);
            if (made is null) { Tell(player, "Recombinator: those two cannot be combined."); return target; }
            _chosen.Remove(player);
            // The first item goes now; the client hears it once the server has answered the orb's use.
            if (inventory.Remove(first.Row, first.Id) is null) { Tell(player, "Recombinator: the first item moved; choose again."); return target; }
            byte row = first.Row; uint gone = first.Id;
            _ = Task.Run(async () =>
            {
                await Task.Delay(250);
                try
                {
                    await Program.Send(player, InstanceItems.InventoryChange(PlayerInventories.InventoryId(row), inventory.NextRevision(row), [gone], []),
                        $"excitingitems: recombinator took item {gone}");
                }
                catch (Exception e) { _log($"recombinator: the removal was not sent: {e.Message}"); }
            });
            Tell(player, "Recombinator: " + string.Join("; ", log));
            return made;
        }
        if (Where(target) is not { } at) { Tell(player, "Recombinator: put the item in your inventory first."); return target; }
        _chosen.AddOrUpdate(player, new Chosen { Id = at.Id, Row = at.Row, Item = target, Kind = kind });
        Tell(player, $"Recombinator: chosen. Now use it on a second {type.Class} (or on this one again to clear the choice).");
        return target;
    }

    sealed record Pooled(ItemMod Mod, ModCatalog.Mod Def, bool Fractured, bool Exclusive);

    static int MostWeight(ModCatalog.Mod m) => m.SpawnWeights.Count == 0 ? 0 : m.SpawnWeights.Max(w => w.Weight);

    Item? Recombine(Player player, Item a, Item b, RecombinatorChoices c, Random rng, List<string> log)
    {
        var catalog = Game.ItemsFor(player);
        var rolls = Game.RollsFor(player);
        var (baseItem, other) = rng.Next(2) == 0 ? (a, b) : (b, a);
        if (catalog.ByHash(baseItem.TypeHash) is not { } type) return null;
        var tags = rolls.TagsWith(type, baseItem.Influences);
        IEnumerable<Pooled> Explicits(Item item) => item.Mods.Where(m => m.Section == ModSection.Explicit)
            .Select((m, i) => (m, i, def: catalog.Mods.ByHash(m.Hash)))
            .Where(x => x.def is { GenerationType: ItemRolls.Prefix or ItemRolls.Suffix })
            .Select(x => new Pooled(x.m, x.def!, x.i < item.FracturedCount, MostWeight(x.def!) == 0 || x.def!.EssenceOnly));
        var pool = Explicits(a).Concat(Explicits(b)).ToList();
        // 3.25: a fractured modifier needs a fractured base.
        if (c.FracturedNeedsFracturedBase && baseItem.FracturedCount == 0) pool.RemoveAll(p => p.Fractured);
        bool Native(Pooled p) => p.Exclusive || p.Def.WeightFor(tags) > 0;
        var counted = pool.ToList();
        if (c.NativeOnly)
        {
            if (!c.NonNativeCount) counted.RemoveAll(p => !Native(p));
            pool.RemoveAll(p => !Native(p));
        }
        var (mostPrefixes, mostSuffixes) = rolls.RareMaxAffixes(baseItem with { Rarity = ItemRarity.Rare });
        int Count(int side, int most)
        {
            int n = Math.Min(6, counted.Count(p => p.Def.GenerationType == side));
            int at = rng.Next(100), k = 0;
            for (; k < 3; k++) if ((at -= CountTable[n][k]) < 0) break;
            return Math.Min(k, most);
        }
        int wantPrefixes = Count(ItemRolls.Prefix, mostPrefixes), wantSuffixes = Count(ItemRolls.Suffix, mostSuffixes);
        var chosen = new List<Pooled>();
        var families = new HashSet<int>();
        bool exclusiveTaken = false;
        int Weight(Pooled p) => c.Selection switch
        {
            "common" => Math.Max(1, MostWeight(p.Def)),
            "rare" => Math.Max(1, 100000 / Math.Max(1, MostWeight(p.Def))),
            _ => 1,
        };
        void Fill(int side, int want)
        {
            for (int i = 0; i < want; i++)
            {
                var open = pool.Where(p => p.Def.GenerationType == side && !chosen.Contains(p) && chosen.All(x => x.Def.Hash != p.Def.Hash)
                    && (!c.ModGroups || !p.Def.Families.Any(families.Contains)) && !(c.OneExclusive && exclusiveTaken && p.Exclusive)).ToList();
                if (open.Count == 0) return;
                int at = rng.Next(open.Sum(Weight));
                var pick = open.First(p => (at -= Weight(p)) < 0);
                chosen.Add(pick);
                foreach (int f in pick.Def.Families) families.Add(f);
                exclusiveTaken |= pick.Exclusive;
            }
        }
        if (rng.Next(2) == 0) { Fill(ItemRolls.Prefix, wantPrefixes); Fill(ItemRolls.Suffix, wantSuffixes); }
        else { Fill(ItemRolls.Suffix, wantSuffixes); Fill(ItemRolls.Prefix, wantPrefixes); }

        // 3.18: a modifier's tier could go up or down.
        var result = new List<(ItemMod Mod, bool Fractured)>();
        foreach (var p in chosen)
        {
            var mod = p.Mod;
            if (!p.Fractured && p.Def.Stats.Count > 0)
            {
                int roll = rng.Next(100);
                int dir = roll < c.TierUp ? 1 : roll < c.TierUp + c.TierDown ? -1 : 0;
                if (dir != 0 && Neighbour(catalog.Mods, p.Def, dir) is { } shifted)
                {
                    mod = shifted.Roll(ModSection.Explicit, rng);
                    log.Add($"\"{catalog.Mods.Describe(p.Mod)}\" went {(dir > 0 ? "up" : "down")} a tier");
                }
            }
            result.Add((mod, p.Fractured && c.KeepFractured));
        }
        int prefixes = chosen.Count(p => p.Def.GenerationType == ItemRolls.Prefix), suffixes = chosen.Count - prefixes;

        // 3.18: an open affix sometimes got a Sentinel modifier of its own.
        if (c.SentinelChance > 0 && rng.Next(c.SentinelChance) == 0)
        {
            var specials = SentinelMods.Where(s => s.Tags.Any(set => set.All(tags.Contains)))
                .Select(s => catalog.Mods.ById(s.Id)).OfType<ModCatalog.Mod>()
                .Where(m => m.GenerationType == ItemRolls.Prefix ? prefixes < mostPrefixes : suffixes < mostSuffixes).ToList();
            if (specials.Count > 0)
            {
                var special = specials[rng.Next(specials.Count)];
                result.Add((special.Roll(ModSection.Explicit, rng), false));
                if (special.GenerationType == ItemRolls.Prefix) prefixes++; else suffixes++;
                log.Add($"a Sentinel modifier: \"{catalog.Mods.Describe(result[^1].Mod)}\"");
            }
        }

        var mods = baseItem.Mods.Where(m => m.Section != ModSection.Explicit).ToList();
        mods.AddRange(result.Where(r => r.Fractured).Select(r => r.Mod));
        mods.AddRange(result.Where(r => !r.Fractured).Select(r => r.Mod));
        var rarity = prefixes + suffixes == 0 ? ItemRarity.Normal
            : prefixes <= 1 && suffixes <= 1 && a.Rarity != ItemRarity.Rare && b.Rarity != ItemRarity.Rare ? ItemRarity.Magic : ItemRarity.Rare;
        var made = baseItem with
        {
            Mods = mods, Rarity = rarity, FracturedCount = (byte)result.Count(r => r.Fractured),
            ItemLevel = c.AverageItemLevel ? (byte)((a.ItemLevel + b.ItemLevel + 1) / 2) : baseItem.ItemLevel,
            Mirrored = false, RequiredLevel = null,
        };
        if (rarity == ItemRarity.Rare && baseItem.Rarity != ItemRarity.Rare)
            made = made with { NameWords = rolls.Roll(baseItem with { Mods = [.. baseItem.Mods.Where(m => m.Section != ModSection.Explicit)] }, ItemRarity.Rare, rng).NameWords };
        log.Insert(0, $"{(ReferenceEquals(baseItem, a) ? "the first" : "the second")} item's base, {prefixes} prefix(es) and {suffixes} suffix(es) from a pool of {pool.Count}");
        return made;
    }

    internal static ModCatalog.Mod? TierNeighbour(ModCatalog mods, ModCatalog.Mod def, int dir) => Neighbour(mods, def, dir);

    /// <summary>The next tier up (dir 1) or down (-1) of a modifier: the same group, side and first stat, the nearest level.</summary>
    static ModCatalog.Mod? Neighbour(ModCatalog mods, ModCatalog.Mod def, int dir)
    {
        uint stat = def.Stats[0].Id;
        var same = mods.All.Where(m => m.ModType == def.ModType && m.GenerationType == def.GenerationType && m.Domain == def.Domain
            && m.Stats.Count > 0 && m.Stats[0].Id == stat && m.Families.SequenceEqual(def.Families) && MostWeight(m) > 0 && m.Hash != def.Hash);
        return dir > 0
            ? same.Where(m => m.Level > def.Level).OrderBy(m => m.Level).FirstOrDefault()
            : same.Where(m => m.Level < def.Level).OrderByDescending(m => m.Level).FirstOrDefault();
    }
}
