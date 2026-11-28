using GameServer.Core.Protocol.Instance;

/// <summary>
/// Hand-overs this local copy adds for private mods (not part of LocalExile). Each call site is one line, so an update
/// of main merges around them: SupportedSkillAt (GemSupports.Rules.cs), UseItemOn and CurrencyResult (Items.cs), and
/// the Imprint item component in GameServer.Core (InstanceItems.cs: its Supported list and its Payload case).
/// </summary>
public static class LocalHooks
{
    /// <summary>The supports that apply to a socketed skill, before their stats are laid over it: the item the gem is
    /// in and the resolved supports; the answer replaces them (same order, same count).</summary>
    public static readonly GameEvent<Func<Player, Item, IReadOnlyList<GemSupports.Applied>, IReadOnlyList<GemSupports.Applied>>> Supports = new();

    /// <summary>A socketed skill's own stats, after its supports: the skill id, its GrantedEffects row, level and
    /// quality, the gem, the item it is in, and the stats by key, to change in place.</summary>
    public static readonly GameEvent<Action<Player, ushort, int, int, int, Item, Item, Dictionary<uint, int>>> SkillStats = new();

    /// <summary>A currency used on an item, before the server's own currencies: the target, the currency's type hash,
    /// the currency item itself when the use came from UseItemOn (null for a preview), and the random to roll with.
    /// The answer is the changed item, or null to leave it to the server.</summary>
    public static readonly GameEvent<Func<Player, Item, uint, Item?, Random, Item?>> CurrencyItemUse = new();

    /// <summary>The currency item UseItemOn is applying, for the CurrencyResult call that follows on the same thread
    /// (no await lies between them). Read once, then cleared.</summary>
    [ThreadStatic] public static Item? UsedItem;

    public static IReadOnlyList<GemSupports.Applied> WithSupports(Player player, Item host, IReadOnlyList<GemSupports.Applied> supports)
    {
        if (!Supports.Any) return supports;
        foreach (var (owner, run) in Supports.Subscribers(player))
            GameEventOwners.Guard(owner, "local supports", () => supports = run(player, host, supports));
        return supports;
    }

    public static void RunSkillStats(Player player, ushort skill, int effect, int level, int quality, Item gem, Item host, Dictionary<uint, int> own)
    {
        if (!SkillStats.Any) return;
        foreach (var (owner, run) in SkillStats.Subscribers(player))
            GameEventOwners.Guard(owner, "local skill stats", () => run(player, skill, effect, level, quality, gem, host, own));
    }

    public static Item? RunCurrencyUse(Player player, Item target, uint currency, Random random)
    {
        var used = UsedItem;
        UsedItem = null;
        if (!CurrencyItemUse.Any) return null;
        if (used is not null && used.TypeHash != currency) used = null;
        Item? result = null;
        foreach (var (owner, run) in CurrencyItemUse.Subscribers(player))
        {
            GameEventOwners.Guard(owner, "local currency use", () => result = run(player, target, currency, used, random));
            if (result is not null) return result;
        }
        return null;
    }
}
