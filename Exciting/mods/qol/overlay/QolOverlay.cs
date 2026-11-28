// The qol overlay: buttons drawn over the game window and keybinds read while the game has focus. It talks to the qol
// mod in the Gateway through 127.0.0.1 only (GET /state, GET /do/<action>). Compiled by qol-overlay.ps1 with Windows
// PowerShell's own C# compiler, so it is written for C# 5 (no string interpolation, no "?.", no "=>" members).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

public static class QolNative
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}

// One price line that rises and fades: a window of its own (click-through, no box), so its whole opacity can fall to
// nothing while the overlay stays as it is. The text is drawn without smoothing, with a dark outline, because a colour-keyed
// window shows smoothed edges as a coloured fringe.
public sealed class QolFloatText : Form
{
    const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_LAYERED = 0x80000;
    static readonly Color Clear = Color.FromArgb(1, 2, 3);
    readonly string _text;
    readonly Font _font;
    public DateTime Born;
    public int StartX, StartY;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams p = base.CreateParams;
            p.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
            return p;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    public QolFloatText(string text, float size)
    {
        _text = text;
        _font = new Font("Segoe UI", size, FontStyle.Bold);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Clear;
        TransparencyKey = Clear;
        Born = DateTime.UtcNow;
        Size measured = TextRenderer.MeasureText(text, _font);
        ClientSize = new Size(measured.Width + 12, measured.Height + 8);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            path.AddString(_text, _font.FontFamily, (int)_font.Style, g.DpiY * _font.SizeInPoints / 72f, new Point(4, 3), StringFormat.GenericDefault);
            using (Pen outline = new Pen(Color.FromArgb(10, 8, 4), 4f))
            {
                outline.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                g.DrawPath(outline, path);
            }
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(255, 215, 120))) g.FillPath(fill, path);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font.Dispose();
        base.Dispose(disposing);
    }
}

public sealed class QolOverlay : Form
{
    const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_LAYERED = 0x80000;
    static readonly Color Clear = Color.FromArgb(1, 2, 3);

