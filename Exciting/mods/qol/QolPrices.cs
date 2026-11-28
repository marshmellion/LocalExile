using System.Runtime.CompilerServices;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Trade;

public sealed partial class QolMod
{
    // The Faustus rates the live economy mod saved (ExchangeRateStore.File), read again when the file changes.
    ExchangeRates? _rates;
    DateTime _ratesWritten;

    Dictionary<string, ExchangePrice>? Prices()
    {
        string file = ExchangeRateStore.File;
        if (!File.Exists(file)) return null;
        var written = File.GetLastWriteTimeUtc(file);
        if (_rates is null || written != _ratesWritten)
        {
            try { _rates = ExchangeRates.Parse(File.ReadAllText(file)); _ratesWritten = written; }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return _rates?.Prices(_choices.Prices.Standard); }
        }
        return _rates?.Prices(_choices.Prices.Standard);
    }

    /// <summary>The price lines of the last 6 seconds, for the overlay; with their ids ("id|text") for the floating
    /// style, which shows each line once.</summary>
    static string Pickups(PlayerState state, bool ids)
    {
        lock (state.Pickups)
        {
            state.Pickups.RemoveAll(p => DateTime.UtcNow - p.At > TimeSpan.FromSeconds(6));
            return string.Join("\n", state.Pickups.Select(p => ids ? p.Id + "|" + p.Text : p.Text));
        }
    }

    sealed class PriceWatch
    {
        public readonly Dictionary<uint, (int X, int Y, uint Type, int Stack)> OnGround = [];
        public readonly Dictionary<uint, int> Held = [];
        public object? Ground;
    }
    readonly ConditionalWeakTable<Player, PriceWatch> _watches = new();

    byte? _chaosIcon, _divineIcon;
    byte PopupIcon(bool divine)
    {
        if (_chaosIcon is null)
        {
            var rows = GameServer.Core.ClientTables.Table("WorldPopupIconTypes").ToList();
            int Row(string id, int fallback) => rows.FindIndex(r => r.Text("Id") == id) is int i and >= 0 ? i : fallback;
            // The rows of qol's content pack, or the Sanctum currency's when the pack is not applied.
            _chaosIcon = (byte)(Row("LocalExileChaosPopup", 2) + 1);
            _divineIcon = (byte)(Row("LocalExileDivinePopup", 2) + 1);
        }
        return divine ? _divineIcon!.Value : _chaosIcon!.Value;
    }

    /// <summary>On each tick: a priced item that left the ground while the player's own count of its type grew was
    /// picked up; its stack's worth shows as a number popup where it lay, in divines from one divine up, else in chaos.</summary>
    async Task WatchPrices(Player player, PlayerState state)
    {
        if (!state.Prices) return;
        var prices = Prices();
        if (prices is null) return;
        var watch = _watches.GetOrCreateValue(player);
        var ground = player.Instance.Ground;
        var items = Game.ItemsOf(player);
        if (!ReferenceEquals(watch.Ground, ground)) { watch.Ground = ground; watch.OnGround.Clear(); watch.Held.Clear(); }
        var catalog = Game.ItemsFor(player);
        var now = new Dictionary<uint, (int, int, uint, int)>();
        foreach (var (id, lying) in ground.All())
            if (catalog.ByHash(lying.Item.TypeHash) is { } type && prices.ContainsKey(type.Id))
                now[id] = (lying.GridX, lying.GridY, lying.Item.TypeHash, lying.Item.StackSize);
        var held = new Dictionary<uint, int>();
        foreach (byte row in new[] { PlayerInventories.MainInventory, PlayerInventories.Cursor })
            foreach (var it in items.Items(row))
                held[it.Item.TypeHash] = held.GetValueOrDefault(it.Item.TypeHash) + it.Item.StackSize;
        double divine = prices.TryGetValue(ExchangeRates.Divine, out var d) ? d.Chaos : 0;
        foreach (var (id, (x, y, typeHash, stack)) in watch.OnGround)
        {
            if (now.ContainsKey(id)) continue;
            if (held.GetValueOrDefault(typeHash) <= watch.Held.GetValueOrDefault(typeHash)) continue;
            if (catalog.ByHash(typeHash) is not { } type || !prices.TryGetValue(type.Id, out var price)) continue;
            double chaos = price.Chaos * stack;
            if (chaos <= 0) continue;
            // The overlay's line: any worth, to the cent (the game's own popup below shows whole numbers only).
            if (_choices.Prices.Overlay && chaos >= _choices.Prices.OverlayMinimum)
                lock (state.Pickups)
                {
                    string worth = divine > 0 && chaos >= divine && type.Id != ExchangeRates.Divine ? $"{chaos / divine:0.##} divine" : $"{chaos:0.##} chaos";
                    state.Pickups.Add((++state.PickupId, DateTime.UtcNow, $"+{worth}  {(stack > 1 ? stack + " x " : "")}{type.Name}"));
                    if (state.Pickups.Count > 8) state.Pickups.RemoveAt(0);
                }
            if (chaos < Math.Max(1, _choices.Prices.MinimumChaos)) continue;
            bool inDivines = divine > 0 && chaos >= divine && type.Id != ExchangeRates.Divine;
            int amount = (int)Math.Round(inDivines ? chaos / divine : chaos);
            if (amount <= 0) continue;
            await Program.Send(player, InstanceEntities.WorldPopupIcon(x, y, player.Id, PopupIcon(inDivines), amount),
                $"qol: {stack} {type.Name} worth {amount} {(inDivines ? "divine" : "chaos")}");
        }
        watch.OnGround.Clear();
        foreach (var (id, v) in now) watch.OnGround[id] = v;
        watch.Held.Clear();
        foreach (var (k, v) in held) watch.Held[k] = v;
    }
}
