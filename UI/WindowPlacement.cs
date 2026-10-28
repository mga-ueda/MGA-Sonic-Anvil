using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil.UI;

/// <summary>通常／F10／F9／F11／F12 の位置・サイズを settings.json に残し、切替と次回起動で戻す。</summary>
internal static class WindowPlacement
{
    public static void Capture(Window window, AppSettings settings) =>
        Capture(window, settings, MainWindowPlacementKind.Editor);

    public static void Capture(Rect bounds, bool maximized, AppSettings settings) =>
        WriteMain(bounds, maximized, settings, MainWindowPlacementKind.Editor);

    public static void Capture(Rect bounds, bool maximized, AppSettings settings, MainWindowPlacementKind kind) =>
        WriteMain(bounds, maximized, settings, kind);

    public static void CapturePlayer(Rect bounds, bool maximized, AppSettings settings) =>
        WriteMain(bounds, maximized, settings, MainWindowPlacementKind.Player);

    public static void CaptureMinimalPlayer(Rect bounds, bool maximized, AppSettings settings) =>
        WriteMain(bounds, maximized, settings, MainWindowPlacementKind.MinimalPlayer);

    public static void Capture(Window window, AppSettings settings, MainWindowPlacementKind kind)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        WriteMain(bounds, window.WindowState == WindowState.Maximized, settings, kind);
    }

    private static void WriteMain(Rect bounds, bool maximized, AppSettings settings, MainWindowPlacementKind kind)
    {
        var x = (int)Math.Round(bounds.X);
        var y = (int)Math.Round(bounds.Y);
        var width = ToStoredExtent(bounds.Width);
        var height = ToStoredExtent(bounds.Height);
        var state = maximized
            ? nameof(WindowState.Maximized)
            : nameof(WindowState.Normal);
        switch (kind)
        {
            case MainWindowPlacementKind.Player:
                settings.PlayerWindowX = x;
                settings.PlayerWindowY = y;
                settings.PlayerWindowWidth = width;
                settings.PlayerWindowHeight = height;
                settings.PlayerWindowState = state;
                return;
            case MainWindowPlacementKind.MinimalPlayer:
                settings.MinimalPlayerWindowX = x;
                settings.MinimalPlayerWindowY = y;
                settings.MinimalPlayerWindowWidth = width;
                settings.MinimalPlayerWindowHeight = height;
                settings.MinimalPlayerWindowState = state;
                return;
            case MainWindowPlacementKind.Waveform:
                settings.WaveformWindowX = x;
                settings.WaveformWindowY = y;
                settings.WaveformWindowWidth = width;
                settings.WaveformWindowHeight = height;
                settings.WaveformWindowState = state;
                return;
            case MainWindowPlacementKind.Analyzer:
                settings.AnalyzerWindowX = x;
                settings.AnalyzerWindowY = y;
                settings.AnalyzerWindowWidth = width;
                settings.AnalyzerWindowHeight = height;
                settings.AnalyzerWindowState = state;
                return;
            case MainWindowPlacementKind.WaveformFullscreen:
                settings.WaveformFullscreenX = x;
                settings.WaveformFullscreenY = y;
                settings.WaveformFullscreenWidth = width;
                settings.WaveformFullscreenHeight = height;
                return;
            case MainWindowPlacementKind.AnalyzerFullscreen:
                settings.AnalyzerFullscreenX = x;
                settings.AnalyzerFullscreenY = y;
                settings.AnalyzerFullscreenWidth = width;
                settings.AnalyzerFullscreenHeight = height;
                return;
            default:
                settings.WindowX = x;
                settings.WindowY = y;
                settings.WindowWidth = width;
                settings.WindowHeight = height;
                settings.WindowState = state;
                return;
        }
    }

    public static bool TryApply(Window window, AppSettings settings) =>
        TryApply(window, settings, MainWindowPlacementKind.Editor);

    public static bool TryApply(Window window, AppSettings settings, MainWindowPlacementKind kind)
    {
        if (!TryGetDipBounds(settings, window.MinWidth, window.MinHeight, kind, out var bounds, out var maximized))
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

    public static bool TryGetDipBounds(
        AppSettings settings,
        double minWidth,
        double minHeight,
        MainWindowPlacementKind kind,
        out Rect bounds,
        out bool maximized)
    {
        bounds = default;
        if (!TryRead(settings, minWidth, minHeight, kind, out var stored, out maximized))
        {
            maximized = false;
            return false;
        }

        bounds = new Rect(
            stored.X,
            stored.Y,
            FromStoredExtent(stored.Width),
            FromStoredExtent(stored.Height));
        return IsVisibleOnAnyScreen(bounds);
    }

    public static void CaptureSettings(Window window, AppSettings settings)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        // 設定ウィンドウは表示倍率の対象外なので等倍で記録する。
        settings.SettingsWindowX = (int)Math.Round(bounds.X);
        settings.SettingsWindowY = (int)Math.Round(bounds.Y);
        settings.SettingsWindowWidth = (int)Math.Round(bounds.Width);
        settings.SettingsWindowHeight = (int)Math.Round(bounds.Height);
        settings.SettingsWindowHasPosition = true;
    }

    public static bool TryApplySettings(Window window, AppSettings settings)
    {
        if (!TryReadSettings(settings, out var bounds, out var hasSize))
        {
            return false;
        }

        var height = hasSize
            ? Math.Max(bounds.Height, window.MinHeight)
            : window.Height;
        var placed = new Rect(bounds.X, bounds.Y, window.Width, height);
        if (!IsVisibleOnAnyScreen(placed))
        {
            return false;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placed.X;
        window.Top = placed.Y;
        if (hasSize)
        {
            window.Height = placed.Height;
        }

        return true;
    }

    public static void CaptureColorPanel(Window window, AppSettings settings)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        // 色開発パネルも表示倍率の対象外なので等倍で記録する。
        settings.ColorPanelX = (int)Math.Round(bounds.X);
        settings.ColorPanelY = (int)Math.Round(bounds.Y);
        settings.ColorPanelWidth = (int)Math.Round(bounds.Width);
        settings.ColorPanelHeight = (int)Math.Round(bounds.Height);
        settings.ColorPanelHasPosition = true;
    }

    public static bool TryApplyColorPanel(Window window, AppSettings settings)
    {
        if (!TryReadColorPanel(settings, out var bounds, out var hasSize))
        {
            return false;
        }

        var width = hasSize ? Math.Max(bounds.Width, window.MinWidth) : window.Width;
        var height = hasSize ? Math.Max(bounds.Height, window.MinHeight) : window.Height;
        var placed = new Rect(bounds.X, bounds.Y, width, height);
        if (!IsVisibleOnAnyScreen(placed))
        {
            return false;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placed.X;
        window.Top = placed.Y;
        if (hasSize)
        {
            window.Width = placed.Width;
            window.Height = placed.Height;
        }

        return true;
    }

    public static void CaptureFadeTool(Window window, AppSettings settings)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        settings.FadeToolWindowX = (int)Math.Round(bounds.X);
        settings.FadeToolWindowY = (int)Math.Round(bounds.Y);
        settings.FadeToolWindowWidth = (int)Math.Round(bounds.Width);
        settings.FadeToolWindowHeight = (int)Math.Round(bounds.Height);
        settings.FadeToolWindowHasPosition = true;
    }

    public static bool TryApplyFadeTool(Window window, AppSettings settings)
    {
        if (!TryReadFadeTool(settings, out var bounds, out var hasSize))
        {
            return false;
        }

        var width = hasSize ? Math.Max(bounds.Width, window.MinWidth) : window.Width;
        var height = hasSize ? Math.Max(bounds.Height, window.MinHeight) : window.Height;
        var placed = new Rect(bounds.X, bounds.Y, width, height);
        if (!IsVisibleOnAnyScreen(placed))
        {
            return false;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placed.X;
        window.Top = placed.Y;
        if (hasSize)
        {
            window.Width = placed.Width;
            window.Height = placed.Height;
        }

        return true;
    }

    public static bool TryReadFadeTool(AppSettings settings, out Rect bounds, out bool hasSize)
    {
        bounds = default;
        hasSize = settings.FadeToolWindowWidth > 0 && settings.FadeToolWindowHeight > 0;
        if (!settings.FadeToolWindowHasPosition)
        {
            return false;
        }

        var width = hasSize ? settings.FadeToolWindowWidth : 1;
        var height = hasSize ? settings.FadeToolWindowHeight : 1;
        bounds = new Rect(settings.FadeToolWindowX, settings.FadeToolWindowY, width, height);
        return true;
    }

    public static bool TryReadColorPanel(AppSettings settings, out Rect bounds, out bool hasSize)
    {
        bounds = default;
        hasSize = settings.ColorPanelWidth > 0 && settings.ColorPanelHeight > 0;
        if (!settings.ColorPanelHasPosition)
        {
            return false;
        }

        var width = hasSize ? settings.ColorPanelWidth : 1;
        var height = hasSize ? settings.ColorPanelHeight : 1;
        bounds = new Rect(settings.ColorPanelX, settings.ColorPanelY, width, height);
        return true;
    }

    public static void CaptureSilenceDetect(Window window, AppSettings settings)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        settings.SilenceDetectX = (int)Math.Round(bounds.X);
        settings.SilenceDetectY = (int)Math.Round(bounds.Y);
        settings.SilenceDetectWidth = (int)Math.Round(bounds.Width);
        settings.SilenceDetectHeight = (int)Math.Round(bounds.Height);
        settings.SilenceDetectHasPlacement = true;
    }

    public static bool TryApplySilenceDetect(Window window, AppSettings settings)
    {
        if (!TryReadSilenceDetect(settings, out var bounds, out var hasSize))
        {
            return false;
        }

        var width = hasSize ? Math.Max(bounds.Width, window.MinWidth) : window.Width;
        var height = hasSize ? Math.Max(bounds.Height, window.MinHeight) : window.Height;
        var placed = new Rect(bounds.X, bounds.Y, width, height);
        if (!IsVisibleOnAnyScreen(placed))
        {
            return false;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placed.X;
        window.Top = placed.Y;
        if (hasSize)
        {
            window.Width = placed.Width;
            window.Height = placed.Height;
        }

        return true;
    }

    public static bool TryReadSilenceDetect(AppSettings settings, out Rect bounds, out bool hasSize)
    {
        bounds = default;
        hasSize = settings.SilenceDetectWidth > 0 && settings.SilenceDetectHeight > 0;
        if (!settings.SilenceDetectHasPlacement)
        {
            return false;
        }

        var width = hasSize ? settings.SilenceDetectWidth : 1;
        var height = hasSize ? settings.SilenceDetectHeight : 1;
        bounds = new Rect(settings.SilenceDetectX, settings.SilenceDetectY, width, height);
        return true;
    }

    public static void CapturePitchCurve(Window window, AppSettings settings)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        settings.PitchCurveX = (int)Math.Round(bounds.X);
        settings.PitchCurveY = (int)Math.Round(bounds.Y);
        settings.PitchCurveWidth = (int)Math.Round(bounds.Width);
        settings.PitchCurveHeight = (int)Math.Round(bounds.Height);
        settings.PitchCurveHasPlacement = true;
    }

    public static bool TryApplyPitchCurve(Window window, AppSettings settings)
    {
        if (!TryReadPitchCurve(settings, out var bounds, out var hasSize))
        {
            return false;
        }

        var width = hasSize ? Math.Max(bounds.Width, window.MinWidth) : window.Width;
        var height = hasSize ? Math.Max(bounds.Height, window.MinHeight) : window.Height;
        var placed = new Rect(bounds.X, bounds.Y, width, height);
        if (!IsVisibleOnAnyScreen(placed))
        {
            return false;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placed.X;
        window.Top = placed.Y;
        if (hasSize)
        {
            window.Width = placed.Width;
            window.Height = placed.Height;
        }

        return true;
    }

    public static bool TryReadPitchCurve(AppSettings settings, out Rect bounds, out bool hasSize)
    {
        bounds = default;
        hasSize = settings.PitchCurveWidth > 0 && settings.PitchCurveHeight > 0;
        if (!settings.PitchCurveHasPlacement)
        {
            return false;
        }

        var width = hasSize ? settings.PitchCurveWidth : 1;
        var height = hasSize ? settings.PitchCurveHeight : 1;
        bounds = new Rect(settings.PitchCurveX, settings.PitchCurveY, width, height);
        return true;
    }

    public static void CenterOnOwner(Window window, Window owner)
    {
        var width = window.ActualWidth > 1 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 1 ? window.ActualHeight : window.Height;
        if (width <= 1)
        {
            width = Math.Max(window.MinWidth, 1);
        }

        if (height <= 1)
        {
            height = Math.Max(window.MinHeight, 1);
        }

        var ownerWidth = owner.ActualWidth > 1 ? owner.ActualWidth : owner.Width;
        var ownerHeight = owner.ActualHeight > 1 ? owner.ActualHeight : owner.Height;
        var ownerRect = new Rect(
            owner.Left,
            owner.Top,
            Math.Max(1, ownerWidth),
            Math.Max(1, ownerHeight));
        var bounds = CenteredOn(ownerRect, width, height);
        if (!IsVisibleOnAnyScreen(bounds))
        {
            bounds = CenteredOn(SystemParameters.WorkArea, width, height);
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.X;
        window.Top = bounds.Y;
    }

    /// <summary>アンカーがあるディスプレイの中央へ置く。位置の保存があるときは呼ばない。</summary>
    public static void CenterOnContainingMonitor(Window window, Window? anchor)
    {
        var width = window.ActualWidth > 1 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 1 ? window.ActualHeight : window.Height;
        if (width <= 1)
        {
            width = Math.Max(window.MinWidth, 1);
        }

        if (height <= 1)
        {
            height = Math.Max(window.MinHeight, 1);
        }

        var screen = anchor is not null && TryGetContainingMonitorDip(anchor, out var monitor)
            ? monitor
            : new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        var bounds = CenteredOn(screen, width, height);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.X;
        window.Top = bounds.Y;
    }

    public static bool TryReadSettings(AppSettings settings, out Rect bounds, out bool hasSize)
    {
        bounds = default;
        hasSize = settings.SettingsWindowHeight > 0;
        if (!settings.SettingsWindowHasPosition)
        {
            return false;
        }

        var width = settings.SettingsWindowWidth > 0 ? settings.SettingsWindowWidth : 1;
        var height = hasSize ? settings.SettingsWindowHeight : 1;
        bounds = new Rect(settings.SettingsWindowX, settings.SettingsWindowY, width, height);
        return true;
    }

    public static void ApplyFirstLaunch(Window window)
    {
        var width = FromStoredExtent(DesignMetrics.WindowDefaultWidth);
        var height = FromStoredExtent(DesignMetrics.WindowDefaultHeight);
        var bounds = CenteredOn(SystemParameters.WorkArea, width, height);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;
        window.Left = bounds.X;
        window.Top = bounds.Y;
    }

    /// <summary>
    /// 動画引数起動：中心位置だけ覚える（サイズは都度原寸。次回は中心を合わせて置く）。
    /// 通常／F10／F9 の Window*・Player*・MinimalPlayer* は触らない。
    /// </summary>
    public static void CaptureVideoLaunchPosition(Window window, AppSettings settings)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        CaptureVideoLaunchCenter(
            bounds.X + bounds.Width * 0.5,
            bounds.Y + bounds.Height * 0.5,
            settings);
    }

    /// <summary>動画引数起動の中心座標だけ書き、通常ウィンドウ枠は変更しない。</summary>
    public static void CaptureVideoLaunchCenter(double centerX, double centerY, AppSettings settings)
    {
        settings.VideoLaunchWindowX = (int)Math.Round(centerX);
        settings.VideoLaunchWindowY = (int)Math.Round(centerY);
        settings.VideoLaunchWindowHasPosition = true;
    }

    /// <summary>記憶した中心と外寸から、左上座標付きの配置矩形を作る。</summary>
    public static Rect VideoLaunchBoundsFromCenter(
        double centerX,
        double centerY,
        double width,
        double height) =>
        new(centerX - width * 0.5, centerY - height * 0.5, width, height);

    /// <summary>
    /// 配置矩形を作業領域内へ押し込む。はみ出す辺だけずらす。領域より大きいときは左上を揃え寸法を切る。
    /// </summary>
    public static Rect ClampRectToWorkArea(Rect bounds, Rect work)
    {
        if (work.Width < 1 || work.Height < 1)
        {
            return bounds;
        }

        var width = Math.Min(Math.Max(1, bounds.Width), work.Width);
        var height = Math.Min(Math.Max(1, bounds.Height), work.Height);
        var x = bounds.X;
        var y = bounds.Y;
        if (x + width > work.X + work.Width)
        {
            x = work.X + work.Width - width;
        }

        if (y + height > work.Y + work.Height)
        {
            y = work.Y + work.Height - height;
        }

        if (x < work.X)
        {
            x = work.X;
        }

        if (y < work.Y)
        {
            y = work.Y;
        }

        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// 中心合わせしたあと、指定ディスプレイの作業領域からはみ出さない配置にする。
    /// </summary>
    public static Rect ResolveVideoLaunchBounds(
        double centerX,
        double centerY,
        double width,
        double height,
        Rect workArea)
    {
        var fittedWidth = Math.Min(Math.Max(1, width), Math.Max(1, workArea.Width));
        var fittedHeight = Math.Min(Math.Max(1, height), Math.Max(1, workArea.Height));
        var bounds = VideoLaunchBoundsFromCenter(centerX, centerY, fittedWidth, fittedHeight);
        return ClampRectToWorkArea(bounds, workArea);
    }

    /// <summary>
    /// 動画ミニプレイヤー（F8／拡張子連動）起動中は通常／F10／F9 スロットへ書かない。
    /// プレイヤー突入前（まだ Library でない）も含む。
    /// </summary>
    public static bool UsesVideoLaunchPlacementSlot(bool videoLaunchPlacement) =>
        videoLaunchPlacement;

    /// <summary>
    /// 動画ミニの配置。クライアント領域が映像の縦横比になるよう、枠（タイトルバー等）を足した外寸にする。
    /// 対象ディスプレイの作業領域に収まるよう縮小。位置は記憶した中心に合わせ、はみ出せばその画面内へ押し込む。
    /// </summary>
    public static void ApplyVideoLaunch(Window window, AppSettings settings, int pixelWidth, int pixelHeight)
    {
        if (pixelWidth < 1 || pixelHeight < 1)
        {
            return;
        }

        var scale = GetWindowDipScale(window);
        TryGetNonClientDipSize(window, out var nonClientWidth, out var nonClientHeight);
        var work = ResolveVideoLaunchWorkArea(window, settings);
        var outer = FitVideoLaunchOuterSize(
            clientWidth: pixelWidth / scale,
            clientHeight: pixelHeight / scale,
            nonClientWidth,
            nonClientHeight,
            minOuterWidth: window.MinWidth,
            minOuterHeight: window.MinHeight,
            maxOuterWidth: work.Width,
            maxOuterHeight: work.Height);

        Rect bounds;
        if (settings.VideoLaunchWindowHasPosition)
        {
            bounds = ResolveVideoLaunchBounds(
                settings.VideoLaunchWindowX,
                settings.VideoLaunchWindowY,
                outer.Width,
                outer.Height,
                work);
            if (!IsVisibleOnAnyScreen(bounds))
            {
                bounds = CenteredOn(work, outer.Width, outer.Height);
                bounds = ClampRectToWorkArea(bounds, work);
            }
        }
        else
        {
            bounds = ClampRectToWorkArea(CenteredOn(work, outer.Width, outer.Height), work);
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Left = bounds.X;
        window.Top = bounds.Y;
    }

    /// <summary>記憶中心（または今の窓）があるモニターの作業領域。取れなければプライマリ。</summary>
    private static Rect ResolveVideoLaunchWorkArea(Window window, AppSettings settings)
    {
        if (settings.VideoLaunchWindowHasPosition
            && TryGetWorkAreaDipForPoint(
                settings.VideoLaunchWindowX,
                settings.VideoLaunchWindowY,
                out var centeredWork))
        {
            return centeredWork;
        }

        if (TryGetContainingMonitorWorkDip(window, out var windowWork))
        {
            return windowWork;
        }

        return SystemParameters.WorkArea;
    }

    /// <summary>
    /// 映像のクライアント寸法から、枠を含めた外寸を求める。縦横比を保ち、最小／最大外寸に収める。
    /// </summary>
    public static Size FitVideoLaunchOuterSize(
        double clientWidth,
        double clientHeight,
        double nonClientWidth,
        double nonClientHeight,
        double minOuterWidth,
        double minOuterHeight,
        double maxOuterWidth,
        double maxOuterHeight)
    {
        clientWidth = Math.Max(1, clientWidth);
        clientHeight = Math.Max(1, clientHeight);
        nonClientWidth = Math.Max(0, nonClientWidth);
        nonClientHeight = Math.Max(0, nonClientHeight);
        maxOuterWidth = Math.Max(1, maxOuterWidth);
        maxOuterHeight = Math.Max(1, maxOuterHeight);

        var maxClientW = Math.Max(1, maxOuterWidth - nonClientWidth);
        var maxClientH = Math.Max(1, maxOuterHeight - nonClientHeight);
        if (clientWidth > maxClientW)
        {
            var shrink = maxClientW / clientWidth;
            clientWidth = maxClientW;
            clientHeight *= shrink;
        }

        if (clientHeight > maxClientH)
        {
            var shrink = maxClientH / clientHeight;
            clientHeight = maxClientH;
            clientWidth *= shrink;
        }

        var minClientW = Math.Max(1, minOuterWidth - nonClientWidth);
        var minClientH = Math.Max(1, minOuterHeight - nonClientHeight);
        if (clientWidth < minClientW || clientHeight < minClientH)
        {
            var grow = Math.Max(minClientW / clientWidth, minClientH / clientHeight);
            clientWidth *= grow;
            clientHeight *= grow;
            if (clientWidth > maxClientW || clientHeight > maxClientH)
            {
                var shrink = Math.Min(maxClientW / clientWidth, maxClientH / clientHeight);
                clientWidth *= shrink;
                clientHeight *= shrink;
            }
        }

        return new Size(clientWidth + nonClientWidth, clientHeight + nonClientHeight);
    }

    /// <summary>ウィンドウのクライアント領域（DIP）。</summary>
    public static bool TryGetClientDipSize(Window window, out double width, out double height)
    {
        width = 0;
        height = 0;
        ArgumentNullException.ThrowIfNull(window);
        TryGetNonClientDipSize(window, out var nonClientWidth, out var nonClientHeight);
        // Width/Height は代入直後から正しい。Actual* はレイアウト待ちで古いことがある。
        var outerWidth = window.WindowState == WindowState.Normal && window.Width > 1
            ? window.Width
            : (window.ActualWidth > 1 ? window.ActualWidth : window.Width);
        var outerHeight = window.WindowState == WindowState.Normal && window.Height > 1
            ? window.Height
            : (window.ActualHeight > 1 ? window.ActualHeight : window.Height);
        if (outerWidth <= 1 || outerHeight <= 1)
        {
            return false;
        }

        width = Math.Max(1, outerWidth - nonClientWidth);
        height = Math.Max(1, outerHeight - nonClientHeight);
        return true;
    }

    /// <summary>物理ピクセル → DIP の倍率（DPI / 96）。</summary>
    public static double DipScale(Window window) => GetWindowDipScale(window);

    /// <summary>タイトルバー／枠の DIP 厚み。未表示時はシステム既定。</summary>
    public static bool TryGetNonClientDipSize(Window window, out double width, out double height)
    {
        width = 0;
        height = 0;
        ArgumentNullException.ThrowIfNull(window);
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero
                && GetWindowRect(hwnd, out var outer)
                && GetClientRect(hwnd, out var client)
                && client.Right > 0
                && client.Bottom > 0)
            {
                var scale = GetWindowDipScale(window);
                if (scale > 0)
                {
                    width = Math.Max(0, (outer.Right - outer.Left - client.Right) / scale);
                    height = Math.Max(0, (outer.Bottom - outer.Top - client.Bottom) / scale);
                    if (width > 0 || height > 0)
                    {
                        return true;
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
        }

        var frame = SystemParameters.WindowNonClientFrameThickness;
        width = Math.Max(0, frame.Left + frame.Right);
        height = Math.Max(0, frame.Top + frame.Bottom);
        return width > 0 || height > 0;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    private static double GetWindowDipScale(Window window)
    {
        try
        {
            var source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget is { } target)
            {
                var m = target.TransformToDevice;
                if (m.M11 > 0)
                {
                    return m.M11;
                }
            }
        }
        catch (InvalidOperationException)
        {
        }

        return GetPrimaryScreenScale();
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
        out bool maximized) =>
        TryRead(settings, minWidth, minHeight, MainWindowPlacementKind.Editor, out bounds, out maximized);

    public static bool TryReadPlayer(
        AppSettings settings,
        double minWidth,
        double minHeight,
        out Rect bounds,
        out bool maximized) =>
        TryRead(settings, minWidth, minHeight, MainWindowPlacementKind.Player, out bounds, out maximized);

    public static bool TryReadMinimalPlayer(
        AppSettings settings,
        double minWidth,
        double minHeight,
        out Rect bounds,
        out bool maximized) =>
        TryRead(settings, minWidth, minHeight, MainWindowPlacementKind.MinimalPlayer, out bounds, out maximized);

    public static bool TryRead(
        AppSettings settings,
        double minWidth,
        double minHeight,
        MainWindowPlacementKind kind,
        out Rect bounds,
        out bool maximized)
    {
        bounds = default;
        var state = kind switch
        {
            MainWindowPlacementKind.Player => settings.PlayerWindowState,
            MainWindowPlacementKind.MinimalPlayer => settings.MinimalPlayerWindowState,
            MainWindowPlacementKind.Waveform => settings.WaveformWindowState,
            MainWindowPlacementKind.Analyzer => settings.AnalyzerWindowState,
            MainWindowPlacementKind.WaveformFullscreen or MainWindowPlacementKind.AnalyzerFullscreen => nameof(WindowState.Normal),
            _ => settings.WindowState,
        };
        maximized = string.Equals(
            state,
            nameof(WindowState.Maximized),
            StringComparison.OrdinalIgnoreCase);
        var width = kind switch
        {
            MainWindowPlacementKind.Player => settings.PlayerWindowWidth,
            MainWindowPlacementKind.MinimalPlayer => settings.MinimalPlayerWindowWidth,
            MainWindowPlacementKind.Waveform => settings.WaveformWindowWidth,
            MainWindowPlacementKind.Analyzer => settings.AnalyzerWindowWidth,
            MainWindowPlacementKind.WaveformFullscreen => settings.WaveformFullscreenWidth,
            MainWindowPlacementKind.AnalyzerFullscreen => settings.AnalyzerFullscreenWidth,
            _ => settings.WindowWidth,
        };
        var height = kind switch
        {
            MainWindowPlacementKind.Player => settings.PlayerWindowHeight,
            MainWindowPlacementKind.MinimalPlayer => settings.MinimalPlayerWindowHeight,
            MainWindowPlacementKind.Waveform => settings.WaveformWindowHeight,
            MainWindowPlacementKind.Analyzer => settings.AnalyzerWindowHeight,
            MainWindowPlacementKind.WaveformFullscreen => settings.WaveformFullscreenHeight,
            MainWindowPlacementKind.AnalyzerFullscreen => settings.AnalyzerFullscreenHeight,
            _ => settings.WindowHeight,
        };
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        // 保存値は表示倍率で割った数。下限は今の DIP と比べる。
        if (FromStoredExtent(width) < minWidth || FromStoredExtent(height) < minHeight)
        {
            return false;
        }

        var x = kind switch
        {
            MainWindowPlacementKind.Player => settings.PlayerWindowX,
            MainWindowPlacementKind.MinimalPlayer => settings.MinimalPlayerWindowX,
            MainWindowPlacementKind.Waveform => settings.WaveformWindowX,
            MainWindowPlacementKind.Analyzer => settings.AnalyzerWindowX,
            MainWindowPlacementKind.WaveformFullscreen => settings.WaveformFullscreenX,
            MainWindowPlacementKind.AnalyzerFullscreen => settings.AnalyzerFullscreenX,
            _ => settings.WindowX,
        };
        var y = kind switch
        {
            MainWindowPlacementKind.Player => settings.PlayerWindowY,
            MainWindowPlacementKind.MinimalPlayer => settings.MinimalPlayerWindowY,
            MainWindowPlacementKind.Waveform => settings.WaveformWindowY,
            MainWindowPlacementKind.Analyzer => settings.AnalyzerWindowY,
            MainWindowPlacementKind.WaveformFullscreen => settings.WaveformFullscreenY,
            MainWindowPlacementKind.AnalyzerFullscreen => settings.AnalyzerFullscreenY,
            _ => settings.WindowY,
        };
        bounds = new Rect(x, y, width, height);
        return true;
    }

    internal static int ToStoredExtent(double scaled) =>
        UiScaleService.ToStoredExtent(scaled, CurrentScale);

    internal static double FromStoredExtent(double unscaled) =>
        UiScaleService.FromStoredExtent(unscaled, CurrentScale);

    private static double CurrentScale => AppStorage.Settings.ResolvedUiScale();

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

    /// <summary>今のウィンドウがあるモニター全面（タスクバー含む）。ブラウザの F11 と同じ。</summary>
    public static bool TryGetContainingMonitorDip(Window window, out Rect monitor)
    {
        monitor = default;
        if (!TryGetMonitorInfoForWindow(window, out var info, out var fromDevice))
        {
            return false;
        }

        monitor = DeviceRectToDip(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right,
            info.Monitor.Bottom,
            fromDevice);
        return monitor.Width > 1 && monitor.Height > 1;
    }

    /// <summary>今のウィンドウがあるモニターの作業領域（タスクバーを除く）。</summary>
    public static bool TryGetContainingMonitorWorkDip(Window window, out Rect work)
    {
        work = default;
        if (!TryGetMonitorInfoForWindow(window, out var info, out var fromDevice))
        {
            return false;
        }

        work = DeviceRectToDip(
            info.Work.Left,
            info.Work.Top,
            info.Work.Right,
            info.Work.Bottom,
            fromDevice);
        return work.Width > 1 && work.Height > 1;
    }

    /// <summary>
    /// ウィンドウがあるモニター全面（タスクバー含む）に合わせる。
    /// 物理ピクセルで置くので、最大化の作業領域に引き戻されない。
    /// </summary>
    public static bool TryCoverContainingMonitor(Window window)
    {
        if (!TryGetMonitorInfoForWindow(window, out var info, out var fromDevice))
        {
            return false;
        }

        var dip = MonitorDip(info.Monitor, fromDevice);
        if (dip.Width <= 1 || dip.Height <= 1)
        {
            return false;
        }

        window.WindowState = WindowState.Normal;
        window.Left = dip.X;
        window.Top = dip.Y;
        window.Width = dip.Width;
        window.Height = dip.Height;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            // 位置・寸法だけ合わせる。Z 順の最前面は呼び出し側の Topmost に任せる。
            // ここで HWND_TOPMOST すると WPF の Topmost=false と食い違い、他アプリを隠す。
            SetWindowPos(
                hwnd,
                HwndTop,
                info.Monitor.Left,
                info.Monitor.Top,
                Math.Max(1, info.Monitor.Right - info.Monitor.Left),
                Math.Max(1, info.Monitor.Bottom - info.Monitor.Top),
                SwpNoActivate | SwpFrameChanged);
        }

        return true;
    }

    /// <summary>
    /// シェルにフルスクリーンだと伝える。伝えないと Windows 11 ではタスクバーが最前面のまま、窓がその裏に入る。
    /// </summary>
    public static void SetShellFullscreen(Window window, bool fullscreen)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (_taskbar is null)
            {
                var type = Type.GetTypeFromCLSID(TaskbarListClsid);
                if (type is null || Activator.CreateInstance(type) is not ITaskbarList2 created)
                {
                    return;
                }

                if (created.HrInit() != 0)
                {
                    return;
                }

                _taskbar = created;
            }

            _taskbar.MarkFullscreenWindow(hwnd, fullscreen);
        }
        catch (COMException)
        {
            _taskbar = null;
        }
        catch (InvalidCastException)
        {
            _taskbar = null;
        }
    }

    private static ITaskbarList2? _taskbar;

    private static readonly Guid TaskbarListClsid = new("56FDF344-FD6D-11d0-958A-006097C9A090");

    [ComImport]
    [Guid("602D4995-B13A-429b-A66E-1935E44F4317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList2
    {
        [PreserveSig]
        int HrInit();

        [PreserveSig]
        int AddTab(IntPtr hwnd);

        [PreserveSig]
        int DeleteTab(IntPtr hwnd);

        [PreserveSig]
        int ActivateTab(IntPtr hwnd);

        [PreserveSig]
        int SetActiveAlt(IntPtr hwnd);

        [PreserveSig]
        int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
    }

    private static Rect MonitorDip(NativeRect pixels, Matrix fromDevice)
    {
        if (HasDeviceTransform(fromDevice) || Math.Abs(GetPrimaryScreenScale() - 1) < 0.02)
        {
            return DeviceRectToDip(pixels.Left, pixels.Top, pixels.Right, pixels.Bottom, fromDevice);
        }

        var scale = GetPrimaryScreenScale();
        return new Rect(
            pixels.Left / scale,
            pixels.Top / scale,
            Math.Max(1, (pixels.Right - pixels.Left) / scale),
            Math.Max(1, (pixels.Bottom - pixels.Top) / scale));
    }

    private static bool HasDeviceTransform(Matrix fromDevice) =>
        Math.Abs(fromDevice.M11 - 1) > 0.01
        || Math.Abs(fromDevice.M22 - 1) > 0.01
        || Math.Abs(fromDevice.OffsetX) > 0.01
        || Math.Abs(fromDevice.OffsetY) > 0.01;

    /// <summary>DIP 点を含む（または最寄りの）モニターの作業領域。</summary>
    public static bool TryGetWorkAreaDipForPoint(double dipX, double dipY, out Rect work)
    {
        work = default;
        if (!TryGetMonitorInfoForDipPoint(dipX, dipY, out var info, out var scale))
        {
            return false;
        }

        work = new Rect(
            info.Work.Left / scale,
            info.Work.Top / scale,
            Math.Max(1, (info.Work.Right - info.Work.Left) / scale),
            Math.Max(1, (info.Work.Bottom - info.Work.Top) / scale));
        return work.Width > 1 && work.Height > 1;
    }

    private static bool TryGetMonitorInfoForDipPoint(
        double dipX,
        double dipY,
        out MonitorInfo info,
        out double scale)
    {
        info = default;
        scale = GetPrimaryScreenScale();
        if (scale <= 0)
        {
            return false;
        }

        var point = new NativePoint
        {
            X = (int)Math.Round(dipX * scale),
            Y = (int)Math.Round(dipY * scale),
        };
        var handle = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(handle, ref info);
    }

    private static bool TryGetMonitorInfoForWindow(Window window, out MonitorInfo info, out Matrix fromDevice)
    {
        info = default;
        fromDevice = Matrix.Identity;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            hwnd = new WindowInteropHelper(window).EnsureHandle();
        }

        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var handle = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(handle, ref info))
        {
            return false;
        }

        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            fromDevice = target.TransformFromDevice;
        }

        return true;
    }

    internal static Rect DeviceRectToDip(int left, int top, int right, int bottom, Matrix fromDevice)
    {
        var a = fromDevice.Transform(new Point(left, top));
        var b = fromDevice.Transform(new Point(right, bottom));
        return new Rect(a, b);
    }

    private static double GetPrimaryScreenScale()
    {
        const int SmCxScreen = 0;
        var dipWidth = SystemParameters.PrimaryScreenWidth;
        var pixelWidth = GetSystemMetrics(SmCxScreen);
        return dipWidth > 0 && pixelWidth > 0 ? pixelWidth / dipWidth : 1d;
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
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
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint flags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}

/// <summary>メインウィンドウの配置スロット。エディタ／F10／F9／F11／F12 で別々に覚える。</summary>
internal enum MainWindowPlacementKind
{
    Editor,
    Player,
    MinimalPlayer,
    /// <summary>F11 など波形レイアウトのウィンドウ（非全画面）。</summary>
    Waveform,
    /// <summary>F12 などアナライザレイアウトのウィンドウ（非全画面）。</summary>
    Analyzer,
    WaveformFullscreen,
    AnalyzerFullscreen,
}
