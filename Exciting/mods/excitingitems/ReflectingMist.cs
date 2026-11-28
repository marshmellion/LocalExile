using GameServer.Core.Protocol.Instance;

/// <summary>Reflecting Mist (private build; the server has no use of its own for it): a rare amulet or ring becomes two
/// mirrored halves. Every explicit modifier's values are multiplied by one roll (3.29: 1.3x to 1.6x in 0.01 steps; before
/// 3.29 1.5x to 2.1x), and each modifier is positive on one half and reflected (negative, or its Inverted modifier) on the
/// other, nearly always split evenly. The first half takes the item's place, the second goes in the inventory (or at the
/// player's feet). Its drops are a chance of its own, from Delirium monsters, any monster or bosses only.</summary>
sealed record MistChoices(bool Enabled = true, int DropChance = 3000, string DropsFrom = "delirium", int MinPercent = 130,
    int MaxPercent = 160, int StepPercent = 1, bool Corrupted = false, bool Mirrored = false, bool Influenced = false,
    bool Veiled = false, bool Synthesised = false, string[]? Classes = null, int Balanced = 90, string Items = "jewellery");

public sealed partial class ExcitingItemsMod
{
    const string MistId = "Metadata/Items/Currency/ReflectiveMist";

    void MistDrops(DropRoll d)
    {
        var c = _choices.ReflectingMist ?? new();
        if (!c.Enabled || c.DropChance <= 0) return;
        bool from = c.DropsFrom switch
        {
            "any" => true,
            "bosses" => d.Monster.Rarity == MonsterRarity.Unique,
            _ => d.Monster.VarietyId.Contains("Affliction", StringComparison.OrdinalIgnoreCase)
                || d.Monster.VarietyId.Contains("Delirium", StringComparison.OrdinalIgnoreCase),
        };
        if (!from) return;
        double weight = d.Monster.Rarity switch { MonsterRarity.Magic => 3, MonsterRarity.Rare => 6, MonsterRarity.Unique => 10, _ => 1 };
        if (Random.Shared.NextDouble() * c.DropChance < weight * Math.Max(0, d.Quantity))
        {
            d.Stacks.Add((MistId, 1));
            if (_choices.Announce) Tell(d.Player, "* A Reflecting Mist dropped.");
        }
    }

