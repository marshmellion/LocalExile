using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GameServer.Core.Protocol.Instance;
using GameServer.Modding;

/// <summary>
/// Things the game removed, back for one's own play:
/// - Labyrinth enchantments, from the "Enchant" currency the client still has, with the helmet ones applied to their
///   skill through the client's own ActiveSkills stat conversions;
/// - alternate quality gems (3.22's lists, altquality.json) and the two Regrading Lenses. The client has no alternate
///   quality data, so the kind is kept in the gem's Intangibility byte, which is saved and never sent for a gem;
/// - Eternal Orbs: one makes an Imprint item of the target, as the game does (the Imprint component's data, reader
///   0x1424aefb0: a u8 of flags, bit 0 an item follows; the local server writes it). The imprinted item is the Imprint's
///   OriginalItem. A server without that keeps the imprint per account instead, restored by a chat command;
/// - a section of unnerfs: Cyclone's 3.7 stages.
/// What changes one skill, and a currency used on an item, comes through LocalHooks, three hand-overs this local copy of
/// the server adds (Modules/Core/Game/LocalHooks.cs). They are looked up by name: without them the mod still loads, with
/// only its drops. The fourth piece of that local copy is the Imprint component in GameServer.Core's item writer.
/// </summary>
/// <para>Merged into Exciting Items (2026-11-25): Exciting Items makes and loads this part with its settings' "legacy"
/// group; it registers under Exciting Items' id. Saved imprints keep their old key.</para>
public sealed class LegacyRestoredMod : IDisposable
{
    sealed record UnnerfChoices(bool Enabled, bool Cyclone, int CycloneStages, int CycloneRadiusPerStage);
    sealed record EternalChoices(bool Enabled, double Rarity);
    // LabyrinthOnly: Enchants drop only in the Labyrinth (its monsters and chests); LabyrinthChest: 1 in this many of the
    // Labyrinth's chests holds one.
    sealed record EnchantChoices(bool Enabled, double Weight, string Tier, bool SkillEffects, bool LabyrinthOnly = false, int LabyrinthChest = 4);
    sealed record GemChoices(bool Enabled, bool Anywhere, double DropRate, double UniqueChance, int[] Quality, double LensWeight, bool InfluenceLook);
    sealed record Choices(UnnerfChoices Unnerfs, EternalChoices EternalOrbs, EnchantChoices Enchants, GemChoices AltGems, bool Announce, bool TestCommands);

    sealed record AltSet(int S, int W, string T, Dictionary<string, int> Q)
    {
        public (uint Key, int Permille)[] Keys = [];
    }
    sealed record AltGem(string N, bool Sup, List<AltSet> Sets);
    sealed record Currencies(uint Eternal, uint Enchant, uint PrimeLens, uint SecondaryLens, int EternalLevel, ItemCatalog.BaseType? ImprintType,
        List<(int DropLevel, double Weight)> Table, Dictionary<uint, AltGem> Gems, List<(ItemCatalog.BaseType Type, AltGem Gem)> Droppable);
    sealed record Imprint(int Marker, string Name, string Item, string At);

    const string EternalId = "Metadata/Items/Currency/CurrencyImprintOrb";
    const string ImprintId = "Metadata/Items/Currency/CurrencyImprint";
    const string EnchantId = "Metadata/Items/Currency/CurrencyLabyrinthEnchant";
    const string PrimeLensId = "Metadata/Items/Currency/CurrencyRerollSkillQualityType";
    const string SecondaryLensId = "Metadata/Items/Currency/CurrencyRerollSupportQualityType";
    const string ImprintsKey = "legacyrestored.imprints";
    const int MostImprints = 20;
    static readonly string[] SetNames = ["Superior", "Anomalous", "Divergent", "Phantasmal"];
    static readonly Regex Placeholder = new(@"\{([^}/]+)(?:/([^}]+))?\}", RegexOptions.Compiled);
    static readonly JsonSerializerOptions Lenient = new() { PropertyNameCaseInsensitive = true };

    readonly ConditionalWeakTable<ItemCatalog, Currencies> _currencies = new();
    readonly ConditionalWeakTable<ModCatalog, List<ModCatalog.Mod>> _enchants = new();
    readonly ConditionalWeakTable<Player, ConcurrentQueue<string>> _pending = new();
    readonly ConditionalWeakTable<Player, ConcurrentQueue<Item>> _gifts = new();
    Lazy<Dictionary<string, (uint In, uint Out)[]>> _conversions = new(() => new());
    Dictionary<string, AltGem> _alt = new();
    Choices _choices = new(new(true, true, 6, 1), new(true, 10), new(true, 20, "eternal", true), new(true, true, 0.1, 0.1, [8, 20], 3, true), true, true);
    Action<string> _log = _ => { };
    string _hooks = "";

