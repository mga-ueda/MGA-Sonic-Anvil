using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal enum TransportIcon
{
    PlayPause,
    Record,
    GoToStart,
    GoToEnd,
    PreviousMarker,
    NextMarker,
    JumpToTime,
    Folder,
    NewDocument,
    FadeIn,
    FadeOut,
    FadeAround,
    CrossfadeEdit,
    Normalize,
    NormalizePerRegion,
    Volume,
    /// <summary>ステータスの出力スピーカー（大型キャビネット）。</summary>
    StudioSpeaker,
    Pitch,
    TimeStretch,
    Reverse,
    Delete,
    Trim,
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    SilenceSelection,
    InsertSilence,
    DetectSilence,
    SelectAll,
    ClearSelection,
    AddMarker,
    SetLoop,
    ClearLoop,
    SetRegion,
    DeleteMarkers,
    Save,
    SaveAs,
    SaveMp3,
    SampleRate,
    BitDepth,
    Channels,
    Analysis,
    Overlay,
    Loudness,
    Center,
    History,
    ColorPalette,
    Settings,
    Help,
    Lock,
    Unlock,
    Waapi,
    PlayerMode,
    EditorMode,
    AnalyzerMaximize,
    AnalyzerRestore,
    SelectToStart,
    SelectToEnd,
    ExportWave,
    ExportByMarkers,
    ExportByRegions,
    LoopPlay,
    Restart,
    Preroll,
    CopySelectionTime,
    CopyFilePath,
    CloseTab,
    WaveformMaximize,
    WaveformRestore,
    SeekViewLeft,
    SeekViewRight,
    MinimalChrome,
    CopyFileName,
    MergeTabs,
    Bounce,
    ExportByChannels,
    DeleteAllMarkers,
    DeleteRegions,
    DeleteAllRegions,
    PreviousTrack,
    NextTrack,
    CopyAllTabTimes,
    /// <summary>マーカー旗と八分音符。オンは旗をマーカー色にする。</summary>
    RangeClick,
}

internal enum TransportCommand
{
    TogglePlayback,
    Stop,
    Record,
    JumpToTime,
    GoToStart,
    PreviousPage,
    PreviousMarker,
    NextMarker,
    NextPage,
    GoToEnd,
    TimeZoomIn,
    TimeZoomOut,
    TimeZoomMax,
    TimeZoomReset,
    AmpZoomIn,
    AmpZoomOut,
    AmpZoomMax,
    AmpZoomReset,
    FadeIn,
    FadeOut,
    FadeAround,
    CrossfadeEdit,
    Normalize,
    NormalizePerRegion,
    Volume,
    Pitch,
    TimeStretch,
    Reverse,
    Delete,
    Trim,
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    SilenceSelection,
    InsertSilence,
    DetectSilence,
    SelectAll,
    ClearSelection,
    AddMarker,
    SetLoop,
    SetRegion,
    DeleteMarkers,
    NewDocument,
    Open,
    Save,
    SaveAs,
    SaveMp3,
    SampleRate,
    BitDepth,
    Channels,
    ToggleAnalysis,
    ToggleSpectrogram,
    ToggleLoudnessView,
    CenterPlayhead,
    History,
    OpenColorPanel,
    OpenSettings,
    OpenManual,
    ToggleWaapi,
    ToggleLibraryMaximize,
    ToggleAnalyzerMaximize,
    SelectToStart,
    SelectToEnd,
    ExportWave,
    ExportByMarkers,
    ExportByRegions,
    LoopPlay,
    Restart,
    Preroll,
    CopySelectionTime,
    CopyFilePath,
    CloseTab,
    MaximizeWaveform,
    ToggleMinimalChrome,
    SeekViewLeft,
    SeekViewRight,
    CopyFileName,
    MergeTabs,
    Bounce,
    ExportByChannels,
    DeleteAllMarkers,
    DeleteRegions,
    DeleteAllRegions,
    PreviousTrack,
    NextTrack,
    CopyAllTabTimes,
    RangeClick,
}

