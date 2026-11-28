using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

public static partial class Game
{
    internal static bool IsVoltaxicBurst(string? effect) =>
        effect is "VoltaxicBurst" or "VoltaxicBurstAltX" or "VoltaxicBurstAltY";

    public const string VoltaxicBurstAo = "Metadata/Effects/Spells/void_burst/explosion.ao";
    const double VoltaxicBurstArtSeconds = 1.5;
    const double VoltaxicBurstDebuffSeconds = 1.0;
    const int VoltaxicBurstBaseRadius = 22;

    sealed class VoltaxicBurstActorState
    {
        public readonly HashSet<ulong> CastIds = [];
        public long LastExpiry;
    }

    sealed class VoltaxicBurstPlayerState
    {
        public readonly Dictionary<uint, VoltaxicBurstActorState> Actors = [];
        public GameArea? Area;
        public long NextCastId;
    }

    internal sealed record VoltaxicBurstMark(ProjectileSession Owner, double Until, int Radius, int LifePercent,
        IReadOnlyDictionary<uint, int> Stats, IReadOnlyList<GemSupports.Applied> Supports);

    internal sealed record VoltaxicBurstDeath(VoltaxicBurstMark Mark, uint Entity, SpawnedMonster Corpse,
        (int X, int Y) At, long Time);

    static readonly Store<Player, VoltaxicBurstPlayerState> VoltaxicBursts = new();
    static readonly RuleTable<SpawnedMonster, VoltaxicBurstMark> VoltaxicBurstMarks = new();

    static double VoltaxicBurstDelaySeconds(Player player, int effect, IReadOnlyDictionary<uint, int> own,
        IReadOnlyDictionary<uint, int> caster)
    {
        int baseDurationMs = own.GetValueOrDefault(Stat.BaseSkillEffectDuration);
        return SkillDurationModifiers.Seconds(baseDurationMs > 0 ? baseDurationMs : 2500,
            caster.GetValueOrDefault, own.GetValueOrDefault, DurationKinds(player, effect));
    }

    static bool VoltaxicBurstTargetsCursor(IReadOnlyDictionary<uint, int> caster) =>
        caster.GetValueOrDefault(Stat.DischargeAndVoltaxicBurstNovaSpellsCastAtTargetLocation) != 0
        || caster.GetValueOrDefault(Stat.NovaSpellsCastAtTargetLocation) != 0;

    static int VoltaxicBurstRadius(Player player, int effect, IReadOnlyDictionary<uint, int> own,
        IReadOnlyDictionary<uint, int> caster)
    {
        int baseRadius = own.GetValueOrDefault(Stat.ActiveSkillBaseAreaOfEffectRadius);
        return SkillRadius(player, baseRadius > 0 ? baseRadius : VoltaxicBurstBaseRadius, own,
            spell: true, effect, caster);
    }

    static double VoltaxicBurstStackMore(int waitingStacks, IReadOnlyDictionary<uint, int> own)
    {
        if (waitingStacks <= 0) return 1;
        int perStackPct = Math.Max(0, own.GetValueOrDefault(Stat.VoltaxicBurstHitAndAilmentDamagePlusPctFinalPerStack));
        return perStackPct <= 0 ? 1 : 1 + (double)waitingStacks * perStackPct / 100d;
    }

    static ulong RegisterVoltaxicBurstCast(Player player, uint actorId, ushort skillId, int effect,
        long nowQuanta, long dueQuanta, bool syncClient)
    {
        var state = VoltaxicBursts.GetOrCreateValue(player);
        if (!ReferenceEquals(state.Area, player.Area))
        {
            state.Area = player.Area;
            state.Actors.Clear();
        }
        if (!state.Actors.TryGetValue(actorId, out var actor))
            state.Actors[actorId] = actor = new();
        ulong castId = (ulong)Interlocked.Increment(ref state.NextCastId);
        actor.CastIds.Add(castId);
        if (dueQuanta > actor.LastExpiry) actor.LastExpiry = dueQuanta;
        SyncVoltaxicBurstBuff(player, actorId, skillId, effect, actor, nowQuanta, syncClient);
        return castId;
    }

