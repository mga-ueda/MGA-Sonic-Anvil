using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>
/// プレイリストの右クリックメニューを外側クリックで閉じたとき、そのクリックが下にある
/// トランスポートの「プレイヤー(F10)」ボタンへ貫通する WPF の挙動を実機入力で再現する。
/// この貫通クリックで ToggleLibraryMaximize が走ると、プレイヤーを抜けて選択以外の
/// トラックが消える（報告されたバグ）。<see cref="LibraryBrowserView.IsContextMenuDismissGuardActive"/>
/// が貫通クリックの瞬間に立っていれば、呼び出し側（MainWindow）はトグルを弾ける。
/// </summary>
public sealed class MenuDismissPassThroughTests
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, nuint dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    [Fact]
    public void OutsideClickDismiss_DoesNotLeakClickIntoPlayerToggle()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.wav");
            var second = Session("02 b.wav");
            var third = Session("03 c.wav");
            var view = new LibraryBrowserView();

            // MainWindow のプレイヤー切替ボタン相当。ガードが立っていればトグルを弾く。
            var toggled = false;
            var clickFired = false;
            var guardAtClick = false;
            var playerButton = new Button { Content = "Player (F10)", Height = 48 };
            playerButton.Click += (_, _) =>
            {
                clickFired = true;
                guardAtClick = view.IsContextMenuDismissGuardActive;
                if (!view.IsContextMenuDismissGuardActive)
                {
                    toggled = true; // ← ガードが無いと（旧コード）ここに入る＝プレイヤーを抜ける
                }
            };

            var root = new DockPanel();
            DockPanel.SetDock(playerButton, Dock.Bottom);
            root.Children.Add(playerButton);
            root.Children.Add(view);

            var window = new Window
            {
                Content = root,
                Width = 900,
                Height = 480,
                Left = 90,
                Top = 90,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                Topmost = true,
            };
            window.Show();
            window.Activate();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second, third], first, [first, second, third]);
            Pump(TimeSpan.FromMilliseconds(400));
            view.FocusList();
            Pump(TimeSpan.FromMilliseconds(150));

            var grid = view.FileGrid;
            var rowCenter = grid.PointToScreen(new Point(grid.ActualWidth / 2, 40));
            for (var attempt = 0; attempt < 5 && !view.HasOpenContextMenu; attempt++)
            {
                SetCursorPos((int)rowCenter.X, (int)rowCenter.Y);
                Pump(TimeSpan.FromMilliseconds(80));
                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                PumpUntil(() => view.HasOpenContextMenu, TimeSpan.FromMilliseconds(600));
            }

            Assert.True(
                view.HasOpenContextMenu,
                "右クリックでコンテキストメニューを開けなかった（実機入力の再現に失敗）。");

            var outside = playerButton.PointToScreen(
                new Point(playerButton.ActualWidth / 2, playerButton.ActualHeight / 2));
            SetCursorPos((int)outside.X, (int)outside.Y);
            Pump(TimeSpan.FromMilliseconds(60));
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            PumpUntil(() => !view.HasOpenContextMenu, TimeSpan.FromSeconds(2));
            Pump(TimeSpan.FromMilliseconds(300));

            window.Close();

            // メニューを開けた環境では、外側クリックが下のボタンへ貫通するのが前提。
            // 貫通した瞬間にガードが立っていればトグルは弾かれる（＝曲が消えない）。
            Assert.True(clickFired, "外側クリックがプレイヤー切替ボタンへ貫通していない（再現条件を満たせない）。");
            Assert.True(guardAtClick, "貫通クリック時にメニュー dismiss ガードが立っていない。");
            Assert.False(toggled, "外側クリックの dismiss がプレイヤー切替へ貫通した。");
        });
    }

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
