using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;
using static Vigil.UI.Kit;

namespace Vigil.UI;

/// <summary>Scan, add to Home Screen, turn on notifications. Watches for the phone and says when it's done.</summary>
public sealed class PhonePairWindow : Window
{
    private readonly App _app;
    private readonly DispatcherTimer _poll;
    private readonly TextBlock _status = T("Waiting for your iPhone…", "Caption", size: 13.5);
    private readonly System.Windows.Shapes.Ellipse _dot = new() { Width = 8, Height = 8, Fill = Palette.Res("Orange"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _done;

    public PhonePairWindow(App app)
    {
        _app = app;
        Title = "Connect your iPhone";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.Res("WindowBg");
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        WindowStyling.Apply(this);
        MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch { } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        _done = Button("Done", Close, primary: true);
        Content = Build(app.Phone.PairingLink());

        _poll = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, async (_, _) => await Check(), Dispatcher);
        Loaded += async (_, _) => { await Check(); _poll.Start(); };
        Closed += (_, _) => _poll.Stop();
    }

    private UIElement Build(string link)
    {
        var root = new StackPanel { Margin = new Thickness(32, 30, 32, 26) };
        root.Children.Add(T("Put Vigil on your iPhone", "TitleXL", size: 26));
        root.Children.Add(T("See what Claude is doing and get a tap when it needs you or finishes. Only while you're away from your PC.",
            "Caption", size: 14, margin: new Thickness(0, 6, 0, 0)));

        var row = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        var qr = new Border
        {
            Width = 180, Height = 180, CornerRadius = new CornerRadius(18), Background = Brushes.White, Padding = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new Image { Source = QrImage(link), Stretch = Stretch.Uniform },
        };
        RenderOptions.SetBitmapScalingMode(qr.Child, BitmapScalingMode.NearestNeighbor);
        row.Children.Add(qr);

        var steps = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        steps.Children.Add(Small("1", "Point your iPhone camera here", "and tap the link that appears."));
        steps.Children.Add(Small("2", "Tap Share, then “Add to Home Screen”", "iPhone only sends notifications to Home Screen apps."));
        steps.Children.Add(Small("3", "Open Vigil from your Home Screen", "and tap “Turn on notifications”."));
        Grid.SetColumn(steps, 1);
        row.Children.Add(steps);
        root.Children.Add(row);

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
        statusRow.Children.Add(_dot);
        _status.Margin = new Thickness(9, 0, 0, 0);
        statusRow.Children.Add(_status);

        var bottom = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        bottom.Children.Add(statusRow);
        _done.HorizontalAlignment = HorizontalAlignment.Right;
        _done.Margin = new Thickness(0, 14, 0, 0);
        bottom.Children.Add(_done);
        root.Children.Add(bottom);

        root.Children.Add(T("The code holds a private key just for your phone. Don't share a photo of it. You can disconnect any time in Settings.",
            "Caption", size: 11.5, color: Palette.Res("Text3"), margin: new Thickness(0, 14, 0, 0)));
        return root;
    }

    private async Task Check()
    {
        if (await _app.Phone.CheckPairedAsync())
        {
            _poll.Stop();
            _dot.Fill = Palette.Res("Green");
            _status.Text = "Connected. Your iPhone will be notified while you're away.";
            _status.Foreground = Palette.Res("Text1");
            _app.Phone.Changed();
        }
    }

    /// <summary>A plain numbered step: "1." in the margin, a title and one line.</summary>
    private static UIElement Small(string n, string title, string line)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(T(n + ".", "BodyStrong", color: Palette.Res("Text2")));
        var text = new StackPanel();
        text.Children.Add(T(title, "BodyStrong", size: 14));
        text.Children.Add(T(line, "Caption", size: 12.5, margin: new Thickness(0, 1, 0, 0)));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        return g;
    }

    private static BitmapImage QrImage(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10, new byte[] { 0x15, 0x15, 0x18 }, new byte[] { 0xFF, 0xFF, 0xFF }, drawQuietZones: false);
        var img = new BitmapImage();
        using var ms = new MemoryStream(png);
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
