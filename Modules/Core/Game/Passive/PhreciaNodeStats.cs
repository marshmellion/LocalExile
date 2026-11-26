using GameServer.Core.World;

/// <summary>The stats only the Phrecia ascendancies' nodes give, at the game's hand-overs. The game's rules do not name
/// them: each stays in the node's stats (the client is sent it) and is given its effect here. They are the game's in
/// every league, not only Phrecia's: a jewel that allocates such a node (JewelAllocatedPassives) brings it anywhere.
/// The phreciaascendancies mod (LocalExile-Mods) gives the same four where it runs: there these stand aside, so each
/// counts once.</summary>
public static class PhreciaNodeStats
{
    /// <summary>The mod with the Phrecia classes, which hands over the same four.</summary>
    public const string PhreciaMod = "phreciaascendancies";

    // AttackHit runs every subscriber for every player: the mod's applies wherever it is loaded.
    static bool ModGivesAttackHit() => GameEventOwners.Count(PhreciaMod) > 0;

    // The other three run for the players of the leagues that list the mod.
    static bool ModRunsFor<T>(GameEvent<T> e, Player player) where T : Delegate =>
        e.Subscribers(player).Any(s => string.Equals(s.Owner, PhreciaMod, StringComparison.OrdinalIgnoreCase));

    public static DamagePacket MaximumAttackDamageUnlessMod(DamagePacket hit, Func<uint, int> get) =>
        get(Stat.MaximumAttackDamagePlusPctFinalFromAscendancy) == 0 || ModGivesAttackHit() ? hit : MaximumAttackDamage(hit, get);

    public static void DurationPerTenStrengthUnlessMod(Player player, StatMap stats)
    {
        if (stats.GetValueOrDefault(Stat.SkillEffectDurationPlusPctPer10Strength) != 0 && !ModRunsFor(GameEvents.DerivedStats, player))
            DurationPerTenStrength(player, stats);
    }

    public static void ManaPerDexterityUnlessMod(Player player, Dictionary<uint, int> sources,
        (int Strength, int Dexterity, int Intelligence) attributes)
    {
        if (sources.GetValueOrDefault(Stat.XManaPerDexterity) != 0 && !ModRunsFor(GameEvents.PoolSources, player))
            ManaPerDexterity(player, sources, attributes);
    }

    public static int CastSpeedWithFishingRodUnlessMod(Player player, Func<uint, int> get) =>
        get(Stat.CastSpeedPlusPctFinalWhileHoldingFishingRod) == 0 || ModRunsFor(GameEvents.CastSpeedMore, player) ? 0
            : CastSpeedWithFishingRod(player, get);

    /// <summary>The Polytheist's Devout of Ryslatha, "30% more Maximum Attack Damage"
    /// (<c>maximum_attack_damage_+%_final_from_ascendancy</c>): the maximum of every final component of an attack's hit.
    /// PoB CalcOffence applies MaxDamage MORE to each type's own maximum and carries it through conversion, which is the
    /// same. The client folds its sources into <c>combined_maximum_attack_damage_+%_final</c> as a product
    /// (FUN_1425d9160).</summary>
    public static DamagePacket MaximumAttackDamage(DamagePacket hit, Func<uint, int> get)
    {
        int percent = get(Stat.MaximumAttackDamagePlusPctFinalFromAscendancy);
        if (percent == 0) return hit;
        double factor = Math.Max(0, 1 + percent / 100.0);
        return hit.Select((_, range) => new(range.Min, range.Max * factor));
    }

    /// <summary>The Antiquarian's Whaletooth Bangle (<c>skill_effect_duration_+%_per_10_strength</c>): increased skill
    /// effect duration per complete ten of final Strength.</summary>
    public static void DurationPerTenStrength(Player player, StatMap stats)
    {
        int perTen = stats.GetValueOrDefault(Stat.SkillEffectDurationPlusPctPer10Strength);
        if (perTen == 0) return;
        stats[Stat.SkillEffectDurationPlusPct] = (int)Math.Clamp(stats.GetValueOrDefault(Stat.SkillEffectDurationPlusPct)
            + (long)(Math.Max(0, stats.GetValueOrDefault(Stat.Strength)) / 10) * perTen, int.MinValue, int.MaxValue);
    }

    /// <summary>The Whisperer's "+# to Maximum Mana per Dexterity" (<c>X_mana_per_dexterity</c>): flat maximum Mana, which
    /// the pools take as <c>base_maximum_mana</c>.</summary>
    public static void ManaPerDexterity(Player player, Dictionary<uint, int> sources,
        (int Strength, int Dexterity, int Intelligence) attributes)
    {
        int perDexterity = sources.GetValueOrDefault(Stat.XManaPerDexterity);
        if (perDexterity == 0) return;
        sources[Stat.BaseMaximumMana] = (int)Math.Clamp(sources.GetValueOrDefault(Stat.BaseMaximumMana)
            + (long)attributes.Dexterity * perDexterity, int.MinValue, int.MaxValue);
    }

    /// <summary>The Surfcaster's Screaming Reels (<c>cast_speed_+%_final_while_holding_fishing_rod</c>): more cast speed
    /// while the active main hand is a Fishing Rod, which the client's calculator tests as main_hand_weapon_type 15.</summary>
    public static int CastSpeedWithFishingRod(Player player, Func<uint, int> get) =>
        get(Stat.CastSpeedPlusPctFinalWhileHoldingFishingRod) is int more and not 0
            && global::Game.ActiveMainHand(player)?.Type?.Class == "FishingRod" ? more : 0;
}
