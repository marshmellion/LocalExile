using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GameServer.Core.Protocol.Instance;

public sealed partial class ExcitingItemsMod
{
    const string KrangleId = "Metadata/Items/Currency/LocalExileKrangleOrb";

    // The Krangle Orb (content/krangleorb.json): the Blood Crucible's and the Dream Furnace's transform. The item or map
    // becomes corrupted and gets one Scourge upside and one downside (the game's own Hellscape modifiers, generation
    // types 24 and 25), replacing a pair it had. Tier: 1 from item level 1, 2 from 45, 3 from 68, each as likely as the
    // others the item level allows; tier 4 (68+) 1 in Tier4 of those. Rarities: which items may be transformed.
    sealed record KrangleChoices(bool Enabled = true, int DropChance = 2500, int Tier4 = 8, string[]? Rarities = null, bool Maps = true);

    static readonly Regex TierOf = new(@"(\d)_*$", RegexOptions.Compiled);
    const int Upside = 24, Downside = 25;

    // The Scourge modifiers by side, read once per catalog.
    readonly ConditionalWeakTable<ModCatalog, Dictionary<int, List<(ModCatalog.Mod Mod, int Tier)>>> _scourge = new();

    Dictionary<int, List<(ModCatalog.Mod Mod, int Tier)>> Scourge(ModCatalog mods) => _scourge.GetValue(mods, m =>
    {
        var sides = new Dictionary<int, List<(ModCatalog.Mod, int)>> { [Upside] = [], [Downside] = [] };
        foreach (var mod in m.All)
            if (mod.Domain == 1 && mod.GenerationType is Upside or Downside && mod.Id.StartsWith("Hellscape", StringComparison.Ordinal))
                sides[mod.GenerationType].Add((mod, TierOf.Match(mod.Id) is { Success: true } t ? t.Groups[1].Value[0] - '0' : 1));
        return sides;
    });

    Item? KrangleUse(Player player, Item target, Random random)
    {
        var c = _choices.KrangleOrb ?? new();
        if (!c.Enabled) { Tell(player, "Krangle Orb: turned off in Exciting Items' settings."); return target; }
        if (target.Foreseeing) return target;
        var items = Game.ItemsFor(player);
        if (items.ByHash(target.TypeHash) is not { } type || !type.Components.Contains("Mods")) { Tell(player, "Krangle Orb: use it on equipment or a map."); return target; }
        bool map = type.Class is "Map" or "MapKey";
        var rarities = (c.Rarities ?? ["rare", "unique"]).Select(r => Enum.TryParse<ItemRarity>(r, true, out var v) ? v : (ItemRarity?)null).OfType<ItemRarity>().ToHashSet();
        if (map ? !c.Maps : !rarities.Contains(target.Rarity)) { Tell(player, "Krangle Orb: that item cannot be scourged."); return target; }
        var tags = Game.RollsFor(player).TagsWith(type, target.Influences);
        int top = target.ItemLevel >= 68 ? 3 : target.ItemLevel >= 45 ? 2 : 1;
        int tier = 1 + random.Next(top);
        if (top == 3 && random.Next(Math.Max(1, c.Tier4)) == 0) tier = 4;
        var sides = Scourge(items.Mods);
        ItemMod? Pick(int side)
        {
            // The tier asked for, or the nearest one below that has a modifier for this base.
            for (int t = tier; t >= 1; t--)
            {
                var pool = sides[side].Where(p => p.Tier == t && p.Mod.Level <= Math.Max(1, (int)target.ItemLevel))
                    .Select(p => (p.Mod, Weight: p.Mod.WeightFor(tags))).Where(p => p.Weight > 0).ToList();
                if (pool.Count == 0) continue;
                int at = random.Next(pool.Sum(p => p.Weight));
                return pool.First(p => (at -= p.Weight) < 0).Mod.Roll(ModSection.Scourge, random);
            }
            return null;
        }
        if (Pick(Upside) is not { } up || Pick(Downside) is not { } down) { Tell(player, "Krangle Orb: nothing can scourge that item."); return target; }
        var mods = target.Mods.Where(m => m.Section != ModSection.Scourge).Append(up).Append(down).ToList();
        Tell(player, $"Krangle Orb: tier {tier} scourge: {items.Mods.Describe(up)} / {items.Mods.Describe(down)}.");
        return target with { Mods = mods, Corrupted = true, RequiredLevel = null };
    }
}
