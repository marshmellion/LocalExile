using GameServer.Core.Protocol.Instance;

/// <summary>Crucible trees, pre-rolled: the client still has Crucible's weapon tree nodes (WeaponPassiveSkills: 2,632
/// nodes, each a modifier, in five columns), but the server cannot send a tree to choose from, so a drop gets a path of
/// nodes already allocated, one per column from the first, for its kind of item and item level. The nodes' modifiers
/// show as lines of their own (the enchantment list by default) and work as any modifier.</summary>
sealed record CrucibleChoices(bool Enabled = true, int Chance = 300, int MinNodes = 2, int MaxNodes = 4, int NotableChance = 15,
    bool Uniques = true, string[]? Rarities = null, string[]? Classes = null, string Section = "enchant", bool IgnoreItemLevel = false);

public sealed partial class ExcitingItemsMod
{
    sealed record TreeNode(string Id, int Tier, int Type, int[] Columns, ModCatalog.Mod Mod, HashSet<string> Kinds);
    List<TreeNode>? _treeNodes;

    static readonly string[] CrucibleDefaultClasses = ["Claw", "Dagger", "Rune Dagger", "Wand", "One Hand Sword", "Thrusting One Hand Sword",
        "One Hand Axe", "One Hand Mace", "Sceptre", "Bow", "Staff", "Warstaff", "Two Hand Sword", "Two Hand Axe", "Two Hand Mace", "Shield"];

    List<TreeNode> TreeNodes(ItemCatalog catalog)
    {
        if (_treeNodes is not null) return _treeNodes;
        var list = new List<TreeNode>();
        if (GameServer.Core.ClientTables.Has("WeaponPassiveSkills"))
            foreach (var row in GameServer.Core.ClientTables.Table("WeaponPassiveSkills"))
            {
                if (row.IntOrNull("Mod") is not int modRow || catalog.Mods.Row(modRow) is not { } mod) continue;
                var kinds = mod.SpawnWeights.Select(w => w.Tag).Where(t => t != "default").ToHashSet();
                list.Add(new TreeNode(row.Text("Id") ?? "", row.IntOrNull("Tier") ?? 1, row.IntOrNull("Type") ?? 0, row.Ints("NodeSpawnLocation"), mod, kinds));
            }
        _log($"{list.Count} Crucible tree nodes");
        return _treeNodes = list;
    }

    /// <summary>A path of pre-allocated nodes on the item, or null when it cannot have one.</summary>
    Item? WithCrucibleTree(Player player, Item item, Random rng, List<string> log, CrucibleChoices c)
    {
        var catalog = Game.ItemsFor(player);
        if (catalog.ByHash(item.TypeHash) is not { } type || !(c.Classes ?? CrucibleDefaultClasses).Contains(type.Class)) return null;
        if (item.Rarity == ItemRarity.Unique && !c.Uniques) return null;
        var tags = Game.RollsFor(player).TagsWith(type, item.Influences);
        var nodes = TreeNodes(catalog).Where(n => n.Kinds.Count == 0 || n.Kinds.Any(tags.Contains))
            .Where(n => c.IgnoreItemLevel || n.Mod.Level <= Math.Max(1, (int)item.ItemLevel)).ToList();
        if (nodes.Count == 0) return null;
        int count = rng.Next(Math.Clamp(c.MinNodes, 1, 5), Math.Clamp(Math.Max(c.MinNodes, c.MaxNodes), 1, 5) + 1);
        var section = c.Section == "explicit" ? ModSection.Explicit : c.Section == "implicit" ? ModSection.Implicit : ModSection.Enchant;
        var mods = item.Mods.ToList();
        var taken = new HashSet<string>();
        int added = 0;
        for (int column = 1; column <= 5 && added < count; column++)
        {
            bool notable = rng.Next(100) < c.NotableChance;
            // One node per column: of its type (a notable now and then), not a group already on the path, its best tier the
            // item level allows.
            var here = nodes.Where(n => n.Columns.Contains(column) && !taken.Contains(n.Id) && (n.Type > 0) == notable).ToList();
            if (here.Count == 0) here = nodes.Where(n => n.Columns.Contains(column) && !taken.Contains(n.Id)).ToList();
            if (here.Count == 0) continue;
            var groups = here.GroupBy(n => n.Id).ToList();
            var group = groups[rng.Next(groups.Count)];
            var node = group.OrderByDescending(n => n.Tier).First();
            taken.Add(node.Id);
            var rolled = node.Mod.Roll(section, rng);
            mods.Add(rolled);
            log.Add($"Crucible node {column}: \"{catalog.Mods.Describe(rolled)}\"{(node.Type > 0 ? " (notable)" : "")}");
            added++;
        }
        return added == 0 ? null : item with { Mods = mods, RequiredLevel = null };
    }
}
