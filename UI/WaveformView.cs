using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
    public const double TimeZoomStep = 1.09050773267;
    public const double TimeZoomMax = 81920d;
    public const double TimeZoomStepMax = 32d;
    public const double AmpZoomMax = 128d;
    public const double WheelTimeStep = 1.189207115;
    private const int PolylineMaxSamplesPerPixel = 1;
    private const int RawColumnMaxSamplesPerPixel = 96;
    private const int RawColumnMaxFrames = 1 << 18;
    public const double SamplePointMinZoom = 10000d;
    private const double SamplePointRadius = 8d / 3d;
    private static readonly double[] DbRequiredMarks = [-3, -6, -12];
    private static readonly double[] DbOptionalMarks = [-9, -18, -24, -36, -48, -60];
    private const double DbOptionalMinGapPx = 11;
    private static double DbScaleLaneWidth => DesignMetrics.DbScaleWidth;
    private const int DragThresholdPx = 3;
    private const float TrailTargetLengthPx = 360f;
    private const int TrailSampleRetainMs = 10400;
    private const float TrailPeakAlpha = 0.15f;
    private const float TrailPlayheadGapPx = 2f;
    private const double TrailMinSecDelta = 0.02;
    private const int TrailSampleMinIntervalMs = 24;
    private const int TrailMaxSamples = 900;
    private const double TrailDiscontinuitySec = 1.25;

    private const double MouseGuideMoveEpsilonPx = 0.5;
    private static double MarkerLaneHeight => DesignMetrics.MarkerLaneHeight;
    private static double TimeLaneHeight => DesignMetrics.RulerHeight;
    private static double ChromeTopHeight => MarkerLaneHeight + TimeLaneHeight;

    private readonly DrawingHost _staticHost;
    private readonly DrawingHost _overlayHost;
    private readonly Line _mouseGuideLine = new();
    private readonly TranslateTransform _mouseGuideTransform = new();
    private readonly List<(WaveMarker Marker, Rect Flag)> _markerFlags = [];
    private readonly HashSet<long> _selectedMarkerFrames = [];
    private bool _markerDragging;
    private bool _markerDragMoved;
    private long _markerDragPrimaryOrigin;
    private long? _markerDragFollowOrigin;
    private long _markerDragLastDelta = long.MinValue;
    private long[] _markerDragOrigins = [];
    private MarkerSnapshot[] _markerDragBefore = [];
    private bool _pendingSelectionPrerollJump;
    private TextBox? _commentEditor;
    private long _commentEditFrame = -1;
    private bool _endingCommentEdit;
    private readonly List<(long Frame, long TickMs)> _trailSamples = [];
    private bool _trailActive;

    private AudioDocument? _document;
    private WriteableBitmap? _waveBitmap;
    private WriteableBitmap? _invertBitmap;
    private int[] _invertPixels = [];
    private Size _waveDipSize;
    private double _waveDpiX;
    private double _waveDpiY;
    private bool _waveDirty = true;
    private bool _staticRebuildQueued;
    private bool _viewChangedQueued;
    private readonly Dictionary<(string Text, bool OnSampleLoop), FormattedText> _timeLabelCache = new();
    private readonly Dictionary<string, FormattedText> _dbLabelCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FormattedText> _markerLabelCache = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Text, bool Selected), FormattedText> _markerCommentLabelCache = new();
    private double _timeLabelPixelsPerDip;
    private float[] _columnMins = [];
    private float[] _columnMaxs = [];
    private int[] _columnYHi = [];
    private int[] _columnYLo = [];
    private int _waveBgra;
    private int _zeroBgra;
    private double _viewStart;
    private double _timeZoom = 1d;
    private double _ampZoom = 1d;
    private long _playheadFrame;
    private double? _mouseGuideX;
    private double _appliedGuideX = double.NaN;
    private bool _guideOverSelection;
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

    public event EventHandler<long>? CursorCommitted;
    public event EventHandler<long>? ScrubStarted;
    public event EventHandler<long>? ScrubPreviewed;
    public event EventHandler<(long Frame, bool Commit)>? ScrubEnded;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ViewChanged;
    public event EventHandler<(long Frame, string Comment)>? MarkerCommentCommitted;
    public event EventHandler<(MarkerSnapshot[] Before, MarkerSnapshot[] After)>? MarkerLayoutCommitted;
    public event EventHandler? MarkersChanged;

    public bool IsEditingMarkerComment =>
        _commentEditor is { Visibility: Visibility.Visible };

    public WaveformView()
    {
        ClipToBounds = true;
        Focusable = true;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        Cursor = Cursors.IBeam;
        Background = Brushes.Transparent;

        _staticHost = new DrawingHost(overlay: false) { Owner = this };
        _overlayHost = new DrawingHost(overlay: true) { Owner = this };
        Children.Add(_staticHost);
        Children.Add(_overlayHost);

        _mouseGuideBrush = WpfControlHelpers.FrozenBrush(Theme.Get("MouseGuideBrush"));
        _mouseGuideOnSelectionBrush = WpfControlHelpers.FrozenBrush(Theme.Get("MouseGuideOnSelectionBrush"));
        _mouseGuideLine.IsHitTestVisible = false;
        _mouseGuideLine.Stroke = _mouseGuideBrush;
        _mouseGuideLine.StrokeThickness = 1;
        _mouseGuideLine.SnapsToDevicePixels = false;
        _mouseGuideLine.UseLayoutRounding = false;
        _mouseGuideLine.Visibility = Visibility.Collapsed;
        _mouseGuideLine.X1 = 0;
        _mouseGuideLine.X2 = 0;
        _mouseGuideLine.Y1 = ChromeTopHeight;
        _mouseGuideLine.Y2 = ChromeTopHeight + 1;
        _mouseGuideLine.RenderTransform = _mouseGuideTransform;
        RenderOptions.SetEdgeMode(_mouseGuideLine, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(_mouseGuideLine, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(_overlayHost, 2);
        Panel.SetZIndex(_mouseGuideLine, 3);
        Children.Add(_mouseGuideLine);
        SizeChanged += (_, _) =>
        {
            EndMarkerCommentEdit(commit: true);
            _waveDirty = true;
            SyncMouseGuideHeight();
            InvalidateStaticLayer();
        };
    }

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
            _scrubbing = false;
            _selectedMarkerFrames.Clear();
            _keyboardSelectAnchor = null;
            InvalidateWaveform();
            RaiseViewChanged();
        }
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

            _playheadFrame = value;
            if (_trailActive)
            {
                RecordTrailSample(value);
            }

            InvalidatePlayheadLayer();
        }
    }

    public void SetTrailRecording(bool active)
    {
        _trailActive = active;
        if (!active)
        {
            _trailSamples.Clear();
            InvalidatePlayheadLayer();
            ApplyMouseGuideOverlay();
            return;
        }

        RecordTrailSample(_playheadFrame);
        ApplyMouseGuideOverlay();
    }

    public bool LoopEnabled { get; set; }

    public bool CenterLocked { get; private set; }

    public bool IsInteracting => _dragging || _markerDragging || _scrubbing;

    public bool IsScrubbing => _scrubbing;

    public IReadOnlyCollection<long> SelectedMarkerFrames => _selectedMarkerFrames;

    public bool HasSelectedMarkers => _selectedMarkerFrames.Count > 0;

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

    public void RefreshOverlay() => InvalidatePlayheadLayer();

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

    public void WheelTimeZoom(int delta, double anchorX) =>
        WheelTimeZoomAtFrame(delta, XToFrame(anchorX));

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
        && _timeZoom >= SamplePointMinZoom
        && ContentWidth > 0
        && ViewSpanFrames <= ContentWidth;

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

        _document.Selection = range;
        _keyboardSelectAnchor = range.IsEmpty ? null : range.StartFrame;
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
            CenterViewOnPlayhead();
            return;
        }

        var margin = Math.Max(1d, span * 0.08);
        var frame = (double)_playheadFrame;
        if (frame > _viewStart + span - margin)
        {
            SetViewStart(frame - span + margin);
            return;
        }

        if (frame < _viewStart)
        {
            SetViewStart(frame - margin);
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

    public bool ClearMarkerSelection()
    {
        if (_selectedMarkerFrames.Count == 0)
        {
            return false;
        }

        _selectedMarkerFrames.Clear();
        InvalidateStaticLayer();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SelectMarkerFrames(IEnumerable<long> frames, bool additive = false)
    {
        if (!additive)
        {
            _selectedMarkerFrames.Clear();
        }

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
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PruneMarkerSelection()
    {
        if (_document is null)
        {
            if (_selectedMarkerFrames.Count == 0)
            {
                return;
            }

            _selectedMarkerFrames.Clear();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_selectedMarkerFrames.RemoveWhere(frame => !_document.HasMarkerAt(frame)) > 0)
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

    public long FrameAt(double x) => ClampFrame(XToFrame(x));

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        var x = e.GetPosition(this).X;
        _mouseGuideX = x < ContentLeft ? null : x;
        ApplyMouseGuideOverlay();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_document is null)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            var pos = e.GetPosition(this);
            if (IsInDbScaleLane(pos))
            {
                e.Handled = true;
                return;
            }
            if (TryHitMarkerFlag(pos, out var marker))
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

        var start = e.GetPosition(this);
        if (IsInDbScaleLane(start))
        {
            e.Handled = true;
            return;
        }

        if (TryHitMarkerFlag(start, out var hit))
        {
            BeginMarkerFlagInteraction(hit, start);
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
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _anchorFrame = frame;
            _keyboardSelectAnchor = null;
            if (!_document.Selection.IsEmpty)
            {
                _document.Selection = WaveSelection.Empty;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        PreviewInteraction(frame, force: true);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        _mouseGuideX = pos.X < ContentLeft ? null : pos.X;
        ApplyMouseGuideOverlay();

        if (_scrubbing && _document is not null)
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
                ApplyMarkerDrag(FrameAt(pos.X) - _markerDragPrimaryOrigin);
            }

            return;
        }

        if (!_dragging)
        {
            Cursor = pos.X < ContentLeft || TryHitMarkerFlag(pos, out _)
                ? Cursors.Arrow
                : Cursors.IBeam;
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

        if (_scrubbing)
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

        if (_scrubbing)
        {
            FinishScrub(commit: true, FrameAt(e.GetPosition(this).X));
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!_dragging)
        {
            _mouseGuideX = null;
            ApplyMouseGuideOverlay();
        }
    }

    internal void PaintStatic(DrawingContext dc)
    {
        var bounds = new Rect(_staticHost.RenderSize);
        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("WaveformBackBrush")), null, bounds);
        var wave = WaveformBounds(bounds);
        var span = _document is null ? 0 : ViewSpanFrames;
        var start = _viewStart;
        DrawDbScaleWell(dc, bounds, wave);
        DrawMarkerLane(dc, bounds, start, span);
        DrawTimeLane(dc, bounds, start, span);
        if (_document is null || _document.FrameCount <= 0 || wave.Width <= 1 || wave.Height <= 1)
        {
            return;
        }

        MarkerRolePaint.DrawSampleLoop(dc, _document, wave, start, span, "SampleLoopWaveFillBrush");
        MarkerRolePaint.DrawBackgrounds(dc, _document, wave, start, span);
        EnsureWaveformBitmap(wave);
        if (_waveBitmap is not null)
        {
            dc.DrawImage(_waveBitmap, wave);
        }

        MarkerRolePaint.DrawRemoveOverlays(dc, _document, wave, start, span);

        var channels = Math.Max(1, _document.Channels);
        var laneGap = channels > 1 ? 4d : 0d;
        var laneHeight = (wave.Height - laneGap * (channels - 1)) / channels;
        DrawChannelLabels(dc, wave, channels, laneGap, laneHeight);
        DrawDbScaleTicks(dc, bounds, wave, channels, laneGap, laneHeight);
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

        DrawMarkers(dc, bounds, start, span);
        DrawPlayhead(dc, bounds, start, span);
    }

    private void EnsureWaveformBitmap(Rect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        if (!_waveDirty
            && _waveBitmap is not null
            && _waveDipSize == bounds.Size
            && Math.Abs(_waveDpiX - dpi.DpiScaleX) < 0.001
            && Math.Abs(_waveDpiY - dpi.DpiScaleY) < 0.001)
        {
            return;
        }

        RebuildWaveformBitmap(bounds, dpi);
        _waveDipSize = bounds.Size;
        _waveDpiX = dpi.DpiScaleX;
        _waveDpiY = dpi.DpiScaleY;
        _waveDirty = false;
    }

    private void RebuildWaveformBitmap(Rect bounds, DpiScale dpi)
    {
        var document = _document!;
        var scaleX = Math.Max(1e-6, dpi.DpiScaleX);
        var scaleY = Math.Max(1e-6, dpi.DpiScaleY);
        var width = Math.Max(1, (int)Math.Round(bounds.Width * scaleX));
        var height = Math.Max(1, (int)Math.Round(bounds.Height * scaleY));
        EnsureWaveBitmap(width, height, dpi);
        EnsureWavePens();

        var bitmap = _waveBitmap!;
        bitmap.Lock();
        try
        {
            unsafe
            {
                var buffer = (int*)bitmap.BackBuffer;
                var stride = bitmap.BackBufferStride / 4;
                new Span<int>(buffer, stride * height).Clear();
                RasterizeWaveform(buffer, stride, width, height, scaleX, scaleY, document);
            }

            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            bitmap.Unlock();
        }

        RebuildInvertBitmap(width, height, dpi);
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
        WaveformInvertPaint.RebuildInPlace(_invertPixels, width, height, _document, _viewStart, ViewSpanFrames);
        _invertBitmap.WritePixels(new Int32Rect(0, 0, width, height), _invertPixels, width * 4, 0);
    }

    private void DrawInvertedSelection(
        DrawingContext dc,
        Rect bounds,
        double start,
        double span,
        WaveSelection selection)
    {
        if (_invertBitmap is null)
        {
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
        dc.PushClip(new RectangleGeometry(new Rect(x0, wave.Y, width, wave.Height)));
        dc.DrawImage(_invertBitmap, wave);
        dc.Pop();
    }

    private unsafe void RasterizeWaveform(
        int* buffer,
        int stride,
        int width,
        int height,
        double scaleX,
        double scaleY,
        AudioDocument document)
    {
        var channels = Math.Max(1, document.Channels);
        var laneGap = channels > 1 ? 4d * scaleY : 0d;
        var laneHeight = (height - laneGap * (channels - 1)) / channels;
        var start = _viewStart;
        var span = ViewSpanFrames;
        var startFrame = Math.Clamp((long)Math.Floor(start), 0, document.FrameCount);
        var endFrame = Math.Clamp((long)Math.Ceiling(start + span), startFrame, document.FrameCount);
        var rangeFrames = endFrame - startFrame;
        if (rangeFrames <= 0 || laneHeight < 1)
        {
            return;
        }

        var usePolyline = IsPolylineZoom(rangeFrames, width);
        var useRawColumns = !usePolyline && rangeFrames <= RawColumnBudget(width);
        if (!usePolyline)
        {
            EnsureColumnBuffers(width * channels);
            var count = useRawColumns
                ? FillRawColumnPeaks(document, startFrame, endFrame, width, channels)
                : document.Peaks.ReadRangePacked(startFrame, endFrame, width, _columnMins, _columnMaxs);
            if (count <= 0)
            {
                return;
            }

            for (var ch = 0; ch < channels; ch++)
            {
                var top = ch * (laneHeight + laneGap);
                var mid = top + laneHeight * 0.5;
                if (!TryLaneClip(top, laneHeight, height, out var clipTop, out var clipBottom))
                {
                    continue;
                }

                DrawLaneGuides(buffer, stride, width, height, top, mid);
                RasterPeakEnvelope(
                    buffer,
                    stride,
                    width,
                    clipTop,
                    clipBottom,
                    ch,
                    channels,
                    count,
                    top,
                    laneHeight,
                    mid,
                    connectNeighbors: rangeFrames <= (long)width * 8);
            }

            return;
        }

        for (var ch = 0; ch < channels; ch++)
        {
            var top = ch * (laneHeight + laneGap);
            var mid = top + laneHeight * 0.5;
            if (!TryLaneClip(top, laneHeight, height, out var clipTop, out var clipBottom))
            {
                continue;
            }

            DrawLaneGuides(buffer, stride, width, height, top, mid);
            RasterSamplePolyline(
                buffer,
                stride,
                width,
                clipTop,
                clipBottom,
                document,
                ch,
                start,
                span,
                top,
                laneHeight,
                mid,
                scaleX);
        }
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

    private unsafe void DrawLaneGuides(int* buffer, int stride, int width, int height, double top, double mid)
    {
        var yMid = (int)Math.Round(mid);
        if ((uint)yMid < (uint)height)
        {
            FillHLine(buffer, stride, width, yMid, _zeroBgra);
        }

        var yTop = (int)Math.Round(top);
        if (yTop > 0 && (uint)yTop < (uint)height)
        {
            FillHLine(buffer, stride, width, yTop, _zeroBgra);
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
        double top,
        double laneHeight,
        double mid,
        bool connectNeighbors)
    {
        EnsureColumnEdges(width);
        var amp = laneHeight * 0.5 * _ampZoom;
        var bottom = top + laneHeight;
        var color = _waveBgra;
        for (var px = 0; px < width; px++)
        {
            var bucket = count == width
                ? px
                : (int)Math.Clamp((long)px * count / width, 0, count - 1);
            var index = bucket * channels + channel;
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

        for (var px = 0; px < width; px++)
        {
            var hi = _columnYHi[px];
            var lo = _columnYLo[px];
            if (connectNeighbors && px + 1 < width)
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
                for (var ch = 0; ch < channels; ch++)
                {
                    var sample = samples[src + ch];
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
        double laneHeight,
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
        var amp = laneHeight * 0.5 * _ampZoom;
        int SampleY(float sample)
        {
            var y = (int)Math.Round(mid - sample * amp);
            return Math.Clamp(y, clipTop, clipBottom - 1);
        }

        float SampleAt(int index) => samples[(first + index) * channels + channel];
        var collectDots = ShouldDrawSamplePoints(count, width / scaleX);
        var dotRadius = Math.Max(1, (int)Math.Round(SamplePointRadius * scaleX));

        if (count == 1)
        {
            var x = (int)Math.Round(FrameToX(first, start, span, width));
            var y = SampleY(SampleAt(0));
            FillVLine(buffer, stride, width, clipTop, clipBottom, x, y - 3, y + 3, _waveBgra);
            if (collectDots)
            {
                FillDot(buffer, stride, width, clipTop, clipBottom, x, y, dotRadius, _waveBgra);
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
                DrawThickLine(buffer, stride, width, clipTop, clipBottom, prevX, prevY, x, y, _waveBgra);
            }

            prevX = x;
            prevY = y;
            havePrev = true;
            if (collectDots)
            {
                FillDot(buffer, stride, width, clipTop, clipBottom, x, y, dotRadius, _waveBgra);
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

    private static unsafe void FillHLine(int* buffer, int stride, int width, int y, int color)
    {
        var p = buffer + y * stride;
        for (var x = 0; x < width; x++)
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
        int channels,
        double laneGap,
        double laneHeight)
    {
        if (laneHeight < 10)
        {
            return;
        }

        var fore = WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush"));
        var backColor = Theme.Get("WaveformBackBrush");
        var back = WpfControlHelpers.FrozenBrush(Color.FromArgb(180, backColor.R, backColor.G, backColor.B));
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double padX = 4;
        const double left = 6;
        for (var ch = 0; ch < channels; ch++)
        {
            var top = bounds.Y + ch * (laneHeight + laneGap);
            var text = new FormattedText(
                ChannelLabels.Name(ch, channels),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                WpfControlHelpers.MonoTypeface,
                11,
                fore,
                pixelsPerDip);
            var boxH = text.Height + 2;
            var y = top + Math.Max(0, (laneHeight - boxH) * 0.5);
            var box = new Rect(left, y, text.Width + padX * 2, boxH);
            if (box.Bottom > top + laneHeight)
            {
                box.Y = top + Math.Max(0, (laneHeight - boxH) * 0.5);
                y = box.Y;
            }

            dc.PushClip(new RectangleGeometry(new Rect(0, top, bounds.Width, laneHeight)));
            dc.DrawRoundedRectangle(back, null, box, 2, 2);
            dc.DrawText(text, new Point(left + padX, y + Math.Max(0, (box.Height - text.Height) * 0.5)));
            dc.Pop();
        }
    }

    private void DrawDbScaleWell(DrawingContext dc, Rect bounds, Rect wave)
    {
        var well = DbScaleBounds(bounds);
        if (well.Width <= 1)
        {
            return;
        }

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TimelineWellBackBrush")), null, well);
        var edge = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("ChromeBorderBrush")), 1);
        edge.Freeze();
        dc.DrawLine(edge, new Point(well.Right - 0.5, 0), new Point(well.Right - 0.5, bounds.Height));

        if (wave.Y > 8 && well.Width > 12)
        {
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var muted = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
            var header = GetDbLabel("dB", pixelsPerDip, muted);
            var hx = well.X + Math.Max(2, (well.Width - header.Width) * 0.5);
            var hy = Math.Max(2, (wave.Y - header.Height) * 0.5);
            dc.DrawText(header, new Point(hx, hy));
        }
    }

    private void DrawDbScaleTicks(
        DrawingContext dc,
        Rect bounds,
        Rect wave,
        int channels,
        double laneGap,
        double laneHeight)
    {
        var well = DbScaleBounds(bounds);
        if (well.Width <= 8 || laneHeight < 8)
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var muted = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        var tick = new Pen(muted, 1);
        tick.Freeze();
        var half = laneHeight * 0.5;
        var amp = half * _ampZoom;

        for (var ch = 0; ch < channels; ch++)
        {
            var top = wave.Y + ch * (laneHeight + laneGap);
            var mid = top + half;
            var bottom = top + laneHeight;
            dc.PushClip(new RectangleGeometry(new Rect(well.X, top, well.Width, laneHeight)));
            DrawDbTick(dc, well, mid, "∞", pixelsPerDip, muted, tick);

            var drawn = new List<double> { mid };
            var values = 1;
            foreach (var db in DbRequiredMarks)
            {
                if (TryDrawDbValue(dc, well, db, mid, amp, top, bottom, pixelsPerDip, muted, tick, drawn, minGap: 0))
                {
                    values++;
                }
            }

            foreach (var db in DbOptionalMarks)
            {
                if (TryDrawDbValue(dc, well, db, mid, amp, top, bottom, pixelsPerDip, muted, tick, drawn, DbOptionalMinGapPx))
                {
                    values++;
                }
            }

            if (values < 4)
            {
                foreach (var db in DbOptionalMarks)
                {
                    if (TryDrawDbValue(dc, well, db, mid, amp, top, bottom, pixelsPerDip, muted, tick, drawn, minGap: 0)
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
        Pen tick,
        List<double> drawn,
        double minGap)
    {
        var dy = Math.Pow(10, db / 20d) * amp;
        if (dy < 3)
        {
            return false;
        }

        var label = db.ToString("0", CultureInfo.InvariantCulture);
        var up = TryDrawDbTickAt(dc, well, mid - dy, label, top, bottom, pixelsPerDip, fore, tick, drawn, minGap);
        var down = TryDrawDbTickAt(dc, well, mid + dy, label, top, bottom, pixelsPerDip, fore, tick, drawn, minGap);
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
        Pen tick,
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

        DrawDbTick(dc, well, y, label, pixelsPerDip, fore, tick);
        drawn.Add(y);
        return true;
    }

    private void DrawDbTick(
        DrawingContext dc,
        Rect well,
        double y,
        string label,
        double pixelsPerDip,
        Brush fore,
        Pen tick)
    {
        var text = GetDbLabel(label, pixelsPerDip, fore);
        var ty = y - text.Height * 0.5;
        var tx = well.Right - 7 - text.Width;
        dc.DrawLine(tick, new Point(well.Right - 5, y), new Point(well.Right - 1, y));
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
        && count <= width
        && _timeZoom >= SamplePointMinZoom;

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
        if (_waveBgra != 0)
        {
            return;
        }

        _waveBgra = ToBgra(Theme.Get("WaveFillBrush"));
        _zeroBgra = ToBgra(Theme.Get("WaveZeroLineBrush"));
    }

    private static int ToBgra(Color color) =>
        color.B | (color.G << 8) | (color.R << 16) | (color.A << 24);

    private double ContentLeft => ScaleLeft(new Rect(0, 0, ActualWidth, ActualHeight));

    private double ContentWidth => Math.Max(0, ActualWidth - ContentLeft);

    private bool IsInDbScaleLane(Point point) =>
        point.X < ContentLeft && point.Y >= ChromeTopHeight;

    private static double ScaleLeft(Rect bounds) =>
        Math.Min(DbScaleLaneWidth, Math.Max(0, bounds.Width));

    private static double ScaleContentWidth(Rect bounds) =>
        Math.Max(0, bounds.Width - ScaleLeft(bounds));

    private static Rect DbScaleBounds(Rect bounds) =>
        new(0, 0, ScaleLeft(bounds), bounds.Height);

    private static Rect WaveformBounds(Rect bounds)
    {
        var top = Math.Min(ChromeTopHeight, Math.Max(0, bounds.Height));
        var left = ScaleLeft(bounds);
        return new Rect(left, top, ScaleContentWidth(bounds), Math.Max(0, bounds.Height - top));
    }

    private static Rect MarkerLaneBounds(Rect bounds) =>
        new(ScaleLeft(bounds), 0, ScaleContentWidth(bounds), Math.Min(MarkerLaneHeight, bounds.Height));

    private static Rect TimeLaneBounds(Rect bounds)
    {
        var top = Math.Min(MarkerLaneHeight, bounds.Height);
        var height = Math.Min(TimeLaneHeight, Math.Max(0, bounds.Height - top));
        return new Rect(ScaleLeft(bounds), top, ScaleContentWidth(bounds), height);
    }

    private static double FrameToViewX(double frame, double start, double span, Rect bounds) =>
        ScaleLeft(bounds) + FrameToX(frame, start, span, ScaleContentWidth(bounds));

    private void DrawMarkerLane(DrawingContext dc, Rect bounds, double start, double span)
    {
        var lane = MarkerLaneBounds(bounds);
        if (lane.Height <= 1)
        {
            return;
        }

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TimelineWellBackBrush")), null, lane);
        var edge = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("ChromeBorderBrush")), 1);
        edge.Freeze();
        dc.DrawLine(edge, new Point(lane.X, lane.Bottom - 0.5), new Point(lane.Right, lane.Bottom - 0.5));
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
            MarkerRolePaint.DrawSampleLoop(dc, _document, lane, start, span, "SampleLoopTimelineBrush");
        }

        var edge = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("ChromeBorderBrush")), 1);
        edge.Freeze();
        dc.DrawLine(edge, new Point(lane.X, lane.Bottom - 0.5), new Point(lane.Right, lane.Bottom - 0.5));
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
        var tick = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush")), 1);
        tick.Freeze();
        var lastRight = double.NegativeInfinity;
        const double minGap = 72;
        for (var t = first; t <= startSec + seconds + step; t += step)
        {
            if (t < -1e-9)
            {
                continue;
            }

            var x = FrameToViewX(t * _document.SampleRate, start, span, bounds);
            if (x - lastRight < minGap)
            {
                continue;
            }

            var frame = (long)Math.Round(t * _document.SampleRate);
            var onLoop = hasLoop && frame >= loop.StartFrame && frame < loop.EndFrame;
            var text = GetTimeLabel(UiStrings.FormatDuration(Math.Max(0, t)), pixelsPerDip, onLoop);
            var textY = lane.Y + Math.Max(1, (lane.Height - text.Height) * 0.5);
            dc.DrawLine(tick, new Point(x, lane.Bottom - 5), new Point(x, lane.Bottom - 1));
            dc.DrawText(text, new Point(x + 3, textY));
            lastRight = x + 3 + text.Width;
        }
    }

    private void DrawMarkers(DrawingContext dc, Rect bounds, double start, double span)
    {
        if (_document is null || _document.Markers.Count == 0)
        {
            return;
        }

        var color = Theme.Get("MarkerBrush");
        var selectedColor = Theme.Get("MarkerSelectedBrush");
        var selectedBorder = Theme.Get("MarkerSelectedBorderBrush");
        var line = new Pen(WpfControlHelpers.FrozenBrush(color), 1);
        line.Freeze();
        var selectedLine = new Pen(WpfControlHelpers.FrozenBrush(selectedBorder), 1.5);
        selectedLine.Freeze();
        var fill = WpfControlHelpers.FrozenBrush(color);
        var selectedFill = WpfControlHelpers.FrozenBrush(selectedColor);
        var selectedPen = new Pen(WpfControlHelpers.FrozenBrush(selectedBorder), 1);
        selectedPen.Freeze();
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
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
            dc.DrawLine(selected ? selectedLine : line, new Point(x, 0), new Point(x, bounds.Height));
            if (lane.Height <= 2)
            {
                continue;
            }

            var idText = GetMarkerLabel(marker.Id.ToString(CultureInfo.InvariantCulture), pixelsPerDip);
            const double padX = 3;
            const double maxFlagWidth = 48;
            var boxH = Math.Min(Math.Max(idText.Height + 2, lane.Height - 2), lane.Height - 1);
            var boxW = Math.Min(maxFlagWidth, idText.Width + padX * 2);
            var box = new Rect(x, 1, boxW, boxH);
            _markerFlags.Add((marker, box));
            if (editing && marker.Frame == _commentEditFrame)
            {
                continue;
            }

            dc.DrawRectangle(selected ? selectedFill : fill, selected ? selectedPen : null, box);
            dc.DrawText(
                idText,
                new Point(x + padX, box.Y + Math.Max(0, (box.Height - idText.Height) * 0.5)));

            if (!string.IsNullOrEmpty(marker.Comment))
            {
                var commentText = GetMarkerCommentLabel(TruncateMarkerComment(marker.Comment), pixelsPerDip, selected);
                const double commentGap = 2;
                var commentX = box.Right + commentGap;
                if (commentX + commentText.Width <= bounds.Width)
                {
                    dc.DrawText(
                        commentText,
                        new Point(
                            commentX,
                            box.Y + Math.Max(0, (box.Height - commentText.Height) * 0.5)));
                }
            }
        }
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

        if (frame is not long target)
        {
            return false;
        }

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

        return false;
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
        if (_document is null)
        {
            return;
        }

        frame = ClampFrame(frame);
        PreviewInteraction(frame, force: true);
        ScrubEnded?.Invoke(this, (frame, commit));
    }

    private void BeginMarkerFlagInteraction(WaveMarker marker, Point start)
    {
        EndMarkerCommentEdit(commit: true);
        var additive = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (additive)
        {
            if (!_selectedMarkerFrames.Add(marker.Frame))
            {
                _selectedMarkerFrames.Remove(marker.Frame);
            }

            InvalidateStaticLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!_selectedMarkerFrames.Contains(marker.Frame))
        {
            _selectedMarkerFrames.Clear();
            _selectedMarkerFrames.Add(marker.Frame);
            InvalidateStaticLayer();
            MarkersChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_document is null || _selectedMarkerFrames.Count == 0)
        {
            return;
        }

        _dragStart = start;
        _markerDragging = true;
        _markerDragMoved = false;
        _markerDragLastDelta = long.MinValue;
        _markerDragPrimaryOrigin = marker.Frame;
        _markerDragOrigins = _selectedMarkerFrames.OrderBy(frame => frame).ToArray();
        _markerDragBefore = _document.SnapshotMarkers();
        _markerDragFollowOrigin = _document.HasMarkerAt(_playheadFrame) && _selectedMarkerFrames.Contains(_playheadFrame)
            ? _playheadFrame
            : null;
        CaptureMouse();
        Cursor = Cursors.Arrow;
    }

    private void ApplyMarkerDrag(long desiredDelta)
    {
        if (_document is null || !_markerDragging || desiredDelta == _markerDragLastDelta)
        {
            return;
        }

        _document.ReplaceMarkers(_markerDragBefore, markDirty: false);
        if (!_document.TryMoveMarkers(_markerDragOrigins, desiredDelta, out var applied, markDirty: false)
            && desiredDelta != 0)
        {
            applied = 0;
        }

        _markerDragLastDelta = desiredDelta;
        RemapSelectedMarkerFrames(_markerDragOrigins, applied);
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

    private void FinishMarkerDrag(bool commit)
    {
        if (!_markerDragging)
        {
            return;
        }

        var before = _markerDragBefore;
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
            _document.ReplaceMarkers(before, markDirty: false);
            _selectedMarkerFrames.Clear();
            foreach (var frame in _markerDragOrigins)
            {
                _selectedMarkerFrames.Add(frame);
            }

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

        var after = _document.SnapshotMarkers();
        ResetMarkerDragState();
        InvalidateStaticLayer();
        InvalidatePlayheadLayer();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
        if (!before.AsSpan().SequenceEqual(after))
        {
            MarkerLayoutCommitted?.Invoke(this, (before, after));
            if (follow is not null)
            {
                CursorCommitted?.Invoke(this, _playheadFrame);
            }
        }
    }

    private void RemapSelectedMarkerFrames(IReadOnlyList<long> origins, long appliedDelta)
    {
        _selectedMarkerFrames.Clear();
        foreach (var frame in origins)
        {
            _selectedMarkerFrames.Add(frame + appliedDelta);
        }
    }

    private void ResetMarkerDragState()
    {
        _markerDragging = false;
        _markerDragMoved = false;
        _markerDragPrimaryOrigin = 0;
        _markerDragFollowOrigin = null;
        _markerDragLastDelta = long.MinValue;
        _markerDragOrigins = [];
        _markerDragBefore = [];
    }

    private void BeginMarkerCommentEdit(WaveMarker marker)
    {
        _commentEditor ??= CreateMarkerCommentEditor();
        _commentEditFrame = marker.Frame;
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
            flag = new Rect(x, 1, 24, Math.Max(16, MarkerLaneHeight - 2));
        }

        const double commentGap = 2;
        var editorX = flag.Right + commentGap;
        _commentEditor.Width = Math.Clamp(160, 80, Math.Max(80, ActualWidth - editorX));
        _commentEditor.Height = Math.Max(flag.Height, MarkerLaneHeight - 2);
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
            var text = _commentEditor.Text;
            _commentEditor.Visibility = Visibility.Collapsed;
            _commentEditFrame = -1;
            if (commit && frame >= 0)
            {
                MarkerCommentCommitted?.Invoke(this, (frame, text.Trim()));
            }

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

    private FormattedText GetTimeLabel(string text, double pixelsPerDip, bool onSampleLoop = false)
    {
        var key = (text, onSampleLoop);
        if (_timeLabelCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            10,
            WpfControlHelpers.FrozenBrush(Theme.Get(onSampleLoop ? "SampleLoopTimeLabelForeBrush" : "MutedForeBrush")),
            pixelsPerDip);
        _timeLabelCache[key] = formatted;
        return formatted;
    }

    private static void DrawRange(
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
        dc.DrawRectangle(
            WpfControlHelpers.FrozenBrush(color),
            null,
            new Rect(x0, wave.Y, Math.Max(1, x1 - x0), wave.Height));
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

    private void SetViewStart(double start)
    {
        if (_document is null)
        {
            if (_viewStart == 0)
            {
                return;
            }

            _viewStart = 0;
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

        EndMarkerCommentEdit(commit: true);
        _viewStart = next;
        InvalidateStaticLayer();
        ApplyMouseGuideOverlay();
        RaiseViewChanged();
    }

    public void SetViewStartExternal(double start) => SetViewStart(start);

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

        _document.CursorFrame = frame;
        _playheadFrame = frame;
        InvalidatePlayheadLayer();
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

    private void InvalidateWaveform() => InvalidateStaticLayer();

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
        _waveDirty = true;
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
        _staticHost.InvalidateVisual();
        _overlayHost.InvalidateVisual();
    }

    private void InvalidatePlayheadLayer()
    {
        _overlayHost.InvalidateVisual();
        ApplyMouseGuideOverlay();
    }

    private void DrawPlayhead(DrawingContext dc, Rect bounds, double start, double span)
    {
        var playX = FrameToViewX(_playheadFrame, start, span, bounds);
        DrawSeekPlaybackTrail(dc, bounds, playX, start, span);
        EnsurePlayheadPens();
        var wave = WaveformBounds(bounds);
        if (wave.Height <= 1)
        {
            return;
        }

        var y0 = wave.Y;
        var y1 = wave.Y + wave.Height;
        dc.DrawLine(_playheadGlowOuter, new Point(playX, y0), new Point(playX, y1));
        dc.DrawLine(_playheadGlowInner, new Point(playX, y0), new Point(playX, y1));
        dc.DrawLine(_playheadCore, new Point(playX, y0), new Point(playX, y1));
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

    private void DrawSeekPlaybackTrail(DrawingContext dc, Rect bounds, double playheadX, double start, double span)
    {
        var now = Environment.TickCount64;
        PruneTrailSamplesByAge(now);
        if (_trailSamples.Count < 2 || bounds.Width <= 0)
        {
            return;
        }

        var contentLeft = ScaleLeft(bounds);
        var trailRightX = playheadX - TrailPlayheadGapPx;
        var trailLeftLimit = playheadX - TrailTargetLengthPx;
        if (trailRightX <= contentLeft || trailRightX <= trailLeftLimit)
        {
            return;
        }

        var fadeMs = TrailFadeMsForView(ScaleContentWidth(bounds));
        double? coveredLeft = null;
        foreach (var sample in _trailSamples)
        {
            if (now - sample.TickMs >= fadeMs)
            {
                continue;
            }

            var x = FrameToViewX(sample.Frame, start, span, bounds);
            if (x > trailRightX)
            {
                continue;
            }

            coveredLeft = coveredLeft is double left ? Math.Min(left, x) : x;
        }

        if (coveredLeft is null)
        {
            return;
        }

        var drawLeft = Math.Max(contentLeft, Math.Max(trailLeftLimit, coveredLeft.Value));
        var drawRight = Math.Min(bounds.Width, trailRightX);
        var drawW = drawRight - drawLeft;
        if (drawW < 1)
        {
            return;
        }

        var color = Theme.Get("PlayheadBrush");
        var peak = Color.FromArgb(ToByteAlpha(TrailPeakAlpha), color.R, color.G, color.B);
        var mid = Color.FromArgb(ToByteAlpha(TrailPeakAlpha * 0.25f), color.R, color.G, color.B);
        var soft = Color.FromArgb(ToByteAlpha(TrailPeakAlpha * 0.06f), color.R, color.G, color.B);
        var clear = Color.FromArgb(0, color.R, color.G, color.B);
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(playheadX - TrailTargetLengthPx, 0),
            EndPoint = new Point(playheadX, 0),
            GradientStops =
            [
                new GradientStop(clear, 0),
                new GradientStop(clear, 0.14),
                new GradientStop(soft, 0.42),
                new GradientStop(mid, 0.72),
                new GradientStop(peak, 1),
            ],
        };
        brush.Freeze();
        var wave = WaveformBounds(bounds);
        dc.DrawRectangle(brush, null, new Rect(drawLeft, wave.Y, drawW, wave.Height));
    }

    private void RecordTrailSample(long frame)
    {
        if (!_trailActive)
        {
            return;
        }

        var now = Environment.TickCount64;
        var durationSec = DurationSeconds();
        if (_trailSamples.Count > 0)
        {
            var last = _trailSamples[^1];
            var secDelta = FramesToSec(Math.Abs(frame - last.Frame));
            if (secDelta >= DiscontinuitySec(durationSec))
            {
                _trailSamples.Clear();
            }
        }

        if (_trailSamples.Count > 0)
        {
            var last = _trailSamples[^1];
            var secDelta = FramesToSec(Math.Abs(frame - last.Frame));
            if (now - last.TickMs < TrailSampleMinIntervalMs && secDelta < TrailMinSecDelta)
            {
                return;
            }
        }

        _trailSamples.Add((frame, now));
        PruneTrailSamplesByAge(now);
        if (_trailSamples.Count > TrailMaxSamples)
        {
            _trailSamples.RemoveRange(0, _trailSamples.Count - TrailMaxSamples);
            PruneTrailSamplesByAge(now);
        }
    }

    private void PruneTrailSamplesByAge(long now)
    {
        var retainMs = Math.Max(TrailSampleRetainMs, TrailFadeMsForView(ContentWidth));
        var remove = 0;
        while (remove < _trailSamples.Count && now - _trailSamples[remove].TickMs >= retainMs)
        {
            remove++;
        }

        if (remove > 0)
        {
            _trailSamples.RemoveRange(0, remove);
        }
    }

    private double TrailFadeMsForView(double contentWidth)
    {
        var durationSec = DurationSeconds();
        if (durationSec <= 0 || contentWidth <= 1)
        {
            return TrailSampleRetainMs;
        }

        var viewDurationSec = durationSec * (ViewSpanFrames / Math.Max(1d, _document?.FrameCount ?? 1));
        var fadeSec = TrailTargetLengthPx / contentWidth * viewDurationSec;
        fadeSec = Math.Clamp(fadeSec, 0.2, 60.0);
        return fadeSec * 1000.0;
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

    private static byte ToByteAlpha(float a) =>
        (byte)Math.Clamp((int)MathF.Round(a * 255f), 0, 255);

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
        if (Math.Abs(_mouseGuideLine.Y1 - top) > 0.01)
        {
            _mouseGuideLine.Y1 = top;
        }

        if (Math.Abs(_mouseGuideLine.Y2 - ActualHeight) > 0.01)
        {
            _mouseGuideLine.Y2 = ActualHeight;
        }
    }

    private void ApplyMouseGuideOverlay()
    {
        if (_mouseGuideX is not double mx || _document is null)
        {
            if (_mouseGuideLine.Visibility != Visibility.Collapsed)
            {
                _mouseGuideLine.Visibility = Visibility.Collapsed;
            }

            _appliedGuideX = double.NaN;
            _guideOverSelection = false;
            _mouseGuideLine.Stroke = _mouseGuideBrush;
            return;
        }

        SyncMouseGuideHeight();
        var over = IsGuideOverSelection(mx, _viewStart, ViewSpanFrames, new Rect(0, 0, ActualWidth, ActualHeight));
        var moved = double.IsNaN(_appliedGuideX) || Math.Abs(_appliedGuideX - mx) >= MouseGuideMoveEpsilonPx;
        if (!moved
            && over == _guideOverSelection
            && _mouseGuideLine.Visibility == Visibility.Visible)
        {
            return;
        }

        _appliedGuideX = mx;
        _mouseGuideTransform.X = mx;
        _mouseGuideLine.Visibility = Visibility.Visible;
        if (over != _guideOverSelection)
        {
            _mouseGuideLine.Stroke = over
                ? (_mouseGuideOnSelectionBrush ?? _mouseGuideBrush)
                : _mouseGuideBrush;
        }

        _guideOverSelection = over;
    }

    private sealed class DrawingHost : FrameworkElement
    {
        private readonly bool _overlay;

        public WaveformView? Owner { get; set; }

        public DrawingHost(bool overlay)
        {
            _overlay = overlay;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            IsHitTestVisible = false;
            SnapsToDevicePixels = !overlay;
            UseLayoutRounding = !overlay;
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
            if (_overlay)
            {
                Owner?.PaintOverlay(dc);
            }
            else
            {
                Owner?.PaintStatic(dc);
            }
        }
    }
}
