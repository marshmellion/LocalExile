using System.IO;
using GameServer.Core.Projectiles;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public sealed class KillExplosionBatchTests
{
    [Fact]
    public async Task Kill_explosions_scale_and_trigger_with_Gravebind_Kulemak_Quecholli_and_Al_Dhih()
    {
        // 1) Secondary kill explosion scaling (physical, chaos, random element, fire) with fixed KillExplosionRadius.
        using (var fixture = new TriggeredSummonTests.Fixture())
        {
            var player = fixture.Player;
            player.Walk.Teleport(100, 100, 1);

            var stats = new Dictionary<uint, int>
            {
                [Stat.EnemiesExplodeForPctLifeAsPhysicalDamage] = 10,
                [Stat.ExplodeEnemiesFor25PctLifeAsChaosOnKillChancePct] = 100,
                [Stat.DamagePlusPct] = 100,
                [Stat.AreaDamagePlusPct] = 50,
                [Stat.ChaosDamagePlusPct] = 50,
                [Stat.BaseSkillAreaOfEffectPlusPct] = 200,
                [Stat.ExplodeEnemiesFor10PctLifeAsRandomElementOnKillChancePct] = 100,
                [Stat.ExplodeEnemiesFor10PctLifeAsFireOnKillChancePct] = 100,
            };
            using (GameEvents.PlayerStatSources.Add("Game", (owner, values) =>
            {
                if (ReferenceEquals(owner, player))
                    foreach (var (k, v) in stats) values[k] = v;
                return values;
            }))
            {
                // Physical: 100 * (1 + 1.0 + 0.5) = 250; Chaos: 250 * (1 + 1.0 + 0.5 + 0.5) = 750;
                // Random Element (100) * 2.5 = 250 + Fire (100) * 2.5 = 250 -> total 1500.
                var corpse = new SpawnedMonster("corpse", new(110, 100, 0), 1, 0, 1000) { Hostile = true, Dead = true, Life = 0 };
                var near = new SpawnedMonster("near", new(134, 100, 0), 1, 0, 10_000) { Hostile = true };
                // Distance 26 from (110, 100): outside fixed KillExplosionRadius (25) even with +200% AoE.
                var outside = new SpawnedMonster("outside", new(136, 100, 0), 1, 0, 10_000) { Hostile = true };
                player.Area.Monsters[0xA001] = corpse;
                player.Area.Monsters[0xA002] = near;
                player.Area.Monsters[0xA003] = outside;

                Assert.True(await Game.ExplodeOnKill(player, 0xA001, corpse, (110, 100)));
                Assert.Equal(10_000u - 1500u, near.Life);
                Assert.Equal(10_000u, outside.Life);
            }

            // 2) Inpulsa: shocked enemy explodes for 10% lightning without shocking neighbors.
            using (GameEvents.PlayerStatSources.Add("Game", (owner, v) =>
            {
                if (ReferenceEquals(owner, player)) v[Stat.ShockedEnemiesExplodeForPctLifeAsLightningDamage] = 10;
                return v;
            }))
            {
                var shockedCorpse = new SpawnedMonster("shocked", new(110, 100, 0), 1, 0, 1000)
                    { Hostile = true, Dead = true, Life = 0, ShockedAtDeath = true };
                var target = new SpawnedMonster("target", new(115, 100, 0), 1, 0, 10_000) { Hostile = true };
                player.Area.Monsters[0xC001] = shockedCorpse;
                player.Area.Monsters[0xC002] = target;
                Assert.True(await Game.ExplodeOnKill(player, 0xC001, shockedCorpse, (110, 100)));
                Assert.Equal(9900u, target.Life);
                Assert.False(target.Ailments.ShockedAtDeath(Game.AreaTime(player)));
            }

            // 3) Hinekora: 500% fire damage on both direct kills and totem-only kills.
            using (GameEvents.PlayerStatSources.Add("Game", (owner, v) =>
            {
                if (ReferenceEquals(owner, player)) v[Stat.ExplodeEnemiesFor500PctLifeAsFireOnKillPctChance] = 100;
                return v;
            }))
            {
                var totemCorpse = new SpawnedMonster("totem-corpse", new(110, 100, 0), 1, 0, 1000)
                    { Hostile = true, Dead = true, Life = 0 };
                var target = new SpawnedMonster("totem-target", new(115, 100, 0), 1, 0, 10_000) { Hostile = true };
                player.Area.Monsters[0xC003] = totemCorpse;
                player.Area.Monsters[0xC004] = target;
                Game.TotemKillLocked(totemCorpse, 0);
                Assert.True(Game.IsTotemKill(totemCorpse));
                Assert.True(await Game.ExplodeOnKill(player, 0xC003, totemCorpse, (110, 100), totemOnly: true));
                Assert.Equal(5000u, target.Life);
            }

            // 4) Fire Mastery (Fire3): ignited AND non-ignite burning enemies (burning ground) explode for 10% fire.
            using (GameEvents.PlayerStatSources.Add("Game", (owner, v) =>
            {
                if (ReferenceEquals(owner, player)) v[Stat.ExplodeBurningEnemiesFor10PctLifeAsFireOnKillChancePct] = 100;
                return v;
            }))
            {
                double now = Game.AreaTime(player);
                var ignitedCorpse = new SpawnedMonster("burning", new(110, 100, 0), 1, 0, 1000)
                    { Hostile = true, Dead = true, Life = 0 };
                ignitedCorpse.Ignites.Add(now, 50, 4);
                var target = new SpawnedMonster("burn-target", new(115, 100, 0), 1, 0, 10_000) { Hostile = true };
                player.Area.Monsters[0xC005] = ignitedCorpse;
                player.Area.Monsters[0xC006] = target;
                Assert.True(await Game.ExplodeOnKill(player, 0xC005, ignitedCorpse, (110, 100)));
                Assert.Equal(9900u, target.Life);

                var groundCorpse = new SpawnedMonster("ground-burning", new(110, 100, 0), 1, 0, 1000)
                    { Hostile = true, Dead = true, Life = 0 };
                player.Area.Monsters[0xC009] = groundCorpse;
                player.Placed.Grounds.Add(new(0x9001, 0, (110, 100), 20, 50, now + 5, now));
                Assert.True(await Game.ExplodeOnKill(player, 0xC009, groundCorpse, (110, 100)));
                Assert.Equal(9800u, target.Life);
                player.Placed.Grounds.Clear();
            }

            // 5) Annihilation Support: triggers ONLY from killing skillStats, NOT global playerStats.
            using (GameEvents.PlayerStatSources.Add("Game", (owner, v) =>
            {
                if (ReferenceEquals(owner, player)) v[Stat.ExplodeEnemiesFor10PctLifeAsRandomElementOnKillingBlowChancePct] = 100;
                return v;
            }))
            {
                var c1 = new SpawnedMonster("ann1", new(110, 100, 0), 1, 0, 1000) { Hostile = true, Dead = true, Life = 0 };
                var t1 = new SpawnedMonster("t1", new(115, 100, 0), 1, 0, 10_000) { Hostile = true };
                player.Area.Monsters[0xC007] = c1;
                player.Area.Monsters[0xC008] = t1;
                Assert.False(await Game.ExplodeOnKill(player, 0xC007, c1, (110, 100)));
                Assert.Equal(10_000u, t1.Life);

                var skillStats = new Dictionary<uint, int> { [Stat.ExplodeEnemiesFor10PctLifeAsRandomElementOnKillingBlowChancePct] = 100 };
                Assert.True(await Game.ExplodeOnKill(player, 0xC007, c1, (110, 100), skillStats: skillStats));
                Assert.Equal(9900u, t1.Life);
            }

            // 6) Gravebind: local-stat-only read, cached equipment check, 60-cell radius, credits owned kills.
            var items = Game.ItemsOf(player);
            var mods = Program.Items.Mods;
            var graveMod = mods.ById("EnemiesKilledCountAsYoursUnique__1")!;
            var glovesType = Program.Items.Find("Hydrascale Gauntlets")!;
            var graveGloves = new Item(glovesType.Hash, glovesType.Components, 0)
            {
                Rarity = ItemRarity.Unique,
                Mods = [new(ModSection.Explicit, graveMod.Hash, [1])],
                Identified = true,
            };
            Assert.False(Game.HasGravebind(player));
            items.Add(PlayerInventories.Gloves, 0, 0, graveGloves);

            Assert.NotEqual(0, ItemModStats.LocalValue(graveGloves, mods, Stat.LocalDisplayEnemiesKilledNearbyCountAsBeingKilledByYou));
            Assert.Equal(0, ItemModStats.Global([graveGloves], mods, Program.Stats).GetValueOrDefault(Stat.LocalDisplayEnemiesKilledNearbyCountAsBeingKilledByYou));
            Assert.True(Game.HasGravebind(player));
            Assert.True(Game.GravebindCreditsKill(player, (150, 100), 1));
            Assert.False(Game.GravebindCreditsKill(player, (170, 100), 1));

            using (GameEvents.PlayerStatSources.Add("Game", (owner, values) =>
            {
                if (ReferenceEquals(owner, player))
                    values[Stat.EnemiesExplodeForPctLifeAsPhysicalDamage] = 10;
                return values;
            }))
            {
                var clock = new TestClock { At = 1 };
                var previous = Game.AreaUpdateContext.Value;
                player.ProjectileRuntimeState = fixture.Session;
                Game.AreaUpdateContext.Value = new(fixture.Lifetime, clock);
                try
                {
                    var variety = new MonsterCatalog().Find("Metadata/Monsters/Zombies/Zombie");
                    var victim = new SpawnedMonster("minion-victim", new(120, 100, 0), 1, 0, 1000)
                        { Hostile = true, Variety = variety, Dead = true, Life = 0 };
                    var bystander = new SpawnedMonster("bystander", new(125, 100, 0), 1, 0, 10_000) { Hostile = true };
                    player.Area.Monsters[0xB001] = victim;
                    player.Area.Monsters[0xB002] = bystander;

                    Game.OwnedKillLocked(victim, 0);
                    Game.CommitAcceptedKillLocked(player, 0xB001, victim, clock.At, playerKill: false);
                    fixture.Session.AdvanceTo(ProjectileRuntime.Time(clock.At + 0.05));
                    await Drain(fixture.Session);

                    Assert.True(victim.CorpseConsumed);
                    Assert.Equal(9900u, bystander.Life);
                }
                finally { Game.AreaUpdateContext.Value = previous; }
            }
        }

        // 7) Cane of Kulemak unveiled effect scales unveiled explicit mods (Domain 28) only + Quecholli SQL check.
        {
            var mods = Program.Items.Mods;
            var veiledEffect = mods.ById("LocalVeiledModEffectUnique__1")!;
            var veiledChaos = mods.ById("JunMasterVeiledChaosExplosionOnKill")!;
            var veiledDomain28 = mods.ById("JunMasterAvoidStunAndElementalStatusAilments1")!;
            var normalDamage = mods.ById("IncreasedPhysicalDamagePercent1")!;
            var staffType = Program.Items.Find("Coiled Staff")!;
            var item = new Item(staffType.Hash, staffType.Components, 0)
            {
                Rarity = ItemRarity.Unique,
                Mods =
                [
                    new(ModSection.Explicit, veiledEffect.Hash, [80]),
                    new(ModSection.Explicit, veiledChaos.Hash, [25]),
                    new(ModSection.Explicit, veiledDomain28.Hash, [20, 20]),
                    new(ModSection.Explicit, normalDamage.Hash, [100]),
                ],
                Identified = true,
            };

            var global = ItemModStats.Global([item], mods, Program.Stats);
            Assert.Equal(45, global.GetValueOrDefault(Stat.ExplodeEnemiesFor25PctLifeAsChaosOnKillChancePct));
            Assert.Equal(36, global.GetValueOrDefault(Stat.BaseAvoidStunPct));
            Assert.Equal(100, global.GetValueOrDefault(Stat.PhysicalDamagePlusPct));

            string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            string pendingSql = Path.Combine(repoRoot, "sql", "updates", "pending_world", "quecholli_explode_mod.sql");
            string? sqlPath = File.Exists(pendingSql)
                ? pendingSql
                : Directory.EnumerateFiles(Path.Combine(repoRoot, "sql", "updates", "world"), "*_quecholli_explode_mod.sql")
                    .SingleOrDefault();
            Assert.NotNull(sqlPath);
            string sql = File.ReadAllText(sqlPath!);
            Assert.Contains("EnemiesExplodeOnDeathUniqueTwoHandMace7", sql);
            Assert.DoesNotContain("\r\n", sql);
        }

        // 8) Madness art + Al Dhih warcry taunt chaos explosion (caster-scoped & cleared by non-warcry taunt).
        using (var f = new MinionUtilityTests.Fixture())
        {
            var player = f.Player;
            var madnessVariety = new MonsterCatalog().Find("Metadata/Monsters/Zombies/Zombie");
            var madCorpse = new SpawnedMonster("mad", new(100, 100, 0), 1, 0, 1000) { Hostile = true, Dead = true, Life = 0, Variety = madnessVariety };
            madCorpse.MadnessExplosion = new(Game.CombatLifetime!, player, new Dictionary<uint, int>(),
                DamagePacket.Of(DamageType.Chaos, 250, 250), Game.MadnessExplosionBaseRadius, (100, 100));
            player.Area.Monsters[0xD001] = madCorpse;
            Game.QueueMadnessExplosionLocked(player, 0xD001, madCorpse);
            Game.MadnessExplosionsLocked(player, 1);
            Assert.Contains(player.Minions.OutputQueue, o => o.CreatesObject && (o.Log?.Contains("Madness", StringComparison.Ordinal) ?? false));
            player.Minions.OutputQueue.Clear();

            double now = Game.AreaTime(player);
            using (GameEvents.PlayerStatSources.Add("Game", (owner, v) =>
            {
                if (ReferenceEquals(owner, player))
                    v[Stat.EnemiesTauntedByWarcryExplodeOnDeathPctChanceDealing8PctLifeAsChaosDamage] = 100;
                return v;
            }))
            {
                var tauntedCorpse = new SpawnedMonster("taunted", new(100, 100, 0), 1, 0, 1000) { Hostile = true, Dead = true, Life = 0 };
                var target = new SpawnedMonster("dhih-target", new(105, 100, 0), 1, 0, 10_000) { Hostile = true };
                player.Area.Monsters[0xD002] = tauntedCorpse;
                player.Area.Monsters[0xD003] = target;

                // Another player's warcry taunt does not count for this player.
                Game.MarkWarcryTaunted(tauntedCorpse, player.Id + 99, now + 5);
                Assert.False(Game.WasWarcryTaunted(tauntedCorpse, player.Id, now));
                Assert.False(await Game.ExplodeOnKill(player, 0xD002, tauntedCorpse, (100, 100)));

                // Non-warcry taunt overwrites and clears a living monster's warcry taunt.
                var living = new SpawnedMonster("living", new(100, 100, 0), 1, 0, 1000) { Hostile = true };
                player.Area.Monsters[0xD004] = living;
                Game.TauntLocked(player, (100, 100), 20, now, 3, fromWarcry: true);
                Assert.True(Game.WasWarcryTaunted(living, player.Id, now));
                Game.TauntLocked(player, (100, 100), 20, now, 3, fromWarcry: false);
                Assert.False(Game.WasWarcryTaunted(living, player.Id, now));
                player.Area.Monsters.Remove(0xD004);

                // Own warcry taunt triggers Al Dhih 8% chaos explosion.
                Game.MarkWarcryTaunted(tauntedCorpse, player.Id, now + 5);
                Assert.True(Game.WasWarcryTaunted(tauntedCorpse, player.Id, now));
                Assert.True(await Game.ExplodeOnKill(player, 0xD002, tauntedCorpse, (100, 100)));
                Assert.Equal(9920u, target.Life);
            }
        }
    }

    [Fact]
    public async Task Sporeguard_fungal_ground_and_Voltaxic_Burst_delay_corpse_explosion()
    {
        // 1) Sporeguard Fungal Ground: 5s duration, explosion on Fungal Ground, visual purge on area exit.
        using (var fixture = new TriggeredSummonTests.Fixture())
        {
            var player = fixture.Player;
            player.Walk.Teleport(100, 100, 1);
            var items = Game.ItemsOf(player);
            var mods = Program.Items.Mods;
            var sporeOnKill = mods.ById("TriggerFungalGroundOnKillUnique__1_")!;
            var sporeStationary = mods.ById("FungalAroundWhenStationaryUnique__1_")!;
            var sporeExplode = mods.ById("EnemiesOnFungalGroundExplodeUnique__1")!;
            var armourType = Program.Items.Find("Saint's Hauberk")!;
            var sporeguard = new Item(armourType.Hash, armourType.Components, 0)
            {
                Rarity = ItemRarity.Unique,
                Mods =
                [
                    new(ModSection.Explicit, sporeOnKill.Hash, [10]),
                    new(ModSection.Explicit, sporeStationary.Hash, [24]),
                    new(ModSection.Explicit, sporeExplode.Hash, [100]),
                ],
                Identified = true,
            };
            items.Add(PlayerInventories.BodyArmour, 0, 0, sporeguard);

            player.ProjectileRuntimeState = fixture.Session;
            Game.SporeguardOnKillLocked(player, (140, 100), 1);
            await Game.TickSporeguardFungalGround(player, 1.5);
            await Drain(fixture.Session);
            var fungalIds = player.Area.Objects.SnapshotCreates().Select(c => c.Id).ToList();
            Assert.NotEmpty(fungalIds);
            Assert.True(Game.OnFungalGround(player, (100, 100), 6.1));
            Assert.True(Game.OnFungalGround(player, (140, 100), 5.5));
            Assert.False(Game.OnFungalGround(player, (140, 100), 6.1));
            Assert.False(Game.OnFungalGround(player, (200, 200), 1.5));

            var corpse = new SpawnedMonster("fungal-corpse", new(140, 100, 0), 1, 0, 1000) { Hostile = true, Dead = true, Life = 0 };
            var near = new SpawnedMonster("fungal-target", new(145, 100, 0), 1, 0, 10_000) { Hostile = true };
            player.Area.Monsters[0xE001] = corpse;
            player.Area.Monsters[0xE002] = near;
            Assert.True(await Game.ExplodeOnKill(player, 0xE001, corpse, (140, 100)));
            Assert.Equal(9900u, near.Life);

            Game.EndAreaSporeguard(player);
            Assert.False(Game.OnFungalGround(player, (100, 100), 1.5));
            Assert.False(Game.OnFungalGround(player, (140, 100), 1.5));
            foreach (uint id in fungalIds) Assert.False(player.Area.Objects.Has(id));
        }

        // 2) Voltaxic Burst: delayed nova, waiting stacks buff, corpse explosion with conversion intact, totem AoE payload.
        var clock = new TestClock { At = 1 };
        using (var fixture = new TriggeredSummonTests.Fixture(clock))
        {
            var player = fixture.Player;
            var previous = Game.AreaUpdateContext.Value;
            player.ProjectileRuntimeState = fixture.Session;
            player.Walk.Teleport(100, 100, clock.At);
            Game.AreaUpdateContext.Value = new(fixture.Lifetime, clock);
            try
            {
                Assert.True(Game.IsVoltaxicBurst("VoltaxicBurst"));
                ushort vbBuff = BuffRows.Find("voidburst_delayed_explosion");
                Assert.NotEqual(0, vbBuff);
                ushort skill = Assert.Single(fixture.Equip(("Voltaxic Burst", 20)));
                var proxy = Game.TotemProxy(player, skill);
                Assert.NotNull(proxy);
                Assert.NotNull(proxy!.Stats);
                Assert.Contains(proxy.Stats!, s => s.Key == Stat.ActiveSkillAreaOfEffectRadius && s.Value > 0);

                // Even with 100% lightning resistance on neighbor, VB's 60% lightning-to-chaos conversion must still deal
                // chaos damage even if character stats already carry a lightning-to-chaos conversion entry.
                using var convSource = GameEvents.PlayerStatSources.Add("Game", (owner, v) =>
                {
                    if (ReferenceEquals(owner, player))
                        v[Stat.SkillLightningDamagePctToConvertToChaos] = 0;
                    return v;
                });

                var victim = new SpawnedMonster("vb-victim", new(102, 100, 0), 1, 0, 100) { Hostile = true };
                var bystander = new SpawnedMonster("vb-bystander", new(160, 100, 0), 1, 0, 10_000) { Hostile = true };
                var corpseNeighbor = new SpawnedMonster("vb-neighbor", new(106, 100, 0), 1, 0, 10_000)
                {
                    Hostile = true,
                    Mods = MonsterModSet.None with { Effects = new(new Dictionary<uint, int> { [Stat.BaseLightningDamageResistancePct] = 100 }) },
                };
                player.Area.Monsters[0xF001] = victim;
                player.Area.Monsters[0xF002] = bystander;
                player.Area.Monsters[0xF003] = corpseNeighbor;

                fixture.Session.AdvanceTo(ProjectileRuntime.Time(clock.At));
                Assert.True(await Game.CastGemSkill(player, skill, (100, 100), (105, 100)));
                // Advance past cast windup so the delayed nova enters waiting queue; it has NOT hit victim yet.
                clock.At += 1.0;
                fixture.Session.AdvanceTo(ProjectileRuntime.Time(clock.At));
                await Drain(fixture.Session);
                Assert.Equal(100u, victim.Life);
                Assert.True(player.Area.Buffs.HasRow(player.Id, vbBuff));

                // Advance past 2.5s base duration so delayed nova detonates, kills victim, and explodes its corpse.
                clock.At += 3.0;
                fixture.Session.AdvanceTo(ProjectileRuntime.Time(clock.At));
                await Drain(fixture.Session);
                await Drain(fixture.Session);
                await Game.PlayerMinionsTick(player, clock.At);
                fixture.Session.AdvanceTo(ProjectileRuntime.Time(clock.At + 0.05));
                await Drain(fixture.Session);

                Assert.Equal(0u, victim.Life);
                Assert.True(corpseNeighbor.Life < corpseNeighbor.MaxLife);
                Assert.Equal(10_000u, bystander.Life);
                Assert.False(player.Area.Buffs.HasRow(player.Id, vbBuff));
            }
            finally
            {
                Game.EndAreaVoltaxicBurst(player);
                Game.AreaUpdateContext.Value = previous;
            }
        }
    }

    static async Task Drain(Game.ProjectileSession session)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Enqueue(() => { done.SetResult(); return Task.CompletedTask; });
        await done.Task;
    }

    sealed class TestClock : IPlayerUpdateSchedule
    {
        public double At = 1;
        public double Now(Player player) => At;
        public Task Wait(int milliseconds, CancellationToken stopping)
        {
            stopping.ThrowIfCancellationRequested();
            At += milliseconds / 1000d;
            return Task.CompletedTask;
        }
    }
}
