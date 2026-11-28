using GameServer.Core.Protocol.Instance;
using GameServer.Modding;

/// <summary>Classic Harvest crafts as chat commands, on the item in the Horticrafting Station's slot. The native panel
/// lists only the client's own HarvestCraftOptions rows, so the removed crafts cannot be added to it; this mod runs
/// them through the same slot and payment the Harvest module uses (IPlayerWorld.CraftInSlot).</summary>
public sealed class HarvestCraftsMod : IMod
{
    sealed record Choices(double CostMultiplier);

    /// <summary>The client's HarvestCraftingItem inventory row (the Harvest module's Slot).</summary>
    const byte Slot = 43;
    const int Prefix = 1, Suffix = 2;

    const string Wild = "Metadata/Items/Currency/HarvestSeedRed", Vivid = "Metadata/Items/Currency/HarvestSeedGreen",
        Primal = "Metadata/Items/Currency/HarvestSeedBlue";

    // What a who types, and the Tags.dat tag it means. The lifeforce colour per type is this mod's own choice.
    static readonly Dictionary<string, (string Tag, string Lifeforce)> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fire"] = ("fire", Wild), ["cold"] = ("cold", Vivid), ["lightning"] = ("lightning", Primal),
        ["physical"] = ("physical", Vivid), ["phys"] = ("physical", Vivid), ["life"] = ("life", Wild),
        ["defence"] = ("defences", Primal), ["defences"] = ("defences", Primal), ["defense"] = ("defences", Primal),
        ["chaos"] = ("chaos", Vivid), ["attack"] = ("attack", Wild), ["caster"] = ("caster", Primal),
        ["spell"] = ("caster", Primal), ["speed"] = ("speed", Vivid), ["critical"] = ("critical", Wild),
        ["crit"] = ("critical", Wild), ["influence"] = ("influence", Primal),
        ["minion"] = ("minion", Primal), ["mana"] = ("mana", Primal), ["attribute"] = ("attribute", Wild),
        ["elemental"] = ("elemental", Wild),
    };

    static readonly string[] InfluenceSuffixes = ["_shaper", "_elder", "_crusader", "_eyrie", "_basilisk", "_adjudicator"];

    const string Help =
        "Classic Harvest crafts, on the item in the Horticrafting Station. <type> is fire, cold, lightning, physical, life, " +
        "defence, chaos, attack, caster, speed, critical or influence.\n" +
        "/hc augment <type> [lucky] | /hc augment lucky\n" +
        "/hc remove <type> | /hc removeadd <type> | /hc removenon <type>\n" +
        "/hc randomise <type> | /hc lucky prefixes|suffixes|all\n" +
        "/hc reforge <type> [more] | /hc reforge keepprefixes|keepsuffixes|best\n" +
        "/hc upgrade magic 2|3|4 [high] | /hc upgrade normal 1|2 | /hc upgrade best\n" +
        "/hc fracture [prefix|suffix] | /hc cost";

    double _cost = 1;

    public void Load(IModContext mod)
    {
        _cost = mod.Settings<Choices>().CostMultiplier;
        GameEvents.Command(mod.Manifest.Id, "hc", "/hc (classic Harvest crafts on the item in the Horticrafting Station; /hc alone lists them)", Run);
        // For the QoL overlay's Harvest panel: whether the station holds an item, the crafts (arguments and a label each),
        // and a craft to run as /hc would.
        AppDomain.CurrentDomain.SetData("localexile.harvest.station", (Func<Player, bool>)(player => Program.WorldOf(player).ItemIdIn(Slot) != 0));
        AppDomain.CurrentDomain.SetData("localexile.harvest.crafts", Menu());
        AppDomain.CurrentDomain.SetData("localexile.harvest.run", (Func<Player, string[], Task>)Run);
    }

    /// <summary>Every craft as "arguments|label", in the order the overlay lists them.</summary>
    static string[] Menu()
    {
        string[] kinds = ["fire", "cold", "lightning", "physical", "chaos", "life", "defences", "attack", "caster", "speed", "critical",
            "influence", "minion", "mana", "attribute", "elemental"];
        string Name(string k) => char.ToUpperInvariant(k[0]) + k[1..];
        var menu = new List<string>();
        foreach (string k in kinds) menu.Add($"augment {k}|Augment: {Name(k)}");
        foreach (string k in kinds) menu.Add($"augment {k} lucky|Augment (lucky): {Name(k)}");
        menu.Add("augment lucky|Augment: a lucky random modifier");
        foreach (string k in kinds) menu.Add($"remove {k}|Remove: {Name(k)}");
        foreach (string k in kinds) menu.Add($"removeadd {k}|Remove {Name(k)}, add a modifier");
        foreach (string k in kinds) menu.Add($"removenon {k}|Remove a non-{Name(k)}, add {Name(k)}");
        foreach (string k in kinds) menu.Add($"randomise {k}|Randomise the values: {Name(k)}");
        menu.AddRange(["lucky prefixes|Lucky values: prefixes", "lucky suffixes|Lucky values: suffixes", "lucky all|Lucky values: all"]);
        foreach (string k in kinds) menu.Add($"reforge {k}|Reforge with {Name(k)}");
        foreach (string k in kinds) menu.Add($"reforge {k} more|Reforge with more {Name(k)}");
        menu.AddRange(["reforge keepprefixes|Reforge, keep prefixes", "reforge keepsuffixes|Reforge, keep suffixes", "reforge best|Reforge: best of two",
            "upgrade magic 2|Magic to rare, 2 modifiers", "upgrade magic 3|Magic to rare, 3 modifiers", "upgrade magic 4|Magic to rare, 4 modifiers",
            "upgrade magic 4 high|Magic to rare, 4 high-tier modifiers", "upgrade normal 1|Normal to magic, 1 modifier", "upgrade normal 2|Normal to magic, 2 modifiers",
            "upgrade best|Upgrade: best", "fracture|Fracture a random modifier", "fracture prefix|Fracture a prefix", "fracture suffix|Fracture a suffix"]);
        return [.. menu];
    }

    // One craft: what it does to the item (null: it does not fit, nothing is spent) and what it costs before the multiplier.
    sealed record Craft(string Name, Func<Engine, Item, Item?> Apply, string Lifeforce, int Cost);

    async Task Run(Player player, string[] args)
    {
        var who = Program.PlayerOf(player);
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase)) { await who.Tell(Help); return; }
        if (args[0].Equals("cost", StringComparison.OrdinalIgnoreCase))
        {
            await who.Tell(_cost <= 0 ? "Classic Harvest crafts are free (costMultiplier 0)."
                : $"Classic Harvest crafts cost lifeforce at x{_cost:0.##} of this mod's base costs; the mod's settings change it.");
            return;
        }
        if (Parse(args) is not { } craft) { await who.Tell("Unknown craft. " + Help); return; }
        var world = Program.WorldOf(player);
        if (world.ItemIdIn(Slot) == 0) { await who.Tell("Put an item in the Horticrafting Station first."); return; }
        var catalog = Game.ItemsFor(player);
        var engine = new Engine(catalog, Game.RollsFor(player), Random.Shared);
        var costs = new List<(uint Currency, int Amount)>();
        int amount = (int)Math.Round(craft.Cost * _cost);
        if (amount > 0 && catalog.Find(craft.Lifeforce)?.Hash is uint lifeforce) costs.Add((lifeforce, amount));
        string? change = null;
        var crafted = await world.CraftInSlot(Slot, item =>
        {
            if (!engine.Craftable(item) || craft.Apply(engine, item) is not { } result) return null;
            change = engine.Changes(item, result);
            return new CraftResult(ItemRolls.ClearForesee(result with { Identified = true }), costs);
        }, $"Classic Harvest craft {craft.Name}");
        if (crafted is null)
        {
            await who.Tell($"{craft.Name}: refused. The item does not fit this craft, or you lack {amount} lifeforce.");
            return;
        }
        // As the Harvest module does after a craft: the slot item's 0x276 event refreshes the panel's item.
        await who.Send(InstanceHarvest.CraftResult((ushort)crafted.Id), $"0x0276 Harvest item event {crafted.Id} ({craft.Name})");
        await who.Tell($"{craft.Name}: {change ?? "done"}{(amount > 0 ? $" ({amount} lifeforce)" : "")}");
    }

    static Craft? Parse(string[] args)
    {
        string verb = args[0].ToLowerInvariant();
        string a = args.Length > 1 ? args[1].ToLowerInvariant() : "", b = args.Length > 2 ? args[2].ToLowerInvariant() : "";
        (string Tag, string Lifeforce)? type = Types.TryGetValue(a, out var found) ? found : null;
        switch (verb)
        {
            case "augment" when a == "lucky":
                return new("Augment (lucky)", (e, i) => e.Augment(i, null, lucky: true), Wild, 30000);
            case "augment" when type is { } t:
                return b == "lucky"
                    ? new($"Augment {t.Tag} (lucky)", (e, i) => e.Augment(i, t.Tag, lucky: true), t.Lifeforce, 30000)
                    : new($"Augment {t.Tag}", (e, i) => e.Augment(i, t.Tag, lucky: false), t.Lifeforce, 15000);
            case "remove" when type is { } t:
                return new($"Remove {t.Tag}", (e, i) => e.Remove(i, t.Tag, has: true), t.Lifeforce, 10000);
            case "removeadd" when type is { } t:
                return new($"Remove {t.Tag}, add {t.Tag}", (e, i) => e.RemoveAdd(i, t.Tag, removeHas: true), t.Lifeforce, 20000);
            case "removenon" when type is { } t:
                return new($"Remove non-{t.Tag}, add {t.Tag}", (e, i) => e.RemoveAdd(i, t.Tag, removeHas: false), t.Lifeforce, 15000);
            case "randomise" or "randomize" when type is { } t:
                return new($"Randomise {t.Tag} values", (e, i) => e.Randomise(i, m => e.Has(m, t.Tag), lucky: false, implicits: false), t.Lifeforce, 5000);
            case "lucky" when a is "prefixes" or "prefix":
                return new("Lucky prefixes", (e, i) => e.Randomise(i, m => m.GenerationType == Prefix, lucky: true, implicits: false), Wild, 10000);
            case "lucky" when a is "suffixes" or "suffix":
                return new("Lucky suffixes", (e, i) => e.Randomise(i, m => m.GenerationType == Suffix, lucky: true, implicits: false), Vivid, 10000);
            case "lucky" when a == "all":
                return new("Lucky values", (e, i) => e.Randomise(i, _ => true, lucky: true, implicits: true), Primal, 20000);
            case "reforge" when a == "keepprefixes":
                return new("Reforge keeping prefixes", (e, i) => e.ReforgeKeeping(i, Prefix), Wild, 10000);
            case "reforge" when a == "keepsuffixes":
                return new("Reforge keeping suffixes", (e, i) => e.ReforgeKeeping(i, Suffix), Vivid, 10000);
            case "reforge" when a == "best":
                return new("Reforge ten times, keeping the best", (e, i) => e.ReforgeBest(i), Primal, 5000);
            case "reforge" when type is { } t:
                return b == "more"
                    ? new($"Reforge with {t.Tag}, {t.Tag} more common", (e, i) => e.Reforge(i, t.Tag, more: true), t.Lifeforce, 2000)
                    : new($"Reforge with {t.Tag}", (e, i) => e.Reforge(i, t.Tag, more: false), t.Lifeforce, 500);
            case "upgrade" when a == "magic" && int.TryParse(b, out int n) && n is >= 2 and <= 4:
                bool high = args.Length > 3 && args[3].Equals("high", StringComparison.OrdinalIgnoreCase);
                return new($"Upgrade magic to rare, {n} {(high ? "high-tier " : "")}modifiers",
                    (e, i) => e.Upgrade(i, ItemRarity.Magic, ItemRarity.Rare, n, high), Wild, high ? 10000 : 2000);
            case "upgrade" when a == "normal" && int.TryParse(b, out int n) && n is >= 1 and <= 2:
                return new($"Upgrade normal to magic, {n} high-tier modifier{(n == 1 ? "" : "s")}",
                    (e, i) => e.Upgrade(i, ItemRarity.Normal, ItemRarity.Magic, n, high: true), Wild, 5000);
            case "upgrade" when a == "best":
                return new("Upgrade a normal item ten times, keeping the best", (e, i) => e.UpgradeBest(i), Wild, 1000);
            case "fracture":
                int side = a is "prefix" or "prefixes" ? Prefix : a is "suffix" or "suffixes" ? Suffix : 0;
                if (a.Length > 0 && side == 0) return null;
                return new(side == Prefix ? "Fracture a prefix" : side == Suffix ? "Fracture a suffix" : "Fracture a modifier",
                    (e, i) => e.Fracture(i, side), Primal, 10000);
            default:
                return null;
        }
    }

    /// <summary>The crafts themselves, on the Gateway's item rules for the who's data set.</summary>
    sealed class Engine(ItemCatalog catalog, ItemRolls rolls, Random rng)
    {
        ModCatalog.Mod? ModOf(ItemMod m) => catalog.Mods.ByHash(m.Hash);

        /// <summary>A mod has a type: its Tags.dat tag, or for "influence" a spawn weight behind an influence tag.</summary>
        public bool Has(ModCatalog.Mod mod, string tag) => tag == "influence"
            ? mod.SpawnWeights.Any(w => w.Weight > 0 && InfluenceSuffixes.Any(s => w.Tag.EndsWith(s, StringComparison.Ordinal)))
            : mod.Tags.Contains(tag);

        public bool Craftable(Item item) => !item.Corrupted && !item.Mirrored && !item.Vestigial && item.Identified
            && item.Components.Contains("Mods") && item.Rarity != ItemRarity.Unique
            && catalog.ByHash(item.TypeHash) is { } type && type.Components.Contains("Mods");

        // The explicit mods a craft may remove or change: not fractured, not protected by a metamod.
        List<int> Open(Item item, Func<ModCatalog.Mod, bool> match)
        {
            var rules = new MetacraftRules(item, catalog.Mods);
            return [.. Enumerable.Range(0, item.Mods.Count).Where(i => item.Mods[i].Section == ModSection.Explicit
                && !rules.Protected(i) && ModOf(item.Mods[i]) is { } mod && match(mod))];
        }

        (int Prefix, int Suffix) Max(Item item) => item.Rarity == ItemRarity.Magic ? (1, 1) : rolls.RareMaxAffixes(item);

        static T Pick<T>(IReadOnlyList<(T Value, int Weight)> pool, Random random)
        {
            long total = pool.Sum(p => (long)Math.Max(0, p.Weight));
            long at = (long)(random.NextDouble() * total);
            foreach (var (value, weight) in pool)
            {
                if (at < weight) return value;
                at -= Math.Max(0, weight);
            }
            return pool[^1].Value;
        }

        ItemMod Roll(ModCatalog.Mod mod, ModSection section, bool lucky)
        {
            var first = mod.Roll(section, rng);
            if (!lucky) return first;
            // Lucky: each value is rolled twice and the higher kept.
            var second = mod.Roll(section, rng);
            return new(section, mod.Hash, [.. first.Values.Select((v, i) => Math.Max(v, second.Values[i]))]);
        }

        // A new explicit goes after the last prefix (a prefix) or last (a suffix), and never before the fractured mods.
        Item Insert(Item item, ModCatalog.Mod mod, ItemMod rolled)
        {
            var mods = item.Mods.ToList();
            int at = mod.GenerationType == Prefix
                ? mods.FindLastIndex(m => m.Section == ModSection.Explicit && ModOf(m)?.GenerationType == Prefix) + 1
                : mods.FindLastIndex(m => m.Section == ModSection.Explicit) + 1;
            if (at == 0) at = mods.FindIndex(m => m.Section == ModSection.Explicit) is int e and >= 0 ? e : mods.Count;
            if (item.FracturedIndexes() is { Count: > 0 } fractured) at = Math.Max(at, fractured.Max() + 1);
            mods.Insert(Math.Min(at, mods.Count), rolled);
            return item with { Mods = mods };
        }

        Item? Add(Item item, string? tag, bool lucky, bool high = false, (int Prefix, int Suffix)? max = null)
        {
            var pool = rolls.AffixesToAdd(item, max ?? Max(item), respectMetamods: true)
                .Where(p => p.Weight > 0 && (tag is null || Has(p.Mod, tag))).ToList();
            // High-tier: only the highest-level mod of each mod type the item can roll.
            if (high) pool = [.. pool.GroupBy(p => p.Mod.ModType.Length > 0 ? p.Mod.ModType : p.Mod.Id)
                .Select(g => g.OrderByDescending(p => p.Mod.Level).First())];
            if (pool.Count == 0) return null;
            var mod = Pick(pool, rng);
            return Insert(item, mod, Roll(mod, ModSection.Explicit, lucky));
        }

        public Item? Augment(Item item, string? tag, bool lucky) =>
            item.Rarity is ItemRarity.Magic or ItemRarity.Rare ? Add(item, tag, lucky) : null;

        public Item? Remove(Item item, string tag, bool has)
        {
            if (item.Rarity is not (ItemRarity.Magic or ItemRarity.Rare)) return null;
            var open = Open(item, m => Has(m, tag) == has);
            if (open.Count == 0) return null;
            int removed = open[rng.Next(open.Count)];
            return item with { Mods = [.. item.Mods.Where((_, i) => i != removed)] };
        }

        public Item? RemoveAdd(Item item, string tag, bool removeHas) =>
            Remove(item, tag, removeHas) is { } removed ? Add(removed, tag, lucky: false) : null;

        public Item? Randomise(Item item, Func<ModCatalog.Mod, bool> match, bool lucky, bool implicits)
        {
            if (item.Rarity is not (ItemRarity.Magic or ItemRarity.Rare)) return null;
            var open = Open(item, match).ToHashSet();
            if (implicits)
                foreach (int i in Enumerable.Range(0, item.Mods.Count))
                    if (item.Mods[i].Section == ModSection.Implicit && ModOf(item.Mods[i]) is not null) open.Add(i);
            if (open.Count == 0) return null;
            return item with { Mods = [.. item.Mods.Select((m, i) => open.Contains(i) ? Roll(ModOf(m)!, m.Section, lucky) : m)] };
        }

        public Item? Reforge(Item item, string tag, bool more)
        {
            if (item.Rarity == ItemRarity.Unique) return null;
            Item? One() => tag == "influence" ? ReforgeInfluence(item) : rolls.RollRequiredTag(item, ItemRarity.Rare, rng, tag);
            if (!more) return One();
            // "More common": the best of three such reforges by how many modifiers of the type it holds (this mod's reading).
            Item? best = null;
            for (int n = 0; n < 3; n++)
                if (One() is { } rolled && (best is null || Count(rolled, tag) > Count(best, tag))) best = rolled;
            return best;
        }

        int Count(Item item, string tag) =>
            item.Mods.Count(m => m.Section == ModSection.Explicit && ModOf(m) is { } mod && Has(mod, tag));

        // An influence reforge: up to twenty ordinary reforges until one holds an influence modifier.
        Item? ReforgeInfluence(Item item)
        {
            if (item.Influences == ItemInfluence.None) return null;
            for (int n = 0; n < 20; n++)
                if (rolls.Roll(item, ItemRarity.Rare, rng) is { } rolled && Count(rolled, "influence") > 0) return rolled;
            return null;
        }

        public Item? ReforgeKeeping(Item item, int keep)
        {
            if (item.Rarity != ItemRarity.Rare) return null;
            var drop = Open(item, m => m.GenerationType != keep).ToHashSet();
            var result = item with { Mods = [.. item.Mods.Where((_, i) => !drop.Contains(i))] };
            var max = rolls.RareMaxAffixes(item);
            int kept = result.Mods.Count(m => m.Section == ModSection.Explicit && ModOf(m)?.GenerationType == keep);
            // The kept side is full for this craft, so only the other side gets new modifiers: one to its maximum.
            var limit = keep == Prefix ? (kept, max.Suffix) : (max.Prefix, kept);
            int target = rng.Next(1, Math.Max(1, keep == Prefix ? max.Suffix : max.Prefix) + 1);
            int added = 0;
            while (added < target && Add(result, null, lucky: false, max: limit) is { } next) { result = next; added++; }
            return added == 0 && drop.Count == 0 ? null : result;
        }

        double AverageLevel(Item item)
        {
            var levels = item.Mods.Where(m => m.Section == ModSection.Explicit).Select(m => ModOf(m)?.Level ?? 0).ToList();
            return levels.Count == 0 ? 0 : levels.Average();
        }

        public Item? ReforgeBest(Item item)
        {
            if (item.Rarity != ItemRarity.Rare) return null;
            Item? best = null;
            for (int n = 0; n < 10; n++)
            {
                var rolled = rolls.Roll(item, ItemRarity.Rare, rng);
                if (best is null || AverageLevel(rolled) > AverageLevel(best)) best = rolled;
            }
            return best;
        }

        public Item? Upgrade(Item item, ItemRarity from, ItemRarity to, int count, bool high)
        {
            if (item.Rarity != from) return null;
            var result = item with { Rarity = to, NameWords = to == ItemRarity.Rare ? rolls.RareName(item, rng) : [] };
            int added = 0;
            while (added < count && Add(result, null, lucky: false, high: high) is { } next) { result = next; added++; }
            return added == 0 ? null : result;
        }

        public Item? UpgradeBest(Item item)
        {
            if (item.Rarity != ItemRarity.Normal) return null;
            // Ten rolls of a random rarity, keeping the highest: magic or rare each time.
            var rarity = Enumerable.Range(0, 10).Any(_ => rng.Next(2) == 1) ? ItemRarity.Rare : ItemRarity.Magic;
            return rolls.Roll(item, rarity, rng);
        }

        public Item? Fracture(Item item, int side)
        {
            if (item.Rarity != ItemRarity.Rare || item.FracturedCount > 0 || item.Influences != ItemInfluence.None || item.Synthesised
                || catalog.ByHash(item.TypeHash) is not { CanBeFractured: true }) return null;
            var explicits = Enumerable.Range(0, item.Mods.Count).Where(i => item.Mods[i].Section == ModSection.Explicit).ToList();
            var candidates = explicits.Where(i => side == 0 || ModOf(item.Mods[i])?.GenerationType == side).ToList();
            // The 3.11 crafts: any modifier of an item with at least five, or a prefix or suffix of one with at least three of them.
            if (candidates.Count < (side == 0 ? 5 : 3)) return null;
            int index = candidates[rng.Next(candidates.Count)];
            var mod = item.Mods[index];
            var mods = item.Mods.Where((_, i) => i != index).ToList();
            mods.Insert(explicits[0], mod);
            return item with { Mods = mods, FracturedCount = 1 };
        }

        /// <summary>What a craft did, for the chat line: the modifiers that left and the ones that came.</summary>
        public string Changes(Item before, Item after)
        {
            string Text(ItemMod m) => catalog.Mods.Describe(m);
            var was = before.Mods.Where(m => m.Section == ModSection.Explicit).ToList();
            var now = after.Mods.Where(m => m.Section == ModSection.Explicit).ToList();
            var removed = was.Where(m => !now.Any(n => n.Hash == m.Hash && n.Values.SequenceEqual(m.Values))).Select(Text).ToList();
            var added = now.Where(m => !was.Any(w => w.Hash == m.Hash && w.Values.SequenceEqual(m.Values))).Select(Text).ToList();
            if (removed.Count + added.Count > 6) return $"{removed.Count} modifiers left, {added.Count} came";
            var parts = new List<string>();
            if (removed.Count > 0) parts.Add("removed " + string.Join("; ", removed));
            if (added.Count > 0) parts.Add("added " + string.Join("; ", added));
            if (after.FracturedCount > before.FracturedCount && after.FracturedIndexes().Count > 0)
                parts.Add("fractured " + Text(after.Mods[after.FracturedIndexes().Min()]));
            return parts.Count == 0 ? "values changed" : string.Join(", ", parts);
        }
    }
}
