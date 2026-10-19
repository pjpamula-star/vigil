using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vigil.Core;

namespace Vigil.UI;

/// <summary>Settings, grouped the way you think about them. Every switch takes effect immediately.</summary>
public sealed class SettingsView : StackPanel
{
    private App? _app;
    private bool _loading;
    private Panel? _targetPanel;

    /// <summary>Where the builders add rows: the main list, or the folded "More options" list.</summary>
    private Panel _target { get => _targetPanel ?? this; set => _targetPanel = value; }

    private readonly TextBlock _connTitle = new();
    private readonly TextBlock _connDetail = new();
    private readonly Button _connButton = new();
    private readonly Border _connCard = new();
    private readonly TextBlock _phoneTitle = new();
    private readonly TextBlock _phoneDetail = new();
    private readonly Button _phoneButton = new();
    private readonly Button _phoneTest = new();
    private CheckBox _phoneAlways = null!;
    private CheckBox _phoneSleep = null!;
    private CheckBox _eyeAlive = null!;

    private CheckBox _sleepWhenDone = null!;
    private CheckBox _phone = null!, _quietPhone = null!, _lockLid = null!, _heat = null!, _ai = null!, _welcome = null!, _startup = null!;
    private Segmented _waitYou = null!, _usage = null!, _easeBattery = null!, _hibBattery = null!;
    private CheckBox _backupWifi = null!, _deskPill = null!, _codex = null!;
    private TextBlock _backupWifiNote = null!, _codexNote = null!;
    private readonly TextBox _ntfy = new();

    public void Attach(App app)
    {
        _app = app;
        Build();
    }

    private AppSettings S => _app!.Engine.Settings;

