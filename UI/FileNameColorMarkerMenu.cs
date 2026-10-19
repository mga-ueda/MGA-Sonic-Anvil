using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>右クリックの「カラー」。28 色から選ぶか、ファイル名ルールへ戻す。</summary>
internal static class FileNameColorMarkerMenu
{
    public static MenuItem Create(Func<string[]> paths, Action<int?> apply)
    {
        var root = new MenuItem { Header = UiStrings.LibraryMenuColor };
        var clear = new MenuItem
        {
            Header = UiStrings.LibraryMenuColorAutomatic,
            IsCheckable = true,
        };
        Arm(clear, () => apply(null));

        var palette = new FileNameColorPaletteBoard { Margin = new Thickness(4, 2, 4, 4) };
        palette.ColorSelected += (_, index) =>
        {
            apply(index);
            Close(root);
        };
        var board = new MenuItem
        {
            Header = palette,
            StaysOpenOnClick = true,
            Padding = new Thickness(0),
            Focusable = false,
            Style = PaletteStyle,
        };
        root.Items.Add(clear);
        root.Items.Add(new Separator());
        root.Items.Add(board);
        root.SubmenuOpened += (_, _) => palette.Mark(CurrentManual(paths(), clear));
        root.IsEnabled = paths().Length > 0;
        return root;
    }

    private static int? CurrentManual(string[] paths, MenuItem clear)
    {
        var anyManual = false;
        int? shared = null;
        var uniform = true;
        foreach (var path in paths)
        {
            if (!FileNameColorAssignments.TryGet(AppStorage.Settings.FileNameColorAssignments, path, out var index))
            {
                uniform = false;
                continue;
            }

            anyManual = true;
            if (shared is null)
            {
                shared = index;
            }
            else if (shared.Value != index)
            {
                uniform = false;
            }
        }

        clear.IsChecked = paths.Length > 0 && !anyManual;
        return uniform ? shared : null;
    }

    private static readonly Style PaletteStyle = CreatePaletteStyle();

    /// <summary>パレット全体を一つのメニュー項目としてハイライトしない。</summary>
    private static Style CreatePaletteStyle()
    {
        var header = new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(
            Control.TemplateProperty,
            new ControlTemplate(typeof(MenuItem)) { VisualTree = header }));
        return style;
    }

    private static void Arm(MenuItem item, Action action)
    {
        var armed = false;
        item.PreviewMouseLeftButtonDown += (_, _) => armed = true;
        item.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                armed = true;
            }
        };
        item.Click += (_, _) =>
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            action();
        };
    }

    private static void Close(MenuItem item)
    {
        item.IsSubmenuOpen = false;
        switch (item.Parent)
        {
            case MenuItem parent:
                Close(parent);
                break;
            case ContextMenu menu:
                menu.IsOpen = false;
                break;
        }
    }
}
