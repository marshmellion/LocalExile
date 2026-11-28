using GameServer.Core.Protocol.Instance;

/// <summary>What the Mirage league left that the server can still use: the Black Barya (the map device's FaridunBoss recipe
/// opens Saresh's arena with it) drops at chances of its own, Saresh drops one of the Mirage uniques, and Screams of the
/// Desiccated gets two shrine buffs. The server's data has only its first version (Acceleration Shrine); the game's had
/// one offensive and one other. With AnyShrines (on by default) they are any two of the thirteen; Divine Shrine was
/// never one of them.</summary>
sealed record MirageChoices(bool Enabled = true, bool MapsOnly = true, int BaryaBosses = 50, int BaryaRare = 3000, int BaryaStrongboxes = 150,
    int BaryaFaridunExiles = 5, int SareshUnique = 1, int BossMirageUnique = 0, bool ScreamsTwoShrines = true, bool AnyShrines = true,
    bool Announce = true);

public sealed partial class ExcitingItemsMod
{
    const string Barya = "The Black Barya";
    static readonly string[] MirageUniques = ["Khatal's Geyser", "Khatal's Weeping", "Saresh's Darkness", "Screams of the Desiccated",
        "Solerai's Radiance", "Fleshrender", "Skysunder", "The Bane of Hope", "The Flame of Hope", "The Desecrated Chalice", "The Sacred Chalice",
        "The Sands of Time", "The Broken Elegy"];
    // Screams of the Desiccated's shrines: the offensive ones and the others.
    static readonly string[] OffensiveShrines = ["SoulcordAccelerationShrineUnique__1", "SoulcordBrutalShrineUnique_1", "SoulcordDiamondShrineUnique_1",
        "SoulcordEchoingShrineUnique__1", "SoulcordMassiveShrineUnique_1", "SoulcordChillingShrineUnique_1", "SoulcordShockingShrineUnique_1",
        "SoulcordSkeletonShrineUnique_1"];
    static readonly string[] OtherShrines = ["SoulcordGloomShrineUnique__1", "SoulcordImpenetrableShrineUnique_1", "SoulcordReplenishingShrineUnique_1",
        "SoulcordResistanceShrineUnique_1", "SoulcordResonatingShrineUnique_1"];

    static bool IsShrineMod(ModCatalog.Mod? m) => m is not null && m.Id.StartsWith("Soulcord", StringComparison.Ordinal) && m.Id.Contains("Shrine", StringComparison.Ordinal);

    /// <summary>Screams of the Desiccated with two shrines (null: not one, or the setting is off).</summary>
    Item? WithTwoShrines(Player player, Item item, Random rng)
    {
        var c = _choices.Mirage ?? new();
        if (!c.ScreamsTwoShrines || item.Rarity != ItemRarity.Unique) return null;
        var mods = Game.ItemsFor(player).Mods;
        if (!item.Mods.Any(m => IsShrineMod(mods.ByHash(m.Hash)))) return null;
        var all = OffensiveShrines.Concat(OtherShrines).Select(mods.ById).OfType<ModCatalog.Mod>().ToList();
        ModCatalog.Mod? first, second;
        if (c.AnyShrines)
        {
            first = all[rng.Next(all.Count)];
            var rest = all.Where(m => m.Hash != first.Hash).ToList();
            second = rest[rng.Next(rest.Count)];
        }
        else
        {
            var offence = OffensiveShrines.Select(mods.ById).OfType<ModCatalog.Mod>().ToList();
            var other = OtherShrines.Select(mods.ById).OfType<ModCatalog.Mod>().ToList();
            if (offence.Count == 0 || other.Count == 0) return null;
            first = offence[rng.Next(offence.Count)];
            second = other[rng.Next(other.Count)];
        }
        var kept = item.Mods.Where(m => !IsShrineMod(mods.ByHash(m.Hash))).ToList();
        kept.Add(first.Roll(ModSection.Explicit, rng));
        kept.Add(second.Roll(ModSection.Explicit, rng));
        return item with { Mods = kept, RequiredLevel = null };
    }

    void MirageDrops(DropRoll d)
    {
        var c = _choices.Mirage ?? new();
        if (!c.Enabled) return;
        var player = d.Player;
        // Screams of the Desiccated, whatever dropped it.
        d.Changes.Add((drop, rng) => WithTwoShrines(player, drop.Item, rng) is { } two ? [drop with { Item = two }] : [drop]);
        bool saresh = d.Monster.VarietyId.Contains("FaridunSareshBoss", StringComparison.Ordinal);
        if (saresh && c.SareshUnique > 0 && Random.Shared.Next(c.SareshUnique) == 0) DropMirageUnique(d);
        else if (!saresh && d.Monster.Rarity == MonsterRarity.Unique && c.BossMirageUnique > 0 && Random.Shared.Next(c.BossMirageUnique) == 0) DropMirageUnique(d);
        if (c.MapsOnly && Game.MapRunOf(player) is not { Atlas: true }) return;
        int chance = d.Monster.VarietyId.Contains("Faridun", StringComparison.Ordinal) && !saresh ? c.BaryaFaridunExiles
            : d.Monster.Rarity switch { MonsterRarity.Unique => c.BaryaBosses, MonsterRarity.Rare => c.BaryaRare, _ => 0 };
        if (chance > 0 && Random.Shared.NextDouble() * chance < Math.Max(0, d.Quantity))
        {
            d.Stacks.Add((Barya, 1));
            if (c.Announce) Tell(player, "* The Black Barya dropped. Put it in the map device to face Saresh, of the Weeping Black.");
        }
    }

    void DropMirageUnique(DropRoll d)
    {
        var uniques = Game.UniquesFor(d.Player);
        var drops = Game.DropsFor(d.Player);
        var pool = MirageUniques.Select(n => uniques.Find(n)).OfType<UniqueCatalog.Definition>().ToList();
        if (pool.Count == 0) return;
        // As likely as each drops elsewhere: Screams of the Desiccated and The Sands of Time are the rarest.
        double total = pool.Sum(u => Math.Max(0.001, drops.UniqueWeight(u.Name)));
        double at = Random.Shared.NextDouble() * total;
        var pick = pool.First(u => (at -= Math.Max(0.001, drops.UniqueWeight(u.Name))) <= 0 || ReferenceEquals(u, pool[^1]));
        if (uniques.Create(pick, Random.Shared) is not { } item) return;
        item = WithTwoShrines(d.Player, item, Random.Shared) ?? item;
        d.Made.Add(item);
        if ((_choices.Mirage ?? new()).Announce) Tell(d.Player, $"* A Mirage unique dropped: {pick.Name}.");
    }

    void MirageChests(Player player, string kind, string id, List<ItemDrops.Drop> drops)
    {
        var c = _choices.Mirage ?? new();
        if (!c.Enabled || c.BaryaStrongboxes <= 0 || !kind.EndsWith("strongbox", StringComparison.Ordinal) || Random.Shared.Next(c.BaryaStrongboxes) != 0) return;
        if (Game.ItemsFor(player).All.FirstOrDefault(t => t.Name == Barya && t.Supported) is not { } type) return;
        drops.Add(new ItemDrops.Drop(ItemCatalog.Create(type) with { StackSize = 1 }, type));
        if (c.Announce) Tell(player, "* The Black Barya came from the strongbox.");
    }
}
