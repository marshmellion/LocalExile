using GameServer.Core.Accounts;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

namespace GameServer.Gateway.Tests;

public sealed class NativeSpellStatsTests
{
    [Fact]
    public void Manabond_requires_and_spends_a_share_of_maximum_mana()
    {
        using var fixture = new TriggeredSummonTests.Fixture();
        var player = fixture.Player;
        ushort skill = Assert.Single(fixture.Equip(("Manabond", 1)));
        Assert.Equal(5, Game.SupportedSkill(player, skill).Stats.GetValueOrDefault(Stat.BaseManaCostPct));
        uint cost = player.Pools.Mana / 20;
        Assert.True(cost > 0);

        player.PlayerMana = cost - 1;
        Assert.False(Game.PayMana(player, skill, out _, out _));
        Assert.Equal(cost - 1, player.PlayerMana);

        player.PlayerMana = cost;
        Assert.True(Game.PayMana(player, skill, out uint mana, out uint life));
        Assert.Equal(cost, mana);
        Assert.Equal(0u, life);
        Assert.Equal(0u, player.PlayerMana);
    }

    [Fact]
    public void Vortex_of_Projection_adds_the_adjustment_to_its_resolved_cold_damage_rate()
    {
        int effect = Program.Skills.EffectRow("VortexAltX");
        int RateAt(int level) => Assert.Single(Program.Skills.EffectStats(effect, level),
            stat => stat.Item1 == Stat.BaseColdDamageToDealPerMinute).Item2;

        Assert.Equal(4591, RateAt(1));
        Assert.Equal(126719, RateAt(20));
    }

