using GameServer.Core.Protocol.Instance;

namespace GameServer.Gateway.Tests;

// Several Spellslingers run side by side, as live: each reserves for its own linked spells, keeps its own buff instance
// (same row, same source, told apart by its skill id as tag) and comes back from the save.
public sealed class SpellslingerTests
{
    [Fact]
    public async Task Two_spellslingers_start_publish_their_own_buffs_and_restore_together()
    {
        using var f = new TriggeredSummonTests.Fixture();
        var player = f.Player;
        uint id = Game.ItemsOf(player).Items(PlayerInventories.Weapon1).Single().Id;
        Game.ItemsOf(player).Change(PlayerInventories.Weapon1, id, item => item with
        {
            Sockets = new[] { "Spellslinger", "Fireball", "Spellslinger", "Arc" }.Select(n =>
            {
                var type = Program.Items.FindGem(n)!.Value;
                return new ItemSocket(SocketColour.White, ItemCatalog.Create(type.Type) with
                {
                    GemVariant = type.Variant.Index, GemExperience = Program.Items.Gem(type.Type)!.ExperienceFor(20)
                });
            }).ToArray(),
            Links = [2, 2],
        });
        var slingers = Game.GemSkillEffects(player).Where(g => Program.Skills.EffectId(g.Effect) == "Spellslinger").ToArray();
        Assert.Equal(2, slingers.Length);

        // Starting one Spellslinger must not auto-start the second during aura reconciliation.
        Assert.True(await Game.ToggleAura(player, slingers[0].Id, slingers[0].Effect, slingers[0].Name,
            Game.SupportedReservationCost(player, slingers[0].Id)!.Value, slingers[0].Level, slingers[0].Quality));
        Game.WithOwnAuraRecords(player, () => []);
        Assert.Single(player.Auras);

        Assert.True(await Game.ToggleAura(player, slingers[1].Id, slingers[1].Effect, slingers[1].Name,
            Game.SupportedReservationCost(player, slingers[1].Id)!.Value, slingers[1].Level, slingers[1].Quality));
        var auras = player.Auras.Values.Where(a => a.EffectId == "Spellslinger").ToArray();
        Assert.Equal(2, auras.Length);

        // Publishing both buffs used to throw on the duplicate key (same row and source).
        Game.WithOwnAuraRecords(player, () => []);
        player.Area.Buffs.Publish();
        ushort row = AuraCatalog.BuffRow("Spellslinger");
        foreach (var gem in slingers) Assert.True(player.Area.Buffs.Has(player.Id, row, player.Id, tag: gem.Id));

        // A saved Spellslinger brings back every one that fits.
        Assert.Equal(2, AuraCatalog.Restore(["Spellslinger"], auras, player.Pools).Count);

        // Right-clicking one Spellslinger buff cancels only that instance and preserves saved intent for the remaining one.
        Game.LoadBuffCancelData();
        ushort FindBuff(ushort tag) => Enumerable.Range(1, 200).Select(i => (ushort)i)
            .First(i => player.Area.Buffs.Find(player.Id, i) is { } b && b.Row == row && b.Tag == tag);
        Assert.True(await Game.CancelRemovableBuff(player, FindBuff(slingers[0].Id)));
        Game.WithOwnAuraRecords(player, () => []);
        player.Area.Buffs.Publish();
        Assert.False(player.Auras.ContainsKey(slingers[0].Id));
        Assert.True(player.Auras.ContainsKey(slingers[1].Id));
        Assert.False(player.Area.Buffs.Has(player.Id, row, player.Id, tag: slingers[0].Id));
        Assert.True(player.Area.Buffs.Has(player.Id, row, player.Id, tag: slingers[1].Id));
        string character = player.Character.Name!;
        Assert.Contains("Spellslinger", CharacterSaves.Remembered(character).Auras);

        // Cancelling the last Spellslinger clears the saved intent as well.
        Assert.True(await Game.CancelRemovableBuff(player, FindBuff(slingers[1].Id)));
        player.Area.Buffs.Publish();
        Assert.Empty(player.Auras);
        Assert.DoesNotContain("Spellslinger", CharacterSaves.Remembered(character).Auras);
    }
}
