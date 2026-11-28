using GameServer.Core.Protocol.Instance;

public sealed partial class QolMod
{
    const string CopiesTab = "Unique Copies";

    /// <summary>The "Unique Copies" quad tab, made the first time a copy needs it.</summary>
    static int CopiesTabOf(AccountStash stash, bool make)
    {
        var tab = stash.Records(null).FirstOrDefault(t => !t.Hidden && t.HiddenKey == 0 && t.Name.Equals(CopiesTab, StringComparison.OrdinalIgnoreCase));
        if (tab is not null) return tab.Index;
        if (!make) return -1;
        int index = stash.AddTabs(7, 1, CopiesTab, 0xaf, 0x60, 0x25)[0];
        stash.Rename(index, CopiesTab);
        return index;
    }

    DateTime _nextRefill;

    /// <summary>A unique tab cell left empty by taking its unique out gets the next copy from the copies tab.</summary>
    async Task RefillUniques(Player player)
    {
        if (!_choices.UniqueCopies || !player.StashOpen || DateTime.UtcNow < _nextRefill) return;
        _nextRefill = DateTime.UtcNow.AddSeconds(1);
        var stash = Game.StashOf(player);
        int copies = CopiesTabOf(stash, make: false);
        var uniqueTab = stash.Records(null).FirstOrDefault(t => t.Type == 4 && !t.Hidden && t.HiddenKey == 0);
        if (copies < 0 || uniqueTab is null) return;
        foreach (var copy in stash.Inventory(copies).Items.ToList())
        {
            if (copy.Item.Rarity != ItemRarity.Unique) continue;
            int page = stash.UniqueDestination(uniqueTab.Index, copy.Item);
            if (page < 0 || stash.Inventory(page).Items.Any(i => stash.Holds(page, i.Item) && SameUnique(i.Item, copy.Item))) continue;
            (InventoryItem Placed, InventoryItem? Displaced)? placed = null;
            Saves.Atomically(() =>
            {
                if (stash.Take(copies, copy.Id) is not { } taken) return;
                placed = stash.Place(page, 0, 0, taken, Game.ShapeOf, out _);
                if (placed is null || placed.Value.Displaced is not null) stash.AddStacked(copies, taken.Item, Game.ShapeOf, out _);
            });
            if (placed is not { Displaced: null } done) continue;
            await Game.SendTabChange(player, stash, copies, [copy.Id], [], "qol: a unique copy leaves the copies tab");
            await Game.SendTabChange(player, stash, page, [], [done.Placed], "qol: a unique copy fills its unique tab cell");
        }
    }

    static bool SameUnique(Item a, Item b) => a.TypeHash == b.TypeHash && a.NameWords.SequenceEqual(b.NameWords);
}

public sealed partial class QolMod
{
    /// <summary>content/stacks.json: every stackable item to <see cref="Choices.StackSize"/> (65,535 at most: the client
    /// reads a stack as 16 bits), in the client and in the server's copy of its data, applied by the content patcher on
    /// the next start. Divination cards (a set is a full stack) and items that become another item at a full stack
    /// (shards, splinters) keep their own size; so do gold and microtransactions.</summary>
    void WriteStackPack(string folder)
    {
        int size = Math.Clamp(_choices.StackSize, 1, 65535);
        string file = Path.Combine(folder, "content", "stacks.json");
        if (size <= 1) { if (File.Exists(file)) File.Delete(file); return; }
        var tables = new[] { "CurrencyItems", "BaseItemTypes", "ItemClasses" };
        if (!tables.All(GameServer.Core.ClientTables.Has)) return;
        var bases = GameServer.Core.ClientTables.Table("BaseItemTypes");
        var classes = GameServer.Core.ClientTables.Table("ItemClasses");
        var edits = new System.Text.Json.Nodes.JsonArray();
        foreach (var r in GameServer.Core.ClientTables.Table("CurrencyItems"))
        {
            if (r.LongOrNull("BaseItemTypesKey") is not long b || b < 0 || b >= bases.Count) continue;
            var baseRow = bases[(int)b];
            string? id = baseRow.Text("Id");
            string cls = baseRow.LongOrNull("ItemClassesKey") is long k && k >= 0 && k < classes.Count ? classes[(int)k].Text("Id") ?? "" : "";
            // Already raised (a patched client.db) or never more than one: judged by the class and the full stack only.
            if (id is null || cls is "DivinationCard" or "Gold" or "Microtransaction" or "InstanceLocalItem" || r.LongOrNull("FullStack_BaseItemTypesKey") is not null) continue;
            if (r.Int("StackSize") <= 1) continue;
            var set = new System.Text.Json.Nodes.JsonObject { ["StackSize"] = size };
            if (r.Has("CurrencyTab_StackSize") && r.Int("CurrencyTab_StackSize") > 0) set["CurrencyTab_StackSize"] = size;
            edits.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["table"] = "CurrencyItems",
                ["where"] = new System.Text.Json.Nodes.JsonObject { ["BaseItemTypesKey"] = new System.Text.Json.Nodes.JsonObject
                    { ["row"] = new System.Text.Json.Nodes.JsonObject { ["table"] = "BaseItemTypes", ["Id"] = id } } },
                ["set"] = set,
            });
        }
        var pack = new System.Text.Json.Nodes.JsonObject { ["edits"] = edits };
        string text = "// Written by the qol mod from its settings (stackSize); edit the setting, not this file.\n"
            + pack.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }) + "\n";
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (!File.Exists(file) || File.ReadAllText(file) != text)
        {
            File.WriteAllText(file, text);
            _log($"content/stacks.json: {edits.Count} stackable item(s) to {size}; applied by the content patcher on the next start");
        }
    }
}