    readonly string _base;
    readonly Dictionary<string, string> _keys;
    readonly Dictionary<string, bool> _down = new Dictionary<string, bool>();
    readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
    readonly Label _message = new Label();
    readonly Label _gems = new Label();
    readonly Label _pickups = new Label();
    readonly Label _toast = new Label();
    readonly List<QolFloatText> _floats = new List<QolFloatText>();
    int _lastPickupId;
    int _lastToastId = -1;
    readonly FlowLayoutPanel _harvest = new FlowLayoutPanel();
    readonly ListBox _crafts = new ListBox();
    readonly List<string> _craftArgs = new List<string>();
    volatile bool _wantCrafts;
    volatile bool _showGems;
    readonly FlowLayoutPanel _bar = new FlowLayoutPanel();
    readonly FlowLayoutPanel _stash = new FlowLayoutPanel();
    readonly Button _prices, _vendor, _maps, _sortTab;
    volatile string _state = "";
    bool _hidden;
    DateTime _messageUntil;
    string _lastMessage = "";

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams p = base.CreateParams;
            p.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
            return p;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    public QolOverlay(int port, Dictionary<string, string> keys)
    {
        _base = "http://127.0.0.1:" + port + "/";
        _keys = keys;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Clear;
        TransparencyKey = Clear;
        Text = "QoL overlay";

        _bar.FlowDirection = FlowDirection.LeftToRight;
        _bar.AutoSize = true;
        _bar.BackColor = Clear;
        _bar.Controls.Add(MakeButton("Loot", "loot", "Bring the loot your filter shows to your feet"));
        _bar.Controls.Add(MakeButton("Sort", "sort", "Sort the inventory"));
        _prices = MakeButton("Prices", "prices", "Price popups on picking up currency: on or off");
        _bar.Controls.Add(_prices);
        Controls.Add(_bar);

        _stash.FlowDirection = FlowDirection.LeftToRight;
        _stash.WrapContents = false;
        _stash.AutoSize = true;
        _stash.BackColor = Clear;
        _stash.Controls.Add(MakeButton("Deposit all", "deposit", "Put the inventory into the stash: special tabs, affinity tabs, tabs named after the item's kind, then this tab"));
        _vendor = MakeButton("Vendor tab", "vendor", "Sell everything in the tab shown (press twice)");
        _stash.Controls.Add(_vendor);
        _maps = MakeButton("Upgrade maps", "maps", "Three maps of a tier make one of the next tier");
        _stash.Controls.Add(_maps);
        _sortTab = MakeButton("Sort tab", "sorttab", "Sort the normal, premium or quad tab shown");
        _stash.Controls.Add(_sortTab);
        _stash.Controls.Add(MakeButton("Sort inventory", "sort", "Sort the inventory"));
        _stash.Visible = false;
        Controls.Add(_stash);

        _message.AutoSize = true;
        _message.BackColor = Color.FromArgb(20, 16, 12);
        _message.ForeColor = Color.FromArgb(230, 210, 160);
        _message.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
        _message.Padding = new Padding(6, 3, 6, 3);
        _message.Visible = false;
        Controls.Add(_message);

        _harvest.FlowDirection = FlowDirection.TopDown;
        _harvest.AutoSize = true;
        _harvest.BackColor = Color.FromArgb(16, 22, 14);
        _harvest.Padding = new Padding(4);
        Label title = new Label();
        title.Text = "Classic Harvest crafts";
        title.AutoSize = true;
        title.ForeColor = Color.FromArgb(170, 230, 140);
        title.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _harvest.Controls.Add(title);
        _crafts.Width = 330;
        _crafts.Height = 260;
        _crafts.BackColor = Color.FromArgb(24, 30, 20);
        _crafts.ForeColor = Color.FromArgb(220, 235, 200);
        _crafts.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        _crafts.BorderStyle = BorderStyle.FixedSingle;
        _crafts.DoubleClick += delegate { Craft(); };
        _harvest.Controls.Add(_crafts);
        Button craft = MakeButton("Craft", "", "Run the craft picked above on the item in the station (double-click works too)");
        craft.Click += delegate { Craft(); };
        _harvest.Controls.Add(craft);
        _harvest.Visible = false;
        Controls.Add(_harvest);

        _pickups.AutoSize = true;
        _pickups.BackColor = Color.FromArgb(14, 12, 8);
        _pickups.ForeColor = Color.FromArgb(255, 215, 120);
        _pickups.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _pickups.Padding = new Padding(6, 3, 6, 3);
        _pickups.Visible = false;
        Controls.Add(_pickups);
        _toast.AutoSize = true;
        _toast.BackColor = Color.FromArgb(20, 10, 30);
        _toast.ForeColor = Color.FromArgb(235, 190, 255);
        _toast.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
        _toast.Padding = new Padding(14, 8, 14, 8);
        _toast.TextAlign = ContentAlignment.MiddleCenter;
        _toast.Visible = false;
        Controls.Add(_toast);

        _gems.AutoSize = true;
        _gems.MaximumSize = new Size(900, 0);
        _gems.BackColor = Color.FromArgb(18, 14, 22);
        _gems.ForeColor = Color.FromArgb(200, 225, 255);
        _gems.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
        _gems.Padding = new Padding(8, 6, 8, 6);
        _gems.Visible = false;
        Controls.Add(_gems);

        Thread poll = new Thread(Poll);
        poll.IsBackground = true;
        poll.Start();
        _timer.Interval = 50;
        _timer.Tick += delegate { Step(); };
        _timer.Start();
    }

    void Craft()
    {
        int at = _crafts.SelectedIndex;
        if (at < 0 || at >= _craftArgs.Count) return;
        Do("hc:" + Uri.EscapeDataString(_craftArgs[at]));
    }

    Button MakeButton(string text, string action, string tip)
    {
        Button b = new Button();
        b.Text = text;
        b.AutoSize = true;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Color.FromArgb(120, 95, 50);
        b.BackColor = Color.FromArgb(28, 22, 16);
        b.ForeColor = Color.FromArgb(225, 200, 145);
        b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        b.Margin = new Padding(2);
        b.TabStop = false;
        string what = action;
        if (what.Length > 0) b.Click += delegate { Do(what); };
        new ToolTip().SetToolTip(b, tip);
        return b;
    }

