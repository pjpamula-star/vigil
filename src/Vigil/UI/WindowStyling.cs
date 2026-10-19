using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Vigil.Core;

namespace Vigil.UI;

/// <summary>Which Windows 11 material sits behind a window.</summary>
internal enum Backdrop
{
    /// <summary>Solid color.</summary>
    None,
    /// <summary>Mica: the desktop wallpaper, heavily blurred and tinted. For app windows.</summary>
    Mica,
    /// <summary>Acrylic: see-through frosted glass. For flyouts and short-lived windows, like Quick Settings.</summary>
    Acrylic,
}

/// <summary>Windows 11 niceties: dark title bar, rounded corners, a quiet border, and the system material.</summary>
internal static class WindowStyling
{
    /// <summary>Off for screenshots, which can't capture the material.</summary>
    public static bool MaterialsAllowed { get; set; } = true;

    /// <summary>Windows 11 22H2 (build 22621) is the first with the documented backdrop attribute.</summary>
    public static bool MaterialsSupported => MaterialsAllowed && Environment.OSVersion.Version.Build >= 22621 && !SystemParameters.HighContrast && TransparencyOn();

    public static void Apply(Window window, bool rounded = true, Backdrop backdrop = Backdrop.Mica)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var dark = 1;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            if (rounded)
            {
                var corner = Native.DWMWCP_ROUND;
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }
            // COLORREF is 0x00BBGGRR: a dim gray hairline.
            var border = 0x003A3A3A;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, ref border, sizeof(int));

            if (backdrop != Backdrop.None && MaterialsSupported) TrySetMaterial(window, hwnd, backdrop);
        };
    }

    private static void TrySetMaterial(Window window, IntPtr hwnd, Backdrop backdrop)
    {
        try
        {
            var type = backdrop == Backdrop.Acrylic ? 3 /* DWMSBT_TRANSIENTWINDOW */ : 2 /* DWMSBT_MAINWINDOW */;
            if (Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) != 0) return;
            // WindowChrome (custom title bars) owns the frame; let it extend the glass instead of fighting it.
            // On glass, Windows draws its own caption buttons (with Snap Layouts), so ours step aside.
            if (System.Windows.Shell.WindowChrome.GetWindowChrome(window) is { } chrome)
            {
                chrome.GlassFrameThickness = new Thickness(-1);
                chrome.UseAeroCaptionButtons = true;
                HideCustomCaptionButtons(window);
            }
            var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            if (Native.DwmExtendFrameIntoClientArea(hwnd, ref margins) != 0) return;
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
            // A light wash of the old solid color keeps text contrast steady over bright wallpapers.
            var tint = ((SolidColorBrush)Palette.Res("WindowBg")).Color;
            window.Background = new SolidColorBrush(Color.FromArgb(backdrop == Backdrop.Acrylic ? (byte)0x80 : (byte)0x40, tint.R, tint.G, tint.B));
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't set the window material", ex);
        }
    }

    /// <summary>Our own minimize/close buttons carry Tag="CaptionButtons".</summary>
    private static void HideCustomCaptionButtons(DependencyObject node)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            if (child is FrameworkElement { Tag: "CaptionButtons" } fe) fe.Visibility = Visibility.Collapsed;
            else HideCustomCaptionButtons(child);
        }
    }

    /// <summary>Settings > Personalization > Colors > Transparency effects.</summary>
    private static bool TransparencyOn()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch { return true; }
    }

    /// <summary>
    /// On glass, solid gray cards look pasted on. Swap them for light, see-through layers
    /// (Fluent's "layer" fills) before any window loads its StaticResources.
    /// </summary>
    public static void UseLayerFills(ResourceDictionary resources)
    {
        if (!MaterialsSupported) return;
        resources["Card"] = Frozen(0x12, 0xFF);
        resources["CardHover"] = Frozen(0x1C, 0xFF);
        resources["Selected"] = Frozen(0x24, 0xFF);
        resources["Control"] = Frozen(0x1F, 0xFF);

        static SolidColorBrush Frozen(byte a, byte v)
        {
            var b = new SolidColorBrush(Color.FromArgb(a, v, v, v));
            b.Freeze();
            return b;
        }
    }
}
