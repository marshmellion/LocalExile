using System.Text.Json;
using System.Text.Json.Nodes;
using GameServer.Core.Protocol.Instance;

/// <summary>Legacy affixes: modifiers that could roll in an older patch and no longer do (legacymods.json, from old
/// patch data). A magic or rare drop sometimes gets one, on the item tags it rolled on then, and the Legacy mark (an
/// enchantment of no stats that loot filters show), which is not the Exciting mark.</summary>
sealed record LegacyModChoices(bool Enabled = true, int Chance = 150, string[]? Rarities = null, bool Mark = true,
    bool Replace = true, int Most = 1);

public sealed partial class ExcitingItemsMod
{
    const string LegacyMarkerId = "LocalExileLegacyMarker", LegacyMarkerName = "Legacy";
    static ModCatalog.Mod? LegacyMarker(ItemCatalog catalog) => catalog.Mods.ById(LegacyMarkerId);

    sealed record LegacyAffix(string Id, (string Tag, int Weight)[] Weights);
    List<LegacyAffix> _legacyAffixes = [];
    volatile bool _nextLegacy;

    void LoadLegacyAffixes(IModContext mod)
    {
        string file = Path.Combine(mod.Folder, "legacymods.json");
        if (!File.Exists(file)) { mod.Log($"legacymods.json is missing from {mod.Folder}: no legacy affixes"); return; }
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            _legacyAffixes = (root?["mods"] as JsonArray ?? []).OfType<JsonObject>().Select(m => new LegacyAffix(
                (string?)m["id"] ?? "",
                (m["weights"] as JsonArray ?? []).OfType<JsonArray>().Select(w => ((string?)w[0] ?? "", (int?)w[1] ?? 0)).ToArray()))
                .Where(a => a.Id.Length > 0).ToList();
            mod.Log($"{_legacyAffixes.Count} legacy affixes");
        }
        catch (Exception e) { mod.Log($"legacymods.json not read: {e.Message}"); }
    }

    /// <summary>The weight a legacy affix had on these tags: the first of its tags the base has decides.</summary>
    static int OldWeight(LegacyAffix a, IReadOnlySet<string> tags)
    {
        foreach (var (tag, weight) in a.Weights) if (tags.Contains(tag)) return weight;
        return 0;
    }

    void LegacyDrops(DropRoll d)
    {
        var c = _choices.LegacyMods ?? new();
        if (!c.Enabled || _legacyAffixes.Count == 0) return;
        var player = d.Player;
        d.Changes.Add((drop, rng) =>
        {
            bool forced = _nextLegacy;
            if (!forced && rng.Next(Math.Max(1, c.Chance)) != 0) return [drop];
            if (WithLegacyAffix(player, drop.Item, drop.Type, rng, c) is not { } changed) return [drop];
            if (forced) _nextLegacy = false;
            if (_choices.Announce) Tell(player, "* A legacy affix dropped.");
            return [drop with { Item = changed }];
        });
    }

    Item? WithLegacyAffix(Player player, Item item, ItemCatalog.BaseType type, Random rng, LegacyModChoices c)
    {
        var rarities = (c.Rarities ?? ["magic", "rare"]).Select(r => Enum.TryParse<ItemRarity>(r, true, out var v) ? v : (ItemRarity?)null).OfType<ItemRarity>().ToHashSet();
        if (!rarities.Contains(item.Rarity) || !type.Components.Contains("Mods") || item.FracturedCount > 0 || item.Corrupted) return null;
        var catalog = Game.ItemsFor(player);
        var rolls = Game.RollsFor(player);
        var tags = rolls.TagsWith(type, item.Influences);
        var mods = item.Mods.ToList();
        int SideOf(ItemMod m) => catalog.Mods.ByHash(m.Hash)?.GenerationType ?? 0;
        var (mostPrefixes, mostSuffixes) = item.Rarity switch
        {
            ItemRarity.Rare => rolls.RareMaxAffixes(item),
            ItemRarity.Magic => (1, 1),
            _ => (3, 3),
        };
        int added = 0;
        for (int round = 0; round < Math.Max(1, c.Most); round++)
        {
            var families = new HashSet<int>(mods.SelectMany(m => catalog.Mods.ByHash(m.Hash)?.Families ?? []));
            var open = _legacyAffixes
                .Select(a => (Mod: catalog.Mods.ById(a.Id), Weight: OldWeight(a, tags)))
                .Where(p => p.Mod is { GenerationType: ItemRolls.Prefix or ItemRolls.Suffix } m && p.Weight > 0 && m.Level <= Math.Max(1, (int)item.ItemLevel)
                    && !m.Families.Any(families.Contains) && mods.All(x => x.Hash != m.Hash))
                .Select(p => (Mod: p.Mod!, p.Weight)).ToList();
            if (open.Count == 0) break;
            int at = rng.Next(open.Sum(p => p.Weight));
            var mod = open.First(p => (at -= p.Weight) < 0).Mod;
            int side = mod.GenerationType;
            int have = mods.Count(m => m.Section == ModSection.Explicit && SideOf(m) == side);
            if (have >= (side == ItemRolls.Prefix ? mostPrefixes : mostSuffixes))
            {
                if (!c.Replace) continue;
                var same = mods.Where(m => m.Section == ModSection.Explicit && SideOf(m) == side).ToList();
                mods.Remove(same[rng.Next(same.Count)]);
            }
            mods.Add(mod.Roll(ModSection.Explicit, rng));
            added++;
        }
        if (added == 0) return null;
        if (c.Mark && LegacyMarker(catalog) is { } marker && mods.All(m => m.Hash != marker.Hash))
            mods.Add(new ItemMod(ModSection.Enchant, marker.Hash, []));
        return item with { Mods = mods, RequiredLevel = null };
    }

    static string[] LegacyFilterRule() =>
    [
        "Show",
        $"    HasEnchantment \"{LegacyMarkerName}\"",
        "    SetFontSize 42",
        "    SetTextColor 255 255 255 255",
        "    SetBorderColor 255 120 0 255",
        "    SetBackgroundColor 70 35 0 255",
        "    PlayAlertSound 2 300",
        "    PlayEffect Orange",
        "    MinimapIcon 1 Orange Diamond",
    ];
}
