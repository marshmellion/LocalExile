using GameServer.Core.Protocol.Instance;
using GameServer.Core.World;

public static partial class Game
{
    internal const string FungalGroundAo = "Metadata/Effects/Spells/monsters_effects/League_Blight/Fungal_ground/fungalGround.ao";
    const int FungalGroundRadiusOnKill = 22;
    const double FungalGroundDurationSeconds = 4;
    const int MaxFungalGroundPatches = 10;

    sealed class FungalPatch(uint objectId, (int X, int Y) at, int radius, double until, GameArea area)
    {
        public readonly uint ObjectId = objectId;
        public readonly (int X, int Y) At = at;
        public readonly int Radius = radius;
        public readonly double Until = until;
        public readonly GameArea Area = area;
        public bool VisualPending = true;
        public bool VisualCreated;
        public bool Removed;
    }

    sealed class SporeguardState
    {
        public readonly Lock Gate = new();
        public GameArea? Area;
        public (uint ObjectId, (int X, int Y) At, int Radius)? Stationary;
        public bool StationaryCreated;
        public readonly List<FungalPatch> Patches = [];
        public readonly List<uint> PendingRemovals = [];
    }

    static readonly Store<Player, SporeguardState> Sporeguards = new();

    static bool HasCreateFungalGroundOnKill(Player player)
    {
        if (PlayerStatValues(player).GetValueOrDefault(Stat.LocalDisplayTriggerLevelXCreateFungalGroundOnKill) > 0)
            return true;
        var items = ItemsOf(player);
        var mods = ItemsFor(player).Mods;
        foreach (var (_, equipped) in items.EquipmentSnapshot())
            if (!IsInactive(player, equipped.Id)
                && ItemModStats.LocalValue(equipped.Item, mods, Stat.LocalDisplayTriggerLevelXCreateFungalGroundOnKill) > 0)
                return true;
        return false;
    }

    static void ResetSporeguardStateLocked(SporeguardState state, GameArea? area)
    {
        foreach (var patch in state.Patches) patch.Removed = true;
        state.Area = area;
        state.Stationary = null;
        state.StationaryCreated = false;
        state.Patches.Clear();
        state.PendingRemovals.Clear();
    }

    static void PruneExpiredPatchesLocked(SporeguardState state, double now)
    {
        for (int i = state.Patches.Count - 1; i >= 0; i--)
        {
            var patch = state.Patches[i];
            if (now < patch.Until) continue;
            patch.Removed = true;
            if (patch.VisualCreated) state.PendingRemovals.Add(patch.ObjectId);
            state.Patches.RemoveAt(i);
        }
    }

    /// <summary>Whether <paramref name="at"/> lies inside any active Fungal Ground patch owned by
    /// <paramref name="player"/> or placed in the area.</summary>
    internal static bool OnFungalGround(Player player, (int X, int Y) at, double now)
    {
        if (Sporeguards.TryGetValue(player, out var state))
        {
            lock (state.Gate)
            {
                if (ReferenceEquals(state.Area, player.Area))
                {
                    if (state.Stationary is { } stationary && Dist(at, stationary.At) <= stationary.Radius)
                        return true;
                    foreach (var patch in state.Patches)
                        if (!patch.Removed && ReferenceEquals(patch.Area, player.Area) && now < patch.Until
                            && Dist(at, patch.At) <= patch.Radius)
                            return true;
                }
            }
        }
        if (player.MonsterPlaced.Grounds.Count > 0 && GroundKindOf("Fungal")?.BuffRow is ushort fungalBuff and > 0)
        {
            foreach (var ground in player.MonsterPlaced.Grounds.Values.ToArray())
                if (ground.BuffRow == fungalBuff && ReferenceEquals(ground.Area, player.Layout)
                    && now < ground.Until && Dist(at, ground.At) <= ground.Radius)
                    return true;
        }
        return false;
    }

    /// <summary>Creates a 4-second Fungal Ground patch at a killed enemy's position when the player has Sporeguard's
    /// trigger-on-kill Fungal Ground mod.</summary>
    internal static void SporeguardOnKillLocked(Player player, (int X, int Y) at, double now)
    {
        if (!CurrentGameWork || player.Closed || player.LeavingArea || player.PlayerLife == 0 || InTown(player))
            return;
        if (!HasCreateFungalGroundOnKill(player)) return;
        var state = Sporeguards.GetOrCreateValue(player);
        lock (state.Gate)
        {
            if (!ReferenceEquals(state.Area, player.Area))
                ResetSporeguardStateLocked(state, player.Area);
            PruneExpiredPatchesLocked(state, now);
            while (state.Patches.Count >= MaxFungalGroundPatches)
            {
                var oldest = state.Patches[0];
                oldest.Removed = true;
                if (oldest.VisualCreated) state.PendingRemovals.Add(oldest.ObjectId);
                state.Patches.RemoveAt(0);
            }
            state.Patches.Add(new(player.TakeObjectId(), at, FungalGroundRadiusOnKill, now + FungalGroundDurationSeconds, player.Area));
        }
        if (ProjectilesFor(player) is { Active: true } session)
            session.EnqueueLater(() => FlushSporeguardVisuals(player, state));
    }

