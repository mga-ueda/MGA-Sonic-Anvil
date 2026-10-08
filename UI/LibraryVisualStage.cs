using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ウィンドウ全体の動画／PDF。動的背景より前、クロームより後ろ。
/// 再生中は明るく、停止中は暗くしてリストを読む。音楽再生時は隠す。
/// </summary>
internal sealed class LibraryVisualStage : Grid
{
    internal const double StoppedDimOpacity = 0.72;

    /// <summary>選択時の暗いプレビューは 1/4 速でループ（無音）。</summary>
    internal const double PreviewLoopSpeed = 0.25;

    /// <summary>動画開始のフェードイン秒数（真っ黒から）。</summary>
    internal const double VideoFadeInSeconds = 0.2;

    /// <summary>本再生停止 → 暗いプレビューへの暗転秒数（現状から）。</summary>
    internal const double PreviewDimFadeSeconds = 0.25;

    /// <summary>映像枠まわりへにじませるぼかし。フル塗りつぶしには使わない。</summary>
    internal const double AmbientSpillBlurRadius = 72;

    /// <summary>映像と同じ枠を広げ、縁から光をこぼす。</summary>
    internal const double AmbientSpillScale = 1.12;

    /// <summary>光漏れの不透明度。</summary>
    internal const double AmbientSpillOpacity = 0.32;

    /// <summary>映像と描画エリアのアスペクト差。これ未満は黒帯なしとみなす。</summary>
    internal const double AmbientSpillAspectEpsilon = 0.02;

    /// <summary>格子（少し荒め）。線 1px。</summary>
    private const double MeshCell = 4;

    private const double MeshLine = 1;

    private const byte MeshLineAlpha = 160;

    private readonly MediaElement _media = new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Manual,
        Stretch = Stretch.Uniform,
        ScrubbingEnabled = true,
        IsHitTestVisible = false,
        Volume = 0,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>
    /// 黒帯は黒のまま。本編と同ソースの映像を映像枠だけ広げてぼかし、縁に光をこぼす。
    /// </summary>
    private readonly Grid _spillHost = new()
    {
        ClipToBounds = true,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private readonly MediaElement _spillMedia = new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Manual,
        Stretch = Stretch.Fill,
        ScrubbingEnabled = true,
        IsHitTestVisible = false,
        Volume = 0,
    };