    public void Load(IModContext mod)
    {
        string id = mod.Manifest.Id;
        _choices = mod.Settings<Choices>();
        _log = mod.Log;
        string data = Path.Combine(mod.Folder, "altquality.json");
        if (File.Exists(data))
            _alt = JsonSerializer.Deserialize<Dictionary<string, AltGem>>(File.ReadAllText(data), Lenient) ?? new();
        else mod.Log($"altquality.json is missing from {mod.Folder}: no alternate quality gems");
        string folder = mod.Folder;
        _conversions = new(() => Conversions(Path.Combine(folder, "conversions.json")));

        // The local server's hand-overs, by name: a copy of the server without them still loads the mod.
        var hooks = typeof(Game).Assembly.GetType("LocalHooks");
        var found = new List<string>();
        if (hooks?.GetField("SkillStats")?.GetValue(null) is GameEvent<Action<Player, ushort, int, int, int, Item, Item, Dictionary<uint, int>>> skillStats)
        { skillStats.Add(id, SkillStats); found.Add("skill stats"); }
        if (hooks?.GetField("Supports")?.GetValue(null) is GameEvent<Func<Player, Item, IReadOnlyList<GemSupports.Applied>, IReadOnlyList<GemSupports.Applied>>> supports)
        { supports.Add(id, Supports); found.Add("supports"); }
        if (hooks?.GetField("CurrencyItemUse")?.GetValue(null) is GameEvent<Func<Player, Item, uint, Item?, Random, Item?>> currencyUse)
        { currencyUse.Add(id, CurrencyUse); found.Add("currency use"); }
        _hooks = found.Count == 3 ? "" : found.Count == 0
            ? "The server has no LocalHooks: only this mod's drops work. Run apply-local-hooks.cmd, then start the game again."
            : $"The server's LocalHooks has only {string.Join(", ", found)}: run apply-local-hooks.cmd, then start the game again.";
        mod.Log($"{_alt.Count} gems with alternate quality; hooks: {(found.Count == 0 ? "none" : string.Join(", ", found))}; Imprint item {(Item.Supported.Contains("Imprint") ? "written by the server" : "not written by this server (imprints are a chat list)")}");
        if (_hooks.Length > 0) mod.Log(_hooks);

        GameEvents.Drops.Add(id, Drops);
        // The play copy's chest hand-over (private-hooks), by name: Enchants in the Labyrinth's chests.
        if (typeof(GameEvents).GetField("ChestDrops")?.GetValue(null) is GameEvent<Action<Player, string, string, List<ItemDrops.Drop>>> chests)
            chests.Add(id, ChestDrops);
        GameEvents.AreaTick.Add(id, GameEvents.Always(async (player, _) =>
        {
            if (_gifts.TryGetValue(player, out var gifts))
                while (gifts.TryDequeue(out var gift)) await Hand(player, gift);
            if (!_pending.TryGetValue(player, out var queue)) return;
            while (queue.TryDequeue(out string? text)) await Program.PlayerOf(player).Tell(text);
        }));
        // For the QoL overlay's gem panel (qol: "gems" key): the client cannot show alternate quality, the overlay can.
        AppDomain.CurrentDomain.SetData(AltGemsKey, (Func<Player, string[]>)AltGemLines);
    }

    public void Dispose()
    {
        if (AppDomain.CurrentDomain.GetData(AltGemsKey) is not null) AppDomain.CurrentDomain.SetData(AltGemsKey, null);
        _pending.Clear();
        _gifts.Clear();
        _currencies.Clear();
        _enchants.Clear();
    }

    /// <summary>An item into the backpack, or at the who's feet when it is full.</summary>
    async Task Hand(Player player, Item item)
    {
        var who = Program.PlayerOf(player);
        var items = Game.ItemsOf(player);
        const byte row = PlayerInventories.MainInventory;
        if (Game.ShapeOf(item) is { } shape && items.FreeCell(row, shape.Width, shape.Height, Game.ShapeOf) is var (x, y))
        {
            var added = items.Add(row, x, y, item);
            await who.Send(InstanceItems.InventoryChange(PlayerInventories.InventoryId(row), items.NextRevision(row), new List<uint>(), new List<InventoryItem> { added }),
                $"0x00bf inventory: item {added.Id} from Legacy Restored");
            return;
        }
        var (px, py) = who.Position;
        await Drop(player, new WorldItemObject(px, py, item), "item from Legacy Restored");
        await who.Tell("* Your backpack is full: the item is at your feet.");
    }

    void Say(Player player, string text)
    {
        _log(text);
        if (_choices.Announce) _pending.GetValue(player, _ => new()).Enqueue("* " + text);
    }

    // ---- what the item catalogue knows of this mod's items ----

    Currencies For(ItemCatalog items) => _currencies.GetValue(items, catalog =>
    {
        ItemCatalog.BaseType? Type(string id) => catalog.Find(id) is { } t && t.Id == id && t.Supported ? t : null;
        var table = new List<(int, double)>();
        foreach (var (id, weight) in ItemDrops.Currency)
            if (Type(id) is { } t) table.Add((t.DropLevel, weight));
        var gems = new Dictionary<uint, AltGem>();
        var droppable = new List<(ItemCatalog.BaseType, AltGem)>();
        foreach (var (id, gem) in _alt)
        {
            if (Type(id) is not { } t || catalog.Gem(t) is not { } info) continue;
            foreach (var set in gem.Sets)
                set.Keys = [.. set.Q.Select(q => (Key: Program.Stats.Key(q.Key), q.Value)).Where(q => q.Key is not null).Select(q => (q.Key!.Value, q.Value))];
            gems[t.Hash] = gem;
            if (!info.IsVaalOrAwakened && t.DropLevel >= 1 && !string.IsNullOrEmpty(t.Name)) droppable.Add((t, gem));
        }
        return new(Type(EternalId)?.Hash ?? 0, Type(EnchantId)?.Hash ?? 0, Type(PrimeLensId)?.Hash ?? 0, Type(SecondaryLensId)?.Hash ?? 0,
            Type(EternalId)?.DropLevel ?? 40, Type(ImprintId), table, gems, droppable);
    });

    static AltSet? SetOf(AltGem gem, int kind) => gem.Sets.FirstOrDefault(s => s.S == kind);

    /// <summary>How a kind shows on the gem: the tooltip has no alternate quality line, but every item's Base component
    /// carries its influences, a gem's too. Anomalous is Shaper, Divergent Elder, Phantasmal Crusader. Only the look:
    /// the kind itself is the Intangibility byte.</summary>
    ItemInfluence Look(int kind) => !_choices.AltGems.InfluenceLook ? ItemInfluence.None : kind switch
    {
        1 => ItemInfluence.Shaper, 2 => ItemInfluence.Elder, 3 => ItemInfluence.Crusader, _ => ItemInfluence.None,
    };

    /// <summary>The alternate quality set a gem item has, if any.</summary>
    AltSet? AltOf(ItemCatalog items, Item gem) =>
        gem.Intangibility is >= 1 and <= 3 && gem.GemVariant == 0 && gem.Components.Contains("SkillGem")
            && For(items).Gems.TryGetValue(gem.TypeHash, out var alt) ? SetOf(alt, gem.Intangibility) : null;

