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

    /// <summary>One button: its own small window (a dark box with a border around the game's button), placed by the
    /// settings in the game's 1600-high coordinates. A negative x counts from the right edge of the screen.</summary>
    sealed class Btn(string id, string path, string action, Place place)
    {
        public readonly string Id = id, Path = path, Action = action;
        public readonly Place Place = place;
        public IUiWindow? Window;
        public IUiControl? Face;
        public bool Failed;
        public float SetX = float.NaN, SetY = float.NaN;
        public string Text = "";
    }
    readonly List<Btn> _buttons = [];
    bool _buttonsFailed, _firstWindowMade;
    const string FirstUi = "Metadata/UI/Mods/Qol/First.ui";
    Btn? _loot, _invSort, _prices, _deposit, _vendor, _sortTab, _maps;

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

        Buttons bs = _s.Buttons;
        context.TrackDisposable(game.Files.RegisterVirtualTextFile(FirstUi,
            "// QoL (in-game panels): the first window (see Show)\n<DEFINITIONS>\n\nbegin Frame\n\tid = qol_first;\n\tsize = 4, 4;\n\tvisible = false;\n\tmouse_enabled = false;\nend\n", priority: 50));
        _loot = Button("loot", "Loot", "loot", bs.Loot, "Bring the loot your filter shows to your feet");
        _invSort = Button("invsort", "Sort", "sort", bs.InventorySort, "Sort the inventory");
        _prices = Button("prices", "Prices: on", "prices", bs.Prices, "Price lines on picking up currency: on or off");
        _deposit = Button("deposit", "Deposit all", "deposit", bs.Deposit,
            "Put the inventory into the stash: special tabs, affinity tabs, tabs named after the item's kind, then this tab");
        _sortTab = Button("sorttab", "Sort tab", "sorttab", bs.SortTab, "Sort the normal, premium or quad tab shown");
        _vendor = Button("vendor", "Vendor tab", "vendor", bs.Vendor, "Sell everything in the tab shown (press twice)");
        _maps = Button("maps", "Upgrade maps", "maps", bs.Maps, "Three maps of a tier make one of the next tier");

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
        foreach (Btn b in _buttons) try { b.Window?.Close(); } catch (Exception) { }
        _buttons.Clear();
        _buttonsFailed = false;
        _firstWindowMade = false;
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

    Btn? Button(string id, string text, string action, Place place, string tip)
    {
        if (!place.Show) return null;
        float w = Math.Clamp(place.Width, 20, 600), h = Math.Clamp(place.Height, 12, 200);
        string path = $"Metadata/UI/Mods/Qol/Button_{id}.ui";
        string clean(string t) => t.Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ');
        string ui = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"""
            // QoL (in-game panels): the {id} button
            <DEFINITIONS>

            begin Frame
            	id = qol_button_{id};
            	aspect_mode = scaley;
            	size = {w}, {h};
            	location = 0, 0;
            	visible = false;
            	mouse_enabled = true;
            	propagate_properties = false;
            	background_colour = argb( 236, 14, 12, 10 );
            	pixel_border_colour = rgb( 137, 100, 64 );

            	begin Button
            		id = btn;
            		location = 0, 0;
            		size = {w}, {h};
            		text = "{clean(text)}";
            	end
            end

            """);
        _ctx!.TrackDisposable(_ctx.Game.Files.RegisterVirtualTextFile(path, ui, priority: 50));
        var b = new Btn(id, path, action, place) { Text = text };
        _buttons.Add(b);
        return b;
    }

    /// <summary>Shows or hides a button; its window is made the first time it shows. A window that cannot be made is
    /// logged once and left alone (the keys and chat commands still work).</summary>
    void Show(Btn? b, bool visible, float screenWidth, float dx = 0, string? text = null)
    {
        // The game's layout reader throws on a file it does not like and is not safe to use again after that, so one
        // failure stops every button from being made (the keys and chat commands still work).
        if (b is null || _buttonsFailed) return;
        if (b.Window is not { IsOpen: true })
        {
            if (!visible) return;
            // Every button's window is made together, the first time one shows: the game then puts them all under
            // the same parent (the one it has at that moment), so their places are measured from the same thing.
            // The first window made after entering the game goes under a different parent from the ones after it and
            // never shows, so a throwaway window is made first.
            if (!_firstWindowMade)
            {
                _firstWindowMade = true;
                try { _ctx!.Game.Ui.Load(FirstUi, "qol_first").Visible = false; }
                catch (Exception e) { _buttonsFailed = true; _ctx!.Logger.Warn($"qol: no buttons: {e.Message}"); return; }
            }
            foreach (Btn each in _buttons)
            {
                if (each.Window is { IsOpen: true }) continue;
                try
                {
                    each.Window = _ctx!.Game.Ui.Load(each.Path, "qol_button_" + each.Id);
                    each.Window.Visible = false;
                    each.Face = each.Window.Find<IButton>("btn");
                    if (each.Face is IButton face) { string action = each.Action; face.Clicked += () => Do(action); }
                    each.SetX = each.SetY = float.NaN;
                }
                catch (Exception e) { each.Failed = true; _buttonsFailed = true; _ctx!.Logger.Warn($"qol: the {each.Id} button was not made, no more buttons are tried: {e.Message}"); return; }
            }
            if (b.Window is not { IsOpen: true }) return;
        }
        if (visible)
        {
            float x = (b.Place.X < 0 ? screenWidth + b.Place.X : b.Place.X) + dx, y = b.Place.Y;
            // The game places a window from the middle of the screen: (0, 0) is a window centred on it. The settings
            // count from the top left corner, so they are converted (buttons.origin "topleft" passes them through).
            if (!string.Equals(_s.Buttons.Origin, "topleft", StringComparison.OrdinalIgnoreCase))
            {
                x -= (screenWidth - b.Place.Width) / 2;
                y -= (1600f - b.Place.Height) / 2;
            }
            if (!(Math.Abs(b.SetX - x) < 0.5f && Math.Abs(b.SetY - y) < 0.5f)) { b.Window.SetPosition(x, y); b.SetX = x; b.SetY = y; }
            if (text is not null && b.Face is { } f && b.Text != text) { f.Text = text; b.Text = text; }
        }
        if (b.Window.Visible != visible) b.Window.Visible = visible;
    }

    // Writes where the game says each button is (to nativemods-shim.log), a few times after the buttons are made
    // and whenever the stash or inventory opens or closes: what a wrong place is worked out from.
    string _lastReport = "";
    int _reports;
    void Report(float sw, bool stash, bool inventory)
    {
        if (!_s.Buttons.LogPlaces || _reports >= 12 || _buttons.TrueForAll(b => b.Window is null)) return;
        var sb = new System.Text.StringBuilder();
        sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"stash={stash} inventory={inventory} screenWidth={sw:0.#}");
        try { if (_ctx!.Game.Ui.GetRootControl() is { } root) sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" root={root.Rect.X:0.#},{root.Rect.Y:0.#} {root.Rect.Width:0.#}x{root.Rect.Height:0.#}"); }
        catch (Exception) { }
        foreach (Btn b in _buttons)
        {
            if (b.Window is not { IsOpen: true } w) continue;
            UiRect r = w.Rect;
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" | {b.Id}: shown={w.Visible} asked={b.SetX:0.#},{b.SetY:0.#} is={r.X:0.#},{r.Y:0.#} {r.Width:0.#}x{r.Height:0.#}");
        }
        string line = sb.ToString();
        if (line == _lastReport) return;
        _lastReport = line;
        _reports++;
        _ctx!.Logger.Info("qol buttons: " + line);
    }

    /// <summary>The screen's width in the game's 1600-high coordinates (2844 at 16:9).</summary>
    float ScreenWidth()
    {
        // The game's own root control does not have the screen's shape (measured: the buttons came out 144 units to
        // the right with it), so the shape comes from the setting: 16:9 unless buttons.screenAspect says otherwise.
        float aspect = _s.Buttons.ScreenAspect > 0.5f ? _s.Buttons.ScreenAspect : 16f / 9f;
        return 1600f * aspect;
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
            string query = "panels=1&" + (_wantGems ? "gems=1&" : "") + (_wantCrafts ? "crafts=1" : "");
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
        float sw = ScreenWidth();
        bool stash = show && st.TabType.Length > 0;
        bool gridTab = st.TabType is "NormalStash" or "PremiumStash" or "QuadStash";
        bool inventory = show && (stash || ctx.Game.Screens.IsPanelOpen(GamePanel.Inventory) || _s.Buttons.InventorySortAlways);
        Show(_loot, show, sw);
        Show(_prices, show, sw, 0, st.Prices ? "Prices: on" : "Prices: off");
        Show(_invSort, inventory, sw);
        // The stash row: the buttons that fit the tab shown, one after the other from the first one's place.
        float dx = 0, gap = _s.Buttons.StashGap;
        void Row(Btn? b, bool on, string? text = null)
        {
            Show(b, on, sw, b is not null && b.Place.InRow ? dx : 0, text);
            if (on && b is { Failed: false } && b.Place.InRow) dx += b.Place.Width + gap;
        }
        Row(_deposit, stash);
        Row(_sortTab, stash && gridTab);
        Row(_vendor, stash && gridTab, st.VendorArmed ? "Sell all?" : "Vendor tab");
        Row(_maps, stash && st.TabType == "MapStash");
        Report(sw, stash, inventory);

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

    /// <summary>Where a button is, in the game's 1600-high coordinates (at 1080p one pixel is about 1.48). A negative
    /// x counts from the right edge of the screen. inRow: it follows the stash buttons before it instead of using its
    /// own x.</summary>
    sealed class Place
    {
        public bool Show { get; set; } = true;
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; } = 130;
        public float Height { get; set; } = 36;
        public bool InRow { get; set; }
    }

    sealed class Buttons
    {
        // Beside the clock, above the flasks.
        public Place Loot { get; set; } = new() { X = 645, Y = 1393, Width = 88, Height = 30 };
        // Above the top right cell of the backpack.
        public Place InventorySort { get; set; } = new() { X = -197, Y = 812, Width = 120, Height = 36 };
        public bool InventorySortAlways { get; set; }
        public Place Prices { get; set; } = new() { Show = false, X = 695, Y = 1393, Width = 150, Height = 30 };
        // The stash row, under the grid and above Highlight Items.
        public Place Deposit { get; set; } = new() { X = 281, Y = 1236, Width = 170, Height = 36, InRow = true };
        public Place SortTab { get; set; } = new() { X = 281, Y = 1236, Width = 130, Height = 36, InRow = true };
        public Place Vendor { get; set; } = new() { X = 281, Y = 1236, Width = 150, Height = 36, InRow = true };
        public Place Maps { get; set; } = new() { X = 281, Y = 1236, Width = 200, Height = 36, InRow = true };
        public float StashGap { get; set; } = 6;
        public string Origin { get; set; } = "centre";
        public float ScreenAspect { get; set; }
        public bool LogPlaces { get; set; } = true;
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
        public Buttons Buttons { get; set; } = new();
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