/// <summary>メニュー／リスト用のトランスポートアイコン。ツールバーと同じ <see cref="TransportIconDrawing"/> を描く。</summary>
internal sealed class TransportIconGlyph : FrameworkElement
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(TransportIcon),
        typeof(TransportIconGlyph),
        new FrameworkPropertyMetadata(TransportIcon.PlayPause, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying),
        typeof(bool),
        typeof(TransportIconGlyph),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsLatchedProperty = DependencyProperty.Register(
        nameof(IsLatched),
        typeof(bool),
        typeof(TransportIconGlyph),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForeOverrideProperty = DependencyProperty.Register(
        nameof(ForeOverride),
        typeof(Color?),
        typeof(TransportIconGlyph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public TransportIcon Icon
    {
        get => (TransportIcon)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public bool IsLatched
    {
        get => (bool)GetValue(IsLatchedProperty);
        set => SetValue(IsLatchedProperty, value);
    }

    /// <summary>Keep Target の鍵など、既定前景以外の色。</summary>
    public Color? ForeOverride
    {
        get => (Color?)GetValue(ForeOverrideProperty);
        set => SetValue(ForeOverrideProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var fore = ForeOverride ?? Theme.Get("PrimaryForeBrush");
        TransportIconDrawing.Draw(
            dc,
            Icon,
            bounds,
            fore,
            IsPlaying,
            UiThemeService.Current,
            accent: true,
            latched: IsLatched);
    }
}

internal sealed class TransportIconButton : Button
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(TransportIcon),
        typeof(TransportIconButton),
        new FrameworkPropertyMetadata(TransportIcon.PlayPause, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying),
        typeof(bool),
        typeof(TransportIconButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsLatchedProperty = DependencyProperty.Register(
        nameof(IsLatched),
        typeof(bool),
        typeof(TransportIconButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public TransportCommand CommandKind { get; init; }

    public TransportIcon Icon
    {
        get => (TransportIcon)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public bool IsLatched
    {
        get => (bool)GetValue(IsLatchedProperty);
        set => SetValue(IsLatchedProperty, value);
    }

    public bool QuietChrome { get; set; }

    /// <summary>プレイヤー中はスロット塗りをせず、ジャケットウォッシュを透かす。</summary>
    public bool WashThrough { get; set; }

    public Color? IconForeOverride { get; set; }

    public TransportIconButton()
    {
        Width = DesignMetrics.TransportButtonSide;
        Height = DesignMetrics.TransportButtonSide;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(Button));
        SnapsToDevicePixels = true;
        TransportHover.Attach(this);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (Icon == TransportIcon.Waapi)
        {
            DrawWaapiToggle(dc, bounds);
            return;
        }

        // スロットは塗らない。グループベール／バー背景を透かして見せ、ホバーだけ乗せる。
        TransportChrome.Paint(
            dc,
            bounds,
            IsEnabled,
            IsMouseOver,
            IsPressed,
            hoverBounds: QuietChrome && Icon == TransportIcon.Folder
                ? TransportIconDrawing.FolderHoverBounds(bounds)
                : null);
        var fore = IconForeOverride ?? TransportChrome.Fore(IsEnabled);
        if (Icon == TransportIcon.Record && IsLatched)
        {
            fore = Theme.Get("RecordLatchForeBrush");
        }

        // 解析など「今オフ」のトグルは弱い色。F9–F12 のモード切替は
        // 未選択でも押せるので、無効色にはしない（選択は IsLatched／アイコン差し替えで示す）。
        if (Icon is TransportIcon.Analysis
            or TransportIcon.Overlay
            or TransportIcon.Loudness)
        {
            fore = IsLatched ? fore : TransportChrome.Fore(false);
        }

        TransportIconDrawing.Draw(
            dc,
            Icon,
            bounds,
            fore,
            IsPlaying,
            UiThemeService.Current,
            accent: IsEnabled,
            latched: IsLatched);
    }

    private void DrawWaapiToggle(DrawingContext dc, Rect bounds)
    {
        var on = IsLatched;
        var hover = IsEnabled && IsMouseOver && !IsPressed;
        var back = WaapiChipBack(on, hover);
        var fore = on
            ? Theme.Get("WaapiToggleOnForeBrush")
            : Theme.Get("WaapiToggleOffForeBrush");

        var slotKey = QuietChrome ? "ProjectBarBackBrush" : "TransportBackBrush";
        if (!WashThrough)
        {
            dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get(slotKey)), null, bounds);
        }

        var chip = new Rect(2, 6, Math.Max(1, bounds.Width - 4), Math.Max(1, bounds.Height - 12));
        dc.DrawRoundedRectangle(
            WpfControlHelpers.FrozenBrush(back),
            null,
            chip,
            DesignMetrics.ControlCornerRadius,
            DesignMetrics.ControlCornerRadius);

        var formatted = new FormattedText(
            UiStrings.WaapiTitle,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            10,
            WpfControlHelpers.FrozenBrush(fore),
            UiDpi.Get(this).PixelsPerDip);
        dc.DrawText(
            formatted,
            new Point(
                (bounds.Width - formatted.Width) * 0.5,
                (bounds.Height - formatted.Height) * 0.5));
    }

    private Color WaapiChipBack(bool on, bool hover)
    {
        if (WashThrough)
        {
            return PlayerChrome.Get(on
                ? hover ? "PlayerWaapiToggleOnHoverBackBrush" : "PlayerWaapiToggleOnBackBrush"
                : hover ? "PlayerWaapiToggleOffHoverBackBrush" : "PlayerWaapiToggleOffBackBrush");
        }

        return on
            ? Theme.Get(hover ? "WaapiToggleOnHoverBackBrush" : "WaapiToggleOnBackBrush")
            : Theme.Get(hover ? "WaapiToggleOffHoverBackBrush" : "WaapiToggleOffBackBrush");
    }

    private Color ChromeHole(string backKey)
    {
        if (IsEnabled && (IsMouseOver || IsPressed))
        {
            return Theme.Get(IsPressed ? "TransportPressedBackBrush" : "TransportHoverBackBrush");
        }

        if (WashThrough)
        {
            return Colors.Transparent;
        }

        return Theme.Get(backKey);
    }

}

/// <summary>
/// トランスポート／ツールバーのアイコン描画。アイコンを追加・変更するときは次の方針に従う。
/// <list type="bullet">
/// <item>座標は 34×36 のデザイン枠で描く。線幅は 1.8、端と角は丸める。等倍（約 17px）でも判別できる形にする。</item>
/// <item>線は前景色（通常は白）。色は塗りで使うのを基本とし、2色目は意味を補う場合だけ入れる。無理に色を付けない。</item>
/// <item>色はアプリ内の同じ意味の要素と揃え、テーマキーから描画時に読む（ユーザーが色を変えると追従する）。
///   マーカー＝MarkerBrush（オレンジ）、リージョン＝RegionTimelineBrush（緑）、ループ＝SampleLoopTimelineBrush（青）、
///   再生＝PlayheadBrush（シアン）、録音＝RecordLatchForeBrush（赤）、削除＝ClearButtonBackBrush（赤）、
///   選択範囲＝WaveSelectionFillBrush。</item>
/// <item>線に色を付けるのは、シークバー（再生ヘッド）のように実物が色線であるものに限る。</item>
/// <item>アプリ内に実物がある形は合わせる。マーカー旗は <see cref="DrawMarkerFlag"/>（竿は白、旗は塗り）。</item>
/// <item>削除系は元の図形に <see cref="DrawDeleteBadge"/>（赤い丸に白い ×）を右下に添える。</item>
/// <item>状態はなるべく色で示す。再生中の停止は色なしの白、録音中は白線を外して赤く点灯させる。</item>
/// <item>色設定だけは例外として固定の赤・緑・青の 3 色を半透明で重ね、白で縁取る。</item>
/// <item><c>accent</c> が false（無効時など）は単色の線画に戻る。色付きの塗りは <see cref="AccentFill"/> で作り、無効時は null にする。</item>
/// </list>
/// </summary>
internal static class TransportIconDrawing
{
    public static void Draw(
        DrawingContext dc,
        TransportIcon icon,
        Rect bounds,
        Color fore,
        bool isPlaying,
        UiTheme theme,
        bool accent = false,
        bool latched = false)
    {
        // 座標は常に 34×36。小さい枠でも同じデザインを縮小して中央に置く。
        const double designW = 34d;
        const double designH = 36d;
        var scale = Math.Min(bounds.Width / designW, bounds.Height / designH);
        if (scale <= 0d)
        {
            return;
        }

        dc.PushTransform(new TranslateTransform(
            (bounds.Width - designW * scale) * 0.5,
            (bounds.Height - designH * scale) * 0.5));
        dc.PushTransform(new ScaleTransform(scale, scale));

        var brush = WpfControlHelpers.FrozenBrush(fore);
        var pen = new Pen(brush, 1.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        _ = theme;
        Brush? fill = null;
        var stroke = pen;
        var cx = designW * 0.5;
        var cy = designH * 0.5;
        // 2色目はアプリ内の対応色。ユーザーの色設定に追従させるため描画時に読む。
        // 基本は「白い線＋色の塗り」。再生ヘッドだけはシークバーと同じく色線で描く。
        var markerBrush = WpfControlHelpers.FrozenBrush(AccentColor("MarkerBrush", fore, accent));
        var markerPen = pen;
        var regionFill = AccentFill("RegionTimelineBrush", accent, 0xD2);
        var loopFill = AccentFill("SampleLoopTimelineBrush", accent, 0xB4);
        var playFill = AccentFill("PlayheadBrush", accent, 0xFF);
        var playheadPen = OutlinePen(pen, WpfControlHelpers.FrozenBrush(AccentColor("PlayheadBrush", fore, accent)));
        var deleteFill = AccentFill("ClearButtonBackBrush", accent, 0xFF);
        var recordFill = accent || latched
            ? WpfControlHelpers.FrozenBrush(AccentColor("RecordLatchForeBrush", fore, true))
            : null;
        Brush? selectionFill = accent
            ? WpfControlHelpers.FrozenBrush(AccentColor("WaveSelectionFillBrush", fore, accent))
            : null;

        switch (icon)
        {
            case TransportIcon.PlayPause:
                if (isPlaying)
                {
                    // 停止表示は色なし。
                    dc.DrawRectangle(fill, stroke, new Rect(cx - 5, cy - 6, 10, 12));
                }
                else
                {
                    DrawPlayTriangle(dc, pen, playFill, cx - 5, cx + 8, cy, 9);
                }

                break;
            case TransportIcon.Record:
                if (latched && recordFill is not null)
                {
                    // 録音中は白線を外して赤で点灯させ、外側の赤リングで録音状態を示す。
                    dc.DrawEllipse(recordFill, null, new Point(cx, cy), 6.4, 6.4);
                    dc.DrawEllipse(null, OutlinePen(pen, recordFill), new Point(cx, cy), 9.6, 9.6);
                }
                else
                {
                    dc.DrawEllipse(recordFill, stroke, new Point(cx, cy), 7.2, 7.2);
                }

                break;
            case TransportIcon.GoToStart:
                dc.DrawLine(pen, new Point(cx - 7, cy - 8), new Point(cx - 7, cy + 8));
                DrawChevron(dc, pen, cx + 2, cy, left: true);
                break;
            case TransportIcon.GoToEnd:
                dc.DrawLine(pen, new Point(cx + 7, cy - 8), new Point(cx + 7, cy + 8));
                DrawChevron(dc, pen, cx - 2, cy, left: false);
                break;
            case TransportIcon.PreviousMarker:
                DrawJumpPoint(dc, pen, cx, cy, previous: true);
                break;
            case TransportIcon.NextMarker:
                DrawJumpPoint(dc, pen, cx, cy, previous: false);
                break;
            case TransportIcon.Undo:
                DrawUndoRedoArrow(dc, pen, cx, cy, redo: false);
                break;
            case TransportIcon.Redo:
                DrawUndoRedoArrow(dc, pen, cx, cy, redo: true);
                break;
            case TransportIcon.Cut:
                DrawCut(dc, pen, cx, cy);
                break;
            case TransportIcon.Copy:
                DrawCopy(dc, pen, cx, cy);
                break;
            case TransportIcon.Paste:
                DrawPaste(dc, pen, cx, cy);
                break;
            case TransportIcon.Trim:
                DrawTrim(dc, pen, selectionFill, cx, cy);
                break;
            case TransportIcon.SilenceSelection:
                DrawSilenceSelection(dc, pen, cx, cy);
                break;
            case TransportIcon.InsertSilence:
                DrawInsertSilence(dc, pen, cx, cy);
                break;
            case TransportIcon.DetectSilence:
                DrawDetectSilence(dc, pen, cx, cy);
                break;
            case TransportIcon.SelectAll:
                dc.DrawRectangle(selectionFill, DashedPen(pen), new Rect(cx - 10, cy - 8, 20, 16));
                DrawMiniWave(dc, pen, cx, cy, 6);
                break;
            case TransportIcon.ClearSelection:
                dc.DrawRectangle(null, DashedPen(pen), new Rect(cx - 10, cy - 8, 20, 16));
                dc.DrawLine(pen, new Point(cx - 4, cy - 4), new Point(cx + 4, cy + 4));
                dc.DrawLine(pen, new Point(cx + 4, cy - 4), new Point(cx - 4, cy + 4));
                break;
            case TransportIcon.CrossfadeEdit:
                dc.DrawLine(pen, new Point(cx - 9, cy - 6), new Point(cx, cy + 7));
                dc.DrawLine(pen, new Point(cx, cy + 7), new Point(cx + 9, cy - 6));
                dc.DrawLine(pen, new Point(cx - 9, cy + 6), new Point(cx, cy - 7));
                dc.DrawLine(pen, new Point(cx, cy - 7), new Point(cx + 9, cy + 6));
                break;
            case TransportIcon.NormalizePerRegion:
                if (regionFill is not null)
                {
                    dc.DrawRectangle(regionFill, null, new Rect(cx - 9, cy - 8, 18, 16));
                }

                dc.DrawLine(pen, new Point(cx - 9, cy - 8), new Point(cx - 9, cy + 8));
                dc.DrawLine(pen, new Point(cx + 9, cy - 8), new Point(cx + 9, cy + 8));
                dc.DrawLine(pen, new Point(cx - 5, cy + 5), new Point(cx - 5, cy - 1));
                dc.DrawLine(pen, new Point(cx, cy + 5), new Point(cx, cy - 6));
                dc.DrawLine(pen, new Point(cx + 5, cy + 5), new Point(cx + 5, cy));
                dc.DrawLine(pen, new Point(cx - 7, cy - 7), new Point(cx + 7, cy - 7));
                break;
            case TransportIcon.ClearLoop:
                DrawCassetteAutoReverse(dc, pen, loopFill);
                DrawDeleteBadge(dc, pen, deleteFill, cx + 6, cy + 5);
                break;
            case TransportIcon.DeleteMarkers:
                DrawMarkerFlag(dc, markerBrush, markerPen, cx - 6, cy - 9, cy + 9, 9);
                DrawDeleteBadge(dc, pen, deleteFill, cx + 5.5, cy + 5);
                break;
            case TransportIcon.JumpToTime:
                // 時計 → 再生ヘッド。編集履歴（巻き戻し矢印の時計）と区別する。
                dc.DrawEllipse(null, pen, new Point(cx - 4, cy), 6, 6);
                dc.DrawLine(pen, new Point(cx - 4, cy), new Point(cx - 4, cy - 3.5));
                dc.DrawLine(pen, new Point(cx - 4, cy), new Point(cx - 1.5, cy + 1.5));
                dc.DrawLine(pen, new Point(cx + 4, cy), new Point(cx + 7, cy));
                dc.DrawLine(pen, new Point(cx + 5, cy - 2.5), new Point(cx + 7.5, cy));
                dc.DrawLine(pen, new Point(cx + 5, cy + 2.5), new Point(cx + 7.5, cy));
                dc.DrawLine(playheadPen, new Point(cx + 10, cy - 9), new Point(cx + 10, cy + 9));
                break;
            case TransportIcon.SampleRate:
                DrawFormatGlyph(dc, pen, brush, cx, cy, "Hz");
                break;
            case TransportIcon.BitDepth:
                DrawFormatGlyph(dc, pen, brush, cx, cy, "bit");
                break;
            case TransportIcon.Channels:
                dc.DrawLine(pen, new Point(cx - 8, cy - 4), new Point(cx - 2, cy - 8));
                dc.DrawLine(pen, new Point(cx - 2, cy - 8), new Point(cx + 2, cy));
                dc.DrawLine(pen, new Point(cx + 2, cy), new Point(cx + 8, cy - 5));
                dc.DrawLine(pen, new Point(cx - 8, cy + 5), new Point(cx - 2, cy + 1));
                dc.DrawLine(pen, new Point(cx - 2, cy + 1), new Point(cx + 2, cy + 8));
                dc.DrawLine(pen, new Point(cx + 2, cy + 8), new Point(cx + 8, cy + 3));
                break;
            case TransportIcon.Waapi:
                DrawWaapiGlyph(dc, pen, brush, cx, cy);
                break;
            case TransportIcon.Folder:
                DrawFolder(dc, pen, cx, cy);
                break;
            case TransportIcon.NewDocument:
                DrawNewDocument(dc, pen, cx, cy);
                break;
            case TransportIcon.Settings:
                DrawGear(dc, pen, cx, cy);
                break;
            case TransportIcon.Help:
                DrawHelp(dc, pen, brush, cx, cy);
                break;
            case TransportIcon.FadeIn:
                FadeCurveIcons.DrawCurve(dc, FadeShape.SCurve, isFadeIn: true, new Rect(cx - 9, cy - 8, 18, 16), pen);
                break;
            case TransportIcon.FadeOut:
                FadeCurveIcons.DrawCurve(dc, FadeShape.SCurve, isFadeIn: false, new Rect(cx - 9, cy - 8, 18, 16), pen);
                break;
            case TransportIcon.FadeAround:
                // 再生ヘッドの前でフェードアウト、後ろでフェードイン。
                FadeCurveIcons.DrawCurve(dc, FadeShape.SCurve, isFadeIn: false, new Rect(cx - 10, cy - 7, 9, 14), pen);
                FadeCurveIcons.DrawCurve(dc, FadeShape.SCurve, isFadeIn: true, new Rect(cx + 1, cy - 7, 9, 14), pen);
                dc.DrawLine(playheadPen, new Point(cx, cy - 9), new Point(cx, cy + 9));
                break;
            case TransportIcon.Volume:
                dc.DrawLine(pen, new Point(cx - 8, cy - 2), new Point(cx - 3, cy - 2));
                dc.DrawLine(pen, new Point(cx - 3, cy - 2), new Point(cx + 2, cy - 7));
                dc.DrawLine(pen, new Point(cx + 2, cy - 7), new Point(cx + 2, cy + 7));
                dc.DrawLine(pen, new Point(cx + 2, cy + 7), new Point(cx - 3, cy + 2));
                dc.DrawLine(pen, new Point(cx - 3, cy + 2), new Point(cx - 8, cy + 2));
                dc.DrawLine(pen, new Point(cx - 8, cy + 2), new Point(cx - 8, cy - 2));
                dc.DrawLine(pen, new Point(cx + 5, cy - 4), new Point(cx + 8, cy));
                dc.DrawLine(pen, new Point(cx + 8, cy), new Point(cx + 5, cy + 4));
                break;
            case TransportIcon.StudioSpeaker:
                DrawStudioSpeaker(dc, pen, cx, cy);
                break;
            case TransportIcon.Pitch:
                dc.DrawEllipse(null, pen, new Point(cx - 3.5, cy + 6), 3.2, 2.2);
                dc.DrawLine(pen, new Point(cx - 0.3, cy + 6), new Point(cx - 0.3, cy - 8));
                dc.DrawLine(pen, new Point(cx - 0.3, cy - 8), new Point(cx + 6.5, cy - 5));
                break;
            case TransportIcon.TimeStretch:
                dc.DrawLine(pen, new Point(cx - 9, cy), new Point(cx + 9, cy));
                dc.DrawLine(pen, new Point(cx - 9, cy), new Point(cx - 5, cy - 4));
                dc.DrawLine(pen, new Point(cx - 9, cy), new Point(cx - 5, cy + 4));
                dc.DrawLine(pen, new Point(cx + 9, cy), new Point(cx + 5, cy - 4));
                dc.DrawLine(pen, new Point(cx + 9, cy), new Point(cx + 5, cy + 4));
                dc.DrawLine(pen, new Point(cx - 3, cy - 7), new Point(cx + 3, cy - 7));
                dc.DrawLine(pen, new Point(cx - 3, cy + 7), new Point(cx + 3, cy + 7));
                break;
            case TransportIcon.Reverse:
                DrawChevron(dc, pen, cx - 4.5, cy, left: true);
                DrawChevron(dc, pen, cx + 4.5, cy, left: true);
                break;
            case TransportIcon.AddMarker:
                DrawMarkerFlag(dc, markerBrush, markerPen, cx - 6, cy - 9, cy + 9, 9);
                dc.DrawLine(pen, new Point(cx + 5.5, cy + 1), new Point(cx + 5.5, cy + 9));
                dc.DrawLine(pen, new Point(cx + 1.5, cy + 5), new Point(cx + 9.5, cy + 5));
                break;
            case TransportIcon.RangeClick:
                // 左にマーカー旗、右に八分音符。オンのときだけ旗をマーカー色にする。
                DrawMarkerFlag(
                    dc,
                    latched ? markerBrush : brush,
                    pen,
                    cx - 10,
                    cy - 8,
                    cy + 8,
                    7);
                DrawEighthNote(dc, pen, cx + 3.2, cy + 5);
                break;
            case TransportIcon.SetLoop:
                DrawCassetteAutoReverse(dc, pen, loopFill);
                break;
            case TransportIcon.SetRegion:
                DrawRegionBrackets(dc, pen, regionFill, cx - 7, cx + 7, cy - 8, cy + 8, 4);
                break;
            case TransportIcon.Analysis:
                DrawSpectrumBars(dc, pen, cx, cy);
                break;
            case TransportIcon.Overlay:
                DrawSpectrumBars(dc, pen, cx, cy);
                DrawWaveformOverlay(dc, pen, cx, cy);
                break;
            case TransportIcon.Loudness:
                DrawLoudnessCurve(dc, pen, cx, cy);
                break;
            case TransportIcon.Center:
                dc.DrawLine(playheadPen, new Point(cx, cy - 9), new Point(cx, cy - 4));
                dc.DrawLine(playheadPen, new Point(cx, cy + 4), new Point(cx, cy + 9));
                dc.DrawLine(pen, new Point(cx - 8, cy), new Point(cx - 3, cy));
                dc.DrawLine(pen, new Point(cx + 3, cy), new Point(cx + 8, cy));
                dc.DrawEllipse(null, pen, new Point(cx, cy), 4.5, 4.5);
                break;
            case TransportIcon.History:
                DrawHistoryClock(dc, pen, cx, cy);
                break;
            case TransportIcon.ColorPalette:
                DrawColorPalette(dc, pen, brush, cx, cy, accent);
                break;
            case TransportIcon.Normalize:
                dc.DrawLine(pen, new Point(cx - 7, cy + 6), new Point(cx - 7, cy - 2));
                dc.DrawLine(pen, new Point(cx - 2, cy + 6), new Point(cx - 2, cy - 7));
                dc.DrawLine(pen, new Point(cx + 3, cy + 6), new Point(cx + 3, cy));
                dc.DrawLine(pen, new Point(cx + 8, cy + 6), new Point(cx + 8, cy - 5));
                dc.DrawLine(pen, new Point(cx - 9, cy - 8), new Point(cx + 9, cy - 8));
                dc.DrawLine(pen, new Point(cx + 5, cy - 10), new Point(cx + 9, cy - 8));
                dc.DrawLine(pen, new Point(cx + 5, cy - 6), new Point(cx + 9, cy - 8));
                break;
            case TransportIcon.Delete:
                dc.DrawLine(pen, new Point(cx - 8, cy - 5), new Point(cx + 8, cy - 5));
                dc.DrawLine(pen, new Point(cx - 3, cy - 8), new Point(cx + 3, cy - 8));
                dc.DrawLine(pen, new Point(cx - 6, cy - 5), new Point(cx - 5, cy + 8));
                dc.DrawLine(pen, new Point(cx - 5, cy + 8), new Point(cx + 5, cy + 8));
                dc.DrawLine(pen, new Point(cx + 5, cy + 8), new Point(cx + 6, cy - 5));
                dc.DrawLine(pen, new Point(cx - 3, cy - 2), new Point(cx - 3, cy + 5));
                dc.DrawLine(pen, new Point(cx, cy - 2), new Point(cx, cy + 5));
                dc.DrawLine(pen, new Point(cx + 3, cy - 2), new Point(cx + 3, cy + 5));
                break;
            case TransportIcon.Save:
                dc.DrawLine(pen, new Point(cx, cy - 11), new Point(cx, cy + 2));
                dc.DrawLine(pen, new Point(cx - 5, cy - 2), new Point(cx, cy + 3));
                dc.DrawLine(pen, new Point(cx + 5, cy - 2), new Point(cx, cy + 3));
                dc.DrawLine(pen, new Point(cx - 8, cy + 8), new Point(cx + 8, cy + 8));
                break;
            case TransportIcon.SaveMp3:
                DrawSaveMp3(dc, pen, cx, cy);
                break;
            case TransportIcon.SaveAs:
                dc.DrawLine(pen, new Point(cx - 1, cy - 11), new Point(cx - 1, cy));
                dc.DrawLine(pen, new Point(cx - 6, cy - 4), new Point(cx - 1, cy + 1));
                dc.DrawLine(pen, new Point(cx + 4, cy - 4), new Point(cx - 1, cy + 1));
                dc.DrawLine(pen, new Point(cx - 8, cy + 8), new Point(cx + 8, cy + 8));
                dc.DrawLine(pen, new Point(cx + 4, cy - 10), new Point(cx + 9, cy - 10));
                dc.DrawLine(pen, new Point(cx + 4, cy - 6), new Point(cx + 9, cy - 6));
                break;
            case TransportIcon.Lock:
                DrawPadlock(dc, stroke, brush, open: false, cx, cy);
                break;
            case TransportIcon.Unlock:
                DrawPadlock(dc, stroke, brush, open: true, cx, cy);
                break;
            case TransportIcon.PlayerMode:
                DrawPlayerMode(dc, pen, cx, cy);
                break;
            case TransportIcon.EditorMode:
                DrawEditorMode(dc, pen, cx, cy);
                break;
            case TransportIcon.AnalyzerMaximize:
                DrawAnalyzerMaximize(dc, pen, cx, cy);
                break;
            case TransportIcon.AnalyzerRestore:
                DrawAnalyzerRestore(dc, pen, cx, cy);
                break;
            case TransportIcon.MinimalChrome:
                DrawMinimalChrome(dc, pen, cx, cy);
                break;
            case TransportIcon.CopyFileName:
                DrawNewDocument(dc, pen, cx - 1, cy);
                dc.DrawLine(pen, new Point(cx + 2, cy + 2), new Point(cx + 9, cy + 2));
                dc.DrawLine(pen, new Point(cx + 2, cy + 6), new Point(cx + 9, cy + 6));
                break;
            case TransportIcon.MergeTabs:
                // 2 ファイル → 1 ファイルへまとめる（ミックスダウン）。
                dc.DrawRectangle(null, pen, new Rect(cx - 10, cy - 9, 6, 6));
                dc.DrawRectangle(null, pen, new Rect(cx - 10, cy + 3, 6, 6));
                dc.DrawLine(pen, new Point(cx - 3, cy - 6), new Point(cx + 1, cy));
                dc.DrawLine(pen, new Point(cx - 3, cy + 6), new Point(cx + 1, cy));
                dc.DrawRectangle(null, pen, new Rect(cx + 3, cy - 6, 7, 12));
                break;
            case TransportIcon.Bounce:
                // 選択ファイルを波形へ焼き込む（並んだタブ＋下矢印）。
                dc.DrawRectangle(null, pen, new Rect(cx - 10, cy - 9, 7, 8));
                dc.DrawRectangle(null, pen, new Rect(cx - 3.5, cy - 9, 7, 8));
                dc.DrawRectangle(null, pen, new Rect(cx + 3, cy - 9, 7, 8));
                dc.DrawLine(pen, new Point(cx, cy + 1), new Point(cx, cy + 8));
                dc.DrawLine(pen, new Point(cx, cy + 8), new Point(cx - 3.5, cy + 4.5));
                dc.DrawLine(pen, new Point(cx, cy + 8), new Point(cx + 3.5, cy + 4.5));
                dc.DrawLine(pen, new Point(cx - 8, cy + 9), new Point(cx + 8, cy + 9));
                break;
            case TransportIcon.ExportByChannels:
                dc.DrawLine(pen, new Point(cx - 8, cy - 6), new Point(cx - 3, cy - 9));
                dc.DrawLine(pen, new Point(cx - 3, cy - 9), new Point(cx + 1, cy - 2));
                dc.DrawLine(pen, new Point(cx + 1, cy - 2), new Point(cx + 8, cy - 6));
                dc.DrawLine(pen, new Point(cx - 8, cy + 4), new Point(cx - 3, cy + 1));
                dc.DrawLine(pen, new Point(cx - 3, cy + 1), new Point(cx + 1, cy + 8));
                dc.DrawLine(pen, new Point(cx + 1, cy + 8), new Point(cx + 8, cy + 4));
                dc.DrawLine(pen, new Point(cx - 9, cy + 8), new Point(cx + 9, cy + 8));
                break;
            case TransportIcon.DeleteAllMarkers:
                DrawMarkerFlag(dc, markerBrush, markerPen, cx - 9, cy - 9, cy + 9, 6);
                DrawMarkerFlag(dc, markerBrush, markerPen, cx - 1, cy - 9, cy + 9, 6);
                DrawDeleteBadge(dc, pen, deleteFill, cx + 6.5, cy + 5);
                break;
            case TransportIcon.DeleteRegions:
                DrawRegionBrackets(dc, pen, regionFill, cx - 10, cx + 3, cy - 8, cy + 8, 3.5);
                DrawDeleteBadge(dc, pen, deleteFill, cx + 6.5, cy + 5);
                break;
            case TransportIcon.DeleteAllRegions:
                DrawRegionBrackets(dc, pen, regionFill, cx - 10, cx - 4, cy - 8, cy + 8, 2.5);
                DrawRegionBrackets(dc, pen, regionFill, cx - 2, cx + 4, cy - 8, cy + 8, 2.5);
                DrawDeleteBadge(dc, pen, deleteFill, cx + 7, cy + 5);
                break;
            case TransportIcon.PreviousTrack:
                // ⏮。先頭へ（|<）・画面左端へ（枠内の <）と区別する。
                dc.DrawLine(pen, new Point(cx - 9, cy - 7), new Point(cx - 9, cy + 7));
                DrawTriangle(dc, pen, cx - 6.5, cx + 0.5, cy, 6.5);
                DrawTriangle(dc, pen, cx + 1.5, cx + 8.5, cy, 6.5);
                break;
            case TransportIcon.NextTrack:
                dc.DrawLine(pen, new Point(cx + 9, cy - 7), new Point(cx + 9, cy + 7));
                DrawTriangle(dc, pen, cx + 6.5, cx - 0.5, cy, 6.5);
                DrawTriangle(dc, pen, cx - 1.5, cx - 8.5, cy, 6.5);
                break;
            case TransportIcon.CopyAllTabTimes:
                dc.DrawEllipse(null, pen, new Point(cx - 2, cy - 1), 6.5, 6.5);
                dc.DrawLine(pen, new Point(cx - 2, cy - 1), new Point(cx - 2, cy - 5));
                dc.DrawLine(pen, new Point(cx - 2, cy - 1), new Point(cx + 2, cy + 1));
                dc.DrawRectangle(null, pen, new Rect(cx + 4, cy - 2, 6, 10));
                break;
            case TransportIcon.SelectToStart:
                dc.DrawLine(pen, new Point(cx - 8, cy - 8), new Point(cx - 8, cy + 8));
                dc.DrawRectangle(selectionFill, pen, new Rect(cx - 4, cy - 6, 12, 12));
                DrawChevron(dc, pen, cx + 1, cy, left: true);
                break;
            case TransportIcon.SelectToEnd:
                dc.DrawLine(pen, new Point(cx + 8, cy - 8), new Point(cx + 8, cy + 8));
                dc.DrawRectangle(selectionFill, pen, new Rect(cx - 8, cy - 6, 12, 12));
                DrawChevron(dc, pen, cx - 1, cy, left: false);
                break;
            case TransportIcon.ExportWave:
                DrawExportWave(dc, pen, cx, cy);
                break;
            case TransportIcon.ExportByMarkers:
                // マーカー位置で 2 つに分割。
                DrawMarkerFlag(dc, markerBrush, markerPen, cx, cy - 9, cy + 9, 7);
                dc.DrawRectangle(null, pen, new Rect(cx - 10, cy + 2, 7, 6));
                dc.DrawRectangle(null, pen, new Rect(cx + 3, cy + 2, 7, 6));
                break;
            case TransportIcon.ExportByRegions:
                DrawRegionBrackets(dc, pen, regionFill, cx - 10, cx - 2, cy - 8, cy + 2, 2.5);
                DrawRegionBrackets(dc, pen, regionFill, cx + 2, cx + 10, cy - 8, cy + 2, 2.5);
                dc.DrawLine(pen, new Point(cx - 10, cy + 7), new Point(cx - 2, cy + 7));
                dc.DrawLine(pen, new Point(cx + 2, cy + 7), new Point(cx + 10, cy + 7));
                break;
            case TransportIcon.LoopPlay:
                DrawCassetteAutoReverse(dc, pen, loopFill);
                DrawPlayTriangle(dc, pen, playFill, cx - 2.5, cx + 4.5, cy, 4);
                break;
            case TransportIcon.Restart:
                DrawRestart(dc, pen, playFill, cx, cy);
                break;
            case TransportIcon.Preroll:
                DrawChevron(dc, pen, cx - 5, cy, left: true);
                DrawChevron(dc, pen, cx - 1, cy, left: true);
                DrawPlayTriangle(dc, pen, playFill, cx + 3, cx + 10, cy, 6);
                break;
            case TransportIcon.CopySelectionTime:
                dc.DrawEllipse(null, pen, new Point(cx - 3, cy - 1), 6.5, 6.5);
                dc.DrawLine(pen, new Point(cx - 3, cy - 1), new Point(cx - 3, cy - 5));
                dc.DrawLine(pen, new Point(cx - 3, cy - 1), new Point(cx + 1, cy + 1));
                dc.DrawLine(pen, new Point(cx + 2, cy + 3), new Point(cx + 9, cy + 3));
                dc.DrawLine(pen, new Point(cx + 2, cy + 7), new Point(cx + 9, cy + 7));
                break;
            case TransportIcon.CopyFilePath:
                // フォルダ＋文字行。ファイル名をコピー（書類＋文字行）と対にする。
                dc.DrawLine(pen, new Point(cx - 9, cy + 1), new Point(cx - 9, cy - 9));
                dc.DrawLine(pen, new Point(cx - 9, cy - 9), new Point(cx - 4, cy - 9));
                dc.DrawLine(pen, new Point(cx - 4, cy - 9), new Point(cx - 2.5, cy - 7));
                dc.DrawLine(pen, new Point(cx - 2.5, cy - 7), new Point(cx + 4, cy - 7));
                dc.DrawLine(pen, new Point(cx + 4, cy - 7), new Point(cx + 4, cy + 1));
                dc.DrawLine(pen, new Point(cx + 4, cy + 1), new Point(cx - 9, cy + 1));
                dc.DrawLine(pen, new Point(cx - 3, cy + 5), new Point(cx + 9, cy + 5));
                dc.DrawLine(pen, new Point(cx - 3, cy + 9), new Point(cx + 9, cy + 9));
                break;
            case TransportIcon.CloseTab:
                dc.DrawRectangle(null, pen, new Rect(cx - 9, cy - 7, 14, 14));
                dc.DrawLine(pen, new Point(cx - 5, cy - 3), new Point(cx + 1, cy + 3));
                dc.DrawLine(pen, new Point(cx + 1, cy - 3), new Point(cx - 5, cy + 3));
                dc.DrawLine(pen, new Point(cx + 5, cy - 4), new Point(cx + 9, cy));
                dc.DrawLine(pen, new Point(cx + 9, cy), new Point(cx + 5, cy + 4));
                break;
            case TransportIcon.WaveformMaximize:
                DrawEditorMode(dc, pen, cx, cy - 1);
                dc.DrawLine(pen, new Point(cx - 9, cy + 8), new Point(cx - 5, cy + 8));
                dc.DrawLine(pen, new Point(cx - 9, cy + 8), new Point(cx - 9, cy + 4));
                dc.DrawLine(pen, new Point(cx + 9, cy + 8), new Point(cx + 5, cy + 8));
                dc.DrawLine(pen, new Point(cx + 9, cy + 8), new Point(cx + 9, cy + 4));
                break;
            case TransportIcon.WaveformRestore:
                DrawEditorMode(dc, pen, cx, cy);
                dc.DrawRectangle(null, pen, new Rect(cx - 9, cy - 9, 6, 5));
                break;
            case TransportIcon.SeekViewLeft:
                // 表示枠の左端へ。先頭へ（|<）と区別するため枠を描く。
                dc.DrawRoundedRectangle(null, pen, new Rect(cx - 10, cy - 8, 20, 16), 2, 2);
                DrawSmallChevron(dc, pen, cx - 3, cy, left: true);
                dc.DrawLine(pen, new Point(cx - 6, cy - 4.5), new Point(cx - 6, cy + 4.5));
                break;
            case TransportIcon.SeekViewRight:
                dc.DrawRoundedRectangle(null, pen, new Rect(cx - 10, cy - 8, 20, 16), 2, 2);
                DrawSmallChevron(dc, pen, cx + 3, cy, left: false);
                dc.DrawLine(pen, new Point(cx + 6, cy - 4.5), new Point(cx + 6, cy + 4.5));
                break;
        }

        dc.Pop();
        dc.Pop();
    }

    private static Color AccentColor(string key, Color fore, bool accent)
    {
        if (!accent)
        {
            return fore;
        }

        try
        {
            return Theme.Get(key);
        }
        catch (InvalidOperationException)
        {
            return fore;
        }
    }

    private static Brush? AccentFill(string key, bool accent, byte alpha)
    {
        if (!accent)
        {
            return null;
        }

        var color = AccentColor(key, Colors.Transparent, accent: true);
        if (color.A == 0)
        {
            return null;
        }

        return WpfControlHelpers.FrozenBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static void DrawPlayTriangle(
        DrawingContext dc,
        Pen pen,
        Brush? fill,
        double baseX,
        double tipX,
        double cy,
        double halfHeight)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(baseX, cy - halfHeight), true, true);
            ctx.LineTo(new Point(tipX, cy), true, false);
            ctx.LineTo(new Point(baseX, cy + halfHeight), true, false);
        }

        geo.Freeze();
        dc.DrawGeometry(fill, pen, geo);
    }

    /// <summary>削除の印。色ありは赤い丸に白い ×、色なしは × のみ。</summary>
    private static void DrawDeleteBadge(DrawingContext dc, Pen pen, Brush? fill, double cx, double cy)
    {
        if (fill is null)
        {
            DrawSmallCross(dc, pen, cx, cy);
            return;
        }

        dc.DrawEllipse(fill, null, new Point(cx, cy), 4.8, 4.8);
        var thin = new Pen(pen.Brush, 1.4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        thin.Freeze();
        dc.DrawLine(thin, new Point(cx - 2.1, cy - 2.1), new Point(cx + 2.1, cy + 2.1));
        dc.DrawLine(thin, new Point(cx + 2.1, cy - 2.1), new Point(cx - 2.1, cy + 2.1));
    }

    private static Pen OutlinePen(Pen source, Brush brush)
    {
        var pen = new Pen(brush, source.Thickness)
        {
            StartLineCap = source.StartLineCap,
            EndLineCap = source.EndLineCap,
            LineJoin = source.LineJoin,
        };
        pen.Freeze();
        return pen;
    }

    private static Pen DashedPen(Pen source)
    {
        var pen = new Pen(source.Brush, 1.4)
        {
            DashStyle = new DashStyle([1.6, 1.6], 0),
            DashCap = PenLineCap.Flat,
            LineJoin = PenLineJoin.Miter,
        };
        pen.Freeze();
        return pen;
    }

    /// <summary>
    /// 前後の地点へ。時間軸上の目盛り（マーカー・選択端・ループ端・ファイル端を区別しない点）と、
    /// 着地の目盛りへ向かう山形。旗は描かない。previous は左右反転。
    /// </summary>
    private static void DrawJumpPoint(DrawingContext dc, Pen pen, double cx, double cy, bool previous)
    {
        if (previous)
        {
            dc.PushTransform(new ScaleTransform(-1, 1, cx, cy));
        }

        var axisY = cy + 8;
        dc.DrawLine(pen, new Point(cx - 9, axisY), new Point(cx + 9, axisY));
        dc.DrawLine(pen, new Point(cx - 9, axisY), new Point(cx - 9, cy + 2));
        dc.DrawLine(pen, new Point(cx - 5, axisY), new Point(cx - 5, cy - 1));
        dc.DrawLine(pen, new Point(cx + 9, axisY), new Point(cx + 9, cy - 8));
        DrawChevron(dc, pen, cx + 2, cy - 1, left: false);

        if (previous)
        {
            dc.Pop();
        }
    }

    /// <summary>八分音符。頭の中心を渡す。ピッチアイコンと同じ線画。</summary>
    private static void DrawEighthNote(DrawingContext dc, Pen pen, double headX, double headY)
    {
        dc.DrawEllipse(null, pen, new Point(headX, headY), 3.2, 2.2);
        var stemX = headX + 3.2;
        var stemTop = headY - 13;
        dc.DrawLine(pen, new Point(stemX, headY), new Point(stemX, stemTop));
        dc.DrawLine(pen, new Point(stemX, stemTop), new Point(stemX + 5.5, stemTop + 3));
    }

    /// <summary>波形上のマーカー旗と同じ形（棒の上端に、外側の角だけ丸めた塗り旗）。</summary>
    private static void DrawMarkerFlag(
        DrawingContext dc,
        Brush fill,
        Pen stem,
        double stemX,
        double top,
        double bottom,
        double width)
    {
        const double flagHeight = 6.5;
        dc.DrawLine(stem, new Point(stemX, top + flagHeight), new Point(stemX, bottom));
        var flag = WpfControlHelpers.FlagGeometry(
            new Rect(stemX - 0.9, top, width + 0.9, flagHeight),
            1.8,
            stemOnLeft: true);
        dc.DrawGeometry(fill, null, flag);
    }

    private static void DrawRegionBrackets(
        DrawingContext dc,
        Pen pen,
        Brush? fill,
        double left,
        double right,
        double top,
        double bottom,
        double arm)
    {
        if (fill is not null)
        {
            dc.DrawRectangle(fill, null, new Rect(left, top, right - left, bottom - top));
        }

        dc.DrawLine(pen, new Point(left, top), new Point(left, bottom));
        dc.DrawLine(pen, new Point(left, top), new Point(left + arm, top));
        dc.DrawLine(pen, new Point(left, bottom), new Point(left + arm, bottom));
        dc.DrawLine(pen, new Point(right, top), new Point(right, bottom));
        dc.DrawLine(pen, new Point(right, top), new Point(right - arm, top));
        dc.DrawLine(pen, new Point(right, bottom), new Point(right - arm, bottom));
    }

    private static void DrawSmallCross(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawLine(pen, new Point(cx - 3, cy - 3), new Point(cx + 3, cy + 3));
        dc.DrawLine(pen, new Point(cx + 3, cy - 3), new Point(cx - 3, cy + 3));
    }

    private static void DrawSmallChevron(DrawingContext dc, Pen pen, double centerX, double centerY, bool left)
    {
        var direction = left ? -1d : 1d;
        dc.DrawLine(pen, new Point(centerX - direction * 2.5, centerY - 4.5), new Point(centerX + direction * 2, centerY));
        dc.DrawLine(pen, new Point(centerX + direction * 2, centerY), new Point(centerX - direction * 2.5, centerY + 4.5));
    }

    private static void DrawTriangle(DrawingContext dc, Pen pen, double tipX, double baseX, double cy, double halfHeight)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(tipX, cy), false, true);
            ctx.LineTo(new Point(baseX, cy - halfHeight), true, false);
            ctx.LineTo(new Point(baseX, cy + halfHeight), true, false);
        }

        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static void DrawMiniWave(DrawingContext dc, Pen pen, double cx, double cy, double amp)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(cx - 6, cy), false, false);
            ctx.LineTo(new Point(cx - 3.5, cy - amp * 0.7), true, false);
            ctx.LineTo(new Point(cx - 1, cy + amp * 0.7), true, false);
            ctx.LineTo(new Point(cx + 1.5, cy - amp * 0.45), true, false);
            ctx.LineTo(new Point(cx + 4, cy + amp * 0.45), true, false);
            ctx.LineTo(new Point(cx + 6, cy), true, false);
        }

        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    /// <summary>反時計回りの矢印で囲んだ時計。編集履歴。</summary>
    private static void DrawHistoryClock(DrawingContext dc, Pen pen, double cx, double cy)
    {
        const double r = 8;
        var start = new Point(cx - r * Math.Cos(Math.PI / 6), cy - r * Math.Sin(Math.PI / 6));
        var end = new Point(cx - r * Math.Cos(Math.PI / 6) + 0.01, cy + r * Math.Sin(Math.PI / 6));
        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, true, SweepDirection.Clockwise, true, false);
        }

        arc.Freeze();
        dc.DrawGeometry(null, pen, arc);
        dc.DrawLine(pen, new Point(start.X - 3.2, start.Y - 0.6), start);
        dc.DrawLine(pen, new Point(start.X + 0.4, start.Y - 3.4), start);
        dc.DrawLine(pen, new Point(cx, cy - 4.5), new Point(cx, cy));
        dc.DrawLine(pen, new Point(cx, cy), new Point(cx + 3.5, cy + 2.5));
    }

    private static void DrawSpectrumBars(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawLine(pen, new Point(cx - 8, cy + 6), new Point(cx - 8, cy));
        dc.DrawLine(pen, new Point(cx - 4, cy + 6), new Point(cx - 4, cy - 6));
        dc.DrawLine(pen, new Point(cx, cy + 6), new Point(cx, cy - 2));
        dc.DrawLine(pen, new Point(cx + 4, cy + 6), new Point(cx + 4, cy - 8));
        dc.DrawLine(pen, new Point(cx + 8, cy + 6), new Point(cx + 8, cy - 4));
    }

    /// <summary>プレイリスト行＋再生。プレイヤーへ切替。</summary>
    private static void DrawPlayerMode(DrawingContext dc, Pen pen, double cx, double cy)
    {
        var play = new StreamGeometry();
        using (var ctx = play.Open())
        {
            ctx.BeginFigure(new Point(cx - 9, cy - 7), true, true);
            ctx.LineTo(new Point(cx - 3, cy), true, false);
            ctx.LineTo(new Point(cx - 9, cy + 7), true, false);
        }

        play.Freeze();
        dc.DrawGeometry(null, pen, play);
        dc.DrawLine(pen, new Point(cx, cy - 6), new Point(cx + 9, cy - 6));
        dc.DrawLine(pen, new Point(cx, cy), new Point(cx + 9, cy));
        dc.DrawLine(pen, new Point(cx, cy + 6), new Point(cx + 9, cy + 6));
    }

    /// <summary>細いクローム＋再生。F09 ミニマルプレイヤー。</summary>
    private static void DrawMinimalChrome(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawRectangle(null, pen, new Rect(cx - 10, cy - 7, 20, 14));
        dc.DrawLine(pen, new Point(cx - 10, cy - 3), new Point(cx + 10, cy - 3));
        var play = new StreamGeometry();
        using (var ctx = play.Open())
        {
            ctx.BeginFigure(new Point(cx - 2, cy - 1), true, true);
            ctx.LineTo(new Point(cx + 5, cy + 3.5), true, false);
            ctx.LineTo(new Point(cx - 2, cy + 8), true, false);
        }

        play.Freeze();
        dc.DrawGeometry(null, pen, play);
    }

    /// <summary>波形。エディタへ戻す。</summary>
    private static void DrawEditorMode(DrawingContext dc, Pen pen, double cx, double cy)
    {
        var wave = new StreamGeometry();
        using (var ctx = wave.Open())
        {
            ctx.BeginFigure(new Point(cx - 10, cy), false, false);
            ctx.LineTo(new Point(cx - 6, cy - 7), true, false);
            ctx.LineTo(new Point(cx - 2, cy + 7), true, false);
            ctx.LineTo(new Point(cx + 2, cy - 5), true, false);
            ctx.LineTo(new Point(cx + 6, cy + 5), true, false);
            ctx.LineTo(new Point(cx + 10, cy), true, false);
        }

        wave.Freeze();
        dc.DrawGeometry(null, pen, wave);
    }

    /// <summary>メーター列。アナライザー最大化へ。</summary>
    private static void DrawAnalyzerMaximize(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawLine(pen, new Point(cx - 9, cy + 8), new Point(cx - 9, cy - 2));
        dc.DrawLine(pen, new Point(cx - 4, cy + 8), new Point(cx - 4, cy - 8));
        dc.DrawLine(pen, new Point(cx + 1, cy + 8), new Point(cx + 1, cy - 4));
        dc.DrawRectangle(null, pen, new Rect(cx + 5, cy - 8, 5, 16));
    }

    /// <summary>入れ子の窓。アナライザー最大化を解除。</summary>
    private static void DrawAnalyzerRestore(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawRectangle(null, pen, new Rect(cx - 6, cy - 4, 14, 12));
        dc.DrawLine(pen, new Point(cx - 3, cy - 4), new Point(cx - 3, cy - 8));
        dc.DrawLine(pen, new Point(cx - 3, cy - 8), new Point(cx + 9, cy - 8));
        dc.DrawLine(pen, new Point(cx + 9, cy - 8), new Point(cx + 9, cy + 2));
        dc.DrawLine(pen, new Point(cx + 9, cy + 2), new Point(cx + 8, cy + 2));
    }

    private static void DrawWaveformOverlay(DrawingContext dc, Pen pen, double cx, double cy)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(cx - 9, cy + 2), false, false);
            ctx.LineTo(new Point(cx - 6, cy - 2), true, false);
            ctx.LineTo(new Point(cx - 3, cy + 4), true, false);
            ctx.LineTo(new Point(cx, cy - 4), true, false);
            ctx.LineTo(new Point(cx + 3, cy + 3), true, false);
            ctx.LineTo(new Point(cx + 6, cy - 1), true, false);
            ctx.LineTo(new Point(cx + 9, cy + 2), true, false);
        }

        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static readonly Rect FolderDesign = new(6, 9, 22, 18);

    internal static Rect FolderHoverBounds(Rect bounds)
    {
        const double chromePad = 3d;
        const double strokePad = 1d;
        var innerW = Math.Max(1, bounds.Width - chromePad * 2);
        var innerH = Math.Max(1, bounds.Height - chromePad * 2);
        var scale = Math.Min(innerW / FolderDesign.Width, innerH / FolderDesign.Height);
        var w = FolderDesign.Width * scale + strokePad * 2;
        var h = FolderDesign.Height * scale + strokePad * 2;
        return new Rect(
            bounds.X + (bounds.Width - w) * 0.5,
            bounds.Y + (bounds.Height - h) * 0.5,
            w,
            h);
    }

    private static void DrawNewDocument(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawLine(pen, new Point(cx - 7, cy + 8), new Point(cx - 7, cy - 10));
        dc.DrawLine(pen, new Point(cx - 7, cy - 10), new Point(cx + 1, cy - 10));
        dc.DrawLine(pen, new Point(cx + 1, cy - 10), new Point(cx + 7, cy - 4));
        dc.DrawLine(pen, new Point(cx + 7, cy - 4), new Point(cx + 7, cy + 8));
        dc.DrawLine(pen, new Point(cx + 7, cy + 8), new Point(cx - 7, cy + 8));
        dc.DrawLine(pen, new Point(cx + 1, cy - 10), new Point(cx + 1, cy - 4));
        dc.DrawLine(pen, new Point(cx + 1, cy - 4), new Point(cx + 7, cy - 4));
        dc.DrawLine(pen, new Point(cx - 4, cy - 1), new Point(cx + 4, cy - 1));
        dc.DrawLine(pen, new Point(cx - 4, cy + 3), new Point(cx + 4, cy + 3));
    }

    private static void DrawHelp(DrawingContext dc, Pen pen, Brush brush, double cx, double cy)
    {
        dc.DrawEllipse(null, pen, new Point(cx, cy), 9, 9);
        var mark = new FormattedText(
            "?",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Semibold"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            14,
            brush,
            1.0);
        dc.DrawText(
            mark,
            new Point(cx - mark.Width * 0.5, cy - mark.Height * 0.52));
    }

    private static void DrawFolder(DrawingContext dc, Pen pen, double cx, double cy)
    {
        var stroke = OutlinePen(pen, 1.25);
        var bounds = FolderDesign;
        dc.PushTransform(new TranslateTransform(
            cx - (bounds.X + bounds.Width * 0.5),
            cy - (bounds.Y + bounds.Height * 0.5)));
        dc.DrawLine(stroke, new Point(6, 12), new Point(6, 9));
        dc.DrawLine(stroke, new Point(6, 9), new Point(14, 9));
        dc.DrawLine(stroke, new Point(14, 9), new Point(16, 12));
        dc.DrawLine(stroke, new Point(16, 12), new Point(28, 12));
        dc.DrawLine(stroke, new Point(28, 12), new Point(28, 27));
        dc.DrawLine(stroke, new Point(28, 27), new Point(6, 27));
        dc.DrawLine(stroke, new Point(6, 27), new Point(6, 12));
        dc.DrawLine(stroke, new Point(6, 15), new Point(28, 15));
        dc.Pop();
    }

    private static readonly Color PaletteRed = Color.FromRgb(0xFF, 0x4D, 0x4D);
    private static readonly Color PaletteGreen = Color.FromRgb(0x4D, 0xE0, 0x5A);
    private static readonly Color PaletteBlue = Color.FromRgb(0x4D, 0x8E, 0xFF);

    private static void DrawPaletteCircle(DrawingContext dc, Color color, Point center, double radius)
    {
        var tint = WpfControlHelpers.FrozenBrush(Color.FromArgb(0xB4, color.R, color.G, color.B));
        dc.DrawEllipse(tint, null, center, radius, radius);
    }

    private static void DrawColorPalette(DrawingContext dc, Pen pen, Brush brush, double cx, double cy, bool accent)
    {
        var stroke = OutlinePen(pen, 1.45);
        const double radius = 6.15;
        const double side = 7.4;
        var rise = side * Math.Sqrt(3) / 2;
        var top = new Point(0, -rise * 2 / 3);
        var left = new Point(-side / 2, rise / 3);
        var right = new Point(side / 2, rise / 3);
        var hull = new Rect(
            left.X - radius,
            top.Y - radius,
            side + radius * 2,
            rise + radius * 2);
        dc.PushTransform(new TranslateTransform(
            cx - (hull.X + hull.Width * 0.5),
            cy - (hull.Y + hull.Height * 0.5)));

        if (accent)
        {
            // RGB は「色そのもの」の記号なので、テーマ色ではなく固定の三原色。
            // 塗りを先に全部敷き、白線を上に重ねて重なりの境界を見せる。
            DrawPaletteCircle(dc, PaletteRed, top, radius);
            DrawPaletteCircle(dc, PaletteGreen, left, radius);
            DrawPaletteCircle(dc, PaletteBlue, right, radius);
            var rim = OutlinePen(pen, 1.2);
            dc.DrawEllipse(null, rim, top, radius, radius);
            dc.DrawEllipse(null, rim, left, radius, radius);
            dc.DrawEllipse(null, rim, right, radius, radius);
            dc.Pop();
            return;
        }

        dc.DrawEllipse(brush, null, left, radius, radius);

        dc.PushClip(new EllipseGeometry(top, radius, radius));
        var hatch = OutlinePen(pen, 1.15);
        for (var i = -4; i <= 4; i++)
        {
            var shift = i * 2.55;
            dc.DrawLine(
                hatch,
                new Point(top.X - 9 + shift, top.Y - 9),
                new Point(top.X + 9 + shift, top.Y + 9));
        }

        dc.Pop();
        dc.DrawEllipse(null, stroke, top, radius, radius);
        dc.DrawEllipse(null, stroke, right, radius, radius);
        dc.Pop();
    }

    private static void DrawLoudnessCurve(DrawingContext dc, Pen pen, double cx, double cy)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(cx - 9, cy + 5), false, false);
            ctx.LineTo(new Point(cx - 5, cy - 1), true, false);
            ctx.LineTo(new Point(cx - 1, cy - 5), true, false);
            ctx.LineTo(new Point(cx + 3, cy - 2), true, false);
            ctx.LineTo(new Point(cx + 7, cy - 6), true, false);
            ctx.LineTo(new Point(cx + 10, cy - 4), true, false);
        }

        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
        dc.DrawLine(pen, new Point(cx - 9, cy + 2), new Point(cx + 10, cy + 2));
    }

    private static void DrawGear(DrawingContext dc, Pen pen, double cx, double cy)
    {
        const int teeth = 8;
        const double outer = 8.8;
        const double inner = 5.5;
        const double hole = 2.7;
        var step = Math.PI * 2d / teeth;
        var points = new Point[teeth * 4];
        for (var i = 0; i < teeth; i++)
        {
            var start = -Math.PI / 2d + (i * step) - (step * 0.5);
            points[i * 4] = GearPolar(cx, cy, inner, start + (step * 0.18));
            points[i * 4 + 1] = GearPolar(cx, cy, outer, start + (step * 0.32));
            points[i * 4 + 2] = GearPolar(cx, cy, outer, start + (step * 0.68));
            points[i * 4 + 3] = GearPolar(cx, cy, inner, start + (step * 0.82));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: false, isClosed: true);
            for (var i = 1; i < points.Length; i++)
            {
                ctx.LineTo(points[i], true, true);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
        dc.DrawEllipse(null, pen, new Point(cx, cy), hole, hole);
    }

    private static Pen OutlinePen(Pen source, double thickness)
    {
        var pen = new Pen(source.Brush, thickness)
        {
            StartLineCap = source.StartLineCap,
            EndLineCap = source.EndLineCap,
            LineJoin = source.LineJoin,
        };
        pen.Freeze();
        return pen;
    }

    private static Point GearPolar(double cx, double cy, double radius, double angle) =>
        new(cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius);

    /// <summary>大型スピーカー（キャビネット＋ウーファー／ツイーター）。</summary>
    private static void DrawStudioSpeaker(DrawingContext dc, Pen pen, double cx, double cy)
    {
        var body = new Rect(cx - 7.5, cy - 11, 15, 22);
        dc.DrawRoundedRectangle(null, pen, body, 1.5, 1.5);
        // 上：ツイーター
        dc.DrawEllipse(null, pen, new Point(cx, cy - 5.5), 3.2, 3.2);
        dc.DrawEllipse(null, pen, new Point(cx, cy - 5.5), 1.1, 1.1);
        // 下：ウーファー
        dc.DrawEllipse(null, pen, new Point(cx, cy + 5), 5.2, 5.2);
        dc.DrawEllipse(null, pen, new Point(cx, cy + 5), 2.2, 2.2);
    }

    private static void DrawPadlock(DrawingContext dc, Pen pen, Brush brush, bool open, double cx, double cy)
    {
        var body = new RectangleGeometry(new Rect(10, 16, 14, 11));
        var shackle = PadlockShackleGeometry(open);
        var bounds = Rect.Union(body.Bounds, shackle.Bounds);
        if (!bounds.IsEmpty)
        {
            dc.PushTransform(new TranslateTransform(
                cx - (bounds.X + bounds.Width * 0.5),
                cy - (bounds.Y + bounds.Height * 0.5)));
        }

        dc.DrawGeometry(null, pen, body);
        dc.DrawEllipse(brush, null, new Point(17, 20), 1.4, 1.4);
        dc.DrawLine(pen, new Point(17, 21.4), new Point(17, 24.2));
        dc.DrawGeometry(null, pen, shackle);
        if (!bounds.IsEmpty)
        {
            dc.Pop();
        }
    }

    private static StreamGeometry PadlockShackleGeometry(bool open)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (open)
            {
                // 右足をボディから大きく離す（メニュー約 17px でも閉錠と判別できるように）。
                ctx.BeginFigure(new Point(12.5, 16), false, false);
                ctx.LineTo(new Point(12.5, 10), true, false);
                ctx.ArcTo(new Point(24, 9.5), new Size(5.75, 5.75), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(25.5, 13), true, false);
            }
            else
            {
                ctx.BeginFigure(new Point(12.5, 16), false, false);
                ctx.LineTo(new Point(12.5, 12.5), true, false);
                ctx.ArcTo(new Point(21.5, 12.5), new Size(4.5, 4.5), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(21.5, 16), true, false);
            }
        }

        geo.Freeze();
        return geo;
    }

    /// <summary>カセットデッキのオートリバース。上下で逆向きの矢印を付けた周回。</summary>
    private static void DrawCassetteAutoReverse(DrawingContext dc, Pen pen, Brush? fill = null)
    {
        const double left = 8.6;
        const double right = 25.4;
        const double top = 12.1;
        const double bottom = 23.9;
        const double radius = 5.9;
        var loop = new StreamGeometry();
        using (var ctx = loop.Open())
        {
            ctx.BeginFigure(new Point(left + radius, top), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(right - radius, top), true, true);
            ctx.ArcTo(
                new Point(right - radius, bottom),
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                true);
            ctx.LineTo(new Point(left + radius, bottom), true, true);
            ctx.ArcTo(
                new Point(left + radius, top),
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                true);
        }

        loop.Freeze();
        dc.DrawGeometry(fill, pen, loop);
        DrawLoopArrow(dc, pen, right - radius + 0.2, top, left: false);
        DrawLoopArrow(dc, pen, left + radius - 0.2, bottom, left: true);
    }

    private static void DrawLoopArrow(DrawingContext dc, Pen pen, double tipX, double tipY, bool left)
    {
        var direction = left ? -1d : 1d;
        var back = tipX - direction * 4.4;
        dc.DrawLine(pen, new Point(back, tipY - 3.5), new Point(tipX + direction * 0.6, tipY));
        dc.DrawLine(pen, new Point(back, tipY + 3.5), new Point(tipX + direction * 0.6, tipY));
    }

    /// <summary>折り角つきのファイルに音符。保存／名前を付けて保存の矢印トレイと区別する。</summary>
    private static void DrawSaveMp3(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawLine(pen, new Point(cx - 7, cy + 8), new Point(cx - 7, cy - 10));
        dc.DrawLine(pen, new Point(cx - 7, cy - 10), new Point(cx + 1, cy - 10));
        dc.DrawLine(pen, new Point(cx + 1, cy - 10), new Point(cx + 7, cy - 4));
        dc.DrawLine(pen, new Point(cx + 7, cy - 4), new Point(cx + 7, cy + 8));
        dc.DrawLine(pen, new Point(cx + 7, cy + 8), new Point(cx - 7, cy + 8));
        dc.DrawLine(pen, new Point(cx + 1, cy - 10), new Point(cx + 1, cy - 4));
        dc.DrawLine(pen, new Point(cx + 1, cy - 4), new Point(cx + 7, cy - 4));
        dc.DrawEllipse(null, pen, new Point(cx - 2.4, cy + 3.4), 2.6, 1.8);
        dc.DrawLine(pen, new Point(cx + 0.2, cy + 3.4), new Point(cx + 0.2, cy - 5.4));
        dc.DrawLine(pen, new Point(cx + 0.2, cy - 5.4), new Point(cx + 4.6, cy - 2.8));
    }

    private static void DrawExportWave(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawLine(pen, new Point(cx - 7, cy + 8), new Point(cx - 7, cy - 10));
        dc.DrawLine(pen, new Point(cx - 7, cy - 10), new Point(cx + 1, cy - 10));
        dc.DrawLine(pen, new Point(cx + 1, cy - 10), new Point(cx + 7, cy - 4));
        dc.DrawLine(pen, new Point(cx + 7, cy - 4), new Point(cx + 7, cy + 8));
        dc.DrawLine(pen, new Point(cx + 7, cy + 8), new Point(cx - 7, cy + 8));
        dc.DrawLine(pen, new Point(cx + 1, cy - 10), new Point(cx + 1, cy - 4));
        dc.DrawLine(pen, new Point(cx + 1, cy - 4), new Point(cx + 7, cy - 4));
        var wave = new StreamGeometry();
        using (var ctx = wave.Open())
        {
            ctx.BeginFigure(new Point(cx - 5, cy + 3), false, false);
            ctx.LineTo(new Point(cx - 2, cy - 1), true, false);
            ctx.LineTo(new Point(cx + 1, cy + 5), true, false);
            ctx.LineTo(new Point(cx + 4, cy + 1), true, false);
        }

        wave.Freeze();
        dc.DrawGeometry(null, pen, wave);
    }

    private static void DrawChevron(DrawingContext dc, Pen pen, double centerX, double centerY, bool left)
    {
        var direction = left ? -1d : 1d;
        dc.DrawLine(pen, new Point(centerX - direction * 4, centerY - 7), new Point(centerX + direction * 3, centerY));
        dc.DrawLine(pen, new Point(centerX + direction * 3, centerY), new Point(centerX - direction * 4, centerY + 7));
    }

    /// <summary>
    /// 元に戻す（↶ の「戻り矢印」）。左向き矢じり → 横棒 → 右で半円を描いて下段へ戻る。
    /// redo は左右反転。外形の縦横中央がアイコン枠の中心に来る座標で描く。
    /// </summary>
    private static void DrawUndoRedoArrow(DrawingContext dc, Pen pen, double cx, double cy, bool redo)
    {
        if (redo)
        {
            dc.PushTransform(new ScaleTransform(-1, 1, cx, cy));
        }

        // 外形: x = cx-8.5 … cx+8.25、y = cy-9 … cy+7（見た目の中心がボタン中心に来る）。
        var tip = new Point(cx - 8.5, cy - 4.5);
        var bendTop = new Point(cx + 2.5, cy - 4.5);
        var bendBottom = new Point(cx + 2.5, cy + 7);
        var tail = new Point(cx - 4.5, cy + 7);
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(tip, false, false);
            ctx.LineTo(bendTop, true, true);
            ctx.ArcTo(bendBottom, new Size(5.75, 5.75), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(tail, true, true);

            ctx.BeginFigure(new Point(tip.X + 4.5, tip.Y - 4.5), false, false);
            ctx.LineTo(tip, true, true);
            ctx.LineTo(new Point(tip.X + 4.5, tip.Y + 4.5), true, true);
        }

        path.Freeze();
        dc.DrawGeometry(null, pen, path);

        if (redo)
        {
            dc.Pop();
        }
    }

    /// <summary>
    /// やり直し再生。反時計回りの円矢印（上で左向きの矢じり）の中に再生三角。
    /// 三角は重心（底辺から 1/3）を円の中心に合わせる。
    /// </summary>
    private static void DrawRestart(DrawingContext dc, Pen pen, Brush? playFill, double cx, double cy)
    {
        const double r = 8d;
        var center = new Point(cx, cy + 1.5);
        var top = new Point(center.X, center.Y - r);
        // 円の左上を開け、左やや上から下→右→上へ回って天辺で止める。
        var startRad = 200d * Math.PI / 180d;
        var start = new Point(center.X + r * Math.Cos(startRad), center.Y + r * Math.Sin(startRad));
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(top, new Size(r, r), 0, true, SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(new Point(top.X - 2.5, top.Y), true, true);

            var tip = new Point(top.X - 2.5, top.Y);
            ctx.BeginFigure(new Point(tip.X + 3.8, tip.Y - 3.8), false, false);
            ctx.LineTo(tip, true, true);
            ctx.LineTo(new Point(tip.X + 3.8, tip.Y + 3.8), true, true);
        }

        path.Freeze();
        dc.DrawGeometry(null, pen, path);

        const double half = 3.6;
        const double length = 6.2;
        var baseX = center.X - length / 3d;
        DrawPlayTriangle(dc, pen, playFill, baseX, baseX + length, center.Y, half);
    }

    /// <summary>ハサミ。下に持ち手の輪を 2 つ、刃は交差して上へ開く。</summary>
    private static void DrawCut(DrawingContext dc, Pen pen, double cx, double cy)
    {
        const double ringR = 3.4;
        var leftRing = new Point(cx - 4.6, cy + 6.4);
        var rightRing = new Point(cx + 4.6, cy + 6.4);
        dc.DrawEllipse(null, pen, leftRing, ringR, ringR);
        dc.DrawEllipse(null, pen, rightRing, ringR, ringR);

        // 輪の内側上から、反対側の刃先へ。
        var inset = ringR * 0.7071;
        dc.DrawLine(pen, new Point(leftRing.X + inset, leftRing.Y - inset), new Point(cx + 3.6, cy - 10));
        dc.DrawLine(pen, new Point(rightRing.X - inset, rightRing.Y - inset), new Point(cx - 3.6, cy - 10));
    }

    private static void DrawCopy(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawRectangle(null, pen, new Rect(cx - 8, cy - 7, 11, 13));
        dc.DrawRectangle(null, pen, new Rect(cx - 3, cy - 3, 11, 13));
    }

    private static void DrawPaste(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawRectangle(null, pen, new Rect(cx - 7, cy - 4, 14, 12));
        dc.DrawRectangle(null, pen, new Rect(cx - 4, cy - 8, 8, 5));
        dc.DrawLine(pen, new Point(cx - 2, cy + 1), new Point(cx + 2, cy + 1));
        dc.DrawLine(pen, new Point(cx - 2, cy + 4), new Point(cx + 2, cy + 4));
    }

    private static void DrawSilenceSelection(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawLine(pen, new Point(cx - 9, cy), new Point(cx - 4, cy - 6));
        dc.DrawLine(pen, new Point(cx - 4, cy - 6), new Point(cx - 4, cy + 6));
        dc.DrawLine(pen, new Point(cx - 4, cy + 6), new Point(cx - 9, cy));
        dc.DrawLine(pen, new Point(cx - 1, cy), new Point(cx + 9, cy));
    }

    /// <summary>中央の選択を残すクロップ枠。左右は外側に開くブラケット（]█[）。</summary>
    private static void DrawTrim(DrawingContext dc, Pen pen, Brush? selectionFill, double cx, double cy)
    {
        dc.DrawRectangle(selectionFill, pen, new Rect(cx - 3.5, cy - 6.5, 7, 13));

        // 左 ] ：縦線 + 外側へ伸びる上下フック
        dc.DrawLine(pen, new Point(cx - 4.5, cy - 8), new Point(cx - 4.5, cy + 8));
        dc.DrawLine(pen, new Point(cx - 4.5, cy - 8), new Point(cx - 9.5, cy - 8));
        dc.DrawLine(pen, new Point(cx - 4.5, cy + 8), new Point(cx - 9.5, cy + 8));

        // 右 [ ：対称
        dc.DrawLine(pen, new Point(cx + 4.5, cy - 8), new Point(cx + 4.5, cy + 8));
        dc.DrawLine(pen, new Point(cx + 4.5, cy - 8), new Point(cx + 9.5, cy - 8));
        dc.DrawLine(pen, new Point(cx + 4.5, cy + 8), new Point(cx + 9.5, cy + 8));
    }

    private static void DrawInsertSilence(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawLine(pen, new Point(cx - 9, cy - 6), new Point(cx - 3, cy));
        dc.DrawLine(pen, new Point(cx - 3, cy), new Point(cx - 9, cy + 6));
        dc.DrawLine(pen, new Point(cx + 9, cy - 6), new Point(cx + 3, cy));
        dc.DrawLine(pen, new Point(cx + 3, cy), new Point(cx + 9, cy + 6));
        dc.DrawLine(pen, new Point(cx - 1, cy - 7), new Point(cx - 1, cy + 7));
        dc.DrawLine(pen, new Point(cx + 1, cy - 7), new Point(cx + 1, cy + 7));
    }

    private static void DrawDetectSilence(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawLine(pen, new Point(cx - 9, cy + 2), new Point(cx - 5, cy - 5));
        dc.DrawLine(pen, new Point(cx - 5, cy - 5), new Point(cx - 2, cy + 5));
        dc.DrawLine(pen, new Point(cx - 2, cy + 5), new Point(cx + 2, cy + 5));
        dc.DrawLine(pen, new Point(cx + 2, cy + 5), new Point(cx + 5, cy - 6));
        dc.DrawLine(pen, new Point(cx + 5, cy - 6), new Point(cx + 9, cy + 2));
        dc.DrawLine(pen, new Point(cx - 8, cy + 7), new Point(cx + 8, cy + 7));
    }

    private static void DrawFormatGlyph(
        DrawingContext dc,
        Pen pen,
        Brush brush,
        double cx,
        double cy,
        string text)
    {
        dc.DrawRoundedRectangle(null, pen, new Rect(cx - 10, cy - 7, 20, 14), 3, 3);
        var mark = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Semibold"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            text.Length > 2 ? 8 : 10,
            brush,
            1.0);
        dc.DrawText(mark, new Point(cx - mark.Width * 0.5, cy - mark.Height * 0.52));
    }

    private static void DrawWaapiGlyph(DrawingContext dc, Pen pen, Brush brush, double cx, double cy)
    {
        dc.DrawRoundedRectangle(
            null,
            pen,
            new Rect(cx - 10, cy - 7, 20, 14),
            3,
            3);
        var text = new FormattedText(
            "W",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Semibold"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            11,
            brush,
            1.0);
        dc.DrawText(text, new Point(cx - text.Width * 0.5, cy - text.Height * 0.52));
    }
}