    static readonly Regex Negative = new(@"-(\d+(?:\.\d+)?)% (more|increased)", RegexOptions.Compiled);

    /// <summary>The set's 3.22 text at a quality. The text is the one for a positive value: a negative one reads as
    /// "less" or "reduced".</summary>
    static string Render(AltSet set, int quality) => Negative.Replace(Text(set, quality), m => $"{m.Groups[1].Value}% {(m.Groups[2].Value == "more" ? "less" : "reduced")}");

    static string Text(AltSet set, int quality) => (Placeholder.Replace(set.T, m =>
    {
        double value = set.Q.GetValueOrDefault(m.Groups[1].Value) * quality / 1000;
        value = m.Groups[2].Value switch
        {
            "negate" => -value,
            "divide_by_one_hundred" or "divide_by_one_hundred_2dp" => value / 100,
            "divide_by_one_hundred_and_negate" => -value / 100,
            "locations_to_metres" => value / 10,
            "per_minute_to_per_second" or "per_minute_to_per_second_2dp" => value / 60,
            "milliseconds_to_seconds" or "milliseconds_to_seconds_1dp" or "milliseconds_to_seconds_2dp" or "milliseconds_to_seconds_2dp_if_required" => value / 1000,
            "multiply_by_four" => value * 4,
            _ => value,
        };
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }).Replace("\r", "").Replace("\n", "; "));

    string Describe(ItemCatalog items, Item gem, AltSet set) =>
        $"{SetNames[set.S]} {items.GemName(gem)} ({gem.Quality}% quality): {Render(set, Math.Max(1, (int)gem.Quality))}";

    // ---- drops ----

    static int Rolls(double chance, Random random)
    {
        if (!(chance > 0)) return 0;
        int whole = (int)Math.Min(20, Math.Floor(chance));
        return whole + (random.NextDouble() < chance - Math.Floor(chance) ? 1 : 0);
    }

    static bool InLabyrinth(Player player) => player.Layout.Id.Contains("Labyrinth", StringComparison.OrdinalIgnoreCase);

    void ChestDrops(Player player, string kind, string id, List<ItemDrops.Drop> drops)
    {
        var c = _choices.Enchants;
        if (!c.Enabled || kind != "chest" || !InLabyrinth(player) || Random.Shared.Next(Math.Max(1, c.LabyrinthChest)) != 0) return;
        if (Game.ItemsFor(player).Find(EnchantId) is { Supported: true } type)
        {
            drops.Add(new(ItemCatalog.Create(type), type));
            Say(player, $"{type.Name} found in the chest.");
        }
    }

    void Drops(DropRoll d)
    {
        var c = _choices;
        var player = d.Player;
        var monster = d.Monster;
        var items = Game.ItemsFor(player);
        var known = For(items);
        var random = Random.Shared;
        // The server's own numbers (ItemDrops): how many items a kill drops, 35% of them currency, 4% gems.
        double dropped = monster.Rarity switch { MonsterRarity.Magic => 1.5, MonsterRarity.Rare => 3, MonsterRarity.Unique => 5, _ => 0.35 }
            * Math.Max(0, d.Quantity);
        double table = known.Table.Where(t => t.DropLevel <= monster.Level).Sum(t => t.Weight);
        double perWeight = table > 0 ? dropped * 0.35 / table : 0;
        void Currency(uint hash, string id, double weight, int level)
        {
            if (hash == 0 || monster.Level < level) return;
            int count = Rolls(perWeight * weight, random);
            if (count == 0) return;
            d.Stacks.Add((id, count));
            Say(player, $"{items.ByHash(hash)?.Name ?? id} dropped.");
        }
        if (c.EternalOrbs.Enabled) Currency(known.Eternal, EternalId, 1 / Math.Max(0.1, c.EternalOrbs.Rarity), known.EternalLevel);
        if (c.Enchants.Enabled && (!c.Enchants.LabyrinthOnly || InLabyrinth(player))) Currency(known.Enchant, EnchantId, c.Enchants.Weight, 1);
        if (c.AltGems.Enabled)
        {
            Currency(known.PrimeLens, PrimeLensId, c.AltGems.LensWeight, 1);
            Currency(known.SecondaryLens, SecondaryLensId, c.AltGems.LensWeight, 1);
            double chance = (c.AltGems.Anywhere ? dropped * 0.04 * c.AltGems.DropRate : 0)
                + (monster.Rarity == MonsterRarity.Unique ? c.AltGems.UniqueChance : 0);
            for (int n = Rolls(chance, random); n > 0; n--)
                if (MakeGem(items, known, monster.Level, random) is { } gem)
                {
                    d.Made.Add(gem);
                    if (AltOf(items, gem) is { } set) Say(player, Describe(items, gem, set) + " dropped.");
                }
        }
    }

    Item? MakeGem(ItemCatalog items, Currencies known, int level, Random random, ItemCatalog.BaseType? wanted = null, int kind = 0, int quality = 0, int gemLevel = 1)
    {
        ItemCatalog.BaseType type;
        AltGem gem;
        if (wanted is not null)
        {
            if (!known.Gems.TryGetValue(wanted.Hash, out var its)) return null;
            (type, gem) = (wanted, its);
        }
        else
        {
            var pool = known.Droppable.Where(g => g.Type.DropLevel <= level).ToList();
            if (pool.Count == 0) return null;
            (type, gem) = pool[random.Next(pool.Count)];
        }
        var set = kind > 0 ? SetOf(gem, kind) : Weighted(gem.Sets, s => s.W, random);
        if (set is null) return null;
        int low = Math.Clamp(_choices.AltGems.Quality.ElementAtOrDefault(0), 1, 20), high = Math.Clamp(_choices.AltGems.Quality.ElementAtOrDefault(1), low, 20);
        return ItemCatalog.Create(type) with
        {
            Quality = (byte)(quality > 0 ? Math.Clamp(quality, 1, 23) : random.Next(low, high + 1)), Intangibility = (byte)set.S,
            Influences = Look(set.S),
            GemExperience = gemLevel > 1 && items.Gem(type) is { } info ? info.ExperienceFor(gemLevel) : 0,
        };
    }

