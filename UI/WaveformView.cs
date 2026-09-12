using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal sealed class WaveformView : Grid
{
    // 旧 2^(1/8) の 5 段階分 = 2^(5/8)。キー／ボタンの時間・振幅ズーム共通。
    public const double TimeZoomStep = 1.5422108254079407;
    public const double TimeZoomMax = 81920d;
    public const double TimeZoomStepMax = 32d;
    public const double AmpZoomMax = 128d;
    // 旧 2^(1/4) の 5 段階分 = 2^(5/4)。ホイール時間ズーム。
    public const double WheelTimeStep = 2.378414230005442;
    private const int PolylineMaxSamplesPerPixel = 1;
    private const double OverlayWaveHeightFraction = 0.5;
    private const int RawColumnMaxSamplesPerPixel = 96;
    private const int RawColumnMaxFrames = 1 << 18;
    private const int SamplePointMaxVisibleFrames = 500;
    private const int TimeLabelCacheMax = 512;
    private const double SamplePointRadius = 8d / 3d;
    private static readonly double[] DbRequiredMarks = [-3, -6, -12];
    private static readonly double[] DbOptionalMarks = [-9, -18, -24, -36, -48, -60];
    private const double DbOptionalMinGapPx = 11;
    private static double DbScaleLaneWidth => DesignMetrics.DbScaleWidth;
    private const int DragThresholdPx = 3;
    private const double LoopHandleMinPx = 5;
    private const double MarkerSnapPx = 8d;
    private const int TrailSampleRetainMs = SeekPlaybackTrailPaint.SampleRetainMs;
    private const double TrailMinSecDelta = 0.02;
    private const int TrailSampleMinIntervalMs = 24;
    private const int TrailMaxSamples = 900;
    private const double TrailDiscontinuitySec = 1.25;

    private const double MouseGuideMoveEpsilonPx = 0.5;
    private static double TimeLaneHeight => DesignMetrics.RulerHeight;

    internal static int CountFlagLaneRows(bool hasMarkers, bool hasRegions)
    {
        if (!hasMarkers && !hasRegions)
        {
            return 0;
        }

        return hasMarkers && hasRegions ? 2 : 1;
    }

    private int FlagLaneCount =>
        _document is null
            ? 0
            : CountFlagLaneRows(_document.Markers.Count > 0, _document.Regions.Count > 0);

    private bool SplitFlagLanes => FlagLaneCount > 1;

    internal static double MarkerLaneHeightForRows(int rows) =>
        Math.Max(0, rows) * DesignMetrics.MarkerLaneRowHeight;

    /// <summary>名前欄の左端。幅は <see cref="ChannelLabels.MaxChars"/> 文字分で固定。</summary>
    internal static double ChannelLabelBadgeX(double wellX) => wellX;

    internal static double ChannelLabelTextX(double badgeX, double badgeW, double textW) =>
        badgeX + Math.Max(0, (badgeW - textW) * 0.5);

    private double MarkerLaneHeight => MarkerLaneHeightForRows(FlagLaneCount);

    private double ChromeTopHeight => MarkerLaneHeight + TimeLaneHeight;

    private readonly DrawingHost _staticHost;
    private readonly DrawingHost _overlayHost;
    private readonly DrawingHost _playheadHost;
    private readonly Rectangle _mouseGuideBar = new();
    private readonly TranslateTransform _mouseGuideTransform = new();
    private readonly TextBlock _loadingLabel = new();
    private readonly List<(WaveMarker Marker, Rect Flag)> _markerFlags = [];
    private readonly List<(WaveSelection Range, long Frame, Rect Flag)> _regionFlags = [];
    private readonly HashSet<long> _selectedMarkerFrames = [];
    private readonly HashSet<RegionEdge> _selectedRegionEdges = [];
    private bool _loopStartSelected;
    private bool _loopEndSelected;
    private long? _markerSelectAnchor;
    private bool _markerDragging;
    private bool _markerDragMoved;
    private long _markerDragPrimaryOrigin;
    private long? _markerDragFollowOrigin;
    private long _markerDragLastDelta = long.MinValue;
    private long? _markerDragSnapHold;
    private long[] _markerDragOrigins = [];
    private MarkerSnapshot[] _markerDragBefore = [];
    private RegionEdge[] _regionDragOrigins = [];
    private WaveRegion[] _regionsDragBefore = [];
    private bool _loopDragStart;
    private bool _loopDragEnd;
    private WaveSelection _loopDragBefore;
    private bool _pendingSelectionPrerollJump;
    private TextBox? _commentEditor;
    private long _commentEditFrame = -1;
    private WaveSelection _commentEditRegion;
    private bool _endingCommentEdit;
    private readonly List<(long Frame, long TickMs)> _trailSamples = [];
    private readonly List<(long Frame, long TickMs)> _exitTrailSamples = [];
    private bool _trailActive;
    private Func<long, float>? _previewGainAtFrame;

    private AudioDocument? _document;
    private ChannelLayout _speakerLayout = ChannelLayout.Stereo;
    private int[] _fileChannelMap = [];
    private int _soloMask;
    private WriteableBitmap? _waveBitmap;
    private WriteableBitmap? _invertBitmap;
    private AudioDocument? _invertDocument;
    private int _invertChannels;
    private int[] _invertPixels = [];
    private int[] _wavePixels = [];
    private int _wavePixelWidth;
    private int _wavePixelHeight;
    private int _waveChannels;
    private Size _waveDipSize;
    private double _waveDpiX;
    private double _waveDpiY;
    private double _waveViewStart;
    private double _waveViewSpan;
    private double _waveAmpZoom;
    private SpectrogramViewMode _waveMode;
    private bool _waveDirty = true;
    private SpectrogramViewMode _spectrogramMode;
    private readonly SpectrogramRenderer _spectrogram = new();
    private readonly SpectrogramBoostBar _boostBar = new();
    private readonly LoudnessOverlayRenderer _loudness = new();
    private double _loudnessTargetLufs = LoudnessMeterEngine.DefaultTargetLufs;
    private double _appliedMarkerLaneHeight = -1;
    private double _staticPaintMs;
    private double _staticPaintLastMs;
    private double _staticPaintEndMs;
    private double _followRebuildAtMs;
    private bool _staticRebuildQueued;
    private bool _deferWaveReload;
    private bool _deferredStaticQueued;
    private bool _viewChangedQueued;
    private readonly Dictionary<(string Text, TimeLabelAccent Accent), FormattedText> _timeLabelCache = new();
    private readonly Dictionary<string, FormattedText> _dbLabelCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FormattedText> _markerLabelCache = new(StringComparer.Ordinal);
    private readonly Dictionary<(WaveSelection Range, long Frame), Rect> _regionFlagLayout = [];
    private readonly Dictionary<long, Rect> _markerFlagLayout = [];
    private readonly Dictionary<(string Text, bool Selected), FormattedText> _markerCommentLabelCache = new();
    private double _timeLabelPixelsPerDip;
    private float[] _columnMins = [];
    private float[] _columnMaxs = [];
    private int[] _columnYHi = [];
    private int[] _columnYLo = [];
    private int _waveBgra;
    private int _waveOverlayBgra;
    private int _loudnessWaveBgra;
    private int _zeroBgra;
    private double _viewStart;
    private double _timeZoom = 1d;
    private double _ampZoom = 1d;
    private long _playheadFrame;
    private double? _pointerX;
    private double? _mouseGuideX;
    private double _appliedGuideX = double.NaN;
    private bool _guideOverSelection;
    private bool _mouseGuideQueued;
    private bool _snapCacheDirty = true;
    private readonly List<(double X, long Frame)> _snapPoints = [];
    private long _hoverCursorAt;
    private Brush? _mouseGuideBrush;
    private Brush? _mouseGuideOnSelectionBrush;
    private bool _dragging;
    private bool _scrubbing;
    private bool _selecting;
    private Point _dragStart;
    private long _anchorFrame;
    private long? _keyboardSelectAnchor;
    private Pen? _playheadGlowOuter;
    private Pen? _playheadGlowInner;
    private Pen? _playheadCore;
    private Color _playheadPenColor;
    private long _exitPlayheadFrame = -1;
    private Pen? _exitPlayheadGlowOuter;
    private Pen? _exitPlayheadGlowInner;
    private Pen? _exitPlayheadCore;
    private Color _exitPlayheadPenColor;

    public event EventHandler<long>? CursorCommitted;
    public event EventHandler<long>? ScrubStarted;
    public event EventHandler<long>? ScrubPreviewed;
    public event EventHandler<(long Frame, bool Commit)>? ScrubEnded;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ViewChanged;
    public event EventHandler<(long Frame, string Comment)>? MarkerCommentCommitted;
    public event EventHandler<(WaveSelection Region, string Name)>? RegionNameCommitted;
    public event EventHandler? TimelineDragStarting;
    public event EventHandler<(
        MarkerSnapshot[] MarkersBefore,
        WaveRegion[] RegionsBefore,
        WaveSelection LoopBefore)>? TimelineLayoutCommitted;
    public event EventHandler? MarkersChanged;
    public event EventHandler<WaveformContextHit>? ContextMenuRequested;
    public event EventHandler<(int Channel, bool Add, bool Mute)>? ChannelLabelClicked;

    public bool IsEditingMarkerComment =>
        _commentEditor is { Visibility: Visibility.Visible };

    public WaveformView()
    {
        ClipToBounds = true;
        Clip = new RectangleGeometry(new Rect(RenderSize));
        Focusable = true;
        FocusVisualStyle = null;
        RefreshLocalizedTips();
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        Cursor = Cursors.IBeam;
        Background = Brushes.Transparent;

        _staticHost = new DrawingHost(LayerKind.Static) { Owner = this };
        _overlayHost = new DrawingHost(LayerKind.Overlay) { Owner = this };
        _playheadHost = new DrawingHost(LayerKind.Playhead) { Owner = this };
        Children.Add(_staticHost);
        Children.Add(_overlayHost);
        Children.Add(_playheadHost);

        _mouseGuideBrush = WpfControlHelpers.FrozenBrush(Theme.Get("MouseGuideBrush"));
        _mouseGuideOnSelectionBrush = WpfControlHelpers.FrozenBrush(Theme.Get("MouseGuideOnSelectionBrush"));
        _mouseGuideBar.Width = 1;
        _mouseGuideBar.HorizontalAlignment = HorizontalAlignment.Left;
        _mouseGuideBar.VerticalAlignment = VerticalAlignment.Stretch;
        _mouseGuideBar.IsHitTestVisible = false;
        _mouseGuideBar.Fill = _mouseGuideBrush;
        _mouseGuideBar.StrokeThickness = 0;
        _mouseGuideBar.SnapsToDevicePixels = true;
        _mouseGuideBar.UseLayoutRounding = true;
        _mouseGuideBar.Visibility = Visibility.Collapsed;
        _mouseGuideBar.RenderTransform = _mouseGuideTransform;
        RenderOptions.SetEdgeMode(_mouseGuideBar, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(_mouseGuideBar, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(_overlayHost, 2);
        Panel.SetZIndex(_playheadHost, 3);
        Panel.SetZIndex(_mouseGuideBar, 4);
        Children.Add(_mouseGuideBar);
        _loadingLabel.Text = UiStrings.LabelWaveformLoading;
        _loadingLabel.FontSize = 13;
        _loadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _loadingLabel.VerticalAlignment = VerticalAlignment.Center;
        _loadingLabel.IsHitTestVisible = false;
        _loadingLabel.Focusable = false;
        _loadingLabel.Visibility = Visibility.Collapsed;
        _loadingLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");
        Panel.SetZIndex(_loadingLabel, 6);
        Children.Add(_loadingLabel);
        _boostBar.Visibility = Visibility.Collapsed;
        _boostBar.ValueChanged += OnSpectrogramBoostChanged;
        Panel.SetZIndex(_boostBar, 5);
        Children.Add(_boostBar);
        SizeChanged += (_, _) =>
        {
            EndMarkerCommentEdit(commit: true);
            _waveDirty = true;
            Clip = new RectangleGeometry(new Rect(RenderSize));
            SyncMouseGuideHeight();
            SyncSpectrogramBoostBar();
            InvalidateStaticLayer();
        };
        _spectrogram.InvalidateRequested += () =>
            Dispatcher.BeginInvoke(InvalidateStaticLayerForSpectrogram);
    }

    /// <summary>
    /// スペクトログラムのキャッシュ進捗による再描画。裏で生成が続いていても、
    /// 波形のみ表示のときは静的レイヤーを無駄に描き直さない。
    /// </summary>
    private void InvalidateStaticLayerForSpectrogram()
    {
        if (SpectrogramVisible)
        {
            InvalidateStaticLayer();
        }
    }

    private void InvalidateStaticLayerForLoudness()
    {
        if (LoudnessVisible)
        {
            _waveDirty = true;
            InvalidateStaticLayer();
        }
    }

    public void RefreshLocalizedTips()
    {
        TipService.Set(this, UiStrings.TipWaveform);
        TipService.Set(_boostBar, UiStrings.TipSpectrogramBoost);
        _boostBar.SetValue(AutomationProperties.NameProperty, UiStrings.TipSpectrogramBoost);
        _loadingLabel.Text = UiStrings.LabelWaveformLoading;
    }

    public void SetLoadingVisible(bool visible) =>
        _loadingLabel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public AudioDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            _timeZoom = 1d;
            _ampZoom = 1d;
            _viewStart = 0;
            CenterLocked = false;
            SetTrailRecording(false);
            EndMarkerCommentEdit(commit: false);
            ResetMarkerDragState();
            CancelPointerInteraction();
            _invertBitmap = null;
            _invertDocument = null;
            _invertChannels = 0;
            _previewGainAtFrame = null;
            ClearTimelineSelection(refresh: false);
            _markerSelectAnchor = null;
            _keyboardSelectAnchor = null;
            _spectrogram.InvalidateCache();
            _loudness.Invalidate();
            InvalidateWaveform();
            SyncSpectrogramBoostBar();
            RaiseViewChanged();
        }
    }

    /// <summary>
    /// お試しフェード中など、描画だけにゲインを掛ける（ドキュメントは変更しない）。
    /// </summary>
    public void SetPreviewGain(Func<long, float>? gainAtFrame)
    {
        _previewGainAtFrame = gainAtFrame;
        InvalidateWaveform();
    }

    public long PlayheadFrame
    {
        get => _playheadFrame;
        set
        {
            if (_playheadFrame == value)
            {
                return;
            }

            if (_dragging || _markerDragging || _scrubbing)
            {
                return;
            }

            ResetTrailIfRewound(value, _trailSamples);
            _playheadFrame = value;
            if (_trailActive)
            {
                RecordTrailSample(value, _trailSamples);
            }

            InvalidatePlayheadOnly();
        }
    }

    public void SetPlayheadFromPlayback(long frame)
    {
        if (_document is null)
        {
            return;
        }

        frame = ClampFrame(frame);
        if (_playheadFrame == frame)
        {
            return;
        }

        ResetTrailIfRewound(frame, _trailSamples);
        _playheadFrame = frame;
        if (_trailActive)
        {
            RecordTrailSample(frame, _trailSamples);
        }

        InvalidatePlayheadOnly();
    }

    /// <summary>
    /// -E 二重再生（Exit レイヤー）ヘッドのフレーム。負値で非表示。
    /// Play -E 有効時、ループ折り返し中はシークバーが 2 本になる（IM Importer と同じ）。
    /// </summary>
    public long ExitPlayheadFrame
    {
        get => _exitPlayheadFrame;
        set
        {
            if (value < 0)
            {
                if (_exitPlayheadFrame < 0 && _exitTrailSamples.Count == 0)
                {
                    return;
                }

                _exitPlayheadFrame = -1;
                _exitTrailSamples.Clear();
                InvalidatePlayheadOnly();
                return;
            }

            if (_exitPlayheadFrame == value)
            {
                return;
            }

            ResetTrailIfRewound(value, _exitTrailSamples);
            _exitPlayheadFrame = value;
            if (_trailActive)
            {
                RecordTrailSample(value, _exitTrailSamples);
            }

            InvalidatePlayheadOnly();
        }
    }

    internal IReadOnlyList<(long Frame, long TickMs)> SeekTrailSamples => _trailSamples;

    internal IReadOnlyList<(long Frame, long TickMs)> ExitSeekTrailSamples => _exitTrailSamples;

    public void SetTrailRecording(bool active)
    {
        _trailActive = active;
        if (!active)
        {
            _trailSamples.Clear();
            _exitTrailSamples.Clear();
            InvalidatePlayheadOnly();
            return;
        }

        RecordTrailSample(_playheadFrame, _trailSamples);
        if (_exitPlayheadFrame >= 0)
        {
            RecordTrailSample(_exitPlayheadFrame, _exitTrailSamples);
        }
    }

    public bool LoopEnabled { get; set; }

    public bool CenterLocked { get; private set; }

    public bool IsInteracting => _dragging || _markerDragging || _scrubbing;

    public bool IsScrubbing => _scrubbing;

    public IReadOnlyCollection<long> SelectedMarkerFrames => _selectedMarkerFrames;

    public void RestoreSelectedMarkers(IEnumerable<long> frames)
    {
        _selectedMarkerFrames.Clear();
        if (_document is not null)
        {
            foreach (var frame in frames)
            {
                if (_document.HasMarkerAt(frame))
                {
                    _selectedMarkerFrames.Add(frame);
                }
            }
        }

        InvalidateStaticLayer();
    }

    public bool HasSelectedMarkers => _selectedMarkerFrames.Count > 0;

    public bool HasSelectedRegions => _selectedRegionEdges.Count > 0;

    public IReadOnlyList<WaveSelection> SelectedRegions
    {
        get
        {
            if (_selectedRegionEdges.Count == 0)
            {
                return [];
            }

            var list = new List<WaveSelection>();
            foreach (var edge in _selectedRegionEdges)
            {
                if (!list.Contains(edge.Range))
                {
                    list.Add(edge.Range);
                }
            }

            return list;
        }
    }

    public bool HasSelectedTimelineItems => HasTimelineDragSelection();

    public bool TryNudgeSelectedTimeline(long desiredDelta, out long applied)
    {
        applied = 0;
        if (_document is null || !HasTimelineDragSelection() || desiredDelta == 0)
        {
            return false;
        }

        var markerOrigins = _selectedMarkerFrames.OrderBy(frame => frame).ToArray();
        var regionOrigins = _selectedRegionEdges.ToArray();
        var loopStart = _loopStartSelected;
        var loopEnd = _loopEndSelected;
        var loopOrigin = _document.SampleLoop;
        var follow = IsSelectedTimelineFrame(_playheadFrame);
        applied = ApplyTimelineDelta(markerOrigins, regionOrigins, loopStart, loopEnd, loopOrigin, desiredDelta);
        if (applied == 0)
        {
            return false;
        }

        RemapSelectedMarkerFrames(markerOrigins, applied);
        RemapSelectedRegionEdges(regionOrigins, applied);
        _loopStartSelected = loopStart;
        _loopEndSelected = loopEnd;
        if (follow)
        {
            var next = _playheadFrame + applied;
            _playheadFrame = next;
            _document.CursorFrame = next;
        }

        InvalidateStaticLayer();
        InvalidatePlayheadLayer();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public double TimeZoom => _timeZoom;

    public double AmpZoom => _ampZoom;

    public double ViewStart => _viewStart;

    public double ViewSpanFrames
    {
        get
        {
            if (_document is null || _document.FrameCount <= 0 || ContentWidth <= 0)
            {
                return 1;
            }

            return Math.Max(1d, _document.FrameCount / Math.Max(1d, _timeZoom));
        }
    }

    public void Refresh() => InvalidateWaveform();

    public void ApplySpeakerLayout(ChannelLayout speaker, int[]? fileChannelMap = null)
    {
        _speakerLayout = speaker;
        _fileChannelMap = fileChannelMap ?? [];
        InvalidateStaticLayer();
    }

    public void SetSoloMask(int mask)
    {
        mask = ChannelSolo.ClampMask(mask, _document?.Channels ?? 1);
        if (_soloMask == mask)
        {
            return;
        }

        _soloMask = mask;
        InvalidateWaveform();
        InvalidateStaticLayer();
        InvalidatePlayheadLayer();
    }

    public void RefreshAppearance()
    {
        _mouseGuideBrush = WpfControlHelpers.FrozenBrush(Theme.Get("MouseGuideBrush"));
        _mouseGuideOnSelectionBrush = WpfControlHelpers.FrozenBrush(Theme.Get("MouseGuideOnSelectionBrush"));
        _timeLabelCache.Clear();
        _dbLabelCache.Clear();
        _waveBgra = 0;
        _waveOverlayBgra = 0;
        _loudnessWaveBgra = 0;
        _zeroBgra = 0;
        _playheadCore = null;
        _exitPlayheadCore = null;
        InvalidateWaveform();
        ApplyMouseGuideOverlay();
    }

    public void RefreshOverlay() => InvalidatePlayheadLayer();

    public bool SpectrogramVisible =>
        _spectrogramMode is SpectrogramViewMode.Spectrogram or SpectrogramViewMode.Overlay;

    public bool LoudnessVisible => _spectrogramMode == SpectrogramViewMode.Loudness;

    public WaveformAnalysisView AnalysisView => _spectrogramMode switch
    {
        SpectrogramViewMode.Spectrogram => WaveformAnalysisView.Spectrogram,
        SpectrogramViewMode.Overlay => WaveformAnalysisView.Overlay,
        SpectrogramViewMode.Loudness => WaveformAnalysisView.Loudness,
        _ => WaveformAnalysisView.Waveform,
    };

    public event EventHandler? AnalysisViewChanged;

    public void SetAnalysisView(WaveformAnalysisView view)
    {
        var next = view switch
        {
            WaveformAnalysisView.Spectrogram => SpectrogramViewMode.Spectrogram,
            WaveformAnalysisView.Overlay => SpectrogramViewMode.Overlay,
            WaveformAnalysisView.Loudness => SpectrogramViewMode.Loudness,
            _ => SpectrogramViewMode.Off,
        };
        if (_spectrogramMode == next)
        {
            return;
        }

        _spectrogramMode = next;
        ApplyAnalysisView();
    }

    public void ToggleSpectrogramView() =>
        SetAnalysisView(_spectrogramMode switch
        {
            SpectrogramViewMode.Spectrogram => WaveformAnalysisView.Overlay,
            SpectrogramViewMode.Overlay => WaveformAnalysisView.Spectrogram,
            _ => WaveformAnalysisView.Spectrogram,
        });

    public void CycleSpectrogramView() =>
        SetAnalysisView(_spectrogramMode switch
        {
            SpectrogramViewMode.Spectrogram => WaveformAnalysisView.Overlay,
            SpectrogramViewMode.Overlay => WaveformAnalysisView.Waveform,
            _ => WaveformAnalysisView.Spectrogram,
        });

    public void ExitSpectrogramView()
    {
        if (SpectrogramVisible)
        {
            SetAnalysisView(WaveformAnalysisView.Waveform);
        }
    }

    public void ToggleLoudnessView() =>
        SetAnalysisView(
            _spectrogramMode == SpectrogramViewMode.Loudness
                ? WaveformAnalysisView.Waveform
                : WaveformAnalysisView.Loudness);

    public void ExitLoudnessView()
    {
        if (LoudnessVisible)
        {
            SetAnalysisView(WaveformAnalysisView.Waveform);
        }
    }

    public double LoudnessTargetLufs
    {
        get => _loudnessTargetLufs;
        set
        {
            var next = LoudnessMeterEngine.ClampTargetLufs(value);
            if (Math.Abs(_loudnessTargetLufs - next) < 1e-6)
            {
                return;
            }

            _loudnessTargetLufs = next;
            if (LoudnessVisible)
            {
                _waveDirty = true;
                InvalidateStaticLayer();
            }
        }
    }

    public void ToggleAnalysisView()
    {
        _spectrogramMode = _spectrogramMode switch
        {
            SpectrogramViewMode.Off => SpectrogramViewMode.Spectrogram,
            SpectrogramViewMode.Spectrogram => SpectrogramViewMode.Overlay,
            SpectrogramViewMode.Overlay => SpectrogramViewMode.Loudness,
            _ => SpectrogramViewMode.Off,
        };
        ApplyAnalysisView();
    }

    private void ApplyAnalysisView()
    {
        if (SpectrogramVisible && _document is not null)
        {
            _spectrogram.RequestCache(_document, () => Dispatcher.BeginInvoke(InvalidateStaticLayerForSpectrogram));
        }

        if (LoudnessVisible && _document is not null)
        {
            _loudness.Ensure(_document, () => Dispatcher.BeginInvoke(InvalidateStaticLayerForLoudness));
        }

        SyncSpectrogramBoostBar();
        InvalidateStaticLayer();
        AnalysisViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool NudgeSpectrogramBoost(int direction)
    {
        if (!SpectrogramVisible || direction == 0)
        {
            return false;
        }

        _boostBar.Nudge(direction);
        return true;
    }

    private void OnSpectrogramBoostChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _spectrogram.DisplayBoostDb = SpectrogramEngine.DisplayBoostDbFromUnit(_boostBar.BoostUnit);
        if (SpectrogramVisible)
        {
            InvalidateStaticLayer();
        }
    }

    private void SyncSpectrogramBoostBar()
    {
        var show = SpectrogramVisible;
        _boostBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _boostBar.IsEnabled = show && _document is not null;
        if (!show || ActualHeight <= 1)
        {
            return;
        }

        var wave = WaveformBounds(new Rect(RenderSize));
        var pad = DesignMetrics.SpectrogramBoostBarPad;
        double? labelW = _spectrogramMode == SpectrogramViewMode.Overlay
            ? WpfControlHelpers.MonoText("-12", 9, Brushes.Transparent, VisualTreeHelper.GetDpi(this).PixelsPerDip).Width
            : null;
        var left = DesignMetrics.SpectrogramBoostBarLeft(labelW);
        _boostBar.Width = DesignMetrics.SpectrogramBoostThumbSize;
        _boostBar.Height = Math.Max(0, wave.Height - pad * 2);
        _boostBar.Margin = new Thickness(left, wave.Y + pad, 0, pad);
    }

    public void InvalidateSpectrogramCache()
    {
        _spectrogram.InvalidateCache();
        _loudness.Invalidate();
        if (SpectrogramVisible && _document is not null)
        {
            _spectrogram.RequestCache(_document, () => Dispatcher.BeginInvoke(InvalidateStaticLayerForSpectrogram));
        }

        if (LoudnessVisible && _document is not null)
        {
            _loudness.Ensure(_document, () => Dispatcher.BeginInvoke(InvalidateStaticLayerForLoudness));
        }
    }

    public void DisposeSpectrogram()
    {
        _spectrogram.Dispose();
        _loudness.Dispose();
    }

    public void ZoomTimeIn(bool anchorPlayhead = true) =>
        SetTimeZoom(_timeZoom * TimeZoomStep, AnchorFrame(anchorPlayhead));

    public void ZoomTimeOut(bool anchorPlayhead = true) =>
        SetTimeZoom(_timeZoom / TimeZoomStep, AnchorFrame(anchorPlayhead));

    public void ZoomTimeToMax()
    {
        var next = _timeZoom + 1e-9 < TimeZoomStepMax ? TimeZoomStepMax : TimeZoomMax;
        SetTimeZoom(next, AnchorFrame(true));
    }

    public void ResetTimeZoom() => SetTimeZoom(1d, 0);

    public void ZoomAmpIn() => SetAmpZoom(_ampZoom * TimeZoomStep);

    public void ZoomAmpOut() => SetAmpZoom(_ampZoom / TimeZoomStep);

    public void ZoomAmpToMax() => SetAmpZoom(AmpZoomMax);

    public void ResetAmpZoom() => SetAmpZoom(1d);

    public void WheelTimeZoomAtPlayhead(int delta) =>
        WheelTimeZoomAtFrame(delta, _playheadFrame);

    public void WheelTimeZoomAtFrame(int delta, double anchorFrame)
    {
        var factor = delta > 0 ? WheelTimeStep : 1d / WheelTimeStep;
        SetTimeZoom(_timeZoom * factor, anchorFrame);
    }

    public void WheelAmpZoom(int delta) =>
        SetAmpZoom(delta > 0 ? _ampZoom * TimeZoomStep : _ampZoom / TimeZoomStep);

    public void PanByVisibleFraction(double fraction)
    {
        SetViewStart(_viewStart + ViewSpanFrames * fraction);
    }

    public bool SamplePointsVisible =>
        _document is not null
        && ContentWidth > 0
        && ViewSpanFrames <= Math.Min(SamplePointMaxVisibleFrames, ContentWidth);

    public long NudgeStepFrames
    {
        get
        {
            if (SamplePointsVisible)
            {
                return 1;
            }

            var framesPerPixel = ViewSpanFrames / Math.Max(1d, ContentWidth);
            return Math.Max(1L, (long)Math.Round(framesPerPixel));
        }
    }

    public long ViewLeftFrame => ClampFrame((long)Math.Round(_viewStart));

    public long ViewRightFrame => ClampFrame((long)Math.Round(_viewStart + ViewSpanFrames));

    public void SeekToViewEdge(int direction)
    {
        if (_document is null)
        {
            return;
        }

        MovePlayhead(direction < 0 ? ViewLeftFrame : ViewRightFrame);
    }

    public void ExtendSelectionToViewEdge(int direction)
    {
        if (_document is null)
        {
            return;
        }

        ExtendSelectionTo(direction < 0 ? ViewLeftFrame : ViewRightFrame);
    }

    public void SeekByVisibleFraction(double fraction)
    {
        if (_document is null)
        {
            return;
        }

        MovePlayhead(ClampFrame(_playheadFrame + (long)Math.Round(ViewSpanFrames * fraction)));
    }

    public void NudgePlayhead(int direction)
    {
        if (_document is null)
        {
            return;
        }

        MovePlayhead(ClampFrame(_playheadFrame + direction * NudgeStepFrames));
    }

    public void NudgeSelection(int direction)
    {
        if (_document is null)
        {
            return;
        }

        ExtendSelectionTo(ClampFrame(_playheadFrame + direction * NudgeStepFrames));
    }

    public void ExtendSelectionByVisibleFraction(double fraction)
    {
        if (_document is null)
        {
            return;
        }

        ExtendSelectionTo(ClampFrame(_playheadFrame + (long)Math.Round(ViewSpanFrames * fraction)));
    }

    public void SeekToMarker(int direction)
    {
        if (_document is null)
        {
            return;
        }

        var target = ClampFrame(_document.AdjacentMarkerFrame(_playheadFrame, direction));
        ClearSelection();
        _keyboardSelectAnchor = null;
        KeepPlayheadAtScreenOffset(target);
        CommitCursor(target, ensureVisible: false);
    }

    public bool SeekToMarkerId(int id, bool extendSelection)
    {
        if (_document is null || id < 1 || id > _document.Markers.Count)
        {
            return false;
        }

        var frame = _document.Markers[id - 1].Frame;
        if (extendSelection)
        {
            ExtendSelectionTo(frame);
        }
        else
        {
            MovePlayhead(frame);
        }

        return true;
    }

    public void ExtendSelectionToMarker(int direction)
    {
        if (_document is null)
        {
            return;
        }

        ExtendSelectionTo(_document.AdjacentMarkerFrame(_playheadFrame, direction));
    }

    public void SelectToDocumentEdge(int direction)
    {
        if (_document is null)
        {
            return;
        }

        _keyboardSelectAnchor = _playheadFrame;
        ExtendSelectionTo(direction < 0 ? 0 : _document.FrameCount);
    }

    public void ExtendSelectionTo(long frame)
    {
        if (_document is null)
        {
            return;
        }

        frame = ClampFrame(frame);
        var anchor = ResolveKeyboardSelectAnchor();
        _keyboardSelectAnchor = anchor;
        var next = WaveSelection.FromPoints(anchor, frame);
        _document.Selection = next;
        _document.CursorFrame = frame;
        _playheadFrame = frame;
        EnsureFrameVisible(frame);
        InvalidatePlayheadLayer();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        CursorCommitted?.Invoke(this, frame);
    }

    private long ResolveKeyboardSelectAnchor()
    {
        if (_document is null)
        {
            return 0;
        }

        if (_keyboardSelectAnchor is long stored)
        {
            return ClampFrame(stored);
        }

        if (_document.Selection.IsEmpty)
        {
            return _playheadFrame;
        }

        return _playheadFrame <= _document.Selection.StartFrame
            ? _document.Selection.EndFrame
            : _document.Selection.StartFrame;
    }

    private void EnsureFrameVisible(long frame)
    {
        if (_document is null)
        {
            return;
        }

        var span = ViewSpanFrames;
        if (span <= 1)
        {
            return;
        }

        if (CenterLocked)
        {
            SetViewStart(frame - span * 0.5);
            return;
        }

        // Keep a small inset so the playhead (and glow) is never clipped at the edge.
        var margin = Math.Max(1d, span * 0.02);
        if (frame < _viewStart + margin)
        {
            SetViewStart(frame - margin);
        }
        else if (frame > _viewStart + span - margin)
        {
            SetViewStart(frame - span + margin);
        }
    }

    /// <summary>
    /// Scroll so <paramref name="frame"/> stays at the same X as the current playhead
    /// (or center when center-locked). Keeps the seek bar's on-screen position across jumps.
    /// </summary>
    private void KeepPlayheadAtScreenOffset(long frame)
    {
        var span = ViewSpanFrames;
        if (span <= 1)
        {
            return;
        }

        var offset = CenterLocked
            ? span * 0.5
            : Math.Clamp((double)_playheadFrame - _viewStart, 0d, span);
        SetViewStart(frame - offset);
    }

    public void PanTimeToStart() => SetViewStart(0);

    public void PanTimeToEnd()
    {
        if (_document is null)
        {
            return;
        }

        SetViewStart(_document.FrameCount - ViewSpanFrames);
    }

    public void CenterViewOnPlayhead()
    {
        SetViewStart(_playheadFrame - ViewSpanFrames * 0.5);
    }

    public void LockCenterToPlayhead()
    {
        CenterLocked = true;
        CenterViewOnPlayhead();
    }

    public void UnlockCenter() => CenterLocked = false;

    public void JumpVisiblePercent(double percent)
    {
        MovePlayhead(VisiblePercentFrame(percent));
    }

    public void ExtendSelectionToVisiblePercent(double percent)
    {
        if (_document is null)
        {
            return;
        }

        ExtendSelectionTo(VisiblePercentFrame(percent));
    }

    private long VisiblePercentFrame(double percent) =>
        ClampFrame((long)Math.Round(_viewStart + ViewSpanFrames * percent));

    private void MovePlayhead(long frame)
    {
        ClearSelection();
        _keyboardSelectAnchor = null;
        frame = ClampFrame(frame);
        EnsureFrameVisible(frame);
        CommitCursor(frame);
    }

    public void SeekKeepingSelection(long frame)
    {
        if (_document is null)
        {
            return;
        }

        frame = ClampFrame(frame);
        EnsureFrameVisible(frame);
        CommitCursor(frame);
    }

    public void SetSelection(WaveSelection range)
    {
        if (_document is null)
        {
            return;
        }

        _document.Selection = range.Clamp(_document.FrameCount);
        _keyboardSelectAnchor = _document.Selection.IsEmpty ? null : _document.Selection.StartFrame;
        InvalidatePlayheadLayer();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FollowPlayhead()
    {
        if (_document is null)
        {
            return;
        }

        var span = ViewSpanFrames;
        if (span <= 1)
        {
            return;
        }

        if (CenterLocked)
        {
            SetViewStart(_playheadFrame - span * 0.5, playbackFollow: true);
            return;
        }

        var margin = Math.Max(1d, span * 0.08);
        var frame = (double)_playheadFrame;
        if (frame > _viewStart + span - margin)
        {
            SetViewStart(frame - span + margin, playbackFollow: true);
            return;
        }

        if (frame < _viewStart)
        {
            SetViewStart(frame - margin, playbackFollow: true);
        }
    }

    public bool TakePendingSelectionPrerollJump()
    {
        var pending = _pendingSelectionPrerollJump;
        _pendingSelectionPrerollJump = false;
        return pending;
    }

    public void SelectAll()
    {
        SelectAll(commitCursor: true);
    }

    private void SelectAll(bool commitCursor)
    {
        if (_document is null)
        {
            return;
        }

        _keyboardSelectAnchor = null;
        _document.Selection = new WaveSelection(0, _document.FrameCount);
        _document.CursorFrame = 0;
        _playheadFrame = 0;
        InvalidatePlayheadLayer();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (commitCursor)
        {
            CursorCommitted?.Invoke(this, 0);
        }
    }

    public void SelectSpanAt(long frame)
    {
        if (_document is null)
        {
            return;
        }

        _pendingSelectionPrerollJump = true;
        var range = _document.DoubleClickSpanAt(frame);
        if (range.IsEmpty)
        {
            _pendingSelectionPrerollJump = false;
            return;
        }

        var loop = _document.SampleLoop;
        var isSampleLoop = !loop.IsEmpty
            && range.StartFrame == loop.StartFrame
            && range.EndFrame == loop.EndFrame;
        var cursor = range.StartFrame;
        if (isSampleLoop)
        {
            var preroll = _document.SampleRate * 3L;
            cursor = range.Length < preroll ? range.StartFrame : range.EndFrame - preroll;
        }

        _keyboardSelectAnchor = range.StartFrame;
        _document.Selection = range;
        _document.CursorFrame = cursor;
        _playheadFrame = cursor;
        EnsureFrameVisible(cursor);
        InvalidatePlayheadLayer();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectRegion(WaveSelection range)
    {
        if (_document is null || range.IsEmpty)
        {
            return;
        }

        _keyboardSelectAnchor = range.StartFrame;
        _document.Selection = range;
        _document.CursorFrame = range.StartFrame;
        _playheadFrame = range.StartFrame;
        EnsureFrameVisible(range.StartFrame);
        InvalidatePlayheadLayer();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void BeginScrubAtFrame(long frame)
    {
        if (_document is null || _scrubbing)
        {
            return;
        }

        EndMarkerCommentEdit(commit: true);
        _scrubbing = true;
        _selecting = false;
        frame = ClampFrame(frame);
        PreviewInteraction(frame, force: true);
        ScrubStarted?.Invoke(this, frame);
    }

    public void PreviewScrubAtFrame(long frame)
    {
        if (!_scrubbing || _document is null)
        {
            return;
        }

        frame = ClampFrame(frame);
        if (frame != _playheadFrame)
        {
            PreviewInteraction(frame);
        }

        // 位置が同じでも Capture を回し、ホールド切れの無音を防ぐ。
        ScrubPreviewed?.Invoke(this, frame);
    }

    public void EndScrubAtFrame(long frame, bool commit) => FinishScrub(commit, frame);

    public void AbandonScrub() => CancelPointerInteraction();

    private void CancelPointerInteraction()
    {
        _scrubbing = false;
        _dragging = false;
        _selecting = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Cursor = Cursors.IBeam;
    }

    public bool CancelScrub()
    {
        if (!_scrubbing)
        {
            return false;
        }

        FinishScrub(commit: false, _playheadFrame);
        return true;
    }

    public bool ClearSelection()
    {
        if (_scrubbing)
        {
            FinishScrub(commit: false, _playheadFrame);
        }

        if (_dragging)
        {
            _dragging = false;
            _selecting = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        if (_document is null || _document.Selection.IsEmpty)
        {
            return false;
        }

        _document.Selection = WaveSelection.Empty;
        _keyboardSelectAnchor = null;
        InvalidatePlayheadLayer();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool CancelMarkerInteraction()
    {
        if (_markerDragging)
        {
            FinishMarkerDrag(commit: false);
            return true;
        }

        return ClearMarkerSelection();
    }

    public bool ClearMarkerSelection() => ClearTimelineSelection(refresh: true);

    private bool ClearTimelineSelection(bool refresh)
    {
        var changed = HasTimelineDragSelection();
        _selectedMarkerFrames.Clear();
        _selectedRegionEdges.Clear();
        _loopStartSelected = false;
        _loopEndSelected = false;
        if (changed && refresh)
        {
            InvalidateStaticLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public void SelectMarkerFrames(IEnumerable<long> frames, bool additive = false)
    {
        if (!additive)
        {
            _selectedMarkerFrames.Clear();
            _selectedRegionEdges.Clear();
            _loopStartSelected = false;
            _loopEndSelected = false;
        }

        if (_document is not null)
        {
            foreach (var frame in frames)
            {
                if (_document.HasMarkerAt(frame))
                {
                    _selectedMarkerFrames.Add(frame);
                    if (!additive)
                    {
                        _markerSelectAnchor = frame;
                    }
                }
            }
        }

        InvalidateStaticLayer();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PruneMarkerSelection()
    {
        if (_document is null)
        {
            if (ClearTimelineSelection(refresh: false))
            {
                MarkersChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        var changed = _selectedMarkerFrames.RemoveWhere(frame => !_document.HasMarkerAt(frame)) > 0;
        if (_markerSelectAnchor is long anchor && !_document.HasMarkerAt(anchor)
            && !IsSelectedRegionOrLoopFrame(anchor))
        {
            _markerSelectAnchor = null;
        }

        changed |= _selectedRegionEdges.RemoveWhere(edge => !_document.Regions.Contains(edge.Range)) > 0;
        if ((_loopStartSelected || _loopEndSelected) && _document.SampleLoop.IsEmpty)
        {
            _loopStartSelected = false;
            _loopEndSelected = false;
            changed = true;
        }

        if (changed)
        {
            InvalidateStaticLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<long> MarkerNudgeFrames(bool includePrevious)
    {
        if (_document is null || !_document.HasMarkerAt(_playheadFrame))
        {
            return [];
        }

        var frames = new List<long> { _playheadFrame };
        if (includePrevious)
        {
            var previous = PreviousMarkerFrame(_playheadFrame);
            if (previous is long frame)
            {
                frames.Insert(0, frame);
            }
        }

        return frames;
    }

    public bool TrySelectTimelineAtPlayhead(bool includePair)
    {
        if (_document is null)
        {
            return false;
        }

        var playhead = _playheadFrame;
        var markers = MarkerNudgeFrames(includePair);
        var loop = _document.SampleLoop;
        var loopStart = !loop.IsEmpty && loop.StartFrame == playhead;
        var loopEnd = !loop.IsEmpty && loop.EndFrame == playhead;
        if (includePair && (loopStart || loopEnd))
        {
            loopStart = true;
            loopEnd = true;
        }

        var regionEdges = new List<RegionEdge>();
        foreach (var region in _document.Regions)
        {
            var onStart = region.StartFrame == playhead;
            var onEnd = region.EndFrame == playhead;
            if (!onStart && !onEnd)
            {
                continue;
            }

            if (includePair || onStart)
            {
                regionEdges.Add(new RegionEdge(region, true));
            }

            if (includePair || onEnd)
            {
                regionEdges.Add(new RegionEdge(region, false));
            }
        }

        if (markers.Count == 0 && !loopStart && !loopEnd && regionEdges.Count == 0)
        {
            return false;
        }

        _selectedMarkerFrames.Clear();
        foreach (var frame in markers)
        {
            _selectedMarkerFrames.Add(frame);
        }

        _selectedRegionEdges.Clear();
        foreach (var edge in regionEdges)
        {
            _selectedRegionEdges.Add(edge);
        }

        _loopStartSelected = loopStart;
        _loopEndSelected = loopEnd;
        _markerSelectAnchor = playhead;
        InvalidateStaticLayer();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private long? PreviousMarkerFrame(long frame)
    {
        if (_document is null)
        {
            return null;
        }

        long? previous = null;
        foreach (var marker in _document.Markers)
        {
            if (marker.Frame >= frame)
            {
                break;
            }

            previous = marker.Frame;
        }

        return previous;
    }

    public long FrameAt(double x) => PointerFrame(x);

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        SetMouseGuideFromX(e.GetPosition(this).X);
        ApplyMouseGuideOverlay();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_document is null)
        {
            return;
        }

        var clickPos = e.GetPosition(this);
        if (TryHitChannelLabel(clickPos, out var labelChannel))
        {
            ChannelLabelClicked?.Invoke(
                this,
                (
                    labelChannel,
                    (Keyboard.Modifiers & ModifierKeys.Control) != 0,
                    (Keyboard.Modifiers & ModifierKeys.Shift) != 0));
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 3)
        {
            var pos = clickPos;
            if (IsInDbScaleLane(pos))
            {
                e.Handled = true;
                return;
            }

            EndMarkerCommentEdit(commit: false);
            ClearMarkerSelection();
            SelectAll();
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2)
        {
            var pos = clickPos;
            if (IsInDbScaleLane(pos))
            {
                e.Handled = true;
                return;
            }
            if (TryHitRegionFlag(pos, out var region, out var regionFrame))
            {
                SelectMarkerFrames([]);
                _selectedRegionEdges.Add(ToRegionEdge(region, regionFrame));
                BeginRegionNameEdit(region);
            }
            else if (TryHitMarkerFlag(pos, out var marker))
            {
                SelectMarkerFrames([marker.Frame]);
                BeginMarkerCommentEdit(marker);
            }
            else
            {
                ClearMarkerSelection();
                SelectSpanAt(FrameAt(pos.X));
            }

            e.Handled = true;
            return;
        }

        var start = clickPos;
        if (IsInDbScaleLane(start))
        {
            e.Handled = true;
            return;
        }

        if (TryHitRegionFlag(start, out var regionHit, out var hitFrame))
        {
            BeginRegionFlagInteraction(regionHit, hitFrame, start);
            e.Handled = true;
            return;
        }

        if (TryHitMarkerFlag(start, out var hit))
        {
            BeginMarkerFlagInteraction(hit, start);
            e.Handled = true;
            return;
        }

        if (TryHitSampleLoopBar(start))
        {
            BeginLoopBarInteraction(start);
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            BeginScrubInteraction(start);
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            ClearMarkerSelection();
        }

        CaptureMouse();
        _dragging = true;
        _selecting = false;
        _dragStart = start;
        var frame = FrameAt(_dragStart.X);
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            _anchorFrame = _document.Selection.IsEmpty ? _document.CursorFrame : _document.Selection.StartFrame;
            _keyboardSelectAnchor = _anchorFrame;
            _document.Selection = WaveSelection.FromPoints(_anchorFrame, frame);
            _selecting = true;
            InvalidatePlayheadLayer();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _anchorFrame = frame;
            _keyboardSelectAnchor = null;
            if (!_document.Selection.IsEmpty)
            {
                _document.Selection = WaveSelection.Empty;
                InvalidatePlayheadLayer();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        PreviewInteraction(frame, force: true);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        SetMouseGuideFromX(pos.X);
        QueueMouseGuideOverlay();

        if (_dragging && _scrubbing && _document is not null)
        {
            Cursor = Cursors.ScrollWE;
            var scrubFrame = FrameAt(pos.X);
            if (scrubFrame != _playheadFrame)
            {
                PreviewInteraction(scrubFrame);
                ScrubPreviewed?.Invoke(this, scrubFrame);
            }

            return;
        }

        if (_markerDragging && _document is not null)
        {
            Cursor = Cursors.SizeWE;
            if (!_markerDragMoved && Math.Abs(pos.X - _dragStart.X) >= DragThresholdPx)
            {
                _markerDragMoved = true;
            }

            if (_markerDragMoved)
            {
                ApplyMarkerDrag(DesiredTimelineDragDelta(pos.X));
            }

            return;
        }

        if (!_dragging)
        {
            UpdateHoverCursor(pos);
        }

        if (_dragging && _document is not null)
        {
            if (!_selecting && Math.Abs(pos.X - _dragStart.X) >= DragThresholdPx)
            {
                _selecting = true;
            }

            var frame = FrameAt(pos.X);
            if (_selecting)
            {
                var next = WaveSelection.FromPoints(_anchorFrame, frame);
                if (next != _document.Selection)
                {
                    _document.Selection = next;
                    InvalidatePlayheadLayer();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            PreviewInteraction(frame);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_markerDragging)
        {
            FinishMarkerDrag(commit: true);
            e.Handled = true;
            return;
        }

        if (_dragging && _scrubbing)
        {
            FinishScrub(commit: true, FrameAt(e.GetPosition(this).X));
            e.Handled = true;
            return;
        }

        if (!_dragging)
        {
            return;
        }

        var wasSelecting = _selecting;
        ReleaseMouseCapture();
        _dragging = false;
        _selecting = false;
        Cursor = Cursors.IBeam;
        if (_document is null)
        {
            return;
        }

        var frame = FrameAt(e.GetPosition(this).X);
        if (wasSelecting)
        {
            PreviewInteraction(frame);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            CommitCursor(frame);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_markerDragging)
        {
            FinishMarkerDrag(commit: true);
        }

        if (_dragging && _scrubbing)
        {
            FinishScrub(commit: true, FrameAt(e.GetPosition(this).X));
        }

        if (_dragging)
        {
            _dragging = false;
            _selecting = false;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!_dragging)
        {
            ClearMouseGuide();
            ApplyMouseGuideOverlay();
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (IsEditingMarkerComment || _dragging || _markerDragging || _scrubbing)
        {
            return;
        }

        var pos = e.GetPosition(this);
        var hitMarker = TryHitMarkerFlag(pos, out var marker);
        var hitRegion = TryHitRegionFlag(pos, out var region);
        var hitLoop = TryHitSampleLoopBar(pos);
        IReadOnlyList<long> frames = [];
        if (hitMarker)
        {
            frames = _selectedMarkerFrames.Contains(marker.Frame) && _selectedMarkerFrames.Count > 1
                ? _selectedMarkerFrames.ToArray()
                : [marker.Frame];
        }

        Focus();
        e.Handled = true;
        ContextMenuRequested?.Invoke(this, new WaveformContextHit
        {
            Frame = PointerFrame(pos.X),
            MarkerFrames = frames,
            Region = hitRegion ? region : WaveSelection.Empty,
            HitLoop = hitLoop,
        });
    }

    internal void PaintStatic(DrawingContext dc)
    {
        // TickCount64 は分解能が約 15ms で軽い描画を過大測定し追従がカクつくため、
        // 高分解能の Stopwatch タイムスタンプで測る。
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        PaintStaticCore(dc);
        var endedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsedMs = (endedAt - startedAt) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        // 追従スクロールの再描画間引きに使う実測コスト（EMA）。急に重くなった直後にも
        // 即応できるよう、直近 1 回の実測と終了時刻も別に持つ（SetViewStart の追従分岐参照）。
        _staticPaintMs = _staticPaintMs * 0.7 + elapsedMs * 0.3;
        _staticPaintLastMs = elapsedMs;
        _staticPaintEndMs = endedAt * 1000d / System.Diagnostics.Stopwatch.Frequency;
    }

    private void PaintStaticCore(DrawingContext dc)
    {
        var bounds = new Rect(_staticHost.RenderSize);
        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("WaveformBackBrush")), null, bounds);
        if (Math.Abs(_appliedMarkerLaneHeight - MarkerLaneHeight) > 0.01)
        {
            _appliedMarkerLaneHeight = MarkerLaneHeight;
            _waveDirty = true;
        }

        var wave = WaveformBounds(bounds);
        var span = _document is null ? 0 : ViewSpanFrames;
        var start = _viewStart;
        DrawDbScaleWell(dc, bounds);
        SyncSpectrogramBoostBar();
        DrawMarkerLane(dc, bounds, start, span);
        DrawTimeLane(dc, bounds, start, span);
        if (_document is null || _document.FrameCount <= 0 || wave.Width <= 1 || wave.Height <= 1)
        {
            return;
        }

        var overlay = _spectrogramMode == SpectrogramViewMode.Overlay;
        var loudness = LoudnessVisible;
        if (SpectrogramVisible)
        {
            _spectrogram.Draw(dc, wave, _document, start, span, this, reuseBitmap: _deferWaveReload);
        }

        if (loudness)
        {
            _loudness.Ensure(_document, () => Dispatcher.BeginInvoke(InvalidateStaticLayerForLoudness));
            _loudness.DrawUnderlay(dc, wave, _loudnessTargetLufs);
        }

        if (!SpectrogramVisible || overlay || loudness)
        {
            if (!overlay && !loudness)
            {
                MarkerRolePaint.DrawRegion(dc, _document, wave, start, span, "RegionWaveFillBrush");
                MarkerRolePaint.DrawSampleLoop(dc, _document, wave, start, span, "SampleLoopWaveFillBrush");
                MarkerRolePaint.DrawBackgrounds(dc, _document, wave, start, span);
            }

            EnsureWaveformBitmap(wave);
            if (loudness)
            {
                dc.PushOpacity(0.75);
                DrawWaveformImage(dc, wave);
                dc.Pop();
            }
            else
            {
                DrawWaveformImage(dc, wave);
            }

            if (!overlay && !loudness)
            {
                MarkerRolePaint.DrawRemoveOverlays(dc, _document, wave, start, span);
            }
        }

        if (loudness)
        {
            _loudness.DrawScale(
                dc,
                DbScaleBounds(bounds),
                wave,
                _loudnessTargetLufs,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (_loudness.Current is { } profile)
            {
                _loudness.DrawOverlay(
                    dc,
                    wave,
                    profile,
                    _loudnessTargetLufs,
                    start,
                    span,
                    _previewGainAtFrame);
            }
        }

        if (!SpectrogramVisible && !loudness)
        {
            var channels = Math.Max(1, _document.Channels);
            var laneGap = channels > 1 ? 4d : 0d;
            var laneHeight = (wave.Height - laneGap * (channels - 1)) / channels;
            DrawDbScaleTicks(dc, bounds, wave, channels, laneGap, laneHeight);
            DrawChannelLabels(dc, bounds, wave, channels, laneGap, laneHeight);
        }
        else if (overlay)
        {
            ResolveWaveLane(overlay: true, wave.Height, 1, 0, _ampZoom, out var laneHeight, out var origin, out var ampHeight);
            var overlayWave = new Rect(wave.X, wave.Y + origin, wave.Width, laneHeight);
            DrawDbScaleTicks(dc, bounds, overlayWave, channels: 1, laneGap: 0, laneHeight, ampHeight);
        }
    }

    internal void PaintOverlay(DrawingContext dc)
    {
        var bounds = new Rect(_overlayHost.RenderSize);
        if (_document is null || _document.FrameCount <= 0 || bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var span = ViewSpanFrames;
        var start = _viewStart;
        if (!_document.Selection.IsEmpty)
        {
            DrawInvertedSelection(dc, bounds, start, span, _document.Selection);
        }

        if (LoopEnabled && !_document.Selection.IsEmpty)
        {
            DrawRange(dc, bounds, start, span, _document.Selection, Theme.Get("LoopRangeFillBrush"));
        }

        RefreshFlagStacks(bounds, start, span);
        DrawRegionFlags(dc, bounds, start, span);
        DrawMarkers(dc, bounds, start, span);
    }

    internal void PaintPlayhead(DrawingContext dc)
    {
        var bounds = new Rect(_playheadHost.RenderSize);
        if (_document is null || _document.FrameCount <= 0 || bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        DrawPlayhead(dc, bounds, _viewStart, ViewSpanFrames);
    }

    private void EnsureWaveformBitmap(Rect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var span = ViewSpanFrames;
        var scaleX = Math.Max(1e-6, dpi.DpiScaleX);
        var scaleY = Math.Max(1e-6, dpi.DpiScaleY);
        var width = Math.Max(1, (int)Math.Round(bounds.Width * scaleX));
        var height = Math.Max(1, (int)Math.Round(bounds.Height * scaleY));
        WaveScroll.Quantize(_viewStart, span, width, out var quantStart, out var bmpSpan, out var bmpWidth);
        if (_deferWaveReload && _waveBitmap is not null && !_waveDirty)
        {
            return;
        }

        if (!_waveDirty
            && _waveBitmap is not null
            && _wavePixelWidth == bmpWidth
            && _wavePixelHeight == height
            && _waveDipSize == bounds.Size
            && Math.Abs(_waveDpiX - dpi.DpiScaleX) < 0.001
            && Math.Abs(_waveDpiY - dpi.DpiScaleY) < 0.001
            && Math.Abs(_waveViewStart - quantStart) < bmpSpan / bmpWidth * 0.01
            && Math.Abs(_waveViewSpan - bmpSpan) < bmpSpan / bmpWidth * 0.01
            && Math.Abs(_waveAmpZoom - _ampZoom) < 1e-6
            && _waveMode == _spectrogramMode
            && _waveChannels == (_document?.Channels ?? 0))
        {
            return;
        }

        EnsureWaveBitmap(bmpWidth, height, dpi);
        EnsureWavePens();
        EnsureWavePixels(bmpWidth, height);

        var shifted = !_waveDirty
            && _wavePixelWidth == bmpWidth
            && _wavePixelHeight == height
            && Math.Abs(_waveViewSpan - bmpSpan) < 0.01
            && Math.Abs(_waveAmpZoom - _ampZoom) < 1e-6
            && _waveMode == _spectrogramMode
            && TryShiftWaveform(quantStart, bmpSpan, bmpWidth, height, scaleX, scaleY);
        if (!shifted)
        {
            Array.Clear(_wavePixels, 0, bmpWidth * height);
            RasterizeWaveformPixels(0, bmpWidth, bmpWidth, height, scaleX, scaleY, quantStart, bmpSpan);
        }

        _waveBitmap!.WritePixels(new Int32Rect(0, 0, bmpWidth, height), _wavePixels, bmpWidth * 4, 0);
        if (!SpectrogramVisible && !LoudnessVisible)
        {
            RebuildInvertBitmap(bmpWidth, height, dpi);
        }

        _waveDipSize = bounds.Size;
        _waveDpiX = dpi.DpiScaleX;
        _waveDpiY = dpi.DpiScaleY;
        _waveViewStart = quantStart;
        _waveViewSpan = bmpSpan;
        _waveAmpZoom = _ampZoom;
        _waveMode = _spectrogramMode;
        _wavePixelWidth = bmpWidth;
        _wavePixelHeight = height;
        _waveChannels = _document?.Channels ?? 0;
        _waveDirty = false;
    }

    private bool TryShiftWaveform(
        double quantStart,
        double bmpSpan,
        int width,
        int height,
        double scaleX,
        double scaleY)
    {
        if (!WaveScroll.TryPixelShift(_waveViewStart, quantStart, bmpSpan, width, out var shiftPx))
        {
            return false;
        }

        var rangeFrames = (long)Math.Ceiling(quantStart + bmpSpan) - (long)Math.Floor(quantStart);
        if (IsPolylineZoom(rangeFrames, width))
        {
            return false;
        }

        WaveScroll.ShiftPacked(_wavePixels, width, height, shiftPx);
        if (shiftPx > 0)
        {
            RasterizeWaveformPixels(width - shiftPx, width, width, height, scaleX, scaleY, quantStart, bmpSpan);
        }
        else
        {
            RasterizeWaveformPixels(0, -shiftPx, width, height, scaleX, scaleY, quantStart, bmpSpan);
        }

        return true;
    }

    private void DrawWaveformImage(DrawingContext dc, Rect wave)
    {
        if (_waveBitmap is null || _wavePixelWidth <= 1)
        {
            return;
        }

        var dest = WaveBitmapDest(wave);
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        var context = group.Open();
        context.DrawImage(_waveBitmap, dest);
        context.Close();
        dc.PushClip(new RectangleGeometry(wave));
        dc.DrawDrawing(group);
        dc.Pop();
    }

    private Rect WaveBitmapDest(Rect wave)
    {
        var span = ViewSpanFrames;
        var offsetDip = span <= 0 || _wavePixelWidth <= 1
            ? 0d
            : (_viewStart - _waveViewStart) / span * wave.Width;
        var visibleWidth = Math.Max(1, _wavePixelWidth - 1);
        return new Rect(
            wave.X - offsetDip,
            wave.Y,
            wave.Width * _wavePixelWidth / visibleWidth,
            wave.Height);
    }

    private void RasterizeWaveformPixels(
        int x0,
        int x1,
        int width,
        int height,
        double scaleX,
        double scaleY,
        double start,
        double span)
    {
        unsafe
        {
            fixed (int* buffer = _wavePixels)
            {
                RasterizeWaveform(buffer, width, width, height, scaleX, scaleY, _document!, x0, x1, start, span);
            }
        }
    }

    private void EnsureWavePixels(int width, int height)
    {
        var needed = width * height;
        if (_wavePixels.Length < needed)
        {
            _wavePixels = new int[needed];
        }
    }

    private void EnsureWaveBitmap(int width, int height, DpiScale dpi)
    {
        if (_waveBitmap is not null
            && _waveBitmap.PixelWidth == width
            && _waveBitmap.PixelHeight == height)
        {
            return;
        }

        _waveBitmap = new WriteableBitmap(
            width,
            height,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Bgra32,
            null);
    }

    private void RebuildInvertBitmap(int width, int height, DpiScale dpi)
    {
        if (_waveBitmap is null)
        {
            return;
        }

        if (_invertBitmap is null
            || _invertBitmap.PixelWidth != width
            || _invertBitmap.PixelHeight != height)
        {
            _invertBitmap = new WriteableBitmap(
                width,
                height,
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Bgra32,
                null);
        }

        var needed = width * height;
        if (_invertPixels.Length < needed)
        {
            _invertPixels = new int[needed];
        }

        _waveBitmap.CopyPixels(new Int32Rect(0, 0, width, height), _invertPixels, width * 4, 0);
        WaveformInvertPaint.RebuildInPlace(
            _invertPixels,
            width,
            height,
            _document,
            _viewStart,
            ViewSpanFrames,
            LaneWaveColors(),
            LaneGapPx(dpi.DpiScaleY));
        _invertBitmap.WritePixels(new Int32Rect(0, 0, width, height), _invertPixels, width * 4, 0);
        _invertDocument = _document;
        _invertChannels = _document?.Channels ?? 0;
    }

    private void DrawInvertedSelection(
        DrawingContext dc,
        Rect bounds,
        double start,
        double span,
        WaveSelection selection)
    {
        if (SpectrogramVisible
            || LoudnessVisible
            || _invertBitmap is null
            || !ReferenceEquals(_invertDocument, _document)
            || _invertChannels != (_document?.Channels ?? 0))
        {
            if (SpectrogramVisible || LoudnessVisible)
            {
                var specWave = WaveformBounds(bounds);
                var sx0 = FrameToViewX(selection.StartFrame, start, span, bounds);
                var sx1 = FrameToViewX(selection.EndFrame, start, span, bounds);
                sx0 = Math.Clamp(sx0, specWave.X, specWave.Right);
                sx1 = Math.Clamp(sx1, specWave.X, specWave.Right);
                if (sx1 > sx0)
                {
                    dc.DrawRectangle(
                        WpfControlHelpers.FrozenBrush(
                            UiThemeService.Current == UiTheme.Light
                                ? Theme.Get("LoopRangeFillBrush")
                                : Color.FromArgb(56, 255, 255, 255)),
                        null,
                        new Rect(sx0, specWave.Y, sx1 - sx0, specWave.Height));
                }
            }

            return;
        }

        var wave = WaveformBounds(bounds);
        var x0 = FrameToViewX(selection.StartFrame, start, span, bounds);
        var x1 = FrameToViewX(selection.EndFrame, start, span, bounds);
        if (x1 < wave.X || x0 > wave.Right)
        {
            return;
        }

        x0 = Math.Clamp(x0, wave.X, wave.Right);
        x1 = Math.Clamp(x1, wave.X, wave.Right);
        var width = Math.Max(1, x1 - x0);
        var clip = SelectionLaneClip(wave, x0, width);
        if (clip is null)
        {
            return;
        }

        dc.PushClip(clip);
        dc.DrawImage(_invertBitmap, WaveBitmapDest(wave));
        dc.Pop();
    }

    private Geometry? SelectionLaneClip(Rect wave, double x0, double width)
    {
        var lanes = SelectedLaneRects(wave);
        if (lanes.Count == 0)
        {
            return null;
        }

        if (lanes.Count == 1)
        {
            return new RectangleGeometry(new Rect(x0, lanes[0].Y, width, lanes[0].Height));
        }

        var group = new GeometryGroup();
        foreach (var lane in lanes)
        {
            group.Children.Add(new RectangleGeometry(new Rect(x0, lane.Y, width, lane.Height)));
        }

        return group;
    }

    private List<Rect> SelectedLaneRects(Rect wave)
    {
        if (_document is null || SpectrogramVisible || LoudnessVisible || _soloMask == 0)
        {
            return [wave];
        }

        var channels = Math.Max(1, _document.Channels);
        if (channels <= 1)
        {
            return [wave];
        }

        const double laneGap = 4;
        var laneHeight = (wave.Height - laneGap * (channels - 1)) / channels;
        var lanes = new List<Rect>();
        for (var ch = 0; ch < channels; ch++)
        {
            if (!ChannelSolo.Contains(_soloMask, ch))
            {
                continue;
            }

            var top = wave.Y + ch * (laneHeight + laneGap);
            lanes.Add(new Rect(wave.X, top, wave.Width, Math.Max(1, laneHeight)));
        }

        return lanes.Count == 0 ? [wave] : lanes;
    }

    private unsafe void RasterizeWaveform(
        int* buffer,
        int stride,
        int width,
        int height,
        double scaleX,
        double scaleY,
        AudioDocument document,
        int x0,
        int x1,
        double start,
        double span)
    {
        var sourceChannels = Math.Max(1, document.Channels);
        var overlay = _spectrogramMode == SpectrogramViewMode.Overlay;
        var loudness = LoudnessVisible;
        var monoWave = overlay || loudness;
        var drawChannels = monoWave ? 1 : sourceChannels;
        var laneGap = !monoWave && drawChannels > 1 ? 4d * scaleY : 0d;
        ResolveWaveLane(overlay, height, drawChannels, laneGap, _ampZoom, out var laneHeight, out var laneOrigin, out var ampHeight);
        x0 = Math.Clamp(x0, 0, width);
        x1 = Math.Clamp(x1, x0, width);
        var startFrame = Math.Clamp((long)Math.Floor(start), 0, document.FrameCount);
        var endFrame = Math.Clamp((long)Math.Ceiling(start + span), startFrame, document.FrameCount);
        var rangeFrames = endFrame - startFrame;
        if (rangeFrames <= 0 || laneHeight < 1 || x1 <= x0)
        {
            return;
        }

        var usePolyline = IsPolylineZoom(rangeFrames, width);
        var useRawColumns = !usePolyline && rangeFrames <= RawColumnBudget(width);
        if (loudness)
        {
            RasterLoudnessDbWaveform(
                buffer,
                stride,
                width,
                height,
                document,
                x0,
                x1,
                start,
                span,
                startFrame,
                rangeFrames,
                sourceChannels,
                usePolyline,
                useRawColumns);
            return;
        }

        if (!usePolyline)
        {
            var f0 = startFrame + (long)x0 * rangeFrames / width;
            var f1 = startFrame + (long)x1 * rangeFrames / width;
            if (f1 <= f0)
            {
                f1 = f0 + 1;
            }

            var colCount = x1 - x0;
            EnsureColumnBuffers(colCount * sourceChannels);
            var count = useRawColumns
                ? FillRawColumnPeaks(document, f0, f1, colCount, sourceChannels)
                : document.Peaks.ReadRangePacked(f0, f1, colCount, _columnMins, _columnMaxs);
            if (count <= 0)
            {
                return;
            }

            if (!useRawColumns && _previewGainAtFrame is not null)
            {
                ApplyPreviewGainToColumns(f0, f1, count, sourceChannels);
            }

            var packedChannels = sourceChannels;
            if (monoWave)
            {
                ChannelMix.FoldPackedPeaksToMid(_columnMins, _columnMaxs, count, sourceChannels);
                packedChannels = 1;
            }

            for (var ch = 0; ch < drawChannels; ch++)
            {
                var top = laneOrigin + ch * (laneHeight + laneGap);
                var mid = top + laneHeight * 0.5;
                if (!TryLaneClip(top, laneHeight, height, out var clipTop, out var clipBottom))
                {
                    continue;
                }

                if (!monoWave)
                {
                    DrawLaneGuides(buffer, stride, width, height, top, mid, drawLaneTop: true, x0, x1);
                }

                RasterPeakEnvelope(
                    buffer,
                    stride,
                    width,
                    clipTop,
                    clipBottom,
                    ch,
                    packedChannels,
                    count,
                    x0,
                    top,
                    laneHeight,
                    ampHeight,
                    mid,
                    connectNeighbors: rangeFrames <= (long)width * 8);
            }

            return;
        }

        for (var ch = 0; ch < drawChannels; ch++)
        {
            var top = laneOrigin + ch * (laneHeight + laneGap);
            var mid = top + laneHeight * 0.5;
            if (!TryLaneClip(top, laneHeight, height, out var clipTop, out var clipBottom))
            {
                continue;
            }

            if (!monoWave)
            {
                DrawLaneGuides(buffer, stride, width, height, top, mid, drawLaneTop: true, 0, width);
            }

            RasterSamplePolyline(
                buffer,
                stride,
                width,
                clipTop,
                clipBottom,
                document,
                monoWave ? -1 : ch,
                start,
                span,
                top,
                ampHeight,
                mid,
                scaleX);
        }
    }

    private unsafe void RasterLoudnessDbWaveform(
        int* buffer,
        int stride,
        int width,
        int height,
        AudioDocument document,
        int x0,
        int x1,
        double start,
        double span,
        long startFrame,
        long rangeFrames,
        int sourceChannels,
        bool usePolyline,
        bool useRawColumns)
    {
        if (usePolyline)
        {
            RasterLoudnessDbPolyline(buffer, stride, width, height, document, start, span);
            return;
        }

        var f0 = startFrame + (long)x0 * rangeFrames / width;
        var f1 = startFrame + (long)x1 * rangeFrames / width;
        if (f1 <= f0)
        {
            f1 = f0 + 1;
        }

        var colCount = x1 - x0;
        EnsureColumnBuffers(colCount * sourceChannels);
        var count = useRawColumns
            ? FillRawColumnPeaks(document, f0, f1, colCount, sourceChannels)
            : document.Peaks.ReadRangePacked(f0, f1, colCount, _columnMins, _columnMaxs);
        if (count <= 0)
        {
            return;
        }

        if (!useRawColumns && _previewGainAtFrame is not null)
        {
            ApplyPreviewGainToColumns(f0, f1, count, sourceChannels);
        }

        ChannelMix.FoldPackedPeaksToMid(_columnMins, _columnMaxs, count, sourceChannels);
        EnsureColumnEdges(width);
        var px1 = Math.Min(width, x0 + count);
        for (var i = 0; i < count; i++)
        {
            var px = x0 + i;
            if ((uint)px >= (uint)width)
            {
                break;
            }

            _columnYHi[px] = LoudnessPeakToY(AbsPeak(_columnMins[i], _columnMaxs[i]), height);
        }

        DrawLoudnessConnected(buffer, stride, width, height, x0, px1);
    }

    private unsafe void RasterLoudnessDbPolyline(
        int* buffer,
        int stride,
        int width,
        int height,
        AudioDocument document,
        double start,
        double span)
    {
        var first = (long)Math.Floor(start);
        var last = Math.Min(document.FrameCount - 1, (long)Math.Ceiling(start + span));
        if (last < first)
        {
            return;
        }

        var count = (int)(last - first + 1);
        var samples = document.Interleaved;
        var channels = document.Channels;
        float AbsAt(int index)
        {
            var frame = first + index;
            SpectrogramEngine.MixFrame(samples, channels, frame, document.FrameCount, out var mixed);
            return Math.Abs(mixed * PreviewGain(frame));
        }

        var color = _loudnessWaveBgra;
        if (count == 1)
        {
            var x = (int)Math.Round(FrameToX(first, start, span, width));
            var y = LoudnessPeakToY(AbsAt(0), height);
            DrawLine(buffer, stride, width, 0, height, x, y, x + 1, y, color);
            return;
        }

        var prevX = 0;
        var prevY = 0;
        var havePrev = false;
        for (var i = 0; i < count; i++)
        {
            var x = (int)Math.Round(FrameToX(first + i, start, span, width));
            var y = LoudnessPeakToY(AbsAt(i), height);
            if (havePrev)
            {
                DrawLine(buffer, stride, width, 0, height, prevX, prevY, x, y, color);
            }

            prevX = x;
            prevY = y;
            havePrev = true;
        }
    }

    private unsafe void DrawLoudnessConnected(
        int* buffer,
        int stride,
        int width,
        int height,
        int x0,
        int px1)
    {
        var color = _loudnessWaveBgra;
        if (px1 - x0 < 2)
        {
            if (px1 > x0 && (uint)x0 < (uint)width)
            {
                var y = _columnYHi[x0];
                DrawLine(buffer, stride, width, 0, height, x0, y, Math.Min(width - 1, x0 + 1), y, color);
            }

            return;
        }

        for (var px = x0; px + 1 < px1; px++)
        {
            DrawLine(
                buffer,
                stride,
                width,
                0,
                height,
                px,
                _columnYHi[px],
                px + 1,
                _columnYHi[px + 1],
                color);
        }
    }

    private int LoudnessPeakToY(float peak, double height) =>
        Math.Clamp((int)Math.Round(LoudnessDbToY(AmplitudeToDb(peak), height)), 0, (int)height - 1);

    private double LoudnessDbToY(double db, double height) =>
        LoudnessOverlayRenderer.LufsToY(db, new Rect(0, 0, 1, height), _loudnessTargetLufs);

    private static float AbsPeak(float min, float max) =>
        Math.Max(Math.Abs(min), Math.Abs(max));

    private static double AmplitudeToDb(float amplitude)
    {
        var a = Math.Abs(amplitude);
        return a < 1e-7f ? -200d : 20d * Math.Log10(a);
    }

    private static void ResolveWaveLane(
        bool overlay,
        double height,
        int drawChannels,
        double laneGap,
        double ampZoom,
        out double laneHeight,
        out double laneOrigin,
        out double ampHeight)
    {
        if (!overlay)
        {
            laneHeight = (height - laneGap * (drawChannels - 1)) / drawChannels;
            laneOrigin = 0d;
            ampHeight = laneHeight;
            return;
        }

        ampHeight = height * OverlayWaveHeightFraction;
        laneHeight = Math.Min(height, ampHeight * ampZoom);
        laneOrigin = (height - laneHeight) * 0.5;
    }

    private static bool TryLaneClip(
        double top,
        double laneHeight,
        int bitmapHeight,
        out int clipTop,
        out int clipBottom)
    {
        clipTop = (int)Math.Ceiling(top);
        clipBottom = (int)Math.Floor(top + laneHeight);
        if (clipTop < 0)
        {
            clipTop = 0;
        }

        if (clipBottom > bitmapHeight)
        {
            clipBottom = bitmapHeight;
        }

        return clipBottom > clipTop;
    }

    private static long RawColumnBudget(int width) =>
        Math.Min(RawColumnMaxFrames, (long)Math.Max(1, width) * RawColumnMaxSamplesPerPixel);

    private unsafe void DrawLaneGuides(
        int* buffer,
        int stride,
        int width,
        int height,
        double top,
        double mid,
        bool drawLaneTop,
        int x0,
        int x1)
    {
        var yMid = (int)Math.Round(mid);
        if ((uint)yMid < (uint)height)
        {
            FillHLine(buffer, stride, width, x0, x1, yMid, _zeroBgra);
        }

        if (!drawLaneTop)
        {
            return;
        }

        var yTop = (int)Math.Round(top);
        if (yTop > 0 && (uint)yTop < (uint)height)
        {
            FillHLine(buffer, stride, width, x0, x1, yTop, _zeroBgra);
        }
    }

    private unsafe void RasterPeakEnvelope(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int channel,
        int channels,
        int count,
        int px0,
        double top,
        double laneHeight,
        double ampHeight,
        double mid,
        bool connectNeighbors)
    {
        EnsureColumnEdges(width);
        var amp = ampHeight * 0.5 * _ampZoom;
        var bottom = top + laneHeight;
        var color = WavePaintBgraFor(channel);
        var px1 = Math.Min(width, px0 + count);
        for (var i = 0; i < count; i++)
        {
            var px = px0 + i;
            if ((uint)px >= (uint)width)
            {
                break;
            }

            var index = i * channels + channel;
            var y1 = Math.Clamp(mid - _columnMaxs[index] * amp, top, bottom);
            var y2 = Math.Clamp(mid - _columnMins[index] * amp, top, bottom);
            if (y2 < y1)
            {
                (y1, y2) = (y2, y1);
            }

            if (y2 - y1 < 1)
            {
                y2 = y1 + 1;
            }

            _columnYHi[px] = (int)Math.Floor(y1);
            _columnYLo[px] = (int)Math.Ceiling(y2);
        }

        for (var px = px0; px < px1; px++)
        {
            var hi = _columnYHi[px];
            var lo = _columnYLo[px];
            if (connectNeighbors && px + 1 < px1)
            {
                hi = Math.Min(hi, _columnYHi[px + 1]);
                lo = Math.Max(lo, _columnYLo[px + 1]);
            }

            FillVLine(buffer, stride, width, clipTop, clipBottom, px, hi, lo, color);
        }
    }

    private void EnsureColumnEdges(int width)
    {
        if (_columnYHi.Length >= width)
        {
            return;
        }

        _columnYHi = new int[width];
        _columnYLo = new int[width];
    }

    private int FillRawColumnPeaks(
        AudioDocument document,
        long startFrame,
        long endFrame,
        int width,
        int channels)
    {
        var rangeFrames = endFrame - startFrame;
        var buckets = (int)Math.Min(width, rangeFrames);
        if (buckets <= 0)
        {
            return 0;
        }

        var samples = document.Interleaved;
        var frameCount = document.FrameCount;
        for (var i = 0; i < buckets * channels; i++)
        {
            _columnMins[i] = float.MaxValue;
            _columnMaxs[i] = float.MinValue;
        }

        for (var i = 0; i < buckets; i++)
        {
            var f0 = startFrame + i * rangeFrames / buckets;
            var f1 = startFrame + (i + 1) * rangeFrames / buckets;
            if (f1 <= f0)
            {
                f1 = f0 + 1;
            }

            f0 = Math.Clamp(f0, 0, frameCount);
            f1 = Math.Clamp(f1, f0, frameCount);
            var dest = i * channels;
            for (var frame = f0; frame < f1; frame++)
            {
                var src = (int)frame * channels;
                var gain = PreviewGain(frame);
                for (var ch = 0; ch < channels; ch++)
                {
                    var sample = samples[src + ch] * (IsLaneMuted(ch) ? 1f : gain);
                    if (sample < _columnMins[dest + ch])
                    {
                        _columnMins[dest + ch] = sample;
                    }

                    if (sample > _columnMaxs[dest + ch])
                    {
                        _columnMaxs[dest + ch] = sample;
                    }
                }
            }
        }

        for (var i = 0; i < buckets * channels; i++)
        {
            if (_columnMins[i] > _columnMaxs[i])
            {
                _columnMins[i] = 0;
                _columnMaxs[i] = 0;
            }
        }

        return buckets;
    }

    private float PreviewGain(long frame) =>
        _previewGainAtFrame is { } gain ? gain(frame) : 1f;

    private void ApplyPreviewGainToColumns(long startFrame, long endFrame, int count, int channels)
    {
        if (_previewGainAtFrame is null || count <= 0)
        {
            return;
        }

        var rangeFrames = endFrame - startFrame;
        if (rangeFrames <= 0)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var f0 = startFrame + i * rangeFrames / count;
            var f1 = startFrame + (i + 1) * rangeFrames / count;
            var mid = f0 + Math.Max(0L, (f1 - f0) / 2);
            var gain = PreviewGain(mid);
            if (Math.Abs(gain - 1f) < 1e-6f)
            {
                continue;
            }

            var dest = i * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                if (IsLaneMuted(ch))
                {
                    continue;
                }

                _columnMins[dest + ch] *= gain;
                _columnMaxs[dest + ch] *= gain;
            }
        }
    }

    private unsafe void RasterSamplePolyline(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        AudioDocument document,
        int channel,
        double start,
        double span,
        double top,
        double ampHeight,
        double mid,
        double scaleX)
    {
        var channels = document.Channels;
        var first = (long)Math.Floor(start);
        var last = Math.Min(document.FrameCount - 1, (long)Math.Ceiling(start + span));
        if (last < first)
        {
            return;
        }

        var count = (int)(last - first + 1);
        var samples = document.Interleaved;
        var amp = ampHeight * 0.5 * _ampZoom;
        int SampleY(float sample)
        {
            var y = (int)Math.Round(mid - sample * amp);
            return Math.Clamp(y, clipTop, clipBottom - 1);
        }

        float SampleAt(int index)
        {
            var frame = first + index;
            var gain = channel >= 0 && IsLaneMuted(channel) ? 1f : PreviewGain(frame);
            if (channel < 0)
            {
                SpectrogramEngine.MixFrame(samples, channels, frame, document.FrameCount, out var mixed);
                return mixed * gain;
            }

            return samples[(int)frame * channels + channel] * gain;
        }

        var collectDots = ShouldDrawSamplePoints(count, width / scaleX);
        var dotRadius = Math.Max(1, (int)Math.Round(SamplePointRadius * scaleX));
        var color = WavePaintBgraFor(channel);

        if (count == 1)
        {
            var x = (int)Math.Round(FrameToX(first, start, span, width));
            var y = SampleY(SampleAt(0));
            FillVLine(buffer, stride, width, clipTop, clipBottom, x, y - 3, y + 3, color);
            if (collectDots)
            {
                FillDot(buffer, stride, width, clipTop, clipBottom, x, y, dotRadius, color);
            }

            return;
        }

        var prevX = 0;
        var prevY = 0;
        var havePrev = false;
        for (var i = 0; i < count; i++)
        {
            var x = (int)Math.Round(FrameToX(first + i, start, span, width));
            var y = SampleY(SampleAt(i));
            if (havePrev)
            {
                DrawThickLine(buffer, stride, width, clipTop, clipBottom, prevX, prevY, x, y, color);
            }

            prevX = x;
            prevY = y;
            havePrev = true;
            if (collectDots)
            {
                FillDot(buffer, stride, width, clipTop, clipBottom, x, y, dotRadius, color);
            }
        }
    }

    private static unsafe void FillVLine(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int x,
        int y1,
        int y2,
        int color)
    {
        if ((uint)x >= (uint)width)
        {
            return;
        }

        if (y1 > y2)
        {
            (y1, y2) = (y2, y1);
        }

        if (y1 < clipTop)
        {
            y1 = clipTop;
        }

        if (y2 >= clipBottom)
        {
            y2 = clipBottom - 1;
        }

        if (y1 > y2)
        {
            return;
        }

        var p = buffer + y1 * stride + x;
        for (var y = y1; y <= y2; y++)
        {
            *p = color;
            p += stride;
        }
    }

    private static unsafe void FillDot(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int cx,
        int cy,
        int radius,
        int color)
    {
        var r2 = radius * radius;
        var y0 = Math.Max(clipTop, cy - radius);
        var y1 = Math.Min(clipBottom - 1, cy + radius);
        var x0 = Math.Max(0, cx - radius);
        var x1 = Math.Min(width - 1, cx + radius);
        for (var y = y0; y <= y1; y++)
        {
            var dy = y - cy;
            var row = buffer + y * stride;
            for (var x = x0; x <= x1; x++)
            {
                var dx = x - cx;
                if (dx * dx + dy * dy <= r2)
                {
                    row[x] = color;
                }
            }
        }
    }

    private static unsafe void FillHLine(int* buffer, int stride, int width, int x0, int x1, int y, int color)
    {
        var p = buffer + y * stride;
        var left = Math.Clamp(x0, 0, width);
        var right = Math.Clamp(x1, left, width);
        for (var x = left; x < right; x++)
        {
            p[x] = color;
        }
    }

    private static unsafe void DrawThickLine(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int x0,
        int y0,
        int x1,
        int y1,
        int color)
    {
        DrawLine(buffer, stride, width, clipTop, clipBottom, x0, y0, x1, y1, color);
        DrawLine(buffer, stride, width, clipTop, clipBottom, x0, y0 + 1, x1, y1 + 1, color);
    }

    private static unsafe void DrawLine(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int x0,
        int y0,
        int x1,
        int y1,
        int color)
    {
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            if ((uint)x0 < (uint)width && y0 >= clipTop && y0 < clipBottom)
            {
                buffer[y0 * stride + x0] = color;
            }

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }


    private void DrawChannelLabels(
        DrawingContext dc,
        Rect bounds,
        Rect wave,
        int channels,
        double laneGap,
        double laneHeight)
    {
        if (laneHeight < 10)
        {
            return;
        }

        var well = DbScaleBounds(bounds);
        if (well.Width <= 8)
        {
            return;
        }

        var fore = WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush"));
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var reserve = 7 + GetDbLabel("-12", pixelsPerDip, fore).Width;
        var maxName = Math.Max(8, well.Width - reserve - 2);
        var layout = ChannelLayout.ForFile(channels, _speakerLayout, _fileChannelMap);
        var tint = ChannelColors.UsesLaneTint(channels);
        const double padX = 3;
        const double padY = 1;
        const double smallEm = 8;
        const double largeEm = 11;
        FormattedText MeasureName(string text, double em, Brush brush) => new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            em,
            brush,
            pixelsPerDip);
        var slot = Math.Max(
            MeasureName(new string('M', ChannelLabels.MaxChars), smallEm, fore).Width,
            MeasureName("WW", largeEm, fore).Width);
        var badgeW = Math.Min(maxName, slot + padX * 2);
        var badgeX = ChannelLabelBadgeX(well.X);
        for (var ch = 0; ch < channels; ch++)
        {
            var muted = IsLaneMuted(ch);
            var nameFore = tint
                ? Brushes.White
                : WpfControlHelpers.FrozenBrush(Theme.Get(muted ? "MutedForeBrush" : "PrimaryForeBrush"));
            var top = wave.Y + ch * (laneHeight + laneGap);
            var name = layout.LabelAt(ch);
            var em = name.Length <= 2 ? largeEm : smallEm;
            var text = MeasureName(name, em, nameFore);
            if (text.Width > badgeW - padX * 2 && em > smallEm)
            {
                text = MeasureName(name, smallEm, nameFore);
            }

            var badgeH = Math.Min(laneHeight, text.Height + padY * 2);
            var badgeY = top + Math.Max(0, (laneHeight - badgeH) * 0.5);
            var x = ChannelLabelTextX(badgeX, badgeW, text.Width);
            var y = badgeY + Math.Max(0, (badgeH - text.Height) * 0.5);
            dc.PushClip(new RectangleGeometry(new Rect(well.X, top, maxName + 1, laneHeight)));
            if (tint)
            {
                dc.DrawRectangle(
                    ChannelSwatch.Brush(ch, muted),
                    null,
                    new Rect(badgeX, badgeY, badgeW, badgeH));
            }

            dc.DrawText(text, new Point(x, y));
            dc.Pop();
        }
    }

    private void DrawDbScaleWell(DrawingContext dc, Rect bounds)
    {
        var well = DbScaleBounds(bounds);
        if (well.Width <= 1)
        {
            return;
        }

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush")), null, well);
    }

    private void DrawDbScaleTicks(
        DrawingContext dc,
        Rect bounds,
        Rect wave,
        int channels,
        double laneGap,
        double laneHeight,
        double? ampHeight = null)
    {
        var well = DbScaleBounds(bounds);
        if (well.Width <= 8 || laneHeight < 8)
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var scale = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        var half = laneHeight * 0.5;
        var amp = (ampHeight ?? laneHeight) * 0.5 * _ampZoom;

        for (var ch = 0; ch < channels; ch++)
        {
            var top = wave.Y + ch * (laneHeight + laneGap);
            var mid = top + half;
            var bottom = top + laneHeight;
            dc.PushClip(new RectangleGeometry(new Rect(well.X, top, well.Width, laneHeight)));
            DrawDbTick(dc, well, mid, "∞", pixelsPerDip, scale);

            var drawn = new List<double> { mid };
            var values = 1;
            foreach (var db in DbRequiredMarks)
            {
                if (TryDrawDbValue(dc, well, db, mid, amp, top, bottom, pixelsPerDip, scale, drawn, minGap: 0))
                {
                    values++;
                }
            }

            foreach (var db in DbOptionalMarks)
            {
                if (TryDrawDbValue(dc, well, db, mid, amp, top, bottom, pixelsPerDip, scale, drawn, DbOptionalMinGapPx))
                {
                    values++;
                }
            }

            if (values < 4)
            {
                foreach (var db in DbOptionalMarks)
                {
                    if (TryDrawDbValue(dc, well, db, mid, amp, top, bottom, pixelsPerDip, scale, drawn, minGap: 0)
                        && ++values >= 4)
                    {
                        break;
                    }
                }
            }

            dc.Pop();
        }
    }

    private bool TryDrawDbValue(
        DrawingContext dc,
        Rect well,
        double db,
        double mid,
        double amp,
        double top,
        double bottom,
        double pixelsPerDip,
        Brush fore,
        List<double> drawn,
        double minGap)
    {
        var dy = Math.Pow(10, db / 20d) * amp;
        if (dy < 3)
        {
            return false;
        }

        var label = db.ToString("0", CultureInfo.InvariantCulture);
        var up = TryDrawDbTickAt(dc, well, mid - dy, label, top, bottom, pixelsPerDip, fore, drawn, minGap);
        var down = TryDrawDbTickAt(dc, well, mid + dy, label, top, bottom, pixelsPerDip, fore, drawn, minGap);
        return up || down;
    }

    private bool TryDrawDbTickAt(
        DrawingContext dc,
        Rect well,
        double y,
        string label,
        double top,
        double bottom,
        double pixelsPerDip,
        Brush fore,
        List<double> drawn,
        double minGap)
    {
        if (y < top - 0.5 || y > bottom + 0.5)
        {
            return false;
        }

        y = Math.Clamp(y, top + 0.5, bottom - 0.5);
        var gap = Math.Max(minGap, 0.75);
        for (var i = 0; i < drawn.Count; i++)
        {
            if (Math.Abs(drawn[i] - y) < gap)
            {
                return false;
            }
        }

        DrawDbTick(dc, well, y, label, pixelsPerDip, fore);
        drawn.Add(y);
        return true;
    }

    private void DrawDbTick(
        DrawingContext dc,
        Rect well,
        double y,
        string label,
        double pixelsPerDip,
        Brush fore)
    {
        var text = GetDbLabel(label, pixelsPerDip, fore);
        var ty = y - text.Height * 0.5;
        var tx = well.Right - 2 - text.Width;
        dc.DrawText(text, new Point(Math.Max(well.X + 2, tx), ty));
    }

    private FormattedText GetDbLabel(string text, double pixelsPerDip, Brush fore)
    {
        if (_dbLabelCache.TryGetValue(text, out var cached))
        {
            return cached;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            9,
            fore,
            pixelsPerDip);
        _dbLabelCache[text] = formatted;
        return formatted;
    }

    private bool ShouldDrawSamplePoints(int count, double width) =>
        count > 0
        && count <= Math.Min(SamplePointMaxVisibleFrames, width);

    private static bool IsPolylineZoom(long rangeFrames, int width) =>
        rangeFrames > 0 && width > 0 && rangeFrames <= (long)width * PolylineMaxSamplesPerPixel;

    private void EnsureColumnBuffers(int width)
    {
        if (_columnMins.Length < width)
        {
            _columnMins = new float[width];
            _columnMaxs = new float[width];
        }
    }

    private void EnsureWavePens()
    {
        if (_waveBgra != 0 && _waveOverlayBgra != 0 && _loudnessWaveBgra != 0)
        {
            return;
        }

        _waveBgra = ToBgra(Theme.Get("WaveFillBrush"));
        _waveOverlayBgra = ToBgra(Theme.Get("WaveFillOverlayBrush"));
        _loudnessWaveBgra = ToBgra(Colors.White);
        _zeroBgra = ToBgra(Theme.Get("WaveZeroLineBrush"));
    }

    private int WavePaintBgra => LoudnessVisible
        ? _loudnessWaveBgra
        : _spectrogramMode == SpectrogramViewMode.Overlay
            ? _waveOverlayBgra
            : _waveBgra;

    private bool IsLaneMuted(int channel) =>
        _soloMask != 0 && !ChannelSolo.Contains(_soloMask, channel);

    private int WavePaintBgraFor(int channel)
    {
        if (channel >= 0
            && !LoudnessVisible
            && _spectrogramMode != SpectrogramViewMode.Overlay)
        {
            return ChannelWavePaint.FillBgra(channel, _document?.Channels ?? 1, IsLaneMuted(channel));
        }

        var color = WavePaintBgra;
        return channel >= 0 && IsLaneMuted(channel) ? ChannelWavePaint.Dim(color) : color;
    }

    private int[] LaneWaveColors()
    {
        var channels = Math.Max(1, _document?.Channels ?? 1);
        var colors = new int[channels];
        for (var ch = 0; ch < channels; ch++)
        {
            colors[ch] = ChannelWavePaint.FillBgra(ch, channels, IsLaneMuted(ch));
        }

        return colors;
    }

    private double LaneGapPx(double scaleY) =>
        (_document?.Channels ?? 1) > 1 ? scaleY * 4 : 0;

    private static int ToBgra(Color color) =>
        color.B | (color.G << 8) | (color.R << 16) | (color.A << 24);

    private double ContentLeft => ScaleLeft(new Rect(0, 0, ActualWidth, ActualHeight));

    private double ContentWidth => Math.Max(0, ActualWidth - ContentLeft);

    private bool IsInDbScaleLane(Point point) =>
        point.X < ContentLeft && point.Y >= ChromeTopHeight;

    private bool TryHitChannelLabel(Point point, out int channel)
    {
        channel = -1;
        if (_document is null || SpectrogramVisible || LoudnessVisible)
        {
            return false;
        }

        var channels = Math.Max(1, _document.Channels);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var wave = WaveformBounds(bounds);
        var well = DbScaleBounds(bounds);
        if (well.Width <= 8 || wave.Height <= 1)
        {
            return false;
        }

        var laneGap = channels > 1 ? 4d : 0d;
        var laneHeight = (wave.Height - laneGap * (channels - 1)) / channels;
        if (laneHeight < 10)
        {
            return false;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var fore = WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush"));
        var reserve = 7 + GetDbLabel("-12", pixelsPerDip, fore).Width;
        var maxName = Math.Max(8, well.Width - reserve - 2);
        if (point.X < well.X || point.X > well.X + maxName + 1)
        {
            return false;
        }

        for (var ch = 0; ch < channels; ch++)
        {
            var top = wave.Y + ch * (laneHeight + laneGap);
            if (point.Y >= top && point.Y < top + laneHeight)
            {
                channel = ch;
                return true;
            }
        }

        return false;
    }

    private enum SpectrogramViewMode
    {
        Off,
        Spectrogram,
        Overlay,
        Loudness,
    }

    private enum LoopBarPart
    {
        None,
        Start,
        End,
        Body,
    }

    private bool TryHitSampleLoopBar(Point pos) =>
        TryHitLoopBar(pos, out _);

    private bool TryHitLoopBar(Point pos, out LoopBarPart part)
    {
        part = LoopBarPart.None;
        if (!TryGetLoopBarRects(out var bar, out var startHandle, out var endHandle))
        {
            return false;
        }

        if (startHandle.Contains(pos))
        {
            part = LoopBarPart.Start;
            return true;
        }

        if (endHandle.Contains(pos))
        {
            part = LoopBarPart.End;
            return true;
        }

        if (bar.Contains(pos))
        {
            part = LoopBarPart.Body;
            return true;
        }

        return false;
    }

    private bool TryGetLoopBarRects(out Rect bar, out Rect startHandle, out Rect endHandle)
    {
        bar = default;
        startHandle = default;
        endHandle = default;
        if (_document is null || _document.SampleLoop.IsEmpty || ViewSpanFrames <= 0)
        {
            return false;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var lane = TimeLaneBounds(bounds);
        if (!MarkerRolePaint.TryGetVisibleRangeRect(_document.SampleLoop, lane, _viewStart, ViewSpanFrames, out bar))
        {
            return false;
        }

        var handle = Math.Min(Math.Max(LoopHandleMinPx, lane.Height * 0.375), Math.Max(2, bar.Width * 0.2));
        startHandle = new Rect(bar.X, bar.Y, handle, bar.Height);
        endHandle = new Rect(bar.Right - handle, bar.Y, handle, bar.Height);
        return true;
    }

    private bool TryHitRangeBar(WaveSelection range, Point pos)
    {
        if (_document is null || range.IsEmpty || ViewSpanFrames <= 0)
        {
            return false;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var lane = TimeLaneBounds(bounds);
        return MarkerRolePaint.TryGetVisibleRangeRect(range, lane, _viewStart, ViewSpanFrames, out var bar)
            && bar.Contains(pos);
    }

    private static double ScaleLeft(Rect bounds) =>
        Math.Min(DbScaleLaneWidth, Math.Max(0, bounds.Width));

    private static double ScaleContentWidth(Rect bounds) =>
        Math.Max(0, bounds.Width - ScaleLeft(bounds));

    private static Rect DbScaleBounds(Rect bounds) =>
        new(0, 0, ScaleLeft(bounds), bounds.Height);

    private Rect WaveformBounds(Rect bounds)
    {
        var top = Math.Min(ChromeTopHeight, Math.Max(0, bounds.Height));
        var left = ScaleLeft(bounds);
        return new Rect(left, top, ScaleContentWidth(bounds), Math.Max(0, bounds.Height - top));
    }

    private Rect MarkerLaneBounds(Rect bounds) =>
        new(ScaleLeft(bounds), 0, ScaleContentWidth(bounds), Math.Min(MarkerLaneHeight, bounds.Height));

    internal const double FlagProximityPad = 8;

    internal readonly record struct PackedTimelineFlag(double StemX, double Width, bool GrowLeft, double Offset = 0)
    {
        public double Left => (GrowLeft ? StemX - Width : StemX) + Offset;
        public double Right => Left + Width;
    }

    internal static bool FlagsOverlapX(double x0, double width0, double x1, double width1, double pad = 0) =>
        width0 > 0 && width1 > 0 && x0 - pad < x1 + width1 && x1 - pad < x0 + width0;

    internal static PackedTimelineFlag[] PackFlagRow(IReadOnlyList<PackedTimelineFlag> flags)
    {
        if (flags.Count == 0)
        {
            return [];
        }

        if (flags.Count == 1)
        {
            return [flags[0]];
        }

        var packed = new PackedTimelineFlag[flags.Count];
        var order = new int[flags.Count];
        for (var i = 0; i < flags.Count; i++)
        {
            packed[i] = flags[i];
            order[i] = i;
        }

        Array.Sort(order, (left, right) =>
        {
            var cmp = packed[left].StemX.CompareTo(packed[right].StemX);
            return cmp != 0 ? cmp : packed[left].GrowLeft.CompareTo(packed[right].GrowLeft);
        });

        for (var n = 0; n < order.Length - 1; n++)
        {
            var i = order[n];
            var j = order[n + 1];
            var a = packed[i];
            var b = packed[j];
            if (a.Right <= b.Left)
            {
                continue;
            }

            if (!a.GrowLeft && b.GrowLeft)
            {
                var gap = b.StemX - a.StemX;
                if (gap <= 0)
                {
                    continue;
                }

                var mid = a.StemX + gap * 0.5;
                packed[i] = a with { Width = Math.Min(a.Width, Math.Max(1, mid - a.StemX)) };
                packed[j] = b with { Width = Math.Min(b.Width, Math.Max(1, b.StemX - mid)) };
                continue;
            }

            if (!a.GrowLeft && !b.GrowLeft)
            {
                packed[i] = a with { Width = Math.Min(a.Width, Math.Max(1, b.StemX - a.StemX)) };
                continue;
            }

            if (a.GrowLeft && b.GrowLeft)
            {
                packed[j] = b with { Width = Math.Min(b.Width, Math.Max(1, b.StemX - a.StemX)) };
            }
        }

        return packed;
    }

    internal static PackedTimelineFlag[] ChainFlagRow(IReadOnlyList<PackedTimelineFlag> flags)
    {
        if (flags.Count == 0)
        {
            return [];
        }

        if (flags.Count == 1)
        {
            return [flags[0] with { Offset = 0 }];
        }

        var packed = new PackedTimelineFlag[flags.Count];
        var order = new int[flags.Count];
        for (var i = 0; i < flags.Count; i++)
        {
            packed[i] = flags[i] with { Offset = 0 };
            order[i] = i;
        }

        Array.Sort(order, (left, right) =>
        {
            var cmp = packed[left].StemX.CompareTo(packed[right].StemX);
            return cmp != 0 ? cmp : left.CompareTo(right);
        });

        var chainRight = double.NegativeInfinity;
        foreach (var i in order)
        {
            var flag = packed[i];
            var left = Math.Max(flag.StemX, chainRight);
            packed[i] = flag with { Offset = left - flag.StemX };
            chainRight = left + flag.Width;
        }

        return packed;
    }

    internal static Rect FullFlagRect(double x, double width, double laneHeight) =>
        new(x, 1, width, Math.Max(2, laneHeight - 2));

    internal static Rect SplitFlagRect(double x, double width, double laneHeight, bool top)
    {
        var y = 1d;
        var inner = Math.Max(4, laneHeight - 2);
        var half = Math.Floor((inner - 1) * 0.5);
        if (top)
        {
            return new Rect(x, y, width, half);
        }

        var bottomY = y + half + 1;
        return new Rect(x, bottomY, width, Math.Max(2, laneHeight - 1 - bottomY));
    }

    internal static Rect LaneFlagRect(double x, double width, double laneHeight, bool split, bool top) =>
        split ? SplitFlagRect(x, width, laneHeight, top) : FullFlagRect(x, width, laneHeight);

    private static Rect TimelineFlagRect(double x, double width, double laneHeight, bool top) =>
        SplitFlagRect(x, width, laneHeight, top);

    private Rect TimeLaneBounds(Rect bounds)
    {
        var top = Math.Min(MarkerLaneHeight, bounds.Height);
        var height = Math.Min(TimeLaneHeight, Math.Max(0, bounds.Height - top));
        return new Rect(ScaleLeft(bounds), top, ScaleContentWidth(bounds), height);
    }

    private double FrameToViewX(double frame, double start, double span, Rect bounds) =>
        ScaleLeft(bounds) + FrameToX(frame, start, span, ScaleContentWidth(bounds));

    private void DrawMarkerLane(DrawingContext dc, Rect bounds, double start, double span)
    {
        var lane = MarkerLaneBounds(bounds);
        if (lane.Height <= 1)
        {
            return;
        }

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TimelineWellBackBrush")), null, lane);
    }

    private void DrawTimeLane(DrawingContext dc, Rect bounds, double start, double span)
    {
        var lane = TimeLaneBounds(bounds);
        if (lane.Height <= 1)
        {
            return;
        }

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TimelineWellBackBrush")), null, lane);
        if (_document is not null)
        {
            DrawSampleLoopBar(dc, lane, start, span);
        }

        if (_document is null || _document.FrameCount <= 0 || span <= 0)
        {
            return;
        }

        var seconds = span / _document.SampleRate;
        var step = NiceTimeStep(seconds);
        var startSec = start / _document.SampleRate;
        var first = Math.Floor(startSec / step) * step;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (Math.Abs(pixelsPerDip - _timeLabelPixelsPerDip) > 0.01)
        {
            _timeLabelCache.Clear();
            _dbLabelCache.Clear();
            _markerLabelCache.Clear();
            _markerCommentLabelCache.Clear();
            _timeLabelPixelsPerDip = pixelsPerDip;
        }

        var loop = _document.SampleLoop;
        var hasLoop = !loop.IsEmpty;
        var tick = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("DbScaleForeBrush")), 1);
        tick.Freeze();
        const double minGap = 72;
        var loopX0 = hasLoop ? FrameToViewX(loop.StartFrame, start, span, bounds) : 0;
        var loopX1 = hasLoop ? FrameToViewX(loop.EndFrame, start, span, bounds) : 0;

        // ラベルが混み合う場合の間引きは「前のラベルとの距離」ではなく
        // 絶対時刻に固定した N 目盛りごとに行う。相対判定だとスクロールで
        // 先頭目盛りが変わるたびに描かれる組がずれ、ラベルが跳んで見える。
        var spacingPx = seconds <= 0 ? 0 : step / seconds * ScaleContentWidth(bounds);
        if (spacingPx > 0)
        {
            var sampleLabel = GetTimeLabel(
                UiStrings.FormatDuration(Math.Max(0, startSec + seconds)),
                pixelsPerDip,
                accent: TimeLabelAccent.None);
            var required = sampleLabel.Width + 3 + minGap;
            if (spacingPx < required)
            {
                step *= NiceMultiplier((int)Math.Ceiling(required / spacingPx));
                first = Math.Floor(startSec / step) * step;
            }
        }

        dc.PushClip(new RectangleGeometry(lane));
        try
        {
            for (var t = first; t <= startSec + seconds + step; t += step)
            {
                if (t < -1e-9)
                {
                    continue;
                }

                var x = FrameToViewX(t * _document.SampleRate, start, span, bounds);
                var label = UiStrings.FormatDuration(Math.Max(0, t));
                var muted = GetTimeLabel(label, pixelsPerDip, accent: TimeLabelAccent.None);
                var origin = new Point(x + 3, lane.Y + Math.Max(1, (lane.Height - muted.Height) * 0.5));
                if (origin.X >= lane.Right || origin.X + muted.Width <= lane.X)
                {
                    continue;
                }

                if (x >= lane.X && x <= lane.Right)
                {
                    dc.DrawLine(tick, new Point(x, lane.Bottom - 5), new Point(x, lane.Bottom - 1));
                }

                if (hasLoop)
                {
                    DrawTimeLabelAcrossBands(
                        dc,
                        muted,
                        GetTimeLabel(label, pixelsPerDip, accent: TimeLabelAccent.Loop),
                        GetTimeLabel(label, pixelsPerDip, accent: TimeLabelAccent.Region),
                        origin,
                        hasLoop ? (loopX0, loopX1) : null,
                        []);
                }
                else
                {
                    dc.DrawText(muted, origin);
                }
            }
        }
        finally
        {
            dc.Pop();
        }
    }

    /// <summary>k 以上で最小の「きれいな」倍率（1,2,5,10,20,50,…）を返す。</summary>
    internal static int NiceMultiplier(int k)
    {
        if (k <= 1)
        {
            return 1;
        }

        var scale = 1;
        while (true)
        {
            foreach (var nice in (ReadOnlySpan<int>)[1, 2, 5])
            {
                var candidate = nice * scale;
                if (candidate >= k)
                {
                    return candidate;
                }
            }

            scale *= 10;
        }
    }

    private static void DrawTimeLabelAcrossBands(
        DrawingContext dc,
        FormattedText muted,
        FormattedText onLoop,
        FormattedText onRegion,
        Point origin,
        (double X0, double X1)? loop,
        IReadOnlyList<(double X0, double X1)> regions)
    {
        var text = new Rect(origin.X, origin.Y, muted.Width, muted.Height);
        if (text.Width <= 0 || text.Height <= 0)
        {
            return;
        }

        dc.DrawText(muted, origin);
        foreach (var regionBand in regions)
        {
            DrawClippedText(
                dc,
                onRegion,
                origin,
                Rect.Intersect(text, BandRect(regionBand.X0, regionBand.X1, text)));
        }

        if (loop is { } loopBand)
        {
            DrawClippedText(
                dc,
                onLoop,
                origin,
                Rect.Intersect(text, BandRect(loopBand.X0, loopBand.X1, text)));
        }
    }

    private static Rect BandRect(double x0, double x1, Rect text)
    {
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        return new Rect(left, text.Y, Math.Max(0, right - left), text.Height);
    }

    private static void DrawClippedText(DrawingContext dc, FormattedText text, Point origin, Rect clip)
    {
        if (clip.IsEmpty || clip.Width <= 0 || clip.Height <= 0)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(clip));
        dc.DrawText(text, origin);
        dc.Pop();
    }

    private void RefreshFlagStacks(Rect bounds, double start, double span)
    {
        _regionFlagLayout.Clear();
        _markerFlagLayout.Clear();
        if (_document is null)
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var laneHeight = MarkerLaneBounds(bounds).Height;
        var split = SplitFlagLanes;
        var regions = new List<(WaveSelection Range, long Frame, double StemX, double Width, bool GrowLeft)>();
        foreach (var region in _document.Regions)
        {
            var id = _document.RegionNumber(region);
            TryCollectFlag(regions, bounds, start, span, region, region.StartFrame, id, growLeft: false, pixelsPerDip);
            TryCollectFlag(regions, bounds, start, span, region, region.EndFrame, id, growLeft: true, pixelsPerDip);
        }

        var markers = new List<(long Frame, double StemX, double Width)>();
        foreach (var marker in _document.Markers)
        {
            var x = FrameToViewX(marker.Frame, start, span, bounds);
            if (x < ScaleLeft(bounds) || x > bounds.Width + 24)
            {
                continue;
            }

            markers.Add((marker.Frame, x, MeasureFlagWidth(marker.Id.ToString(CultureInfo.InvariantCulture), pixelsPerDip)));
        }

        var top = new List<(bool Region, WaveSelection Range, long Frame, PackedTimelineFlag Flag)>(regions.Count + markers.Count);
        var bottom = new List<(long Frame, PackedTimelineFlag Flag)>(markers.Count);
        foreach (var region in regions)
        {
            top.Add((true, region.Range, region.Frame, new PackedTimelineFlag(region.StemX, region.Width, region.GrowLeft)));
        }

        foreach (var marker in markers)
        {
            var flag = new PackedTimelineFlag(marker.StemX, marker.Width, GrowLeft: false);
            if (split)
            {
                bottom.Add((marker.Frame, flag));
            }
            else
            {
                top.Add((false, default, marker.Frame, flag));
            }
        }

        ApplyPackedRow(top, laneHeight, split, topRow: true, chain: top.TrueForAll(item => !item.Region));
        if (bottom.Count > 0)
        {
            var packed = ChainFlagRow(bottom.ConvertAll(item => item.Flag));
            for (var i = 0; i < bottom.Count; i++)
            {
                _markerFlagLayout[bottom[i].Frame] = LaneFlagRect(packed[i].Left, packed[i].Width, laneHeight, split: true, top: false);
            }
        }
    }

    private void ApplyPackedRow(
        List<(bool Region, WaveSelection Range, long Frame, PackedTimelineFlag Flag)> row,
        double laneHeight,
        bool split,
        bool topRow,
        bool chain)
    {
        if (row.Count == 0)
        {
            return;
        }

        var packed = chain
            ? ChainFlagRow(row.ConvertAll(item => item.Flag))
            : PackFlagRow(row.ConvertAll(item => item.Flag));
        for (var i = 0; i < row.Count; i++)
        {
            var item = row[i];
            var box = LaneFlagRect(packed[i].Left, packed[i].Width, laneHeight, split, topRow);
            if (item.Region)
            {
                _regionFlagLayout[(item.Range, item.Frame)] = box;
            }
            else
            {
                _markerFlagLayout[item.Frame] = box;
            }
        }
    }

    private void TryCollectFlag(
        List<(WaveSelection Range, long Frame, double StemX, double Width, bool GrowLeft)> dest,
        Rect bounds,
        double start,
        double span,
        WaveSelection region,
        long frame,
        int id,
        bool growLeft,
        double pixelsPerDip)
    {
        var x = FrameToViewX(frame, start, span, bounds);
        if (x < ScaleLeft(bounds) || x > bounds.Width + 24)
        {
            return;
        }

        dest.Add((region, frame, x, MeasureFlagWidth(id.ToString(CultureInfo.InvariantCulture), pixelsPerDip), growLeft));
    }

    private double MeasureFlagWidth(string id, double pixelsPerDip)
    {
        const double padX = 3;
        const double maxFlagWidth = 48;
        return Math.Min(maxFlagWidth, GetMarkerLabel(id, pixelsPerDip).Width + padX * 2);
    }

    private void DrawRegionFlags(DrawingContext dc, Rect bounds, double start, double span)
    {
        _regionFlags.Clear();
        if (_document is null || _document.Regions.Count == 0)
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var color = Theme.Get("RegionTimelineBrush");
        var line = WpfControlHelpers.FrozenHairline(color, pixelsPerDip);
        var selectedLine = WpfControlHelpers.FrozenHairline(Theme.Get("MarkerSelectedBorderBrush"), pixelsPerDip);
        var fill = WpfControlHelpers.FrozenBrush(color);
        var selectedPen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("MarkerSelectedBorderBrush")), 1);
        selectedPen.Freeze();
        var lane = MarkerLaneBounds(bounds);
        for (var i = 0; i < _document.Regions.Count; i++)
        {
            var region = _document.Regions[i];
            var id = _document.RegionNumber(region);
            DrawRegionFlag(dc, bounds, lane, start, span, region, region.StartFrame, id, _selectedRegionEdges.Contains(new RegionEdge(region, true)), line, selectedLine, fill, selectedPen, pixelsPerDip);
            DrawRegionFlag(dc, bounds, lane, start, span, region, region.EndFrame, id, _selectedRegionEdges.Contains(new RegionEdge(region, false)), line, selectedLine, fill, selectedPen, pixelsPerDip);
        }
    }

    private void DrawRegionFlag(
        DrawingContext dc,
        Rect bounds,
        Rect lane,
        double start,
        double span,
        WaveSelection region,
        long frame,
        int id,
        bool selected,
        Pen line,
        Pen selectedLine,
        Brush fill,
        Pen selectedPen,
        double pixelsPerDip)
    {
        var x = FrameToViewX(frame, start, span, bounds);
        if (x < ScaleLeft(bounds) || x > bounds.Width + 24)
        {
            return;
        }

        var xs = WpfControlHelpers.SnapDeviceCenter(x, pixelsPerDip);
        dc.DrawLine(selected ? selectedLine : line, new Point(xs, 0), new Point(xs, bounds.Height));
        if (lane.Height <= 2)
        {
            return;
        }

        var idText = GetMarkerLabel(id.ToString(CultureInfo.InvariantCulture), pixelsPerDip);
        const double padX = 3;
        if (!_regionFlagLayout.TryGetValue((region, frame), out var box))
        {
            var growLeft = frame != region.StartFrame;
            var boxW = MeasureFlagWidth(id.ToString(CultureInfo.InvariantCulture), pixelsPerDip);
            box = LaneFlagRect(growLeft ? x - boxW : x, boxW, lane.Height, SplitFlagLanes, top: true);
        }

        _regionFlags.Add((region, frame, box));
        dc.DrawRectangle(fill, selected ? selectedPen : null, box);
        dc.PushClip(new RectangleGeometry(box));
        dc.DrawText(
            idText,
            new Point(box.X + padX, box.Y + Math.Max(0, (box.Height - idText.Height) * 0.5)));
        dc.Pop();

        if (frame != region.StartFrame || _document is null || _commentEditRegion == region)
        {
            return;
        }

        var name = _document.RegionName(region);
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var nameText = GetMarkerCommentLabel(TruncateMarkerComment(name), pixelsPerDip, selected);
        const double nameGap = 2;
        var nameX = box.Right + nameGap;
        var nameY = box.Y + Math.Max(0, (box.Height - nameText.Height) * 0.5);
        if (nameX + nameText.Width <= bounds.Width
            && !TextOverlapsOtherFlag(nameX, nameText.Width, nameY, nameText.Height, box))
        {
            dc.DrawText(nameText, new Point(nameX, nameY));
        }
    }

    private bool TryHitRegionFlag(Point point, out WaveSelection region) =>
        TryHitRegionFlag(point, out region, out _);

    private bool TryHitRegionFlag(Point point, out WaveSelection region, out long frame)
    {
        for (var i = _regionFlags.Count - 1; i >= 0; i--)
        {
            var hit = _regionFlags[i];
            if (hit.Flag.Contains(point))
            {
                region = hit.Range;
                frame = hit.Frame;
                return true;
            }
        }

        region = WaveSelection.Empty;
        frame = 0;
        return false;
    }

    private void DrawMarkers(DrawingContext dc, Rect bounds, double start, double span)
    {
        if (_document is null || _document.Markers.Count == 0)
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var color = Theme.Get("MarkerBrush");
        var selectedColor = Theme.Get("MarkerSelectedBrush");
        var selectedBorder = Theme.Get("MarkerSelectedBorderBrush");
        var line = WpfControlHelpers.FrozenHairline(color, pixelsPerDip);
        var selectedLine = WpfControlHelpers.FrozenHairline(selectedBorder, pixelsPerDip);
        var fill = WpfControlHelpers.FrozenBrush(color);
        var selectedFill = WpfControlHelpers.FrozenBrush(selectedColor);
        var selectedPen = new Pen(WpfControlHelpers.FrozenBrush(selectedBorder), 1);
        selectedPen.Freeze();
        var lane = MarkerLaneBounds(bounds);
        _markerFlags.Clear();
        var editing = IsEditingMarkerComment;
        foreach (var marker in _document.Markers)
        {
            var x = FrameToViewX(marker.Frame, start, span, bounds);
            if (x < ScaleLeft(bounds) - 24 || x > bounds.Width + 24)
            {
                continue;
            }

            if (x < ScaleLeft(bounds))
            {
                continue;
            }

            var selected = _selectedMarkerFrames.Contains(marker.Frame);
            var idText = GetMarkerLabel(marker.Id.ToString(CultureInfo.InvariantCulture), pixelsPerDip);
            const double padX = 3;
            if (!_markerFlagLayout.TryGetValue(marker.Frame, out var box))
            {
                var boxW = MeasureFlagWidth(marker.Id.ToString(CultureInfo.InvariantCulture), pixelsPerDip);
                box = LaneFlagRect(x, boxW, lane.Height, SplitFlagLanes, top: !SplitFlagLanes);
            }

            _markerFlags.Add((marker, box));
            var lineTop = lane.Height > 2 ? box.Bottom : 0;
            var xs = WpfControlHelpers.SnapDeviceCenter(x, pixelsPerDip);
            dc.DrawLine(selected ? selectedLine : line, new Point(xs, lineTop), new Point(xs, bounds.Height));
            if (lane.Height <= 2)
            {
                continue;
            }
            if (editing && marker.Frame == _commentEditFrame)
            {
                continue;
            }

            dc.DrawRectangle(selected ? selectedFill : fill, selected ? selectedPen : null, box);
            dc.PushClip(new RectangleGeometry(box));
            dc.DrawText(
                idText,
                new Point(box.X + padX, box.Y + Math.Max(0, (box.Height - idText.Height) * 0.5)));
            dc.Pop();

            if (!string.IsNullOrEmpty(marker.Comment))
            {
                var commentText = GetMarkerCommentLabel(TruncateMarkerComment(marker.Comment), pixelsPerDip, selected);
                const double commentGap = 2;
                var commentX = box.Right + commentGap;
                var commentY = box.Y + Math.Max(0, (box.Height - commentText.Height) * 0.5);
                if (commentX + commentText.Width <= bounds.Width
                    && !TextOverlapsOtherFlag(commentX, commentText.Width, commentY, commentText.Height, box))
                {
                    dc.DrawText(commentText, new Point(commentX, commentY));
                }
            }
        }
    }

    private bool TextOverlapsOtherFlag(double x, double width, double y, double height, Rect except)
    {
        var text = new Rect(x, y, width, height);
        foreach (var box in _regionFlagLayout.Values)
        {
            if (box != except && box.IntersectsWith(text))
            {
                return true;
            }
        }

        foreach (var box in _markerFlagLayout.Values)
        {
            if (box != except && box.IntersectsWith(text))
            {
                return true;
            }
        }

        return false;
    }

    private static string TruncateMarkerComment(string comment) =>
        comment.Length <= 24 ? comment : comment[..23] + "…";

    public bool CancelMarkerCommentEdit()
    {
        if (!IsEditingMarkerComment)
        {
            return false;
        }

        EndMarkerCommentEdit(commit: false);
        return true;
    }

    public bool TryBeginRenameMarker()
    {
        if (_document is null)
        {
            return false;
        }

        long? frame = null;
        if (_document.HasMarkerAt(_playheadFrame))
        {
            frame = _playheadFrame;
        }
        else if (_selectedMarkerFrames.Count == 1)
        {
            foreach (var selected in _selectedMarkerFrames)
            {
                frame = selected;
                break;
            }
        }

        if (frame is long target)
        {
            foreach (var marker in _document.Markers)
            {
                if (marker.Frame != target)
                {
                    continue;
                }

                SelectMarkerFrames([marker.Frame]);
                BeginMarkerCommentEdit(marker);
                return true;
            }
        }

        return TryBeginRenameRegion();
    }

    private bool TryBeginRenameRegion()
    {
        if (_document is null)
        {
            return false;
        }

        var region = WaveSelection.Empty;
        foreach (var edge in _selectedRegionEdges)
        {
            region = edge.Range;
            break;
        }

        if (region.IsEmpty)
        {
            foreach (var item in _document.Regions)
            {
                if (item.StartFrame == _playheadFrame || item.EndFrame == _playheadFrame)
                {
                    region = item;
                    break;
                }
            }

            if (region.IsEmpty)
            {
                _document.TryGetRegionAt(_playheadFrame, out region);
            }
        }

        if (region.IsEmpty)
        {
            return false;
        }

        SelectMarkerFrames([]);
        _selectedRegionEdges.Clear();
        _selectedRegionEdges.Add(new RegionEdge(region, true));
        _selectedRegionEdges.Add(new RegionEdge(region, false));
        BeginRegionNameEdit(region);
        return true;
    }

    private bool TryHitMarkerFlag(Point point, out WaveMarker marker)
    {
        for (var i = _markerFlags.Count - 1; i >= 0; i--)
        {
            var hit = _markerFlags[i];
            if (hit.Flag.Contains(point))
            {
                marker = hit.Marker;
                return true;
            }
        }

        marker = default;
        return false;
    }

    private void BeginScrubInteraction(Point start)
    {
        EndMarkerCommentEdit(commit: true);
        _dragStart = start;
        _dragging = true;
        _scrubbing = true;
        _selecting = false;
        CaptureMouse();
        Cursor = Cursors.ScrollWE;
        var frame = FrameAt(start.X);
        PreviewInteraction(frame, force: true);
        ScrubStarted?.Invoke(this, frame);
    }

    private void FinishScrub(bool commit, long frame)
    {
        if (!_scrubbing)
        {
            return;
        }

        _scrubbing = false;
        _dragging = false;
        _selecting = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Cursor = Cursors.IBeam;
        frame = ClampFrame(frame);
        if (_document is not null)
        {
            PreviewInteraction(frame, force: true);
        }

        ScrubEnded?.Invoke(this, (frame, commit));
    }

    private readonly record struct RegionEdge(WaveSelection Range, bool IsStart)
    {
        public long Frame => IsStart ? Range.StartFrame : Range.EndFrame;
    }

    private static RegionEdge ToRegionEdge(WaveSelection region, long frame) =>
        new(region, frame == region.StartFrame);

    private void BeginMarkerFlagInteraction(WaveMarker marker, Point start)
    {
        EndMarkerCommentEdit(commit: true);
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (shift)
        {
            SelectTimelineRangeTo(marker.Frame, additive: control);
            return;
        }

        if (control)
        {
            if (!_selectedMarkerFrames.Add(marker.Frame))
            {
                _selectedMarkerFrames.Remove(marker.Frame);
            }

            _markerSelectAnchor = marker.Frame;
            InvalidateStaticLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!_selectedMarkerFrames.Contains(marker.Frame))
        {
            ClearTimelineSelection(refresh: false);
            _selectedMarkerFrames.Add(marker.Frame);
            InvalidateStaticLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
        }

        _markerSelectAnchor = marker.Frame;
        if (_document is null || !HasTimelineDragSelection())
        {
            return;
        }

        BeginTimelineDrag(start, marker.Frame);
    }

    private void SelectTimelineRangeTo(long endFrame, bool additive)
    {
        if (_document is null)
        {
            return;
        }

        var startFrame = ResolveTimelineSelectAnchor(endFrame);
        if (!additive)
        {
            ClearTimelineSelection(refresh: false);
        }

        var lo = Math.Min(startFrame, endFrame);
        var hi = Math.Max(startFrame, endFrame);
        foreach (var item in _document.Markers)
        {
            if (item.Frame >= lo && item.Frame <= hi)
            {
                _selectedMarkerFrames.Add(item.Frame);
            }
        }

        foreach (var region in _document.Regions)
        {
            if (region.StartFrame >= lo && region.StartFrame <= hi)
            {
                _selectedRegionEdges.Add(new RegionEdge(region, true));
            }

            if (region.EndFrame >= lo && region.EndFrame <= hi)
            {
                _selectedRegionEdges.Add(new RegionEdge(region, false));
            }
        }

        var loop = _document.SampleLoop;
        if (!loop.IsEmpty)
        {
            if (loop.StartFrame >= lo && loop.StartFrame <= hi)
            {
                _loopStartSelected = true;
            }

            if (loop.EndFrame >= lo && loop.EndFrame <= hi)
            {
                _loopEndSelected = true;
            }
        }

        InvalidateStaticLayer();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    private long ResolveTimelineSelectAnchor(long fallback)
    {
        if (_markerSelectAnchor is long stored)
        {
            return stored;
        }

        var first = long.MaxValue;
        void Consider(long frame)
        {
            if (frame < first)
            {
                first = frame;
            }
        }

        foreach (var frame in _selectedMarkerFrames)
        {
            Consider(frame);
        }

        foreach (var edge in _selectedRegionEdges)
        {
            Consider(edge.Frame);
        }

        if (_document is not null && !_document.SampleLoop.IsEmpty)
        {
            if (_loopStartSelected)
            {
                Consider(_document.SampleLoop.StartFrame);
            }

            if (_loopEndSelected)
            {
                Consider(_document.SampleLoop.EndFrame);
            }
        }

        return first == long.MaxValue ? fallback : first;
    }

    private void BeginRegionFlagInteraction(WaveSelection region, long frame, Point start)
    {
        EndMarkerCommentEdit(commit: true);
        var edge = ToRegionEdge(region, frame);
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (shift)
        {
            SelectTimelineRangeTo(frame, additive: control);
            return;
        }

        if (control)
        {
            if (!_selectedRegionEdges.Add(edge))
            {
                _selectedRegionEdges.Remove(edge);
            }

            _markerSelectAnchor = frame;
            InvalidateStaticLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!_selectedRegionEdges.Contains(edge))
        {
            ClearTimelineSelection(refresh: false);
            _selectedRegionEdges.Add(edge);
            InvalidateStaticLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
        }

        _markerSelectAnchor = frame;
        if (_document is null || !HasTimelineDragSelection())
        {
            return;
        }

        BeginTimelineDrag(start, frame);
    }

    private void DrawSampleLoopBar(DrawingContext dc, Rect lane, double start, double span)
    {
        if (_document is null
            || !MarkerRolePaint.TryGetVisibleRangeRect(_document.SampleLoop, lane, start, span, out var bar))
        {
            return;
        }

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("SampleLoopTimelineBrush")), null, bar);
        if (!TryGetLoopBarRects(out _, out var startHandle, out var endHandle))
        {
            return;
        }

        var grip = WpfControlHelpers.FrozenBrush(Color.FromRgb(0x8E, 0xC4, 0xDC));
        var selected = WpfControlHelpers.FrozenBrush(Theme.Get("MarkerSelectedBorderBrush"));
        dc.DrawRectangle(_loopStartSelected ? selected : grip, null, startHandle);
        dc.DrawRectangle(_loopEndSelected ? selected : grip, null, endHandle);
    }

    private void BeginLoopBarInteraction(Point start)
    {
        EndMarkerCommentEdit(commit: true);
        if (_document is null || _document.SampleLoop.IsEmpty || !TryHitLoopBar(start, out var part))
        {
            return;
        }

        var loop = _document.SampleLoop;
        var isBody = part == LoopBarPart.Body;
        var isStart = part == LoopBarPart.Start;
        var frame = isBody
            ? RawFrameAt(start.X)
            : isStart ? loop.StartFrame : loop.EndFrame;
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (shift)
        {
            SelectTimelineRangeTo(frame, additive: control);
            return;
        }

        if (control)
        {
            if (isBody)
            {
                var both = _loopStartSelected && _loopEndSelected;
                _loopStartSelected = !both;
                _loopEndSelected = !both;
            }
            else if (isStart)
            {
                _loopStartSelected = !_loopStartSelected;
            }
            else
            {
                _loopEndSelected = !_loopEndSelected;
            }

            _markerSelectAnchor = frame;
            InvalidateStaticLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var already = isBody
            ? _loopStartSelected && _loopEndSelected
            : isStart ? _loopStartSelected : _loopEndSelected;
        if (!already)
        {
            ClearTimelineSelection(refresh: false);
            _loopStartSelected = isBody || isStart;
            _loopEndSelected = isBody || !isStart;
            InvalidateStaticLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
        }

        _markerSelectAnchor = frame;
        if (!HasTimelineDragSelection())
        {
            return;
        }

        BeginTimelineDrag(start, frame);
    }

    private bool HasTimelineDragSelection() =>
        _selectedMarkerFrames.Count > 0
        || _selectedRegionEdges.Count > 0
        || _loopStartSelected
        || _loopEndSelected;

    private bool IsSelectedRegionOrLoopFrame(long frame)
    {
        if (_loopStartSelected && _document?.SampleLoop.StartFrame == frame)
        {
            return true;
        }

        if (_loopEndSelected && _document?.SampleLoop.EndFrame == frame)
        {
            return true;
        }

        foreach (var edge in _selectedRegionEdges)
        {
            if (edge.Frame == frame)
            {
                return true;
            }
        }

        return false;
    }

    private void BeginTimelineDrag(Point start, long primaryFrame)
    {
        if (_document is null)
        {
            return;
        }

        TimelineDragStarting?.Invoke(this, EventArgs.Empty);
        _dragStart = start;
        _markerDragging = true;
        _markerDragMoved = false;
        _markerDragLastDelta = long.MinValue;
        _markerDragSnapHold = null;
        _markerDragPrimaryOrigin = primaryFrame;
        _markerDragOrigins = _selectedMarkerFrames.OrderBy(frame => frame).ToArray();
        _markerDragBefore = _document.SnapshotMarkers();
        _regionDragOrigins = [.. _selectedRegionEdges];
        _regionsDragBefore = _document.SnapshotRegions();
        _loopDragStart = _loopStartSelected && !_document.SampleLoop.IsEmpty;
        _loopDragEnd = _loopEndSelected && !_document.SampleLoop.IsEmpty;
        _loopDragBefore = _document.SampleLoop;
        _markerDragFollowOrigin = IsSelectedTimelineFrame(_playheadFrame) ? _playheadFrame : null;
        CaptureMouse();
        Cursor = Cursors.SizeWE;
    }

    private bool IsSelectedTimelineFrame(long frame)
    {
        if (_selectedMarkerFrames.Contains(frame))
        {
            return true;
        }

        return IsSelectedRegionOrLoopFrame(frame);
    }

    private long DesiredTimelineDragDelta(double x)
    {
        var exclude = TimelineDragSnapExclude();
        if (TrySnapXToMarker(x, exclude, out _, out var snapped))
        {
            _markerDragSnapHold = snapped;
            return snapped - _markerDragPrimaryOrigin;
        }

        if (_markerDragSnapHold is long held
            && TryGetSnapHoldDistance(x, held, out var holdDist)
            && holdDist <= MarkerSnapPx * 1.5)
        {
            return held - _markerDragPrimaryOrigin;
        }

        _markerDragSnapHold = null;
        return RawFrameAt(x) - _markerDragPrimaryOrigin;
    }

    private bool TryGetSnapHoldDistance(double mouseX, long frame, out double dist)
    {
        dist = 0;
        if (ContentWidth <= 0)
        {
            return false;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dist = Math.Abs(FrameToViewX(frame, _viewStart, ViewSpanFrames, bounds) - mouseX);
        return true;
    }

    private HashSet<long>? TimelineDragSnapExclude()
    {
        var exclude = new HashSet<long>();
        foreach (var frame in _markerDragOrigins)
        {
            exclude.Add(frame);
        }

        foreach (var frame in _selectedMarkerFrames)
        {
            exclude.Add(frame);
        }

        return exclude.Count == 0 ? null : exclude;
    }

    private bool IsDraggingRegionEdge(WaveSelection range, bool isStart)
    {
        if (!_markerDragging)
        {
            return false;
        }

        foreach (var edge in _selectedRegionEdges)
        {
            if (edge.IsStart == isStart && edge.Range == range)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyMarkerDrag(long desiredDelta)
    {
        if (_document is null || !_markerDragging || desiredDelta == _markerDragLastDelta)
        {
            return;
        }

        RestoreTimelineDragOrigins();
        var applied = ApplyTimelineDelta(
            _markerDragOrigins,
            _regionDragOrigins,
            _loopDragStart,
            _loopDragEnd,
            _loopDragBefore,
            desiredDelta);
        _markerDragLastDelta = desiredDelta;
        RemapSelectedMarkerFrames(_markerDragOrigins, applied);
        RemapSelectedRegionEdges(_regionDragOrigins, applied);
        _loopStartSelected = _loopDragStart;
        _loopEndSelected = _loopDragEnd;
        if (_markerDragFollowOrigin is long follow)
        {
            var next = follow + applied;
            _playheadFrame = next;
            _document.CursorFrame = next;
        }

        InvalidateStaticLayer();
        InvalidatePlayheadLayer();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    private long ApplyTimelineDelta(
        IReadOnlyList<long> markerOrigins,
        IReadOnlyList<RegionEdge> regionOrigins,
        bool loopStart,
        bool loopEnd,
        WaveSelection loopOrigin,
        long desiredDelta)
    {
        if (_document is null)
        {
            return 0;
        }

        var moves = ToRegionMoves(regionOrigins);
        if ((loopStart || loopEnd) && !loopOrigin.IsEmpty)
        {
            moves.Add(new RangeEdgeMove(loopOrigin, loopStart, loopEnd));
        }

        var sorted = markerOrigins as long[] ?? [.. markerOrigins.OrderBy(frame => frame)];
        var occupied = new HashSet<long>();
        foreach (var marker in _document.Markers)
        {
            if (Array.BinarySearch(sorted, marker.Frame) < 0)
            {
                occupied.Add(marker.Frame);
            }
        }

        var applied = TimelineMoves.Resolve(sorted, occupied, moves, desiredDelta, _document.FrameCount);
        if (applied == 0)
        {
            return 0;
        }

        if (sorted.Length > 0)
        {
            _document.TryMoveMarkers(sorted, applied, out _, markDirty: false);
        }

        var regionMoves = ToRegionMoves(regionOrigins);
        if (regionMoves.Count > 0)
        {
            _document.TryMoveRegionEdges(regionMoves, applied, out _, markDirty: false);
        }

        if (loopStart || loopEnd)
        {
            _document.TryMoveSampleLoopEdges(loopStart, loopEnd, applied, out _, markDirty: false);
        }

        return applied;
    }

    private static List<RangeEdgeMove> ToRegionMoves(IReadOnlyList<RegionEdge> edges)
    {
        var map = new Dictionary<WaveSelection, RangeEdgeMove>();
        foreach (var edge in edges)
        {
            if (map.TryGetValue(edge.Range, out var existing))
            {
                map[edge.Range] = existing with
                {
                    Start = existing.Start || edge.IsStart,
                    End = existing.End || !edge.IsStart,
                };
            }
            else
            {
                map[edge.Range] = new RangeEdgeMove(edge.Range, edge.IsStart, !edge.IsStart);
            }
        }

        return [.. map.Values];
    }

    private void RestoreTimelineDragOrigins()
    {
        if (_document is null)
        {
            return;
        }

        _document.ReplaceMarkers(_markerDragBefore, markDirty: false);
        _document.SetRegions(_regionsDragBefore, markDirty: false);
        _document.SetSampleLoop(_loopDragBefore, markDirty: false);
    }

    private void FinishMarkerDrag(bool commit)
    {
        if (!_markerDragging)
        {
            return;
        }

        var markersBefore = _markerDragBefore;
        var regionsBefore = _regionsDragBefore;
        var loopBefore = _loopDragBefore;
        var moved = _markerDragMoved;
        var follow = _markerDragFollowOrigin;
        _markerDragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Cursor = Cursors.IBeam;
        if (_document is null)
        {
            ResetMarkerDragState();
            return;
        }

        if (!commit || !moved)
        {
            RestoreTimelineDragOrigins();
            RemapSelectedMarkerFrames(_markerDragOrigins, 0);
            RemapSelectedRegionEdges(_regionDragOrigins, 0);
            _loopStartSelected = _loopDragStart;
            _loopEndSelected = _loopDragEnd;
            if (follow is long origin)
            {
                _playheadFrame = origin;
                _document.CursorFrame = origin;
            }

            ResetMarkerDragState();
            InvalidateStaticLayer();
            InvalidatePlayheadLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var changed = !markersBefore.AsSpan().SequenceEqual(_document.SnapshotMarkers())
            || !regionsBefore.AsSpan().SequenceEqual(_document.SnapshotRegions())
            || loopBefore != _document.SampleLoop;
        ResetMarkerDragState();
        InvalidateStaticLayer();
        InvalidatePlayheadLayer();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
        if (changed)
        {
            TimelineLayoutCommitted?.Invoke(this, (markersBefore, regionsBefore, loopBefore));
            if (follow is not null)
            {
                CursorCommitted?.Invoke(this, _playheadFrame);
            }
        }
    }

    private void RemapSelectedMarkerFrames(IReadOnlyList<long> origins, long appliedDelta)
    {
        if (_markerSelectAnchor is long anchor)
        {
            foreach (var frame in origins)
            {
                if (frame == anchor)
                {
                    _markerSelectAnchor = anchor + appliedDelta;
                    break;
                }
            }
        }

        _selectedMarkerFrames.Clear();
        foreach (var frame in origins)
        {
            _selectedMarkerFrames.Add(frame + appliedDelta);
        }
    }

    private void RemapSelectedRegionEdges(IReadOnlyList<RegionEdge> origins, long appliedDelta)
    {
        var byRange = new Dictionary<WaveSelection, (bool Start, bool End)>();
        foreach (var edge in origins)
        {
            byRange.TryGetValue(edge.Range, out var flags);
            byRange[edge.Range] = (flags.Start || edge.IsStart, flags.End || !edge.IsStart);
        }

        _selectedRegionEdges.Clear();
        foreach (var pair in byRange)
        {
            var next = TimelineMoves.ShiftEdges(pair.Key, pair.Value.Start, pair.Value.End, appliedDelta);
            if (pair.Value.Start)
            {
                _selectedRegionEdges.Add(new RegionEdge(next, true));
            }

            if (pair.Value.End)
            {
                _selectedRegionEdges.Add(new RegionEdge(next, false));
            }

            if (_markerSelectAnchor is long anchor)
            {
                if (pair.Value.Start && pair.Key.StartFrame == anchor)
                {
                    _markerSelectAnchor = pair.Key.StartFrame + appliedDelta;
                }
                else if (pair.Value.End && pair.Key.EndFrame == anchor)
                {
                    _markerSelectAnchor = pair.Key.EndFrame + appliedDelta;
                }
            }
        }
    }

    private void ResetMarkerDragState()
    {
        _markerDragging = false;
        _markerDragMoved = false;
        _markerDragPrimaryOrigin = 0;
        _markerDragFollowOrigin = null;
        _markerDragLastDelta = long.MinValue;
        _markerDragSnapHold = null;
        _markerDragOrigins = [];
        _markerDragBefore = [];
        _regionDragOrigins = [];
        _regionsDragBefore = [];
        _loopDragStart = false;
        _loopDragEnd = false;
        _loopDragBefore = WaveSelection.Empty;
    }

    private void BeginRegionNameEdit(WaveSelection region)
    {
        if (_document is null || region.IsEmpty)
        {
            return;
        }

        _commentEditor ??= CreateMarkerCommentEditor();
        _commentEditFrame = -1;
        _commentEditRegion = region;
        _commentEditor.Text = _document.RegionName(region);
        PlaceRegionNameEditor(region);
        _commentEditor.Visibility = Visibility.Visible;
        _commentEditor.Focus();
        _commentEditor.SelectAll();
        InvalidatePlayheadLayer();
    }

    private void PlaceRegionNameEditor(WaveSelection region)
    {
        if (_commentEditor is null)
        {
            return;
        }

        Rect flag = default;
        foreach (var item in _regionFlags)
        {
            if (item.Range == region && item.Frame == region.StartFrame)
            {
                flag = item.Flag;
                break;
            }
        }

        if (flag.Width < 8)
        {
            var x = FrameToViewX(region.StartFrame, _viewStart, ViewSpanFrames, new Rect(0, 0, ActualWidth, ActualHeight));
            flag = LaneFlagRect(x, 24, MarkerLaneHeight, SplitFlagLanes, top: true);
        }

        const double commentGap = 2;
        var editorX = flag.Right + commentGap;
        _commentEditor.Width = Math.Clamp(160, 80, Math.Max(80, ActualWidth - editorX));
        _commentEditor.Height = Math.Max(12, flag.Height);
        _commentEditor.Margin = new Thickness(editorX, flag.Y, 0, 0);
    }

    private void BeginMarkerCommentEdit(WaveMarker marker)
    {
        _commentEditor ??= CreateMarkerCommentEditor();
        _commentEditFrame = marker.Frame;
        _commentEditRegion = WaveSelection.Empty;
        _commentEditor.Text = marker.Comment ?? string.Empty;
        PlaceMarkerCommentEditor(marker);
        _commentEditor.Visibility = Visibility.Visible;
        _commentEditor.Focus();
        _commentEditor.SelectAll();
        InvalidatePlayheadLayer();
    }

    private void PlaceMarkerCommentEditor(WaveMarker marker)
    {
        if (_commentEditor is null)
        {
            return;
        }

        Rect flag = default;
        foreach (var item in _markerFlags)
        {
            if (item.Marker.Frame == marker.Frame)
            {
                flag = item.Flag;
                break;
            }
        }

        if (flag.Width < 8)
        {
            var x = FrameToViewX(marker.Frame, _viewStart, ViewSpanFrames, new Rect(0, 0, ActualWidth, ActualHeight));
            flag = LaneFlagRect(x, 24, MarkerLaneHeight, SplitFlagLanes, top: !SplitFlagLanes);
        }

        const double commentGap = 2;
        var editorX = flag.Right + commentGap;
        _commentEditor.Width = Math.Clamp(160, 80, Math.Max(80, ActualWidth - editorX));
        _commentEditor.Height = Math.Max(12, flag.Height);
        _commentEditor.Margin = new Thickness(editorX, flag.Y, 0, 0);
    }

    private TextBox CreateMarkerCommentEditor()
    {
        var editor = new TextBox
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Padding = new Thickness(3, 0, 3, 0),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            VerticalContentAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        editor.SetResourceReference(StyleProperty, "DarkTextBoxStyle");
        editor.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        editor.SetResourceReference(Control.BackgroundProperty, "TimelineWellBackBrush");
        editor.SetResourceReference(Control.BorderBrushProperty, "MarkerBrush");
        editor.SetResourceReference(TextBox.CaretBrushProperty, "PrimaryForeBrush");
        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                EndMarkerCommentEdit(commit: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                EndMarkerCommentEdit(commit: false);
                e.Handled = true;
            }
        };
        editor.LostFocus += (_, _) => EndMarkerCommentEdit(commit: true);
        Panel.SetZIndex(editor, 10);
        Children.Add(editor);
        return editor;
    }

    private void EndMarkerCommentEdit(bool commit)
    {
        if (_endingCommentEdit || _commentEditor is null || _commentEditor.Visibility != Visibility.Visible)
        {
            return;
        }

        _endingCommentEdit = true;
        try
        {
            var frame = _commentEditFrame;
            var region = _commentEditRegion;
            var text = _commentEditor.Text;
            _commentEditor.Visibility = Visibility.Collapsed;
            _commentEditFrame = -1;
            _commentEditRegion = WaveSelection.Empty;
            if (commit)
            {
                if (!region.IsEmpty)
                {
                    RegionNameCommitted?.Invoke(this, (region, text.Trim()));
                }
                else if (frame >= 0)
                {
                    MarkerCommentCommitted?.Invoke(this, (frame, text.Trim()));
                }
            }

            ClearMarkerSelection();
            Focus();
            InvalidatePlayheadLayer();
        }
        finally
        {
            _endingCommentEdit = false;
        }
    }

    private FormattedText GetMarkerLabel(string text, double pixelsPerDip)
    {
        if (_markerLabelCache.TryGetValue(text, out var cached))
        {
            return cached;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            10,
            WpfControlHelpers.FrozenBrush(Theme.Get("MarkerLabelForeBrush")),
            pixelsPerDip);
        _markerLabelCache[text] = formatted;
        return formatted;
    }

    private FormattedText GetMarkerCommentLabel(string text, double pixelsPerDip, bool selected)
    {
        var key = (text, selected);
        if (_markerCommentLabelCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            10,
            WpfControlHelpers.FrozenBrush(Theme.Get(selected ? "AccentCyanBrush" : "PrimaryForeBrush")),
            pixelsPerDip);
        _markerCommentLabelCache[key] = formatted;
        return formatted;
    }

    private enum TimeLabelAccent
    {
        None,
        Loop,
        Region,
    }

    private FormattedText GetTimeLabel(string text, double pixelsPerDip, TimeLabelAccent accent)
    {
        var key = (text, accent);
        if (_timeLabelCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var brushKey = accent switch
        {
            TimeLabelAccent.Loop => "SampleLoopTimeLabelForeBrush",
            TimeLabelAccent.Region => "RegionTimeLabelForeBrush",
            _ => "MutedForeBrush",
        };
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            10,
            WpfControlHelpers.FrozenBrush(Theme.Get(brushKey)),
            pixelsPerDip);
        if (_timeLabelCache.Count >= TimeLabelCacheMax)
        {
            // 深い拡大の追従スクロール中は目盛りラベル（ms 単位）が毎回新しく
            // 際限なく増えるため、上限で丸ごと捨てる（表示中の数十個は次回再生成）。
            _timeLabelCache.Clear();
        }

        _timeLabelCache[key] = formatted;
        return formatted;
    }

    private void DrawRange(
        DrawingContext dc,
        Rect bounds,
        double start,
        double span,
        WaveSelection selection,
        Color color)
    {
        var wave = WaveformBounds(bounds);
        var x0 = FrameToViewX(selection.StartFrame, start, span, bounds);
        var x1 = FrameToViewX(selection.EndFrame, start, span, bounds);
        if (x1 < wave.X || x0 > wave.Right)
        {
            return;
        }

        x0 = Math.Clamp(x0, wave.X, wave.Right);
        x1 = Math.Clamp(x1, wave.X, wave.Right);
        var width = Math.Max(1, x1 - x0);
        foreach (var lane in SelectedLaneRects(wave))
        {
            dc.DrawRectangle(
                WpfControlHelpers.FrozenBrush(color),
                null,
                new Rect(x0, lane.Y, width, lane.Height));
        }
    }

    internal static double NiceTimeStep(double visibleSeconds)
    {
        var raw = visibleSeconds / 8d;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1e-6))));
        var mantissa = raw / pow;
        var nice = mantissa < 2 ? 1d : mantissa < 5 ? 2d : 5d;
        return nice * pow;
    }

    private void SetTimeZoom(double zoom, double anchorFrame)
    {
        if (_document is null)
        {
            return;
        }

        var oldSpan = ViewSpanFrames;
        var ratio = oldSpan <= 0 ? 0 : (anchorFrame - _viewStart) / oldSpan;
        var nextZoom = Math.Clamp(zoom, 1d, TimeZoomMax);
        if (Math.Abs(nextZoom - _timeZoom) < 1e-12)
        {
            return;
        }

        EndMarkerCommentEdit(commit: true);
        _timeZoom = nextZoom;
        var newSpan = ViewSpanFrames;
        var max = Math.Max(0d, _document.FrameCount - newSpan);
        _viewStart = Math.Clamp(anchorFrame - newSpan * ratio, 0d, max);
        InvalidateStaticLayer();
        RaiseViewChanged();
    }

    public void SetVisibleRange(double viewStart, double viewSpan)
    {
        if (_document is null || _document.FrameCount <= 0)
        {
            return;
        }

        var total = (double)_document.FrameCount;
        var minSpan = Math.Max(1d, total / TimeZoomMax);
        var span = Math.Clamp(viewSpan, minSpan, total);
        var nextZoom = Math.Clamp(total / span, 1d, TimeZoomMax);
        var actualSpan = Math.Max(1d, total / Math.Max(1d, nextZoom));
        var nextStart = Math.Clamp(viewStart, 0d, Math.Max(0d, total - actualSpan));
        if (Math.Abs(nextZoom - _timeZoom) < 1e-12 && Math.Abs(nextStart - _viewStart) < 0.0001)
        {
            return;
        }

        EndMarkerCommentEdit(commit: true);
        _timeZoom = nextZoom;
        _viewStart = nextStart;
        InvalidateStaticLayer();
        RaiseViewChanged();
    }

    private void SetAmpZoom(double zoom)
    {
        var next = Math.Clamp(zoom, 1d, AmpZoomMax);
        if (Math.Abs(next - _ampZoom) < 1e-12)
        {
            return;
        }

        _ampZoom = next;
        InvalidateStaticLayer();
        RaiseViewChanged();
    }

    private void SetViewStart(double start, bool playbackFollow = false, bool deferReload = false)
    {
        if (_document is null)
        {
            if (_viewStart == 0)
            {
                return;
            }

            _viewStart = 0;
            _deferWaveReload = false;
            InvalidateStaticLayer();
            RaiseViewChanged();
            return;
        }

        var max = Math.Max(0d, _document.FrameCount - ViewSpanFrames);
        var next = Math.Clamp(start, 0d, max);
        if (Math.Abs(next - _viewStart) < 0.0001)
        {
            return;
        }

        if (!playbackFollow && !deferReload)
        {
            EndMarkerCommentEdit(commit: true);
        }

        _viewStart = next;
        _snapCacheDirty = true;
        QueueMouseGuideOverlay();
        if (deferReload)
        {
            // キャッシュした波形／スペクトログラムをずらして先にスクロールする。
            // 足りない端の読み直しは入力が空いてから（ContextIdle）。
            _deferWaveReload = true;
            InvalidateStaticLayer();
            RaiseViewChanged();
            QueueDeferredStaticRebuild();
            return;
        }

        _deferWaveReload = false;
        if (playbackFollow)
        {
            // 追従スクロール。シークバー層は毎回更新。静的層（波形＋スペクトログラム）は
            // 実測描画コストに応じて間引く：軽ければ毎フレーム（≒60fps）で滑らかに流し、
            // 深い拡大で重い場合は間隔を広げ、ディスパッチャに入力処理の余地を残す
            // （Render 優先度の連続再描画がマウス／キー入力を飢餓させて操作不能になるのを防ぐ）。
            InvalidatePlayheadOnly();
            // TickCount64 は分解能約 15ms で毎フレーム判定に使えないため Stopwatch。
            var nowMs = System.Diagnostics.Stopwatch.GetTimestamp()
                * 1000d / System.Diagnostics.Stopwatch.Frequency;
            // 「前回ペイントの終了時刻」を起点に、ペイント実測コストに比例した休止を必ず挟む。
            // 起点を要求時刻にすると重いペイント自体が休止を食い潰して隙間が消える。
            // また休止に上限を設けると、ペイントが上限より重い深い拡大で隙間ゼロ＝入力飢餓
            // （操作不能・終了不能）に戻るため、上限は設けない。急変に即応するため
            // EMA と直近実測の大きい方を使う。
            var idleMs = Math.Max(Math.Max(_staticPaintMs, _staticPaintLastMs) * 1.5, 8d);
            var lastMs = Math.Max(_followRebuildAtMs, _staticPaintEndMs);
            if (nowMs - lastMs >= idleMs)
            {
                _followRebuildAtMs = nowMs;
                InvalidateStaticLayer();
                RaiseViewChanged();
            }

            return;
        }

        InvalidateStaticLayer();
        RaiseViewChanged();
    }

    public void SetViewStartExternal(double start, bool playbackFollow = false) =>
        SetViewStart(start, playbackFollow);

    /// <summary>
    /// 概要ドラッグ用。表示位置はすぐ動かし、ピーク／スペクトログラムの読み直しは遅らせる。
    /// </summary>
    public void PanViewStart(double start) => SetViewStart(start, deferReload: true);

    public void CommitPannedView()
    {
        if (!_deferWaveReload && !_deferredStaticQueued)
        {
            return;
        }

        _deferWaveReload = false;
        InvalidateStaticLayer();
    }

    public void ApplyPersistedView(double timeZoom, double ampZoom, double viewStart, long playhead)
    {
        if (_document is null)
        {
            return;
        }

        _timeZoom = Math.Clamp(timeZoom, 1d, TimeZoomMax);
        _ampZoom = Math.Clamp(ampZoom, 1d, AmpZoomMax);
        var max = Math.Max(0d, _document.FrameCount - ViewSpanFrames);
        _viewStart = Math.Clamp(viewStart, 0d, max);
        _playheadFrame = Math.Clamp(playhead, 0, _document.FrameCount);
        _document.CursorFrame = _playheadFrame;
        InvalidateStaticLayer();
        RaiseViewChanged();
    }

    private double AnchorFrame(bool playhead) =>
        playhead ? _playheadFrame : _viewStart + ViewSpanFrames * 0.5;

    private long XToFrame(double x)
    {
        if (_document is null || ContentWidth <= 0)
        {
            return 0;
        }

        var local = Math.Clamp(x - ContentLeft, 0, ContentWidth);
        return (long)Math.Round(_viewStart + (local / ContentWidth) * ViewSpanFrames);
    }

    private long RawFrameAt(double x) => ClampFrame(XToFrame(x));

    private long PointerFrame(double x) =>
        CanSnapPointerToMarkers && TrySnapXToMarker(x, null, out _, out var frame)
            ? frame
            : RawFrameAt(x);

    private bool CanSnapPointerToMarkers =>
        !_markerDragging
        && _document is not null
        && (_document.Markers.Count > 0 || !_document.SampleLoop.IsEmpty || _document.Regions.Count > 0);

    private void SetMouseGuideFromX(double x) => _pointerX = x;

    private void ClearMouseGuide()
    {
        _pointerX = null;
        _mouseGuideX = null;
    }

    private void ResolveMouseGuideX()
    {
        if (_pointerX is not double x || x < ContentLeft)
        {
            _mouseGuideX = null;
            return;
        }

        if (_markerDragging)
        {
            if (TrySnapXToMarker(x, TimelineDragSnapExclude(), out var dragSnapX, out _))
            {
                _mouseGuideX = dragSnapX;
            }
            else if (_markerDragSnapHold is long held
                && TryGetSnapHoldDistance(x, held, out var holdDist)
                && holdDist <= MarkerSnapPx * 1.5)
            {
                _mouseGuideX = FrameToViewX(held, _viewStart, ViewSpanFrames, new Rect(0, 0, ActualWidth, ActualHeight));
            }
            else
            {
                _mouseGuideX = x;
            }

            return;
        }

        _mouseGuideX = CanSnapPointerToMarkers && TrySnapXToMarker(x, null, out var snappedX, out _)
            ? snappedX
            : x;
    }

    private void QueueMouseGuideOverlay()
    {
        if (_mouseGuideQueued)
        {
            return;
        }

        _mouseGuideQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, FlushMouseGuideOverlay);
    }

    private void FlushMouseGuideOverlay()
    {
        _mouseGuideQueued = false;
        ApplyMouseGuideOverlay();
    }

    private bool TrySnapXToMarker(double mouseX, HashSet<long>? exclude, out double snappedX, out long frame)
    {
        snappedX = 0;
        frame = 0;
        if (_document is null || ContentWidth <= 0)
        {
            return false;
        }

        if (!_markerDragging)
        {
            EnsureSnapCache();
            var bestDist = MarkerSnapPx;
            long? best = null;
            var bestX = 0d;
            for (var i = 0; i < _snapPoints.Count; i++)
            {
                var point = _snapPoints[i];
                if (exclude is not null && exclude.Contains(point.Frame))
                {
                    continue;
                }

                var dist = Math.Abs(point.X - mouseX);
                if (dist > bestDist)
                {
                    continue;
                }

                bestDist = dist;
                best = point.Frame;
                bestX = point.X;
            }

            if (best is not { } cached)
            {
                return false;
            }

            snappedX = bestX;
            frame = cached;
            return true;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var start = _viewStart;
        var span = ViewSpanFrames;
        var viewEnd = start + span;
        var dragBestDist = MarkerSnapPx;
        long? dragBest = null;
        var dragBestX = 0d;

        void Consider(long markerFrame)
        {
            if (exclude is not null && exclude.Contains(markerFrame))
            {
                return;
            }

            if (markerFrame < start - 1e-9 || markerFrame > viewEnd + 1e-9)
            {
                return;
            }

            var x = FrameToViewX(markerFrame, start, span, bounds);
            var dist = Math.Abs(x - mouseX);
            if (dist > dragBestDist)
            {
                return;
            }

            dragBestDist = dist;
            dragBest = markerFrame;
            dragBestX = x;
        }

        foreach (var marker in _document.Markers)
        {
            Consider(marker.Frame);
        }

        var loop = _document.SampleLoop;
        if (!loop.IsEmpty)
        {
            // 動かしている端そのものは除外する。同じフレームの他マーカーは残す（吸着後の震え防止）。
            if (!_loopDragStart)
            {
                Consider(loop.StartFrame);
            }

            if (!_loopDragEnd)
            {
                Consider(loop.EndFrame);
            }
        }

        foreach (var region in _document.Regions)
        {
            if (!IsDraggingRegionEdge(region, isStart: true))
            {
                Consider(region.StartFrame);
            }

            if (!IsDraggingRegionEdge(region, isStart: false))
            {
                Consider(region.EndFrame);
            }
        }

        if (dragBest is not { } found)
        {
            return false;
        }

        snappedX = dragBestX;
        frame = found;
        return true;
    }

    private void EnsureSnapCache()
    {
        if (!_snapCacheDirty)
        {
            return;
        }

        _snapCacheDirty = false;
        _snapPoints.Clear();
        if (_document is null || ContentWidth <= 0)
        {
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var start = _viewStart;
        var span = ViewSpanFrames;
        var viewEnd = start + span;

        void Add(long markerFrame)
        {
            if (markerFrame < start - 1e-9 || markerFrame > viewEnd + 1e-9)
            {
                return;
            }

            _snapPoints.Add((FrameToViewX(markerFrame, start, span, bounds), markerFrame));
        }

        foreach (var marker in _document.Markers)
        {
            Add(marker.Frame);
        }

        var loop = _document.SampleLoop;
        if (!loop.IsEmpty)
        {
            Add(loop.StartFrame);
            Add(loop.EndFrame);
        }

        foreach (var region in _document.Regions)
        {
            Add(region.StartFrame);
            Add(region.EndFrame);
        }
    }

    internal static bool TryPickNearestSnap(
        IReadOnlyList<long> frames,
        double mouseX,
        double maxDistPx,
        double viewStart,
        double viewEnd,
        Func<long, double> frameToX,
        out double snappedX,
        out long frame)
    {
        snappedX = 0;
        frame = 0;
        var bestDist = maxDistPx;
        long? best = null;
        var bestX = 0d;
        for (var i = 0; i < frames.Count; i++)
        {
            var markerFrame = frames[i];
            if (markerFrame < viewStart - 1e-9 || markerFrame > viewEnd + 1e-9)
            {
                continue;
            }

            var x = frameToX(markerFrame);
            var dist = Math.Abs(x - mouseX);
            if (dist > bestDist)
            {
                continue;
            }

            bestDist = dist;
            best = markerFrame;
            bestX = x;
        }

        if (best is not { } found)
        {
            return false;
        }

        snappedX = bestX;
        frame = found;
        return true;
    }

    private static double FrameToX(double frame, double start, double span, double width) =>
        span <= 0 ? 0 : (frame - start) / span * width;

    private long ClampFrame(long frame)
    {
        if (_document is null)
        {
            return 0;
        }

        return Math.Clamp(frame, 0, _document.FrameCount);
    }

    private void PreviewInteraction(long frame, bool force = false)
    {
        if (_document is null)
        {
            return;
        }

        if (!force && frame == _playheadFrame)
        {
            return;
        }

        ResetTrailIfRewound(frame, _trailSamples);
        _document.CursorFrame = frame;
        _playheadFrame = frame;
        InvalidatePlayheadOnly();
    }

    private void CommitCursor(long frame, bool ensureVisible = true)
    {
        if (_document is null)
        {
            return;
        }

        frame = ClampFrame(frame);
        if (ensureVisible && !IsInteracting)
        {
            EnsureFrameVisible(frame);
        }

        PreviewInteraction(frame);
        _keyboardSelectAnchor = null;
        CursorCommitted?.Invoke(this, frame);
    }

    private void InvalidateWaveform()
    {
        _waveDirty = true;
        InvalidateStaticLayer();
    }

    private void QueueDeferredStaticRebuild()
    {
        if (_deferredStaticQueued)
        {
            return;
        }

        _deferredStaticQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, FlushDeferredStaticRebuild);
    }

    private void FlushDeferredStaticRebuild()
    {
        _deferredStaticQueued = false;
        _deferWaveReload = false;
        InvalidateStaticLayer();
    }

    private void RaiseViewChanged()
    {
        if (_viewChangedQueued)
        {
            return;
        }

        _viewChangedQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, FlushViewChanged);
    }

    private void FlushViewChanged()
    {
        _viewChangedQueued = false;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void InvalidateStaticLayer()
    {
        _snapCacheDirty = true;
        if (_staticRebuildQueued)
        {
            return;
        }

        _staticRebuildQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, FlushStaticRebuild);
    }

    private void FlushStaticRebuild()
    {
        _staticRebuildQueued = false;
        SyncMouseGuideHeight();
        _staticHost.InvalidateVisual();
        _overlayHost.InvalidateVisual();
        _playheadHost.InvalidateVisual();
    }

    private void InvalidatePlayheadLayer()
    {
        _overlayHost.InvalidateVisual();
        _playheadHost.InvalidateVisual();
    }

    private void InvalidatePlayheadOnly() => _playheadHost.InvalidateVisual();

    private void DrawPlayhead(DrawingContext dc, Rect bounds, double start, double span)
    {
        var wave = WaveformBounds(bounds);
        if (wave.Width <= 1 || wave.Height <= 1)
        {
            return;
        }

        // dB 列とスクロール行には出さない。左端拡縮で再生位置が画面外になると、
        // クリップなしでは ＋－ の上にヘッドが描かれる。
        dc.PushClip(new RectangleGeometry(new Rect(wave.X, 0, wave.Width, wave.Y + wave.Height)));
        try
        {
            var playX = FrameToViewX(_playheadFrame, start, span, bounds);
            if (_exitPlayheadFrame >= 0)
            {
                var exitX = FrameToViewX(_exitPlayheadFrame, start, span, bounds);
                DrawSeekPlaybackTrail(dc, bounds, exitX, start, span, _exitTrailSamples, Theme.Get("SeekExitBrush"));
            }

            DrawSeekPlaybackTrail(dc, bounds, playX, start, span, _trailSamples, Theme.Get("PlayheadBrush"));
            EnsurePlayheadPens();
            var y0 = wave.Y;
            var y1 = wave.Y + wave.Height;
            if (_exitPlayheadFrame >= 0)
            {
                // -E 二重再生ヘッド（赤）。メインヘッドより下層に描く。
                EnsureExitPlayheadPens();
                var exitX = FrameToViewX(_exitPlayheadFrame, start, span, bounds);
                if (IsPlayheadXVisible(exitX, wave))
                {
                    dc.DrawLine(_exitPlayheadGlowOuter, new Point(exitX, y0), new Point(exitX, y1));
                    dc.DrawLine(_exitPlayheadGlowInner, new Point(exitX, y0), new Point(exitX, y1));
                    dc.DrawLine(_exitPlayheadCore, new Point(exitX, y0), new Point(exitX, y1));
                }
            }

            if (!IsPlayheadXVisible(playX, wave))
            {
                return;
            }

            dc.DrawLine(_playheadGlowOuter, new Point(playX, y0), new Point(playX, y1));
            dc.DrawLine(_playheadGlowInner, new Point(playX, y0), new Point(playX, y1));
            dc.DrawLine(_playheadCore, new Point(playX, y0), new Point(playX, y1));
        }
        finally
        {
            dc.Pop();
        }
    }

    private static bool IsPlayheadXVisible(double x, Rect wave) =>
        x >= wave.X - 1 && x <= wave.X + wave.Width + 1;

    private void EnsureExitPlayheadPens()
    {
        var color = Theme.Get("SeekExitBrush");
        if (_exitPlayheadCore is not null && _exitPlayheadPenColor == color)
        {
            return;
        }

        _exitPlayheadPenColor = color;
        _exitPlayheadGlowOuter = new Pen(WpfControlHelpers.FrozenBrush(Color.FromArgb(40, color.R, color.G, color.B)), 3);
        _exitPlayheadGlowInner = new Pen(WpfControlHelpers.FrozenBrush(Color.FromArgb(90, color.R, color.G, color.B)), 1.5);
        _exitPlayheadCore = new Pen(WpfControlHelpers.FrozenBrush(color), 1);
        _exitPlayheadGlowOuter.Freeze();
        _exitPlayheadGlowInner.Freeze();
        _exitPlayheadCore.Freeze();
    }

    private void EnsurePlayheadPens()
    {
        var color = Theme.Get("PlayheadBrush");
        if (_playheadCore is not null && _playheadPenColor == color)
        {
            return;
        }

        _playheadPenColor = color;
        _playheadGlowOuter = new Pen(WpfControlHelpers.FrozenBrush(Color.FromArgb(40, color.R, color.G, color.B)), 3);
        _playheadGlowInner = new Pen(WpfControlHelpers.FrozenBrush(Color.FromArgb(90, color.R, color.G, color.B)), 1.5);
        _playheadCore = new Pen(WpfControlHelpers.FrozenBrush(color), 1);
        _playheadGlowOuter.Freeze();
        _playheadGlowInner.Freeze();
        _playheadCore.Freeze();
    }

    private void DrawSeekPlaybackTrail(
        DrawingContext dc,
        Rect bounds,
        double playheadX,
        double start,
        double span,
        List<(long Frame, long TickMs)> samples,
        Color color)
    {
        PruneTrailSamplesByAge(Environment.TickCount64, samples);
        var wave = WaveformBounds(bounds);
        SeekPlaybackTrailPaint.Draw(
            dc,
            wave,
            playheadX,
            ScaleLeft(bounds),
            samples,
            frame => FrameToViewX(frame, start, span, bounds),
            TrailFadeMsForView(ScaleContentWidth(bounds)),
            color);
    }

    private void ResetTrailIfRewound(long frame, List<(long Frame, long TickMs)> samples)
    {
        if (!_trailActive || samples.Count == 0)
        {
            return;
        }

        if (frame < samples[^1].Frame)
        {
            samples.Clear();
        }
    }

    private void RecordTrailSample(long frame, List<(long Frame, long TickMs)> samples)
    {
        if (!_trailActive)
        {
            return;
        }

        ResetTrailIfRewound(frame, samples);
        var now = Environment.TickCount64;
        var durationSec = DurationSeconds();
        if (samples.Count > 0)
        {
            var last = samples[^1];
            if (FramesToSec(Math.Abs(frame - last.Frame)) >= DiscontinuitySec(durationSec))
            {
                samples.Clear();
            }
        }

        if (samples.Count > 0)
        {
            var last = samples[^1];
            var secDelta = FramesToSec(Math.Abs(frame - last.Frame));
            if (now - last.TickMs < TrailSampleMinIntervalMs && secDelta < TrailMinSecDelta)
            {
                return;
            }
        }

        samples.Add((frame, now));
        PruneTrailSamplesByAge(now, samples);
        if (samples.Count > TrailMaxSamples)
        {
            samples.RemoveRange(0, samples.Count - TrailMaxSamples);
            PruneTrailSamplesByAge(now, samples);
        }
    }

    private void PruneTrailSamplesByAge(long now, List<(long Frame, long TickMs)> samples)
    {
        var retainMs = Math.Max(TrailSampleRetainMs, TrailFadeMsForView(ContentWidth));
        var remove = 0;
        while (remove < samples.Count && now - samples[remove].TickMs >= retainMs)
        {
            remove++;
        }

        if (remove > 0)
        {
            samples.RemoveRange(0, remove);
        }
    }

    private double TrailFadeMsForView(double contentWidth)
    {
        var durationSec = DurationSeconds();
        var viewDurationSec = durationSec * (ViewSpanFrames / Math.Max(1d, _document?.FrameCount ?? 1));
        return SeekPlaybackTrailPaint.FadeMs(contentWidth, viewDurationSec);
    }

    private double DurationSeconds() =>
        _document is null || _document.SampleRate <= 0
            ? 0
            : _document.FrameCount / (double)_document.SampleRate;

    private double FramesToSec(long frames) =>
        _document is null || _document.SampleRate <= 0
            ? frames / 48000d
            : frames / (double)_document.SampleRate;

    private static double DiscontinuitySec(double durationSec) =>
        durationSec <= 0 ? TrailDiscontinuitySec : Math.Max(TrailDiscontinuitySec, durationSec * 0.025);

    private bool IsGuideOverSelection(double x, double start, double span, Rect bounds)
    {
        if (_document is null || _document.Selection.IsEmpty || span <= 0 || ScaleContentWidth(bounds) <= 0)
        {
            return false;
        }

        var x0 = FrameToViewX(_document.Selection.StartFrame, start, span, bounds);
        var x1 = FrameToViewX(_document.Selection.EndFrame, start, span, bounds);
        return x >= x0 && x < x1;
    }

    private void SyncMouseGuideHeight()
    {
        var top = Math.Min(ChromeTopHeight, Math.Max(0, ActualHeight));
        var margin = new Thickness(0, top, 0, 0);
        if (_mouseGuideBar.Margin != margin)
        {
            _mouseGuideBar.Margin = margin;
        }
    }

    private void ApplyMouseGuideOverlay()
    {
        ResolveMouseGuideX();
        if (_mouseGuideX is not double mx || _document is null)
        {
            if (_mouseGuideBar.Visibility != Visibility.Collapsed)
            {
                _mouseGuideBar.Visibility = Visibility.Collapsed;
            }

            _appliedGuideX = double.NaN;
            _guideOverSelection = false;
            _mouseGuideBar.Fill = _mouseGuideBrush;
            return;
        }

        var over = IsGuideOverSelection(mx, _viewStart, ViewSpanFrames, new Rect(0, 0, ActualWidth, ActualHeight));
        var moved = double.IsNaN(_appliedGuideX) || Math.Abs(_appliedGuideX - mx) >= MouseGuideMoveEpsilonPx;
        if (!moved
            && over == _guideOverSelection
            && _mouseGuideBar.Visibility == Visibility.Visible)
        {
            return;
        }

        _appliedGuideX = mx;
        _mouseGuideTransform.X = mx;
        _mouseGuideBar.Visibility = Visibility.Visible;
        if (over != _guideOverSelection)
        {
            _mouseGuideBar.Fill = over
                ? (_mouseGuideOnSelectionBrush ?? _mouseGuideBrush)
                : _mouseGuideBrush;
        }

        _guideOverSelection = over;
    }

    private void UpdateHoverCursor(Point pos)
    {
        if (_trailActive)
        {
            var now = Environment.TickCount64;
            if (now - _hoverCursorAt < 32)
            {
                return;
            }

            _hoverCursorAt = now;
        }

        Cursor next;
        if (TryHitChannelLabel(pos, out _))
        {
            next = Cursors.Hand;
        }
        else if (pos.X < ContentLeft
            || TryHitMarkerFlag(pos, out _)
            || TryHitRegionFlag(pos, out _, out _))
        {
            next = Cursors.Arrow;
        }
        else if (TryHitLoopBar(pos, out var loopPart))
        {
            next = loopPart == LoopBarPart.Body ? Cursors.SizeAll : Cursors.SizeWE;
        }
        else
        {
            next = Cursors.IBeam;
        }

        if (!ReferenceEquals(Cursor, next))
        {
            Cursor = next;
        }
    }

    private enum LayerKind
    {
        Static,
        Overlay,
        Playhead,
    }

    private sealed class DrawingHost : FrameworkElement
    {
        private readonly LayerKind _kind;

        public WaveformView? Owner { get; set; }

        public DrawingHost(LayerKind kind)
        {
            _kind = kind;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            IsHitTestVisible = false;
            var overlay = kind != LayerKind.Static;
            SnapsToDevicePixels = !overlay;
            UseLayoutRounding = !overlay;
            ClipToBounds = true;
            if (overlay)
            {
                RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            }
            else
            {
                RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
                RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            switch (_kind)
            {
                case LayerKind.Overlay:
                    Owner?.PaintOverlay(dc);
                    break;
                case LayerKind.Playhead:
                    Owner?.PaintPlayhead(dc);
                    break;
                default:
                    Owner?.PaintStatic(dc);
                    break;
            }
        }
    }
}