internal static class TransportHover
{
    public static void Attach(ButtonBase button)
    {
        button.MouseEnter += (_, _) => button.InvalidateVisual();
        button.MouseLeave += (_, _) => button.InvalidateVisual();
        button.PreviewMouseLeftButtonDown += (_, _) => button.InvalidateVisual();
        button.PreviewMouseLeftButtonUp += (_, _) => button.InvalidateVisual();
        button.LostMouseCapture += (_, _) => button.InvalidateVisual();
        button.IsEnabledChanged += (_, _) => button.InvalidateVisual();
    }
}

internal static class TransportChrome
{
    public static void Paint(
        DrawingContext dc,
        Rect bounds,
        bool enabled,
        bool hover,
        bool pressed,
        Rect? hoverBounds = null)
    {
        if (enabled && (hover || pressed))
        {
            var fill = pressed
                ? Theme.Get("TransportPressedBackBrush")
                : Theme.Get("TransportHoverBackBrush");
            const double pad = 3d;
            var rect = hoverBounds
                ?? new Rect(pad, pad, Math.Max(1, bounds.Width - pad * 2), Math.Max(1, bounds.Height - pad * 2));
            var radius = DesignMetrics.ControlCornerRadius;
            dc.DrawRoundedRectangle(WpfControlHelpers.FrozenBrush(fill), null, rect, radius, radius);
        }
    }

    public static Color Fore(bool enabled) =>
        enabled ? Theme.Get("TransportForeBrush") : Theme.Get("TransportDisabledForeBrush");
}
