using GameServer.Core.Protocol.Instance;

public sealed partial class ExcitingItemsMod
{
    const string LimitBreakId = "Metadata/Items/Currency/LocalExileLimitBreakOrb";

    // The Limit Break Orb (content/limitbreakorb.json): one more socket than the item may have, up to Max. The server's
    // own socket limits stay the game's, so this orb is the only way past them. AnyItem: also corrupted and mirrored
    // items. LinkNew: the new socket joins the last link group.
    sealed record LimitBreakChoices(bool Enabled = true, int DropChance = 3000, int Max = 6, bool AnyItem = false, bool LinkNew = false);

    /// <summary>Every currency of this mod used on an item: the result, the target itself to refuse, or null when it is
    /// none of this mod's (GameEvents.CurrencyOnItem).</summary>
    Item? CurrencyOnItem(Player player, Item target, Item used, ItemCatalog.BaseType currency, Random random) => currency.Id switch
    {
        OrbId => OrbUse(player, target, currency.Hash, used, random),
        LimitBreakId => LimitBreakUse(player, target, random),
        KrangleId => KrangleUse(player, target, random),
        DefiledId => DefiledUse(player, target, random),
        MistId => MistUse(player, target, random),
        ArmourRecombinator or WeaponRecombinator or JewelleryRecombinator => RecombinatorUse(player, target, currency.Id, random),
        _ => _legacyCurrencies?.Use(player, target, used, currency, random),
    };

    Item? LimitBreakUse(Player player, Item target, Random random)
    {
        var c = _choices.LimitBreakOrb ?? new();
        if (!c.Enabled) { Tell(player, "Limit Break Orb: turned off in Exciting Items' settings."); return target; }
        if (target.Foreseeing) return target;
        if (!c.AnyItem && (target.Corrupted || target.Mirrored)) { Tell(player, "Limit Break Orb: not on a corrupted or mirrored item."); return target; }
        var type = Game.ItemsFor(player).ByHash(target.TypeHash);
        if (type is null || !type.Components.Contains("Sockets") || !target.Components.Contains("Sockets") || type.HasOneSocket
            || type.Class is "Belt" or "Amulet" or "Ring" or "Quiver" || type.Class.Contains("Flask") || type.Class.Contains("Jewel"))
        { Tell(player, "Limit Break Orb: use it on a weapon or a piece of armour."); return target; }
        int max = Math.Clamp(c.Max, 1, 6);
        if (target.Sockets.Count >= max) { Tell(player, $"Limit Break Orb: that item already has {target.Sockets.Count} sockets."); return target; }
        // The colour of a random socket already there, or any of the three when it has none.
        var colour = target.Sockets.Count > 0 && random.Next(2) == 0 ? target.Sockets[random.Next(target.Sockets.Count)].Colour
            : (SocketColour)(1 + random.Next(3));
        var sockets = target.Sockets.Append(new ItemSocket(colour)).ToList();
        var links = (target.Links ?? Enumerable.Repeat((byte)1, target.Sockets.Count).ToList()).ToList();
        if (c.LinkNew && links.Count > 0) links[^1]++;
        else links.Add(1);
        Tell(player, $"Limit Break Orb: the item now has {sockets.Count} sockets.");
        return target with { Sockets = sockets, Links = links };
    }

    /// <summary>The new currencies' drops: each 1 in its chance per normal monster, magic 3 times as likely, rare 6, unique 10.</summary>
    void NewCurrencyDrops(DropRoll d)
    {
        double weight = d.Monster.Rarity switch { MonsterRarity.Magic => 3, MonsterRarity.Rare => 6, MonsterRarity.Unique => 10, _ => 1 }
            * Math.Max(0, d.Quantity);
        void Roll(string id, bool enabled, int chance, string name)
        {
            if (!enabled || chance <= 0 || Game.ItemsFor(d.Player).Find(id) is not { Supported: true }) return;
            if (Random.Shared.NextDouble() * chance >= weight) return;
            d.Stacks.Add((id, 1));
            if (_choices.Announce) Tell(d.Player, $"* A {name} dropped.");
        }
        var limit = _choices.LimitBreakOrb ?? new();
        Roll(LimitBreakId, limit.Enabled, limit.DropChance, "Limit Break Orb");
        var krangle = _choices.KrangleOrb ?? new();
        Roll(KrangleId, krangle.Enabled, krangle.DropChance, "Krangle Orb");
    }
}
