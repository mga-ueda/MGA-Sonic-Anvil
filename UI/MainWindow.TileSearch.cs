using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    /// <summary>
    /// タイル表示専用のタブ名フィルター。Ctrl+F で開き、1 文字ごとに判定する。
    /// 空白区切りは AND、| 区切りは OR。ヒットしないタイルは暗くしてすりガラスで覆う。
    /// Enter で条件を確定、Alt+Enter はヒット以外を閉じてフィルター解除（未保存は残す。確認なし）。
    /// Esc は開いた時点の文字列へ戻すキャンセル、空欄で解除。
    /// Ctrl+Shift+F はボックスを出さずに検索ワードを空にして確定（フィルター解除）する。
    /// </summary>
    private readonly List<string[]> _tileSearchGroups = [];

    /// <summary>Esc キャンセルで戻す、検索ボックスを開いた時点の文字列。</summary>
    private string _tileSearchTextAtOpen = string.Empty;

    private bool IsTileSearchFocused => TileSearchBox.IsKeyboardFocusWithin;

    /// <summary>検索フィルターが効いているか。タイル表示以外では常に無効。</summary>
    private bool TileSearchFilterActive => _tileMode && _tileSearchGroups.Count > 0;

    /// <summary>
    /// 他のショートカットより先に呼ぶ。Ctrl+F はタイル表示のときだけ検索を開く。
    /// ボックスにフォーカスがある間は Enter（確定）／Alt+Enter（ヒット以外を閉じる）と Esc（キャンセル）だけ拾う。
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

        // Alt でボックスからフォーカスが外れても、フィルター中／ボックス表示中は閉じる。
        // 空クエリは全件ヒット扱いなので、解除してから判定すると誰も閉じない。
        if (key == Key.Enter && modifiers == ModifierKeys.Alt)
        {
            if (!_tileMode
                || (!IsTileSearchFocused
                    && TileSearchHost.Visibility != Visibility.Visible
                    && !TileSearchFilterActive))
            {
                return false;
            }

            CommitTileSearchKeepHits();
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

    /// <summary>
    /// Alt+Enter。ヒットしなかったタブを閉じ、未保存の編集があるタブは残す。確認は出さない。
    /// 閉じたあとフィルターは解除する。
    /// </summary>
    private void CommitTileSearchKeepHits()
    {
        // フィルター解除より先に条件を固定する。空条件は全件ヒットなので、先に消すと閉じる対象が無くなる。
        var groups = SnapshotTileSearchGroups();
        var drop = new List<DocumentSession>();
        foreach (var session in TileSearchQuery.SessionsToDrop(_sessions, groups))
        {
            if (ReferenceEquals(session, _recordSession))
            {
                continue;
            }

            drop.Add(session);
        }

        if (drop.Count > 0)
        {
            CloseUnmatchedTileSearchSessions(drop);
        }

        CloseTileSearch();
    }

    /// <summary>今画面に効いている条件。TextChanged で消えたあとも、残っているグループを優先する。</summary>
    private List<string[]> SnapshotTileSearchGroups()
    {
        if (_tileSearchGroups.Count > 0)
        {
            return [.. _tileSearchGroups];
        }

        return TileSearchQuery.Parse(TileSearchBox.Text);
    }

    private void CloseUnmatchedTileSearchSessions(IReadOnlyList<DocumentSession> drop)
    {
        var dropSet = drop as HashSet<DocumentSession> ?? [.. drop];
        DocumentSession? nextActive = null;
        if (_activeSession is { } active && !dropSet.Contains(active))
        {
            nextActive = active;
        }
        else
        {
            foreach (var session in _sessions)
            {
                if (!dropSet.Contains(session))
                {
                    nextActive = session;
                    break;
                }
            }
        }

        if (_activeSession is { } closing && dropSet.Contains(closing))
        {
            CaptureActiveSessionView();
        }

        DropLibrarySessionsFast(drop);
        if (_sessions.Count == 0)
        {
            BindWorkspace(null);
            ForgetClosedDocument();
            return;
        }

        if (!ReferenceEquals(_activeSession, nextActive))
        {
            BindWorkspace(nextActive);
        }
        else
        {
            RebuildTabBar();
            RefreshStatus();
        }

        // 前面タブを差し替えただけでは古いタイルが残る。フィルター解除より先に張り直す。
        NotifyWaveformSessionsChanged();
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
        _tileSearchGroups.AddRange(TileSearchQuery.Parse(text));
    }

    /// <summary>どれかの OR グループの全語（AND）をタブ名が含めばヒット。</summary>
    private bool TileSearchMatches(DocumentSession session) =>
        TileSearchQuery.Matches(session.DisplayName, _tileSearchGroups);

    private const double TileSearchBlurRadius = 8;

    /// <summary>
    /// ぼかしをタイルの外へはみ出させる量。半径ぶん足さないと端が薄くなる。
    /// さらに仕切りの半分（2px）を足し、隣のタイルと仕切りの中央でフルのぼかしが重なるようにする。
    /// </summary>
    private const double TileSearchBlurOverscan = TileSearchBlurRadius + TileDividerWidth / 2;

    private const int TileSearchZUnmatched = 0;
    private const int TileSearchZFrost = 1;
    private const int TileSearchZMatched = 2;

    /// <summary>
    /// グリッド全体を覆う一枚の暗幕。タイル毎だとセル境界・余りマス・仕切りに線が出る。
    /// ヒットしたタイルはこれより前面に出すので、穴が開いたように見える。
    /// </summary>
    private Border? _tileSearchFrost;

    /// <summary>検索フィルターでヒットせず、すりガラスに覆われているか。覆われたタイルは操作できない。</summary>
    private bool IsTileSearchVeiled(DocumentSession session) =>
        TileSearchFilterActive && !TileSearchMatches(session);

    /// <summary>
    /// ヒットしない領域を一続きのすりガラスにする。暗幕はグリッド全体の 1 枚。
    /// ぼかしは仕切りの半分まで伸ばして隣と中央でつなぎ、ヒットしたタイルだけ前面に残す。
    /// 余りマス（波形の無い背景）も同じ暗幕で覆う。
    /// </summary>
    private void ApplyTileSearchVeils()
    {
        var filter = TileSearchFilterActive;
        EnsureTileSearchFrost(filter);

        foreach (var pane in _tilePanes)
        {
            var veiled = IsTileSearchVeiled(pane.Session);
            ApplyTileSearchBlur(pane, veiled);
            Panel.SetZIndex(pane.Host, filter && !veiled ? TileSearchZMatched : TileSearchZUnmatched);
        }
    }

    private void ApplyTileSearchBlur(WaveformTilePane pane, bool veiled)
    {
        if (veiled)
        {
            pane.Body.Effect ??= new BlurEffect { Radius = TileSearchBlurRadius };
            pane.Body.Margin = new Thickness(-TileSearchBlurOverscan);
            pane.Layers.ClipToBounds = false;
            pane.Host.ClipToBounds = false;
            return;
        }

        pane.Body.Effect = null;
        pane.Body.Margin = new Thickness(0);
        pane.Layers.ClipToBounds = true;
        pane.Host.ClipToBounds = false;
    }

    private void EnsureTileSearchFrost(bool visible)
    {
        if (_tileGrid is null)
        {
            return;
        }

        if (_tileSearchFrost is null)
        {
            _tileSearchFrost = new Border
            {
                // セル境界の 1px 隙間をレイアウト丸めで取りこぼさない。
                SnapsToDevicePixels = false,
                UseLayoutRounding = false,
            };
            _tileSearchFrost.SetResourceReference(Border.BackgroundProperty, "TileSearchVeilBrush");
        }

        var rows = Math.Max(1, _tileGrid.RowDefinitions.Count);
        var cols = Math.Max(1, _tileGrid.ColumnDefinitions.Count);
        if (!ReferenceEquals(_tileSearchFrost.Parent, _tileGrid))
        {
            DetachTileHost(_tileSearchFrost);
            Grid.SetRow(_tileSearchFrost, 0);
            Grid.SetColumn(_tileSearchFrost, 0);
            _tileGrid.Children.Add(_tileSearchFrost);
        }

        Grid.SetRowSpan(_tileSearchFrost, rows);
        Grid.SetColumnSpan(_tileSearchFrost, cols);
        Panel.SetZIndex(_tileSearchFrost, TileSearchZFrost);
        _tileSearchFrost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _tileSearchFrost.IsHitTestVisible = visible;
    }
}
