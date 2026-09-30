using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイリスト「波形表示」列。細長い縦棒を横に並べるだけ。曲ごとの見た目は揃える。
/// 幅は S / M / L（S 基準の 1 / 2 / 3 倍）。処理の間引き方針は共通。
/// </summary>
internal static class LibraryPlaylistWaveform
{
    public const double BarThickness = 1;
    public const double BarGap = 1;

    private static LibraryPlaylistWaveformSize _effectiveSize = LibraryPlaylistWaveformSize.L;

    private static readonly ConcurrentDictionary<string, float[]> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static event Action? EffectiveSizeChanged;

    public static event Action<string>? BarsUpdated;

    public static int BarCount => LibraryPlaylistWaveformSizes.BarCount(_effectiveSize);

    public static double ContentWidth => (BarCount * (BarThickness + BarGap)) - BarGap;

    public static double ColumnWidth =>
        ContentWidth + (LibraryBrowserView.LibraryColumnCellPadX * 2);

    public static void SetEffectiveSize(LibraryPlaylistWaveformSize size)
    {
        size = size is LibraryPlaylistWaveformSize.S or LibraryPlaylistWaveformSize.M
            ? size
            : LibraryPlaylistWaveformSize.L;
        if (_effectiveSize == size)
        {
            return;
        }

        _effectiveSize = size;
        EffectiveSizeChanged?.Invoke();
    }

    public static bool TryGet(string path, int barCount, out float[] bars) =>
        Cache.TryGetValue(CacheKey(path, barCount), out bars!);

    public static bool HasEntry(string path, int barCount) =>
        Cache.ContainsKey(CacheKey(path, barCount));

    public static void Set(string path, int barCount, float[] bars)
    {
        Cache[CacheKey(path, barCount)] = bars;
        BarsUpdated?.Invoke(path);
    }

    public static float[] BuildBars(
        string path,
        PeakPyramid? peaks,
        int barCount,
        CancellationToken cancellationToken)
    {
        if (peaks is { IsEmpty: false } ready)
        {
            return PeakPyramid.BuildPlaylistBarsFromPeaks(ready, barCount);
        }

        return PeakPyramid.BuildPlaylistBarsFromPath(path, barCount, cancellationToken);
    }

    private static string CacheKey(string path, int barCount) => path + "\u0001" + barCount;
}

/// <summary>プレイリスト行の粗い波形セル。データが来るまで空。</summary>
internal sealed class LibraryPlaylistWaveformCell : FrameworkElement
{
    private string? _path;
    private float[]? _bars;

    public LibraryPlaylistWaveformCell()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        MinHeight = 16;
        DataContextChanged += (_, _) => BindRow();
        Loaded += (_, _) =>
        {
            LibraryPlaylistWaveform.BarsUpdated += OnBarsUpdated;
            LibraryPlaylistWaveform.EffectiveSizeChanged += OnEffectiveSizeChanged;
            BindRow();
        };
        Unloaded += (_, _) =>
        {
            LibraryPlaylistWaveform.BarsUpdated -= OnBarsUpdated;
            LibraryPlaylistWaveform.EffectiveSizeChanged -= OnEffectiveSizeChanged;
        };
    }

    private void OnEffectiveSizeChanged()
    {
        if (!TryInvokeOnUi(OnEffectiveSizeChangedCore))
        {
            return;
        }
    }

    private void OnEffectiveSizeChangedCore() => BindRow();

    private void OnBarsUpdated(string path)
    {
        if (!TryInvokeOnUi(() => OnBarsUpdatedCore(path)))
        {
            return;
        }
    }

    private void OnBarsUpdatedCore(string path)
    {
        if (!string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (LibraryPlaylistWaveform.TryGet(path, LibraryPlaylistWaveform.BarCount, out var bars))
        {
            _bars = bars;
            InvalidateVisual();
        }
    }

    private bool TryInvokeOnUi(Action action)
    {
        Dispatcher dispatcher;
        try
        {
            dispatcher = Dispatcher;
        }
        catch
        {
            return false;
        }

        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return false;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return true;
        }

        try
        {
            dispatcher.BeginInvoke(action);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void BindRow()
    {
        _path = null;
        _bars = null;
        if (DataContext is LibraryFileRow { Tag: DocumentSession session }
            && session.Document.SourcePath is { Length: > 0 } path)
        {
            _path = path;
            if (LibraryPlaylistWaveform.TryGet(path, LibraryPlaylistWaveform.BarCount, out var bars))
            {
                _bars = bars;
            }
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bars = _bars;
        if (bars is null || bars.Length == 0 || ActualHeight <= 0 || ActualWidth <= 0)
        {
            return;
        }

        var fill = PlayerChrome.Get("PlayerWaveFillBrush");
        // リスト内は背景が暗いので、本体波形より半透明を少し弱めて見やすくする。
        var brush = WpfControlHelpers.FrozenBrush(Color.FromArgb(
            (byte)Math.Min(0xFF, fill.A + 0x28),
            fill.R,
            fill.G,
            fill.B));
        var midY = ActualHeight * 0.5;
        var maxHalf = Math.Max(1.0, midY - 1);
        var x = LibraryBrowserView.LibraryColumnCellPadX;
        var count = Math.Min(bars.Length, LibraryPlaylistWaveform.BarCount);
        for (var i = 0; i < count; i++)
        {
            var amp = Math.Clamp(bars[i], 0f, 1f);
            if (amp > 0f)
            {
                var height = Math.Max(1.0, amp * maxHalf * 2);
                var top = midY - (height * 0.5);
                dc.DrawRectangle(
                    brush,
                    null,
                    new Rect(x, top, LibraryPlaylistWaveform.BarThickness, height));
            }

            x += LibraryPlaylistWaveform.BarThickness + LibraryPlaylistWaveform.BarGap;
        }
    }
}
