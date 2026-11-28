using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using GameServer.Core.Protocol.Instance;

/// <summary>
/// Quality of life for the private play copy: deposit all, vendor a stash tab, sort the inventory, upgrade maps in the
/// map tab, bring the loot the filter shows to the player's feet, a price popup on picking up currency, 65,000 stacks,
/// six sockets on small items, and the unique tab holding copies.
/// <para>The actions are asked for three ways: the overlay (qol-overlay.ps1) through a port on this computer only
/// (127.0.0.1), chat commands (/qol ...), and nothing else. They run on the player's next area tick, inside the game's
/// frame, as the game's own moves do.</para>
/// </summary>
public sealed partial class QolMod : IMod, IDisposable
{
    sealed record PriceChoices(bool Enabled = true, double MinimumChaos = 1, bool Standard = false, bool Overlay = true, double OverlayMinimum = 0.01,
        bool Remember = true, string Style = "float", double FloatSeconds = 3, double FloatHold = 1, string Place = "feet", int Scatter = 40, int FeetX = 0, int FeetY = 0, int FloatRise = 120, int FloatSize = 14);
    sealed record ButtonChoices(string Place = "below", int X = -1, int Y = 852);
    sealed record LootChoices(int MaxItems = 250, bool Gold = true, bool PlayerDropped = false, int LeaveWithin = 12,
        string FilterFolder = "", bool IgnoreFilter = false);
    sealed record DepositChoices(string[] Keep, bool ShownTabLast = true);
    sealed record MapChoices(string[] Rarities, bool Corrupted = false, int Top = 16);
    sealed record Choices(int Port, DepositChoices Deposit, LootChoices Loot, PriceChoices Prices, MapChoices Maps,
        int StackSize = 65000, bool UniqueCopies = true, bool VendorConfirm = true, bool StartOverlay = true,
        RequirementChoices? Requirements = null, bool HarvestPanel = true, bool Toasts = true, bool ToastSounds = true,
        int ToastSeconds = 7, ButtonChoices? StashButtons = null, bool OverlayWithInGameUi = false);

    Choices _choices = new(6170, new(["Portal Scroll", "Scroll of Wisdom"]), new(), new(), new(["normal", "magic", "rare"]));
    Action<string> _log = _ => { };
    TcpListener? _listener;
    CancellationTokenSource? _stop;

    // The players this server has ticked lately, newest last: the overlay acts for the one ticked most recently.
    readonly ConditionalWeakTable<Player, PlayerState> _states = new();
    readonly List<WeakReference<Player>> _seen = [];

