using GameServer.Core.Protocol.Instance;
using GameServer.Core.Projectiles;

namespace GameServer.Gateway.Tests;

public sealed class PlacedSkillCleanupTests
{
    [Fact]
    public async Task EndAreaPlacedSkills_purges_traps_mines_and_grounds_from_Area_Objects()
    {
        using var fixture = new TriggeredSummonTests.Fixture();
        var player = fixture.Player;
        var clock = new TestClock();
        var previous = Game.AreaUpdateContext.Value;
        player.ProjectileRuntimeState = fixture.Session;
        player.Walk.Teleport(100, 100, clock.At);
        Game.AreaUpdateContext.Value = new(fixture.Lifetime, clock);
        try
        {
            ushort skill = Assert.Single(fixture.Equip(("Stormblast Mine", 1)));
            fixture.Session.AdvanceTo(ProjectileRuntime.Time(clock.At));
            Assert.True(await Game.CastGemSkill(player, skill, (100, 100), (112, 100)));
            var mine = Assert.Single(player.Placed.All.Values);

            // Simulate the persistent instance holding the mine create record before area exit.
            player.Area.Objects.RecordDirect(mine.Id, new byte[20], AreaDelta.Kind.Create, player);
            Assert.True(player.Area.Objects.Has(mine.Id));

            // Simulate area exit on a persistent shared instance: Area.Objects must drop the mine so a returning player
            // does not replay a CreateObject pointing at a stale Parent player ID.
            Game.EndAreaPlacedSkills(player);

            Assert.Empty(player.Placed.All);
            Assert.Empty(player.Placed.Leaving);
            Assert.Empty(player.Placed.Grounds);
            Assert.False(player.Area.Objects.Has(mine.Id));
        }
        finally { Game.AreaUpdateContext.Value = previous; }
    }

    [Fact]
    public void EndAreaPlacedSkills_handles_leaving_and_ground_entries_and_is_idempotent()
    {
        using var fixture = new TriggeredSummonTests.Fixture();
        var player = fixture.Player;
        uint leavingId = 0x9001u, groundId = 0x9002u;
        player.Area.Objects.RecordDirect(leavingId, new byte[20], AreaDelta.Kind.Create, player);
        player.Area.Objects.RecordDirect(groundId, new byte[20], AreaDelta.Kind.Create, player);
        Assert.True(player.Area.Objects.Has(leavingId));
        Assert.True(player.Area.Objects.Has(groundId));

        player.Placed.Leaving.Add((leavingId, 99));
        player.Placed.Grounds.Add(new(groundId, 1, (100, 100), 10, 50, 99, 0));

        Game.EndAreaPlacedSkills(player);
        Game.EndAreaPlacedSkills(player);

        Assert.Empty(player.Placed.Leaving);
        Assert.Empty(player.Placed.Grounds);
        Assert.False(player.Area.Objects.Has(leavingId));
        Assert.False(player.Area.Objects.Has(groundId));
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
