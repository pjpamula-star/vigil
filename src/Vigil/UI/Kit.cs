using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Vigil.UI;

/// <summary>Small builders so code-built pages use exactly the same type and spacing as the XAML ones.</summary>
internal static class Kit
{
    public static Style S(string key) => (Style)Application.Current.Resources[key];

    public static TextBlock T(string text, string style = "Body", double? size = null, Brush? color = null, Thickness? margin = null, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, Style = S(style) };
        if (size is { } sz) t.FontSize = sz;
        if (color is not null) t.Foreground = color;
        if (margin is { } m) t.Margin = m;
        if (weight is { } w) t.FontWeight = w;
        return t;
    }

    public static Border Card(UIElement child, Thickness? padding = null, Brush? background = null, Thickness? margin = null) => new()
    {
        Background = background ?? Palette.Res("Card"),
        CornerRadius = new CornerRadius(16),
        Padding = padding ?? new Thickness(18, 16, 18, 16),
        Margin = margin ?? new Thickness(0, 0, 0, 0),
        Child = child,
    };

    public static Button Button(string text, Action onClick, bool primary = false, Thickness? margin = null)
    {
        var b = new Button
        {
            Content = text,
            Style = S(primary ? "PrimaryButton" : "SecondaryButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = margin ?? new Thickness(0),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>"01" style section number + title, like Insomnia's page.</summary>
    public static StackPanel Section(string number, string title, string? lead = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 40, 0, 0) };
        panel.Children.Add(T(number, "Label", size: 12, color: Palette.Res("Text3")));
        panel.Children.Add(T(title, "TitleL", size: 22, margin: new Thickness(0, 4, 0, 0)));
        if (lead is not null) panel.Children.Add(T(lead, "Caption", size: 14, margin: new Thickness(0, 6, 0, 0)));
        return panel;
    }

    /// <summary>A numbered step: a circled number and one plain sentence.</summary>
    /// <summary>A plain numbered step: "1." then one sentence with a bold lead.</summary>
    public static UIElement Step(string n, string bold, string rest)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(T(n + ".", "BodyStrong", color: Palette.Res("Text2")));
        var text = new TextBlock { Style = S("Body") };
        text.Inlines.Add(new System.Windows.Documents.Run(bold) { FontWeight = FontWeights.SemiBold });
        text.Inlines.Add(new System.Windows.Documents.Run(" " + rest) { Foreground = Palette.Res("Text2") });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    public static Border Hairline(double top = 0, double bottom = 0) =>
        new() { Height = 1, Background = Palette.Res("Hairline"), Margin = new Thickness(0, top, 0, bottom) };
}

internal static class Icons
{
    public const string Open = "\uE8A7";
    public const string ChevronDown = "\uE70D";
    public const string ChevronUp = "\uE70E";
    public const string ChevronRight = "\uE76C";
    public const string Close = "\uE711";

    /// <summary>Text with a small system icon after it ("Open in Claude ↗" without the arrow character).</summary>
    public static StackPanel Label(string text, string glyph, Brush color, double size = 12.5)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(new TextBlock { Text = text, Foreground = color, FontSize = size, VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(new TextBlock
        {
            Text = glyph, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = size - 3,
            Foreground = color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 0, 0),
        });
        return p;
    }
}