    /// <summary>Maintains stationary Fungal Ground, expires timed kill patches, and synchronises Fungal Ground visuals.</summary>
    internal static Task TickSporeguardFungalGround(Player player, double now)
    {
        int stationaryRadius = !CurrentGameWork || player.Closed || player.LeavingArea || player.PlayerLife == 0
            || player.Paused || InTown(player)
            ? 0
            : Math.Max(0, PlayerStatValues(player).GetValueOrDefault(Stat.FungalGroundWhileStationaryRadius));
        if (!Sporeguards.TryGetValue(player, out var existing))
        {
            if (stationaryRadius <= 0) return Task.CompletedTask;
            existing = Sporeguards.GetOrCreateValue(player);
        }
        (uint ObjectId, (int X, int Y) At, int Radius)? newStationary = null;
        lock (existing.Gate)
        {
            if (!ReferenceEquals(existing.Area, player.Area))
                ResetSporeguardStateLocked(existing, player.Area);
            PruneExpiredPatchesLocked(existing, now);
            bool wantStationary = stationaryRadius > 0 && !player.Walk.Moving(now);
            var here = wantStationary ? player.Walk.Position(now) : default;
            if (!wantStationary)
            {
                if (existing.Stationary is { } old)
                {
                    if (existing.StationaryCreated) existing.PendingRemovals.Add(old.ObjectId);
                    existing.Stationary = null;
                    existing.StationaryCreated = false;
                }
            }
            else if (existing.Stationary is not { } kept || kept.At != here || kept.Radius != stationaryRadius)
            {
                if (existing.Stationary is { } old)
                {
                    if (existing.StationaryCreated) existing.PendingRemovals.Add(old.ObjectId);
                    existing.StationaryCreated = false;
                }
                uint id = player.TakeObjectId();
                newStationary = (id, here, stationaryRadius);
                existing.Stationary = newStationary;
            }
        }
        return FlushSporeguardVisuals(player, existing, newStationary);
    }

    /// <summary>Clears per-area Sporeguard Fungal Ground state when the player leaves an area.</summary>
    internal static void EndAreaSporeguard(Player player)
    {
        if (!Sporeguards.TryGetValue(player, out var state)) return;
        lock (state.Gate) ResetSporeguardStateLocked(state, null);
        Sporeguards.Remove(player);
    }

    static async Task FlushSporeguardVisuals(Player player, SporeguardState state,
        (uint ObjectId, (int X, int Y) At, int Radius)? stationaryCreate = null)
    {
        if (!CurrentGameWork || player.Closed || player.LeavingArea) return;
        List<uint>? removals = null;
        List<FungalPatch>? creates = null;
        GameArea area = player.Area;
        lock (state.Gate)
        {
            if (!ReferenceEquals(state.Area, area)) return;
            if (state.PendingRemovals.Count > 0)
            {
                removals = [.. state.PendingRemovals];
                state.PendingRemovals.Clear();
            }
            foreach (var patch in state.Patches)
            {
                if (!patch.VisualPending || patch.Removed || !ReferenceEquals(patch.Area, area)) continue;
                patch.VisualPending = false;
                (creates ??= []).Add(patch);
            }
        }
        if (removals is not null)
        {
            foreach (uint id in removals)
                await SendWorldRemoval(player, id, $"0x02bd remove fungal ground 0x{id:x}");
        }
        if (stationaryCreate is { } stationary)
        {
            bool sent = await SendFungalGroundVisual(player, stationary.ObjectId, stationary.At, stationary.Radius,
                () =>
                {
                    lock (state.Gate)
                        return ReferenceEquals(state.Area, area) && state.Stationary is { } s && s.ObjectId == stationary.ObjectId;
                });
            if (sent)
            {
                bool stale = false;
                lock (state.Gate)
                {
                    if (ReferenceEquals(state.Area, area) && state.Stationary is { } s && s.ObjectId == stationary.ObjectId)
                        state.StationaryCreated = true;
                    else
                        stale = true;
                }
                if (stale && ReferenceEquals(player.Area, area) && !player.Closed && !player.LeavingArea)
                    await SendWorldRemoval(player, stationary.ObjectId, $"0x02bd remove stale fungal ground 0x{stationary.ObjectId:x}");
            }
        }
        if (creates is not null)
        {
            foreach (var patch in creates)
            {
                bool sent = await SendFungalGroundVisual(player, patch.ObjectId, patch.At, patch.Radius,
                    () =>
                    {
                        lock (state.Gate)
                            return !patch.Removed && ReferenceEquals(state.Area, area) && ReferenceEquals(patch.Area, area);
                    });
                if (sent)
                {
                    bool stale = false;
                    lock (state.Gate)
                    {
                        if (!patch.Removed && ReferenceEquals(state.Area, area))
                            patch.VisualCreated = true;
                        else
                            stale = true;
                    }
                    if (stale && ReferenceEquals(player.Area, area) && !player.Closed && !player.LeavingArea)
                        await SendWorldRemoval(player, patch.ObjectId, $"0x02bd remove stale fungal ground 0x{patch.ObjectId:x}");
                }
            }
        }
    }

    static Task<bool> SendFungalGroundVisual(Player player, uint id, (int X, int Y) at, int radius, Func<bool> still)
    {
        string ao = GroundKindOf("Fungal") is { Art.Length: > 0 } kind && kind.ArtCells > 0 ? kind.Art : FungalGroundAo;
        float scale = AreaArtScale(radius, ao);
        var effect = new EffectObject(at.X, at.Y, ao) { Flags0 = EffectObject.FixedRotation, Scale = scale };
        return SendWorldCreate(player, id,
            () => InstanceEntities.CreateObject(id, InstanceEntities.ObjectTypeHash(EffectObject.TypePath), effect.Blob()),
            $"0x02bb create fungal ground 0x{id:x} at ({at.X}, {at.Y})",
            still: () => CurrentGameWork && !player.Closed && !player.LeavingArea && still(),
            wake: true, awaitBirth: false);
    }
}
