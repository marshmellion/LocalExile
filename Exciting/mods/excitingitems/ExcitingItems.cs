using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GameServer.Core.Protocol.Instance;
using GameServer.Modding;

/// <summary>
/// Rare chances on magic, rare and unique drops, each its own and all able to land on one item:
/// - a unique item's modifier as an extra implicit (one, two or three; which ones by the chosen mode);
/// - an empowered modifier: one implicit or explicit modifier's values times 1.1 to 2;
/// - an extra modifier, one more than the item could have, and every modifier of its side (prefixes or suffixes)
///   fractured.
/// There is no hook on a rolled drop, so the mod watches the instance's ground for new items on the who tick, takes
/// a chosen one off the ground and puts the changed item down in its place; the Gateway's ground sync removes the old
/// object from the client and creates the new one. The game's own refusals (no fracturing of influenced or synthesised
/// items) are its crafting rules, not a drop's: an item gets these whatever it is.
/// </summary>
public sealed partial class ExcitingItemsMod : IMod, IDisposable
{
    sealed record ImplicitChoices(int Chance, int ChanceTwo, int ChanceThree, string Mode, bool RarityWeights, string[] Rarities);
    sealed record EmpoweredChoices(int Chance, int[] Percent, string[] Rarities, bool OtherItems = true, int OtherChance = 1000,
        int GemChance = 20000, int GemLevels = 1, int GemQualityMax = 30);
    sealed record ExtraChoices(int Chance, string[] Rarities, bool InfluencedMods = true);
    // What a Radiant Orb gives, each a weight (0: never): one, two or three unique implicits, an empowered modifier, an
    // extra fractured modifier. AnyItem: also on corrupted, mirrored, fractured, synthesised and influenced items.
    sealed record OrbChoices(bool Enabled = true, int DropChance = 400, bool AnyItem = true, int OneImplicit = 300,
        int TwoImplicits = 15, int ThreeImplicits = 1, int Empowered = 300, int Extra = 300, int Custom = 200, int Crucible = 150);
    sealed record Choices(ImplicitChoices Implicits, EmpoweredChoices Empowered, ExtraChoices ExtraModifier, string[] Blacklist,
        bool Mark, bool ShowInFilters, bool Announce, string FilterFolder, OrbChoices? RadiantOrb = null,
        LimitBreakChoices? LimitBreakOrb = null, KrangleChoices? KrangleOrb = null, LegacyModChoices? LegacyMods = null,
        RecombinatorChoices? Recombinators = null, DefiledChoices? DefiledOrb = null, MistChoices? ReflectingMist = null,
        JackpotChoices? Jackpot = null, CustomImplicitChoices? CustomImplicits = null, LegacyUniqueChoices? LegacyUniques = null,
        LegacyCurrencyChoices? LegacyCurrencies = null, CrucibleChoices? Crucible = null, IdolChoices? Idols = null, MirageChoices? Mirage = null,
        UnobtainableChoices? Unobtainable = null, CardRateChoices? CardRate = null, AscendantJewelChoices? AscendantJewels = null);

    const string FilterStart = "# >>> excitingitems (LocalExile mod): always show exciting items >>>";
    const string FilterEnd = "# <<< excitingitems <<<";
    const string OwnFilter = "LocalExile Exciting Items.filter";
    // What Unique Implicits, this mod's old name, put in the filters: taken out again.
    const string OldStart = "# >>> uniqueimplicits (LocalExile mod): always show items with a unique implicit >>>";
    const string OldEnd = "# <<< uniqueimplicits <<<";
    const string OldOwnFilter = "LocalExile Unique Implicits.filter";
    // The marker modifier the content pack adds (content/marker.json): a modifier of our own, no stats, put on an exciting
    // item as an enchantment, the list the server sends also for an unidentified item (explicit mods only come once it
    // is identified), so filters match it by name: HasEnchantment "Exciting". Without the patched data (the pack not
    // applied) the item is marked synthesised instead, as before.
    const string MarkerId = "LocalExileExcitingMarker", MarkerName = "Exciting";

    static ModCatalog.Mod? Marker(ItemCatalog catalog) => catalog.Mods.ById(MarkerId);

    static string[] FilterRule(bool marker) =>
    [
        "Show",
        marker ? $"    HasEnchantment \"{MarkerName}\"" : "    SynthesisedItem True",
        "    SetFontSize 45",
        "    SetTextColor 255 255 255 255",
        "    SetBorderColor 255 215 0 255",
        "    SetBackgroundColor 110 0 150 255",
        "    PlayAlertSound 6 300",
        "    PlayEffect Yellow",
        "    MinimapIcon 0 Yellow Star",
    ];

    // The mark: the marker enchantment when the data has it, else the synthesised flag.
    static Item Marked(ItemCatalog catalog, Item item)
    {
        if (Marker(catalog) is not { } marker) return item with { Synthesised = true };
        if (item.Mods.Any(m => m.Hash == marker.Hash)) return item;
        return item with { Mods = [.. item.Mods, new ItemMod(ModSection.Enchant, marker.Hash, [])] };
    }

    // A modifier that names a slot or a kind of item, or changes the item it is on, belongs on the kinds of item the
    // uniques that have it are. The others can go on any gear.
    static readonly string[] SlotWords =
    [
        "local_", "socketed", "ring", "amulet", "belt", "quiver", "shield", "off_hand", "main_hand", "weapon", "boots",
        "gloves", "helmet", "body_armour", "flask", "jewel", "wand", "bow", "staff", "sword", "axe", "mace", "claw",
        "dagger", "sceptre", "one_hand", "two_hand", "dual_wield", "wielding", "while_holding", "unarmed", "this_item",
        "item_", "radius", "passive",
    ];
    static readonly HashSet<string> JewelClasses = new(StringComparer.OrdinalIgnoreCase) { "Jewel", "AbyssJewel" };
    static readonly HashSet<string> NotGear = new(StringComparer.OrdinalIgnoreCase)
        { "Jewel", "AbyssJewel", "AnimalCharm", "Map", "MapFragment", "Relic", "SanctumRelic", "Tincture" };

    [Flags] enum Theme { None = 0, Offence = 1, Defence = 2, Speed = 4, Ailment = 8, Scaling = 16, Utility = 32 }

    sealed class Source(ModCatalog.Mod mod, bool bound, Dictionary<int, (int Min, int Max)> ranges, string unique, Theme theme)
    {
        public ModCatalog.Mod Mod => mod;
        public bool Bound => bound;
        public Dictionary<int, (int Min, int Max)> Ranges => ranges;
        public string Unique => unique;
        public Theme Theme => theme;
        public HashSet<string> Classes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Jewel => Classes.Overlaps(JewelClasses);
        public bool Gear => Classes.Any(c => !NotGear.Contains(c));
        /// <summary>The drop weight of the most common unique that has the modifier (0 to 1; Mageblood about 0.001).</summary>
        public double Weight;
    }
    sealed class Watch { public uint Next; public bool Started; public readonly HashSet<uint> Ours = []; }
    sealed record Forced(int Implicits, bool Empowered, bool Extra, bool Custom = false, bool Crucible = false);
    int _nextJackpot;
    sealed record JackpotChoices(bool Enabled = true, int Two = 20000, int Three = 200000, int Four = 2000000);

    readonly ConditionalWeakTable<ItemCatalog, List<Source>> _pools = new();
    readonly ConditionalWeakTable<InstanceGround, Watch> _watches = new();
    Choices _choices = new(new(1000, 100000, 1000000, "wild", true, ["magic", "rare", "unique"]), new(500, [10, 100], ["magic", "rare", "unique"]),
        new(1000, ["magic", "rare", "unique"]), [], true, true, true, "");
    Forced? _forced;
    LegacyRestoredMod? _legacy;
    LegacyCurrencies? _legacyCurrencies = null;

    public void Dispose() { _legacy?.Dispose(); _legacy = null; _customRules?.Dispose(); _customRules = null; }
    Action<string> _log = _ => { };

