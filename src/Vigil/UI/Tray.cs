using System.Drawing;
using System.Windows.Forms;
using Vigil.Core;

namespace Vigil.UI;

/// <summary>The eye in the taskbar. Left-click: the glance. Right-click: quick actions.</summary>
internal sealed class Tray : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly App _app;
    private TrayState _state = (TrayState)(-1);
    private int _px = 16;
    private EyePose _pose = EyePose.Rest;

    public Tray(App app)
    {
        _app = app;
        _icon = new NotifyIcon { Visible = true, Text = "Vigil" };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _app.TogglePopover();
        };
        _icon.BalloonTipClicked += (_, _) => _app.TogglePopover();
        _icon.ContextMenuStrip = BuildMenu();
        Update();
        // The tray eye blinks and glances along with every other eye.
        EyeMotion.Moved += OnEyeMoved;
        EyeMotion.Wake();
    }

    private void OnEyeMoved()
    {
        var pose = EyeMotion.Pose.Quantized();
        if (pose == _pose || (int)_state < 0) return;
        _pose = pose;
        _icon.Icon = IconFactory.Tray(_state, _px, pose);
    }

    private static int IconPx => Math.Max(16, (int)Math.Round(16 * DpiScale()));

    private static double DpiScale()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96.0;
    }

    public void Update()
    {
        var status = Status.Of(_app);
        if (status.Icon != _state)
        {
            _state = status.Icon;
            _px = IconPx;
            _icon.Icon = IconFactory.Tray(status.Icon, _px, _pose);
        }
        // Same words as the card. Tooltips max out at 127 characters.
        var tip = $"Vigil: {status.Title}. {status.Line} (Ctrl+Alt+V)";
        _icon.Text = tip.Length > 127 ? tip[..126] + "…" : tip;
    }

    public void Toast(string title, string message, ToolTipIcon icon = ToolTipIcon.None)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = string.IsNullOrWhiteSpace(message) ? " " : message;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(5000);
    }

    /// <summary>Insomnia's order: status first, then the few things you'd want, then Quit.</summary>
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkRenderer(),
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(4),
        };
        var title = new ToolStripMenuItem("…") { Enabled = false, Font = new Font("Segoe UI Semibold", 9.5f) };
        var line = new ToolStripMenuItem("…") { Enabled = false };
        var facts = new ToolStripMenuItem("…") { Enabled = false };
        menu.Items.Add(title);
        menu.Items.Add(line);
        menu.Items.Add(facts);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("What happened", () => _app.ShowHappened(null)));
        menu.Items.Add(Item("Welcome back (preview)", () => _app.ShowWelcomeBackExample()));
        menu.Items.Add(Item("How Vigil works", () => _app.ShowWelcome()));
        var phone = Item("Connect my iPhone…", () => _app.ShowPhonePairing());
        menu.Items.Add(phone);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Settings…", () => _app.ShowSettings()));
        menu.Items.Add(Item("Quit Vigil", () => _app.Quit()));

        menu.Opening += (_, _) =>
        {
            var st = Status.Of(_app);
            title.Text = st.Title;
            line.Text = st.Line.Length > 70 ? st.Line[..69] + "…" : st.Line;
            facts.Text = Status.Facts(_app.Engine);
            phone.Visible = !_app.Phone.Enabled;
        };
        return menu;
    }

    private static ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { Log.Error($"Menu '{text}' failed", ex); }
        };
        return item;
    }

    public void Dispose()
    {
        EyeMotion.Moved -= OnEyeMoved;
        _icon.Visible = false;
        _icon.Dispose();
    }

    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public static readonly Color Bg = Color.FromArgb(32, 32, 36);
        private static readonly Color Hover = Color.FromArgb(52, 52, 58);
        private static readonly Color Line = Color.FromArgb(55, 55, 60);

        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = true; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Color.FromArgb(245, 245, 247) : Color.FromArgb(161, 161, 166);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(161, 161, 166);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            using var b = new SolidBrush(Hover);
            var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
            e.Graphics.FillRectangle(b, r);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var p = new Pen(Line);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Bg;
            public override Color MenuBorder => Line;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => Hover;
            public override Color ImageMarginGradientBegin => Bg;
            public override Color ImageMarginGradientMiddle => Bg;
            public override Color ImageMarginGradientEnd => Bg;
            public override Color SeparatorDark => Line;
        }
    }
}