    Item? MistUse(Player player, Item target, Random random)
    {
        var c = _choices.ReflectingMist ?? new();
        // Off: refused (content/reflectingmist.json gives it a Divine Orb's client row, and the server must not use that).
        if (!c.Enabled) { Tell(player, "Reflecting Mist: turned off in Exciting Items' settings."); return target; }
        if (target.Foreseeing) return target;
        var catalog = Game.ItemsFor(player);
        if (catalog.ByHash(target.TypeHash) is not { } type) return target;
        var classes = c.Classes ?? ["Ring", "Amulet"];
        // Items: "jewellery" (the game's: rare rings and amulets), "nonunique" (any magic or rare item with modifiers) or
        // "any" (uniques too).
        bool jewellery = c.Items is not ("nonunique" or "any");
        string? why = !type.Components.Contains("Mods") || !target.Mods.Any(m => m.Section == ModSection.Explicit) ? "only items with explicit modifiers"
            : jewellery && !classes.Contains(type.Class) ? $"only {string.Join(" or ", classes).ToLowerInvariant()}s (settings)"
            : jewellery && target.Rarity != ItemRarity.Rare ? "only rare items (settings)"
            : c.Items == "nonunique" && target.Rarity == ItemRarity.Unique ? "not unique items (settings)"
            : target.Corrupted && !c.Corrupted ? "not corrupted items (settings)"
            : target.Mirrored && !c.Mirrored ? "not mirrored items (settings)"
            : target.Influences != ItemInfluence.None && !c.Influenced ? "not influenced items (settings)"
            : target.Synthesised && !c.Synthesised ? "not synthesised items (settings)"
            : !c.Veiled && target.Mods.Any(m => catalog.Mods.ByHash(m.Hash)?.Id.Contains("Veiled", StringComparison.OrdinalIgnoreCase) == true) ? "not veiled items (settings)"
            : jewellery && target.Sockets.Count > 0 ? "not items with sockets"
            : null;
        if (why is not null) { Tell(player, $"Reflecting Mist: {why}."); return target; }

        int lo = Math.Max(1, Math.Min(c.MinPercent, c.MaxPercent)), hi = Math.Max(c.MinPercent, c.MaxPercent), step = Math.Max(1, c.StepPercent);
        int percent = lo + random.Next((hi - lo) / step + 1) * step;
        var explicits = target.Mods.Select((m, i) => (m, i)).Where(x => x.m.Section == ModSection.Explicit).ToList();
        // Which half each modifier is positive on: an even split Balanced % of the time, else any split.
        var positiveOnFirst = explicits.Select(_ => random.Next(2) == 0).ToList();
        if (random.Next(100) < c.Balanced)
        {
            var order = Enumerable.Range(0, explicits.Count).OrderBy(_ => random.Next()).ToList();
            for (int k = 0; k < order.Count; k++) positiveOnFirst[order[k]] = k % 2 == 0;
        }
        ItemMod Positive(ItemMod m) => m with { Values = [.. m.Values.Select(v => (int)Math.Floor(v * percent / 100.0))] };
        ItemMod Reflected(ItemMod m)
        {
            var def = catalog.Mods.ByHash(m.Hash);
            if (def is null) return Positive(m);
            // Some modifiers have no negative form of their own: an Inverted modifier stands in (added damage to hits
            // against you, life loss ...). Its values are the scaled ones, as they are.
            if ((catalog.Mods.ById(def.Id + "Inverted") ?? catalog.Mods.ById(def.Id.TrimEnd('_') + "Inverted")) is { } inverted
                && inverted.Stats.Count == m.Values.Count)
                return new ItemMod(ModSection.Explicit, inverted.Hash, [.. m.Values.Select(v => (int)Math.Floor(v * percent / 100.0))]);
            // A modifier that only picks something (an aspect, a metamod) stays as it is.
            if (def.Stats.All(s => s.Min == s.Max)) return m;
            // Added damage has no negative of its own ("Adds -116 to -158" means nothing to the game): without an Inverted
            // modifier it stays as it is on both halves.
            if (def.Stats.Any(st => StatName(st.Id) is { } n && (n.Contains("minimum_added", StringComparison.Ordinal)
                || n.Contains("maximum_added", StringComparison.Ordinal) || n.Contains("added_damage", StringComparison.Ordinal))))
                return Positive(m);
            return m with { Values = [.. m.Values.Select(v => -(int)Math.Floor(v * percent / 100.0))] };
        }
        Item Half(bool first)
        {
            var mods = target.Mods.ToList();
            // The item's own defences never go below nothing: the server stops them at 0, but the client's tooltip turns a
            // negative into 4294967... So a reflected flat or % to the item's armour, evasion, energy shield or ward may take
            // the base (with this half's positive ones) down to 0 and no further.
            var defences = catalog.Defences(type);
            var flat = new Dictionary<char, long> { ['a'] = defences?.ArmourMin ?? 0, ['e'] = defences?.EvasionMin ?? 0, ['s'] = defences?.EnergyShieldMin ?? 0, ['w'] = defences?.WardMin ?? 0 };
            var pct = new Dictionary<char, long> { ['a'] = 100, ['e'] = 100, ['s'] = 100, ['w'] = 100 };
            var halves = new List<(int Index, ItemMod Mod, bool Reflected)>();
            for (int k = 0; k < explicits.Count; k++)
            {
                var (m, i) = explicits[k];
                bool positive = positiveOnFirst[k] == first;
                halves.Add((i, positive ? Positive(m) : Reflected(m), !positive));
            }
            foreach (var (_, mod, _) in halves) Budget(mod, add: true);
            foreach (var (i, mod, reflected) in halves) mods[i] = reflected ? Budget(mod, add: false) : mod;
            return target with { Mods = mods, Mirrored = true, RequiredLevel = null };

            ItemMod Budget(ItemMod mod, bool add)
            {
                if (catalog.Mods.ByHash(mod.Hash) is not { } def) return mod;
                var values = mod.Values.ToList();
                for (int j = 0; j < values.Count && j < def.Stats.Count; j++)
                {
                    var (pool, kinds) = LocalDefence(def.Stats[j].Id) is { } d ? (d.Pct ? pct : flat, d.Kinds) : (null, "");
                    if (pool is null) continue;
                    if (add) { if (values[j] > 0) foreach (char kind in kinds) pool[kind] += values[j]; continue; }
                    if (values[j] >= 0) continue;
                    long room = kinds.Min(kind => pool[kind]);
                    values[j] = (int)Math.Max(values[j], -Math.Max(0, room));
                    foreach (char kind in kinds) pool[kind] += values[j];
                }
                return add ? mod : mod with { Values = values };
            }
        }
        var one = Half(true);
        // The second half has the sockets but not the gems in them (they stay on the first).
        var two = Half(false) with { Sockets = [.. target.Sockets.Select(k => k with { Gem = null })] };
        int plus = positiveOnFirst.Count(p => p), minus = positiveOnFirst.Count - plus;
        Tell(player, $"Reflecting Mist: {percent}% modifier effect; this half {plus} positive / {minus} reflected, the other half the opposite.");
        _ = Task.Run(async () => { await Task.Delay(250); await GiveItem(player, two, "the other reflected half"); });
        return one;
    }

