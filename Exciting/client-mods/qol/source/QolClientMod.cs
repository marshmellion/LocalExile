using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using LocalExile.Client.Sdk;
using LocalExile.Client.Sdk.Engine;

namespace LocalExile.Qol.Client;

/// <summary>
/// The QoL mod's overlay, inside the game (NativeMods client mod): buttons on the HUD, the inventory, the stash and the
/// map tab, keys, the server's messages and pop-ups as toasts, price lines over the character, the alternate quality
/// gems panel and the Harvest panel. It asks the qol server mod on this computer (127.0.0.1, GET /state and
/// GET /do/&lt;action&gt;), the same way the overlay window does, so the server mod needs nothing new.
/// </summary>
public sealed class QolClientMod : IClientMod
{
    const string HarvestUi = "Metadata/UI/Mods/Qol/Harvest.ui", GemsUi = "Metadata/UI/Mods/Qol/Gems.ui", PricesUi = "Metadata/UI/Mods/Qol/Prices.ui";
    const int HarvestRows = 12, GemRows = 20, PriceRows = 10;

    IClientContext? _ctx;
    Settings _s = new();
    HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    CancellationTokenSource? _stop;
    volatile string _stateJson = "";
    volatile bool _wantGems, _wantCrafts;
    bool _hidden, _unloading;
    double _sinceApply;

    readonly List<ISlotAttachment> _all = [];
    ISlotAttachment? _prices, _vendor, _sortTab, _maps;
    readonly List<ISlotAttachment> _stashButtons = [];

    string _lastMessage = "";
    int _lastToastId = -1;

    IUiWindow? _priceWindow, _gemWindow, _harvestWindow;
    bool _showGems, _harvestDismissed;
    int _gemPage, _harvestPage, _harvestPick = -1;
    readonly List<(string Args, string Label)> _crafts = [];
    string[] _gemLines = [];

    public void Load(IClientContext context)
    {
        _ctx = context;
        _unloading = false;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        _s = Settings.Read(Path.Combine(context.ModDirectory, "settings.json"), context.Logger);
        IGameServices game = context.Game;

        foreach (string name in new[] { "Harvest.ui", "Gems.ui", "Prices.ui" })
            context.TrackDisposable(game.Files.RegisterVirtualTextFile("Metadata/UI/Mods/Qol/" + name, Resource(name), priority: 50));

        if (_s.HudButtons)
        {
            Slot(ScreenSlot.HudTopRight, "qol.hud.loot", "Loot", "loot", 10, 80, "Bring the loot your filter shows to your feet");
            Slot(ScreenSlot.HudTopRight, "qol.hud.sort", "Sort", "sort", 20, 80, "Sort the inventory");
            _prices = Slot(ScreenSlot.HudTopRight, "qol.hud.prices", "Prices", "prices", 30, 130, "Price lines on picking up currency: on or off");
        }
        if (_s.InventorySortButton)
            Slot(ScreenSlot.InventoryToolbar, "qol.inv.sort", "Sort", "sort", 50, 80, "Sort the inventory");
        if (_s.StashButtons is "header" or "footer")
        {
            ScreenSlot place = _s.StashButtons == "footer" ? ScreenSlot.StashFooter : ScreenSlot.StashHeader;
            _stashButtons.Add(Slot(place, "qol.stash.deposit", "Deposit all", "deposit", 50, 130,
                "Put the inventory into the stash: special tabs, affinity tabs, tabs named after the item's kind, then this tab"));
            _vendor = Slot(place, "qol.stash.vendor", "Vendor tab", "vendor", 60, 170, "Sell everything in the tab shown (press twice)");
            _sortTab = Slot(place, "qol.stash.sorttab", "Sort tab", "sorttab", 70, 110, "Sort the normal, premium or quad tab shown");
            _stashButtons.Add(_vendor);
            _stashButtons.Add(_sortTab);
        }
        if (_s.MapButton)
            _maps = Slot(ScreenSlot.MapTabBar, "qol.maps.upgrade", "Upgrade maps", "maps", 50, 150, "Three maps of a tier make one of the next tier");

        foreach (var (action, label) in new[] { ("loot", "QoL: bring loot"), ("sort", "QoL: sort inventory"), ("deposit", "QoL: deposit all"),
                     ("prices", "QoL: price lines on/off"), ("hide", "QoL: hide/show the QoL buttons"), ("gems", "QoL: alternate quality gems"),
                     ("maps", "QoL: upgrade maps"), ("vendor", "QoL: vendor tab"), ("sorttab", "QoL: sort tab") })
        {
            if (!_s.Keys.TryGetValue(action, out string? key) || string.IsNullOrWhiteSpace(key)) continue;
            if (!KeyChord.TryParse(key, out KeyChord chord)) { context.Logger.Warn($"qol: key '{key}' for {action} is not a key"); continue; }
            string what = action;
            context.TrackDisposable(game.Input.Keybinds.Register("qol." + action, label, chord, () => Key(what),
                allowWhenTextInputFocused: false, suppressGameInput: _s.KeysBlockTheGame));
        }

        game.Frame += OnFrame;
        _stop = new CancellationTokenSource();
        CancellationToken stop = _stop.Token;
        _ = Task.Run(() => Poll(stop));
        context.Logger.Info($"qol in-game panels: asking 127.0.0.1:{_s.Port}");
    }

