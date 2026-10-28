using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Video.Dxgi;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ウィンドウ全体の動画／PDF。動的背景より前、クロームより後ろ。
/// 再生中は明るく、停止中は暗くしてリストを読む。音楽再生時は隠す。
/// </summary>
internal sealed partial class LibraryVisualStage : Grid
{
    internal const double StoppedDimOpacity = 0.72;

    /// <summary>選択時の暗いプレビューは 1/3 速でループ（無音）。</summary>
    internal const double PreviewLoopSpeed = 1.0 / 3.0;

    /// <summary>動画開始のフェードイン秒数（真っ黒から）。</summary>
    internal const double VideoFadeInSeconds = 0.2;

    /// <summary>本再生停止 → 暗いプレビューへ戻るときの速度ランプ秒数（1 → 1/3）。</summary>
    internal const double PreviewSpeedRampSeconds = 1;

    /// <summary>速度ランプの Tick 間隔（映像クロック更新）。</summary>
    internal const double PreviewSpeedRampTickMs = 50;

    /// <summary>本再生停止 → 暗いプレビューへの暗転秒数（現状から）。</summary>
    internal const double PreviewDimFadeSeconds = 0.25;

    /// <summary>映像枠まわりへにじませるぼかし。フル塗りつぶしには使わない。</summary>
    internal const double AmbientSpillBlurRadius = 72;

    /// <summary>メーター・波形・ファイル名の背後を、縁を溶かしながら少し暗くする。</summary>
    private const double HudFrostFeather = 32;

    /// <summary>暗幕の濃さ。映像は透けたまま、文字が読める程度。</summary>
    private const byte HudDimAlpha = 0x66;

    /// <summary>映像と同じ枠を広げ、縁から光をこぼす。</summary>
    internal const double AmbientSpillScale = 1.12;

    /// <summary>光漏れの不透明度。</summary>
    internal const double AmbientSpillOpacity = 0.50;

    /// <summary>映像と描画エリアのアスペクト差。これ未満は黒帯なしとみなす。</summary>
    internal const double AmbientSpillAspectEpsilon = 0.02;

    /// <summary>格子（少し荒め）。線はデバイス 1px（DPI 追従）。</summary>
    private const double MeshCell = 4;

    private const byte MeshLineAlpha = 160;