    // A stat's name: Program.Stats.Names is by key - 1.
    static string? StatName(uint stat) => stat >= 1 && stat <= Program.Stats.Names.Count ? Program.Stats.Names[(int)stat - 1] : null;

    /// <summary>The item's own defences a stat changes (a armour, e evasion, s energy shield, w ward), flat or %.</summary>
    static (bool Pct, string Kinds)? LocalDefence(uint stat) => stat switch
    {
        Stat.LocalBasePhysicalDamageReductionRating => (false, "a"),
        Stat.LocalBaseEvasionRating => (false, "e"),
        Stat.LocalEnergyShield => (false, "s"),
        Stat.LocalEvasionRatingAndEnergyShield => (false, "es"),
        Stat.LocalWard => (false, "w"),
        Stat.LocalPhysicalDamageReductionRatingPlusPct => (true, "a"),
        Stat.LocalEvasionRatingPlusPct => (true, "e"),
        Stat.LocalEnergyShieldPlusPct => (true, "s"),
        Stat.LocalArmourAndEnergyShieldPlusPct => (true, "as"),
        Stat.LocalArmourAndEvasionPlusPct => (true, "ae"),
        Stat.LocalEvasionAndEnergyShieldPlusPct => (true, "es"),
        Stat.LocalArmourAndEvasionAndEnergyShieldPlusPct => (true, "aes"),
        Stat.LocalWardPlusPct => (true, "w"),
        _ => null,
    };

    /// <summary>An item into the main inventory, or at the player's feet when it is full.</summary>
    async Task GiveItem(Player player, Item item, string what)
    {
        try
        {
            var inventory = Game.ItemsOf(player);
            byte row = PlayerInventories.MainInventory;
            if (Game.ShapeOf(item) is { } shape && inventory.FreeCell(row, shape.Width, shape.Height, Game.ShapeOf) is { } cell)
            {
                var placed = inventory.Add(row, cell.X, cell.Y, item);
                await Program.Send(player, InstanceItems.InventoryChange(PlayerInventories.InventoryId(row), inventory.NextRevision(row), [], [placed]),
                    $"excitingitems: {what} into the inventory");
                return;
            }
            var (x, y) = Program.PlayerOf(player).Position;
            var lying = new WorldItemObject(x, y, item);
            uint id = player.Instance.Ground.Add(lying, player);
            await Game.SendGroundItem(player, id, lying, $"excitingitems: 0x02bb create world item 0x{id:x} ({what})", justDropped: true);
            Tell(player, $"Your inventory is full: {what} is at your feet.");
        }
        catch (Exception e) { _log($"{what} not given: {e.Message}"); }
    }
}