    public void Unload()
    {
        _unloading = true;
        _stop?.Cancel();
        if (_ctx is { } ctx) ctx.Game.Frame -= OnFrame;
        foreach (var w in new[] { _priceWindow, _gemWindow, _harvestWindow }) try { w?.Close(); } catch (Exception) { }
        _priceWindow = _gemWindow = _harvestWindow = null;
        _all.Clear();
        _stashButtons.Clear();
        _crafts.Clear();
        try { _http.Dispose(); } catch (Exception) { }
        _ctx = null;
    }

    static string Resource(string name)
    {
        using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException("no resource " + name);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    ISlotAttachment Slot(ScreenSlot slot, string id, string text, string action, int order, float width, string tip)
    {
        ISlotAttachment a = _ctx!.Game.Screens.RegisterSlotButton(slot, id, text, () => Do(action),
            new SlotAttachmentOptions(Order: order, Width: width, Height: 30f, Tooltip: tip));
        _ctx.TrackDisposable(a);
        _all.Add(a);
        return a;
    }

    void Key(string action)
    {
        if (action == "hide") { _hidden = !_hidden; return; }
        if (action == "gems") { _showGems = !_showGems; _wantGems = _showGems; _gemPage = 0; return; }
        Do(action);
    }

    void Do(string action)
    {
        int port = _s.Port;
        _ = Task.Run(async () =>
        {
            try { await _http.GetStringAsync($"http://127.0.0.1:{port}/do/{Uri.EscapeDataString(action)}"); }
            catch (Exception) { }
        });
    }

    async Task Poll(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            string query = (_wantGems ? "gems=1&" : "") + (_wantCrafts ? "crafts=1" : "");
            try { _stateJson = await _http.GetStringAsync($"http://127.0.0.1:{_s.Port}/state?{query}", stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception) { _stateJson = ""; }
            try { await Task.Delay(_stateJson.Length == 0 ? 1500 : Math.Max(50, _s.PollMilliseconds), stop); }
            catch (OperationCanceledException) { return; }
        }
    }

    // ---- applied on the game thread, ten times a second ----

    void OnFrame(float dt)
    {
        if (_unloading || _ctx is null) return;
        _sinceApply += dt;
        if (_sinceApply < 0.1) return;
        _sinceApply = 0;
        try { Apply(); }
        catch (Exception e) { _ctx.Logger.Warn("qol: " + e.Message); }
    }

    void Apply()
    {
        var ctx = _ctx!;
        State st = State.Parse(_stateJson);
        bool show = st.Connected && !_hidden && ctx.Game.Screens.IsInGame;
        foreach (var a in _all) SetVisible(a, show);

        if (_prices?.Control is { } p) p.Text = st.Prices ? "Prices: on" : "Prices: off";
        bool gridTab = st.TabType is "NormalStash" or "PremiumStash" or "QuadStash";
        if (_vendor is not null) { SetVisible(_vendor, show && gridTab); if (_vendor.Control is { } v) v.Text = st.VendorArmed ? "Confirm: sell all?" : "Vendor tab"; }
        if (_sortTab is not null) SetVisible(_sortTab, show && gridTab);
        if (_maps is not null) SetVisible(_maps, show && st.TabType == "MapStash");

        if (_s.MessagesAsToasts && st.Message.Length > 0 && st.Message != _lastMessage)
            ctx.Game.Toasts.Show("QoL", st.Message, ToastSeverity.Info, TimeSpan.FromSeconds(Math.Max(1, _s.MessageSeconds)));
        _lastMessage = st.Message;

        if (st.ToastId != _lastToastId)
        {
            if (_lastToastId >= 0 && _s.Popups && st.Toast.Length > 0)
            {
                ctx.Game.Toasts.Show("QoL", st.Toast, ToastSeverity.Success, TimeSpan.FromSeconds(Math.Max(1, _s.PopupSeconds)));
                if (_s.PopupSounds && st.ToastSound.Length > 0 && File.Exists(st.ToastSound)) PlayWav(st.ToastSound);
            }
            _lastToastId = st.ToastId;
        }

        PriceLines(show ? st.Pickups : "");
        Gems(show, st);
        Harvest(show, st);
    }

    static void SetVisible(ISlotAttachment a, bool visible)
    {
        if (a.Control is { } c && c.Visible != visible) c.Visible = visible;
    }

    void PriceLines(string text)
    {
        string[] lines = _s.PriceLines ? text.Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];
        if (lines.Length == 0)
        {
            if (_priceWindow is { IsOpen: true } && _priceWindow.Visible) _priceWindow.Visible = false;
            return;
        }
        if (_priceWindow is not { IsOpen: true })
        {
            _priceWindow = _ctx!.Game.Ui.Load(PricesUi, "qol_prices");
            if (Math.Abs(_s.PriceLinesY - 520) > 0.5f) _priceWindow.SetPosition(_priceWindow.Rect.X, _s.PriceLinesY);
        }
        if (!_priceWindow.Visible) _priceWindow.Visible = true;
        int rows = Math.Clamp(_s.PriceLineRows, 1, PriceRows);
        string[] shown = lines.Length > rows ? lines[^rows..] : lines;
        for (int i = 0; i < PriceRows; i++)
            if (_priceWindow.Find<ILabel>($"line_{i}") is { } l)
            {
                string t = i < shown.Length ? shown[i] : "";
                if (l.Text != t) l.Text = t;
                if (l.Visible != (i < shown.Length)) l.Visible = i < shown.Length;
            }
    }