/// <summary>A hidden window that receives lid open/close notifications from Windows.</summary>
internal sealed class PowerEvents : IDisposable
{
    private readonly System.Windows.Interop.HwndSource _source;
    private readonly IntPtr _lidHandle;
    private readonly IntPtr _displayHandle;
    private readonly Action<bool, bool> _onLid;
    private readonly Action<bool>? _onDisplay;
    private readonly Action _onClose;
    private readonly Action? _onHotkey;
    private bool _firstLid = true;
    private const int HotkeyId = 0x5649; // "VI"

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    /// <summary>True if Ctrl+Alt+V was free and is now Vigil's.</summary>
    public bool HotkeyRegistered { get; }

    public PowerEvents(Action<bool, bool> onLid, Action onClose, Action? onHotkey = null, Action<bool>? onDisplay = null)
    {
        _onLid = onLid;
        _onDisplay = onDisplay;
        _onClose = onClose;
        _onHotkey = onHotkey;
        var p = new System.Windows.Interop.HwndSourceParameters("VigilPowerEvents") { Width = 0, Height = 0, WindowStyle = 0 };
        _source = new System.Windows.Interop.HwndSource(p);
        _source.AddHook(WndProc);
        var guid = Native.GUID_LIDSWITCH_STATE_CHANGE;
        _lidHandle = Native.RegisterPowerSettingNotification(_source.Handle, ref guid, Native.DEVICE_NOTIFY_WINDOW_HANDLE);
        // Screen on or off. On a Modern Standby laptop, off (with nothing holding it awake) is asleep.
        var display = Native.GUID_CONSOLE_DISPLAY_STATE;
        if (onDisplay is not null) _displayHandle = Native.RegisterPowerSettingNotification(_source.Handle, ref display, Native.DEVICE_NOTIFY_WINDOW_HANDLE);
        // Ctrl+Alt+V opens Vigil's card from anywhere. MOD_ALT=1, MOD_CONTROL=2, MOD_NOREPEAT=0x4000.
        if (onHotkey is not null) HotkeyRegistered = RegisterHotKey(_source.Handle, HotkeyId, 0x1 | 0x2 | 0x4000, 0x56);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == HotkeyId) // WM_HOTKEY
        {
            handled = true;
            _onHotkey?.Invoke();
            return IntPtr.Zero;
        }
        // WM_CLOSE (e.g. "taskkill /IM Vigil.exe" or an updater): quit cleanly and restore settings.
        if (msg == 0x0010)
        {
            handled = true;
            _onClose();
            return IntPtr.Zero;
        }
        if (msg == Native.WM_POWERBROADCAST && wParam.ToInt32() == Native.PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
        {
            var setting = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.POWERBROADCAST_SETTING>(lParam);
            if (setting.PowerSetting == Native.GUID_LIDSWITCH_STATE_CHANGE)
            {
                // Windows sends the current state right after registering; that's not a real change.
                _onLid(setting.Data != 0, _firstLid);
                _firstLid = false;
            }
            else if (setting.PowerSetting == Native.GUID_CONSOLE_DISPLAY_STATE)
            {
                // 0 = off, 1 = on, 2 = dimmed (still on).
                _onDisplay?.Invoke(setting.Data != 0);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_lidHandle != IntPtr.Zero) Native.UnregisterPowerSettingNotification(_lidHandle);
        if (_displayHandle != IntPtr.Zero) Native.UnregisterPowerSettingNotification(_displayHandle);
        if (HotkeyRegistered) UnregisterHotKey(_source.Handle, HotkeyId);
        _source.Dispose();
    }
}