    /// <summary>
    /// 黒帯は黒のまま。本編と同ソースの映像を枠いっぱいに広げてぼかし、縁に光をこぼす。
    /// </summary>
    private readonly Grid _spillHost = new()
    {
        ClipToBounds = true,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private readonly Image _spillImage = new()
    {
        Stretch = Stretch.Fill,
        IsHitTestVisible = false,
    };

    /// <summary>光漏れの縮小フレーム（CPU ビットマップ）。D3DImage は複数 Image で共有できないので使わない。</summary>
    private WriteableBitmap? _spillBitmap;

    /// <summary>縮小 BGRA の作業バッファ（毎フレーム確保しない）。</summary>
    private byte[]? _spillScratch;

    private readonly Border _spill = new()
    {
        // 旧 MediaElement 経路と同じく、映像の表示矩形（UniformContentRect）に合わせて置く。
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

    /// <summary>波形・メーター・ファイル名の背後。映像はぼかさず、少し暗くするだけ。</summary>
    private readonly Grid _glassHost = new()
    {
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        UseLayoutRounding = false,
        SnapsToDevicePixels = false,
    };

    private readonly VisualBrush _hudDimBrush = new()
    {
        Stretch = Stretch.None,
        AlignmentX = AlignmentX.Left,
        AlignmentY = AlignmentY.Top,
        TileMode = TileMode.None,
        ViewboxUnits = BrushMappingMode.Absolute,
        ViewportUnits = BrushMappingMode.Absolute,
    };

    private readonly Grid _hudDimFeather = new()
    {
        IsHitTestVisible = false,
        UseLayoutRounding = false,
        SnapsToDevicePixels = false,
        Effect = new BlurEffect
        {
            Radius = HudFrostFeather,
            KernelType = KernelType.Gaussian,
            RenderingBias = RenderingBias.Performance,
        },
    };

    private Size _hudFrostMaskSize;

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
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        Opacity = 0,
        SnapsToDevicePixels = true,
        UseLayoutRounding = true,
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
    private int _videoFadeInTicket;
    private int _previewSpeedRampTicket;
    private DispatcherTimer? _previewSpeedRampTimer;
    private readonly Stopwatch _previewSpeedRampWatch = new();
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
        _spill.Child = _spillImage;
        _spillHost.Children.Add(_spill);
        // フェードイン／SnapMediaBlack は _dxgiHost.Opacity。光漏れも同じ値に追従する。
        _spillHost.SetBinding(
            OpacityProperty,
            new Binding(nameof(Opacity)) { Source = _dxgiHost, Mode = BindingMode.OneWay });
        _glassHost.Background = WpfControlHelpers.FrozenBrush(Color.FromArgb(HudDimAlpha, 0, 0, 0));
        _hudDimBrush.Visual = _hudDimFeather;
        _glassHost.OpacityMask = _hudDimBrush;
        _pdfHost.Children.Add(_pdf);
        // 光漏れ → DXGI 映像 → HUD の薄い暗幕 → PDF → dim で暗く → 格子。
        Children.Add(_spillHost);
        Children.Add(_dxgiHost);
        Children.Add(_glassHost);
        Children.Add(_pdfHost);
        Children.Add(_dim);
        Children.Add(_mesh);
        // BitmapCache は HiDPI で 1px 線を太くぼかすので使わない。
        RenderOptions.SetEdgeMode(_mesh, EdgeMode.Aliased);
        RefreshMeshBrush();
        Loaded += (_, _) => RefreshMeshBrush();
        SizeChanged += (_, _) =>
        {
            ApplyPdfLayout();
            if (_video && IsShown)
            {
                SyncAmbientSpill();
            }
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

    /// <summary>Stretch.Uniform と同じ表示矩形（HUD／レイアウト計算用）。</summary>
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

    /// <summary>本再生を止めて静止している（暗い 1/3 速プレビューではない）。</summary>
    public bool IsFramePaused => _video && IsShown && _framePaused;

    public bool VisualClockRunning => _video && IsShown && _visualClock.IsRunning;

    public bool IsVideo => _video && IsShown;

    public bool TryGetNaturalVideoPixels(out int width, out int height)
    {
        if (_video)
        {
            width = _dxgiWidth;
            height = _dxgiHeight;
            return width > 0 && height > 0;
        }

        // PDF ページ画素（F8 ミニ窓の原寸）。まだ読み込み前なら不可。
        width = (int)Math.Round(_pdfNaturalWidth);
        height = (int)Math.Round(_pdfNaturalHeight);
        return IsPdf && width > 0 && height > 0;
    }

    public bool IsPdf => !_video && IsShown;

    /// <summary>0 始まりの表示中ページ。PDF 以外は 0。</summary>
    public int PdfPageIndex => IsPdf ? _pdfPage : 0;

    /// <summary>総ページ数。未読込や PDF 以外は 1。</summary>
    public int PdfPageCount => IsPdf ? Math.Max(1, _pdfPages) : 1;

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
                return LibraryPlayerMode.VisualDurationOrFallback(_dxgiDuration, _fallbackDuration);
            }

            return TimeSpan.FromSeconds(Math.Max(1, _pdfPages) * LibraryPlaylistDocuments.PdfSecondsPerPage);
        }
    }

    public void Hide()
    {
        CancelPreviewSeek();
        CancelPreviewSpeedRamp();
        _dimFadeTicket++;
        StopMedia();
        LibraryPlayerMode.FadeElementOpacity(_dim, StoppedDimOpacity, instant: true);
        LibraryPlayerMode.FadeElementOpacity(_dxgiHost, 1, instant: true);

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
        CancelVideoFadeIn(restoreOpaque: true);
        _dxgiHost.BeginAnimation(OpacityProperty, null);
        _dxgiHost.Opacity = 1;
        _pdfHost.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    /// <param name="sameSourceClip">
    /// 論理ソースが同じ（プロキシ差し替え含む）。本再生の再開では黒フェードしない。
    /// </param>
    public void ShowVideo(
        string path,
        bool play,
        TimeSpan fallbackDuration = default,
        bool sameSourceClip = false)
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
        var pathChanged = ConsumeDxgiReopenPending()
            || !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase);
        var resumeSameClip = play && sameSourceClip;
        // 同じクリップのホバー→本再生／本再生の再入では黒フラッシュしない
        //（Opacity=0 のまま固着するのを防ぐ。プロキシ差し替えも同じ）。
        if ((pathChanged && !resumeSameClip) || !play)
        {
            SnapMediaBlack();
        }

