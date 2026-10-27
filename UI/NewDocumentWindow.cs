using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>新規ファイルのフォーマット。都度聞く。前回の指定を初期値にする。</summary>
internal sealed class NewDocumentWindow : Window
{
    private readonly ComboBox _rateCombo = CreateCombo();
    private readonly ComboBox _bitsCombo = CreateCombo();
    private readonly ComboBox _layoutCombo = CreateCombo();
    private readonly string[] _visibleIds;

    public DefaultAudioFormat.Spec SelectedFormat { get; private set; }

    public static DefaultAudioFormat.Spec? Show(Window owner, DefaultAudioFormat.Spec current, IEnumerable<string> visibleIds)
    {
        var dialog = new NewDocumentWindow(current, visibleIds)
        {
            Owner = owner,
        };
        WindowPaintReveal.EnsureRevealedForDialog(owner);
        return AcrylicDialogChrome.ShowDialog(dialog) == true ? dialog.SelectedFormat : null;
    }

    private NewDocumentWindow(DefaultAudioFormat.Spec current, IEnumerable<string> visibleIds)
    {
        _visibleIds = [.. visibleIds];
        SelectedFormat = current;
        Title = UiStrings.DialogNewDocumentTitle;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Foreground = (Brush)FindResource("PrimaryForeBrush");
        AcrylicDialogChrome.Prepare(this);
        AudioFormatComboFill.Fill(_rateCombo, _bitsCombo, _layoutCombo, current, _visibleIds);
                        
        var ok = CreateButton(UiStrings.ButtonOk, accept: true, accent: true, isDefault: true);
        var cancel = CreateButton(UiStrings.ButtonCancel, accept: false, isCancel: true);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel();
        root.Children.Add(FormatRow());
        root.Children.Add(buttons);
        AcrylicDialogChrome.SetBody(this, root, DesignMetrics.AudioPad);
        SourceInitialized += (_, _) =>
        {
            ComboBoxFit.Apply(_rateCombo);
            ComboBoxFit.Apply(_bitsCombo);
            ComboBoxFit.Apply(_layoutCombo);
        };
        AppDialogKeys.Attach(this, Close);
    }

    private void Accept()
    {
        SelectedFormat = AudioFormatComboFill.Read(_rateCombo, _bitsCombo, _layoutCombo, _visibleIds);
        DialogResult = true;
        Close();
    }

    private RoundedButton CreateButton(string text, bool accept, bool accent = false, bool isDefault = false, bool isCancel = false)
    {
        var button = new RoundedButton
        {
            Content = text,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
        };
        AppDialogKeys.PrepareActionButton(button, isDefault, isCancel);
        if (accent)
        {
            ActionButtonLooks.ApplyAccent(button);
        }
        else
        {
            ActionButtonLooks.ApplyClear(button);
        }

        button.Click += (_, _) =>
        {
            if (accept)
            {
                Accept();
            }
            else
            {
                Close();
            }
        };
        return button;
    }

    private static ComboBox CreateCombo() =>
        new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Height = DesignMetrics.AudioInputHeight,
            Padding = new Thickness(6, 1, 6, 1),
            FontSize = 11.333,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinWidth = 120,
        };

    private Grid FormatRow()
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < 6; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        AddField(grid, 0, UiStrings.LabelDefaultSampleRate, _rateCombo, left: 0);
        AddField(grid, 2, UiStrings.LabelDefaultBitDepth, _bitsCombo, left: 16);
        AddField(grid, 4, UiStrings.LabelDefaultChannelLayout, _layoutCombo, left: 16);
        return grid;
    }

    private static void AddField(Grid grid, int column, string label, ComboBox combo, double left)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(left, 0, 8, 0),
        };
                Grid.SetColumn(text, column);
        Grid.SetColumn(combo, column + 1);
        grid.Children.Add(text);
        grid.Children.Add(combo);
    }
}
