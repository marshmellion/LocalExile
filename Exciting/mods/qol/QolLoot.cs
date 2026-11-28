using GameServer.Core.Protocol.Instance;

public sealed partial class QolMod
{
    LootFilter? _filter;

    /// <summary>The filter the client has chosen (production_Config.ini's item_filter, in the LocalExile config folder),
    /// read again when the choice or the file changes; null when none can be found.</summary>
    LootFilter? Filter(out string why)
    {
        why = "";
        string documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Path of Exile");
        string root = _choices.Loot.FilterFolder.Trim().Length > 0 ? _choices.Loot.FilterFolder.Trim() : documents;
        string? chosen = null;
        foreach (string config in new[] { Path.Combine(root, "LocalExile", "production_Config.ini"), Path.Combine(root, "production_Config.ini") })
        {
            if (!File.Exists(config)) continue;
            chosen = File.ReadLines(config).Select(l => l.Trim()).Where(l => l.StartsWith("item_filter=", StringComparison.Ordinal))
                .Select(l => l["item_filter=".Length..].Trim()).FirstOrDefault();
            if (chosen is not null) break;
        }
        if (string.IsNullOrEmpty(chosen)) { why = "no loot filter is chosen in the game's options"; return null; }
        string name = chosen.TrimStart('|');
        string[] places = [Path.Combine(root, name), Path.Combine(root, "LocalExile", name), Path.Combine(root, "OnlineFilters", name)];
        string? path = places.FirstOrDefault(File.Exists);
        if (path is null) { why = $"the chosen filter \"{chosen}\" is not in {root}"; return null; }
        if (_filter is { } known && known.Path == path && known.Written == File.GetLastWriteTimeUtc(path)) return known;
        try { return _filter = LootFilter.Read(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = $"the filter cannot be read: {e.Message}"; return null; }
    }

    LootFilter.Facts FactsOf(Player player, ItemCatalog.BaseType type, Item item, int areaLevel, int mapTier)
    {
        var mods = Game.ItemsFor(player).Mods;
        var groups = new List<string>();
        int at = 0;
        string Letter(ItemSocket s) => s.Colour switch
        {
            SocketColour.Red => "R", SocketColour.Green => "G", SocketColour.Blue => "B", SocketColour.White => "W",
            SocketColour.Abyss => "A", SocketColour.Delve => "D", _ => "",
        };
        foreach (byte size in item.Links ?? [])
        {
            groups.Add(string.Concat(item.Sockets.Skip(at).Take(size).Select(Letter)));
            at += size;
        }
        if (at < item.Sockets.Count) groups.AddRange(item.Sockets.Skip(at).Select(Letter));
        return new(ClassName(type.Class), type.Name, item.Rarity, item.ItemLevel, type.DropLevel, areaLevel, item.StackSize,
            item.Quality, item.Identified, item.Corrupted, item.Mirrored, item.FracturedCount > 0, item.Synthesised, item.Vestigial,
            item.Sockets.Count, groups.Count == 0 ? 0 : groups.Max(g => g.Length), groups, type.Width, type.Height, mapTier,
            item.Influences, [.. item.Mods.Where(m => m.Section == ModSection.Explicit).Select(m => mods.ByHash(m.Hash)?.Name ?? "")],
            item.Mods.Any(m => m.Section == ModSection.Enchant), item.MemoryStrands,
            [.. item.Mods.Where(m => m.Section == ModSection.Implicit).Select(m => mods.ByHash(m.Hash)?.Name ?? "")],
            [.. item.Mods.Where(m => m.Section == ModSection.Enchant).Select(m => mods.ByHash(m.Hash)?.Name ?? "")]);
    }

    // ---- bring the loot the filter shows to the player's feet ----

    async Task BringLoot(Player player)
    {
        string why = "";
        var filter = _choices.Loot.IgnoreFilter ? null : Filter(out why);
        if (filter is null && !_choices.Loot.IgnoreFilter) { Tell(player, why + ": nothing brought (set ignoreFilter to bring everything)"); return; }
        var ground = player.Instance.Ground;
        var catalog = Game.ItemsFor(player);
        var stash = Game.StashOf(player);
        var me = Program.PlayerOf(player).Position;
        int areaLevel = ServerLevels.AreaLevel(player.Layout.Id);
        var rng = Game.AreaRng(player);
        var moved = new List<(uint Old, uint New, WorldItemObject Item)>();
        int hidden = 0;
        foreach (var (id, lying) in ground.All())
        {
            if (moved.Count >= _choices.Loot.MaxItems) break;
            if (catalog.ByHash(lying.Item.TypeHash) is not { } type) continue;
            var (by, shared) = ground.Origin(id);
            if (!shared && !ReferenceEquals(by, player)) continue;
            if (lying.PlayerDropped && !_choices.Loot.PlayerDropped) continue;
            bool gold = type.Class == ItemDrops.GoldClass;
            if (gold && !_choices.Loot.Gold) continue;
            double dx = lying.GridX - me.X, dy = lying.GridY - me.Y;
            if (dx * dx + dy * dy <= _choices.Loot.LeaveWithin * _choices.Loot.LeaveWithin) continue;
            if (!gold && filter is not null && !filter.Shows(FactsOf(player, type, lying.Item, areaLevel, MapTier(stash, type.Hash) ?? 0)))
            {
                hidden++;
                continue;
            }
            if (ground.Take(id, lying) is null) continue;
            var (x, y) = Game.ScatterDropPosition(player, me, rng);
            var there = lying with { GridX = x, GridY = y };
            uint newId = ground.Add(there, by, shared, justDropped: true);
            moved.Add((id, newId, there));
        }
        foreach (var (old, newId, there) in moved)
        {
            await Game.SendGroundItem(player, newId, there, $"qol: loot 0x{old:x} -> 0x{newId:x} at the player's feet", justDropped: true);
            await Game.SendWorldRemoval(player, old, $"qol: loot 0x{old:x} brought to the player");
        }
        Tell(player, moved.Count == 0 ? "no loot to bring" + (hidden > 0 ? $" ({hidden} hidden by the filter)" : "")
            : $"brought {moved.Count} item(s)" + (hidden > 0 ? $", left {hidden} the filter hides" : ""));
    }
}