    void Gems(bool show, State st)
    {
        _wantGems = _showGems;
        if (!_showGems || !show)
        {
            if (_gemWindow is { IsOpen: true }) { _gemWindow.Close(); }
            _gemWindow = null;
            return;
        }
        if (st.Gems.Length > 0) _gemLines = st.Gems.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (_gemWindow is not { IsOpen: true })
        {
            var w = _ctx!.Game.Ui.Load(GemsUi, "qol_gems");
            _gemWindow = w;
            w.Closed += () => { if (ReferenceEquals(_gemWindow, w)) { _gemWindow = null; _showGems = false; } };
            if (w.Find<IButton>("close_button") is { } c) c.Clicked += () => { _showGems = false; };
            if (w.Find<IButton>("prev_button") is { } pr) pr.Clicked += () => _gemPage = Math.Max(0, _gemPage - 1);
            if (w.Find<IButton>("next_button") is { } nx) nx.Clicked += () => _gemPage++;
        }
        int per = Math.Clamp(_s.GemRowsPerPage, 1, GemRows);
        int pages = Math.Max(1, (_gemLines.Length + per - 1) / per);
        _gemPage = Math.Clamp(_gemPage, 0, pages - 1);
        for (int i = 0; i < GemRows; i++)
            if (_gemWindow.Find<ILabel>($"line_{i}") is { } l)
            {
                int at = _gemPage * per + i;
                bool has = i < per && at < _gemLines.Length;
                string t = has ? _gemLines[at] : "";
                if (l.Text != t) l.Text = t;
                if (l.Visible != has) l.Visible = has;
            }
        if (_gemWindow.Find<ILabel>("page_label") is { } pl) pl.Text = $"Page {_gemPage + 1} of {pages}";
    }

