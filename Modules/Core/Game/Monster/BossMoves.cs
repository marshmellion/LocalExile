using System.Collections.Concurrent;
using System.Net.Sockets;
using GameServer.Core.Diagnostics;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using static Game;
using static Program;

/// <summary>
/// The skills of a boss's kit (BossKits) that are not a hit aimed at the player: a teleport, a movement skill (a leap
/// slam, a lightning warp, a charge, a flicker) and a self-buff (a shield, a cry, an aura). The ordinary AI used to leave
/// them all out, so a boss whose kit is one ranged spell stood where it was for the whole fight (Merveil, the Siren
/// cast Ice Spear from her spawn point and never teleported).
/// <para>Each one plays the client's own clip as an action, then the server moves the boss to where it lands, in the
/// order the King in the Mists' moves were tuned to against the real client (KingInMists.MoveWithSkill): end the action
/// before the move, 0x1b7 to the cell, then a short 0x1ba so the model settles there. A movement skill that hits lands its
/// hit on a player standing where it lands.</para>
/// </summary>
public static class BossMoves
{
    public enum Kind { Hit, Teleport, Leap, Buff }

    // What a skill's name says it is, for the actions that are no hit aimed at the player, matched on whole words of the
    // id ("Cry" is not "Crystal"). Kept to names the client plays on the actor itself: a skill that drives another object
    // (a turret, a marker the server never creates) is left out in BossKits.Effects, as before.
    static readonly HashSet<string> BuffWords = new(StringComparer.Ordinal)
    {
        "Shield", "Cry", "Hatred", "Wrath", "Haste", "Grace", "Determination", "Discipline", "Vanish", "Enrage", "Enraged",
        "Buff", "Taunt", "Aura", "Fortify", "Shell", "Bloodlust", "PhaseRun", "ImmortalCall", "RestoreLife",
    };

    // Words that make a skill a hit whatever buff word it also carries (a shield beam, an enraged spine storm).
    static readonly HashSet<string> HitWords = new(StringComparer.Ordinal)
    {
        "Beam", "Storm", "Spinestorm", "Screech", "Nova", "Bolt", "Ball", "Slam", "Strike", "Cleave", "Spike", "Blast", "Explosion",
    };

    // Moves that are a scripted step of an encounter, not something to do in a fight: a teleport of the player, a trip
    // to the arena's centre or tower, a start or portal-in, a hide. Left out of the kit.
    static readonly string[] Scripted =
    [
        "TeleportPlayer", "ToCentre", "NoTeleport", "TeleportTower", "StartFight", "PortalIn", "Hide", "Detonation",
        "MirrorArrow", "Burrow", "TeleportMarker", "DeathTeleport",
    ];

    static readonly ConcurrentDictionary<int, Kind> Kinds = new();

    /// <summary>Whether the skill is a scripted step the kit leaves out (BossKits.Effects).</summary>
    public static bool IsScripted(string effectId) => Scripted.Any(w => effectId.Contains(w, StringComparison.Ordinal))
        // A slam or attack that also hides is still a hit (QuakeSlamHideDropBearBoss, ChimeraHideAttack).
        && !Words(effectId).Any(w => HitWords.Contains(w) || w == "Attack");

