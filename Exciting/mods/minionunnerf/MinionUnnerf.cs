using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

/// <summary>
/// Minion Unnerf: undoes some of the spectre and minion nerfs, and lets anything be a spectre.
/// - Spectre level as before 3.19: a level 20 Raise Spectre makes level 76 spectres (now 70), level 25 makes 83 (now 78).
/// - Minion damage and life as before 3.19: that patch cut minion damage and life (not spectres') by about 20% at gem
///   level 20; this gives it back as more damage and more life on the minion skills.
/// - Minion Damage Support as before 3.15: 25% to 49% more damage at gem levels 1 to 20 (now 39%), and no "25% less
///   maximum life".
/// - Any monster's corpse can be raised as a spectre: unique ones and bosses too, and the ones GGG made un-raisable.
/// - Optionally, your minions' kills count as your kills too (on-kill effects, charges), and still as theirs.
/// Every part has its own setting. Needs the server's GemLevelStats and CorpseRaisable (the private hooks).
/// </summary>
public sealed class MinionUnnerfMod : IMod
{
    sealed record Choices(bool Enabled = true, SpectreChoices? Spectres = null, MinionChoices? Minions = null, SupportChoices? Supports = null,
        KillChoices? Kills = null);
    sealed record KillChoices(bool MinionKillsAreYours = true);
    sealed record SpectreChoices(bool OldLevel = true, int ExtraLevels = 0, int MaxLevel = 100, int MoreDamage = 0, int MoreLife = 0,
        bool AnyCorpse = true, bool Uniques = true, bool Bosses = true, bool CommandIgnoresSpellEcho = true);
    sealed record MinionChoices(bool OldScaling = true, int MoreDamage = 25, int MoreLife = 25);
    sealed record SupportChoices(bool OldMinionDamage = true, bool NoLifePenalty = true);

    Choices _c = new();
    Action<string> _log = _ => { };
    SpectreChoices S => _c.Spectres ?? new();
    MinionChoices M => _c.Minions ?? new();
    SupportChoices G => _c.Supports ?? new();
    KillChoices K => _c.Kills ?? new();

    // The minion skills 3.19 left out: Raise Spectre wholly, and these for damage only (their damage comes from items).
    static readonly HashSet<string> ItemDamage = new(StringComparer.Ordinal)
        { "AnimateGuardian", "AnimateWeapon", "VaalAnimateWeapon", "BlinkArrow", "MirrorArrow" };

