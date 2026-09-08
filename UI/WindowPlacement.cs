using System.Runtime.InteropServices;
using System.Windows;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil.UI;

/// <summary>終了時の位置・サイズ・最大化を settings.json に残し、次回起動で戻す。</summary>
internal static class WindowPlacement
{
    public static void Capture(Window window, AppSettings settings)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        settings.WindowX = (int)Math.Round(bounds.X);
        settings.WindowY = (int)Math.Round(bounds.Y);
        settings.WindowWidth = (int)Math.Round(bounds.Width);
        settings.WindowHeight = (int)Math.Round(bounds.Height);
        settings.WindowState = window.WindowState == WindowState.Maximized
            ? nameof(WindowState.Maximized)
            : nameof(WindowState.Normal);
    }

    public static bool TryApply(Window window, AppSettings settings)
    {
        if (!TryRead(settings, window.MinWidth, window.MinHeight, out var bounds, out var maximized))
        {
            return false;
        }

        if (!IsVisibleOnAnyScreen(bounds))
        {
            return false;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        window.Left = bounds.X;
        window.Top = bounds.Y;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        if (maximized)
        {
            window.WindowState = WindowState.Maximized;
        }

        return true;
    }

    public static void ApplyFirstLaunch(Window window)
    {
        var width = DesignMetrics.WindowDefaultWidth;
        var height = DesignMetrics.WindowDefaultHeight;
        var bounds = CenteredOn(SystemParameters.WorkArea, width, height);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;
        window.Left = bounds.X;
        window.Top = bounds.Y;
    }

    public static Rect CenteredOn(Rect workArea, double width, double height) =>
        new(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height);

    public static bool TryRead(
        AppSettings settings,
        double minWidth,
        double minHeight,
        out Rect bounds,
        out bool maximized)
    {
        bounds = default;
        maximized = string.Equals(
            settings.WindowState,
            nameof(WindowState.Maximized),
            StringComparison.OrdinalIgnoreCase);
        if (settings.WindowWidth <= 0 || settings.WindowHeight <= 0)
        {
            return false;
        }

        if (settings.WindowWidth < minWidth || settings.WindowHeight < minHeight)
        {
            return false;
        }

        bounds = new Rect(settings.WindowX, settings.WindowY, settings.WindowWidth, settings.WindowHeight);
        return true;
    }

    /// <summary>
    /// いずれかのモニターの作業領域と重なるか。
    /// 仮想スクリーンの外接矩形だけだと、L 字配置の空白域に復元することがある。
    /// </summary>
    public static bool IsVisibleOnAnyScreen(Rect bounds)
    {
        const int margin = 40;
        var visibleArea = new Rect(
            bounds.X + margin,
            bounds.Y + margin,
            Math.Max(1, bounds.Width - margin * 2),
            Math.Max(1, bounds.Height - margin * 2));

        var scale = GetPrimaryScreenScale();
        var pixelArea = new Rect(
            visibleArea.X * scale,
            visibleArea.Y * scale,
            Math.Max(1, visibleArea.Width * scale),
            Math.Max(1, visibleArea.Height * scale));

        var intersects = false;
        _ = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr monitor, IntPtr _, ref NativeRect _, IntPtr _) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var work = new Rect(
                        info.Work.Left,
                        info.Work.Top,
                        Math.Max(1, info.Work.Right - info.Work.Left),
                        Math.Max(1, info.Work.Bottom - info.Work.Top));
                    if (work.IntersectsWith(pixelArea))
                    {
                        intersects = true;
                        return false;
                    }
                }

                return true;
            },
            IntPtr.Zero);
        return intersects;
    }

    private static double GetPrimaryScreenScale()
    {
        const int SmCxScreen = 0;
        var dipWidth = SystemParameters.PrimaryScreenWidth;
        var pixelWidth = GetSystemMetrics(SmCxScreen);
        return dipWidth > 0 && pixelWidth > 0 ? pixelWidth / dipWidth : 1d;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