    public void Load(IModContext mod)
    {
        _legacy?.Dispose();
        _legacy = new LegacyRestoredMod();
        _legacy.Load(new SubContext(mod, "legacy"));
        string id = mod.Manifest.Id;
        _choices = mod.Settings<Choices>();
        _log = mod.Log;
        try { mod.Log(Filters(_choices)); }
        catch (Exception e) { mod.Log($"loot filters not updated: {e.Message}"); }
        GameEvents.AreaTick.Add(id, GameEvents.Always(async (player, _) =>
        {
            if (_told.TryGetValue(player, out var queue))
                while (queue.TryDequeue(out string? text)) await Program.PlayerOf(player).Tell(text);
            await Scan(player);
        }));
        GameEvents.Drops.Add(id, OrbDrops);
        GameEvents.Drops.Add(id, NewCurrencyDrops);
        LoadLegacyAffixes(mod);
        LoadCustomImplicits(mod);
        LoadLegacyUniques(mod);
        GameEvents.Drops.Add(id, LegacyUniqueDrops);
        _legacyCurrencies = new LegacyCurrencies(() => _choices.LegacyCurrencies ?? new(), Tell);
        GameEvents.Drops.Add(id, _legacyCurrencies.Drops);
        LoadIdolImplicits(mod);
        GameEvents.Drops.Add(id, IdolDrops);
        GameEvents.Drops.Add(id, GemDrops);
        GameEvents.Drops.Add(id, MirageDrops);
        LoadUnobtainable(mod);
        LoadAscendantNodes(mod.Log);
        GameEvents.Drops.Add(id, UnobtainableDrops);
        GameEvents.Drops.Add(id, CardRateDrops);
        GameEvents.Drops.Add(id, AscendantDrops);
        GameEvents.Drops.Add(id, LegacyDrops);
        GameEvents.Drops.Add(id, RecombinatorDrops);
        GameEvents.Drops.Add(id, DefiledDrops);
        GameEvents.Drops.Add(id, MistDrops);
        // The private hand-over for chests and strongboxes (GameEvents.ChestDrops), by name as the server may not have it.
        if (typeof(GameEvents).GetField("ChestDrops")?.GetValue(null) is GameEvent<Action<Player, string, string, List<ItemDrops.Drop>>> chests)
        {
            chests.Add(id, DefiledChest);
            chests.Add(id, _legacyCurrencies.Chests);
            chests.Add(id, IdolChests);
            chests.Add(id, MirageChests);
            chests.Add(id, UnobtainableChests);
            chests.Add(id, CardRateChests);
            chests.Add(id, AscendantChests);
        }
        // The server's hand-over for a currency used on an item (GameEvents.CurrencyOnItem), by name so a server
        // without it still loads the mod; before that existed, the play setup's own (LocalHooks, apply-local-hooks).
        var hooks = typeof(Game).Assembly.GetType("LocalHooks");
        if (typeof(GameEvents).GetField("CurrencyOnItem")?.GetValue(null) is GameEvent<Func<Player, Item, Item, ItemCatalog.BaseType, Random, Item?>> onItem)
            onItem.Add(id, CurrencyOnItem);
        else if (hooks?.GetField("CurrencyItemUse")?.GetValue(null) is GameEvent<Func<Player, Item, uint, Item?, Random, Item?>> currencyUse)
            currencyUse.Add(id, OrbUse);
        else mod.Log("the server has no hand-over for a currency used on an item (GameEvents.CurrencyOnItem): the Radiant Orb cannot be used");
        GameEvents.Command(id, "exciting",
            "/exciting (Exciting Items: the chances) | /exciting next implicit [1-3] | empowered | extra | custom | crucible | all | legacy | jackpot [2-4] (the next magic, rare or unique drop gets it) | /exciting orb [limit|krangle|defiled|mist|armour|weapon|jewellery] [n] (this mod's currencies at your feet) | /exciting idol [n] [scarab] | /exciting barya [n] | /exciting mirageunique [name] | /exciting unobtainable [name|list] | /exciting ascendant [node|ascendancy|list [filter]] [1-3] | /exciting legacyunique [name] [version] | legacyuniques <name> | /exciting legacy | gems | imprints | restore <n> | forget <n> | use <currency> | give ... (the legacy part)",
            async (player, args) =>
            {
                var who = Program.PlayerOf(player);
                // Legacy uniques: "/exciting legacyunique [name] [version]" at your feet, "/exciting legacyuniques <name>" lists versions.
                if (args.Length > 0 && args[0].ToLowerInvariant() is "legacyunique" or "legacyuniques")
                {
                    var words = args.Skip(1).ToList();
                    if (args[0].Equals("legacyuniques", StringComparison.OrdinalIgnoreCase))
                    {
                        string find = string.Join(' ', words);
                        var found = _legacyUniques.Where(u => find.Length == 0 || u.Name.Contains(find, StringComparison.OrdinalIgnoreCase)).Take(12).ToList();
                        await who.Tell(found.Count == 0 ? "Exciting Items: no legacy unique by that name." : $"Exciting Items: {_legacyUniques.Count} legacy uniques; matching:");
                        foreach (var u in found) await who.Tell($"  {u.Name}{(u.Removed ? " (removed)" : "")}: {string.Join(", ", u.Versions.Select(v => v.Name))}");
                        return;
                    }
                    // A last word with a digit (1.0.0), or Current / Legacy, names the version.
                    string? version = words.Count > 1 && (words[^1].Any(char.IsDigit) || words[^1] is "Current" or "Legacy") ? words[^1] : null;
                    if (version is not null) words.RemoveAt(words.Count - 1);
                    string? name = words.Count > 0 ? string.Join(' ', words) : null;
                    if (RandomLegacyUnique(player, _choices.LegacyUniques ?? new(), Random.Shared, out string label, name, version) is not { } legacyItem)
                    { await who.Tell("Exciting Items: no legacy unique could be made for that (see /exciting legacyuniques <name>)."); return; }
                    await GiveItem(player, legacyItem, label);
                    await who.Tell($"Exciting Items: {label}.");
                    return;
                }
                // Ascendant jewels: "/exciting ascendant [node or ascendancy] [1-3]" at your feet, "/exciting ascendant list [filter]".
                if (args.Length > 0 && args[0].ToLowerInvariant() is "ascendant" or "ascendancy")
                {
                    await AscendantCommand(player, args);
                    return;
                }
                // Unobtainable uniques: "/exciting unobtainable [name]" at your feet, "/exciting unobtainable list" (how many, by group).
                if (args.Length > 0 && args[0].ToLowerInvariant() is "unobtainable" or "unobtainables")
                {
                    if (args.Length > 1 && args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
                    {
                        var now = UnobtainablePool(player).Select(p => p.Unique.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var groups = _unobtainable.GroupBy(u => u.Group).Select(g => $"{g.Key} {g.Count(u => now.Contains(u.Name))}/{g.Count()}");
                        await who.Tell($"Exciting Items: {now.Count} unobtainable uniques can drop now ({string.Join(", ", groups)}).");
                        return;
                    }
                    string want = string.Join(' ', args.Skip(1));
                    if (MakeUnobtainable(player, Random.Shared, want) is not { } made) { await who.Tell($"Exciting Items: no unobtainable unique{(want.Length > 0 ? $" like \"{want}\"" : "")} can be made."); return; }
                    await GiveItem(player, made.Item, made.Name);
                    await who.Tell($"Exciting Items: {made.Name}.");
                    return;
                }
                // Mirage: "/exciting barya [n]", "/exciting mirageunique [name]" (Screams of the Desiccated with its two shrines).
                if (args.Length > 0 && args[0].ToLowerInvariant() is "barya" or "mirageunique")
                {
                    if (args[0].Equals("barya", StringComparison.OrdinalIgnoreCase))
                    {
                        int n = args.Length > 1 && int.TryParse(args[1], out int bn) ? Math.Clamp(bn, 1, 20) : 1;
                        if (Game.ItemsFor(player).All.FirstOrDefault(t => t.Name == Barya && t.Supported) is not { } barya) { await who.Tell("Exciting Items: the client data has no Black Barya."); return; }
                        await GiveItem(player, ItemCatalog.Create(barya) with { StackSize = (ushort)Math.Min(n, barya.MaxStack) }, Barya);
                        await who.Tell("Exciting Items: The Black Barya. Put it in the map device to face Saresh, of the Weeping Black.");
                        return;
                    }
                    string want = string.Join(' ', args.Skip(1));
                    var uniques = Game.UniquesFor(player);
                    var name = MirageUniques.FirstOrDefault(m => want.Length > 0 && m.Contains(want, StringComparison.OrdinalIgnoreCase))
                        ?? MirageUniques[Random.Shared.Next(MirageUniques.Length)];
                    if (uniques.Find(name) is not { } definition || uniques.Create(definition, Random.Shared) is not { } mirage) { await who.Tell($"Exciting Items: {name} could not be made."); return; }
                    mirage = WithTwoShrines(player, mirage, Random.Shared) ?? mirage;
                    await GiveItem(player, mirage, name);
                    await who.Tell($"Exciting Items: {name}.");
                    return;
                }
                // Idols: "/exciting idol [n] [scarab]" at your feet (scarab: each with a scarab's effect).
                if (args.Length > 0 && args[0].ToLowerInvariant() is "idol" or "idols")
                {
                    int many = args.Length > 1 && int.TryParse(args[1], out int k) ? Math.Clamp(k, 1, 20) : 1;
                    bool scarab = args.Any(a => a.Equals("scarab", StringComparison.OrdinalIgnoreCase));
                    var ic = _choices.Idols ?? new();
                    var was = _choices;
                    if (scarab) _choices = _choices with { Idols = ic with { ScarabImplicit = 1 } };
                    try
                    {
                        for (int i = 0; i < many; i++)
                            if (MakeIdol(player, Math.Max(68, (int)Program.PlayerOf(player).Level), Random.Shared, out string idolLabel) is { } idol)
                                await GiveItem(player, idol, idolLabel);
                    }
                    finally { if (scarab) _choices = was; }
                    await who.Tell("Exciting Items: Idols given. They go in the Idol inventory: the map device's storage grid in the Atlas screen's side panel, with the Idols module on in your league.");
                    return;
                }
                // The legacy part's commands (they were /legacy when it was a mod of its own).
                if (args.Length > 0 && _legacy is not null && args[0].ToLowerInvariant() is "legacy" or "gems" or "imprints" or "restore" or "forget" or "use" or "give")
                {
                    await _legacy.Command(player, args[0].Equals("legacy", StringComparison.OrdinalIgnoreCase) ? [.. args.Skip(1)] : args);
                    return;
                }
                if (args.Length > 0 && args[0].Equals("orb", StringComparison.OrdinalIgnoreCase))
                {
                    // "orb test": the test copy with the Divine Orb's picture (content/radiantorb-test.json).
                    // "orb limit" / "orb krangle": the Limit Break Orb or the Krangle Orb instead.
                    string kind = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                    string wantedId = kind switch
                    {
                        "test" => OrbId + "Test", "limit" or "limitbreak" => LimitBreakId, "krangle" => KrangleId, "defiled" => DefiledId,
                        "mist" => MistId, "armour" => ArmourRecombinator, "weapon" => WeaponRecombinator, "jewellery" or "jewelry" => JewelleryRecombinator,
                        _ => OrbId,
                    };
                    if (kind is "test" or "limit" or "limitbreak" or "krangle" or "defiled" or "mist" or "armour" or "weapon" or "jewellery" or "jewelry")
                        args = [args[0], .. args.Skip(2)];
                    if (Game.ItemsFor(player).Find(wantedId) is not { Supported: true } orb)
                    { await who.Tell("Exciting Items: that orb is not in the game data yet (apply the content packs, then start again)."); return; }
                    int count = args.Length > 1 && int.TryParse(args[1], out int wanted) ? Math.Clamp(wanted, 1, orb.MaxStack) : 1;
                    var (x, y) = who.Position;
                    // The ground no longer shows a new item by itself (LocalExile 0.6): it is sent as a drop is.
                    var lying = new WorldItemObject(x, y, ItemCatalog.Create(orb) with { StackSize = (ushort)count });
                    uint orbId = player.Instance.Ground.Add(lying, player);
                    await Game.SendGroundItem(player, orbId, lying, $"excitingitems: 0x02bb create world item 0x{orbId:x} ({count} {orb.Name})", justDropped: true);
                    await who.Tell($"Exciting Items: {count} {orb.Name}{(count == 1 ? "" : " ×" + count)} at your feet.");
                    return;
                }
                if (args.Length > 0 && args[0].Equals("next", StringComparison.OrdinalIgnoreCase))
                {
                    string what = args.Length > 1 ? args[1].ToLowerInvariant() : "all";
                    if (what == "jackpot")
                    {
                        int size = args.Length > 2 && int.TryParse(args[2], out int j) ? Math.Clamp(j, 2, 4) : 4;
                        Volatile.Write(ref _nextJackpot, size);
                        await who.Tell($"Exciting Items: the next magic, rare or unique drop is a jackpot of {size}.");
                        return;
                    }
                    if (what == "legacy")
                    {
                        _nextLegacy = true;
                        await who.Tell("Exciting Items: the next magic or rare drop that can take one gets a legacy affix.");
                        return;
                    }
                    int count = args.Length > 2 && int.TryParse(args[2], out int n) ? Math.Clamp(n, 1, 3) : 1;
                    var forced = what switch
                    {
                        "implicit" or "implicits" => new Forced(count, false, false),
                        "empowered" or "boost" => new Forced(0, true, false),
                        "extra" or "fractured" => new Forced(0, false, true),
                        "custom" => new Forced(0, false, false, true),
                        "crucible" or "tree" => new Forced(0, false, false, false, true),
                        _ => new Forced(count, true, true, true, true),
                    };
                    Volatile.Write(ref _forced, forced);
                    await who.Tell($"Exciting Items: the next magic, rare or unique drop gets {Effects(forced.Implicits, forced.Empowered, forced.Extra)}{(forced.Custom ? (forced.Implicits > 0 || forced.Empowered || forced.Extra ? ", " : "") + "a custom implicit" : "")}{(forced.Crucible ? ", a Crucible tree" : "")}.");
                    return;
                }
                var c = _choices;
                int known = Pool(Game.ItemsFor(player), Game.UniquesFor(player), Game.DropsFor(player)).Count;
                await who.Tell($"Exciting Items: unique implicits 1 in {c.Implicits.Chance} / {c.Implicits.ChanceTwo} / {c.Implicits.ChanceThree} " +
                    $"({c.Implicits.Mode}, {known} modifiers known); empowered 1 in {c.Empowered.Chance} (+{Low(c)}-{High(c)}%); " +
                    $"extra fractured modifier 1 in {c.ExtraModifier.Chance}. /exciting next implicit|empowered|extra|all forces one.");
            });
    }

    static int Low(Choices c) => Math.Max(1, c.Empowered.Percent.ElementAtOrDefault(0));
    static int High(Choices c) => Math.Max(Low(c), c.Empowered.Percent.ElementAtOrDefault(1));

    static string Effects(int implicits, bool empowered, bool extra) => string.Join(", ", new[]
    {
        implicits > 0 ? (implicits == 1 ? "a unique implicit" : $"{implicits} unique implicits") : null,
        empowered ? "an empowered modifier" : null,
        extra ? "an extra fractured modifier" : null,
    }.Where(s => s is not null));

    static bool Takes(string[] rarities, ItemRarity rarity) => rarities.Contains(rarity.ToString(), StringComparer.OrdinalIgnoreCase);

    // ---- which items and which modifiers ----

    /// <summary>Gear and jewels with modifiers; not flasks, maps, cluster jewels (their modifiers come from their
    /// enchantments) or Timeless Jewels.</summary>
    static readonly HashSet<string> FlaskKinds = ["LifeFlask", "ManaFlask", "HybridFlask", "UtilityFlask"];
    static readonly HashSet<string> OtherKinds = ["LifeFlask", "ManaFlask", "HybridFlask", "UtilityFlask", "Trinket", "AtlasRelic"];

    /// <summary>A flask's custom implicit (FlaskChance) and an empowered modifier (OtherChance) on a flask, Heist trinket or
    /// Idol; marked as gear is, so loot filters show it.</summary>
    Item? ExciteOther(Player player, Item item, ItemCatalog.BaseType type, Random rng, Forced? forced, out string effects, List<string> log)
    {
        effects = "";
        var c = _choices;
        var ci = c.CustomImplicits ?? new();
        bool flask = FlaskKinds.Contains(type.Class);
        bool custom = flask && (forced?.Custom ?? (ci.Enabled && ci.FlaskChance > 0 && rng.Next(ci.FlaskChance) == 0));
        bool empowered = item.Mods.Any(m => m.Section is ModSection.Explicit or ModSection.Implicit)
            && (forced?.Empowered ?? (c.Empowered.OtherItems && c.Empowered.OtherChance > 0 && rng.Next(c.Empowered.OtherChance) == 0));
        if (!custom && !empowered) return null;
        var now = item;
        bool gotCustom = false, gotEmpowered = false;
        if (custom && WithCustomImplicit(player, now, rng, log) is { } withCustom) { now = withCustom; gotCustom = true; }
        if (empowered && WithEmpowered(player, now, rng, log) is { } withEmpowered) { now = withEmpowered; gotEmpowered = true; }
        if (ReferenceEquals(now, item)) return null;
        effects = string.Join(", ", new[] { gotCustom ? "a custom implicit" : "", gotEmpowered ? "an empowered modifier" : "" }.Where(e => e.Length > 0));
        return c.Mark ? Marked(Game.ItemsFor(player), now) : now;
    }

    /// <summary>A dropped gem, very rarely: one level more than its cap allows (as a Vaal Orb's) or quality above 20%.</summary>
    void GemDrops(DropRoll d)
    {
        var e = _choices.Empowered;
        if (e.GemChance <= 0) return;
        var player = d.Player;
        d.Changes.Add((drop, rng) =>
        {
            if (!drop.Item.Components.Contains("SkillGem") || rng.Next(e.GemChance) != 0 || Game.ItemsFor(player).Gem(drop.Type) is not { MaxLevel: > 0 } gem)
                return [drop];
            Item changed;
            if (rng.Next(2) == 0 && e.GemLevels > 0)
                changed = drop.Item with { GemExperience = -Math.Clamp(e.GemLevels, 1, 10) };
            else
                changed = drop.Item with { Quality = (byte)rng.Next(21, Math.Max(21, e.GemQualityMax) + 1) };
            if (_choices.Announce) Tell(player, $"* An empowered gem dropped: {Game.ItemsFor(player).GemName(changed)} (level {gem.Level(changed.GemExperience)}, {changed.Quality}% quality).");
            return [drop with { Item = changed }];
        });
    }

    static bool Target(ItemCatalog.BaseType type) => type.Components.Contains("Mods") && !type.Components.Contains("Flask")
        && !type.Components.Contains("Map") && !type.Components.Contains("MapKey")
        && (JewelClasses.Contains(type.Class) ? !type.Id.Contains("PassiveTreeExpansion") && !type.Id.Contains("Timeless") : !NotGear.Contains(type.Class));

    bool Blacklisted(ModCatalog.Mod mod) => _choices.Blacklist.Any(entry => entry.StartsWith("stat:", StringComparison.OrdinalIgnoreCase)
        ? mod.Stats.Any(s => Program.Stats.Name(s.Id).Contains(entry[5..], StringComparison.OrdinalIgnoreCase))
        : !entry.StartsWith("class:", StringComparison.OrdinalIgnoreCase) && mod.Id.StartsWith(entry, StringComparison.OrdinalIgnoreCase));

    /// <summary>A modifier only uniques of blacklisted kinds of item have ("class:UtilityFlask").</summary>
    bool BlacklistedClasses(IEnumerable<string> classes)
    {
        var listed = _choices.Blacklist.Where(e => e.StartsWith("class:", StringComparison.OrdinalIgnoreCase)).Select(e => e[6..].Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return listed.Count > 0 && classes.All(listed.Contains);
    }

    static readonly (Theme Theme, Regex Words)[] Themes =
    [
        (Theme.Speed, new(@"movement_velocity|movement_speed|onslaught|phasing|flask_effect|action_speed")),
        (Theme.Ailment, new(@"ailment|ignite|chill|freez|shock|poison|bleed|stun|curse|elusive|blind|maim|hinder|avoid_|immun|_on_self")),
        (Theme.Scaling, new(@"_per_|per_\d|for_each|_charge|charges|aura|reservation|gem_level|_gems_|socketed|minion|totem|brand")),
        (Theme.Defence, new(@"life|energy_shield|armour|evasion|resist|block|suppress|damage_taken|damage_reduction|regenerat|recoup|fortif|dodge|recover|leech_from")),
        (Theme.Offence, new(@"damage|attack_speed|cast_speed|critical|penetrat|accuracy|projectile|area_of_effect|chain|pierce|impale|skill_effect_duration|added_")),
    ];

    /// <summary>What a modifier is for, told by its stats' names; a modifier of none of these is utility.</summary>
    static Theme ThemeOf(ModCatalog.Mod mod)
    {
        string words = string.Join(' ', mod.Stats.Select(s => s.Id)).ToLowerInvariant();
        var theme = Theme.None;
        foreach (var (t, regex) in Themes) if (regex.IsMatch(words)) theme |= t;
        // "damage taken" and "leech" are defence and sustain, not offence.
        if (theme.HasFlag(Theme.Offence) && Regex.IsMatch(words, "damage_taken|damage_reduction") && !Regex.IsMatch(words, @"damage_\+%|added_|_damage_%")) theme &= ~Theme.Offence;
        return theme == Theme.None ? Theme.Utility : theme;
    }

    /// <summary>The themes a slot takes in thematic mode.</summary>
    static Theme Wants(ItemCatalog.BaseType type) => type.Class switch
    {
        "Gloves" or "Quiver" => Theme.Offence,
        "Body Armour" or "Shield" => Theme.Defence,
        "Boots" => Theme.Speed | Theme.Ailment,
        "Helmet" => Theme.Defence | Theme.Scaling,
        "Amulet" or "Ring" or "Belt" => Theme.Utility,
        _ => type.Tags.Contains("weapon") ? Theme.Offence : Theme.Utility,
    };

    // Every unique's modifiers, once per data set: which kinds of item have each, and how it rolls there.
    List<Source> Pool(ItemCatalog catalog, UniqueCatalog uniques, ItemDrops drops) => _pools.GetValue(_catalog = catalog, _ =>
    {
        var byId = new Dictionary<string, Source>(StringComparer.Ordinal);
        foreach (var unique in uniques.All)
        {
            string? baseClass = catalog.Find(unique.BaseTypeId)?.Class;
            var ids = unique.Mods.Concat(unique.Variants?.SelectMany(v => v.Mods) ?? []);
            foreach (string modId in ids.Distinct())
            {
                if (catalog.Mods.ById(modId) is not { Stats.Count: > 0 } mod || Blacklisted(mod)) continue;
                if (!byId.TryGetValue(modId, out var source))
                {
                    string words = (mod.Id + " " + string.Join(' ', mod.Stats.Select(s => s.Id))).ToLowerInvariant();
                    bool bound = mod.Stats.Any(s => s.Local) || SlotWords.Any(words.Contains);
                    var ranges = (unique.Ranges ?? []).Where(r => r.Mod == modId && !r.Implicit)
                        .GroupBy(r => r.Stat).ToDictionary(g => g.Key, g => (g.First().Min, g.First().Max));
                    byId[modId] = source = new(mod, bound, ranges, unique.Name, ThemeOf(mod));
                }
                if (baseClass is not null) source.Classes.Add(baseClass);
                source.Weight = Math.Max(source.Weight, drops.UniqueWeight(unique.Name));
            }
        }
        // A modifier only flasks, maps or the like have stays with them, so it never reaches gear or a jewel.
        var pool = byId.Values.Where(s => s.Classes.Count > 0 && !BlacklistedClasses(s.Classes)).ToList();
        // A unique with no drop weight (a boss's, a league's) counts as the rarest there is.
        double rarest = pool.Where(s => s.Weight > 0).Select(s => s.Weight).DefaultIfEmpty(1).Min();
        foreach (var source in pool) if (source.Weight <= 0) source.Weight = rarest;
        return pool;
    });

    /// <summary>Whether a modifier may go on this kind of item. Jewels and gear never mix, and a modifier is always
    /// welcome on its own kind of item. Every modifier not blacklisted has somewhere to go: one the chosen mode leaves
    /// without a slot that drops follows Wild's rules, and one tied to kinds of item that never drop (a fishing rod's)
    /// goes on any gear.</summary>
    bool Fits(Source source, ItemCatalog.BaseType type)
    {
        string mode = _choices.Implicits.Mode.ToLowerInvariant();
        if (Rule(source, type, mode)) return true;
        if (Placeable(source, mode)) return false;
        if (mode != "wild" && Rule(source, type, "wild")) return true;
        return !Placeable(source, "wild") && JewelClasses.Contains(type.Class) == source.Jewel;
    }

    static bool Rule(Source source, ItemCatalog.BaseType type, string mode)
    {
        bool jewel = JewelClasses.Contains(type.Class);
        if (jewel ? !source.Jewel : !source.Gear) return false;
        if (source.Classes.Contains(type.Class)) return true;
        if (source.Bound) return false;
        return mode switch
        {
            "sanity" => false,
            "thematic" => jewel || (source.Theme & Wants(type)) != 0,
            _ => true,
        };
    }

    readonly ConditionalWeakTable<ItemCatalog, Dictionary<(Source, string), bool>> _placeable = new();
    readonly ConditionalWeakTable<ItemCatalog, List<ItemCatalog.BaseType>> _slots = new();
    ItemCatalog? _catalog;

    /// <summary>Whether some kind of item that drops (and the jewels) takes the modifier by the mode's rule.</summary>
    bool Placeable(Source source, string mode)
    {
        if (_catalog is not { } catalog) return true;
        var known = _placeable.GetValue(catalog, _ => new());
        lock (known)
            if (!known.TryGetValue((source, mode), out bool placeable))
                known[(source, mode)] = placeable = Slots(catalog).Any(t => Rule(source, t, mode));
            else return placeable;
        return known[(source, mode)];
    }

    /// <summary>One base of each kind of item a monster drops, and of each jewel.</summary>
    List<ItemCatalog.BaseType> Slots(ItemCatalog catalog) => _slots.GetValue(catalog, c =>
    {
        var slots = new List<ItemCatalog.BaseType>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int row = 0; c.Row(row) is { } t; row++)
            if (Target(t) && (JewelClasses.Contains(t.Class) || ItemDrops.EquipmentClasses.Contains(t.Class) && ItemDrops.Droppable(t)) && seen.Add(t.Class))
                slots.Add(t);
        return slots;
    });

    // One by weight: a modifier of a rarer unique comes up that much less often. Without the setting, every one alike.
    Source Pick(List<Source> pool, Random rng, out int index)
    {
        index = rng.Next(pool.Count);
        if (!_choices.Implicits.RarityWeights) return pool[index];
        double at = rng.NextDouble() * pool.Sum(s => s.Weight);
        for (int i = 0; i < pool.Count; i++)
            if ((at -= pool[i].Weight) < 0) { index = i; break; }
        return pool[index];
    }

    // ---- the three effects ----

    // How many implicits a drop gets: the rarest roll that succeeds, three before two before one.
    /// <summary>The jackpot's size: 4, 3 or 2 effects at once, each 1 in its own chance (the biggest is asked first), or 0.</summary>
    int JackpotRolled(Random rng)
    {
        if (Interlocked.Exchange(ref _nextJackpot, 0) is int asked and > 0) return asked;
        var j = _choices.Jackpot ?? new();
        if (!j.Enabled) return 0;
        if (j.Four > 0 && rng.Next(j.Four) == 0) return 4;
        if (j.Three > 0 && rng.Next(j.Three) == 0) return 3;
        if (j.Two > 0 && rng.Next(j.Two) == 0) return 2;
        return 0;
    }

    int ImplicitsRolled(Random rng) =>
        rng.Next(Math.Max(1, _choices.Implicits.ChanceThree)) == 0 ? 3
        : rng.Next(Math.Max(1, _choices.Implicits.ChanceTwo)) == 0 ? 2
        : rng.Next(Math.Max(1, _choices.Implicits.Chance)) == 0 ? 1 : 0;

    /// <summary>How many unique implicits a jackpot's implicit effect gives: 1, 2 or 3 in the same proportion as the ordinary
    /// chances (1 in Chance, ChanceTwo, ChanceThree), so 2 and 3 stay as much rarer than 1 as they are anywhere.</summary>
    int JackpotImplicits(Random rng)
    {
        var c = _choices.Implicits;
        double one = 1.0 / Math.Max(1, c.Chance), two = 1.0 / Math.Max(1, c.ChanceTwo), three = 1.0 / Math.Max(1, c.ChanceThree);
        double at = rng.NextDouble() * (one + two + three);
        return at < three ? 3 : at < three + two ? 2 : 1;
    }

    Item? WithUniqueImplicits(Player player, Item item, ItemCatalog.BaseType type, int count, Random rng, List<string> log)
    {
        var catalog = Game.ItemsFor(player);
        var have = item.Mods.Select(m => m.Hash).ToHashSet();
        var pool = Pool(catalog, Game.UniquesFor(player), Game.DropsFor(player)).Where(s => !have.Contains(s.Mod.Hash) && Fits(s, type)).ToList();
        var mods = item.Mods.ToList();
        var families = new HashSet<int>(item.Mods.SelectMany(m => catalog.Mods.ByHash(m.Hash)?.Families ?? []));
        int added = 0;
        while (added < count && pool.Count > 0)
        {
            var source = Pick(pool, rng, out int pick);
            pool.RemoveAt(pick);
            // Two modifiers of one family would be the same line twice.
            if (source.Mod.Families.Any(families.Contains)) continue;
            foreach (int family in source.Mod.Families) families.Add(family);
            var rolled = source.Mod.Roll(ModSection.Implicit, rng, stat => source.Ranges.TryGetValue(stat, out var range) ? range : null);
            int at = mods.FindLastIndex(m => m.Section == ModSection.Implicit) + 1;
            if (at == 0) at = mods.FindIndex(m => m.Section == ModSection.Explicit) is int first and >= 0 ? first : mods.Count;
            mods.Insert(at, rolled);
            log.Add($"implicit \"{catalog.Mods.Describe(rolled)}\" (from {source.Unique})");
            added++;
        }
        return added == 0 ? null : item with { Mods = mods };
    }

    /// <summary>One implicit or explicit modifier (a unique implicit this mod added included), every value of it better
    /// than it is by the chosen percent. A modifier with a downside or a value that picks something (a granted gem, a
    /// seed) is not chosen.</summary>
    Item? WithEmpowered(Player player, Item item, Random rng, List<string> log)
    {
        var mods = Game.ItemsFor(player).Mods;
        var own = Game.UniquesFor(player).RollsOf(item);
        // A value its modifier cannot roll otherwise (a fixed 1) is a switch: "has no sockets", "cannot leech".
        bool Switch(ModCatalog.Mod mod, ModSection section, int stat, int value) =>
            Math.Abs(value) == 1 && (own?.Invoke(mod, section, stat) ?? mod.RangeAt(stat)) is var (min, max) && min == max;
        var candidates = item.Mods.Select((m, i) => (m, i)).Where(p => p.m.Section is ModSection.Explicit or ModSection.Implicit && mods.ByHash(p.m.Hash) is { } mod
            && p.m.Values.Any(v => v != 0)
            && p.m.Values.Select((v, s) => s < mod.Stats.Count && !PicksSomething(Program.Stats.Name(mod.Stats[s].Id)) && !Switch(mod, p.m.Section, s, v)
                && (v == 0 || (v > 0) != LessIsBetter(Program.Stats.Name(mod.Stats[s].Id)))).All(ok => ok)).ToList();
        if (candidates.Count == 0) return null;
        var (chosen, index) = candidates[rng.Next(candidates.Count)];
        int percent = rng.Next(Low(_choices), High(_choices) + 1);
        var values = chosen.Values.Select(v => v == 0 ? 0 : (int)Math.Clamp(Math.Round(v * (1 + percent / 100.0), MidpointRounding.AwayFromZero) is var x && x == v ? v + Math.Sign(v) : x, int.MinValue, int.MaxValue)).ToList();
        var changed = item.Mods.ToList();
        changed[index] = chosen with { Values = values };
        log.Add($"empowered +{percent}%: \"{mods.Describe(chosen)}\" -> {string.Join(", ", values)}");
        return item with { Mods = changed };
    }

    /// <summary>One modifier more than the item could have, and its side fractured: a rare (or magic item) is first
    /// filled to its most prefixes and suffixes, then gets one more of a side; a unique gets one rare-style prefix or
    /// suffix. The fractured modifiers go to the front of the explicits, where the client reads them.</summary>
    Item? WithExtraModifier(Player player, Item item, ItemCatalog.BaseType type, Random rng, List<string> log, bool anyItem = false)
    {
        // An item already fractured keeps its fractured modifiers as they are: the orb (anyItem) adds the extra modifier
        // unfractured, a drop is left alone.
        if (item.FracturedCount > 0 && !anyItem) return null;
        bool keepFractures = item.FracturedCount > 0;
        var catalog = Game.ItemsFor(player);
        var rolls = Game.RollsFor(player);
        var pool = rolls.Pool(type, item).Where(p => p.Weight > 0 && !Blacklisted(p.Mod)).ToList();
        string? influencedBy = null;
        if (_choices.ExtraModifier.InfluencedMods)
        {
            // The modifiers one influence (not the item's own) adds to this base's pool at this item level, as an item
            // with that influence would roll them; the item does not get the influence.
            var have = pool.Select(p => p.Mod.Hash).ToHashSet();
            var options = Enum.GetValues<ItemInfluence>().Where(i => i != ItemInfluence.None && !item.Influences.HasFlag(i))
                .Select(i => (Influence: i, Mods: rolls.Pool(type, item.ItemLevel, item.Influences | i, null)
                    .Where(p => p.Weight > 0 && !have.Contains(p.Mod.Hash) && !Blacklisted(p.Mod)).ToList()))
                .Where(o => o.Mods.Count > 0).ToList();
            if (options.Count > 0)
            {
                var chosen = options[rng.Next(options.Count)];
                influencedBy = chosen.Influence.ToString();
                pool.AddRange(chosen.Mods);
            }
        }
        if (pool.Count == 0) return null;
        var mods = item.Mods.ToList();
        int SideOf(ItemMod m) => catalog.Mods.ByHash(m.Hash)?.GenerationType ?? 0;
        var families = new HashSet<int>(mods.SelectMany(m => catalog.Mods.ByHash(m.Hash)?.Families ?? []));
        var added = new List<ItemMod>();
        ItemMod? Add(int side)
        {
            var open = pool.Where(p => p.Mod.GenerationType == side && !p.Mod.Families.Any(families.Contains) && mods.All(m => m.Hash != p.Mod.Hash)).ToList();
            if (open.Count == 0) return null;
            int at = rng.Next(open.Sum(p => p.Weight));
            var mod = open.First(p => (at -= p.Weight) < 0).Mod;
            foreach (int f in mod.Families) families.Add(f);
            var rolled = mod.Roll(ModSection.Explicit, rng);
            mods.Add(rolled);
            added.Add(rolled);
            return rolled;
        }
        const int Prefix = ItemRolls.Prefix, Suffix = ItemRolls.Suffix;
        int chosenSide = rng.Next(2) == 0 ? Prefix : Suffix;
        if (item.Rarity is ItemRarity.Rare or ItemRarity.Magic && !keepFractures)
        {
            var (most, mostSuffixes) = item.Rarity == ItemRarity.Rare ? rolls.RareMaxAffixes(item) : (1, 1);
            while (mods.Count(m => m.Section == ModSection.Explicit && SideOf(m) == Prefix) < most && Add(Prefix) is not null) { }
            while (mods.Count(m => m.Section == ModSection.Explicit && SideOf(m) == Suffix) < mostSuffixes && Add(Suffix) is not null) { }
        }
        if (Add(chosenSide) is null && Add(chosenSide == Prefix ? Suffix : Prefix) is null) return null;
        chosenSide = SideOf(added[^1]);
        if (keepFractures)
        {
            log.Add($"extra {(chosenSide == Prefix ? "prefix" : "suffix")} \"{catalog.Mods.Describe(added[^1])}\" (the item was already fractured)");
            return item with { Mods = mods, RequiredLevel = null };
        }
        // A unique's own modifiers have no side: only the one added is fractured there.
        bool Fractured(ItemMod m) => m.Section == ModSection.Explicit && (item.Rarity == ItemRarity.Unique ? ReferenceEquals(m, added[^1]) : SideOf(m) == chosenSide);
        var ordered = mods.Where(m => m.Section != ModSection.Explicit)
            .Concat(mods.Where(Fractured)).Concat(mods.Where(m => m.Section == ModSection.Explicit && !Fractured(m))).ToList();
        log.Add($"extra {(chosenSide == Prefix ? "prefix" : "suffix")} \"{catalog.Mods.Describe(added[^1])}\"" +
            (influencedBy is null ? "" : $" ({influencedBy} modifiers in the pool)") +
            (added.Count > 1 ? $" after {added.Count - 1} to fill the item" : "") + $", {ordered.Count(Fractured)} fractured");
        return item with { Mods = ordered, FracturedCount = (byte)ordered.Count(Fractured), RequiredLevel = null };
    }

    /// <summary>A stat whose value says which one and not how much: a granted support gem and its socket, a Timeless
    /// Jewel's seed, conqueror and version, Thread of Hope's ring, a veiled modifier's seed.</summary>
    static bool PicksSomething(string stat) => stat.Contains("_index", StringComparison.Ordinal)
        || stat.StartsWith("local_unique_jewel_alternate_tree_", StringComparison.Ordinal)
        || stat is "local_jewel_variable_ring_radius_value" or "veiled_mod_seed";

    /// <summary>A stat where less is better: damage the character takes, what a skill costs, flask charges used,
    /// requirements, and ailments, curses and self-damage on the character. Told by the stat's name.</summary>
    static bool LessIsBetter(string stat) => LowerIsBetter.IsMatch(stat) && !OnEnemies.IsMatch(stat);

    static readonly Regex LowerIsBetter = new(
        @"damage_taken_(from_hits_|over_time_|from_crit_)?\+|_cost_\+|charges_used_\+|requirements?_\+"
        + @"|^(local_)?self_.*(duration|effect)_\+%|(curse|chill|shocked)_effect(iveness)?_on_self_\+%|ailment_on_self_duration_\+%"
        + @"|against_self|^self_.*damage_on_.*skill_use|life_taken_per_minute|reservation_efficiency_-2%_per_1|^-%_of_your",
        RegexOptions.CultureInvariant);

    // "Enemies take increased damage" is damage taken too, and there more is better.
    static readonly Regex OnEnemies = new(@"enem(y|ies).*damage_taken|ailment_types|chilling_areas", RegexOptions.CultureInvariant);

    /// <summary>The drop with whatever effects come up (or were forced), or null with none.</summary>
    Item? Excite(Player player, Item item, Random rng, Forced? forced, out string effects, List<string> log, bool anyItem = false)
    {
        effects = "";
        var c = _choices;
        if ((!anyItem && (item.Mirrored || item.Corrupted)) || Game.ItemsFor(player).ByHash(item.TypeHash) is not { } type) return null;
        // Flasks, Heist trinkets and Idols: their own rolls (a flask's custom implicit, an empowered modifier), none of gear's.
        if (!Target(type))
            return OtherKinds.Contains(type.Class) ? ExciteOther(player, item, type, rng, forced, out effects, log) : null;
        if (item.Rarity is not (ItemRarity.Magic or ItemRarity.Rare or ItemRarity.Unique)) return null;
        bool extra = forced?.Extra ?? (Takes(c.ExtraModifier.Rarities, item.Rarity) && rng.Next(Math.Max(1, c.ExtraModifier.Chance)) == 0);
        bool empowered = forced?.Empowered ?? (Takes(c.Empowered.Rarities, item.Rarity) && rng.Next(Math.Max(1, c.Empowered.Chance)) == 0);
        int implicits = forced?.Implicits ?? (Takes(c.Implicits.Rarities, item.Rarity) ? ImplicitsRolled(rng) : 0);
        bool legacy = false;
        var ci = c.CustomImplicits ?? new();
        bool custom = forced?.Custom ?? (ci.Enabled && Takes(ci.Rarities ?? ["magic", "rare", "unique"], item.Rarity) && rng.Next(Math.Max(1, ci.Chance)) == 0);
        var cc = c.Crucible ?? new();
        bool crucible = forced?.Crucible ?? (cc.Enabled && Takes(cc.Rarities ?? ["magic", "rare", "unique"], item.Rarity) && rng.Next(Math.Max(1, cc.Chance)) == 0);
        // A jackpot: 2, 3 or all 4 effects at once (a unique implicit, an empowered modifier, an extra modifier and a
        // legacy affix), drawn from those the item can take, on top of what the ordinary rolls gave.
        int jackpot = forced is null ? JackpotRolled(rng) : 0;
        if (jackpot > 0)
        {
            var kinds = new List<string> { "implicit", "empowered", "extra" };
            if (item.Rarity is not ItemRarity.Unique && (c.LegacyMods ?? new()).Enabled && _legacyAffixes.Count > 0) kinds.Add("legacy");
            if (ci.Enabled && ci.InJackpots && _custom.Count > 0) kinds.Add("custom");
            foreach (string kind in kinds.OrderBy(_ => rng.Next()).Take(jackpot))
                switch (kind)
                {
                    case "implicit": implicits = Math.Max(implicits, JackpotImplicits(rng)); break;
                    case "empowered": empowered = true; break;
                    case "extra": extra = true; break;
                    case "legacy": legacy = true; break;
                    case "custom": custom = true; break;
                }
            log.Add($"jackpot of {jackpot}");
        }
        if (!extra && !empowered && implicits == 0 && !legacy && !custom && !crucible) return null;
        var now = item;
        bool gotLegacy = false;
        // The legacy affix first, so the extra modifier can still be added after it on a full item.
        if (legacy && WithLegacyAffix(player, now, type, rng, (c.LegacyMods ?? new()) with { Rarities = ["magic", "rare"] }) is { } withLegacy)
        { now = withLegacy; gotLegacy = true; log.Add("a legacy affix"); }
        bool gotExtra = false, gotEmpowered = false; int gotImplicits = 0;
        if (extra && WithExtraModifier(player, now, type, rng, log, anyItem) is { } withExtra) { now = withExtra; gotExtra = true; }
        if (implicits > 0 && WithUniqueImplicits(player, now, type, implicits, rng, log) is { } withImplicits)
        {
            gotImplicits = withImplicits.Mods.Count(m => m.Section == ModSection.Implicit) - now.Mods.Count(m => m.Section == ModSection.Implicit);
            now = withImplicits;
        }
        bool gotCrucible = false;
        if (crucible && WithCrucibleTree(player, now, rng, log, cc) is { } withTree) { now = withTree; gotCrucible = true; }
        bool gotCustom = false;
        if (custom && WithCustomImplicit(player, now, rng, log) is { } withCustom) { now = withCustom; gotCustom = true; }
        // Last, so it can land on any of them, a unique implicit just added included.
        if (empowered && WithEmpowered(player, now, rng, log) is { } withEmpowered) { now = withEmpowered; gotEmpowered = true; }
        if (ReferenceEquals(now, item)) return null;
        effects = string.Join(", ", new[] { Effects(gotImplicits, gotEmpowered, gotExtra), gotCustom ? "a custom implicit" : "", gotLegacy ? "a legacy affix" : "", gotCrucible ? "a Crucible tree" : "" }.Where(e => e.Length > 0))
            + (jackpot > 0 ? $" (JACKPOT x{jackpot})" : "");
        return c.Mark ? Marked(Game.ItemsFor(player), now) : now;
    }

    async Task Scan(Player player)
    {
        if (!player.Connection.PlayerSent || player.Closed || player.LeavingArea) return;
        var ground = player.Instance.Ground;
        var watch = _watches.GetValue(ground, _ => new Watch());
        var replaced = new List<(uint OldId, uint NewId, WorldItemObject Item, string Label)>();
        lock (watch)
        {
            var all = ground.All();
            uint last = all.Count == 0 ? 0 : all.Max(e => e.Id);
            // What already lay there when the mod first saw this ground is left alone.
            if (!watch.Started) { watch.Started = true; watch.Next = last + 1; return; }
            foreach (var (id, lying) in all)
            {
                // An item a player put down was a drop once already, and an item only one player sees (a quest item,
                // the start weapon) is left alone; so is what this mod put down itself.
                if (id < watch.Next || watch.Ours.Remove(id) || lying.PlayerDropped || !ground.Origin(id).Shared
                    || (lying.Item.Rarity is not (ItemRarity.Magic or ItemRarity.Rare or ItemRarity.Unique)
                        && !OtherKinds.Contains(Game.ItemsFor(player).ByHash(lying.Item.TypeHash)?.Class ?? ""))) continue;
                var forced = Volatile.Read(ref _forced);
                var log = new List<string>();
                if (Excite(player, lying.Item, Random.Shared, forced, out string effects, log) is not { } changed) continue;
                // Only one caller gets the item, so a pickup in the same moment cannot duplicate it.
                if (ground.Take(id, lying) is null) continue;
                // Put down by no one and shared, so it is sent to every client in the area, the killer's too.
                var replacement = lying with { Item = changed };
                uint newId = ground.Add(replacement, null, shared: true);
                watch.Ours.Add(newId);
                last = Math.Max(last, newId);
                if (forced is not null) Interlocked.CompareExchange(ref _forced, null, forced);
                string name = Game.ItemsFor(player).ByHash(changed.TypeHash)?.Name ?? "item";
                replaced.Add((id, newId, replacement, $"{changed.Rarity} {name} with {effects}"));
                _log($"{changed.Rarity} {name} at ({lying.GridX}, {lying.GridY}), world item 0x{id:x} -> 0x{newId:x}: {string.Join("; ", log)}");
            }
            watch.Next = last + 1;
        }
        // Since LocalExile 0.6 the ground is not synced to the clients by itself: the old object is removed and the
        // changed item is sent, as the game does when it moves an item.
        foreach (var (oldId, newId, replacement, label) in replaced)
        {
            // The changed item first, so nothing the removal of the old object waits for can keep it back.
            await Game.SendGroundItem(player, newId, replacement, $"excitingitems: 0x02bb create world item 0x{newId:x} ({label})", justDropped: true);
            _log($"world item 0x{newId:x} sent ({label})");
            if (_choices.Announce) await Program.PlayerOf(player).Tell("* Exciting item dropped: " + label);
            await Game.SendWorldRemoval(player, oldId, $"excitingitems: replace world item 0x{oldId:x}");
        }
    }

    // ---- the Radiant Orb (content/radiantorb.json) ----

    const string OrbId = "Metadata/Items/Currency/LocalExileRadiantOrb";
    readonly ConditionalWeakTable<Player, System.Collections.Concurrent.ConcurrentQueue<string>> _told = new();

    void Tell(Player player, string text) => _told.GetValue(player, _ => new()).Enqueue(text);

    /// <summary>Kills sometimes drop a Radiant Orb: 1 in DropChance for a normal monster, 3× for magic, 6× for rare,
    /// 10× for unique.</summary>
    void OrbDrops(DropRoll d)
    {
        var c = _choices.RadiantOrb ?? new();
        if (!c.Enabled || c.DropChance <= 0 || Game.ItemsFor(d.Player).Find(OrbId) is not { Supported: true }) return;
        double weight = d.Monster.Rarity switch { MonsterRarity.Magic => 3, MonsterRarity.Rare => 6, MonsterRarity.Unique => 10, _ => 1 };
        if (Random.Shared.NextDouble() * c.DropChance < weight * Math.Max(0, d.Quantity))
        {
            d.Stacks.Add((OrbId, 1));
            if (_choices.Announce) Tell(d.Player, "* A Radiant Orb dropped.");
        }
    }

    /// <summary>A Radiant Orb used on a magic, rare or unique item that is not exciting yet: one result, picked by the
    /// weights in the settings among those the item can take, and the Exciting mark. Any other answer than a new item is
    /// the target itself: refused, nothing spent, and the server does not go on to the orb's copied Divine Orb data (that
    /// rerolled an exciting item's numbers).</summary>
    Item? OrbUse(Player player, Item target, uint currency, Item? used, Random random)
    {
        var items = Game.ItemsFor(player);
        if (items.Find(OrbId) is not { } orb || currency != orb.Hash) return null;
        var o = _choices.RadiantOrb ?? new();
        // A Hinekora preview asks for the result without spending the orb: not previewed.
        if (target.Foreseeing) return target;
        if (!o.Enabled) { Tell(player, "Radiant Orb: turned off in Exciting Items' settings."); return target; }
        if (Marker(items) is { } marker && target.Mods.Any(m => m.Hash == marker.Hash))
        { Tell(player, "Radiant Orb: that item is already radiant."); return target; }
        var choices = new List<(Forced Forced, int Weight)>
        {
            (new(1, false, false), o.OneImplicit), (new(2, false, false), o.TwoImplicits), (new(3, false, false), o.ThreeImplicits),
            (new(0, true, false), o.Empowered), (new(0, false, true), o.Extra), (new(0, false, false, true), o.Custom),
            (new(0, false, false, false, true), o.Crucible),
        };
        var log = new List<string>();
        // Weighted, without putting back: a result the item cannot take gives way to the next one drawn.
        var left = choices.Where(c => c.Weight > 0).ToList();
        while (left.Count > 0)
        {
            int at = random.Next(left.Sum(c => c.Weight));
            int pick = left.FindIndex(c => (at -= c.Weight) < 0);
            var (forced, _) = left[pick];
            left.RemoveAt(pick);
            if (Excite(player, target, random, forced, out string effects, log, o.AnyItem) is { } result)
            {
                foreach (string line in log) _log("Radiant Orb: " + line);
                Tell(player, $"Radiant Orb: the item gains {effects}.");
                return result;
            }
        }
        Tell(player, o.AnyItem ? "Radiant Orb: use it on a magic, rare or unique piece of gear or jewel."
            : "Radiant Orb: use it on a magic, rare or unique piece of gear or jewel that is not corrupted or mirrored.");
        return target;
    }

    // ---- loot filters ----

    // The rule goes at the top of every loot filter in the folder, between its two marker lines, or comes out again.
    // The old name's rule comes out in any case. Online filters (OnlineFilters\<id>, rewritten by the game when they
    // update) are not touched: each gets a copy beside the local filters, "<its name> (Exciting).filter", with the rule
    // on top, made again at every server start so it follows the online filter's updates. Pick that copy in Options.
    const string OnlineCopySuffix = " (Exciting).filter";

    static string Filters(Choices choices)
    {
        string games = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Path of Exile");
        // The game lists filters in My Games\Path of Exile itself; an earlier version wrote its filter one folder down.
        string oldFolder = Path.Combine(games, "LocalExile");
        string folder = choices.FilterFolder.Trim().Length > 0 ? choices.FilterFolder.Trim() : games;
        if (!Directory.Exists(folder)) return $"no loot filter folder at {folder}";
        if (!oldFolder.Equals(folder, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(oldFolder, OwnFilter)))
            File.Delete(Path.Combine(oldFolder, OwnFilter));
        bool wanted = choices.ShowInFilters && choices.Mark;
        string own = Path.Combine(folder, OwnFilter), oldOwn = Path.Combine(folder, OldOwnFilter);
        if (File.Exists(oldOwn)) File.Delete(oldOwn);
        var filters = Directory.GetFiles(folder, "*.filter").Where(f => !f.Equals(own, StringComparison.OrdinalIgnoreCase)
            && !f.EndsWith(OnlineCopySuffix, StringComparison.OrdinalIgnoreCase)).ToList();
        int changed = 0;
        string[] legacy = (choices.LegacyMods ?? new()).Mark && LegacyMarker(Program.Items) is not null ? ["", .. LegacyFilterRule()] : [];
        string[] block = [FilterStart, .. FilterRule(Marker(Program.Items) is not null), .. legacy, FilterEnd];
        int copies = OnlineCopies(folder, wanted ? block : null, ref changed);
        static bool Cut(List<string> lines, string start, string end)
        {
            int from = lines.FindIndex(l => l.Trim() == start), to = lines.FindIndex(l => l.Trim() == end);
            if (from < 0 || to < from) return false;
            lines.RemoveRange(from, to - from + 1);
            if (from < lines.Count && lines[from].Trim().Length == 0) lines.RemoveAt(from);
            return true;
        }
        foreach (string file in filters)
        {
            var lines = File.ReadAllLines(file).ToList();
            var before = string.Join('\n', lines);
            Cut(lines, OldStart, OldEnd);
            Cut(lines, FilterStart, FilterEnd);
            if (wanted) lines.InsertRange(0, block.Append(""));
            if (string.Join('\n', lines) == before) continue;
            File.WriteAllLines(file, lines);
            changed++;
        }
        // With no filter of the who's own, one that holds only the rule: it hides nothing.
        if (wanted && filters.Count == 0 && (!File.Exists(own) || !File.ReadAllLines(own).SequenceEqual(block))) { File.WriteAllLines(own, block); changed++; }
        if (!wanted && File.Exists(own)) { File.Delete(own); changed++; }
        return $"loot filters in {folder}: {filters.Count} found, {copies} online filter cop{(copies == 1 ? "y" : "ies")}, " +
            $"{changed} changed (rule {(wanted ? "on" : "off")})";
    }

    // Writes "<name> (Exciting).filter" for every online filter (block on top, then the online filter as it is), or,
    // with no block, deletes those copies. A copy is ours only if it starts with the block's first line.
    static int OnlineCopies(string folder, string[]? block, ref int changed)
    {
        foreach (string old in Directory.GetFiles(folder, "*" + OnlineCopySuffix))
            if (File.ReadLines(old).FirstOrDefault()?.Trim() == FilterStart) { File.Delete(old); changed++; }
        string online = Path.Combine(folder, "OnlineFilters");
        if (block is null || !Directory.Exists(online)) return 0;
        int copies = 0;
        foreach (string file in Directory.GetFiles(online))
        {
            string[] lines = File.ReadAllLines(file);
            string? name = lines.Take(20).FirstOrDefault(l => l.StartsWith("#name:", StringComparison.OrdinalIgnoreCase))?[6..].Trim();
            if (string.IsNullOrEmpty(name)) continue;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            File.WriteAllLines(Path.Combine(folder, name + OnlineCopySuffix), [.. block, "", .. lines]);
            copies++;
        }
        return copies;
    }
}