    void Harvest(bool show, State st)
    {
        if (!st.Harvest) _harvestDismissed = false;
        if (!_s.HarvestPanel || !show || !st.Harvest || _harvestDismissed)
        {
            _wantCrafts = false;
            if (_harvestWindow is { IsOpen: true }) _harvestWindow.Close();
            _harvestWindow = null;
            return;
        }
        if (_crafts.Count == 0)
        {
            _wantCrafts = true;
            foreach (string line in st.Crafts.Split('\n'))
            {
                int bar = line.IndexOf('|');
                if (bar > 0) _crafts.Add((line[..bar], line[(bar + 1)..]));
            }
            if (_crafts.Count == 0) return;
            _wantCrafts = false;
        }
        if (_harvestWindow is not { IsOpen: true })
        {
            var w = _ctx!.Game.Ui.Load(HarvestUi, "qol_harvest");
            _harvestWindow = w;
            w.Closed += () => { if (ReferenceEquals(_harvestWindow, w)) _harvestWindow = null; };
            if (w.Find<IButton>("close_button") is { } c) c.Clicked += () => _harvestDismissed = true;
            if (w.Find<IButton>("prev_button") is { } pr) pr.Clicked += () => _harvestPage = Math.Max(0, _harvestPage - 1);
            if (w.Find<IButton>("next_button") is { } nx) nx.Clicked += () => _harvestPage++;
            if (w.Find<IButton>("craft_button") is { } cr) cr.Clicked += () =>
            {
                if (_harvestPick >= 0 && _harvestPick < _crafts.Count) Do("hc:" + _crafts[_harvestPick].Args);
            };
            for (int i = 0; i < HarvestRows; i++)
            {
                int row = i;
                if (w.Find<IButton>($"row_{i}") is { } b)
                    b.Clicked += () => { int at = _harvestPage * Math.Clamp(_s.HarvestRowsPerPage, 1, HarvestRows) + row; if (at < _crafts.Count) _harvestPick = at; };
            }
        }
        int per = Math.Clamp(_s.HarvestRowsPerPage, 1, HarvestRows);
        int pages = Math.Max(1, (_crafts.Count + per - 1) / per);
        _harvestPage = Math.Clamp(_harvestPage, 0, pages - 1);
        for (int i = 0; i < HarvestRows; i++)
        {
            int at = _harvestPage * per + i;
            bool has = i < per && at < _crafts.Count;
            if (_harvestWindow.Find<IUiControl>($"row_frame_{i}") is { } f && f.Visible != has) f.Visible = has;
            if (has && _harvestWindow.Find<IButton>($"row_{i}") is { } b)
            {
                string t = (at == _harvestPick ? "> " : "") + _crafts[at].Label;
                if (b.Text != t) b.Text = t;
            }
        }
        if (_harvestWindow.Find<ILabel>("page_label") is { } pl) pl.Text = $"Page {_harvestPage + 1} of {pages}";
        if (_harvestWindow.Find<ILabel>("selected_label") is { } sl)
        {
            string t = _harvestPick >= 0 && _harvestPick < _crafts.Count ? "Picked: " + _crafts[_harvestPick].Label + " (press Craft)" : "Pick a craft, then press Craft.";
            if (sl.Text != t) sl.Text = t;
        }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    static void PlayWav(string path)
    {
        try { PlaySound(path, IntPtr.Zero, 0x00020001); } // SND_FILENAME | SND_ASYNC
        catch (Exception) { }
    }

    // ---- the server mod's /state answer ----

    sealed record State(bool Connected, bool Prices, string TabType, bool VendorArmed, string Message, string Pickups,
        string Toast, string ToastSound, int ToastId, string Gems, bool Harvest, string Crafts)
    {
        static readonly State None = new(false, false, "", false, "", "", "", "", -1, "", false, "");

        public static State Parse(string json)
        {
            if (json.Length == 0) return None;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var r = doc.RootElement;
                string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                bool B(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
                int I(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int x) ? x : -1;
                return new(B("connected"), B("prices"), S("tabType"), B("vendorArmed"), S("message"), S("pickups"), S("toast"),
                    S("toastSound"), I("toastId"), S("gems"), B("harvest"), S("crafts"));
            }
            catch (JsonException) { return None; }
        }
    }

    sealed class Settings
    {
        public int Port { get; set; } = 6170;
        public int PollMilliseconds { get; set; } = 250;
        public Dictionary<string, string> Keys { get; set; } = new()
        {
            ["loot"] = "F5", ["sort"] = "F6", ["deposit"] = "F7", ["prices"] = "F8", ["hide"] = "F9", ["gems"] = "F10",
        };
        public bool KeysBlockTheGame { get; set; }
        public bool HudButtons { get; set; } = true;
        public bool InventorySortButton { get; set; } = true;
        public string StashButtons { get; set; } = "header";
        public bool MapButton { get; set; } = true;
        public bool MessagesAsToasts { get; set; } = true;
        public int MessageSeconds { get; set; } = 5;
        public bool Popups { get; set; } = true;
        public int PopupSeconds { get; set; } = 7;
        public bool PopupSounds { get; set; } = true;
        public bool PriceLines { get; set; } = true;
        public float PriceLinesY { get; set; } = 520;
        public int PriceLineRows { get; set; } = 6;
        public bool HarvestPanel { get; set; } = true;
        public int HarvestRowsPerPage { get; set; } = 12;
        public int GemRowsPerPage { get; set; } = 20;

        public static Settings Read(string path, IModLogger log)
        {
            if (!File.Exists(path)) return new();
            try
            {
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
                }) ?? new();
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                log.Warn($"qol: settings.json not read ({e.Message}): the defaults are used");
                return new();
            }
        }
    }
}
