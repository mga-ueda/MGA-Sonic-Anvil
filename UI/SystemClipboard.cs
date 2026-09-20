using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// Windows クリップボード。WPF の <see cref="Clipboard.SetText"/> は
/// <c>OleFlushClipboard</c> が <c>CLIPBRD_E_CANT_OPEN</c> だと例外になる。
/// コンテキストメニューの Click 中に UI スレッドを Sleep させると、メニューが
/// クリップボードを離せなくなり、アプリ自身が失敗を固定してしまう。
/// </summary>
internal static class SystemClipboard
{
    internal const int RetryAttempts = 8;
    internal const int RetryDelayMs = 50;
    internal const int ClipbrdECantOpen = unchecked((int)0x800401D0);

    public static bool TrySetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryCompleteSet(
            () => Clipboard.SetDataObject(CreateTextData(text), copy: false),
            Clipboard.Flush);
    }

    public static bool TrySetFileDropCopy(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var data = LibraryShellFiles.CreateOleCopyData(paths);
        if (data is null)
        {
            return false;
        }

        return TryCompleteSet(
            () => Clipboard.SetDataObject(data, copy: false),
            Clipboard.Flush);
    }

    public static void TrySetFileDropCopy(IReadOnlyList<string> paths, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (LibraryShellFiles.ExistingPaths(paths).Length == 0)
        {
            return;
        }

        var dispatcher = owner?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        RetryWhenIdle(
            dispatcher,
            remaining =>
            {
                if (TrySetFileDropCopy(paths))
                {
                    return true;
                }

                if (remaining <= 1)
                {
                    ShowBusy(owner);
                    return true;
                }

                return false;
            },
            RetryAttempts);
    }

    public static void TrySetText(string text, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(text);
        var dispatcher = owner?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        RetryWhenIdle(
            dispatcher,
            remaining =>
            {
                if (TrySetText(text))
                {
                    return true;
                }

                if (remaining <= 1)
                {
                    ShowBusy(owner);
                    return true;
                }

                return false;
            },
            RetryAttempts);
    }

    public static bool TryGetText(out string text)
    {
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            return true;
        }
        catch (ExternalException)
        {
            text = string.Empty;
            return false;
        }
    }

    public static bool ContainsText()
    {
        try
        {
            return Clipboard.ContainsText();
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    public static void ShowBusy(Window? owner)
    {
        OwnerCenteredMessageBox.Show(
            owner,
            UiStrings.ErrorClipboardBusy,
            UiStrings.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// 置きは必須。Flush（アプリ終了後も残す）は失敗しても貼れるので成功扱い。
    /// </summary>
    internal static bool TryCompleteSet(Action set, Action persist)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(persist);
        try
        {
            set();
        }
        catch (ExternalException)
        {
            return false;
        }

        try
        {
            persist();
        }
        catch (ExternalException)
        {
        }

        return true;
    }

    private static DataObject CreateTextData(string text)
    {
        var data = new DataObject();
        data.SetText(text);
        return data;
    }

    /// <summary>
    /// メニュー閉鎖や描画のあとで実行する。失敗時はディスパッチャを止めずに間隔を空ける。
    /// </summary>
    private static void RetryWhenIdle(Dispatcher dispatcher, Func<int, bool> attempt, int remaining)
    {
        dispatcher.BeginInvoke(
            () =>
            {
                if (attempt(remaining))
                {
                    return;
                }

                var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(RetryDelayMs),
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    RetryWhenIdle(dispatcher, attempt, remaining - 1);
                };
                timer.Start();
            },
            DispatcherPriority.Background);
    }
}