    private readonly Border _spill = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
        Opacity = AmbientSpillOpacity,
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new ScaleTransform(AmbientSpillScale, AmbientSpillScale),
        Effect = new BlurEffect
        {
            Radius = AmbientSpillBlurRadius,
            KernelType = KernelType.Gaussian,
            RenderingBias = RenderingBias.Performance,
        },
    };

    private readonly Grid _pdfHost = new()
    {
        ClipToBounds = true,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private readonly Image _pdf = new()
    {
        Stretch = Stretch.Fill,
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Border _dim = new()
    {
        Background = Brushes.Black,
        Opacity = StoppedDimOpacity,
        IsHitTestVisible = false,
    };

    /// <summary>暗いプレビューの最前面格子。</summary>
    private readonly Border _mesh = new()
    {
        Background = CreateMeshBrush(),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        Opacity = 0,
    };

    private TimeSpan _fallbackDuration;
    private string? _path;
    private bool _video;
    private bool _playing;

    /// <summary>本再生の一時停止。暗いプレビューへ落とさず、現在フレームで止める。</summary>
    private bool _framePaused;
    private int _pdfPage;
    private int _pdfPages = 1;
    private int _pdfLoadTicket;
    private double _pdfZoom = LibraryPdfZoom.Min;
    private bool _pdfSnapLatched;
    private double _pdfNaturalWidth;
    private double _pdfNaturalHeight;
    private BitmapSource? _pdfSharp;
    private int _previewSeekTicket;
    private int _dimFadeTicket;
    private TimeSpan _previewLoopStart;
    private readonly Stopwatch _visualClock = new();
    private TimeSpan _visualOrigin;
    private double _visualSpeed = 1;

    public LibraryVisualStage()
    {
        IsHitTestVisible = false;
        Background = Brushes.Black;
        Visibility = Visibility.Collapsed;
        SnapsToDevicePixels = true;
        _spill.Child = _spillMedia;
        _spillHost.Children.Add(_spill);
        _pdfHost.Children.Add(_pdf);
        // 光漏れ → Media / PDF → dim で暗く → 格子。
        Children.Add(_spillHost);
        Children.Add(_media);
        Children.Add(_pdfHost);
        Children.Add(_dim);
        Children.Add(_mesh);
        _mesh.CacheMode = new BitmapCache { EnableClearType = false, SnapsToDevicePixels = true };
        SizeChanged += (_, _) =>
        {
            ApplyPdfLayout();
            if (_video && IsShown)
            {
                SyncAmbientSpill();
            }
        };
        _media.MediaEnded += (_, _) =>
        {
            if (_framePaused)
            {
                return;
            }

            if (_video && !_playing && IsShown)
            {
                RestartPreviewLoop();
                return;
            }

            if (_video && !_playing)
            {
                return;
            }

            Ended?.Invoke(this, EventArgs.Empty);
        };
        _media.MediaOpened += (_, _) =>
        {
            if (_media.NaturalDuration.HasTimeSpan)
            {
                var duration = _media.NaturalDuration.TimeSpan;
                if (duration > TimeSpan.Zero)
                {
                    _fallbackDuration = duration;
                }

                Opened?.Invoke(this, duration);
            }

            SyncAmbientSpill();
            if (_video && !_playing && !_framePaused)
            {
                RestartHoverPreviewAtStart();
            }
        };
        _media.MediaFailed += (_, _) =>
        {
            // プロキシ無しのコーデックは開けないことがある。ホバーではエラーを出さない。
        };
    }

    /// <summary>
    /// Stretch.Uniform で黒帯が出るとき。縦長・4:3・横長のレターボックスなど。
    /// </summary>
    internal static bool WantsAmbientSpill(
        int naturalWidth,
        int naturalHeight,
        double stageWidth,
        double stageHeight)
    {
        if (naturalWidth <= 0 || naturalHeight <= 0 || stageWidth <= 0 || stageHeight <= 0)
        {
            return false;
        }

        var videoAspect = naturalWidth / (double)naturalHeight;
        var stageAspect = stageWidth / stageHeight;
        return Math.Abs(videoAspect - stageAspect) > AmbientSpillAspectEpsilon;
    }

    /// <summary>MediaElement Stretch.Uniform と同じ表示矩形。</summary>
    internal static Rect UniformContentRect(
        int naturalWidth,
        int naturalHeight,
        double stageWidth,
        double stageHeight)
    {
        if (naturalWidth <= 0 || naturalHeight <= 0 || stageWidth <= 0 || stageHeight <= 0)
        {
            return Rect.Empty;
        }

        var videoAspect = naturalWidth / (double)naturalHeight;
        var stageAspect = stageWidth / stageHeight;
        double width;
        double height;
        if (videoAspect > stageAspect)
        {
            width = stageWidth;
            height = stageWidth / videoAspect;
        }
        else
        {
            height = stageHeight;
            width = stageHeight * videoAspect;
        }

        return new Rect((stageWidth - width) * 0.5, (stageHeight - height) * 0.5, width, height);
    }

    public event EventHandler? Ended;

    public event EventHandler<TimeSpan>? Opened;

    public bool IsShown => Visibility == Visibility.Visible && _path is not null;

    public bool IsPlaying => _playing && IsShown;

    public bool VisualClockRunning => _video && IsShown && _visualClock.IsRunning;

    public bool IsVideo => _video && IsShown;

    public bool IsPdf => !_video && IsShown;

    /// <summary>B キー背景固定用。表示中ページのビットマップを複製する。</summary>
    public bool TryClonePdfBitmap(out BitmapSource? bitmap)
    {
        bitmap = null;
        if (!IsPdf || _pdfSharp is null)
        {
            return false;
        }

        bitmap = _pdfSharp.Clone();
        if (bitmap.CanFreeze)
        {
            bitmap.Freeze();
        }

        return true;
    }

    public TimeSpan Position
    {
        get
        {
            if (!IsShown)
            {
                return TimeSpan.Zero;
            }

            if (_video)
            {
                return CurrentVisualPosition();
            }

            return TimeSpan.FromSeconds(_pdfPage * LibraryPlaylistDocuments.PdfSecondsPerPage);
        }
    }

    public TimeSpan Duration
    {
        get
        {
            if (_video)
            {
                return LibraryPlayerMode.VisualDurationOrFallback(
                    _media.NaturalDuration.HasTimeSpan ? _media.NaturalDuration.TimeSpan : TimeSpan.Zero,
                    _fallbackDuration);
            }

            return TimeSpan.FromSeconds(Math.Max(1, _pdfPages) * LibraryPlaylistDocuments.PdfSecondsPerPage);
        }
    }

    public void Hide()
    {
        CancelPreviewSeek();
        _dimFadeTicket++;
        StopMedia();
        LibraryPlayerMode.FadeElementOpacity(_dim, StoppedDimOpacity, instant: true);
        LibraryPlayerMode.FadeElementOpacity(_media, 1, instant: true);
        try
        {
            _media.SpeedRatio = 1;
            _spillMedia.SpeedRatio = 1;
        }
        catch
        {
        }

        _path = null;
        _playing = false;
        _framePaused = false;
        _fallbackDuration = TimeSpan.Zero;
        StopVisualClock();
        _previewLoopStart = TimeSpan.Zero;
        ResetPdfView();
        _pdfSharp = null;
        ClearPdfSources();
        ClearAmbientSpill();
        SyncPreviewFx(active: false, instant: true);
        _media.BeginAnimation(OpacityProperty, null);
        _media.Opacity = 1;
        _pdfHost.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    public void ShowVideo(string path, bool play, TimeSpan fallbackDuration = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Hide();
            return;
        }

        ResetPdfView();
        _pdfHost.Visibility = Visibility.Collapsed;
        _pdfSharp = null;
        ClearPdfSources();
        _video = true;
        var pathChanged = !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase);
        // 同じクリップのホバー→本再生では黒フラッシュしない（2回目の Show で Opacity=0 のまま固着する）。
        if (pathChanged || !play)
        {
            SnapMediaBlack();
        }

        _media.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
        if (pathChanged)
        {
            _fallbackDuration = fallbackDuration > TimeSpan.Zero ? fallbackDuration : TimeSpan.Zero;
            ClearAmbientSpill();
        }
        else if (fallbackDuration > TimeSpan.Zero)
        {
            _fallbackDuration = fallbackDuration;
        }

        if (!play)
        {
            ApplyVideoDim(instant: true);
            _playing = false;
            StopVisualClock(TimeSpan.Zero);
            _previewLoopStart = TimeSpan.Zero;
        }

        if (pathChanged)
        {
            CancelPreviewSeek();
            StopMedia();
            // 前クリップの再生位置／クロックを残さない（上下キーで別動画へ移ったとき用）。
            StopVisualClock(TimeSpan.Zero);
            _previewLoopStart = TimeSpan.Zero;
            _playing = false;
            _path = path;
            try
            {
                SetMediaSource(new Uri(path, UriKind.Absolute));
            }
            catch (UriFormatException)
            {
                SetMediaSource(new Uri(path));
            }
        }
        else
        {
            _path = path;
        }

        if (!play)
        {
            SyncPreviewFx(active: true);
            // 前クリップの Position が残ると、新しいファイルが途中から開く。
            SetMediaPosition(TimeSpan.Zero);

            // SnapMediaBlack の Opacity=0 をここで戻す。Opened 待ちだと暗いまま残る。
            StartPreviewLoop(fadeIn: true);
            return;
        }

        // 別クリップへの本再生はプレビュー位置を引き継がない。
        SetPlaying(true, fromPreviewAllowed: !pathChanged);
    }

    public void ShowPdf(string path, bool play, TimeSpan position = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Hide();
            return;
        }

        Visibility = Visibility.Visible;
        CancelPreviewSeek();
        StopMedia();
        _video = false;
        _previewLoopStart = TimeSpan.Zero;
        ClearAmbientSpill();
        SyncPreviewFx(active: false, instant: true);
        _media.Visibility = Visibility.Collapsed;
        _pdfHost.Visibility = Visibility.Visible;
        var page = PageFromPosition(position);
        var pathChanged = !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase);
        _path = path;
        if (pathChanged)
        {
            ResetPdfView();
        }

        if (pathChanged || page != _pdfPage)
        {
            _pdfPage = page;
            _ = LoadPdfPageAsync(path, page);
        }
        else
        {
            ApplyPdfLayout();
        }

        SetPlaying(play);
    }

    public void SetPlaying(bool playing, bool fromPreviewAllowed = true)
    {
        var fromFramePause = fromPreviewAllowed && _video && _framePaused && playing;
        var fromPreview = fromPreviewAllowed && _video && !_playing && playing && !_framePaused;
        var alreadyPlaying = _video && _playing && playing && !_framePaused;
        _playing = playing;
        _framePaused = false;

        if (!_video)
        {
            // PDF 選択プレビューは暗くするだけ（メッシュなし）。
            if (playing)
            {
                _dimFadeTicket++;
            }

            LibraryPlayerMode.FadeElementOpacity(_dim, playing ? 0 : StoppedDimOpacity, instant: true);
            LibraryPlayerMode.FadeElementOpacity(_media, 1, instant: true);
            SyncPreviewFx(active: !playing, instant: playing);
            return;
        }

        if (alreadyPlaying)
        {
            PlayMedia(speedRatio: 1);
            _media.BeginAnimation(OpacityProperty, null);
            _media.Opacity = 1;
            return;
        }

        if (playing)
        {
            CancelPreviewSeek();
            // 本再生へ戻すときは dim／格子を即消す（つなぎ中のプレビュー・アニメ残留を防ぐ）。
            ApplyVideoDim(instant: true, playing: true);
            SyncPreviewFx(active: false, instant: true);
            if (fromPreview || fromFramePause)
            {
                var pos = CurrentVisualPosition();
                PlayMedia(speedRatio: 1);
                _media.BeginAnimation(OpacityProperty, null);
                _media.Opacity = 1;
                BeginVisualClock(pos, speed: 1);
            }
            else
            {
                // 新しいクリップの本再生は真っ黒から。
                SnapMediaBlack();
                SetMediaPosition(TimeSpan.Zero);
                PlayMedia(speedRatio: 1);
                BeginVisualClock(TimeSpan.Zero, speed: 1);
                BeginVideoFadeIn();
            }
        }
        else
        {
            // 止めた位置から暗いプレビューへ。真っ黒フェードではなく現状から暗くする。
            CancelPreviewSeek();
            var pos = CurrentVisualPosition();
            StopVisualClock(pos);
            EnterDimPreviewAt(pos);
        }
    }

    /// <summary>
    /// 本再生をその場で一時停止する。暗いプレビュー（1/4 速ループ）へは落とさない。
    /// </summary>
    public void PauseAtCurrentFrame()
    {
        if (!_video || !IsShown)
        {
            return;
        }

        CancelPreviewSeek();
        var pos = CurrentVisualPosition();
        StopVisualClock(pos);
        PauseMedia();
        _playing = false;
        _framePaused = true;
        ApplyVideoDim(instant: true, playing: true);
        SyncPreviewFx(active: false, instant: true);
        _media.BeginAnimation(OpacityProperty, null);
        _media.Opacity = 1;
    }

    private void ApplyVideoDim(bool instant, bool playing = false)
    {
        if (playing)
        {
            // 進行中の暗転フェード完了でまた暗くならないようチケットを進める。
            _dimFadeTicket++;
        }

        LibraryPlayerMode.FadeElementOpacity(_dim, playing ? 0 : StoppedDimOpacity, instant);
    }

    private void SyncPreviewFx(bool active, bool instant = false)
    {
        var want = active && _video;
        if (want)
        {
            _mesh.Visibility = Visibility.Visible;
            LibraryPlayerMode.FadeElementOpacity(
                _mesh,
                1,
                instant,
                seconds: LibraryPlayerMode.ChromeFadeSeconds);
            return;
        }

        LibraryPlayerMode.FadeElementOpacity(
            _mesh,
            0,
            instant,
            seconds: LibraryPlayerMode.ChromeFadeSeconds);
        if (instant)
        {
            _mesh.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>指定位置から暗いプレビューへ（停止位置の再シーク後など）。</summary>
    public void EnterDimPreviewAt(TimeSpan position)
    {
        if (!_video || !IsShown)
        {
            return;
        }

        _playing = false;
        _framePaused = false;
        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        _previewLoopStart = position;
        _media.BeginAnimation(OpacityProperty, null);
        _media.Opacity = 1;
        SetMediaPosition(position);
        PlayMedia(PreviewLoopSpeed);
        FadeDimToPreview();
        SyncPreviewFx(active: true);
        BeginVisualClock(position, PreviewLoopSpeed);
    }

    private void FadeDimToPreview()
    {
        var ticket = ++_dimFadeTicket;
        _dim.BeginAnimation(OpacityProperty, null);
        var from = _dim.Opacity;
        var to = StoppedDimOpacity;
        if (Math.Abs(from - to) < 0.001)
        {
            _dim.Opacity = to;
            return;
        }

        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(PreviewDimFadeSeconds),
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn },
        };
        anim.Completed += (_, _) =>
        {
            if (ticket != _dimFadeTicket)
            {
                return;
            }

            _dim.BeginAnimation(OpacityProperty, null);
            _dim.Opacity = to;
        };
        _dim.BeginAnimation(OpacityProperty, anim);
    }

    private void SnapMediaBlack()
    {
        _media.BeginAnimation(OpacityProperty, null);
        _media.Opacity = 0;
    }

    /// <summary>動画本再生は真っ黒からフェードイン。</summary>
    private void BeginVideoFadeIn()
    {
        _media.BeginAnimation(OpacityProperty, null);
        _media.Opacity = 0;
        var seekTicket = _previewSeekTicket;
        var wantPlaying = _playing;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (!IsShown || _playing != wantPlaying)
                {
                    return;
                }

                if (!wantPlaying && seekTicket != _previewSeekTicket)
                {
                    return;
                }

                _media.BeginAnimation(OpacityProperty, null);
                _media.Opacity = 0;
                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromSeconds(VideoFadeInSeconds),
                    FillBehavior = FillBehavior.HoldEnd,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                };
                anim.Completed += (_, _) =>
                {
                    _media.BeginAnimation(OpacityProperty, null);
                    _media.Opacity = 1;
                };
                _media.BeginAnimation(OpacityProperty, anim);
            },
            DispatcherPriority.Render);
    }

    private void CancelPreviewSeek() => _previewSeekTicket++;

    /// <summary>ホバーは常に 0 秒から。暗転の自動スキップはしない。</summary>
    private void RestartHoverPreviewAtStart()
    {
        if (!_video || _playing || _framePaused || !IsShown)
        {
            return;
        }

        _previewLoopStart = TimeSpan.Zero;
        SetMediaPosition(TimeSpan.Zero);
        StartPreviewLoop(fadeIn: false);
    }

    private void PresentPdfBitmap()
    {
        if (_pdfSharp is null)
        {
            ClearPdfSources();
            return;
        }

        RenderOptions.SetBitmapScalingMode(_pdf, BitmapScalingMode.HighQuality);
        _pdf.Source = _pdfSharp;
        _pdf.Opacity = 1;
    }

    private void ClearPdfSources()
    {
        _pdf.Source = null;
        _pdf.Opacity = 1;
    }

    private static Brush CreateMeshBrush()
    {
        var cell = MeshCell;
        var line = MeshLine;
        var group = new DrawingGroup();
        var bar = new SolidColorBrush(Color.FromArgb(MeshLineAlpha, 0, 0, 0));
        bar.Freeze();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(bar, null, new Rect(0, 0, cell, line));
            dc.DrawRectangle(bar, null, new Rect(0, 0, line, cell));
        }

        group.Freeze();
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell, cell),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, cell, cell),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    private void StartPreviewLoop(bool fadeIn)
    {
        if (!_video || _playing || _framePaused || !IsShown)
        {
            return;
        }

        BeginVisualClock(_previewLoopStart, PreviewLoopSpeed);
        PlayMedia(PreviewLoopSpeed);
        if (fadeIn)
        {
            BeginVideoFadeIn();
        }
        else
        {
            _media.BeginAnimation(OpacityProperty, null);
            _media.Opacity = 1;
        }
    }

    private void RestartPreviewLoop()
    {
        if (!_video || _playing || _framePaused || !IsShown)
        {
            return;
        }

        SetMediaPosition(_previewLoopStart);
        PlayMedia(PreviewLoopSpeed);
        BeginVisualClock(_previewLoopStart, PreviewLoopSpeed);
    }

    public void Seek(TimeSpan position)
    {
        if (!IsShown)
        {
            return;
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        if (_video)
        {
            var duration = _media.NaturalDuration.HasTimeSpan
                ? _media.NaturalDuration.TimeSpan
                : TimeSpan.MaxValue;
            if (position > duration)
            {
                position = duration;
            }

            SetMediaPosition(position);
            if (_playing)
            {
                PlayMedia(speedRatio: 1);
                BeginVisualClock(position, speed: 1);
            }
            else if (_framePaused)
            {
                PauseMedia();
                StopVisualClock(position);
            }
            else
            {
                PlayMedia(PreviewLoopSpeed);
                BeginVisualClock(position, PreviewLoopSpeed);
            }

            return;
        }

        var page = PageFromPosition(position);
        if (page == _pdfPage || _path is null)
        {
            return;
        }

        _pdfPage = page;
        _ = LoadPdfPageAsync(_path, page);
    }

    public bool TryStepPdfPage(int delta)
    {
        if (!IsPdf || delta == 0 || _path is null)
        {
            return false;
        }

        var page = Math.Clamp(_pdfPage + delta, 0, Math.Max(0, _pdfPages - 1));
        if (page == _pdfPage)
        {
            return false;
        }

        _pdfPage = page;
        _ = LoadPdfPageAsync(_path, page);
        return true;
    }

    public bool TryGoPdfPageEdge(int edge)
    {
        if (!IsPdf || _path is null)
        {
            return false;
        }

        var page = edge < 0 ? 0 : Math.Max(0, _pdfPages - 1);
        if (page == _pdfPage)
        {
            return true;
        }

        _pdfPage = page;
        _ = LoadPdfPageAsync(_path, page);
        return true;
    }

    public bool TryZoomPdf(int direction, bool isRepeat)
    {
        if (!IsPdf || direction == 0)
        {
            return false;
        }

        var snap = CurrentPdfSnapFactor();
        var next = LibraryPdfZoom.Step(_pdfZoom, direction, snap, isRepeat, ref _pdfSnapLatched);
        if (Math.Abs(next - _pdfZoom) < 1e-9)
        {
            return true;
        }

        _pdfZoom = next;
        ApplyPdfLayout();
        return true;
    }

    public void ResetPdfZoomLatch() => _pdfSnapLatched = false;

    private TimeSpan CurrentVisualPosition()
    {
        if (_visualClock.IsRunning)
        {
            var pos = LibraryPlayerMode.VisualPlayPosition(
                _visualOrigin,
                _visualClock.Elapsed.TotalSeconds,
                _visualSpeed,
                Duration);
            if (!_playing
                && Duration > TimeSpan.Zero
                && pos >= Duration
                && _visualSpeed > 0)
            {
                RestartPreviewLoop();
                return _previewLoopStart;
            }

            return pos;
        }

        if (_visualOrigin > TimeSpan.Zero)
        {
            return _visualOrigin;
        }

        try
        {
            return _media.Position;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private void BeginVisualClock(TimeSpan origin, double speed)
    {
        _visualOrigin = origin < TimeSpan.Zero ? TimeSpan.Zero : origin;
        _visualSpeed = speed;
        if (speed == 0)
        {
            _visualClock.Reset();
            return;
        }

        _visualClock.Restart();
    }

    private void StopVisualClock(TimeSpan? origin = null)
    {
        if (origin is { } pos)
        {
            _visualOrigin = pos < TimeSpan.Zero ? TimeSpan.Zero : pos;
        }
        else if (_visualClock.IsRunning)
        {
            _visualOrigin = CurrentVisualPosition();
        }

        _visualSpeed = 0;
        _visualClock.Reset();
    }

    public void SetSpeedRatio(double ratio)
    {
        if (!_video || !IsShown)
        {
            return;
        }

        // MediaElement の負の SpeedRatio／高速再生はカクつきやすい。
        // 早送り・巻き戻しは一時停止して Position スクラブで追従する。
        if (ratio <= 0 || Math.Abs(ratio - 1) > 0.001)
        {
            try
            {
                _media.SpeedRatio = 1;
                _spillMedia.SpeedRatio = 1;
            }
            catch
            {
            }

            PauseMedia();
            StopVisualClock(CurrentVisualPosition());
            return;
        }

        if (_playing)
        {
            PlayMedia(speedRatio: 1);
            BeginVisualClock(CurrentVisualPosition(), speed: 1);
        }
    }

    private void ResetPdfView()
    {
        _pdfZoom = LibraryPdfZoom.Min;
        _pdfSnapLatched = false;
        _pdfNaturalWidth = 0;
        _pdfNaturalHeight = 0;
        _pdf.Width = double.NaN;
        _pdf.Height = double.NaN;
        _pdf.Stretch = Stretch.Uniform;
    }

    private double CurrentPdfSnapFactor() =>
        LibraryPdfZoom.SnapFactor(ActualWidth, ActualHeight, _pdfNaturalWidth, _pdfNaturalHeight);

    private void ApplyPdfLayout()
    {
        if (!IsPdf || _pdf.Source is null || _pdfNaturalWidth <= 0 || _pdfNaturalHeight <= 0)
        {
            return;
        }

        var vw = ActualWidth;
        var vh = ActualHeight;
        if (vw <= 0 || vh <= 0)
        {
            return;
        }

        var fit = Math.Min(vw / _pdfNaturalWidth, vh / _pdfNaturalHeight);
        if (fit <= 0)
        {
            return;
        }

        var scale = fit * LibraryPdfZoom.Clamp(_pdfZoom);
        _pdf.Stretch = Stretch.Fill;
        _pdf.Width = _pdfNaturalWidth * scale;
        _pdf.Height = _pdfNaturalHeight * scale;
    }

    private int PageFromPosition(TimeSpan position)
    {
        var page = (int)Math.Floor(position.TotalSeconds / LibraryPlaylistDocuments.PdfSecondsPerPage);
        return Math.Clamp(page, 0, Math.Max(0, _pdfPages - 1));
    }

    private async Task LoadPdfPageAsync(string path, int page)
    {
        var ticket = ++_pdfLoadTicket;
        var pages = 1;
        try
        {
            var count = await LibraryPdfPages.GetPageCountAsync(path).ConfigureAwait(true);
            if (count > 0)
            {
                pages = count;
            }
        }
        catch
        {
        }

        _pdfPages = pages;
        page = Math.Clamp(page, 0, pages - 1);
        _pdfPage = page;
        var bytes = await LibraryPdfPages.RenderPngAsync(path, page, maxEdge: 3840).ConfigureAwait(true);
        if (ticket != _pdfLoadTicket || !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (bytes is null || bytes.Length == 0)
        {
            _pdfSharp = null;
            ClearPdfSources();
            return;
        }

        var image = new BitmapImage();
        using (var stream = new MemoryStream(bytes, writable: false))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
        }

        image.Freeze();
        _pdfNaturalWidth = image.PixelWidth;
        _pdfNaturalHeight = image.PixelHeight;
        _pdfSharp = image;
        PresentPdfBitmap();
        ApplyPdfLayout();
        Opened?.Invoke(this, Duration);
    }

    private void StopMedia() => ClearMediaSource();

    /// <summary>
    /// 映像とステージのアスペクトがずれて黒帯が出るときだけ、再生中フレームの光漏れを出す。
    /// </summary>
    private void SyncAmbientSpill()
    {
        if (!_video || !IsShown)
        {
            ClearAmbientSpill();
            return;
        }

        var width = _media.NaturalVideoWidth;
        var height = _media.NaturalVideoHeight;
        if (width <= 0
            || height <= 0
            || !WantsAmbientSpill(width, height, ActualWidth, ActualHeight))
        {
            PauseSpillMedia();
            _spillHost.Visibility = Visibility.Collapsed;
            return;
        }

        LayoutAmbientSpill(width, height);
        _spillHost.Visibility = Visibility.Visible;
        MirrorSpillTransport();
    }

    private void LayoutAmbientSpill(int naturalWidth, int naturalHeight)
    {
        var rect = UniformContentRect(naturalWidth, naturalHeight, ActualWidth, ActualHeight);
        if (rect.IsEmpty)
        {
            _spill.Width = 0;
            _spill.Height = 0;
            _spill.Margin = new Thickness(0);
            return;
        }

        _spill.Width = rect.Width;
        _spill.Height = rect.Height;
        _spill.Margin = new Thickness(rect.X, rect.Y, 0, 0);
    }

    private void ClearAmbientSpill()
    {
        PauseSpillMedia();
        _spillHost.Visibility = Visibility.Collapsed;
        _spill.Width = 0;
        _spill.Height = 0;
        _spill.Margin = new Thickness(0);
    }

    private void SetMediaSource(Uri uri)
    {
        _media.Source = uri;
        try
        {
            _spillMedia.Source = uri;
        }
        catch
        {
        }
    }

    private void ClearMediaSource()
    {
        try
        {
            _media.Stop();
            _media.Source = null;
        }
        catch
        {
        }

        try
        {
            _spillMedia.Stop();
            _spillMedia.Source = null;
        }
        catch
        {
        }
    }

    private void SetMediaPosition(TimeSpan position)
    {
        try
        {
            _media.Position = position;
        }
        catch
        {
        }

        try
        {
            _spillMedia.Position = position;
        }
        catch
        {
        }
    }

    private void PlayMedia(double speedRatio)
    {
        try
        {
            _media.Volume = 0;
            _media.SpeedRatio = speedRatio;
            _media.Play();
        }
        catch
        {
        }

        if (_spillHost.Visibility != Visibility.Visible)
        {
            return;
        }

        try
        {
            _spillMedia.Volume = 0;
            _spillMedia.SpeedRatio = speedRatio;
            _spillMedia.Play();
        }
        catch
        {
        }
    }

    private void PauseMedia()
    {
        try
        {
            _media.Pause();
        }
        catch
        {
        }

        PauseSpillMedia();
    }

    private void PauseSpillMedia()
    {
        try
        {
            _spillMedia.Pause();
        }
        catch
        {
        }
    }

    /// <summary>本編の位置・速度に光漏れ側を合わせる。</summary>
    private void MirrorSpillTransport()
    {
        if (_spillHost.Visibility != Visibility.Visible || !_video)
        {
            return;
        }

        try
        {
            _spillMedia.Volume = 0;
            _spillMedia.SpeedRatio = _media.SpeedRatio;
            _spillMedia.Position = _media.Position;
            _spillMedia.Play();
        }
        catch
        {
        }
    }
}
