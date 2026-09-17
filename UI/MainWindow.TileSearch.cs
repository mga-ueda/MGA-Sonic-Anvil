using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Effects;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    /// <summary>
    /// タイル表示専用のタブ名フィルター。Ctrl+F で開き、1 文字ごとに判定する。
    /// 空白区切りは AND、| 区切りは OR。ヒットしないタイルは暗くしてすりガラスで覆う。
    /// Enter で条件を確定、Esc は開いた時点の文字列へ戻すキャンセル、空欄で解除。
    /// Ctrl+Shift+F はボックスを出さずに検索ワードを空にして確定（フィルター解除）する。
    /// </summary>
    private readonly List<string[]> _tileSearchGroups = [];

    /// <summary>Esc キャンセルで戻す、検索ボックスを開いた時点の文字列。</summary>
    private string _tileSearchTextAtOpen = string.Empty;

    private static readonly char[] TileSearchOrSeparators = ['|', '｜'];
    private static readonly char[] TileSearchAndSeparators = [' ', '\u3000', '\t'];

    private bool IsTileSearchFocused => TileSearchBox.IsKeyboardFocusWithin;

    /// <summary>検索フィルターが効いているか。タイル表示以外では常に無効。</summary>
    private bool TileSearchFilterActive => _tileMode && _tileSearchGroups.Count > 0;

    /// <summary>
    /// 他のショートカットより先に呼ぶ。Ctrl+F はタイル表示のときだけ検索を開く。
    /// ボックスにフォーカスがある間は Enter（確定）と Esc（キャンセル）だけ拾う。
    /// </summary>
    private bool TryProcessTileSearchKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.F && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            // 検索ワードを空にして確定＝フィルター解除。ボックスは出さずサイレントに実行する。
            if (TileSearchBox.Text.Length == 0)
            {
                return false;
            }

            CloseTileSearch();
            return true;
        }

        if (key == Key.F && modifiers == ModifierKeys.Control)
        {
            if (!_tileMode)
            {
                return false;
            }

            OpenTileSearch();
            return true;
        }

        if (!IsTileSearchFocused || ImeComposition.IsComposing)
        {
            return false;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            CommitTileSearch();
            return true;
        }

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            CancelTileSearch();
            return true;
        }

        return false;
    }

    private void OpenTileSearch()
    {
        TileSearchLabel.Text = UiStrings.TileSearchLabel;
        TileSearchHint.Text = UiStrings.TileSearchSyntaxHint;
        TipService.Set(TileSearchBox, UiStrings.TipTileSearch);
        _tileSearchTextAtOpen = TileSearchBox.Text;
        TileSearchHost.Visibility = Visibility.Visible;
        TileSearchBox.Focus();
        TileSearchBox.SelectAll();
    }

    /// <summary>
    /// Enter。今の条件を確定してボックスを閉じる（フィルターは維持）。空欄なら解除して閉じる。
    /// 確定してもタブは選択しない。残っていた複数選択も解除する（選択は Ctrl+Shift+A で明示的に行う）。
    /// </summary>
    private void CommitTileSearch()
    {
        ClearTabSelection();
        if (_tileSearchGroups.Count == 0)
        {
            CloseTileSearch();
            return;
        }

        HideTileSearchBox();
    }

    /// <summary>Esc。開いた時点の文字列へ戻してボックスを閉じる。戻した文字列のフィルターは残る。</summary>
    private void CancelTileSearch()
    {
        TileSearchBox.Text = _tileSearchTextAtOpen;
        if (_tileSearchGroups.Count == 0)
        {
            CloseTileSearch();
            return;
        }

        HideTileSearchBox();
    }

    /// <summary>検索文字は残したままボックスだけ隠す。次の Ctrl+F で同じ条件から編集できる。</summary>
    private void HideTileSearchBox()
    {
        TileSearchHost.Visibility = Visibility.Collapsed;
        Keyboard.Focus(Waveform);
    }

    /// <summary>検索文字を消してボックスを隠す。タイル解除時にも呼ぶ。</summary>
    private void CloseTileSearch()
    {
        if (TileSearchBox.Text.Length > 0)
        {
            // TextChanged がフィルター解除まで面倒を見る。
            TileSearchBox.Text = string.Empty;
        }

        if (TileSearchHost.Visibility == Visibility.Collapsed)
        {
            return;
        }

        TileSearchHost.Visibility = Visibility.Collapsed;
        if (IsTileSearchFocused)
        {
            Keyboard.Focus(Waveform);
        }
    }

    private void TileSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ParseTileSearch(TileSearchBox.Text);
        // 条件が変わると古いタブ選択はヒット状況と食い違うので解除する。
        ClearTabSelection();
        ApplyTileSearchVeils();
    }

    /// <summary>| で OR グループへ分け、各グループを空白で AND 語へ分ける。</summary>
    private void ParseTileSearch(string text)
    {
        _tileSearchGroups.Clear();
        foreach (var group in text.Split(TileSearchOrSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var terms = group.Split(TileSearchAndSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length > 0)
            {
                _tileSearchGroups.Add(terms);
            }
        }
    }

    /// <summary>どれかの OR グループの全語（AND）をタブ名が含めばヒット。</summary>
    private bool TileSearchMatches(DocumentSession session)
    {
        if (_tileSearchGroups.Count == 0)
        {
            return true;
        }

        var name = session.DisplayName;
        foreach (var terms in _tileSearchGroups)
        {
            var all = true;
            foreach (var term in terms)
            {
                if (!name.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    all = false;
                    break;
                }
            }

            if (all)
            {
                return true;
            }
        }

        return false;
    }

    private const double TileSearchBlurRadius = 8;

    /// <summary>検索フィルターでヒットせず、すりガラスに覆われているか。覆われたタイルは操作できない。</summary>
    private bool IsTileSearchVeiled(DocumentSession session) =>
        TileSearchFilterActive && !TileSearchMatches(session);

    /// <summary>
    /// ヒットしないタイルを暗い覆い＋ぼかし（すりガラス）にする。ぼかしは端が薄くなって
    /// 隣のタイルとの境目に見えるため、ぼかし半径ぶん外へはみ出させてタイル境界でクリップする。
    /// </summary>
    private void ApplyTileSearchVeils()
    {
        foreach (var pane in _tilePanes)
        {
            var veiled = IsTileSearchVeiled(pane.Session);
            pane.Veil.Visibility = veiled ? Visibility.Visible : Visibility.Collapsed;
            if (veiled)
            {
                pane.Body.Effect ??= new BlurEffect { Radius = TileSearchBlurRadius };
                pane.Body.Margin = new Thickness(-TileSearchBlurRadius);
            }
            else
            {
                pane.Body.Effect = null;
                pane.Body.Margin = new Thickness(0);
            }
        }
    }
}