    static bool TryConsumeVoltaxicBurstCast(Player player, uint actorId, ushort skillId, int effect, ulong castId,
        long nowQuanta, bool syncClient, out int remainingStacks)
    {
        remainingStacks = 0;
        if (!VoltaxicBursts.TryGetValue(player, out var state) || !ReferenceEquals(state.Area, player.Area))
            return false;
        if (!state.Actors.TryGetValue(actorId, out var actor) || !actor.CastIds.Remove(castId))
            return false;
        remainingStacks = actor.CastIds.Count;
        if (remainingStacks == 0) state.Actors.Remove(actorId);
        SyncVoltaxicBurstBuff(player, actorId, skillId, effect, actor, nowQuanta, syncClient);
        return true;
    }

    static void SyncVoltaxicBurstBuff(Player player, uint actorId, ushort skillId, int effect,
        VoltaxicBurstActorState actor, long nowQuanta, bool syncClient)
    {
        if (!syncClient || player.Closed || player.LeavingArea) return;
        int count = actor.CastIds.Count;
        player.Area.Stats.Set(actorId, Stat.VoltaxicBurstStackCount, count);
        ushort row = BuffRows.Find("voidburst_delayed_explosion");
        if (row == 0) return;
        if (count == 0)
        {
            player.Area.Buffs.Clear(actorId, row, actorId);
            return;
        }
        ushort sourceSkill = actorId == player.Id ? skillId : (ushort)0x4000;
        (ushort Id, ushort Action)? active = SkillsFor(player).ActionHash(effect) is ushort action
            ? (sourceSkill, action) : null;
        double remaining = Math.Max(0, (actor.LastExpiry - nowQuanta) / (double)ProjectileRuntime.QuantaPerSecond);
        player.Area.Buffs.Set(actorId, BuffRows.Spec(row, actorId, null, remaining) with
        {
            SourceSkill = sourceSkill,
            ActiveSkill = active,
            Stacks = (ushort)Math.Min(count, ushort.MaxValue),
        });
    }

    /// <summary>Clears waiting Voltaxic Burst casts and buff state when leaving an area.</summary>
    internal static void EndAreaVoltaxicBurst(Player player)
    {
        if (!VoltaxicBursts.TryGetValue(player, out var state)) return;
        ushort row = BuffRows.Find("voidburst_delayed_explosion");
        if (ReferenceEquals(state.Area, player.Area))
        {
            foreach (uint actorId in state.Actors.Keys)
            {
                player.Area.Stats.Set(actorId, Stat.VoltaxicBurstStackCount, 0);
                if (row != 0) player.Area.Buffs.Clear(actorId, row, actorId);
            }
        }
        state.Actors.Clear();
        state.Area = null;
        VoltaxicBursts.Remove(player);
    }

    static void MarkVoltaxicBurstHit(Player player, uint monsterId, int effect, IReadOnlyDictionary<uint, int> own,
        IReadOnlyList<GemSupports.Applied> supports, double now, int radius)
    {
        int percent = own.GetValueOrDefault(Stat.CorpseExplosionMonsterLifePctLightning);
        if (percent <= 0 || !player.Area.Monsters.TryGetValue(monsterId, out var monster)) return;
        var session = ProjectilesFor(player);
        if (!session.Active) return;
        VoltaxicBurstMarks.AddOrUpdate(monster, new(session, now + VoltaxicBurstDebuffSeconds, radius, percent, own, supports));
    }

    /// <summary>Queues Voltaxic Burst's corpse explosion when an enemy killed by or shortly after its hit dies.</summary>
    internal static void QueueVoltaxicBurstDeath(uint id, SpawnedMonster monster, double now, ProjectileSession? current = null)
    {
        if (!VoltaxicBurstMarks.TryGetValue(monster, out var mark) || !VoltaxicBurstMarks.Remove(monster)) return;
        var owner = mark.Owner;
        if (!owner.Active || now > mark.Until) return;
        var death = new VoltaxicBurstDeath(mark, id, monster, monster.Walk.Position(now), ProjectileRuntime.Time(now));
        if (ReferenceEquals(current, owner))
            owner.ScheduleVoltaxicBurstDeath(death);
        else
            owner.EnqueueLater(() =>
            {
                if (!owner.Active) return Task.CompletedTask;
                using (ProjectileMutation(owner.Player, advance: false))
                    owner.ReleaseVoltaxicBurstCorpseNow(death);
                return Task.CompletedTask;
            });
    }