        SyncVideoPresenterVisibility();
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
            // Space 一時停止の _framePaused が残ると StartPreviewLoop が即 return して暗い 1/3 速が始まらない。
            ApplyVideoDim(instant: true);
            _playing = false;
            _framePaused = false;
            StopVisualClock(TimeSpan.Zero);
            _previewLoopStart = TimeSpan.Zero;
        }

        if (pathChanged)
        {
            CancelPreviewSeek();
            CancelVideoFadeIn(restoreOpaque: false);
            var keepFramePause = resumeSameClip && _framePaused;
            StopMedia();
            // 別クリップへ移るときだけ位置を捨てる。同じ本編のプロキシ差し替えは再開位置を残す。
            if (!resumeSameClip)
            {
                StopVisualClock(TimeSpan.Zero);
                _previewLoopStart = TimeSpan.Zero;
                _playing = false;
                _framePaused = false;
            }
            else
            {
                _playing = false;
                _framePaused = keepFramePause;
            }

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

        // 同じ本編の再開（プレビュー／フレーム一時停止／プロキシ差し替え）は位置を引き継ぐ。
        SetPlaying(true, fromPreviewAllowed: !pathChanged || resumeSameClip);
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
        _dxgiHost.Visibility = Visibility.Collapsed;
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
            LibraryPlayerMode.FadeElementOpacity(_dxgiHost, 1, instant: true);
            SyncPreviewFx(active: !playing, instant: playing);
            return;
        }

        if (alreadyPlaying)
        {
            CancelVideoFadeIn(restoreOpaque: true);
            PlayRealtime(CurrentVisualPosition(), restartClock: false);
            _dxgiHost.BeginAnimation(OpacityProperty, null);
            _dxgiHost.Opacity = 1;
            return;
        }

        if (playing)
        {
            CancelPreviewSeek();
            var resume = fromPreview || fromFramePause;
            // 昇格側で始めた暗転フェードを、ここで打ち切らない。
            if (!(fromPreview && _dim.HasAnimatedProperties))
            {
                ApplyVideoDim(instant: !fromPreview, playing: true);
            }

            // プレビュー開始直後は格子がフェードイン中。HasAnimatedProperties だけでは
            // 昇格側のフェードアウトと区別できず、引数起動だと格子だけ本再生に残る。
            // 本再生へ入るときは格子だけ即時消す（暗転は fromPreview 時に現状からフェード）。
            SyncPreviewFx(active: false, instant: true);
            if (resume)
            {
                var pos = CurrentVisualPosition();
                CancelVideoFadeIn(restoreOpaque: true);
                PlayRealtime(pos, restartClock: true);
                _dxgiHost.BeginAnimation(OpacityProperty, null);
                _dxgiHost.Opacity = 1;
            }
            else
            {
                // 新しいクリップの本再生は真っ黒から。
                SnapMediaBlack();
                SetMediaPosition(TimeSpan.Zero);
                PlayRealtime(TimeSpan.Zero, restartClock: true);
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
    /// 本再生をその場で一時停止する。暗いプレビュー（1/3 速ループ）へは落とさない。
    /// </summary>
    public void PauseAtCurrentFrame()
    {
        if (!_video || !IsShown)
        {
            return;
        }

        CancelPreviewSeek();
        CancelPreviewSpeedRamp();
        CancelVideoFadeIn(restoreOpaque: true);
        var pos = CurrentVisualPosition();
        StopVisualClock(pos);
        PauseMedia();
        _playing = false;
        _framePaused = true;
        ApplyVideoDim(instant: true, playing: true);
        SyncPreviewFx(active: false, instant: true);
        _dxgiHost.BeginAnimation(OpacityProperty, null);
        _dxgiHost.Opacity = 1;
    }

    /// <summary>
    /// スクラブ用シーク。再生中なら一度だけ止め、あとは映像クロック位置だけ更新する。
    /// 通常の Seek は再生中にクロック再始動するため、ドラッグ連打だとデコードが積み上がる。
    /// <paramref name="scrubPreview"/> が true のとき粗いプレビュー（Seek＋数枚）。静止後に false で正確な絵。
    /// </summary>
    public void ScrubTo(TimeSpan position, bool scrubPreview = false)
    {
        if (!_video || !IsShown)
        {
            return;
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        var duration = MediaSeekLimit();
        if (position > duration)
        {
            position = duration;
        }

        CancelPreviewSeek();
        CancelPreviewSpeedRamp();
        if (_playing)
        {
            PauseMedia();
            _playing = false;
            _framePaused = true;
            ApplyVideoDim(instant: true, playing: true);
            SyncPreviewFx(active: false, instant: true);
            _dxgiHost.BeginAnimation(OpacityProperty, null);
            _dxgiHost.Opacity = 1;
        }
        else
        {
            _framePaused = true;
        }

        // DXGI: 粗い／正確の切り替えを先に伝えてから位置を確定する（次の tick の RequestPosition がそのモードで読む）。
        SetDxgiScrubPreview(scrubPreview);
        SetMediaPosition(position);
        StopVisualClock(position);
    }

    private void ApplyVideoDim(bool instant, bool playing = false)
    {
        FadeDimOpacity(playing ? 0 : StoppedDimOpacity, instant, playing ? LibraryPlayerMode.ChromeFadeSeconds : PreviewDimFadeSeconds);
    }

    /// <summary>暗転のフェード。後から来た操作はチケットで完了処理を捨てる。</summary>
    private void FadeDimOpacity(double to, bool instant, double seconds)
    {
        var ticket = ++_dimFadeTicket;
        _dim.BeginAnimation(OpacityProperty, null);
        var from = _dim.Opacity;
        if (instant || Math.Abs(from - to) < 0.001)
        {
            _dim.Opacity = to;
            return;
        }

        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new SineEase
            {
                EasingMode = to > from ? EasingMode.EaseIn : EasingMode.EaseOut,
            },
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

        // 消すときは常に即時。フェードアウト中に Visibility が残ると、引数起動の
        // プレビュー→本再生レースで格子だけ本再生に見える／残ることがある。
        LibraryPlayerMode.FadeElementOpacity(_mesh, 0, instant: true);
        _mesh.Visibility = Visibility.Collapsed;
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
        CancelVideoFadeIn(restoreOpaque: true);
        _dxgiHost.BeginAnimation(OpacityProperty, null);
        _dxgiHost.Opacity = 1;
        // 本再生からの引き継ぎでは現在位置付近にいるので、無駄なシークで一発カクつかせない。
        SeekMediaIfNeeded(position);
        FadeDimOpacity(StoppedDimOpacity, instant: false, PreviewDimFadeSeconds);
        SyncPreviewFx(active: true);
        // 本再生から戻るときはいきなり 1/3 にせず、1 秒かけて落とす。
        BeginPreviewSpeedRamp(position);
    }

    private void SnapMediaBlack()
    {
        CancelVideoFadeIn(restoreOpaque: false);
        _dxgiHost.BeginAnimation(OpacityProperty, null);
        _dxgiHost.Opacity = 0;
    }

    private void CancelVideoFadeIn(bool restoreOpaque)
    {
        _videoFadeInTicket++;
        _dxgiHost.BeginAnimation(OpacityProperty, null);
        if (restoreOpaque)
        {
            _dxgiHost.Opacity = 1;
        }
    }

    /// <summary>動画本再生は真っ黒からフェードイン。</summary>
    private void BeginVideoFadeIn()
    {
        var ticket = ++_videoFadeInTicket;
        _dxgiHost.BeginAnimation(OpacityProperty, null);
        _dxgiHost.Opacity = 0;
        var seekTicket = _previewSeekTicket;
        var wantPlaying = _playing;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket != _videoFadeInTicket)
                {
                    return;
                }

                if (!IsShown)
                {
                    return;
                }

                // フェード待ち中に停止／再開が変わったとき、Opacity=0 のまま固着させない。
                if (_playing != wantPlaying)
                {
                    if (_playing || _framePaused)
                    {
                        _dxgiHost.BeginAnimation(OpacityProperty, null);
                        _dxgiHost.Opacity = 1;
                    }

                    return;
                }

                if (!wantPlaying && seekTicket != _previewSeekTicket)
                {
                    return;
                }

                _dxgiHost.BeginAnimation(OpacityProperty, null);
                _dxgiHost.Opacity = 0;
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
                    if (ticket != _videoFadeInTicket)
                    {
                        return;
                    }

                    _dxgiHost.BeginAnimation(OpacityProperty, null);
                    _dxgiHost.Opacity = 1;
                };
                _dxgiHost.BeginAnimation(OpacityProperty, anim);
            },
            DispatcherPriority.Render);
    }

    private void CancelPreviewSeek() => _previewSeekTicket++;

    private void CancelPreviewSpeedRamp()
    {
        _previewSpeedRampTicket++;
        _previewSpeedRampWatch.Reset();
        if (_previewSpeedRampTimer is null)
        {
            return;
        }

        _previewSpeedRampTimer.Stop();
        _previewSpeedRampTimer = null;
    }

    /// <summary>
    /// 速度ランプ／1/3 速を捨てて 1 倍・明るい映像にする。
    /// <see cref="_playing"/> は false のまま残す（HoverResumeFrame が現在位置を拾えるようにする）。
    /// </summary>
    public void InterruptPreviewSpeedRampForMainPlay()
    {
        if (!_video || !IsShown || _playing || _framePaused)
        {
            return;
        }

        var pos = CurrentVisualPosition();
        FadeDimOpacity(0, instant: false, LibraryPlayerMode.ChromeFadeSeconds);
        SyncPreviewFx(active: false, instant: true);
        PlayRealtime(pos, restartClock: true);
        _dxgiHost.BeginAnimation(OpacityProperty, null);
        _dxgiHost.Opacity = 1;
    }

    /// <summary>
    /// 本再生の 1 倍へ戻す。速度ランプ中／1/3 速プレビュー中は映像クロックを 1 倍へ戻す。
    /// </summary>
    private void PlayRealtime(TimeSpan position, bool restartClock)
    {
        var ramping = _previewSpeedRampTimer is not null;
        var previewSpeed = Math.Abs(_visualSpeed - 1) > 0.001;
        CancelPreviewSpeedRamp();
        var hardReset = ramping || previewSpeed;
        if (restartClock || hardReset)
        {
            BeginVisualClock(position, speed: 1);
        }
    }

    /// <summary>本再生速度から 1/3 へ 1 秒で落とす。</summary>
    private void BeginPreviewSpeedRamp(TimeSpan position)
    {
        CancelPreviewSpeedRamp();
        var ticket = _previewSpeedRampTicket;
        BeginVisualClock(position, speed: 1);
        _previewSpeedRampWatch.Restart();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(PreviewSpeedRampTickMs),
        };
        timer.Tick += (_, _) =>
        {
            if (ticket != _previewSpeedRampTicket)
            {
                return;
            }

            if (_playing || _framePaused || !IsShown)
            {
                CancelPreviewSpeedRamp();
                return;
            }

            var progress = _previewSpeedRampWatch.Elapsed.TotalSeconds / PreviewSpeedRampSeconds;
            if (progress >= 1)
            {
                CancelPreviewSpeedRamp();
                // 最終速度だけ Play で確定（SpeedRatio 単体の遅延対策）。
                ApplyPreviewSpeed(PreviewLoopSpeed, restartPlayback: true);
                return;
            }

            ApplyPreviewSpeed(
                LibraryPlayerMode.PreviewSpeedAtRampProgress(progress, from: 1, to: PreviewLoopSpeed),
                restartPlayback: false);
        };
        _previewSpeedRampTimer = timer;
        timer.Start();
    }

    /// <summary>プレビュー速度を映像クロックへ反映する。</summary>
    private void ApplyPreviewSpeed(double speed, bool restartPlayback)
    {
        _ = restartPlayback;
        BeginVisualClock(CurrentVisualPosition(), speed);
    }

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

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        RefreshMeshBrush();
    }

    private void RefreshMeshBrush() =>
        _mesh.Background = CreateMeshBrush(UiDpi.PixelsPerDip(this));

    /// <summary>線幅はデバイス 1px（<see cref="WpfControlHelpers.DeviceHairline"/>）。</summary>
    internal static Brush CreateMeshBrush(double pixelsPerDip)
    {
        var cell = MeshCell;
        var line = WpfControlHelpers.DeviceHairline(pixelsPerDip);
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

        CancelPreviewSpeedRamp();
        BeginVisualClock(_previewLoopStart, PreviewLoopSpeed);
        if (fadeIn)
        {
            BeginVideoFadeIn();
        }
        else
        {
            _dxgiHost.BeginAnimation(OpacityProperty, null);
            _dxgiHost.Opacity = 1;
        }
    }

    private void RestartPreviewLoop()
    {
        if (!_video || _playing || _framePaused || !IsShown)
        {
            return;
        }

        CancelPreviewSpeedRamp();
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
            var duration = MediaSeekLimit();
            if (position > duration)
            {
                position = duration;
            }

            // 通常の Seek は正確な絵（スクラブ プレビューが残っていれば解除）。
            SetDxgiScrubPreview(false);
            SetMediaPosition(position);
            if (_playing)
            {
                CancelPreviewSpeedRamp();
                if (LibraryPlayerMode.VideoSeekIsShuttleScrub(_playing, _visualClock.IsRunning))
                {
                    // テンキー 1／3: 一時停止のままコマ送り。再開すると 1 倍に戻る。
                    StopVisualClock(position);
                }
                else
                {
                    BeginVisualClock(position, speed: 1);
                }
            }
            else if (_framePaused)
            {
                CancelPreviewSpeedRamp();
                StopVisualClock(position);
            }
            else
            {
                // 暗い選択プレビューの 1/3 速ループは維持する（シークでランプは切る）。
                CancelPreviewSpeedRamp();
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

    /// <summary>1 始まりのページ番号へ移動。範囲外や PDF 以外は false。</summary>
    public bool TryGoPdfPageNumber(int pageNumber1Based)
    {
        if (!IsPdf || _path is null || pageNumber1Based < 1)
        {
            return false;
        }

        var page = Math.Clamp(pageNumber1Based - 1, 0, Math.Max(0, _pdfPages - 1));
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
            // 速度ランプ中に RestartPreviewLoop するとランプが潰れ、本再生復帰の hardReset 判定も狂う。
            if (!_playing
                && _previewSpeedRampTimer is null
                && Duration > TimeSpan.Zero
                && pos >= Duration
                && _visualSpeed > 0)
            {
                RestartPreviewLoop();
                return _previewLoopStart;
            }

            return pos;
        }

        return _visualOrigin > TimeSpan.Zero ? _visualOrigin : TimeSpan.Zero;
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

        // クロックが走る（再生／プレビューループ）ならスクラブ プレビューは終わり。
        SetDxgiScrubPreview(false);
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

        // 早送り・巻き戻しは一時停止して Position スクラブで追従する。
        if (ratio <= 0)
        {
            // _playing は維持し、Stop 時に同じ本再生として 1 倍へ戻せるようにする。
            StopVisualClock(CurrentVisualPosition());
            return;
        }

        if (Math.Abs(ratio - 1) > 0.001)
        {
            // 本再生の再開以外の倍率は映像クロック側（プレビュー経路）へ任せる。
            return;
        }

        if (_playing)
        {
            BeginVisualClock(CurrentVisualPosition(), speed: 1);
            return;
        }

        // シャトル解除などがプレビュー表示中に ratio=1 で来ても、暗い 1/3 速を維持する。
        if (!_framePaused)
        {
            CancelPreviewSpeedRamp();
            BeginVisualClock(CurrentVisualPosition(), PreviewLoopSpeed);
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
        ClearMediaSource();
        StopDxgiPresenter();
    }

    private TimeSpan MediaSeekLimit() =>
        Duration > TimeSpan.Zero ? Duration : TimeSpan.MaxValue;

    /// <summary>
    /// 波形・メーター・ファイル名の矩形だけ少し暗くする。clip はステージ座標。
    /// 縁はぼかして、四角の切り口が映像に残らないようにする。
    /// </summary>
    internal void SetHudFrost(bool on, Geometry? clip)
    {
        if (!on || clip is null || !_video || !IsShown)
        {
            HideHudFrost();
            return;
        }

        var stageSize = new Size(ActualWidth, ActualHeight);
        var signature = HudFrostFeatherSignature(clip);
        if (_glassHost.Visibility == Visibility.Visible
            && signature == _hudFrostFeatherSignature
            && NearlySameSize(stageSize, _hudFrostMaskSize))
        {
            return;
        }

        _glassHost.Visibility = Visibility.Visible;
        _glassHost.Opacity = 1;
        ApplyHudFrostMask(clip, stageSize, signature);
    }

    private void HideHudFrost()
    {
        if (_glassHost.Visibility != Visibility.Visible)
        {
            return;
        }

        _glassHost.Visibility = Visibility.Collapsed;
        _hudFrostFeatherSignature = null;
        _hudFrostMaskSize = default;
        _hudDimFeather.Children.Clear();
    }

    /// <summary>縁のなじませ。ビットマップへ焼かず、合成側でぼかす。</summary>
    private void ApplyHudFrostMask(Geometry clip, Size stageSize, string signature)
    {
        var pad = HudFrostFeather;
        var bounds = Rect.Inflate(clip.Bounds, pad, pad);
        bounds.Intersect(new Rect(0, 0, stageSize.Width, stageSize.Height));
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            return;
        }

        _hudFrostFeatherSignature = signature;
        _hudFrostMaskSize = stageSize;
        _hudDimFeather.Children.Clear();
        _hudDimFeather.Width = bounds.Width;
        _hudDimFeather.Height = bounds.Height;
        AddHudFrostFeatherRects(_hudDimFeather, clip, bounds.Location);
        _hudDimFeather.Measure(new Size(bounds.Width, bounds.Height));
        _hudDimFeather.Arrange(new Rect(0, 0, bounds.Width, bounds.Height));
        _hudDimBrush.Viewbox = new Rect(0, 0, bounds.Width, bounds.Height);
        _hudDimBrush.Viewport = bounds;
    }

    private static bool NearlySameSize(Size a, Size b) =>
        Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5;

    private string? _hudFrostFeatherSignature;

    private static string HudFrostFeatherSignature(Geometry clip)
    {
        var text = new System.Text.StringBuilder();
        AppendHudFrostFeatherSignature(text, clip);
        return text.ToString();
    }

    private static void AppendHudFrostFeatherSignature(System.Text.StringBuilder text, Geometry clip)
    {
        if (clip is GeometryGroup group)
        {
            foreach (var child in group.Children)
            {
                AppendHudFrostFeatherSignature(text, child);
            }

            return;
        }

        if (clip is not RectangleGeometry rect)
        {
            return;
        }

        var bounds = rect.Rect;
        // 1 DIP 単位に丸めて、微小なレイアウト揺れでマスクを焼き直さない。
        text.Append((int)Math.Round(bounds.X));
        text.Append(',');
        text.Append((int)Math.Round(bounds.Y));
        text.Append(',');
        text.Append((int)Math.Round(bounds.Width));
        text.Append(',');
        text.Append((int)Math.Round(bounds.Height));
        text.Append(';');
    }

    private static void AddHudFrostFeatherRects(Panel mask, Geometry clip, Point origin)
    {
        if (clip is GeometryGroup group)
        {
            foreach (var child in group.Children)
            {
                AddHudFrostFeatherRects(mask, child, origin);
            }

            return;
        }

        if (clip is not RectangleGeometry rect || rect.Rect.Width < 1 || rect.Rect.Height < 1)
        {
            return;
        }

        var bounds = rect.Rect;
        mask.Children.Add(new Border
        {
            Background = Brushes.White,
            Width = bounds.Width,
            Height = bounds.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(bounds.X - origin.X, bounds.Y - origin.Y, 0, 0),
            IsHitTestVisible = false,
        });
    }

    private void SetMediaSource(Uri uri) =>
        OpenDxgiSource(uri.IsFile ? uri.LocalPath : uri.OriginalString);

    private void ClearMediaSource() => HideHudFrost();

    private void SetMediaPosition(TimeSpan position) =>
        _ = position;

    /// <summary>DXGI では常にクロック位置へ追従するので、近接シークの特別扱いは不要。</summary>
    private void SeekMediaIfNeeded(TimeSpan position) => SetMediaPosition(position);

    private void PlayMedia(double speedRatio) => _ = speedRatio;

    private void PauseMedia()
    {
    }

    /// <summary>
    /// 映像とステージのアスペクトがずれて黒帯が出るとき（非最大化の窓でも起き得る）だけ光漏れを出す。
    /// 映像の表示矩形へ枠を置き、原映像を枠いっぱいに塗って中心から 1.12 倍・強ぼかしで縁へこぼす。
    /// 中身は <see cref="UpdateAmbientSpillFrame"/> の縮小 CPU ビットマップ。DXGI の帯は透明クリアなので、
    /// 本編ホストの背面に置いたこの層が帯から透けて見える。
    /// </summary>
    private void SyncAmbientSpill()
    {
        if (!_video
            || !IsShown
            || !WantsAmbientSpill(_dxgiWidth, _dxgiHeight, ActualWidth, ActualHeight))
        {
            ClearAmbientSpill();
            return;
        }

        LayoutAmbientSpill();
        _spillHost.Visibility = Visibility.Visible;
        // 初回表示や再表示で中身がまだ無いときは、直近フレームで一度埋める（以後は present ごとに更新）。
        if (_spillBitmap is null && _dxgiLastFrame is not null)
        {
            UpdateAmbientSpillFrame(_dxgiLastFrame);
        }
    }

    /// <summary>光漏れ枠を映像の表示矩形（Stretch.Uniform 相当）へ合わせる。</summary>
    private void LayoutAmbientSpill()
    {
        var rect = UniformContentRect(_dxgiWidth, _dxgiHeight, ActualWidth, ActualHeight);
        if (rect.IsEmpty || rect.Width < 1 || rect.Height < 1)
        {
            return;
        }

        _spill.Width = rect.Width;
        _spill.Height = rect.Height;
        _spill.Margin = new Thickness(rect.X, rect.Y, 0, 0);
    }

    /// <summary>デコード済みフレームを縮小して光漏れビットマップへ書く。NV12／BGRA 両対応。</summary>
    private void UpdateAmbientSpillFrame(DxgiDecodedFrame frame)
    {
        if (_spillHost.Visibility != Visibility.Visible)
        {
            return;
        }

        var (dw, dh) = AmbientSpillFrame.DownscaledSize(frame.Width, frame.Height);
        if (dw <= 0 || dh <= 0)
        {
            return;
        }

        var need = dw * dh * 4;
        if (_spillScratch is null || _spillScratch.Length < need)
        {
            _spillScratch = new byte[need];
        }

        AmbientSpillFrame.FillBgra(frame, _spillScratch, dw, dh);
        WriteSpillBitmap(dw, dh);
    }

    private void WriteSpillBitmap(int width, int height)
    {
        if (_spillScratch is null)
        {
            return;
        }

        if (_spillBitmap is null || _spillBitmap.PixelWidth != width || _spillBitmap.PixelHeight != height)
        {
            _spillBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            _spillImage.Source = _spillBitmap;
        }

        _spillBitmap.WritePixels(new Int32Rect(0, 0, width, height), _spillScratch, width * 4, 0);
    }

    private void ClearAmbientSpill()
    {
        _spillHost.Visibility = Visibility.Collapsed;
        _spillImage.Source = null;
        _spillBitmap = null;
    }
}