    static T? Weighted<T>(IReadOnlyList<T> options, Func<T, double> weight, Random random) where T : class
    {
        double total = options.Sum(o => Math.Max(0, weight(o)));
        if (!(total > 0)) return options.Count > 0 ? options[random.Next(options.Count)] : null;
        double at = random.NextDouble() * total;
        foreach (var option in options)
            if ((at -= Math.Max(0, weight(option))) < 0) return option;
        return options[^1];
    }

    // ---- one skill's stats ----

    static void Merge(Dictionary<uint, int> stats, uint key, int value)
    {
        if (value == 0) return;
        stats[key] = stats.TryGetValue(key, out int had) && had != 0 && Program.Stats.Name(key)?.EndsWith("_final", StringComparison.Ordinal) == true
            ? (int)Math.Round(100 * ((1 + had / 100.0) * (1 + value / 100.0) - 1))
            : stats.GetValueOrDefault(key) + value;
    }

    /// <summary>Lays a gem's alternate quality over stats that hold its ordinary quality: the ordinary quality's part
    /// comes out (what the catalogue adds between quality 0 and this quality), the alternate set's goes in.</summary>
    static void Regrade(SkillCatalog skills, int effect, int level, int quality, AltSet set, Dictionary<uint, int> stats, bool onlyHeld)
    {
        var plain = new Dictionary<uint, int>();
        foreach (var (key, value) in skills.EffectStats(effect, level, 0)) plain[key] = value;
        foreach (var (key, value) in skills.EffectStats(effect, level, quality))
        {
            int ordinary = value - plain.GetValueOrDefault(key);
            if (ordinary != 0 && (!onlyHeld || stats.ContainsKey(key))) stats[key] = stats.GetValueOrDefault(key) - ordinary;
        }
        foreach (var (key, permille) in set.Keys) Merge(stats, key, permille * quality / 1000);
    }

    void SkillStats(Player player, ushort skill, int effect, int level, int quality, Item gem, Item host, Dictionary<uint, int> own)
    {
        var c = _choices;
        var skills = Game.SkillsFor(player);
        var items = Game.ItemsFor(player);
        string? effectId = skills.EffectId(effect);
        uint? speed = Program.Stats.Key("base_projectile_speed_+%");
        int speedBefore = speed is uint held ? own.GetValueOrDefault(held) : 0;
        if (c.AltGems.Enabled && quality > 0 && AltOf(items, gem) is { } set)
            Regrade(skills, effect, level, quality, set, own, onlyHeld: false);

        // A helmet enchantment names its skill by a stat of that skill alone ("fireball_damage_+%"); the client's
        // ActiveSkills row turns each into the general stat ("damage_+%") for that skill only.
        if (c.Enchants.Enabled && c.Enchants.SkillEffects && effectId is not null && _conversions.Value.TryGetValue(effectId, out var conversions))
            foreach (var (_, worn) in Game.ItemsOf(player).EquipmentSnapshot())
                foreach (var mod in worn.Item.Mods)
                {
                    if (mod.Section != ModSection.Enchant || items.Mods.ByHash(mod.Hash) is not { } row) continue;
                    for (int i = 0; i < row.Stats.Count && i < mod.Values.Count; i++)
                    {
                        if (mod.Values[i] == 0) continue;
                        uint key = row.Stats[i].Id;
                        foreach (var (from, to) in conversions)
                            if (from == key) Merge(own, to, mod.Values[i]);
                    }
                }

        // The client draws the who's own projectiles from the total, projectile_speed_+%, which the server publishes
        // for a skill from the character's sources alone: what this mod added to the skill's base input goes into it.
        if (speed is uint baseSpeed && Program.Stats.Key("projectile_speed_+%") is uint totalSpeed
            && own.GetValueOrDefault(baseSpeed) - speedBefore is int faster and not 0)
            own[totalSpeed] = own.GetValueOrDefault(totalSpeed) + faster;

        // Cyclone, 3.7.0: it gains stages while channelled, 3 at gem level 1 and 6 at level 20, each +1 to melee range,
        // and melee range was its radius. The stages are taken as full: they filled within the first second.
        if (c.Unnerfs.Enabled && c.Unnerfs.Cyclone && effectId == "Cyclone" && Program.Stats.Key("active_skill_base_area_of_effect_radius") is uint radius)
        {
            int most = Math.Max(1, c.Unnerfs.CycloneStages), least = (most + 1) / 2;
            int stages = Math.Clamp(least + (Math.Clamp(level, 1, 20) - 1) * (most - least) / 19, 1, most);
            own[radius] = (own.GetValueOrDefault(radius) is > 0 and int had ? had : 16) + stages * Math.Max(1, c.Unnerfs.CycloneRadiusPerStage);
        }
    }

    IReadOnlyList<GemSupports.Applied> Supports(Player player, Item host, IReadOnlyList<GemSupports.Applied> supports)
    {
        if (!_choices.AltGems.Enabled) return supports;
        List<GemSupports.Applied>? changed = null;
        var items = Game.ItemsFor(player);
        for (int i = 0; i < supports.Count; i++)
        {
            var support = supports[i];
            int socket = support.Gem.Socket;
            if (support.Gem.Quality <= 0 || socket < 0 || socket >= host.Sockets.Count || host.Sockets[socket].Gem is not { } gem) continue;
            // The socket is this item's own, not the other hand's: the gem in it is the support that was applied.
            if (AltOf(items, gem) is not { } set || items.ByHash(gem.TypeHash) is not { } type || items.Gem(type) is not { IsSupport: true } info
                || info.Variant(gem.GemVariant).Effect != support.Gem.Effect) continue;
            var stats = new Dictionary<uint, int>();
            foreach (var (key, value) in support.Stats) stats[key] = stats.GetValueOrDefault(key) + value;
            Regrade(Game.SkillsFor(player), support.Gem.Effect, support.Gem.Level, support.Gem.Quality, set, stats, onlyHeld: true);
            changed ??= new(supports);
            changed[i] = support with { Stats = new List<(uint Key, int Value)>(stats.Select(s => (s.Key, s.Value))) };
        }
        return changed ?? supports;
    }