    public sealed partial class ProjectileSession
    {
        internal void ScheduleVoltaxicBurst((ushort Id, int Effect, string Name, int Level, int Quality, uint Gem) gem,
            (int X, int Y) aim, IReadOnlyDictionary<uint, int> own, IReadOnlyDictionary<uint, int> caster,
            SkillCriticalRoll? critical, SacrificeUse sacrifice, Func<bool> current, ushort sequence, int repeat,
            double damageMore, SpawnedMonster? brandedTarget, double beidatMore)
        {
            if (!Active || !current()) return;
            double delay = VoltaxicBurstDelaySeconds(Player, gem.Effect, own, caster);
            long due = Runtime.Now + ProjectileRuntime.Time(delay);
            ulong castId = RegisterVoltaxicBurstCast(Player, Player.Id, gem.Id, gem.Effect, Runtime.Now, due, syncClient: true);
            Runtime.Schedule(due, () =>
            {
                if (!TryConsumeVoltaxicBurstCast(Player, Player.Id, gem.Id, gem.Effect, castId, Runtime.Now,
                        syncClient: true, out int remainingStacks))
                    return;
                if (!Active || !current()) return;
                var liveCaster = CharacterPlayerStats(Player);
                var liveSource = SupportedSkill(Player, gem.Id);
                double now = ProjectileWorldTime(Player);
                var centre = VoltaxicBurstTargetsCursor(liveCaster) ? aim : Player.Walk.Position(now);
                int radius = VoltaxicBurstRadius(Player, gem.Effect, liveSource.Stats, liveCaster);
                double stackMore = VoltaxicBurstStackMore(remainingStacks, liveSource.Stats);
                Effect(centre.X, centre.Y, VoltaxicBurstAo, ProjectileRuntime.Time(VoltaxicBurstArtSeconds), radius);
                Enqueue(() => AreaSkillHitForUse(null, Player, gem.Id, gem, centre, centre, immediate: true,
                    sequence: sequence, repeat: repeat, damageMore: damageMore, radiusOverride: radius,
                    criticalRoll: critical, sacrifice: sacrifice, triggeredCurrent: current, brandedTarget: brandedTarget,
                    hitAndAilmentMore: beidatMore * stackMore,
                    onHit: id => MarkVoltaxicBurstHit(Player, id, gem.Effect, liveSource.Stats, liveSource.Supports,
                        ProjectileWorldTime(Player), radius)));
            });
        }

        internal void ReleaseTotemVoltaxicBurst(TotemState.Totem totem,
            (ushort Id, int Effect, string Name, int Level, int Quality, uint Gem) gem, (int X, int Y) aim,
            ushort sequence, Func<bool> current)
        {
            if (!Active || !current()) return;
            var initialSpell = SupportedSkill(Player, totem.SkillId);
            var initialCaster = TotemCasterStats(CharacterPlayerStats(Player));
            double delay = VoltaxicBurstDelaySeconds(Player, gem.Effect, initialSpell.Stats, initialCaster);
            long due = Runtime.Now + ProjectileRuntime.Time(delay);
            ulong castId = RegisterVoltaxicBurstCast(Player, totem.Id, gem.Id, gem.Effect, Runtime.Now, due, syncClient: true);
            Runtime.Schedule(due, () =>
            {
                if (!TryConsumeVoltaxicBurstCast(Player, totem.Id, gem.Id, gem.Effect, castId, Runtime.Now,
                        syncClient: true, out int remainingStacks))
                    return;
                if (!Active || !current()) return;
                var spell = SupportedSkill(Player, totem.SkillId);
                var caster = TotemCasterStats(CharacterPlayerStats(Player));
                var centre = VoltaxicBurstTargetsCursor(caster) ? aim : totem.At;
                int radius = VoltaxicBurstRadius(Player, gem.Effect, spell.Stats, caster);
                double totemMore = Math.Max(0, 1 + spell.Stats.GetValueOrDefault(Stat.SupportTotemDamagePlusPctFinal) / 100d);
                double stackMore = VoltaxicBurstStackMore(remainingStacks, spell.Stats);
                double totalMore = totemMore * stackMore;
                Effect(centre.X, centre.Y, VoltaxicBurstAo, ProjectileRuntime.Time(VoltaxicBurstArtSeconds), radius);
                double now = ProjectileWorldTime(Player);
                TotemNovaLocked(Player, this, totem, centre, radius, sequence, damageMore: totalMore,
                    ailmentDamageMore: totalMore,
                    onHit: id => MarkVoltaxicBurstHit(Player, id, gem.Effect, spell.Stats, spell.Supports, now, radius));
            });
        }

