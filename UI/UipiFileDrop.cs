using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 同権限プロセスからの DnD を落とさないための UIPI メッセージフィルタと WM_DROPFILES。
/// 通常のエクスプローラー → 管理者アプリの OLE ドロップは OS 仕様で不可。
/// </summary>
internal static class UipiFileDrop
{
    private const uint WmDropFiles = 0x0233;
    private const uint WmCopyData = 0x004A;
    /// <summary>非公開だが OLE DnD に必要（WM_COPYGLOBALDATA）。</summary>
    private const uint WmCopyGlobalData = 0x0049;
    private const uint MsgfltAllow = 1;
    private const uint MsgfltAdd = 1;

    /// <summary>Main の先頭で呼ぶ。RegisterDragDrop より前にプロセス全体へ効かせる。</summary>
    public static void AllowProcessWide()
    {
        ChangeWindowMessageFilter(WmDropFiles, MsgfltAdd);
        ChangeWindowMessageFilter(WmCopyData, MsgfltAdd);
        ChangeWindowMessageFilter(WmCopyGlobalData, MsgfltAdd);
    }

    public static void Attach(Window window, Action<string[]> onFilesDropped)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(onFilesDropped);

        HwndSourceHook? hook = null;
        IntPtr hookedHwnd = IntPtr.Zero;

        void Apply()
        {
            var helper = new WindowInteropHelper(window);
            helper.EnsureHandle();
            var hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            AllowProcessWide();

            var info = new ChangeFilterStruct { CbSize = (uint)Marshal.SizeOf<ChangeFilterStruct>() };
            ChangeWindowMessageFilterEx(hwnd, WmDropFiles, MsgfltAllow, ref info);
            info = new ChangeFilterStruct { CbSize = (uint)Marshal.SizeOf<ChangeFilterStruct>() };
            ChangeWindowMessageFilterEx(hwnd, WmCopyData, MsgfltAllow, ref info);
            info = new ChangeFilterStruct { CbSize = (uint)Marshal.SizeOf<ChangeFilterStruct>() };
            ChangeWindowMessageFilterEx(hwnd, WmCopyGlobalData, MsgfltAllow, ref info);

            // OLE が UIPI で死んでも、シェルの旧パスで落とせるようにする。
            DragAcceptFiles(hwnd, true);

            var source = HwndSource.FromHwnd(hwnd);
            if (source is null)
            {
                return;
            }

            if (hook is not null && hookedHwnd != IntPtr.Zero && hookedHwnd != hwnd)
            {
                HwndSource.FromHwnd(hookedHwnd)?.RemoveHook(hook);
                hook = null;
            }

            if (hook is not null)
            {
                return;
            }

            IntPtr DropHook(IntPtr hwndHook, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                _ = hwndHook;
                _ = wParam;
                if ((uint)msg != WmDropFiles)
                {
                    return IntPtr.Zero;
                }

                handled = true;
                try
                {
                    var paths = ReadDropFiles(lParam);
                    if (paths.Length > 0)
                    {
                        onFilesDropped(paths);
                    }
                }
                finally
                {
                    DragFinish(lParam);
                }

                return IntPtr.Zero;
            }

            hook = DropHook;
            source.AddHook(hook);
            hookedHwnd = hwnd;
        }

        window.SourceInitialized += (_, _) => Apply();
        // タイトルバー変更などで HWND が張り直されてもフィルタを再適用。
        window.Loaded += (_, _) => Apply();
    }

    private static string[] ReadDropFiles(IntPtr hDrop)
    {
        var count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
        if (count == 0)
        {
            return [];
        }

        var result = new List<string>(checked((int)count));
        var sb = new StringBuilder(520);
        for (uint i = 0; i < count; i++)
        {
            sb.Clear();
            var len = DragQueryFile(hDrop, i, sb, sb.Capacity);
            if (len > 0)
            {
                result.Add(sb.ToString());
            }
        }

        return [.. result];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChangeFilterStruct
    {
        public uint CbSize;
        public uint ExtStatus;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(
        IntPtr hwnd,
        uint message,
        uint action,
        ref ChangeFilterStruct pChangeFilterStruct);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hwnd, bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(
        IntPtr hDrop,
        uint iFile,
        StringBuilder? lpszFile,
        int cch);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr hDrop);
}
