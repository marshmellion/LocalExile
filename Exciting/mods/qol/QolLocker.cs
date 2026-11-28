using System.Reflection;
using System.Runtime.CompilerServices;
using GameServer.Core.Protocol.Instance;

public sealed partial class QolMod
{
    // The Chart Locker's affinity (the Allflame league's locker on the Sovereign's deck). Its tick box sends C→S 0xd3 for the
    // "league locker" tab 0xffff (affinity 28 in 3.29.3d); the Gateway looks for a stash tab of that index and refuses it,
    // so the tick was never kept. Kept here in the league stash's module data; with it on, a Ctrl + click on a chart
    // (C→S 0xe1 naming tab 0xffff) goes into the locker, answered by S→C 0xd5 for tab 0xffff.
    const ushort LockerTab = InstanceStash.LockerTabIndex;
    const string LockerAffinityKey = "qol.chart-locker-affinities";

    void HookChartLocker(string id)
    {
        if (!_choices.Deposit.ChartLocker) return;
        GameEvents.Packet(InstanceStash.SetAffinitiesId).Add(id, SetLockerAffinity);
        GameEvents.Packet(InstanceStash.MoveToAffinityTabId).Add(id, MoveToLocker);
        // Back after a reload: the kept affinities as the game's own 0xd3. Sent when the character enters an area only, the
        // tick did not come back (2026-11-28): the client seems to set up the locker's state later (the stash tab list, the
        // locker window), so it is sent again a few seconds after entering and shortly after any click on an object (the
        // locker, the stash), at most once every few seconds.
        GameEvents.PlayerEntered.Add(id, async player =>
        {
            await SendLockerAffinities(player, "on entering");
            ResendLockerLater(player, 3000, "3 s after entering");
        });
        GameEvents.ObjectUse.Add(id, (player, _, _) =>
        {
            ResendLockerLater(player, 500, "after an object click");
            return Task.FromResult(false);
        });
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Player, StrongBox<long>> LockerResent = new();

    void ResendLockerLater(Player player, int ms, string why)
    {
        if (LockerAffinities(player) == 0) return;
        var last = LockerResent.GetOrCreateValue(player);
        long now = Environment.TickCount64;
        lock (last) { if (now - last.Value < 1500) return; last.Value = now; }
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(ms); if (!player.Closed) await SendLockerAffinities(player, why); }
            catch (Exception e) { _log($"Chart Locker affinities not sent again: {e.Message}"); }
        });
    }

    static async Task SendLockerAffinities(Player player, string why)
    {
        if (LockerAffinities(player) is not (> 0 and var bits)) return;
        var on = Enumerable.Range(1, 31).Where(a => (bits & (1u << a)) != 0).Select(a => ((byte)a, true)).ToList();
        await Program.Send(player, InstanceStash.EncodeSetAffinities(LockerTab, InstanceStash.Personal, on),
            $"qol: 0x00d3 Chart Locker affinities again ({why}) [{string.Join(", ", on.Select(o => o.Item1))}]");
    }

    static uint LockerAffinities(Player player) =>
        uint.TryParse(Game.PersonalStashOf(player).ModuleData(LockerAffinityKey), out uint bits) ? bits : 0;

    async Task<bool> SetLockerAffinity(Player player, byte[] bytes)
    {
        var set = InstanceStash.ReadSetAffinities(bytes);
        if (set.Tab != LockerTab || set.Kind != InstanceStash.Personal) return false;
        uint now = 0;
        Game.PersonalStashOf(player).ChangeModuleData(LockerAffinityKey, old =>
        {
            now = uint.TryParse(old, out uint b) ? b : 0;
            foreach (var (affinity, on) in set.Changes.Where(c => c.Affinity is > 0 and < 32))
                now = on ? now | 1u << affinity : now & ~(1u << affinity);
            return now.ToString();
        });
        _log($"{player.Character.Name}: Chart Locker affinities [{string.Join(", ", set.Changes.Select(c => $"{c.Affinity}={(c.On ? "on" : "off")}"))}]: now 0x{now:x}");
        // The window takes the change as done when it is echoed (as for the stash's tabs).
        await Program.Send(player, set.Encode(), "qol: 0x00d3 Chart Locker affinities echoed");
        return true;
    }

    async Task<bool> MoveToLocker(Player player, byte[] bytes)
    {
        var move = InstanceStash.ReadMoveToAffinityTab(bytes);
        if (move.Tabs.FirstOrDefault(t => t.Tab == LockerTab) is not (LockerTab, var affinity)) return false;
        var items = Game.ItemsOf(player);
        var it = items.Items(Main).Concat(items.Items(PlayerInventories.ExpandedInventory)).FirstOrDefault(i => i.Id == move.ItemId);
        bool stored = it is not null && await IntoChartLocker(player, it, "", force: true);
        if (!stored && move.Tabs.Any(t => t.Tab != LockerTab)) return false;   // another tab was named too: the Gateway tries it
        byte cellX = stored && items.Items(DeepwaterLockerRow).FirstOrDefault(i => i.Id == move.ItemId) is { } placed ? placed.X : (byte)0;
        await Program.Send(player, InstanceStash.EncodeAffinityMoveResult(stored, LockerTab, cellX, affinity),
            $"qol: 0x00d5 Chart Locker affinity move {(stored ? "succeeded" : "refused")}: item {move.ItemId} affinity={affinity}");
        return true;
    }

    // The Deepwater module's DeepwaterLocker.LockerRow (inventory 112).
    const byte DeepwaterLockerRow = 112;
}
