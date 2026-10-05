using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    };

    private string? _path;
    private bool _video;
    private bool _playing;
    private int _pdfPage;
    private int _pdfPages = 1;
    private int _pdfLoadTicket;
    private double _pdfZoom = LibraryPdfZoom.Min;
    private bool _pdfSnapLatched;
    private double _pdfNaturalWidth;
    private double _pdfNaturalHeight;
    private BitmapSource? _pdfSharp;
    private int _previewSeekTicket;
    private TimeSpan _previewLoopStart;

    public LibraryVisualStage()
    {
        IsHitTestVisible = false;
        Background = Brushes.Black;
        Visibility = Visibility.Collapsed;
        SnapsToDevicePixels = true;
        _pdfHost.Children.Add(_pdf);
        // Media / PDF → dim で暗く → 格子。
        Children.Add(_media);
        Children.Add(_pdfHost);
        Children.Add(_dim);
        Children.Add(_mesh);
        _mesh.CacheMode = new BitmapCache { EnableClearType = false, SnapsToDevicePixels = true };
        SizeChanged += (_, _) => ApplyPdfLayout();
        _media.MediaEnded += (_, _) =>
        {
            if (_video && !_playing && IsShown)
            {
                RestartPreviewLoop();
                return;
            }

            if (!_video)
            {
                Ended?.Invoke(this, EventArgs.Empty);
            }
        };
        _media.MediaOpened += (_, _) =>
        {
            if (_media.NaturalDuration.HasTimeSpan)
            {
                Opened?.Invoke(this, _media.NaturalDuration.TimeSpan);
            }

            if (_video && !_playing)
            {
                _ = SeekVisiblePreviewFrameAsync(fadeIn: true);
            }
        };
    }

    public event EventHandler? Ended;

    public event EventHandler<TimeSpan>? Opened;

    public bool IsShown => Visibility == Visibility.Visible && _path is not null;

    public bool IsPlaying => _playing && IsShown;

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
                try
                {
                    return _media.Position;
                }
                catch
                {
                    return TimeSpan.Zero;
                }
            }

            return TimeSpan.FromSeconds(_pdfPage * LibraryPlaylistDocuments.PdfSecondsPerPage);
        }
    }

    public TimeSpan Duration
    {
        get
        {
            if (_video && _media.NaturalDuration.HasTimeSpan)
            {
                return _media.NaturalDuration.TimeSpan;
            }

            return TimeSpan.FromSeconds(Math.Max(1, _pdfPages) * LibraryPlaylistDocuments.PdfSecondsPerPage);
        }
    }

    public void Hide()
    {
        CancelPreviewSeek();
        StopMedia();
        LibraryPlayerMode.FadeElementOpacity(_dim, StoppedDimOpacity, instant: true);
        LibraryPlayerMode.FadeElementOpacity(_media, 1, instant: true);
        _media.SpeedRatio = 1;
        _path = null;
        _playing = false;
        _previewLoopStart = TimeSpan.Zero;
        ResetPdfView();
        _pdfSharp = null;
        ClearPdfSources();
        SyncPreviewFx(active: false);
        _media.BeginAnimation(OpacityProperty, null);
        _media.Opacity = 1;
        _pdfHost.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    public void ShowVideo(string path, bool play)
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
        // 表示を出す前に真っ黒へ（前フレームが一瞬見えるのを防ぐ）。
        SnapMediaBlack();
        _media.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
        var pathChanged = !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase);
        if (!play)
        {
            ApplyVideoDim(instant: true);
            _playing = false;
        }

        if (pathChanged)
        {
            CancelPreviewSeek();
            StopMedia();
            _previewLoopStart = TimeSpan.Zero;
            _path = path;
            _media.Source = new Uri(path);
        }
        else
        {
            _path = path;
        }

        if (!play)
        {
            SyncPreviewFx(active: true);
            // Open 済みなら黒スキップのうえループ。未 Open は MediaOpened で開始。
            if (_media.NaturalDuration.HasTimeSpan)
            {
                _ = SeekVisiblePreviewFrameAsync(fadeIn: pathChanged);
            }

            return;
        }

        SetPlaying(true);
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
        SyncPreviewFx(active: false);
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

    public void SetPlaying(bool playing)
    {
        _playing = playing;
        if (!_video)
        {
            // PDF 選択プレビューは暗くするだけ（メッシュなし）。
            LibraryPlayerMode.FadeElementOpacity(_dim, playing ? 0 : StoppedDimOpacity, instant: true);
            LibraryPlayerMode.FadeElementOpacity(_media, 1, instant: true);
            SyncPreviewFx(active: !playing);
            return;
        }

        if (playing)
        {
            CancelPreviewSeek();
            // 先に真っ黒にしてから dim を外す（明るいフレームが一瞬出るのを防ぐ）。
            SnapMediaBlack();
            ApplyVideoDim(instant: true, playing: true);
            SyncPreviewFx(active: false);
            try
            {
                // プレビューで先のフレームにいても、本再生は冒頭から。
                _media.Position = TimeSpan.Zero;
            }
            catch
            {
            }

            _media.SpeedRatio = 1;
            _media.Volume = 0;
            _media.Play();
            BeginVideoFadeIn();
        }
        else
        {
            // 止めた位置から暗いプレビューへ。真っ黒フェードではなく現状から暗くする。
            CancelPreviewSeek();
            EnterDimPreviewFromCurrent();
        }
    }

    private void ApplyVideoDim(bool instant, bool playing = false)
    {
        LibraryPlayerMode.FadeElementOpacity(_dim, playing ? 0 : StoppedDimOpacity, instant);
    }

    private void SyncPreviewFx(bool active)
    {
        _mesh.BeginAnimation(OpacityProperty, null);
        _mesh.Opacity = 1;
        // 格子は動画の暗いプレビューだけ。PDF には出さない。
        _mesh.Visibility = active && _video ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>本再生停止：現在位置のまま 1/4 速プレビューへ移し、dim／格子を短くフェードイン。</summary>
    private void EnterDimPreviewFromCurrent()
    {
        TimeSpan pos;
        try
        {
            pos = _media.Position;
        }
        catch
        {
            pos = TimeSpan.Zero;
        }

        if (pos < TimeSpan.Zero)
        {
            pos = TimeSpan.Zero;
        }

        EnterDimPreviewAt(pos);
    }

    /// <summary>指定位置から暗いプレビューへ（停止位置の再シーク後など）。</summary>
    public void EnterDimPreviewAt(TimeSpan position)
    {
        if (!_video || !IsShown)
        {
            return;
        }

        _playing = false;
        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        _previewLoopStart = position;
        _media.BeginAnimation(OpacityProperty, null);
        _media.Opacity = 1;
        try
        {
            _media.Position = position;
            _media.Volume = 0;
            _media.SpeedRatio = PreviewLoopSpeed;
            _media.Play();
        }
        catch
        {
        }

        FadeDimToPreview();
        FadeMeshIn();
    }

    private void FadeDimToPreview()
    {
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
            _dim.BeginAnimation(OpacityProperty, null);
            _dim.Opacity = to;
        };
        _dim.BeginAnimation(OpacityProperty, anim);
    }

    private void FadeMeshIn()
    {
        _mesh.BeginAnimation(OpacityProperty, null);
        _mesh.Opacity = 0;
        _mesh.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(PreviewDimFadeSeconds),
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) =>
        {
            _mesh.BeginAnimation(OpacityProperty, null);
            _mesh.Opacity = 1;
        };
        _mesh.BeginAnimation(OpacityProperty, anim);
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

    private bool PreviewSeekStillActive(int ticket) =>
        ticket == _previewSeekTicket && _video && !_playing && IsShown;

    private async Task SeekVisiblePreviewFrameAsync(bool fadeIn)
    {
        var ticket = ++_previewSeekTicket;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        if (!PreviewSeekStillActive(ticket))
        {
            return;
        }

        if (!_media.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        var duration = _media.NaturalDuration.TimeSpan;
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        // 先にループ表示を開始し、黒スキップは表示後に精査する（波形より遅く見えないように）。
        _previewLoopStart = TimeSpan.Zero;
        try
        {
            _media.Position = TimeSpan.Zero;
        }
        catch
        {
        }

        StartPreviewLoop(fadeIn);

        await Task.Delay(60).ConfigureAwait(true);
        if (!PreviewSeekStillActive(ticket))
        {
            return;
        }

        if (TrySampleFrameLooksVisible())
        {
            return;
        }

        foreach (var pos in LibraryVideoPreview.CandidatePositions(duration))
        {
            if (pos <= TimeSpan.Zero)
            {
                continue;
            }

            if (!PreviewSeekStillActive(ticket))
            {
                return;
            }

            try
            {
                _media.Position = pos;
                _media.Volume = 0;
                _media.SpeedRatio = PreviewLoopSpeed;
                _media.Play();
            }
            catch
            {
                continue;
            }

            await Task.Delay(90).ConfigureAwait(true);
            if (!PreviewSeekStillActive(ticket))
            {
                return;
            }

            if (TrySampleFrameLooksVisible())
            {
                _previewLoopStart = pos;
                return;
            }
        }

        if (!PreviewSeekStillActive(ticket))
        {
            return;
        }

        _previewLoopStart = TimeSpan.Zero;
        try
        {
            _media.Position = TimeSpan.Zero;
            _media.Volume = 0;
            _media.SpeedRatio = PreviewLoopSpeed;
            _media.Play();
        }
        catch
        {
        }
    }

    private bool TrySampleFrameLooksVisible()
    {
        var nw = _media.NaturalVideoWidth;
        var nh = _media.NaturalVideoHeight;
        if (nw <= 0 || nh <= 0)
        {
            return false;
        }

        const int sampleW = 64;
        var sampleH = Math.Max(1, nh * sampleW / nw);
        var priorOpacity = _media.Opacity;
        try
        {
            // VisualBrush は Opacity=0 の Visual を真っ黒としてキャプチャするため、
            // 判定中だけ Media を見える状態に戻す。
            if (priorOpacity != 1)
            {
                _media.BeginAnimation(OpacityProperty, null);
                _media.Opacity = 1;
            }

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(
                    new VisualBrush(_media) { Stretch = Stretch.Fill },
                    null,
                    new Rect(0, 0, sampleW, sampleH));
            }

            var bitmap = new RenderTargetBitmap(sampleW, sampleH, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[sampleW * sampleH * 4];
            bitmap.CopyPixels(pixels, sampleW * 4, 0);
            return LibraryVideoPreview.FrameLooksVisible(pixels, sampleW * sampleH);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (priorOpacity != 1)
            {
                _media.BeginAnimation(OpacityProperty, null);
                _media.Opacity = priorOpacity;
            }
        }
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
        if (!_video || _playing || !IsShown)
        {
            return;
        }

        try
        {
            _media.Volume = 0;
            _media.SpeedRatio = PreviewLoopSpeed;
            _media.Play();
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
        catch
        {
        }
    }

    private void RestartPreviewLoop()
    {
        if (!_video || _playing || !IsShown)
        {
            return;
        }

        try
        {
            _media.Position = _previewLoopStart;
            _media.Volume = 0;
            _media.SpeedRatio = PreviewLoopSpeed;
            _media.Play();
        }
        catch
        {
        }
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
            try
            {
                var duration = _media.NaturalDuration.HasTimeSpan
                    ? _media.NaturalDuration.TimeSpan
                    : TimeSpan.MaxValue;
                if (position > duration)
                {
                    position = duration;
                }

                _media.Position = position;
            }
            catch
            {
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

    public void SetSpeedRatio(double ratio)
    {
        if (!_video || !IsShown)
        {
            return;
        }

        try
        {
            // MediaElement の負の SpeedRatio／高速再生はカクつきやすい。
            // 早送り・巻き戻しは一時停止して Position スクラブで追従する。
            if (ratio <= 0 || Math.Abs(ratio - 1) > 0.001)
            {
                _media.SpeedRatio = 1;
                _media.Pause();
                return;
            }

            _media.SpeedRatio = 1;
            if (_playing)
            {
                _media.Play();
            }
        }
        catch
        {
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

    private void StopMedia()
    {
        try
        {
            _media.Stop();
            _media.Source = null;
        }
        catch
        {
        }
    }
}
