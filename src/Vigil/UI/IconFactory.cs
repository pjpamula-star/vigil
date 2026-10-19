using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vigil.UI;

public enum TrayState { Idle, Working, NeedsYou, Hot, Manual, Paused }

/// <summary>Draws the Night-watch eye for the tray in each state, at the exact pixel size Windows asks for.</summary>
internal static class IconFactory
{
    private static readonly Dictionary<(TrayState, int, bool, Vigil.Core.EyePose), System.Drawing.Icon> Cache = new();

    /// <summary>The tray icon for a state, with the eye in <paramref name="pose"/> (blinking, glancing). Each pose is drawn once.</summary>
    public static System.Drawing.Icon Tray(TrayState state, int px, Vigil.Core.EyePose? pose = null)
    {
        var light = TaskbarIsLight();
        var p = (pose ?? Vigil.Core.EyePose.Rest).Quantized();
        if (Cache.TryGetValue((state, px, light, p), out var cached)) return cached;
        // A few hundred poses at most in practice; start over rather than grow without end.
        // (Icons handed to the tray are kept alive by it; the cache only drops its references.)
        if (Cache.Count > 600) Cache.Clear();
        var icon = ToIcon(StateImage(state, px, light, p));
        Cache[(state, px, light, p)] = icon;
        return icon;
    }

    /// <summary>
    /// The tray icon for a state. Monochrome like the built-in Windows tray icons (white on a dark
    /// taskbar, near-black on a light one). Color appears only as a small badge that means something:
    /// amber = a session needs you, red = too hot or battery low.
    /// </summary>
    public static BitmapSource StateImage(TrayState state, int px, bool lightBackground = false, Vigil.Core.EyePose? pose = null)
    {
        var ink = lightBackground ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Colors.White;
        var (alpha, filled, badge) = state switch
        {
            TrayState.Idle => (0.6, false, (Color?)null),
            TrayState.Paused => (0.38, false, null),
            TrayState.NeedsYou => (1.0, true, Color.FromRgb(0xFF, 0x9F, 0x0A)),
            TrayState.Hot => (1.0, true, Color.FromRgb(0xFF, 0x45, 0x3A)),
            _ => (1.0, true, null),
        };
        return Render(px, Color.FromArgb((byte)(alpha * 255), ink.R, ink.G, ink.B), badge, filled, pose);
    }

    /// <summary>True when Windows uses a light taskbar (tray icons must then be dark).</summary>
    public static bool TaskbarIsLight()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The Vigil mark as an image, in plain white by default.</summary>
    public static ImageSource Glyph(int px, Color? color = null) => Render(px, color ?? Color.FromRgb(0xF5, 0xF5, 0xF7), null, filledPupil: true);

    private static BitmapSource Render(int px, Color color, Color? badge, bool filledPupil, Vigil.Core.EyePose? pose = null)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var scale = px / 100.0;
            dc.PushTransform(new ScaleTransform(scale, scale));
            // Thicker stroke at tiny sizes so the shape survives 16 px. The badge has a thin dark ring so it
            // reads at 16 px on any taskbar.
            var stroke = px <= 20 ? 10.5 : px <= 32 ? 9 : 7.5;
            EyeView.Draw(dc, pose ?? Vigil.Core.EyePose.Rest, new SolidColorBrush(color), filledPupil, stroke, badge is { } b ? new SolidColorBrush(b) : null);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static System.Drawing.Icon ToIcon(BitmapSource src)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using var bitmap = new System.Drawing.Bitmap(ms);
        var h = bitmap.GetHicon();
        // Clone so the icon owns its own handle, then free the temporary one.
        var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }
}