    public void Load(IModContext mod)
    {
        string id = mod.Manifest.Id;
        _c = mod.Settings<Choices>();
        _log = mod.Log;
        if (!_c.Enabled) { mod.Log("off"); return; }
        bool hooks = typeof(GameEvents).GetField("GemLevelStats") is not null && typeof(GameEvents).GetField("CorpseRaisable") is not null;
        if (!hooks) { mod.Log("waiting for a server update (update-main): this server has no GemLevelStats / CorpseRaisable hooks yet, so nothing changes"); return; }
        Subscribe(id);
        // The game's client will not aim Raise Spectre at some corpses (unique monsters, bosses): this raises one anyway.
        GameEvents.Command(id, "spectre", "/spectre [name] (raise the nearest corpse, or the nearest one with that name, as a spectre)", RaiseCommand);
        mod.Log($"spectre level {(S.OldLevel ? "as before 3.19" : "as now")}{(S.ExtraLevels != 0 ? $" {S.ExtraLevels:+#;-#} levels" : "")}; "
            + $"minions {(M.OldScaling ? $"+{M.MoreDamage}% more damage, +{M.MoreLife}% more life" : "as now")}; "
            + $"Minion Damage Support {(G.OldMinionDamage ? "as before 3.15" : "as now")}; any corpse as a spectre: {(S.AnyCorpse ? "yes" : "no")}; minion kills count as yours: {(K.MinionKillsAreYours ? "yes" : "no")}");
    }

    // Through reflection, so the mod still compiles (and says what is missing) on a server without the hooks.
    void Subscribe(string id)
    {
        Hook(id, "GemLevelStats", (Action<string, int, Dictionary<uint, int>>)GemStats);
        Hook(id, "CorpseRaisable", (Func<Player, SpawnedMonster, string, bool?>)Raisable);
        // /spectre raises one corpse: its cast does not repeat (Spell Echo), which would raise the next corpse too.
        if (typeof(GameEvents).GetField("SpellRepeatCount") is not null)
            Hook(id, "SpellRepeatCount", (Func<Player, ushort, string, int, int?>)CommandRepeats);
        else if (S.CommandIgnoresSpellEcho) _log("/spectre ignoring Spell Echo: this server has no SpellRepeatCount hook yet (update-main)");
        // Newer than the other two: a server without it still gets the rest.
        if (typeof(GameEvents).GetField("OwnedKillCountsAsYours") is not null)
            Hook(id, "OwnedKillCountsAsYours", (Func<Player, SpawnedMonster, bool>)((_, _) => K.MinionKillsAreYours));
        else if (K.MinionKillsAreYours) _log("minion kills as yours: this server has no OwnedKillCountsAsYours hook yet (update-main)");
    }

    static void Hook(string id, string name, Delegate run)
    {
        object gameEvent = typeof(GameEvents).GetField(name)!.GetValue(null)!;
        gameEvent.GetType().GetMethod("Add")!.Invoke(gameEvent, [id, run]);
    }

    // ---- /spectre ----

    async Task RaiseCommand(Player player, string[] args)
    {
        var who = Program.PlayerOf(player);
        if (!S.AnyCorpse) { await who.Tell("Spectre Unnerf: \"Any monster can be a spectre\" is off."); return; }
        var skills = Game.SkillsFor(player);
        var gem = Game.GemSkillEffects(player).FirstOrDefault(g => skills.EffectId(g.Effect) == "RaiseSpectre");
        if (gem.Name is null) { await who.Tell("You have no Raise Spectre gem socketed."); return; }
        string want = string.Join(' ', args).Trim();
        double now = Game.AreaTime(player);
        var here = player.Walk.Position(now);
        var corpse = player.Area.Monsters
            .Where(e => e.Value is { Hostile: true, Dead: true, CorpseConsumed: false } m && m.Variety is { Supported: true } v
                && (want.Length == 0 || v.Name.Contains(want, StringComparison.OrdinalIgnoreCase)))
            .Select(e => (Id: e.Key, Monster: e.Value, Distance: Game.Dist(e.Value.Walk.Position(now), here)))
            .Where(c => c.Distance <= 80)
            .OrderBy(c => c.Distance).FirstOrDefault();
        if (corpse.Monster is null)
        {
            await who.Tell(want.Length == 0 ? "No corpse near you." : $"No corpse called \"{want}\" near you.");
            return;
        }
        // The cast this command sends does not repeat for a moment (CommandRepeats), whatever supports the gem.
        if (S.CommandIgnoresSpellEcho) lock (_quiet) _quiet[player] = (gem.Id, Environment.TickCount64 + 1500);
        // The client's own cast on that corpse (C→S 0x24 with the Raise Spectre skill), in the area's frame after this command.
        var bytes = new LoginWriter().U16(InstanceTransfer.UseSkillOnObjectId).U32(corpse.Id).U16(gem.Id).U16(0xffff).U16(0x0408).ToArray();
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            await player.Frames.Run(() => Game.OnClientMessages([(InstanceTransfer.UseSkillOnObjectId, bytes)], [], player));
        });
        await who.Tell($"Raising {corpse.Monster.Variety!.Name} as a spectre.");
    }

    readonly Dictionary<Player, (ushort Skill, long Until)> _quiet = new();

    int? CommandRepeats(Player player, ushort skill, string effect, int repeats)
    {
        if (repeats == 0 || !S.CommandIgnoresSpellEcho) return null;
        lock (_quiet)
        {
            if (!_quiet.TryGetValue(player, out var q)) return null;
            if (Environment.TickCount64 > q.Until) { _quiet.Remove(player); return null; }
            return q.Skill == skill ? 0 : null;
        }
    }

    // ---- gem stats ----

    void GemStats(string effect, int level, Dictionary<uint, int> stats)
    {
        if (effect == "RaiseSpectre")
        {
            if (stats.TryGetValue(Stat.RaisedSpectreLevel, out int spectre))
            {
                int target = S.OldLevel ? OldSpectreLevel(level) : spectre;
                stats[Stat.RaisedSpectreLevel] = Math.Clamp(target + S.ExtraLevels, 1, Math.Clamp(S.MaxLevel, 1, 100));
            }
            More(stats, Stat.ActiveSkillMinionDamagePlusPctFinal, S.MoreDamage);
            More(stats, Stat.ActiveSkillMinionLifePlusPctFinal, S.MoreLife);
            return;
        }
        if (effect == "SupportMinionDamage" && G.OldMinionDamage)
        {
            uint more = Stat.Of("support_minion_damage_+%_final");
            if (more != 0 && stats.ContainsKey(more)) stats[more] = 25 + (int)Math.Round((Math.Max(1, level) - 1) * 24 / 19.0);
        }
        if (effect.StartsWith("SupportMinionDamage", StringComparison.Ordinal) || effect.StartsWith("SupportAwakenedMinionDamage", StringComparison.Ordinal))
        {
            uint penalty = Stat.Of("support_minion_damage_minion_life_+%_final");
            if (G.NoLifePenalty && penalty != 0) stats.Remove(penalty);
            return;
        }
        if (!M.OldScaling || effect.StartsWith("RaiseSpectre", StringComparison.Ordinal) || !CreatesMinions(effect)) return;
        if (!ItemDamage.Contains(effect)) More(stats, Stat.ActiveSkillMinionDamagePlusPctFinal, M.MoreDamage);
        More(stats, Stat.ActiveSkillMinionLifePlusPctFinal, M.MoreLife);
    }

    // Before 3.19: level 28 at gem level 1 (unchanged), 76 at 20, 83 at 25 (about 1.4 a level past 20).
    static int OldSpectreLevel(int gem) => gem <= 20
        ? 28 + (int)Math.Round((Math.Max(1, gem) - 1) * 48 / 19.0)
        : Math.Min(100, 76 + (int)Math.Round((gem - 20) * 1.4));

    // Another "more" on a stat that may have one already: they multiply.
    static void More(Dictionary<uint, int> stats, uint stat, int percent)
    {
        if (percent == 0) return;
        double now = 1 + stats.GetValueOrDefault(stat) / 100.0;
        stats[stat] = (int)Math.Round((now * (1 + percent / 100.0) - 1) * 100);
    }

    static bool CreatesMinions(string effect)
    {
        int row = Program.Skills.EffectRow(effect);
        return row >= 0 && !Program.Skills.IsSupportEffect(row) && Program.Skills.HasType(row, "CreatesMinion");
    }

    // ---- any corpse ----

    bool? Raisable(Player player, SpawnedMonster corpse, string family)
    {
        if (!S.AnyCorpse || family != "Spectre" || corpse.Variety is not { Supported: true } variety) return null;
        if (corpse.Rarity == MonsterRarity.Unique)
        {
            if (!S.Uniques) return null;
            if (!S.Bosses && IsBoss(variety.Id)) return null;
        }
        return true;
    }

    // The endgame's pinnacle and map bosses, by their monster paths.
    static bool IsBoss(string id) =>
        id.Contains("/AtlasBosses/", StringComparison.Ordinal) || id.Contains("/AtlasExiles/", StringComparison.Ordinal)
        || id.Contains("/Maven/", StringComparison.Ordinal) || id.Contains("/UberMaven", StringComparison.Ordinal)
        || id.Contains("/AtlasInvaders/", StringComparison.Ordinal) || id.Contains("/Cleansing/", StringComparison.Ordinal)
        || id.Contains("/Searing/", StringComparison.Ordinal) || id.Contains("/Labyrinth/", StringComparison.Ordinal) && id.Contains("Boss", StringComparison.Ordinal)
        || id.Contains("/MapBoss", StringComparison.Ordinal) || id.Contains("/Bosses/", StringComparison.Ordinal);
}
