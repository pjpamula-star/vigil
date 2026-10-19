using System.Windows;

namespace Vigil.Core;

/// <summary>
/// Finds the empty stretch of the taskbar: between the last app button and whatever comes next
/// (the weather widget, or the tray). The desktop pill sits there, against the right end, so new app
/// buttons fill in from the left without pushing it around, and it never covers a window.
/// </summary>
public static class TaskbarGap
{
    public enum Kind { App, Other }

    /// <summary>
    /// The empty gap in the taskbar, in the same units as the inputs, or null if the taskbar isn't along
    /// the bottom. <paramref name="items"/> are the taskbar's buttons: app buttons (and Start, Search,
    /// Task View) as <see cref="Kind.App"/>; widgets and tray icons as <see cref="Kind.Other"/>.
    /// </summary>
    public static Rect? Find(Rect taskbar, Rect screen, IEnumerable<(Kind Kind, Rect Bounds)> items)
    {
        // Only a bottom taskbar: it must be wide, and hug the bottom of the screen.
        if (taskbar.Width < taskbar.Height * 4 || taskbar.Bottom < screen.Bottom - 2 || taskbar.Top < screen.Top + screen.Height / 2) return null;

        var list = items.Where(i => i.Bounds.Width > 0 && i.Bounds.IntersectsWith(taskbar)).ToList();
        var apps = list.Where(i => i.Kind == Kind.App).Select(i => i.Bounds).ToList();
        var start = apps.Count > 0 ? apps.Max(r => r.Right) : taskbar.Left;
        var after = list.Where(i => i.Kind == Kind.Other && i.Bounds.Left >= start - 1).Select(i => i.Bounds.Left).ToList();
        var end = after.Count > 0 ? after.Min() : taskbar.Right;
        if (end <= start) return null;
        return new Rect(start, taskbar.Top, end - start, taskbar.Height);
    }
}