    [Fact]
    public async Task Vortex_self_cast_creates_chilled_ground_and_deals_cold_dot()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vortex-dot-{Guid.NewGuid():N}.db");
        var previous = Program.GameLogicOrNull;
        var previousLifetime = Game.CombatLifetime;
        var lifetime = new ModuleLifetime("Game"); Game.CombatLifetime = lifetime;
        Saves.Open(path); Program.SetGameLogic(new GameServer.Modules.Game.GameLogic());
        var player = SpellTotemExecutionTests.PlayerOf("VortexSelf");
        try
        {
            var items = Game.ItemsOf(player);
            var wand = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Weapons/OneHandWeapons/Wands/Wand1")!);
            var vortexGem = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SkillGemFrostBoltNova")!);
            items.Add(PlayerInventories.Weapon1, 0, 0, wand with { Sockets = [new ItemSocket(SocketColour.Blue, vortexGem)] });
            var gem = Assert.Single(Game.GemSkillEffects(player));
            player.Clock.Start();
            using var wire = new SpellTotemExecutionTests.Wire();
            player.Connection.Session = new("test", wire.Stream, wire.Ciphers);
            var session = Game.ProjectilesFor(player);
            var monster = new SpawnedMonster("vortex-dummy", new(105, 100, 0), 0, 0, 50000) { Hostile = true };
            player.Area.Monsters[910] = monster;

            long start = session.Runtime.Now;
            session.ScheduleSpatialSpell(gem, (100, 100), (105, 100), 0, () => true,
                new Game.SacrificeUse(player, gem.Id, gem.Effect, gem.Gem), null);
            session.AdvanceTo(start + GameServer.Core.Projectiles.ProjectileRuntime.Time(0.05));
            var hitDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Enqueue(() => { hitDone.SetResult(); return Task.CompletedTask; });
            await hitDone.Task;
            uint lifeAfterHit = monster.Life;
            Assert.True(lifeAfterHit < 50000, $"Expected Vortex initial hit to damage monster, life={lifeAfterHit}");

            session.AdvanceTo(start + GameServer.Core.Projectiles.ProjectileRuntime.Time(1.5));
            double now = session.Runtime.Now / (double)GameServer.Core.Projectiles.ProjectileRuntime.QuantaPerSecond;
            Assert.True(monster.Life < lifeAfterHit, $"Expected Vortex cold DoT to reduce life below {lifeAfterHit}, got {monster.Life}");
            Assert.True(monster.Ailments.Magnitude(MonsterElementalAilments.Kind.Chill, now) > 0);
            session.Invalidate();
        }
        finally
        {
            player.Closed = true;
            lifetime.Stop(_ => { });
            Game.CombatLifetime = previousLifetime;
            PlayerInventories.Forget(player.Character.Name!);
            Program.SetGameLogic(previous!);
            TestEnvironment.ReleaseSaves(path);
        }
    }

    [Fact]
    public async Task Vortex_and_ColdSnap_totems_create_chilled_ground_and_deal_cold_dot()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cold-totem-dot-{Guid.NewGuid():N}.db");
        var previous = Program.GameLogicOrNull;
        var previousLifetime = Game.CombatLifetime;
        var lifetime = new ModuleLifetime("Game"); Game.CombatLifetime = lifetime;
        Saves.Open(path); Program.SetGameLogic(new GameServer.Modules.Game.GameLogic());
        var player = SpellTotemExecutionTests.PlayerOf("ColdTotem");
        try
        {
            foreach (string gemId in new[] { "SkillGemFrostBoltNova", "SkillGemColdSnap" })
            {
                var items = Game.ItemsOf(player);
                foreach (var prev in items.EquipmentSnapshot().Where(e => e.Row == PlayerInventories.Weapon1))
                    items.Remove(PlayerInventories.Weapon1, prev.Item.Id);
                var wand = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Weapons/OneHandWeapons/Wands/Wand1")!);
                var active = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/" + gemId)!);
                var support = ItemCatalog.Create(Program.Items.Find("Metadata/Items/Gems/SupportGemSpellTotem")!);
                items.Add(PlayerInventories.Weapon1, 0, 0, wand with
                {
                    Sockets = [new ItemSocket(SocketColour.Blue, active), new ItemSocket(SocketColour.Red, support)],
                    Links = [2],
                });
                var gem = Assert.Single(Game.GemSkillEffects(player));
                var source = Game.SupportedSkill(player, gem.Id);
                Assert.True(Game.SpellTotemEffect(player, gem.Effect));

                player.Area.Monsters.Clear();
                var monster = new SpawnedMonster("totem-dummy", new(105, 100, 0), 0, 0, 50000) { Hostile = true };
                player.Area.Monsters[920] = monster;
                player.Clock.Start();
                using var wire = new SpellTotemExecutionTests.Wire();
                player.Connection.Session = new("test", wire.Stream, wire.Ciphers);
                var session = Game.ProjectilesFor(player);
                var totem = new TotemState.Totem(800, gem.Id, (100, 100), 100, 100, null)
                {
                    Effect = gem.Effect, SourceGem = gem.Gem, SourceHost = source.Host, SourceItem = source.SourceGem
                };
                player.Totems.Place(totem, 1, 0);
                Game.TickSpellTotemsLocked(player, new List<byte>());
                Assert.True(totem.NextCast > 0);
                double castAt = totem.NextCast;
                long castQuanta = Math.Max(session.Runtime.Now, GameServer.Core.Projectiles.ProjectileRuntime.Time(castAt));
                session.AdvanceTo(castQuanta);
                uint lifeAfterHit = monster.Life;
                Assert.True(lifeAfterHit < 50000, $"Expected {gemId} totem hit to damage monster, life={lifeAfterHit}");

                // Stop totem from casting a second hit so we isolate ground DoT ticks.
                totem.DiedAt = castAt + 0.01;
                session.AdvanceTo(castQuanta + GameServer.Core.Projectiles.ProjectileRuntime.Time(1.2));
                double now = session.Runtime.Now / (double)GameServer.Core.Projectiles.ProjectileRuntime.QuantaPerSecond;
                Assert.True(monster.Life < lifeAfterHit, $"Expected {gemId} totem ground DoT to reduce life below {lifeAfterHit}, got {monster.Life}");
                Assert.True(monster.Ailments.Magnitude(MonsterElementalAilments.Kind.Chill, now) > 0);
                session.Invalidate();
            }
        }
        finally
        {
            player.Closed = true;
            lifetime.Stop(_ => { });
            Game.CombatLifetime = previousLifetime;
            PlayerInventories.Forget(player.Character.Name!);
            Program.SetGameLogic(previous!);
            TestEnvironment.ReleaseSaves(path);
        }
    }
}
