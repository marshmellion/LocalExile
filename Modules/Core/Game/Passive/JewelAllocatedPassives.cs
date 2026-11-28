/// <summary>
/// "Allocates X" on a jewel in the passive tree: mod_granted_passive_hash and its _2 to _5, the stats Megalomaniac's
/// notables use, with the node's PassiveSkillGraphId as the value. While the jewel sits in an allocated jewel socket (or
/// an open cluster socket) the character has that node: its stats, the skill it grants, its buffs and auras, as if it
/// were allocated. Any node counts, an ascendancy's from any class or family (a Phrecia node included, whatever the
/// character's own ascendancy), but a multiple-choice parent grants nothing: its options are nodes of their own. A node
/// the character has allocated already is not counted twice.
/// </summary>
public static partial class Game
{
    static readonly uint[] GrantedPassiveStats =
        [Stat.ModGrantedPassiveHash, Stat.ModGrantedPassiveHash2, Stat.ModGrantedPassiveHash3, Stat.ModGrantedPassiveHash4, Stat.ModGrantedPassiveHash5];

    /// <summary>The nodes the jewels in the player's passive tree allocate, each once, none the player allocated.</summary>
    public static IReadOnlyList<ushort> JewelAllocatedNodes(Player player)
    {
        var tree = PassivesFor(player);
        var mods = ItemsFor(player).Mods;
        var allocated = new HashSet<ushort>(player.Character.Passives);
        allocated.UnionWith(player.Character.MainAscendancy.Allocated);
        List<ushort>? result = null;
        foreach (var jewel in ItemsOf(player).Items(PlayerInventories.PassiveJewels))
        {
            if (!(tree.JewelNodeForSlot(jewel.X) is { } socket && allocated.Contains(socket) || ClusterSlotOpen(player, jewel.X))) continue;
            foreach (var mod in jewel.Item.Mods)
            {
                if (mods.ByHash(mod.Hash) is not { } def) continue;
                for (int j = 0; j < def.Stats.Count; j++)
                {
                    if (Array.IndexOf(GrantedPassiveStats, def.Stats[j].Id) < 0) continue;
                    int hash = j < mod.Values.Count ? mod.Values[j] : def.Stats[j].Min;
                    if (hash is <= 0 or > ushort.MaxValue || tree.Find((ushort)hash) is not { } node) continue;
                    if (node.IsStart || node.IsJewelSocket || node.RespecMultiplier == 0 && !node.IsExpansion && node.Stats.Count == 0) continue;
                    if (allocated.Contains(node.GraphId) || result?.Contains(node.GraphId) == true) continue;
                    (result ??= []).Add(node.GraphId);
                }
            }
        }
        return result ?? (IReadOnlyList<ushort>)[];
    }

    /// <summary>Every node whose effect the player has: the tree's, the ascendancy's and those jewels allocate.</summary>
    public static ushort[] EffectiveNodes(Player player)
    {
        var jewels = JewelAllocatedNodes(player);
        return jewels.Count == 0
            ? [.. player.Character.Passives, .. player.Character.MainAscendancy.Allocated]
            : [.. player.Character.Passives, .. player.Character.MainAscendancy.Allocated, .. jewels];
    }

    /// <summary>A jewel-allocated node's stats as the game takes them: its own, without the character's ascendancy policy
    /// (it is not the character's ascendancy). "+# to Maximum Power and Frenzy Charges" (a Phrecia node's) is the two
    /// maximums it is.</summary>
    public static IReadOnlyList<(uint Key, int Value)> JewelNodeStats(IReadOnlyList<(uint Key, int Value)> stats)
    {
        if (!stats.Any(s => s.Key == Stat.MaximumPowerAndFrenzyChargesPlus)) return stats;
        var result = new List<(uint Key, int Value)>(stats.Count + 1);
        foreach (var (key, value) in stats)
            if (key == Stat.MaximumPowerAndFrenzyChargesPlus) { result.Add((Stat.MaxFrenzyCharges, value)); result.Add((Stat.MaxPowerCharges, value)); }
            else result.Add((key, value));
        return result;
    }
}
