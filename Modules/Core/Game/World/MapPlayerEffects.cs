public static partial class Game
{
    /// <summary>A map's stats that act on the players in it, as the player stats they are (GameEvents.PlayerStatSources):
    /// movement speed, debuff expiry and character size. ("Players cannot gain Flask Charges" and "no Experience" are hard
    /// blocks where charges and experience are gained.) Outside a map, or with none of them, the sources come back
    /// untouched.</summary>
    public static Dictionary<uint, int> MapPlayerStatSources(Player player, Dictionary<uint, int> sources)
    {
        if (MapModsOf(player) is not { } map) return sources;
        Dictionary<uint, int>? stats = null;
        void Add(uint playerStat, int value)
        {
            if (value == 0) return;
            stats ??= new(sources);
            stats[playerStat] = ActorScale.Saturate((long)stats.GetValueOrDefault(playerStat) + value);
        }
        Add(Stat.BaseMovementVelocityPlusPct, map.Get(Stat.MapPlayerMovementVelocityPlusPct));
        Add(Stat.DebuffTimePassedPlusPct, map.Get(Stat.MapDebuffTimePassedPlusPct));
        Add(Stat.ActorScalePlusPct, map.Get(Stat.MapActorScalePlusPct));
        return stats ?? sources;
    }

    /// <summary>"Players cannot use Travel Skills" (map_player_travel_skills_disabled): the server refuses them.</summary>
    static string? TravelSkillsDisabledByMap(Player player, int effect) =>
        SkillsFor(player).IsTravel(effect) && MapModsOf(player)?.Get(Stat.MapPlayerTravelSkillsDisabled) > 0
            ? "travel skills are disabled in this area" : null;
}
