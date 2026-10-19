using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static Vigil.UI.Kit;

namespace Vigil.UI;

/// <summary>
/// First launch (and "How Vigil works"): what it is in one sentence, three steps, one button.
/// Like Insomnia's page: "One eye. Click the eye. Hold × to end."
/// </summary>
public sealed class WelcomeWindow : Window
{
    private readonly App _app;

    public WelcomeWindow(App app)
    {
        _app = app;
        Title = "Welcome to Vigil";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.Res("WindowBg") is Brush b ? b : Brushes.Black;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        WindowStyling.Apply(this);
        MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch { } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Content = Build();
    }

    private UIElement Build()
    {
        var root = new StackPanel { Margin = new Thickness(34, 34, 34, 28) };

        root.Children.Add(new EyeView { Width = 64, Height = 64, Ink = Palette.Res("Text1"), HorizontalAlignment = HorizontalAlignment.Left });
        root.Children.Add(T("Vigil keeps your PC awake while Claude works.", "TitleXL", size: 28, margin: new Thickness(0, 18, 0, 0)));
        root.Children.Add(T("Then lets it sleep. There's nothing to turn on or off.", "Caption", size: 15, margin: new Thickness(0, 8, 0, 6)));

        root.Children.Add(Numbered("1", "Find the eye by the clock.", "Or press Ctrl+Alt+V from anywhere. If it's hidden, click ^ in the taskbar and drag the eye out."));
        root.Children.Add(Numbered("2", "Start Claude. The eye lights up.", "Your PC stays awake, even with the lid closed."));
        root.Children.Add(Numbered("3", "Come back to a summary.", "When Claude is done, your PC sleeps. Unlock to see what happened."));

        // The four looks of the eye
        var legend = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        legend.Children.Add(Look(TrayState.Idle, "Asleep\nwhen idle"));
        legend.Children.Add(Look(TrayState.Working, "Awake"));
        legend.Children.Add(Look(TrayState.NeedsYou, "Needs\nyou"));
        legend.Children.Add(Look(TrayState.Hot, "Too hot,\neasing off"));
        root.Children.Add(Card(legend, padding: new Thickness(10, 14, 10, 12), margin: new Thickness(0, 24, 0, 0)));

        // One button
        var connected = _app.HooksInstalled;
        var button = new Button
        {
            Content = connected ? "Got it" : "Connect to Claude Code",
            Style = S("PrimaryButton"),
            Padding = new Thickness(22, 11, 22, 11),
            FontSize = 14,
            Margin = new Thickness(0, 26, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsDefault = true,
        };
        button.Click += (_, _) =>
        {
            if (!connected)
            {
                try { _app.Connect(); }
                catch (Exception ex)
                {
                    MessageBox.Show($"Couldn't update Claude's settings:\n\n{ex.Message}", "Vigil");
                    return;
                }
            }
            Close();
        };
        root.Children.Add(button);
        root.Children.Add(T(connected
                ? "On your phone, open the Claude app and tap Code to see your sessions."
                : "Adds small hooks to Claude's settings so Vigil knows when it's working. Backed up first. Undo any time in Settings.",
            "Caption", size: 12, color: Palette.Res("Text3"), margin: new Thickness(0, 10, 0, 0)));
        if (connected) ((TextBlock)root.Children[^1]).TextAlignment = TextAlignment.Center;
        else ((TextBlock)root.Children[^1]).TextAlignment = TextAlignment.Center;

        return root;
    }

    /// <summary>A plain numbered step: "1." in the margin, a title and one line.</summary>
    private static UIElement Numbered(string n, string title, string line)
    {
        var g = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(T(n + ".", "BodyStrong", size: 15, color: Palette.Res("Text2")));
        var text = new StackPanel();
        text.Children.Add(T(title, "BodyStrong", size: 15));
        text.Children.Add(T(line, "Caption", size: 13, margin: new Thickness(0, 2, 0, 0)));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        return g;
    }

    private static UIElement Look(TrayState state, string label)
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        // The four looks, live: they blink along with the real one.
        p.Children.Add(new EyeView
        {
            Width = 28, Height = 28, Stroke = 9,
            Ink = Palette.Res(state == TrayState.Idle ? "Text2" : "Text1"),
            Filled = state != TrayState.Idle,
            Badge = state switch
            {
                TrayState.NeedsYou => new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)),
                TrayState.Hot => new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
                _ => null,
            },
        });
        var t = T(label, "Caption", size: 12, margin: new Thickness(0, 6, 0, 0));
        t.TextAlignment = TextAlignment.Center;
        p.Children.Add(t);
        return p;
    }
}

/// <summary>A small window that just holds the settings.</summary>
public sealed class SettingsWindow : Window
{
    private readonly App _app;
    public SettingsView View { get; } = new() { Margin = new Thickness(30, 4, 30, 28) };

    public SettingsWindow(App app)
    {
        _app = app;
        Title = "Vigil settings";
        Width = 560;
        Height = 720;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.Res("WindowBg");
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        WindowStyling.Apply(this);
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false,
        });
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var close = new Button { Tag = "CaptionButtons", Style = S("CloseButton"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 8, 0) };
        close.Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, Foreground = Palette.Res("Text1") };
        close.Click += (_, _) => Close();
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close, true);

        var grid = new Grid();
        grid.Children.Add(new ScrollViewer { Content = View, Margin = new Thickness(0, 40, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        grid.Children.Add(close);
        Content = grid;
        View.Attach(app);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_app.Quitting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
