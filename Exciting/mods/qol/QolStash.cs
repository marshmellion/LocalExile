using System.Reflection;
using GameServer.Core.Protocol.Instance;

public sealed partial class QolMod
{
    const byte Main = PlayerInventories.MainInventory;

    // ---- what the stash rules know, read through reflection (the private copy may lag main's names) ----

    static object? Rules(AccountStash stash) =>
        typeof(AccountStash).GetField("_rules", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(stash);

    static int? MapTier(AccountStash stash, uint hash) =>
        Rules(stash) is { } rules && rules.GetType().GetMethod("MapTier") is { } m ? (int?)m.Invoke(rules, [hash]) : null;

    static IReadOnlyCollection<int> AffinitiesOf(AccountStash stash, uint hash) =>
        Rules(stash) is { } rules
        && rules.GetType().GetField("_affinities", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(rules) is Dictionary<uint, HashSet<int>> all
        && all.TryGetValue(hash, out var set) ? set : [];

    static bool IsGridType(byte type) => type is 0 or 1 or 2 or 7 or 10;

    /// <summary>The item class's display name (ItemClasses.Name: "Boots", "Two Hand Swords"), the class Id otherwise.</summary>
    static readonly Dictionary<string, string> ClassNames = new(StringComparer.OrdinalIgnoreCase);
    static string ClassName(string classId)
    {
        lock (ClassNames)
        {
            if (ClassNames.Count == 0)
                foreach (var r in GameServer.Core.ClientTables.Table("ItemClasses"))
                    if (r.Text("Id") is { } id) ClassNames[id] = r.Text("Name") ?? id;
            return ClassNames.TryGetValue(classId, out var name) ? name : classId;
        }
    }

    // Tab names that hold a group of classes: "Weapons", "Armour", "Jewellery" and so on, besides a class's own name.
    static readonly (string Group, string[] Classes)[] Groups =
    [
        ("weapons", ["Claw", "Dagger", "Rune Dagger", "Wand", "One Hand Sword", "Thrusting One Hand Sword", "One Hand Axe", "One Hand Mace",
            "Sceptre", "Bow", "Staff", "Warstaff", "Two Hand Sword", "Two Hand Axe", "Two Hand Mace", "FishingRod"]),
        ("one hand", ["Claw", "Dagger", "Rune Dagger", "Wand", "One Hand Sword", "Thrusting One Hand Sword", "One Hand Axe", "One Hand Mace", "Sceptre"]),
        ("two hand", ["Bow", "Staff", "Warstaff", "Two Hand Sword", "Two Hand Axe", "Two Hand Mace"]),
        ("swords", ["One Hand Sword", "Thrusting One Hand Sword", "Two Hand Sword"]),
        ("axes", ["One Hand Axe", "Two Hand Axe"]),
        ("maces", ["One Hand Mace", "Two Hand Mace", "Sceptre"]),
        ("daggers", ["Dagger", "Rune Dagger"]),
        ("staves", ["Staff", "Warstaff"]),
        ("armour", ["Body Armour", "Helmet", "Gloves", "Boots", "Shield"]),
        ("armor", ["Body Armour", "Helmet", "Gloves", "Boots", "Shield"]),
        ("body", ["Body Armour"]),
        ("chests", ["Body Armour"]),
        ("helmets", ["Helmet"]),
        ("helms", ["Helmet"]),
        ("jewellery", ["Amulet", "Ring", "Belt"]),
        ("jewelry", ["Amulet", "Ring", "Belt"]),
        ("accessories", ["Amulet", "Ring", "Belt"]),
        ("jewels", ["Jewel", "AbyssJewel"]),
        ("flasks", ["LifeFlask", "ManaFlask", "HybridFlask", "UtilityFlask", "UtilityFlaskCritical"]),
        ("gems", ["Active Skill Gem", "Support Skill Gem"]),
        ("quivers", ["Quiver"]),
        ("shields", ["Shield"]),
        // The Allflame league's items: Deepwater charts and the embers (no stash tab type or affinity holds them).
        ("allflame", ["DeepwaterChart", "NecropolisPack"]),
        ("deepwater", ["DeepwaterChart"]),
        ("charts", ["DeepwaterChart"]),
        ("embers", ["NecropolisPack"]),
    ];

    /// <summary>Whether a tab's name names the item's class or a group holding it. A name may list several, split by
    /// "/", ",", "+" or "&amp;" ("Boots / Gloves").</summary>
    static bool NameHolds(string tabName, string classId)
    {
        string name = ClassName(classId);
        foreach (string raw in tabName.Split(['/', ',', '+', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string word = raw.ToLowerInvariant();
            if (word == classId.ToLowerInvariant() || word == name.ToLowerInvariant() || word + "s" == name.ToLowerInvariant()
                || word == name.ToLowerInvariant().TrimEnd('s') || word.TrimEnd('s') == classId.ToLowerInvariant().TrimEnd('s')) return true;
            foreach (var (group, classes) in Groups)
                if ((word == group || word == group.TrimEnd('s')) && classes.Contains(classId, StringComparer.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // ---- the Chart Locker (Game module's DeepwaterLocker: RegionOf, and Store as its Ctrl + click uses) ----

    static MethodInfo? _lockerRegion, _lockerStore;
    static bool _lockerLooked;

    async Task<bool> IntoChartLocker(Player player, InventoryItem it, string name, bool force = false)
    {
        bool sulphur = name == "Dead Man's Sulphur";
        if (!force && (sulphur ? !_choices.Deposit.SulphurToLocker : !_choices.Deposit.ChartLocker)) return false;
        if (!_lockerLooked)
        {
            _lockerLooked = true;
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("DeepwaterLocker", false)).FirstOrDefault(t => t is not null);
            _lockerRegion = type?.GetMethod("RegionOf", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _lockerStore = type?.GetMethod("Store", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (_lockerRegion is null || _lockerStore is null || _lockerStore.GetParameters().Length != 3)
            {
                _log("deposit: no Chart Locker on this server (charts go to the stash)");
                _lockerStore = null;
            }
        }
        if (_lockerStore is null || _lockerRegion!.Invoke(null, [it.Item.TypeHash]) is null) return false;
        byte from = Game.ItemsOf(player).RowOf(it.Id) ?? Main;
        try { return _lockerStore.Invoke(null, [player, it, from]) is Task<bool> stored && await stored; }
        catch (TargetInvocationException e) { _log($"deposit: Chart Locker: {e.InnerException?.Message}"); return false; }
    }

    // ---- deposit all ----

    async Task DepositAll(Player player)
    {
        if (!player.StashOpen) { Tell(player, "open your stash first"); return; }
        var stash = Game.StashOf(player);
        var items = Game.ItemsOf(player);
        var catalog = Game.ItemsFor(player);
        var tabs = stash.Records(null).Where(t => !t.Hidden && t.HiddenKey == 0 && t.Type != 16).ToList();
        var keep = _choices.Deposit.Keep.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int moved = 0, left = 0;
        var where = new Dictionary<string, int>();
        foreach (var it in items.Items(Main).ToList())
        {
            if (catalog.ByHash(it.Item.TypeHash) is not { } type || keep.Contains(type.Name)) continue;
            // The Allflame league's Chart Locker (the Sovereign's deck) holds charts and Dead Man's Sulphur, in tabs of its own.
            if (await IntoChartLocker(player, it, type.Name))
            {
                moved++;
                where["Chart Locker"] = where.GetValueOrDefault("Chart Locker") + 1;
                continue;
            }
            var affinities = AffinitiesOf(stash, type.Hash);
            bool isMap = MapTier(stash, type.Hash) is not null;
            // Special tabs first (currency, fragments, essences, maps, uniques ...), then a tab given the item's affinity,
            // then a tab named after its kind, then the tab shown.
            var order = new List<InstanceStash.Tab>();
            order.AddRange(tabs.Where(t => t.Type == 5 && isMap));
            order.AddRange(tabs.Where(t => t.Type == 4 && it.Item.Rarity == ItemRarity.Unique));
            order.AddRange(tabs.Where(t => !IsGridType(t.Type) && t.Type is not (4 or 5) && stash.Holds(t.Index, type.Hash)));
            order.AddRange(tabs.Where(t => IsGridType(t.Type) && t.Type != 2 && affinities.Any(a => a < 32 && (t.Affinities & (1u << a)) != 0)));
            order.AddRange(tabs.Where(t => IsGridType(t.Type) && t.Type != 2 && NameHolds(t.Name, type.Class)));
            if (_choices.Deposit.ShownTabLast && tabs.FirstOrDefault(t => t.Index == stash.LastTab) is { } shown && IsGridType(shown.Type) && shown.Type != 2)
                order.Add(shown);
            // A unique the unique tab already holds goes to the copies tab, right after the unique tab refuses it.
            if (_choices.UniqueCopies && it.Item.Rarity == ItemRarity.Unique && tabs.Any(t => t.Type == 4))
            {
                int copies = CopiesTabOf(stash, make: false);
                if (copies < 0)
                {
                    bool held = tabs.Where(t => t.Type == 4).Any(u => stash.UniqueDestination(u.Index, it.Item) is int page and >= 0
                        && stash.Inventory(page).Items.Any(i => SameUnique(i.Item, it.Item)));
                    if (held)
                    {
                        copies = CopiesTabOf(stash, make: true);
                        await Game.SendTabListAgain(player, InstanceStash.Personal);
                        tabs = stash.Records(null).Where(t => !t.Hidden && t.HiddenKey == 0 && t.Type != 16).ToList();
                    }
                }
                if (copies >= 0 && tabs.FirstOrDefault(t => t.Index == copies) is { } copiesTab)
                    order.Insert(order.FindLastIndex(t => t.Type == 4) + 1, copiesTab);
            }
            bool done = false;
            foreach (var tab in order.DistinctBy(t => t.Index))
            {
                await Game.CtrlClickToStash(player, tab.Index, it.Id, "qol deposit all");
                if (items.RowOf(it.Id) is null)
                {
                    moved++;
                    where[tab.Name] = where.GetValueOrDefault(tab.Name) + 1;
                    done = true;
                    break;
                }
            }
            if (!done) left++;
        }
        Tell(player, $"deposited {moved} item(s)" + (where.Count > 0 ? $" ({string.Join(", ", where.Select(w => $"{w.Value} to {w.Key}"))})" : "")
            + (left > 0 ? $"; {left} had no tab" : ""));
    }

    // ---- vendor the shown tab ----

    static object? _vendor;
    static object? Vendor()
    {
        if (_vendor is not null) return _vendor;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VendorRecipes", false)).FirstOrDefault(t => t is not null);
        if (type is null) return null;
        var field = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); } })
            .Select(t => t.GetField("VendorSales", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            .FirstOrDefault(f => f is not null && f.FieldType == type);
        return _vendor = field?.GetValue(null);
    }

    async Task VendorTab(Player player, PlayerState state)
    {
        if (!player.StashOpen) { Tell(player, "open your stash first"); return; }
        var stash = Game.StashOf(player);
        int tab = stash.LastTab;
        if (stash.TabType(tab) is not { } type || !IsGridType(type) || type == 2)
        {
            Tell(player, "vendor all works on normal, premium and quad tabs only");
            return;
        }
        var (_, inside) = stash.Inventory(tab);
        if (inside.Count == 0) { Tell(player, $"\"{stash.NameOf(tab)}\" is empty"); return; }
        if (_choices.VendorConfirm && (DateTime.UtcNow > state.VendorArmedUntil || state.VendorArmedTab != tab))
        {
            state.VendorArmedUntil = DateTime.UtcNow.AddSeconds(5);
            state.VendorArmedTab = tab;
            Tell(player, $"sell all {inside.Count} item(s) in \"{stash.NameOf(tab)}\"? Press vendor again within 5 seconds");
            return;
        }
        state.VendorArmedUntil = default;
        if (Vendor() is not { } vendor || vendor.GetType().GetMethod("Evaluate") is not { } evaluate)
        {
            Tell(player, "the vendor's prices are not available on this server");
            return;
        }
        var offered = inside.ToList();
        // Evaluate(offered, random, basicSaleMultiplier = null[, player = null]): its inputs are filled by type, so a server
        // that adds one (0.6 added the player) still works; any other optional input keeps its default.
        var inputs = evaluate.GetParameters().Select((p, i) =>
            i == 0 ? offered
            : p.ParameterType == typeof(Random) ? Game.AreaRng(player)
            : p.ParameterType.IsInstanceOfType(player) ? player
            : p.HasDefaultValue ? p.DefaultValue
            : null).ToArray();
        object quote = evaluate.Invoke(vendor, inputs)!;
        var rewards = (IReadOnlyList<Item>)quote.GetType().GetProperty("Rewards")!.GetValue(quote)!;
        var catalog = Game.ItemsFor(player);
        var gone = new List<uint>();
        var now = new List<InventoryItem>();
        long gold = 0;
        string? why = null;
        Saves.Atomically(() =>
        {
            foreach (var it in offered)
                if (stash.Take(tab, it.Id) is not null) gone.Add(it.Id);
            foreach (var reward in rewards)
            {
                if (catalog.ByHash(reward.TypeHash)?.Class == ItemDrops.GoldClass) { gold += reward.StackSize; continue; }
                if (stash.AddStacked(tab, reward, Game.ShapeOf, out string w) is { } s)
                {
                    now.AddRange(s.Changed);
                    now.AddRange(s.Added);
                    if (s.Left > 0) why = "some of the reward did not fit";
                }
                else why = w;
            }
            if (gold > 0) stash.AddGold(gold);
        });
        await Game.SendTabChange(player, stash, tab, gone, now.DistinctBy(i => i.Id).ToList(), $"qol: vendored stash tab {tab}");
        string got = string.Join(", ", rewards.GroupBy(r => catalog.ByHash(r.TypeHash)?.Name ?? "?")
            .Select(g => $"{g.Sum(r => r.StackSize)} {g.Key}"));
        Tell(player, $"sold {gone.Count} item(s) from \"{stash.NameOf(tab)}\" for {(got.Length > 0 ? got : "nothing")}" + (why is null ? "" : $" ({why})"));
    }

    // ---- sort the inventory ----

    static int Category(ItemCatalog.BaseType type, Item item) => type.Class switch
    {
        "StackableCurrency" or "DelveStackableSocketableCurrency" => 0,
        _ when type.MaxStack > 1 => 1,
        "Map" or "MapFragment" or "MiscMapItem" => 2,
        "DivinationCard" => 3,
        "Active Skill Gem" or "Support Skill Gem" => 4,
        "Jewel" or "AbyssJewel" => 5,
        "LifeFlask" or "ManaFlask" or "HybridFlask" or "UtilityFlask" or "UtilityFlaskCritical" => 6,
        "Amulet" or "Ring" or "Belt" => 7,
        _ => item.Rarity == ItemRarity.Unique ? 8 : 9,
    };

    async Task SortInventory(Player player)
    {
        var items = Game.ItemsOf(player);
        var catalog = Game.ItemsFor(player);
        var before = items.Items(Main).ToList();
        if (before.Count == 0) { Tell(player, "the inventory is empty"); return; }
        var slot = InstanceInventories.PlayerDefaults[Main];
        int width = slot.Width, height = slot.Height;

        // Plain stacks of one type (no modifiers, no sockets) are put together first.
        var merged = new List<InventoryItem>();
        var merges = new List<(InventoryItem Into, int Size)>();
        foreach (var group in before.GroupBy(i => i.Item.TypeHash))
        {
            var type = catalog.ByHash(group.Key);
            var plain = group.Where(i => type is { MaxStack: > 1 } && i.Item.Mods.Count == 0 && i.Item.Sockets.Count == 0).ToList();
            merged.AddRange(group.Except(plain));
            if (plain.Count == 0) continue;
            int total = plain.Sum(i => i.Item.StackSize), max = type!.MaxStack, k = 0;
            while (total > 0)
            {
                int size = Math.Min(max, total);
                total -= size;
                merged.Add(plain[k] with { Item = plain[k].Item with { StackSize = (ushort)size } });
                k++;
            }
        }
        var kept = merged.Select(i => i.Id).ToHashSet();

        List<(InventoryItem Item, byte W, byte H)>? Pack(IEnumerable<InventoryItem> order)
        {
            var used = new bool[width, height];
            var placed = new List<(InventoryItem, byte, byte)>();
            foreach (var it in order)
            {
                var shape = Game.ShapeOf(it.Item) ?? new PlayerInventories.Shape(1, 1, "");
                (int X, int Y)? cell = null;
                for (int x = 0; x + shape.Width <= width && cell is null; x++)
                    for (int y = 0; y + shape.Height <= height && cell is null; y++)
                    {
                        bool free = true;
                        for (int dx = 0; dx < shape.Width && free; dx++)
                            for (int dy = 0; dy < shape.Height && free; dy++) free = !used[x + dx, y + dy];
                        if (free) cell = (x, y);
                    }
                if (cell is not { } c) return null;
                for (int dx = 0; dx < shape.Width; dx++)
                    for (int dy = 0; dy < shape.Height; dy++) used[c.X + dx, c.Y + dy] = true;
                placed.Add((it with { X = (byte)c.X, Y = (byte)c.Y }, shape.Width, shape.Height));
            }
            return placed;
        }
        string Name(InventoryItem i) => catalog.ByHash(i.Item.TypeHash)?.Name ?? "";
        int Cat(InventoryItem i) => catalog.ByHash(i.Item.TypeHash) is { } t ? Category(t, i.Item) : 10;
        int Area(InventoryItem i) => Game.ShapeOf(i.Item) is { } s ? s.Width * s.Height : 1;
        var layout = Pack(merged.OrderBy(Cat).ThenByDescending(Area).ThenBy(Name).ThenByDescending(i => i.Item.Rarity).ThenByDescending(i => i.Item.StackSize))
            ?? Pack(merged.OrderByDescending(Area).ThenBy(Cat).ThenBy(Name));
        if (layout is null) { Tell(player, "the inventory is too full to sort"); return; }
        Saves.Atomically(() =>
        {
            foreach (var it in before) items.Remove(Main, it.Id);
            foreach (var (it, _, _) in layout) items.Put(Main, it);
        });
        var now = layout.Select(p => p.Item).ToList();
        await Program.Send(player, InstanceItems.InventoryChange(PlayerInventories.InventoryId(Main), items.NextRevision(Main),
            before.Select(i => i.Id).ToList(), now), $"qol: sorted the inventory ({now.Count} item(s), {before.Count - now.Count} stack(s) merged)");
        Tell(player, $"sorted {now.Count} item(s)" + (before.Count > now.Count ? $", {before.Count - now.Count} stack(s) merged" : ""));
    }

    // ---- sort the stash tab shown (normal, premium, quad) ----

    /// <summary>Items packed into a grid in this order, top to bottom then left to right (columns first, as the
    /// inventory sort); null when they do not fit.</summary>
    static List<InventoryItem>? PackGrid(IEnumerable<InventoryItem> order, int width, int height)
    {
        var used = new bool[width, height];
        var placed = new List<InventoryItem>();
        foreach (var it in order)
        {
            var shape = Game.ShapeOf(it.Item) ?? new PlayerInventories.Shape(1, 1, "");
            (int X, int Y)? cell = null;
            for (int x = 0; x + shape.Width <= width && cell is null; x++)
                for (int y = 0; y + shape.Height <= height && cell is null; y++)
                {
                    bool free = true;
                    for (int dx = 0; dx < shape.Width && free; dx++)
                        for (int dy = 0; dy < shape.Height && free; dy++) free = !used[x + dx, y + dy];
                    if (free) cell = (x, y);
                }
            if (cell is not { } c) return null;
            for (int dx = 0; dx < shape.Width; dx++)
                for (int dy = 0; dy < shape.Height; dy++) used[c.X + dx, c.Y + dy] = true;
            placed.Add(it with { X = (byte)c.X, Y = (byte)c.Y });
        }
        return placed;
    }

    async Task SortTab(Player player)
    {
        if (!player.StashOpen) { Tell(player, "open your stash first"); return; }
        var stash = Game.StashOf(player);
        var catalog = Game.ItemsFor(player);
        int tab = stash.LastTab;
        // NormalStash, PremiumStash (12 x 12) and QuadStash (24 x 24).
        if (stash.TabType(tab) is not (0 or 1 or 7) || stash.TabType(tab) is not { } type)
        { Tell(player, "sort tab works on normal, premium and quad tabs only"); return; }
        int size = type == 7 ? 24 : 12;
        var (_, before) = stash.Inventory(tab);
        if (before.Count == 0) { Tell(player, $"\"{stash.NameOf(tab)}\" is empty"); return; }
        string Name(InventoryItem i) => catalog.ByHash(i.Item.TypeHash)?.Name ?? "";
        int Cat(InventoryItem i) => catalog.ByHash(i.Item.TypeHash) is { } t ? Category(t, i.Item) : 10;
        int Area(InventoryItem i) => Game.ShapeOf(i.Item) is { } s ? s.Width * s.Height : 1;
        var layout = PackGrid(before.OrderBy(Cat).ThenByDescending(Area).ThenBy(Name).ThenByDescending(i => i.Item.Rarity).ThenByDescending(i => i.Item.StackSize), size, size)
            ?? PackGrid(before.OrderByDescending(Area).ThenBy(Cat).ThenBy(Name), size, size);
        if (layout is null) { Tell(player, $"\"{stash.NameOf(tab)}\" is too full to sort"); return; }
        var gone = new List<uint>();
        var now = new List<InventoryItem>();
        string? failed = null;
        Saves.Atomically(() =>
        {
            foreach (var it in before) if (stash.Take(tab, it.Id) is not null) gone.Add(it.Id);
            foreach (var it in layout)
            {
                if (stash.Place(tab, it.X, it.Y, it, Game.ShapeOf, out string why) is { } placed) now.Add(placed.Placed);
                else failed ??= why;
            }
        });
        // An item that did not go back (it should not happen: the layout fits) goes back where it was.
        if (failed is not null)
            foreach (var it in before.Where(b => now.All(n => n.Item != b.Item)))
                if (stash.Place(tab, it.X, it.Y, it, Game.ShapeOf, out _) is { } back) now.Add(back.Placed);
        await Game.SendTabChange(player, stash, tab, gone, now, $"qol: sorted stash tab {tab}");
        Tell(player, $"sorted {now.Count} item(s) in \"{stash.NameOf(tab)}\"" + (failed is null ? "" : $" ({failed})"));
    }

    // ---- upgrade maps: three of a tier make one of the next, as the blight tab's oils ----

    async Task UpgradeMaps(Player player)
    {
        if (!player.StashOpen) { Tell(player, "open your stash first"); return; }
        var stash = Game.StashOf(player);
        var catalog = Game.ItemsFor(player);
        var mapTab = stash.Records(null).FirstOrDefault(t => t.Type == 5 && !t.Hidden && t.HiddenKey == 0);
        if (mapTab is null) { Tell(player, "you have no map tab"); return; }
        var pages = stash.MapChildren(mapTab.Index).Prepend(mapTab.Index).Distinct().ToList();
        var rarities = _choices.Maps.Rarities.Select(r => Enum.TryParse<ItemRarity>(r, true, out var v) ? v : (ItemRarity?)null)
            .OfType<ItemRarity>().ToHashSet();
        // Every plain "Map (Tier N)" in the tab, with its page and tier.
        var maps = new List<(int Page, InventoryItem Item, int Tier)>();
        foreach (int page in pages)
            foreach (var it in stash.Inventory(page).Items)
                if (catalog.ByHash(it.Item.TypeHash) is { } type && type.Name.StartsWith("Map (Tier ", StringComparison.Ordinal)
                    && MapTier(stash, type.Hash) is int tier && rarities.Contains(it.Item.Rarity) && (_choices.Maps.Corrupted || !it.Item.Corrupted)
                    && !it.Item.Mirrored)
                    maps.Add((page, it, tier));
        int top = Math.Clamp(_choices.Maps.Top, 2, 16);
        var gone = new Dictionary<int, List<uint>>();
        var made = new Dictionary<int, List<InventoryItem>>();
        var counts = new SortedDictionary<int, int>();
        string? problem = null;
        Saves.Atomically(() =>
        {
            for (int tier = 1; tier < top; tier++)
            {
                var these = maps.Where(m => m.Tier == tier).OrderBy(m => m.Item.Item.Rarity).ThenBy(m => m.Item.Item.ItemLevel).ToList();
                while (these.Count >= 3)
                {
                    var three = these.Take(3).ToList();
                    these.RemoveRange(0, 3);
                    if (Game.ModDrop(player, $"Map (Tier {tier + 1})", Math.Min(100, 67 + tier + 1)) is not { } drop) { problem = $"no Map (Tier {tier + 1})"; return; }
                    int page = stash.MapDestination(mapTab.Index, drop.Item, Game.ShapeOf);
                    if (page < 0 || stash.AddStacked(page, drop.Item with { Rarity = ItemRarity.Normal }, Game.ShapeOf, out string why) is not { Added.Count: > 0 } added)
                    {
                        problem = $"no room for a tier {tier + 1} map";
                        return;
                    }
                    foreach (var (p, it, _) in three)
                        if (stash.Take(p, it.Id) is not null) (gone.TryGetValue(p, out var l) ? l : gone[p] = []).Add(it.Id);
                    (made.TryGetValue(page, out var m) ? m : made[page] = []).AddRange(added.Added);
                    counts[tier + 1] = counts.GetValueOrDefault(tier + 1) + 1;
                    // The new map counts for the next tier up: three tier 1s and six more can make a tier 3.
                    maps.Add((page, added.Added[0], tier + 1));
                    maps.RemoveAll(x => three.Any(t => t.Item.Id == x.Item.Id));
                }
            }
        });
        // A map made and used again in the same go never reaches the client.
        var madeIds = made.Values.SelectMany(l => l.Select(i => i.Id)).ToHashSet();
        var usedAgain = gone.Values.SelectMany(l => l).Where(madeIds.Contains).ToHashSet();
        foreach (var l in gone.Values) l.RemoveAll(usedAgain.Contains);
        foreach (var l in made.Values) l.RemoveAll(i => usedAgain.Contains(i.Id));
        foreach (int page in gone.Keys.Union(made.Keys))
            await Game.SendTabChange(player, stash, page, gone.GetValueOrDefault(page) ?? [], made.GetValueOrDefault(page) ?? [],
                $"qol: map upgrade on map tab page {page}");
        Tell(player, counts.Count == 0
            ? problem ?? "no tier has three maps to upgrade"
            : $"made {string.Join(", ", counts.Select(c => $"{c.Value} tier {c.Key}"))}" + (problem is null ? "" : $"; stopped: {problem}"));
    }
}