    static IEnumerable<string> Words(string id)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(id, "[A-Z][a-z]+|[A-Z]+(?![a-z])").Select(m => m.Value).ToList();
        for (int i = 0; i < words.Count; i++)
        {
            yield return words[i];
            if (i + 1 < words.Count) yield return words[i] + words[i + 1];
        }
    }

    /// <summary>What a granted effect is to the kit (cached: the AI asks every step).</summary>
    public static Kind KindOf(SkillCatalog catalog, int effect) => Kinds.GetOrAdd(effect, e =>
    {
        string id = catalog.EffectId(e) ?? "";
        bool projectile = catalog.IsProjectile(e) || id.Contains("Projectile", StringComparison.Ordinal) || id.Contains("Shot", StringComparison.Ordinal);
        if ((id.Contains("Teleport", StringComparison.Ordinal) || id.EndsWith("Blink", StringComparison.Ordinal)) && !projectile) return Kind.Teleport;
        if (catalog.IsMovement(e)) return Kind.Leap;
        // A buff never hits: an attack or a projectile with "Shield" or "Enrage" in its name is a hit (a thrown shield,
        // an enraged cleave).
        if (!catalog.IsAttack(e) && !projectile && Words(id).Any(BuffWords.Contains) && !Words(id).Any(HitWords.Contains)) return Kind.Buff;
        return Kind.Hit;
    });

    /// <summary>A teleport that lands on the player and hits (a teleport slam or strike): it lands beside them, as a
    /// leap does, and its hit lands there.</summary>
    public static bool Strikes(string effectId) =>
        effectId.Contains("Slam", StringComparison.Ordinal) || effectId.Contains("Strike", StringComparison.Ordinal)
        || effectId.Contains("Attack", StringComparison.Ordinal) || effectId.Contains("Impact", StringComparison.Ordinal);

    /// <summary>The cooldown a kit gives a move the tables give none: a boss that teleports every free turn only
    /// blinks around the player and never casts.</summary>
    public static double DefaultCooldown(Kind kind) => kind switch
    {
        Kind.Teleport => 8,
        Kind.Leap => 6,
        Kind.Buff => 18,
        _ => 0,
    };

    /// <summary>How far a move reaches: a teleport and a buff work at any distance in the fight, a leap closes a gap of up
    /// to its variety's aggro range (but never across the arena).</summary>
    public static double Reach(Kind kind, MonsterCatalog.Variety variety) => kind switch
    {
        Kind.Leap => Math.Min(70, Math.Max(variety.AttackRange, variety.AggroRange)),
        _ => Math.Max(variety.AttackRange, variety.AggroRange),
    };

    /// <summary>Whether this move makes sense now: a leap closes a gap (it never jumps onto a player already in melee
    /// reach), a teleport is taken when the player is close (to get away) or far (to close in), or when the boss has not
    /// moved for a while (the kit's cooldown spaces those out).</summary>
    public static bool Fits(Kind kind, SpawnedMonster monster, double distance) => kind switch
    {
        Kind.Leap => monster.Variety is not ({ MovementSpeed: 0 } or { Immobile: true })
            && distance > Math.Max(8, monster.Variety?.AttackRange ?? 8) + 4,
        _ => true,
    };

    /// <summary>The kit entry behind an attack the AI chose, when it is a move rather than a hit.</summary>
    public static (MonsterKitSkill Entry, Kind Kind)? MoveOf(ClientState conn, SpawnedMonster monster, MonsterAttack attack)
    {
        if (monster.Kit.Count == 0) return null;
        var catalog = SkillsFor(conn);
        foreach (var entry in monster.Kit)
            if (entry.Attack.SkillId == attack.SkillId && KindOf(catalog, entry.Effect) is var kind and not Kind.Hit)
                return (entry, kind);
        return null;
    }

    /// <summary>Where the move takes the boss. A teleport lands 8-14 cells from the player at a random side (a "flee" one
    /// 22-28 cells away from them), a leap or warp two cells short of the player on the boss's side; a buff stays.
    /// Always a cell the boss can stand on.</summary>
    public static (int X, int Y) Landing(ClientState conn, Kind kind, string effectId, (int X, int Y) from, (int X, int Y) player)
    {
        if (kind == Kind.Buff) return from;
        (int X, int Y) want;
        if (kind == Kind.Teleport && !Strikes(effectId))
        {
            bool away = effectId.Contains("Flee", StringComparison.Ordinal) || effectId.Contains("Away", StringComparison.Ordinal);
            double angle = Random.Shared.NextDouble() * Math.PI * 2;
            double length = away ? 22 + Random.Shared.NextDouble() * 6 : 8 + Random.Shared.NextDouble() * 6;
            if (away)
            {
                // Away from the player, roughly on the boss's own side.
                double dx = from.X - player.X, dy = from.Y - player.Y, l = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                angle = Math.Atan2(dy / l, dx / l) + (Random.Shared.NextDouble() - .5) * Math.PI / 2;
            }
            want = ((int)Math.Round(player.X + Math.Cos(angle) * length), (int)Math.Round(player.Y + Math.Sin(angle) * length));
        }
        else
        {
            double dx = from.X - player.X, dy = from.Y - player.Y, l = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            want = ((int)Math.Round(player.X + dx / l * 2), (int)Math.Round(player.Y + dy / l * 2));
        }
        if (!conn.Layout.WalkableFor(want.X, want.Y, 0) || conn.Barrier.Contains(want)) want = conn.Layout.Near(want);
        return want;
    }

    /// <summary>Plays the move: the clip as an action toward where it lands, then the boss is put there on both sides,
    /// and a leap's hit lands on a player standing within its radius. Runs off the AI's lock; the AI has already made the
    /// boss busy for the move's time.</summary>
    public static async Task Run(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        uint id, SpawnedMonster boss, MonsterKitSkill entry, Kind kind, int action)
    {
        try
        {
            var catalog = SkillsFor(conn);
            string effectId = catalog.EffectId(entry.Effect) ?? entry.Effect.ToString();
            double startedAt = conn.Clock.Elapsed.TotalSeconds;
            (int X, int Y) from, player, to;
            lock (conn.Monsters)
            {
                if (boss.Dead || boss.Life == 0 || boss.ActionGeneration != action) return;
                from = boss.Walk.Position(startedAt);
                player = conn.Walk.Position(startedAt);
                to = Landing(conn, kind, effectId, from, player);
                boss.Walk.Order(from.X, from.Y, startedAt);
            }
            // The previous clip must be over, or the client ignores the new action and the move after it (King, live
            // 2026-10-31).
            await SendWorldRecord(remote, stream, ciphers, conn, id, InstanceProtocol.ActionEnded(id),
                $"0x01ac {boss.Label} ends its action before {effectId}");
            await Task.Delay(120);
            ushort sequence = KingInMists.SequenceForAction(conn, boss, swing: false) ?? conn.NextActionSequence();
            // A leap and a warp aim at the cell they land on, a teleport too (its clip turns the model that way), a buff
            // at the boss's own cell.
            if (!await SendMonsterAction(remote, stream, ciphers, conn, id, boss, action,
                () => InstanceProtocol.ActionStarted(id, from.X, from.Y, to.X, to.Y, entry.Attack.SkillId, sequence, conn.MonsterOrderFlags),
                $"0x01ab {boss.Label} {effectId} ({kind}) from ({from.X}, {from.Y}) to ({to.X}, {to.Y})")) return;
            double seconds = Math.Clamp(entry.Attack.Seconds, .4, 3.5);
            await Task.Delay(TimeSpan.FromSeconds(kind == Kind.Teleport ? seconds * .75 : seconds));
            if (!Current(conn, boss, action)) return;
            bool flinching;
            lock (conn.Monsters) flinching = conn.Clock.Elapsed.TotalSeconds < boss.FlinchUntil;
            if (!flinching)
                await SendWorldRecord(remote, stream, ciphers, conn, id, InstanceProtocol.ActionEnded(id), $"0x01ac {boss.Label} {effectId} ends");
            if (kind == Kind.Buff) return;
            await Task.Delay(100);
            if (!Current(conn, boss, action)) return;
            double movedAt = conn.Clock.Elapsed.TotalSeconds;
            lock (conn.Monsters) boss.Walk.Teleport(to.X, to.Y, movedAt);
            // A teleport is the client's blink (0x1b7 plays its effect); a leap's clip already moved the model, and a fast
            // glide puts it on the server's cell without a second effect.
            if (kind == Kind.Teleport)
            {
                await SendWorldRecord(remote, stream, ciphers, conn, id, InstanceProtocol.Teleport(id, to.X, to.Y), $"0x01b7 {boss.Label} {effectId} to ({to.X}, {to.Y})");
                await Task.Delay(120);
            }
            await SendWorldRecord(remote, stream, ciphers, conn, id, InstanceProtocol.MoveTo(id, to.X, to.Y, 0.1f, InstanceProtocol.FollowTerrain),
                $"0x01ba {boss.Label} {effectId} settles at ({to.X}, {to.Y})");
            if ((kind == Kind.Leap || Strikes(effectId)) && !entry.Attack.Damage.IsZero) await Land(remote, stream, ciphers, conn, id, boss, entry.Attack, to);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[gateway] {remote} monster 0x{id:x} kit move {entry.Effect} failed: {ServerLog.Describe(ex)}");
        }
    }

    static bool Current(ClientState conn, SpawnedMonster boss, int action)
    {
        if (conn.Closed || conn.LeavingArea) return false;
        lock (conn.Monsters) return !boss.Dead && boss.Life > 0 && boss.ActionGeneration == action;
    }

    /// <summary>A leap's or a warp's hit where it lands: the player within a few cells of the landing cell takes it.</summary>
    static async Task Land(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint id,
        SpawnedMonster boss, MonsterAttack attack, (int X, int Y) at)
    {
        double radius = 6 + (boss.Variety?.ObjectSize ?? 2);
        MonsterHitOutcome? outcome = null;
        await Send(remote, stream, ciphers, [], null, atGate: () =>
        {
            lock (conn.Monsters)
            {
                if (!CurrentGameWork || conn.Closed || conn.LeavingArea || conn.Paused || conn.PlayerLife == 0 || InGracePeriod(conn) || boss.Dead) return null;
                double now = Math.Max(ProjectileWorldTime(conn), conn.Clock.Elapsed.TotalSeconds);
                var player = conn.Walk.Position(now);
                double d = Math.Sqrt(Math.Pow(player.X - at.X, 2) + Math.Pow(player.Y - at.Y, 2));
                if (d > radius) return null;
                outcome = CommitMonsterHitLocked(remote, conn, id, boss, attack, now, player, at, false, conn.NextActionSequence());
                return outcome?.HitPacket;
            }
        });
        if (outcome is not null) await PresentMonsterHit(remote, stream, ciphers, conn, id, outcome);
    }
}