        internal void SchedulePlacedVoltaxicBurst(PlacedAreaHit placement, (int X, int Y) aim, Func<bool> current)
        {
            if (!Active || !current()) return;
            var placed = placement.Device;
            var initialCaster = placed.Offense?.Caster ?? CharacterPlayerStats(Player);
            double delay = VoltaxicBurstDelaySeconds(Player, placed.Effect, placed.Own, initialCaster);
            long due = Runtime.Now + ProjectileRuntime.Time(delay);
            ulong castId = RegisterVoltaxicBurstCast(Player, placed.Id, placed.Skill, placed.Effect, Runtime.Now, due, syncClient: false);
            Runtime.Schedule(due, () =>
            {
                if (!TryConsumeVoltaxicBurstCast(Player, placed.Id, placed.Skill, placed.Effect, castId, Runtime.Now,
                        syncClient: false, out int remainingStacks))
                    return;
                if (!Active || !current()) return;
                var caster = placed.Offense?.Caster ?? CharacterPlayerStats(Player);
                var centre = VoltaxicBurstTargetsCursor(caster) ? aim : placed.At;
                int radius = VoltaxicBurstRadius(Player, placed.Effect, placed.Own, caster);
                double totalMore = placement.DamageMore * VoltaxicBurstStackMore(remainingStacks, placed.Own);
                Effect(centre.X, centre.Y, VoltaxicBurstAo, ProjectileRuntime.Time(VoltaxicBurstArtSeconds), radius);
                double now = ProjectileWorldTime(Player);
                var supports = SupportedSkill(Player, placed.Skill).Supports;
                PlacedAreaDamageLocked(Player, placed, centre, radius, now, totalMore, currentSkill: true,
                    spellDamage: true, spatialSpell: true,
                    onHit: id => MarkVoltaxicBurstHit(Player, id, placed.Effect, placed.Own, supports, now, radius),
                    ailmentDamageMore: totalMore);
            });
        }

        internal void ScheduleVoltaxicBurstDeath(VoltaxicBurstDeath death)
        {
            bool Current() => Player.PlayerLife > 0 && !Player.Closed && !Player.LeavingArea;
            ScheduleContact(death.Time, Current, () => ReleaseVoltaxicBurstCorpseNow(death));
        }

        internal void ReleaseVoltaxicBurstCorpseNow(VoltaxicBurstDeath death)
        {
            if (!Active || Player.PlayerLife == 0 || Player.Closed || Player.LeavingArea) return;
            var corpse = death.Corpse;
            var current = Player.Area.Monsters.GetValueOrDefault(death.Entity);
            if (current != corpse && (current is not null || !corpse.CorpseConsumed)) return;
            if (corpse.CorpseConsumed) return;
            var mark = death.Mark;
            double now = ProjectileWorldTime(Player);
            if (Player.Connection.Connected && TakeExplosionArt(Player, now))
                Effect(death.At.X, death.At.Y, VoltaxicBurstAo, ProjectileRuntime.Time(VoltaxicBurstArtSeconds), mark.Radius);
            var stats = CharacterPlayerStats(Player);
            int StatValue(uint key) => ActorScale.Add(key, stats.GetValueOrDefault(key), mark.Stats.GetValueOrDefault(key));
            double raw = (corpse.MaxLifeWithoutParty ?? corpse.MaxLife) * (double)mark.LifePercent / 100;
            var conversionStats = new Dictionary<uint, int>(stats);
            foreach (var (k, v) in mark.Stats) conversionStats.TryAdd(k, v);
            var conversion = DamageStats.Conversion(conversionStats, Stats, attack: null);
            var supports = mark.Supports.SelectMany(s => s.Stats).Select(s => (s.Key, s.Value));
            DamagePacket Resolve(DamagePacket basis) => PlayerHitDamage.Resolve(basis, StatValue, conversion,
                spell: false, area: true, secondary: true, supportModifiers: supports);
            ReleaseSkillCorpseExplosion(this, death.Entity, corpse, death.At, now, mark.Radius,
                DamagePacket.Of(DamageType.Lightning, raw, raw), Resolve,
                Penetration.Of(stats, Stats, attack: false, skill: mark.Stats),
                StatValue(Stat.CannotPenetrateOrIgnoreElementalResistances), "Voltaxic Burst");
        }
    }
}