    private void Build()
    {
        Children.Clear();
        Children.Add(new TextBlock { Text = "Settings", Style = Res("TitleXL"), Margin = new Thickness(0, 0, 0, 18) });

        // ---- Connection ----
        _connCard.CornerRadius = new CornerRadius(16);
        _connCard.Padding = new Thickness(18, 16, 18, 16);
        _connTitle.Style = Res("TitleL");
        _connDetail.Style = Res("Caption");
        _connDetail.FontSize = 13;
        _connDetail.Margin = new Thickness(0, 4, 0, 12);
        _connButton.Click += (_, _) => ToggleConnection();
        _connButton.HorizontalAlignment = HorizontalAlignment.Left;
        _connCard.Child = new StackPanel { Children = { _connTitle, _connDetail, _connButton } };
        _connCard.Margin = new Thickness(0, 0, 0, 10);
        Children.Add(_connCard);

        // ---- Vigil on my iPhone ----
        _phoneTitle.Style = Res("BodyStrong");
        _phoneDetail.Style = Res("Caption");
        _phoneDetail.Margin = new Thickness(0, 3, 0, 0);
        _phoneButton.Click += async (_, _) =>
        {
            if (_app!.Phone.Enabled)
            {
                if (MessageBox.Show("Disconnect your iPhone? It will stop getting Vigil notifications.", "Vigil", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                await _app.Phone.DisconnectAsync();
                Refresh();
            }
            else _app.ShowPhonePairing();
        };
        var phoneGrid = new Grid { Margin = new Thickness(0, 10, 0, 6) };
        phoneGrid.ColumnDefinitions.Add(new ColumnDefinition());
        phoneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var phoneText = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        phoneText.Children.Add(_phoneTitle);
        phoneText.Children.Add(_phoneDetail);
        phoneGrid.Children.Add(phoneText);
        _phoneButton.VerticalAlignment = VerticalAlignment.Center;
        _phoneTest.Content = "Send a test";
        _phoneTest.Style = Res("SecondaryButton");
        _phoneTest.Margin = new Thickness(0, 0, 8, 0);
        _phoneTest.Click += async (_, _) =>
        {
            _phoneTest.IsEnabled = false;
            await _app!.Phone.SendTestAsync();
            _phoneTest.Content = "Sent";
            _phoneTest.IsEnabled = true;
        };
        var phoneButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _phoneTest, _phoneButton } };
        Grid.SetColumn(phoneButtons, 1);
        phoneGrid.Children.Add(phoneButtons);
        Children.Add(phoneGrid);
        Children.Add(new Border { Height = 1, Background = Palette.Res("Hairline"), Margin = new Thickness(0, 6, 0, 0) });
        _phoneAlways = Switch("Notify my iPhone even when I'm at my PC",
            "Off: your iPhone only buzzes while you're away (PC locked or lid closed).",
            on => S.PhoneOnlyWhenAway = !on);
        _phoneSleep = Switch("Tell my iPhone when my PC goes to sleep",
            "After Claude finishes, so you know it isn't still running.",
            on => S.PhoneNotifySleep = on);

        // ---- The five that matter ----
        _phone = Switch("Reply from my phone in the Claude app",
            "In the Claude app, open Code to read and answer your terminal sessions.",
            on => _app!.Engine.Claude.SetRemoteControlAtStartup(on));
        _lockLid = Switch("Lock my PC when I close the lid",
            "Because Vigil keeps it awake, it locks it for you instead.",
            on => S.LockOnLidClose = on);
        _heat = Switch("Protect my PC from heat and low battery",
            "Slows down first. If that's not enough, it hibernates. Nothing is lost.",
            on => S.HeatProtection = on);
        _ai = Switch("Write short summaries with Claude",
            "One line per finished task, in about 3 seconds. Uses your Claude plan.",
            on => S.AiHeadlines = on);
        _startup = Switch("Start Vigil when I sign in", null, on => Startup.Set(on, Environment.ProcessPath!));

        // ---- Everything else, folded away ----
        var more = new StackPanel { Visibility = Visibility.Collapsed };
        var toggle = new Button { Style = Res("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 0) };
        var toggleText = new TextBlock { Text = "More options", Foreground = Palette.Res("Blue"), FontSize = 13 };
        toggle.Content = toggleText;
        toggle.Click += (_, _) =>
        {
            more.Visibility = more.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            toggleText.Text = more.Visibility == Visibility.Visible ? "Fewer options" : "More options";
        };
        Children.Add(toggle);
        Children.Add(more);
        _target = more;

        Section("Waiting");
        _waitYou = Choice("When Claude is waiting on you, stay awake for",
            "So you can answer from your phone. After that, your PC sleeps.",
            new[] { ("Don't wait", 0), ("15 min", 15), ("30 min", 30), ("1 hour", 60), ("2 hours", 120) },
            v => S.WaitForYouMinutes = v);
        _usage = Choice("When Claude hits a usage limit, stay awake for",
            "Claude continues by itself when the limit resets, but only if your PC is awake.",
            new[] { ("Don't wait", 0), ("2 hours", 2), ("5 hours", 5), ("8 hours", 8) },
            v => S.UsageLimitWaitHours = v);

        _sleepWhenDone = Switch("When it's all done and the lid is closed, sleep right away",
            "Instead of staying on until the idle timer. On battery it hibernates, so it won't drain or get warm in your bag.",
            on => S.SleepWhenDoneLidClosed = on);
        _eyeAlive = Switch("Let the eye blink and look around",
            "Now and then it blinks, glances about, and watches your mouse go by.",
            on =>
            {
                S.EyeAlive = on;
                EyeMotion.Enabled = on;
            });

        Section("Battery");
        _easeBattery = Choice("On battery, slow down at", null, new[] { ("30%", 30), ("25%", 25), ("20%", 20), ("15%", 15) }, v => S.EaseOffBatteryPercent = v);
        _hibBattery = Choice("Hibernate at", null, new[] { ("15%", 15), ("10%", 10), ("8%", 8), ("5%", 5) }, v => S.HibernateBatteryPercent = v);

        Section("Desktop");
        _deskPill = Switch("Show a status pill in my taskbar",
            "Sits in the taskbar's empty space: what needs you, what's working, what's done. Hover for details, drag it anywhere, right-click to put it back.",
            on => _app!.SetDeskPill(on));

        Section("Codex");
        _codex = Switch("Also follow my Codex sessions",
            null, on =>
            {
                var codex = _app!.Engine.Codex;
                if (!on) { codex.Uninstall(); return; }
                if (!codex.Available) throw new InvalidOperationException("Codex isn't set up on this PC yet. Install OpenAI's Codex CLI, run it once, then turn this on.");
                codex.Install(_app.Engine.Port);
            });
        _codexNote = new TextBlock { Style = Res("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -2, 0, 6) };
        _target.Children.Insert(_target.Children.Count - 1, _codexNote);

        Section("Wi-Fi");
        _backupWifi = Switch("If the internet drops while Claude works, join my phone's hotspot",
            null, on => S.BackupWifi = on);
        _backupWifiNote = new TextBlock { Style = Res("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -2, 0, 6) };
        // Under the switch's own text, above its hairline.
        _target.Children.Insert(_target.Children.Count - 1, _backupWifiNote);

        Section("Phone");
        _quietPhone = Switch("Quiet my phone while I'm at the PC",
            "Claude only pushes to your phone while you're away (locked or lid closed).",
            on => _app!.Engine.Claude.SetPresenceFile(on ? Paths.PresenceFile : null));
        var ntfyRow = new StackPanel { Margin = new Thickness(0, 10, 0, 4) };
        ntfyRow.Children.Add(new TextBlock { Text = "Short notes from Vigil (optional)", Style = Res("Body") });
        ntfyRow.Children.Add(new TextBlock
        {
            Text = "One-line updates through the free ntfy app. Pick a hard-to-guess topic: anyone who knows it can read the notes.",
            Style = Res("Caption"), Margin = new Thickness(0, 3, 0, 8),
        });
        _ntfy.Style = Res("Field");
        _ntfy.Width = 240;
        _ntfy.VerticalAlignment = VerticalAlignment.Center;
        _ntfy.Margin = new Thickness(0, 0, 8, 8);
        _ntfy.LostFocus += (_, _) => { if (!_loading) { S.NtfyTopic = _ntfy.Text.Trim(); S.Save(); } };
        var gen = SmallButton("Make one", () =>
        {
            _ntfy.Text = "vigil-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            S.NtfyTopic = _ntfy.Text;
            S.Save();
        });
        var test = SmallButton("Send test", () =>
        {
            S.NtfyTopic = _ntfy.Text.Trim();
            S.Save();
            _ = PhoneNotes.SendAsync(S.NtfyTopic, "Vigil is connected", "You'll get a short note here when Claude finishes or needs you.");
        });
        ntfyRow.Children.Add(new WrapPanel { Children = { _ntfy, gen, test } });
        Add(ntfyRow);

        Section("Other");
        _welcome = Switch("Show what happened when I unlock",
            "Only when something happened while you were gone.",
            on => S.ShowWelcomeBack = on);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(SmallButton("Put my sleep settings back now", () =>
        {
            _app!.Engine.StopNow();
            var restored = _app.Engine.Power.Restore();
            _app.Toast("Sleep settings restored", restored ? "Your original power settings are back." : "Nothing to restore. Your settings were already normal.");
        }));
        actions.Children.Add(SmallButton("Preview the Welcome back screen", () => _app!.ShowWelcomeBackExample()));
        actions.Children.Add(SmallButton("Open log file", () => Process.Start(new ProcessStartInfo(Paths.Log) { UseShellExecute = true })));
        Add(actions);
        Add(new TextBlock
        {
            Text = $"Vigil {typeof(App).Assembly.GetName().Version?.ToString(3)}. Your power settings are saved before every change and put back afterwards.",
            Style = Res("Caption"), FontSize = 12, Margin = new Thickness(0, 14, 0, 0),
        });
        _target = this;

        Refresh();
    }

    public void Refresh()
    {
        if (_app is null) return;
        _loading = true;
        try
        {
            var installed = _app.HooksInstalled;
            if (installed)
            {
                _connCard.Background = Palette.Res("Card");
                _connTitle.Text = "Connected to Claude Code";
                var heard = _app.Engine.Tracker.Snapshot().Count;
                _connDetail.Text = heard > 0
                    ? $"Vigil is following {heard} Claude session{(heard == 1 ? "" : "s")}."
                    : "Start Claude in a terminal and Vigil takes it from there.";
                _connButton.Content = "Disconnect";
                _connButton.Style = Res("SecondaryButton");
            }
            else
            {
                _connCard.Background = Palette.Res("Card");
                _connTitle.Text = "Connect Vigil to Claude Code";
                _connDetail.Text = "Vigil needs this to know when Claude is working. It adds small hooks to Claude's settings, backed up first. Disconnect removes them.";
                _connButton.Content = "Connect";
                _connButton.Style = Res("PrimaryButton");
            }

            var paired = _app.Phone.Enabled;
            _phoneTitle.Text = "Vigil on my iPhone";
            _phoneDetail.Text = paired
                ? "Your iPhone shows what Claude is doing and buzzes when it needs you or finishes."
                : "See what Claude is doing and get a tap when it needs you or finishes. Takes a minute.";
            _phoneButton.Content = paired ? "Disconnect" : "Connect";
            _phoneTest.Visibility = paired ? Visibility.Visible : Visibility.Collapsed;
            Show(_phoneAlways, paired);
            _phoneAlways.IsChecked = !S.PhoneOnlyWhenAway;
            Show(_phoneSleep, paired);
            _phoneSleep.IsChecked = S.PhoneNotifySleep;
            _phoneButton.Style = Res(paired ? "SecondaryButton" : "PrimaryButton");

            var codex = _app.Engine.Codex;
            var codexOn = SafeGet(codex.IsInstalled);
            _codex.IsChecked = codexOn;
            _codexNote.Text = CodexText(codex.Available, codexOn, S.CodexSeen);

            var claude = _app.Engine.Claude;
            _phone.IsChecked = SafeGet(() => claude.RemoteControlAtStartup);
            _quietPhone.IsChecked = SafeGet(() => claude.PresenceFile is not null);
            _ntfy.Text = S.NtfyTopic;
            Select(_waitYou, S.WaitForYouMinutes);
            Select(_usage, S.UsageLimitWaitHours);
            _lockLid.IsChecked = S.LockOnLidClose;
            _sleepWhenDone.IsChecked = S.SleepWhenDoneLidClosed;
            _eyeAlive.IsChecked = S.EyeAlive;
            _heat.IsChecked = S.HeatProtection;
            Select(_easeBattery, S.EaseOffBatteryPercent);
            Select(_hibBattery, S.HibernateBatteryPercent);
            _ai.IsChecked = S.AiHeadlines;
            _welcome.IsChecked = S.ShowWelcomeBack;
            _backupWifi.IsChecked = S.BackupWifi;
            _deskPill.IsChecked = S.DeskPill;
            _backupWifiNote.Text = BackupWifiText();
            _startup.IsChecked = Startup.IsEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>How it works, in plain words. Reads nothing from Wi-Fi: opening Settings never touches the network.</summary>
    private static string BackupWifiText() =>
        "Uses a saved Wi-Fi network named like a phone (for example “Sam’s iPhone”), and goes back to your Wi-Fi when Claude is done. " +
        "Join your hotspot once from the Wi-Fi menu so Windows remembers it, and on iPhone keep Personal Hotspot › Allow Others to Join on. " +
        "Vigil never scans for nearby networks, so it never needs your location.";

    private void ToggleConnection()
    {
        try
        {
            if (_app!.HooksInstalled) _app.Disconnect();
            else _app.Connect();
        }
        catch (Exception ex)
        {
            Log.Error("Connection change failed", ex);
            MessageBox.Show($"Couldn't update Claude's settings:\n\n{ex.Message}", "Vigil", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Refresh();
    }

    /// <summary>What the Codex switch needs from you, if anything.</summary>
    internal static string CodexText(bool available, bool on, bool seen) =>
        !available && !on ? "Install OpenAI's Codex CLI and run it once, then turn this on."
        : !on ? "Shows Codex sessions next to Claude's and keeps the PC awake for them too. Adds hooks to Codex's settings, backed up first."
        : seen ? "Codex sessions show up next to Claude's, marked Codex."
        : "One more step: in Codex, type /hooks and approve Vigil's hooks. Codex only runs hooks you've approved.";

    // ---------- builders ----------

    private void Section(string title) =>
        _target.Children.Add(new TextBlock { Text = title, Style = Res("Label"), FontSize = 12, Margin = new Thickness(0, 24, 0, 2) });

    private void Add(UIElement e) => _target.Children.Add(e);

    /// <summary>Shows or hides a switch together with its divider.</summary>
    private static void Show(CheckBox cb, bool shown)
    {
        var v = shown ? Visibility.Visible : Visibility.Collapsed;
        cb.Visibility = v;
        if (cb.Tag is UIElement line) line.Visibility = v;
    }

    private CheckBox Switch(string title, string? caption, Action<bool> onChange)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, Style = Res("Body") });
        if (caption is not null)
            text.Children.Add(new TextBlock { Text = caption, Style = Res("Caption"), Margin = new Thickness(0, 3, 0, 0) });
        var cb = new CheckBox { Content = text, Style = Res("Switch"), Margin = new Thickness(0, 10, 0, 6) };
        cb.Click += (_, _) =>
        {
            if (_loading) return;
            try
            {
                onChange(cb.IsChecked == true);
                S.Save();
                _app!.Engine.ApplySettings();
            }
            catch (Exception ex)
            {
                Log.Error($"Setting '{title}' failed", ex);
                MessageBox.Show(ex.Message, "Vigil", MessageBoxButton.OK, MessageBoxImage.Warning);
                Refresh();
            }
        };
        var line = new Border { Height = 1, Background = Palette.Res("Hairline"), Margin = new Thickness(0, 6, 0, 0) };
        _target.Children.Add(cb);
        _target.Children.Add(line);
        cb.Tag = line; // hidden together, so a hidden switch doesn't leave its divider behind
        return cb;
    }

    private Segmented Choice(string title, string? caption, (string label, int value)[] options, Action<int> onChange)
    {
        var text = new StackPanel { Margin = new Thickness(0, 10, 0, 8) };
        text.Children.Add(new TextBlock { Text = title, Style = Res("Body") });
        if (caption is not null)
            text.Children.Add(new TextBlock { Text = caption, Style = Res("Caption"), Margin = new Thickness(0, 3, 0, 0) });
        var seg = new Segmented(options, v =>
        {
            if (_loading) return;
            onChange(v);
            S.Save();
            _app!.Engine.ApplySettings();
        });
        _target.Children.Add(text);
        _target.Children.Add(seg);
        _target.Children.Add(new Border { Height = 1, Background = Palette.Res("Hairline"), Margin = new Thickness(0, 12, 0, 0) });
        return seg;
    }

    private static void Select(Segmented seg, int value) => seg.Select(value);

    private Button SmallButton(string text, Action onClick)
    {
        var b = new Button { Content = text, Style = Res("SecondaryButton"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 7, 14, 7) };
        b.Click += (_, _) =>
        {
            try { onClick(); }
            catch (Exception ex) { Log.Warn($"{text}: {ex.Message}"); }
        };
        return b;
    }

    private static bool SafeGet(Func<bool> f)
    {
        try { return f(); }
        catch { return false; }
    }

    private static Style Res(string key) => (Style)Application.Current.Resources[key];
}

/// <summary>An iOS-style segmented control.</summary>
public sealed class Segmented : Border
{
    private static int _groups;
    private readonly List<(RadioButton button, int value)> _items = new();
    private bool _selecting;

    public Segmented((string label, int value)[] options, Action<int> onChange)
    {
        Background = Palette.Res("Control");
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(3);
        HorizontalAlignment = HorizontalAlignment.Left;
        var group = $"seg{Interlocked.Increment(ref _groups)}";
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, value) in options)
        {
            var rb = new RadioButton
            {
                Content = label, GroupName = group, MinWidth = 64,
                Style = (Style)Application.Current.Resources["Segment"],
            };
            rb.Checked += (_, _) => { if (!_selecting) onChange(value); };
            panel.Children.Add(rb);
            _items.Add((rb, value));
        }
        Child = panel;
    }

    public void Select(int value)
    {
        var best = _items.OrderBy(i => Math.Abs(i.value - value)).First();
        // Setting the selection from code is not the user picking: don't report it as a change.
        // (Reporting it made "What happened" re-render, re-select and re-render until the stack ran out.)
        _selecting = true;
        try { best.button.IsChecked = true; }
        finally { _selecting = false; }
    }
}