    sealed class PlayerState
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Asked = new();
        public double LastTick;
        public DateTime LastSeen;
        public string Message = "";
        public DateTime MessageAt;
        public DateTime VendorArmedUntil;
        public int VendorArmedTab = -1;
        public bool Prices = true;
        public bool Busy;
        public DateTime NextWatch;
        // The overlay's price lines: what was picked up and its worth, newest last.
        public readonly List<(int Id, DateTime At, string Text)> Pickups = [];
        public int PickupId;
        // Pop-ups other mods ask for (localexile.qol.toast): the newest one shows, with its sound.
        public string Toast = "";
        public string ToastSound = "";
        public DateTime ToastAt;
        public int ToastId;
    }

    // The Player object is new in every area, so the price switch is kept by character name (and saved, unless the
    // settings say otherwise): it no longer goes back to its default on each area change.
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _pricesOn = new(StringComparer.OrdinalIgnoreCase);
    string _pricesFile = "";

    PlayerState StateOf(Player player) => _states.GetValue(player, p =>
        new() { Prices = _pricesOn.TryGetValue(p.Character.Name ?? "", out bool on) ? on : _choices.Prices.Enabled });

    void SetPrices(Player player, PlayerState state, bool on)
    {
        state.Prices = on;
        _pricesOn[player.Character.Name ?? ""] = on;
        if (!_choices.Prices.Remember || _pricesFile.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_pricesFile)!);
            File.WriteAllText(_pricesFile, JsonSerializer.Serialize(_pricesOn.ToDictionary(k => k.Key, k => k.Value)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log($"price switches not saved: {e.Message}"); }
    }

    void LoadPrices()
    {
        // Outside the mod's folder: a file changing there reloads the mod.
        _pricesFile = Path.GetFullPath(Path.Combine("data", "moddata", "qol", "prices.json"));
        if (!_choices.Prices.Remember || !File.Exists(_pricesFile)) return;
        try
        {
            if (JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(_pricesFile)) is { } saved)
                foreach (var (name, on) in saved) _pricesOn[name] = on;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _log($"price switches not read: {e.Message}"); }
    }

    public void Load(IModContext mod)
    {
        string id = mod.Manifest.Id;
        _choices = mod.Settings<Choices>();
        _log = mod.Log;
        LoadPrices();
        GameEvents.AreaTick.Add(id, GameEvents.Always(Tick));
        // Any mod can show a pop-up on the overlay: (player, text, the full path of a .wav to play or null).
        AppDomain.CurrentDomain.SetData("localexile.qol.toast", (Action<Player, string, string?>)((player, text, sound) =>
        {
            if (!_choices.Toasts) return;
            var state = StateOf(player);
            lock (state.Pickups)
            {
                state.Toast = text;
                state.ToastSound = _choices.ToastSounds ? sound ?? "" : "";
                state.ToastAt = DateTime.UtcNow;
                state.ToastId++;
            }
        }));
        GameEvents.Command(id, "qol",
            "/qol deposit | vendor | sort | sorttab | maps | loot | prices [on|off] | status (quality of life; the overlay does the same)",
            async (player, args) =>
            {
                string what = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
                if (what == "prices" && args.Length > 1) what = "prices-" + args[1].ToLowerInvariant();
                Ask(player, what);
                await Task.CompletedTask;
            });
        try { WriteStackPack(mod.Folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { mod.Log($"content/stacks.json not written: {e.Message}"); }
        LoadRequirements(mod);
        StartListening();
        if (_choices.StartOverlay) StartOverlay(mod.Folder);
        mod.Log($"port 127.0.0.1:{_choices.Port}, stacks {_choices.StackSize}");
    }

    /// <summary>The overlay (overlay/qol-overlay.ps1) with Windows PowerShell; it keeps to one copy itself.</summary>
    void StartOverlay(string folder)
    {
        if (!OperatingSystem.IsWindows()) return;
        // The in-game QoL panels (NativeMods client mod Mods\Client\qol, loaded by Client\version.dll) do what the
        // overlay does, inside the game: the overlay then stays off unless asked for as well.
        string root = Path.GetFullPath(Path.Combine(folder, "..", "..", ".."));
        if (!_choices.OverlayWithInGameUi && File.Exists(Path.Combine(root, "Mods", "Client", "qol", "mod.json"))
            && File.Exists(Path.Combine(root, "Client", "version.dll")))
        {
            _log("the in-game QoL panels are installed: the overlay is not started (setting overlayWithInGameUi)");
            return;
        }
        string script = Path.Combine(folder, "overlay", "qol-overlay.ps1");
        if (!File.Exists(script)) { _log($"no overlay at {script}"); return; }
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true };
            System.Diagnostics.Process.Start(start)?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { _log($"the overlay did not start: {e.Message}"); }
    }

    public void Dispose()
    {
        DisposeRequirements();
        _stop?.Cancel();
        try { _listener?.Stop(); } catch (SocketException) { }
        _listener = null;
    }

    void Ask(Player player, string what) => StateOf(player).Asked.Enqueue(what);

    void Tell(Player player, string text)
    {
        var state = StateOf(player);
        state.Message = text;
        state.MessageAt = DateTime.UtcNow;
        _ = Program.PlayerOf(player).Tell("QoL: " + text);
    }

    async Task Tick(Player player, double now)
    {
        // Only a player at a keyboard: a body the server drives (Player bots in town, a copy in your party) has no overlay,
        // and taking it for yours made the stash buttons vanish whenever it ticked last.
        if (!player.HasClient) return;
        var state = StateOf(player);
        state.LastSeen = DateTime.UtcNow;
        state.LastTick = now;
        lock (_seen)
        {
            _seen.RemoveAll(w => !w.TryGetTarget(out var p) || ReferenceEquals(p, player) || p.Closed);
            _seen.Add(new(player));
        }
        if (player.Closed || player.LeavingArea || !player.Connection.PlayerSent) return;
        // The watches run ten times a second at most; the asked actions on every tick.
        if (DateTime.UtcNow >= state.NextWatch)
        {
            state.NextWatch = DateTime.UtcNow.AddMilliseconds(100);
            await WatchPrices(player, state);
            await RefillUniques(player);
        }
        if (state.Busy) return;
        while (state.Asked.TryDequeue(out string? what))
        {
            state.Busy = true;
            try { await Run(player, state, what); }
            catch (Exception e)
            {
                _log($"{what} failed: {e}");
                Tell(player, $"{what} failed: {e.Message}");
            }
            finally { state.Busy = false; }
        }
    }

    async Task Run(Player player, PlayerState state, string what)
    {
        switch (what)
        {
            case "deposit": await DepositAll(player); break;
            case "vendor": await VendorTab(player, state); break;
            case "sort": await SortInventory(player); break;
            case "sorttab": await SortTab(player); break;
            case "maps": await UpgradeMaps(player); break;
            case "loot": await BringLoot(player); break;
            case "prices": SetPrices(player, state, !state.Prices); Tell(player, $"price popups {(state.Prices ? "on" : "off")}"); break;
            case "prices-on": SetPrices(player, state, true); Tell(player, "price popups on"); break;
            case "prices-off": SetPrices(player, state, false); Tell(player, "price popups off"); break;
            case "status":
                Tell(player, $"stash {(player.StashOpen ? "open" : "closed")}, price popups {(state.Prices ? "on" : "off")}, " +
                    $"overlay port 127.0.0.1:{_choices.Port}");
                break;
            case var hc when hc.StartsWith("hc:", StringComparison.Ordinal):
                // A Harvest craft from the overlay's Harvest panel, run by the harvestcrafts mod as /hc would.
                if (AppDomain.CurrentDomain.GetData("localexile.harvest.run") is Func<Player, string[], Task> craft)
                    await craft(player, hc[3..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
                else Tell(player, "the harvestcrafts mod is not loaded");
                break;
            default: Tell(player, $"unknown action \"{what}\""); break;
        }
    }

    Player? Current()
    {
        lock (_seen)
        {
            for (int i = _seen.Count - 1; i >= 0; i--)
                if (_seen[i].TryGetTarget(out var p) && !p.Closed && _states.TryGetValue(p, out var s)
                    && DateTime.UtcNow - s.LastSeen < TimeSpan.FromSeconds(5)) return p;
        }
        return null;
    }

    // ---- the overlay's port: GET /state, GET /do/<action>; one request per connection ----

    void StartListening()
    {
        _stop = new();
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _choices.Port);
            _listener.Start();
        }
        catch (SocketException e)
        {
            _log($"the overlay port {_choices.Port} cannot be opened ({e.Message}): only the chat commands work");
            _listener = null;
            return;
        }
        var listener = _listener;
        var stop = _stop.Token;
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(stop); }
                catch (Exception) when (stop.IsCancellationRequested) { return; }
                catch (Exception e) { _log($"overlay port: {e.Message}"); await Task.Delay(1000, CancellationToken.None); continue; }
                _ = Task.Run(() => Serve(client));
            }
        });
    }

    async Task Serve(TcpClient client)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 2000;
            var stream = client.GetStream();
            var buffer = new byte[2048];
            int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            string request = Encoding.ASCII.GetString(buffer, 0, read);
            string path = request.Split(' ') is [_, var p, ..] ? p : "/";
            string body = Answer(path);
            byte[] content = Encoding.UTF8.GetBytes(body);
            string head = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(content);
        }
        catch (Exception e) when (e is IOException or SocketException or TimeoutException or ObjectDisposedException) { }
    }

    string Answer(string path)
    {
        var player = Current();
        if (path.StartsWith("/do/", StringComparison.Ordinal))
        {
            if (player is null) return JsonSerializer.Serialize(new { ok = false, message = "no character in game" });
            string what = Uri.UnescapeDataString(path[4..]).ToLowerInvariant();
            Ask(player, what);
            return JsonSerializer.Serialize(new { ok = true, message = $"{what} asked" });
        }
        if (player is null) return JsonSerializer.Serialize(new { connected = false });
        var state = StateOf(player);
        string tabType = "", tabName = "";
        if (player.StashOpen)
        {
            try
            {
                var stash = Game.StashOf(player);
                int tab = stash.LastTab;
                tabType = stash.TypeOf(tab);
                tabName = stash.NameOf(tab);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException) { }
        }
        bool fresh = DateTime.UtcNow - state.MessageAt < TimeSpan.FromSeconds(6);
        bool toastFresh = DateTime.UtcNow - state.ToastAt < TimeSpan.FromSeconds(Math.Max(1, _choices.ToastSeconds));
        // The Harvest panel: the station holds an item (harvestcrafts), and the crafts once the overlay asks (/state?crafts=1).
        bool station = _choices.HarvestPanel && AppDomain.CurrentDomain.GetData("localexile.harvest.station") is Func<Player, bool> atStation && atStation(player);
        string crafts = station && path.Contains("crafts=1", StringComparison.Ordinal) && AppDomain.CurrentDomain.GetData("localexile.harvest.crafts") is string[] menu
            ? string.Join("\n", menu) : "";
        // The gem panel's text, only while it is open (/state?gems=1): Exciting Items' alternate quality gems.
        string gems = "";
        if (path.Contains("gems=1", StringComparison.Ordinal))
            gems = AppDomain.CurrentDomain.GetData("localexile.altgems") is Func<Player, string[]> altGems
                ? (altGems(player) is { Length: > 0 } lines ? string.Join("\n", lines) : "No alternate quality gems in your backpack or sockets.")
                : "Exciting Items (with its legacy part) is not loaded: no alternate quality gems.";
        return JsonSerializer.Serialize(new
        {
            connected = true,
            character = Program.PlayerOf(player).Character,
            stash = player.StashOpen,
            tabType,
            tabName,
            prices = state.Prices,
            vendorArmed = DateTime.UtcNow < state.VendorArmedUntil,
            message = fresh ? state.Message : "",
            pickups = Pickups(state, false),
            pickupList = Pickups(state, true),
            pickupStyle = _choices.Prices.Style,
            floatSeconds = _choices.Prices.FloatSeconds,
            floatHold = _choices.Prices.FloatHold,
            pickupPlace = _choices.Prices.Place,
            pickupScatter = _choices.Prices.Scatter,
            feetX = _choices.Prices.FeetX,
            feetY = _choices.Prices.FeetY,
            floatRise = _choices.Prices.FloatRise,
            floatSize = _choices.Prices.FloatSize,
            buttonsPlace = (_choices.StashButtons ?? new()).Place,
            buttonsX = (_choices.StashButtons ?? new()).X,
            buttonsY = (_choices.StashButtons ?? new()).Y,
            toast = toastFresh ? state.Toast : "",
            toastSound = toastFresh ? state.ToastSound : "",
            toastId = state.ToastId,
            gems,
            harvest = station,
            crafts,
        });
    }
}