    void Do(string action)
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            try { Fetch("do/" + action); } catch (Exception) { }
        });
    }

    string Fetch(string path)
    {
        HttpWebRequest r = (HttpWebRequest)WebRequest.Create(_base + path);
        r.Timeout = 700;
        r.ReadWriteTimeout = 700;
        r.Proxy = null;
        using (WebResponse w = r.GetResponse())
        using (StreamReader s = new StreamReader(w.GetResponseStream(), Encoding.UTF8))
            return s.ReadToEnd();
    }

    void Poll()
    {
        while (true)
        {
            try { _state = Fetch("state?" + (_showGems ? "gems=1&" : "") + (_wantCrafts ? "crafts=1" : "")); }
            catch (Exception) { _state = ""; }
            Thread.Sleep(_state.Length == 0 ? 1500 : 250);
        }
    }

    string KeyOf(string action)
    {
        string key;
        return _keys.TryGetValue(action, out key) && key.Length > 0 ? key : "the gems key";
    }

    static string Field(string json, string name)
    {
        Match m = Regex.Match(json, "\"" + name + "\":(\"((?:[^\"\\\\]|\\\\.)*)\"|-?[a-z0-9.]+)");
        if (!m.Success) return "";
        return m.Groups[2].Success ? Regex.Unescape(m.Groups[2].Value) : m.Groups[1].Value;
    }

    void Step()
    {
        IntPtr game = QolNative.FindWindow("POEWindowClass", null);
        bool focused = game != IntPtr.Zero && QolNative.GetForegroundWindow() == game && !QolNative.IsIconic(game);
        ReadKeys();
        string state = _state;
        bool connected = Field(state, "connected") == "true";
        if (!focused || _hidden || !connected)
        {
            if (Visible) Hide();
            ClearFloats();
            return;
        }
        QolNative.RECT rc;
        QolNative.GetClientRect(game, out rc);
        QolNative.POINT origin = new QolNative.POINT();
        QolNative.ClientToScreen(game, ref origin);
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        if (Left != origin.X || Top != origin.Y || Width != w || Height != h) SetBounds(origin.X, origin.Y, w, h);
        // The game's panels scale with the window height: at 1080 the stash and the inventory are each about 650 wide.
        double scale = h / 1080.0;
        int panel = (int)(652 * scale);
        _bar.Location = new Point(w / 2 - _bar.Width / 2, (int)(6 * scale));
        bool stash = Field(state, "stash") == "true";
        _stash.Visible = stash;
        // The stash buttons: in a row under the grid (the dark band above Highlight Items), or the old column beside the
        // stash, where the tab list's folders open over them.
        bool below = Field(state, "buttonsPlace") != "beside";
        FlowDirection flow = below ? FlowDirection.LeftToRight : FlowDirection.TopDown;
        if (_stash.FlowDirection != flow) _stash.FlowDirection = flow;
        int bx, by;
        if (!int.TryParse(Field(state, "buttonsX"), out bx)) bx = -1;
        if (!int.TryParse(Field(state, "buttonsY"), out by)) by = 852;
        if (below)
            _stash.Location = new Point(bx < 0 ? Math.Max(0, panel / 2 - _stash.Width / 2) : (int)(bx * scale), (int)(by * scale));
        else
            _stash.Location = new Point(bx < 0 ? panel + 6 : (int)(bx * scale), (int)(150 * scale));
        string tabType = Field(state, "tabType");
        _maps.Visible = tabType == "MapStash";
        _sortTab.Visible = tabType == "NormalStash" || tabType == "PremiumStash" || tabType == "QuadStash";
        _vendor.Visible = tabType == "NormalStash" || tabType == "PremiumStash" || tabType == "QuadStash";
        bool armed = Field(state, "vendorArmed") == "true";
        _vendor.Text = armed ? "Confirm: sell all?" : "Vendor tab";
        _vendor.BackColor = armed ? Color.FromArgb(120, 20, 15) : Color.FromArgb(28, 22, 16);
        bool prices = Field(state, "prices") == "true";
        _prices.Text = prices ? "Prices: on" : "Prices: off";
        string message = Field(state, "message");
        if (message.Length > 0 && message != _lastMessage) { _lastMessage = message; _messageUntil = DateTime.UtcNow.AddSeconds(5); _message.Text = message; }
        if (message.Length == 0) _lastMessage = "";
        _message.Visible = DateTime.UtcNow < _messageUntil;
        _message.Location = new Point(w / 2 - _message.Width / 2, _bar.Bottom + 4);
        bool harvest = Field(state, "harvest") == "true";
        if (harvest && _craftArgs.Count == 0)
        {
            _wantCrafts = true;
            string menu = Field(state, "crafts");
            if (menu.Length > 0)
            {
                _crafts.Items.Clear();
                foreach (string line in menu.Split('\n'))
                {
                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;
                    _craftArgs.Add(line.Substring(0, bar));
                    _crafts.Items.Add(line.Substring(bar + 1));
                }
                _wantCrafts = false;
            }
        }
        _harvest.Visible = harvest && _craftArgs.Count > 0;
        if (_harvest.Visible) _harvest.Location = new Point((int)(w * 0.36), (int)(h * 0.18));
        bool floating = Field(state, "pickupStyle") != "box";
        string pickups = floating ? "" : Field(state, "pickups");
        if (floating) Float(state, origin, w, h, scale);
        _pickups.Visible = pickups.Length > 0;
        if (pickups.Length > 0)
        {
            if (_pickups.Text != pickups) _pickups.Text = pickups;
            _pickups.Location = PickupSpot(state, w, h, scale, _pickups.Width, 0, false);
        }
        // Pop-ups from other mods (QoL's localexile.qol.toast): the text, and its sound once when a new one comes.
        string toast = Field(state, "toast");
        _toast.Visible = toast.Length > 0;
        if (toast.Length > 0)
        {
            if (_toast.Text != toast) _toast.Text = toast;
            _toast.Location = new Point(w / 2 - _toast.Width / 2, (int)(h * 0.30));
        }
        int toastId;
        if (int.TryParse(Field(state, "toastId"), out toastId) && toastId != _lastToastId)
        {
            string sound = Field(state, "toastSound");
            if (_lastToastId >= 0 && toast.Length > 0 && sound.Length > 0 && System.IO.File.Exists(sound))
            {
                try { new System.Media.SoundPlayer(sound).Play(); }
                catch (Exception) { }
            }
            _lastToastId = toastId;
        }
        _gems.Visible = _showGems;
        if (_showGems)
        {
            string text = "Alternate quality gems (" + KeyOf("gems") + " closes)\n\n" + Field(state, "gems").Replace("\n", "\n\n");
            if (_gems.Text != text) _gems.Text = text;
            _gems.Location = new Point(w / 2 - _gems.Width / 2, (int)(h * 0.22));
        }
        if (!Visible) Show();
    }

    // Each new price line (by its id) becomes a floating text beside the character; the ones showing rise and fade.
    void Float(string state, QolNative.POINT origin, int w, int h, double scale)
    {
        double seconds, rise, size;
        if (!double.TryParse(Field(state, "floatSeconds"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds) || seconds <= 0) seconds = 3;
        if (!double.TryParse(Field(state, "floatRise"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rise)) rise = 120;
        if (!double.TryParse(Field(state, "floatSize"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out size) || size < 6) size = 14;
        double hold;
        if (!double.TryParse(Field(state, "floatHold"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hold) || hold < 0) hold = 1;
        if (hold > seconds) hold = seconds;
        string[] lines = Field(state, "pickupList").Split('\n');
        // A new area (or a reload of the mod) numbers the lines from 1 again.
        int top = 0, parsed;
        foreach (string line in lines)
        {
            int cut = line.IndexOf('|');
            if (cut > 0 && int.TryParse(line.Substring(0, cut), out parsed) && parsed > top) top = parsed;
        }
        if (top > 0 && top < _lastPickupId) _lastPickupId = 0;
        int newest = _lastPickupId;
        foreach (string line in lines)
        {
            int bar = line.IndexOf('|'), id;
            if (bar <= 0 || !int.TryParse(line.Substring(0, bar), out id)) continue;
            if (id <= _lastPickupId) continue;
            if (id > newest) newest = id;
            QolFloatText text = new QolFloatText(line.Substring(bar + 1), (float)(size * Math.Max(0.6, scale)));
            // Lines that come together start one under the other instead of on top of each other.
            int young = 0;
            foreach (QolFloatText f in _floats)
                if ((DateTime.UtcNow - f.Born).TotalSeconds < 0.6) young++;
            // Where it starts: at the character's feet with a small random offset (the screen's centre, where the game keeps
            // the character), under the buff rows at the top left, or to the right of the character.
            Point at = PickupSpot(state, w, h, scale, text.Width, young * text.Height, true);
            text.StartX = origin.X + at.X;
            text.StartY = origin.Y + at.Y;
            text.Location = new Point(text.StartX, text.StartY);
            text.Show();
            _floats.Add(text);
        }
        _lastPickupId = newest;
        for (int i = _floats.Count - 1; i >= 0; i--)
        {
            QolFloatText f = _floats[i];
            double t = (DateTime.UtcNow - f.Born).TotalSeconds / seconds;
            if (t >= 1) { f.Close(); f.Dispose(); _floats.RemoveAt(i); continue; }
            f.Location = new Point(f.StartX, f.StartY - (int)(rise * scale * t));
            // Fully there for the hold time, then fading to nothing by the end.
            double age = (DateTime.UtcNow - f.Born).TotalSeconds, fade = Math.Max(0.05, seconds - hold);
            f.Opacity = age < hold ? 1.0 : Math.Max(0.0, 1.0 - (age - hold) / fade);
        }
    }

    readonly Random _scatter = new Random();

    Point PickupSpot(string state, int w, int h, double scale, int width, int below, bool scatter)
    {
        string place = Field(state, "pickupPlace");
        int jitter, feetX, feetY;
        if (!int.TryParse(Field(state, "pickupScatter"), out jitter) || jitter < 0) jitter = 40;
        if (!int.TryParse(Field(state, "feetX"), out feetX)) feetX = 0;
        if (!int.TryParse(Field(state, "feetY"), out feetY)) feetY = 0;
        if (place == "topleft")
            return new Point((int)(12 * scale), (int)(150 * scale) + below);
        if (place == "right")
            return new Point((int)(w * 0.55), (int)(h * 0.45) + below);
        int dx = scatter && jitter > 0 ? _scatter.Next(-jitter, jitter + 1) : 0;
        int dy = scatter && jitter > 0 ? _scatter.Next(-jitter / 3, jitter / 3 + 1) : 0;
        return new Point(w / 2 - width / 2 + (int)((dx + feetX) * scale), (int)(h * 0.5 + (dy + feetY) * scale) + below);
    }

    void ClearFloats()
    {
        foreach (QolFloatText f in _floats) { f.Close(); f.Dispose(); }
        _floats.Clear();
    }

    // Keybinds, read only while the game has focus: "F5", "Ctrl+Q", "Shift+Alt+L" (key names as Keys enum names).
    void ReadKeys()
    {
        IntPtr game = QolNative.FindWindow("POEWindowClass", null);
        bool focused = game != IntPtr.Zero && QolNative.GetForegroundWindow() == game;
        foreach (KeyValuePair<string, string> k in _keys)
        {
            bool now = focused && Pressed(k.Value);
            bool before;
            _down.TryGetValue(k.Key, out before);
            _down[k.Key] = now;
            if (!now || before) continue;
            if (k.Key == "hide") { _hidden = !_hidden; continue; }
            if (k.Key == "gems") { _showGems = !_showGems; continue; }
            Do(k.Key);
        }
    }

    static bool Down(int vk) { return (QolNative.GetAsyncKeyState(vk) & 0x8000) != 0; }

    static bool Pressed(string combo)
    {
        if (string.IsNullOrEmpty(combo)) return false;
        bool ctrl = false, shift = false, alt = false;
        int key = 0;
        foreach (string part in combo.Split('+'))
        {
            string p = part.Trim();
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) ctrl = true;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
            else
            {
                Keys parsed;
                if (!Enum.TryParse(p, true, out parsed)) return false;
                key = (int)parsed;
            }
        }
        if (key == 0 || !Down(key)) return false;
        return ctrl == Down(0x11) && shift == Down(0x10) && alt == Down(0x12);
    }

    public static void Run(int port, Dictionary<string, string> keys)
    {
        QolNative.SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.Run(new QolOverlay(port, keys));
    }
}
