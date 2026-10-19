using System.Windows;
using Vigil.Core;
using K = Vigil.Core.TaskbarGap.Kind;

namespace Vigil.Tests;

public class TaskbarGapTests
{
    private static readonly Rect Screen = new(0, 0, 2048, 1280);
    private static readonly Rect Bar = new(0, 1220, 2048, 60);

    [Fact]
    public void Finds_the_space_between_the_last_app_and_the_weather_widget()
    {
        // The real layout from a left-aligned Windows 11 taskbar.
        var items = new[]
        {
            (K.App, new Rect(0, 1220, 69, 60)),        // Start
            (K.App, new Rect(404, 1220, 55, 60)),
            (K.App, new Rect(1119, 1220, 55, 60)),     // Chrome, last app
            (K.Other, new Rect(1442, 1220, 190, 60)),  // Widgets
            (K.Other, new Rect(1646, 1220, 40, 60)),   // tray
        };
        Assert.Equal(new Rect(1174, 1220, 268, 60), TaskbarGap.Find(Bar, Screen, items));
    }

    [Fact]
    public void Centered_taskbar_uses_the_space_before_the_tray()
    {
        var items = new[]
        {
            (K.Other, new Rect(0, 1220, 190, 60)),     // Widgets on the left
            (K.App, new Rect(700, 1220, 55, 60)),
            (K.App, new Rect(1300, 1220, 55, 60)),
            (K.Other, new Rect(1646, 1220, 40, 60)),
        };
        Assert.Equal(new Rect(1355, 1220, 291, 60), TaskbarGap.Find(Bar, Screen, items));
    }

    [Fact]
    public void No_gap_for_a_side_or_top_taskbar()
    {
        Assert.Null(TaskbarGap.Find(new Rect(0, 0, 60, 1280), Screen, Array.Empty<(K, Rect)>()));
        Assert.Null(TaskbarGap.Find(new Rect(0, 0, 2048, 60), Screen, Array.Empty<(K, Rect)>()));
    }
}
