using GameServer.Core.Protocol.Instance;

namespace GameServer.Modules.Atlas;

/// <summary>
/// The unique a Valdo Map or the Voidborn Reliquary rewards, by its name in world.db. Exact names only, never
/// UniqueCatalog.Find's substring fallback ("Cortex" must not become Replica Cortex): a class or base variant
/// ("Forbidden Flame (Witch)"), an exact unique, a legendary map (Cortex, Vaults of Atziri: UniqueCatalog.NamedMap), or
/// Impossible Escape (its catalogue, a random keystone). Null when the name is none of them.
/// </summary>
internal static class FoilRewards
{
    public static Item? Create(Player player, string name, Random random)
    {
        var uniques = UniquesFor(player);
        string key = UniqueCatalog.NameKey(name);
        int variant = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (variant > 0 && name.EndsWith(')') && uniques.Find(name) is { CreationVariant: not null } chosen
            && UniqueCatalog.NameKey(chosen.Name).Equals(UniqueCatalog.NameKey(name[..variant]), StringComparison.OrdinalIgnoreCase))
            return uniques.Create(chosen, random);
        if (uniques.All.FirstOrDefault(d => UniqueCatalog.NameKey(d.Name).Equals(key, StringComparison.OrdinalIgnoreCase)) is { } exact)
            return uniques.Create(exact, random);
        if (uniques.NamedMap(name) is { } map) return map;
        if (key.Equals("Impossible Escape", StringComparison.OrdinalIgnoreCase) && ImpossibleEscapeCatalogue.All is { Count: > 0 } keystones)
            return ImpossibleEscapeCatalogue.Create(ItemsFor(player), keystones[random.Next(keystones.Count)].Name);
        return null;
    }

    /// <summary>The reward's name word (Words HASH32) for the Valdo Map's "Reward: Foil {0}" line; null when it resolves
    /// to nothing.</summary>
    public static uint? NameWord(Player player, string name, Random random) =>
        Create(player, name, random) is { NameWords.Count: > 0 } item ? item.NameWords[0] : null;
}
