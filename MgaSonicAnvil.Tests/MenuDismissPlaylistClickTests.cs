using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>
/// プレイリストの右クリックメニューを「プレイリスト自身（別の行の波形列など）」を左クリックして
/// 閉じたとき、その dismiss クリックが DataGrid の既定選択へ貫通して別トラックを選択・再生して
/// しまう不具合の回帰テスト。貫通すると MainWindow 側で選択以外のトラックが落ちる
/// （71 曲 → 1 曲）。dismiss クリックはメニューが閉じた直後に届くので
/// <see cref="LibraryBrowserView.HasOpenContextMenu"/> だけでは拾えず、閉じてからアイドルまで立つ
/// dismiss ガード中は、プレイリストの左クリックを握りつぶす必要がある。
/// </summary>
public sealed class MenuDismissPlaylistClickTests
{
    /// <summary>
    /// 決定的テスト（実機入力なし）。メニューを開いて閉じた直後（dismiss ガード中）に、
    /// 行へ PreviewMouseLeftButtonDown をトンネルで流し、ハンドラが握りつぶす（e.Handled）ことを確かめる。
    /// 修正前は閉じた後 IsOpen=false のため握りつぶさず、DataGrid の既定選択が走ってしまう。
    /// </summary>
    [Fact]
    public void DismissGuard_SwallowsPlaylistRowClick()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new List<DocumentSession>();
            for (var i = 0; i < 6; i++)
            {
                sessions.Add(Session($"{i:00} track.wav"));
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.FocusList();
            Flush();

            var activations = 0;
            view.SessionActivated += (_, _) => activations++;

            // メニューを開く → 閉じる（アイドルへは落とさない＝ dismiss ガードは立ったまま）。
            Assert.True(view.TryOpenKeyboardContextMenu());
            Assert.True(view.HasOpenContextMenu);
            view.CloseKeyboardContextMenu();
            Assert.False(view.HasOpenContextMenu);
            Assert.True(view.IsContextMenuDismissGuardActive);

            var grid = view.FileGrid;
            var rows = FindDescendants<DataGridRow>(grid).ToList();
            Assert.True(rows.Count >= 5, $"行が足りない: {rows.Count}");

            // 外側クリック dismiss の貫通に相当する PreviewMouseLeftButtonDown をグリッドへ流す。
            // トンネルでグリッドのハンドラが走り、dismiss ガード中なら握りつぶす（修正後）。
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            };
            grid.RaiseEvent(args);

            // 修正後: dismiss ガード中は握りつぶす。これが false だと DataGrid の既定選択が走り、
            //         別トラックへの SessionActivated（＝再生切替・トラック脱落）が起きる。
            Assert.True(args.Handled, "dismiss ガード中のプレイリスト左クリックが握りつぶされていない。");
            Assert.Equal(0, activations);
            Assert.Same(sessions[0], view.SelectedSession);
            Assert.Equal(sessions.Count, view.BoundRowCount);

            window.Close();
        });
    }

    /// <summary>
    /// 実機入力での end-to-end 回帰。メニューを右クリックで開き、別の行（波形列あたり）を左クリックして
    /// 閉じる。dismiss が貫通すると別トラックが SessionActivated される（修正前）。修正後は 0 件。
    /// </summary>
    [Fact]
    public void OutsideClickOnPlaylistRow_DoesNotActivateClickedTrack()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new List<DocumentSession>();
            for (var i = 0; i < 8; i++)
            {
                sessions.Add(Session($"{i:00} track.wav"));
            }

            var view = new LibraryBrowserView();
            var root = new DockPanel();
            var bottom = new Button { Content = "Player (F10)", Height = 40 };
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            root.Children.Add(view);

            var window = new Window
            {
                Content = root,
                Width = 1000,
                Height = 560,
                Left = 80,
                Top = 80,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                Topmost = true,
            };
            window.Show();
            window.Activate();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Pump(TimeSpan.FromMilliseconds(400));
            view.FocusList();
            Pump(TimeSpan.FromMilliseconds(150));

            var grid = view.FileGrid;
            var rows = FindDescendants<DataGridRow>(grid).ToList();
            var row0 = rows[0];
            var p0 = row0.PointToScreen(new Point(row0.ActualWidth / 2, row0.ActualHeight / 2));
            for (var attempt = 0; attempt < 5 && !view.HasOpenContextMenu; attempt++)
            {
                SetCursorPos((int)p0.X, (int)p0.Y);
                Pump(TimeSpan.FromMilliseconds(80));
                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                PumpUntil(() => view.HasOpenContextMenu, TimeSpan.FromMilliseconds(600));
            }

            Assert.True(view.HasOpenContextMenu, "メニューを開けなかった（実機入力の再現に失敗）。");

            var activations = 0;
            DocumentSession? lastActivated = null;
            view.SessionActivated += (_, s) =>
            {
                activations++;
                lastActivated = s;
            };

            var targetRow = rows[4];
            var winBR = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
            var rowTL = targetRow.PointToScreen(new Point(0, 0));
            var targetX = Math.Min(rowTL.X + targetRow.ActualWidth * 0.7, winBR.X - 40);
            var pt = new Point(targetX, targetRow.PointToScreen(new Point(0, targetRow.ActualHeight / 2)).Y);
            SetCursorPos((int)pt.X, (int)pt.Y);
            Pump(TimeSpan.FromMilliseconds(60));
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            PumpUntil(() => !view.HasOpenContextMenu, TimeSpan.FromSeconds(2));
            Pump(TimeSpan.FromMilliseconds(400));

            window.Close();

            // dismiss クリックは「閉じるだけ」。別トラックを選択・再生してはいけない。
            Assert.Equal(0, activations);
            Assert.Null(lastActivated);
            Assert.Same(sessions[0], view.SelectedSession);
            Assert.Equal(sessions.Count, view.BoundRowCount);
        });
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, nuint dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    private static DocumentSession Session(string name) =>
        new(AudioDocument.CreateDeferred(Path.Combine(Path.GetTempPath(), name)));

    private static void EnsureTheme()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        Application.Current!.Resources["AccentCyanBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xFF));
        Application.Current.Resources["PrimaryForeBrush"] = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
        Application.Current.Resources["MenuHighlightBackBrush"] = new SolidColorBrush(Color.FromRgb(0x37, 0x37, 0x3A));
        Application.Current.Resources["ColorPanelBackBrush"] = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x2A));
        Application.Current.Resources["ChromeBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4E));
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static void Flush() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void PumpUntil(Func<bool> done, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = timeout };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        poll.Tick += (_, _) =>
        {
            if (!done())
            {
                return;
            }

            poll.Stop();
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        poll.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
