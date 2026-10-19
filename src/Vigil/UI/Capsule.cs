using System.Windows;
using System.Windows.Controls;

namespace Vigil.UI;

/// <summary>
/// A true capsule: the corner radius is always half the height.
/// (WPF turns an oversized CornerRadius like 999 into an ellipse on wide shapes.)
/// </summary>
public sealed class Capsule : Border
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        var r = Math.Max(0, Math.Min(finalSize.Height, finalSize.Width) / 2);
        if (Math.Abs(CornerRadius.TopLeft - r) > 0.1) CornerRadius = new CornerRadius(r);
        return base.ArrangeOverride(finalSize);
    }
}
