// Kills by what the player owns. A kill by the player's own totem, trap or mine, or by their minion, reaches
// GameEvents.Kill with PlayerKill false, and the effects that read PlayerKill are not changed here. Those kill sites
// mark the monster before its kill is committed, so an on-kill rule whose own reference counts such kills as its
// owner's can ask KilledByPlayerOrOwned. kill.Conn is the owner.
public static partial class Game
{
    static readonly RuleTable<SpawnedMonster, object> OwnedKills = new();
    static readonly object Owned = new();

    /// <summary>Marks that the player's own totem, trap, mine or minion dealt the killing hit (<paramref name="life"/>
    /// 0). Under the Monsters lock, before the kill is committed.</summary>
    public static void OwnedKillLocked(SpawnedMonster m, uint life)
    {
        if (life == 0) OwnedKills.AddOrUpdate(m, Owned);
    }

    /// <summary>The player's own kill, or their totem's, trap's, mine's or minion's.</summary>
    public static bool KilledByPlayerOrOwned(KillContext kill) =>
        kill.PlayerKill || OwnedKills.TryGetValue(kill.Monster, out _);

    public const int GravebindRadius = 60;
    static readonly RuleTable<SpawnedMonster, object> GravebindPartyClaims = new();

    /// <summary>Whether the player wears an active item granting Gravebind's nearby-kill attribution.</summary>
    public static bool HasGravebind(Player player)
    {
        var items = ItemsOf(player);
        var mods = ItemsFor(player).Mods;
        foreach (var (_, equipped) in items.EquipmentSnapshot())
            if (!IsInactive(player, equipped.Id)
                && ItemModStats.LocalValue(equipped.Item, mods, Stat.LocalDisplayEnemiesKilledNearbyCountAsBeingKilledByYou) != 0)
                return true;
        return false;
    }

    /// <summary>Gravebind credits owned (minion/totem/trap/mine) kills within 60 cells to a living owner.</summary>
    public static bool GravebindCreditsKill(Player owner, (int X, int Y) deathAt, double now) =>
        owner.PlayerLife > 0 && !owner.Closed && !owner.LeavingArea
        && HasGravebind(owner) && Dist(owner.Walk.Position(now), deathAt) <= GravebindRadius;

    static bool ClaimGravebindPartyKill(SpawnedMonster monster)
    {
        lock (GravebindPartyClaims)
        {
            if (GravebindPartyClaims.TryGetValue(monster, out _)) return false;
            GravebindPartyClaims.AddOrUpdate(monster, Owned);
            return true;
        }
    }
}
