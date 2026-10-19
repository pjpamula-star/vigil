using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Vigil.Core;

namespace Vigil.UI;

/// <summary>
/// Reads where the taskbar's buttons are, through UI Automation (the same view screen readers use),
/// and returns the empty gap in physical pixels. Called off the UI thread: asking Explorer can take
/// a moment, and must never stall Vigil's windows.
/// </summary>
internal static class TaskbarProbe
{
    public static Rect? FindGap()
    {
        var bar = FindWindow("Shell_TrayWnd", null);
        if (bar == IntPtr.Zero || !GetWindowRect(bar, out var r)) return null;
        var taskbar = new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        var monitor = MonitorFromWindow(bar, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;
        var screen = new Rect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);

        var root = AutomationElement.FromHandle(bar);
        var items = new List<(TaskbarGap.Kind, Rect)>();
        foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition))
        {
            AutomationElement.AutomationElementInformation c;
            try { c = e.Current; } catch { continue; }
            var b = c.BoundingRectangle;
            if (b.IsEmpty || b.Width <= 0) continue;
            var kind = c.ClassName switch
            {
                "Taskbar.TaskListButtonAutomationPeer" => TaskbarGap.Kind.App,
                _ when c.AutomationId is "StartButton" or "SearchButton" or "TaskViewButton" => TaskbarGap.Kind.App,
                _ when c.AutomationId == "WidgetsButton" || c.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal) || c.ClassName == "TrayNotifyWnd" => TaskbarGap.Kind.Other,
                _ => (TaskbarGap.Kind?)null,
            };
            if (kind is { } k) items.Add((k, b));
        }
        return TaskbarGap.Find(taskbar, screen, items);
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RECT Monitor, Work; public int Flags; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr m, ref MonitorInfo i);
}