    /// <summary>Per GrantedEffects id, the stats of that skill alone and the general stat each one is for that skill:
    /// the client's ActiveSkills rows, then 3.22's (conversions.json) for the pairs the client has dropped since.</summary>
    static Dictionary<string, (uint In, uint Out)[]> Conversions(string older)
    {
        var pairs = new Dictionary<string, Dictionary<uint, uint>>(StringComparer.Ordinal);
        if (GameServer.Core.ClientTables.Has("ActiveSkills"))
            foreach (var row in GameServer.Core.ClientTables.Table("ActiveSkills"))
            {
                if (row.TextOrNull("GrantedEffect") is not { Length: > 0 } effect) continue;
                int[] from = row.Ints("Input_StatKeys"), to = row.Ints("Output_StatKeys");
                if (from.Length == 0 || from.Length != to.Length) continue;
                if (!pairs.TryGetValue(effect, out var known)) pairs[effect] = known = new();
                // A Stats row is its stat key less one (SkillCatalog.EffectStats).
                for (int i = 0; i < from.Length; i++) known.TryAdd((uint)from[i] + 1, (uint)to[i] + 1);
            }
        if (File.Exists(older) && JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(older)) is { } file)
            foreach (var (effect, stats) in file)
            {
                if (!pairs.TryGetValue(effect, out var known)) pairs[effect] = known = new();
                foreach (var (from, to) in stats)
                    if (Program.Stats.Key(from) is uint input && Program.Stats.Key(to) is uint output) known.TryAdd(input, output);
            }
        return pairs.ToDictionary(p => p.Key, p => p.Value.Select(s => (s.Key, s.Value)).ToArray(), StringComparer.Ordinal);
    }

    // ---- currency used on an item ----

    Item? CurrencyUse(Player player, Item target, uint currency, Item? used, Random random)
    {
        if (currency == 0) return null;
        var c = _choices;
        var items = Game.ItemsFor(player);
        var known = For(items);
        uint imprint = known.ImprintType?.Hash ?? 0;
        if (currency != known.Enchant && currency != known.Eternal && currency != known.PrimeLens && currency != known.SecondaryLens && currency != imprint) return null;
        // A Hinekora preview asks for the result without using the currency: these are not previewed.
        if (target.Foreseeing) return null;
        string? why;
        Item? result;
        if (currency == imprint) result = RestoreFrom(player, items, target, used, out why);
        else if (currency == known.Enchant) result = c.Enchants.Enabled ? Enchant(player, items, target, random, out why) : Off(out why);
        else if (currency == known.Eternal) result = c.EternalOrbs.Enabled ? MakeImprint(player, items, target, random, out why) : Off(out why);
        else result = c.AltGems.Enabled ? Regrade(player, items, target, currency == known.SecondaryLens, random, out why) : Off(out why);
        if (result is null && why is not null) Say(player, $"{items.ByHash(currency)?.Name}: {why}");
        return result;
    }

    static Item? Off(out string? why) { why = "turned off in Legacy Restored's settings."; return null; }

    List<ModCatalog.Mod> EnchantPool(ModCatalog mods) => _enchants.GetValue(mods, catalog =>
    {
        var pool = new List<ModCatalog.Mod>();
        for (int row = 0; catalog.Row(row) is { } mod; row++)
            if (mod.GenerationType == 10 && mod.Domain == 1 && mod.Stats.Count > 0 && mod.SpawnWeights.Any(w => w.Weight > 0)) pool.Add(mod);
        return pool;
    });

    Item? Enchant(Player player, ItemCatalog items, Item target, Random random, out string? why)
    {
        why = "use it on a helmet, gloves, boots or a belt.";
        if (!target.Components.Contains("Mods") || items.ByHash(target.TypeHash) is not { } type) return null;
        var fitting = EnchantPool(items.Mods).Where(m => m.WeightFor(type.Tags) > 0).ToList();
        if (fitting.Count == 0) return null;
        int wanted = _choices.Enchants.Tier.ToLowerInvariant() switch { "normal" => 32, "cruel" => 53, "merciless" => 66, _ => 75 };
        var levels = fitting.Select(m => m.Level).Distinct().OrderBy(l => l).ToList();
        int level = levels.Where(l => l <= wanted).DefaultIfEmpty(levels[0]).Max();
        var held = target.Mods.Where(m => m.Section == ModSection.Enchant).Select(m => m.Hash).ToHashSet();
        var pool = fitting.Where(m => m.Level == level && !held.Contains(m.Hash)).ToList();
        if (pool.Count == 0) { why = "it has the only enchantment it can take."; return null; }
        var chosen = Weighted(pool, m => m.WeightFor(type.Tags), random)!;
        var rolled = chosen.Roll(ModSection.Enchant, random);
        // An enchantment does not raise the level the item needs, as a modifier of its level would.
        int needed = ItemModStats.LevelRequirement(target, type.DropLevel, items.Mods);
        var mods = new List<ItemMod>(target.Mods.Where(m => m.Section != ModSection.Enchant)) { rolled };
        why = null;
        Say(player, $"{type.Name} enchanted: {(chosen.Name.Length > 0 ? chosen.Name.Replace("Enchantment ", "") : chosen.Id)} ({string.Join(", ", chosen.Stats.Select((s, i) => $"{s.Id} {(i < rolled.Values.Count ? rolled.Values[i] : 0)}"))})");
        return target with { Mods = mods, RequiredLevel = needed };
    }

    Item? Regrade(Player player, ItemCatalog items, Item target, bool support, Random random, out string? why)
    {
        why = support ? "use it on a support gem with quality." : "use it on a skill gem with quality.";
        if (!target.Components.Contains("SkillGem") || !For(items).Gems.TryGetValue(target.TypeHash, out var alt) || alt.Sup != support) return null;
        if (target.GemVariant != 0) { why = "a transfigured gem has no other quality types."; return null; }
        int current = target.Intangibility is >= 1 and <= 3 ? target.Intangibility : 0;
        var set = Weighted(alt.Sets.Where(s => s.S != current).ToList(), s => s.W, random);
        if (set is null) { why = "this gem has no other quality type."; return null; }
        why = null;
        var changed = target with { Intangibility = (byte)set.S, Influences = Look(set.S) };
        Say(player, "Regraded: " + Describe(items, changed, set));
        return changed;
    }

    // ---- imprints ----

    static List<Imprint> Imprints(Player player)
    {
        try { return JsonSerializer.Deserialize<List<Imprint>>(Program.WorldOf(player).AccountData.Get(ImprintsKey) ?? "[]", Lenient) ?? []; }
        catch (JsonException) { return []; }
    }

    Item? MakeImprint(Player player, ItemCatalog items, Item target, Random random, out string? why)
    {
        why = "use it on a piece of equipment, a jewel or a map.";
        if (!target.Components.Contains("Mods") || target.Components.Contains("Charges") || target.Components.Contains("Stack")
            || items.ByHash(target.TypeHash) is not { } type) return null;
        if (target.FracturedCount > 0) { why = "an imprint cannot be made of a fractured item."; return null; }
        // The item carries the imprint's number in Charges, which only flasks use and which is saved with every item:
        // it is how the imprint finds "this specific item" again after the item has been crafted on.
        int marker = target.Charges != 0 ? target.Charges : random.Next(1, int.MaxValue);
        if (For(items).ImprintType is { } imprintType)
        {
            // As the game does it: an Imprint item that holds the item as it is now.
            why = null;
            _gifts.GetValue(player, _ => new()).Enqueue(ItemCatalog.Create(imprintType) with { StackSize = 1, OriginalItem = target with { Charges = marker } });
            Say(player, $"Imprint made of your {Named(target, type)}. Right-click the Imprint, then the item, to restore it.");
            return target with { Charges = marker };
        }
        string saved = JsonSerializer.Serialize(CharacterSaves.ToSaved(target with { Charges = 0 }, items.Mods), CharacterSaves.Opts);
        string name = Named(target, type);
        bool full = false;
        Program.WorldOf(player).AccountData.Change(ImprintsKey, old =>
        {
            List<Imprint> list;
            try { list = JsonSerializer.Deserialize<List<Imprint>>(old ?? "[]", Lenient) ?? []; }
            catch (JsonException) { list = []; }
            if (list.Count >= MostImprints) { full = true; return old; }
            list.Add(new(marker, name, saved, DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
            return JsonSerializer.Serialize(list);
        });
        if (full) { why = $"you have {MostImprints} imprints already: /exciting forget <n> one first."; return null; }
        why = null;
        Say(player, $"Imprint made of your {name}. /exciting imprints lists them; /exciting restore <n> puts one back.");
        return target with { Charges = marker };
    }

    static string Named(Item item, ItemCatalog.BaseType type) => $"{item.Rarity} {type.Name}, item level {item.ItemLevel}, {item.Mods.Count} modifiers";

    /// <summary>An Imprint used on an item: the item becomes the one the Imprint holds, when it is that item.</summary>
    Item? RestoreFrom(Player player, ItemCatalog items, Item target, Item? used, out string? why)
    {
        why = "this imprint holds nothing.";
        if (used?.OriginalItem is not { } kept) return null;
        why = "it restores only the item it was made of.";
        if (kept.TypeHash != target.TypeHash || kept.Charges == 0 || kept.Charges != target.Charges) return null;
        if (target.FracturedCount > 0) { why = "it cannot be applied to a fractured item."; return null; }
        why = null;
        Say(player, $"Restored your {(items.ByHash(kept.TypeHash) is { } type ? Named(kept, type) : "item")} from the Imprint.");
        return kept;
    }

    async Task Restore(Player player, int number, bool forget)
    {
        var who = Program.PlayerOf(player);
        var list = Imprints(player);
        if (number < 1 || number > list.Count) { await who.Tell($"Exciting Items: no imprint {number} (/exciting imprints)."); return; }
        var imprint = list[number - 1];
        void Drop() => Program.WorldOf(player).AccountData.Change(ImprintsKey, old =>
        {
            var now = JsonSerializer.Deserialize<List<Imprint>>(old ?? "[]", Lenient) ?? [];
            int at = now.FindIndex(i => i.Marker == imprint.Marker && i.Item == imprint.Item);
            if (at >= 0) now.RemoveAt(at);
            return JsonSerializer.Serialize(now);
        });
        if (forget) { Drop(); await who.Tell($"Exciting Items: imprint {number} ({imprint.Name}) forgotten."); return; }

        var catalog = Game.ItemsFor(player);
        var items = Game.ItemsOf(player);
        const byte row = PlayerInventories.MainInventory;
        var found = items.Items(row).FirstOrDefault(i => i.Item.Charges == imprint.Marker && i.Item.Components.Contains("Mods") && !i.Item.Components.Contains("Charges"));
        if (found is null) { await who.Tell("Exciting Items: the imprinted item is not in your backpack (take it off or out of the stash first)."); return; }
        Item restored;
        try
        {
            var saved = JsonSerializer.Deserialize<CharacterSaves.SavedItem>(imprint.Item, CharacterSaves.Opts) ?? throw new JsonException("empty");
            restored = CharacterSaves.FromSaved(saved, catalog);
        }
        catch (Exception e) when (e is JsonException or UnknownSavedItemException)
        {
            await who.Tell($"Exciting Items: imprint {number} no longer reads ({e.Message}); /exciting forget {number} removes it.");
            return;
        }
        if (restored.TypeHash != found.Item.TypeHash) { await who.Tell("Exciting Items: that imprint is of another item."); return; }
        // Another imprint of the same item keeps finding it.
        bool others = list.Where((other, at) => at != number - 1).Any(other => other.Marker == imprint.Marker);
        var changed = items.Change(row, found.Id, _ => restored with { Charges = others ? imprint.Marker : 0 });
        if (changed is null) { await who.Tell("Exciting Items: the item could not be changed."); return; }
        Drop();
        await who.Send(InstanceItems.InventoryChange(PlayerInventories.InventoryId(row), items.NextRevision(row), new List<uint> { found.Id }, new List<InventoryItem> { changed }),
            $"0x00bf inventory: item {found.Id} restored from an imprint");
        _log($"imprint restored: {imprint.Name}");
        await who.Tell($"Exciting Items: your {imprint.Name} is restored.");
    }

    // ---- /exciting legacy | gems | imprints | restore | forget | use | give ----

    const string AltGemsKey = "localexile.altgems";

    /// <summary>Every alternate quality gem the character carries or has socketed, with its 3.22 text at its quality:
    /// "where: name (quality): text".</summary>
    string[] AltGemLines(Player player)
    {
        try
        {
            var catalog = Game.ItemsFor(player);
            var items = Game.ItemsOf(player);
            var lines = new List<string>();
            foreach (var held in items.Items(PlayerInventories.MainInventory).ToList())
                if (AltOf(catalog, held.Item) is { } set) lines.Add("Backpack: " + Describe(catalog, held.Item, set));
            foreach (var (_, _, _, gem) in items.SocketedGemPlaces())
                if (AltOf(catalog, gem) is { } set) lines.Add("Socketed: " + Describe(catalog, gem, set));
            return [.. lines];
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { return []; }
    }

    /// <summary>The legacy part's commands, under /exciting (ExcitingItems.cs passes them on): /exciting legacy,
    /// gems, imprints, restore, forget, use, give.</summary>
    public async Task Command(Player player, string[] args)
    {
        var who = Program.PlayerOf(player);
        var c = _choices;
        var catalog = Game.ItemsFor(player);
        var known = For(catalog);
        string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        switch (verb)
        {
            case "gems":
            {
                var items = Game.ItemsOf(player);
                var lines = new List<string>();
                // A gem from before the look was added, or after the setting changed, gets its look here.
                var relooked = new List<InventoryItem>();
                foreach (var held in items.Items(PlayerInventories.MainInventory).ToList())
                    if (AltOf(catalog, held.Item) is { } set)
                    {
                        lines.Add("backpack: " + Describe(catalog, held.Item, set));
                        if (held.Item.Influences != Look(set.S)
                            && items.Change(PlayerInventories.MainInventory, held.Id, gem => gem with { Influences = Look(set.S) }) is { } now) relooked.Add(now);
                    }
                if (relooked.Count > 0)
                    await who.Send(InstanceItems.InventoryChange(PlayerInventories.InventoryId(PlayerInventories.MainInventory), items.NextRevision(PlayerInventories.MainInventory),
                        new List<uint>(relooked.Select(i => i.Id)), relooked), "0x00bf inventory: alternate quality gems shown by influence");
                foreach (var (_, _, _, gem) in items.SocketedGemPlaces())
                    if (AltOf(catalog, gem) is { } set) lines.Add("socketed: " + Describe(catalog, gem, set));
                await who.Tell(lines.Count == 0 ? "Exciting Items: no alternate quality gems in your backpack or sockets." : $"Exciting Items: {lines.Count} alternate quality gem(s):");
                foreach (string line in lines.Take(40)) await who.Tell("  " + line);
                return;
            }
            case "imprints":
            {
                var list = Imprints(player);
                await who.Tell(list.Count == 0 ? (known.ImprintType is null ? "Exciting Items: no imprints. Use an Eternal Orb on an item to make one."
                    : "Exciting Items: no imprints kept in this list: an Eternal Orb now makes an Imprint item in your backpack.") : $"Exciting Items: {list.Count} imprint(s); /exciting restore <n> with the item in your backpack:");
                for (int i = 0; i < list.Count; i++) await who.Tell($"  {i + 1}. {list[i].Name} ({list[i].At})");
                return;
            }
            case "restore" or "forget":
                if (args.Length < 2 || !int.TryParse(args[1], out int number)) { await who.Tell($"Exciting Items: /exciting {verb} <n> (the number in /exciting imprints)."); return; }
                await Restore(player, number, verb == "forget");
                return;
            case "use":
                await Use(player, args.Length > 1 ? args[1].ToLowerInvariant() : "");
                return;
            case "give" when c.TestCommands:
                await Give(player, args);
                return;
        }
        await who.Tell($"Exciting Items: unnerfs {(c.Unnerfs.Enabled ? "on" : "off")}{(c.Unnerfs.Enabled && c.Unnerfs.Cyclone ? " (Cyclone)" : "")}, " +
            $"Eternal Orbs {(c.EternalOrbs.Enabled ? $"on ({c.EternalOrbs.Rarity:0.#}x rarer than a Divine Orb)" : "off")}, " +
            $"enchantments {(c.Enchants.Enabled ? $"on ({c.Enchants.Tier})" : "off")}, " +
            $"alternate quality gems {(c.AltGems.Enabled ? $"on ({known.Gems.Count} gems)" : "off")}.");
        if (_hooks.Length > 0) await who.Tell("Exciting Items: " + _hooks);
        await who.Tell("  /exciting gems | imprints | restore <n> | forget <n> | use enchant|eternal|lens|lens2 (on the item in your backpack's top-left cell)" +
            (c.TestCommands ? " | give eternal|enchant|lens|lens2 [n] | give gem anomalous|divergent|phantasmal <gem name> [level]" : ""));
    }

    uint CurrencyNamed(Currencies known, string name) => name switch
    {
        "enchant" => known.Enchant, "eternal" => known.Eternal, "lens" => known.PrimeLens, "lens2" => known.SecondaryLens,
        "imprint" => known.ImprintType?.Hash ?? 0, _ => 0,
    };

    /// <summary>The same as right-clicking the currency and clicking the item, for a client that will not do that with
    /// a currency it no longer uses: the currency comes from the backpack, the item is the one in its top-left cell.</summary>
    async Task Use(Player player, string name)
    {
        var who = Program.PlayerOf(player);
        var known = For(Game.ItemsFor(player));
        uint hash = CurrencyNamed(known, name);
        if (hash == 0) { await who.Tell("Exciting Items: /exciting use enchant|eternal|imprint|lens|lens2."); return; }
        var items = Game.ItemsOf(player);
        const byte row = PlayerInventories.MainInventory;
        var used = items.Items(row).FirstOrDefault(i => i.Item.TypeHash == hash);
        var target = items.Items(row).FirstOrDefault(i => i.X == 0 && i.Y == 0);
        if (used is null) { await who.Tell($"Exciting Items: no {Game.ItemsFor(player).ByHash(hash)?.Name} in your backpack."); return; }
        if (target is null || target.Id == used.Id) { await who.Tell("Exciting Items: put the item to change in the top-left cell of your backpack."); return; }
        if (target.Item.Corrupted || target.Item.Mirrored) { await who.Tell("Exciting Items: a corrupted or mirrored item cannot be changed."); return; }
        if (CurrencyUse(player, target.Item, hash, used.Item, Random.Shared) is not { } result) return;
        if (items.Change(row, target.Id, _ => result) is not { } changed) return;
        var taken = items.ConsumeOne(row, used.Id);
        var now = new List<InventoryItem> { changed };
        if (taken?.Left is { } left) now.Add(left);
        await who.Send(InstanceItems.InventoryChange(PlayerInventories.InventoryId(row), items.NextRevision(row), new List<uint> { used.Id, target.Id }, now),
            $"0x00bf inventory: item {target.Id} changed by /exciting use {name}");
    }

    /// <summary>An item on the ground at the player's feet, sent as a drop is (since LocalExile 0.6 the ground is not
    /// sent to the client by itself).</summary>
    static async Task Drop(Player player, WorldItemObject lying, string what)
    {
        uint id = player.Instance.Ground.Add(lying, player);
        await Game.SendGroundItem(player, id, lying, $"legacy: 0x02bb create world item 0x{id:x} ({what})", justDropped: true);
    }

    async Task Give(Player player, string[] args)
    {
        var who = Program.PlayerOf(player);
        var catalog = Game.ItemsFor(player);
        var known = For(catalog);
        string what = args.Length > 1 ? args[1].ToLowerInvariant() : "";
        var (x, y) = who.Position;
        if (what == "gem")
        {
            int kind = args.Length > 2 ? Array.FindIndex(SetNames, n => n.Equals(args[2], StringComparison.OrdinalIgnoreCase)) : -1;
            int gemLevel = args.Length > 4 && int.TryParse(args[^1], out int wantedLevel) ? Math.Clamp(wantedLevel, 1, 20) : 1;
            string name = string.Join(' ', args.Skip(3).Take(args.Length - 3 - (args.Length > 4 && int.TryParse(args[^1], out _) ? 1 : 0)));
            var type = known.Droppable.Concat(known.Gems.Select(g => (Type: catalog.ByHash(g.Key)!, Gem: g.Value)))
                .FirstOrDefault(g => g.Type is not null && g.Type.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Type;
            if (kind < 1 || type is null || MakeGem(catalog, known, 100, Random.Shared, type, kind, 20, gemLevel) is not { } gem)
            { await who.Tell("Exciting Items: /exciting give gem anomalous|divergent|phantasmal <gem name> [level] (a gem that has that quality type)."); return; }
            await Drop(player, new WorldItemObject(x, y, gem), "alternate quality gem");
            if (AltOf(catalog, gem) is { } set) await who.Tell("Exciting Items: " + Describe(catalog, gem, set));
            return;
        }
        uint hash = CurrencyNamed(known, what);
        if (what == "imprint" || hash == 0 || catalog.ByHash(hash) is not { } currency) { await who.Tell("Exciting Items: /exciting give eternal|enchant|lens|lens2 [n] | give gem <type> <gem name> [level]."); return; }
        int count = args.Length > 2 && int.TryParse(args[2], out int n) ? Math.Clamp(n, 1, currency.MaxStack) : 1;
        await Drop(player, new WorldItemObject(x, y, ItemCatalog.Create(currency) with { StackSize = (ushort)count }), currency.Name);
        await who.Tell($"Exciting Items: {count} {currency.Name} at your feet.");
    }
}

/// <summary>A part of a mod with a group of the mod's settings as its own: Legacy Restored's code reads its settings as it
/// did when it was a mod of its own.</summary>
sealed class SubContext(IModContext parent, string group) : IModContext
{
    public ModManifest Manifest => parent.Manifest;
    public IModuleLifetime Lifetime => parent.Lifetime;
    public string Folder => parent.Folder;
    public void Log(string message) => parent.Log(message);
    public T Settings<T>() => ModSettings.Bound<T>(parent.Settings<JsonObject>()[group] as JsonObject ?? new JsonObject());
    public void SaveSettings<T>(T settings) => throw new NotSupportedException("a part's settings are saved with its mod's");
    // LocalExile 0.6.1: league rules belong to a mod as a whole, not to one of its parts.
    public void ProvideLeagueRules(Func<LeagueRules> rules) => parent.ProvideLeagueRules(rules);
}
