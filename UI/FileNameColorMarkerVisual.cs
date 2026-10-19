using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil.UI;

/// <summary>ファイル名の頭に置く縦長の色マーカー。</summary>
internal static class FileNameColorMarkerVisual
{
    public const double Width = 4;
    public const double Height = 14;
    public const double Gap = 6;

    /// <summary>マーカーと、その右の隙間。タブ幅と列幅に足す。</summary>
    public static double SlotWidth => Width + Gap;

    private static readonly Brush[] Brushes = CreateBrushes();

    public static IReadOnlyList<FileNameColorChoice> Choices { get; } = CreateChoices();

    public static Border Create() =>
        new()
        {
            Width = Width,
            Height = Height,
            Margin = new Thickness(0, 0, Gap, 0),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

    public static void Apply(Border marker, string? fileName, string? path)
    {
        var index = FileNameColorResolve.TryResolve(fileName, path, out var resolved, out _)
            ? resolved
            : -1;
        if (!TryBrush(index, out var brush))
        {
            marker.Background = null;
            marker.Visibility = Visibility.Collapsed;
            return;
        }

        marker.Background = brush;
        marker.Visibility = Visibility.Visible;
    }

    public static bool TryBrush(int index, out Brush brush)
    {
        if ((uint)index >= (uint)Brushes.Length)
        {
            brush = System.Windows.Media.Brushes.Transparent;
            return false;
        }

        brush = Brushes[index];
        return true;
    }

    private static Brush[] CreateBrushes()
    {
        var brushes = new Brush[FileNameColorPalette.Count];
        for (var i = 0; i < brushes.Length; i++)
        {
            FileNameColorPalette.TryRgb(i, out var r, out var g, out var b);
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            brushes[i] = brush;
        }

        return brushes;
    }

    private static FileNameColorChoice[] CreateChoices()
    {
        var choices = new FileNameColorChoice[FileNameColorPalette.Count];
        for (var i = 0; i < choices.Length; i++)
        {
            choices[i] = new FileNameColorChoice(i, "#" + FileNameColorPalette.Colors[i], Brushes[i]);
        }

        return choices;
    }
}

internal sealed class FileNameColorChoice
{
    public FileNameColorChoice(int index, string hex, Brush brush)
    {
        Index = index;
        Hex = hex;
        Brush = brush;
    }

    public int Index { get; }

    public string Hex { get; }

    public Brush Brush { get; }
}
