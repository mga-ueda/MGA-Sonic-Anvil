using System.IO;
using System.Runtime.InteropServices;

namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// MF の読み手を専用ワーカースレッドに置き、UI からは位置を要求してキャッシュを引くだけにする非同期ポンプ。
/// <list type="bullet">
/// <item>UI: <see cref="Open"/>／<see cref="Close"/>（ワーカー完了を待つ）、<see cref="RequestPosition"/>（最新だけ残す）、
/// <see cref="TryGetFrame"/>（ブロックしない）。</item>
/// <item>ワーカー: 最新の要求位置を見て、前進なら読み進め、後退や大きな前進なら Seek（MF は手前のキーフレームへ着地）して
/// 目標＋先読み分までデコードし <see cref="DxgiFrameCache"/> に積む。1 回の起床で読む枚数を区切り、新しい要求を拾い直す。</item>
/// </list>
/// 古い要求は上書きで捨てる。UI をブロックするのは Open／Close のときだけ。
/// <para>
/// シャトル（<see cref="SetShuttleMode"/>）: 音声 3 倍で映像クロックを止め、要求位置が音声ヘッドに追従して 3 倍速で進む。
/// 方針は「スクラブ／巻き戻しと同じく Seek で追従する」。巻き戻しは要求が区間の手前へ出るたび Seek し、
/// 着地したキーフレーム以降がすぐ出るので追従が切れない。前進も同じで、含むフレームが無く読み進めで
/// 追いつけていない（遅れが縮まない＝音声 3 倍に読みが負けている）なら、Seek せず読み進め続けるのではなく
/// 要求位置へ Seek して手前のキーフレームから出し直す（<see cref="ShouldSeekOnShuttleMiss"/>）。
/// 先読みと最寄り許容は広げ、「遅れていても動いている」絵を出し続ける。
/// </para>
/// </summary>
internal sealed class DxgiAsyncFramePump : IDisposable
{
    /// <summary>これより先へ飛ぶなら Seek。手前なら読み進める（キーフレーム間隔程度）。</summary>
    private static readonly long ForwardReadLimitTicks = TimeSpan.FromSeconds(1.5).Ticks;

    /// <summary>1 回の起床で読む上限。要求の取り直しの粒度。</summary>
    private const int MaxSamplesPerWake = 6;

    // ---- シャトル中の値（SetShuttleMode(true) のとき通常値の代わりに使う） ----

    /// <summary>
    /// シャトル中の先読み。3 倍速なら 0.5s の実時間ぶん。これ以上はキャッシュ容量（1080p で 20 枚強）に収まらず、
    /// 読んでも捨てる（<see cref="PumpOnce"/> 側で容量にも抑える）。
    /// </summary>
    private static readonly TimeSpan ShuttlePrefetch = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// シャトル中、読み進めで追いつくのを諦めて必ず Seek する遅れの上限。これ未満でも遅れが縮まなければ Seek する
    /// （<see cref="ShouldSeekOnShuttleMiss"/>）。縮んでいる間だけ、ここまでは読み進めを許す。
    /// </summary>
    private static readonly long ShuttleForwardReadLimitTicks = TimeSpan.FromSeconds(5).Ticks;

    /// <summary>シャトル中の最寄り許容。遅れたフレームでも止めずに出し続ける（3 倍速で ≈ 0.67s の実時間ぶん）。</summary>
    private static readonly TimeSpan ShuttleNearestTolerance = TimeSpan.FromSeconds(2);

    /// <summary>シャトル中の 1 回の起床で読む上限。要求の取り直しを減らして追いつきを速める。</summary>
    private const int ShuttleMaxSamplesPerWake = 16;

    /// <summary>
    /// シャトル中、Seek 直後の追いつき中に先読みを抑える枚数（目標＋この枚数で一旦止めて要求位置を取り直す）。
    /// Seek 後にいつもの先読み（1.5s 超）まで一気に読むと、その間に音声ヘッドが先へ行き、読んだ分がほぼ捨てになる。
    /// まず目標の 1 枚を出し、要求が同じ区間に留まっているときだけ通常の先読みへ広げる（巻き戻しでは先読み自体が無駄）。
    /// </summary>
    private const int ShuttleSeekRunwayFrames = 2;

    /// <summary>先読みを容量に抑えるとき残す枚数（直前に出した枚と、追い出し順の揺れのぶん）。</summary>
    private const int PrefetchCacheMarginFrames = 2;

    // ---- スクラブ プレビュー中の値（SetScrubPreviewMode(true) のとき） ----

    /// <summary>
    /// スクラブ プレビューで Seek 後に読む枚数。TimeCaster のスクラブ プレビューと同じく、長 GOP ソースでは
    /// 着地したキーフレームをそのまま粗い絵として出し、目標まで読み進めない（正確な絵は静止後の通常モードで）。
    /// MJPEG プロキシは全部キーフレームなので Seek だけで目標の枚が出る。
    /// </summary>
    private const int ScrubPreviewSamplesAfterSeek = 2;

    /// <summary>スクラブ プレビューで、Seek せず読み進めで追う前進の上限（枚）。これ以下なら Seek より安い。</summary>
    private const int ScrubPreviewForwardReadFrames = 3;

    /// <summary>
    /// スクラブ プレビュー中の最寄り許容。着地したキーフレームが目標から GOP ぶん離れていても出す
    /// （出さないと前の位置の絵が残って、動かしているのに止まって見える）。
    /// </summary>
    private static readonly TimeSpan ScrubPreviewNearestTolerance = TimeSpan.FromSeconds(10);

    /// <summary>キャッシュに使うメモリの目安。フレーム寸法と画素形式（BGRA 4B／NV12 1.5B）で枚数に換算する。</summary>
    private const long CacheBudgetBytes = 192L * 1024 * 1024;

    private const int MinCacheFrames = 6;
    private const int MaxCacheFrames = 64;

    private readonly object _commandGate = new();
    private readonly Queue<(Action Action, ManualResetEventSlim Done)> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly DxgiFrameCache _cache;
    private readonly DxgiPrototypeFrameSource _reader = new();
    private readonly Stack<byte[]> _pool = new();

    private Thread? _worker;
    private volatile bool _disposed;
    private volatile bool _isOpen;
    private volatile bool _shuttle;
    private volatile bool _scrubPreview;
    private long _requestedTicks = -1;
    private int _frameBytes;

    // ---- ワーカー専用の状態 ----
    private bool _hasRange;
    private long _rangeStart;
    private long _rangeEnd;
    private long _lastSeekTicks = -1;

    /// <summary>スクラブ プレビューで粗い絵を出し終えた要求位置。同じ位置へはもう読まない（通常モードへ戻ると捨てる）。</summary>
    private long _scrubPreviewServedTicks = -1;

    /// <summary>今の読み区間で最後に捨て読み（キャッシュに積まず）したサンプルの終端。これより手前は穴がありうる。</summary>
    private long _lastSkipEnd = long.MinValue;
    private bool _catchingUp;
    private bool _failed;

    /// <summary>
    /// シャトル中の前進追いつきで、前回の起床時に見た遅れ（target - _rangeEnd）。long.MaxValue なら未観測
    /// （Seek 直後／含むフレームが出た直後）。縮んでいなければ Seek へ切り替える。
    /// </summary>
    private long _shuttleChaseGap = long.MaxValue;

    public DxgiAsyncFramePump(int cacheCapacity = 0)
    {
        _cache = new DxgiFrameCache(cacheCapacity > 0 ? cacheCapacity : 32);
        _cache.Evicted += ReturnBuffer;
        ExplicitCapacity = cacheCapacity;
    }

    /// <summary>0 ならフレーム寸法とメモリ目安から自動。</summary>
    public int ExplicitCapacity { get; }

    /// <summary>要求位置より先に読んでおく時間（通常時）。</summary>
    public TimeSpan Prefetch { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary><see cref="TryGetFrame"/> で「含むフレーム」が無いとき、最寄りを返す許容距離（通常時）。</summary>
    public TimeSpan NearestTolerance { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>シャトル中か（<see cref="SetShuttleMode"/>）。</summary>
    public bool IsShuttleMode => _shuttle;

    /// <summary>今のモードで実際に使う先読み。シャトル中は <see cref="Prefetch"/> と <see cref="ShuttlePrefetch"/> の大きい方。</summary>
    public TimeSpan EffectivePrefetch => _shuttle ? Max(Prefetch, ShuttlePrefetch) : Prefetch;

    /// <summary>
    /// 今のモードで実際に使う、Seek へ切り替える前進距離。通常時はこれ未満なら読み進め。
    /// シャトル中はこれが上限で、これ未満でも遅れが縮まなければ Seek する。
    /// </summary>
    public TimeSpan EffectiveForwardReadLimit => TimeSpan.FromTicks(_shuttle ? ShuttleForwardReadLimitTicks : ForwardReadLimitTicks);

    /// <summary>
    /// 今のモードで実際に使う最寄り許容。シャトル中は <see cref="NearestTolerance"/> と <see cref="ShuttleNearestTolerance"/> の大きい方。
    /// スクラブ プレビュー中は <see cref="ScrubPreviewNearestTolerance"/>（着地したキーフレームを出すため）。
    /// </summary>
    public TimeSpan EffectiveNearestTolerance =>
        _scrubPreview ? Max(NearestTolerance, ScrubPreviewNearestTolerance)
        : _shuttle ? Max(NearestTolerance, ShuttleNearestTolerance)
        : NearestTolerance;

    /// <summary>スクラブ プレビュー中か（<see cref="SetScrubPreviewMode"/>）。</summary>
    public bool IsScrubPreviewMode => _scrubPreview;

    /// <summary>今のモードで実際に使う、1 回の起床で読む上限。</summary>
    public int EffectiveMaxSamplesPerWake => _shuttle ? ShuttleMaxSamplesPerWake : MaxSamplesPerWake;

    public bool IsOpen => _isOpen && !_disposed;

    public TimeSpan Duration { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>読み手の出力形式（NV12 優先、駄目なら BGRA）。開いていなければ Bgra32。</summary>
    public DxgiFramePixelFormat PixelFormat { get; private set; } = DxgiFramePixelFormat.Bgra32;

    public VideoColorInfo ColorInfo { get; private set; } = VideoColorInfo.Default;

    public TimeSpan FrameDuration { get; private set; } = TimeSpan.FromSeconds(1.0 / 30);

    public int CachedFrameCount => _cache.Count;

    /// <summary>
    /// ワーカーで開く（完了まで待つ）。開けなければ false（呼び出し側はテストパターンへ）。
    /// <paramref name="forceLimitedRange"/> は MJPEG プロキシ用: プロキシは TV レンジで焼いてあるが、MF の MJPEG デコーダは
    /// JPEG 慣例でフルレンジ（または不明）を返すことがあるので、<see cref="ColorInfo"/> とフレームの色情報を
    /// リミテッドレンジに固定する（行列・原色はそのまま。シェーダ側の計算は変えない）。
    /// </summary>
    public bool Open(string path, bool forceLimitedRange = false)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        Close();
        EnsureWorker();
        var ok = false;
        if (!RunOnWorker(() => ok = OpenOnWorker(path, forceLimitedRange), TimeSpan.FromSeconds(15)))
        {
            // 時間切れ: 遅れて開いても使わないように閉じ直す。
            Close();
            return false;
        }

        return ok;
    }

    /// <summary>読み手を閉じてキャッシュを捨てる。ワーカーが閉じ終わるまで（上限つきで）待つ。</summary>
    public void Close()
    {
        _isOpen = false;
        _scrubPreview = false;
        Interlocked.Exchange(ref _requestedTicks, -1);
        _cache.Clear();
        if (_worker is null)
        {
            return;
        }

        RunOnWorker(CloseOnWorker, TimeSpan.FromSeconds(3));
    }

    /// <summary>表示したい位置。連打しても最新だけ残る。</summary>
    public void RequestPosition(TimeSpan position)
    {
        if (!IsOpen)
        {
            return;
        }

        Interlocked.Exchange(ref _requestedTicks, Math.Max(0, position.Ticks));
        _wake.Set();
    }

    /// <summary>
    /// シャトル（音声 3 倍・映像クロック停止で要求位置が音声ヘッドに追従）中かを UI が毎 tick 伝える。
    /// 入るときはワーカーを起こして先読みをすぐ広げる。抜けるときはキャッシュをそのまま残す（追いつき中の絵を切らない）。
    /// 既定は false。
    /// </summary>
    public void SetShuttleMode(bool shuttle)
    {
        if (_shuttle == shuttle)
        {
            return;
        }

        _shuttle = shuttle;
        if (shuttle && IsOpen)
        {
            _wake.Set();
        }
    }

    /// <summary>
    /// スクラブ プレビュー（ドラッグ中の粗い絵）。true の間は、含むフレームが無ければ読み進めで追わず
    /// Seek＋数枚だけ読んで最寄り（長 GOP なら着地したキーフレーム）を出し、同じ位置へはそれ以上読まない。
    /// false に戻すと次の要求で通常どおり目標まで読み進める（静止後の正確な絵）。どちらの切り替えでもワーカーを起こす。
    /// MediaElement 経路には関係しない。既定は false。
    /// </summary>
    public void SetScrubPreviewMode(bool preview)
    {
        if (_scrubPreview == preview)
        {
            return;
        }

        _scrubPreview = preview;
        if (IsOpen)
        {
            _wake.Set();
        }
    }

    /// <summary>position を含むフレーム、無ければ <see cref="EffectiveNearestTolerance"/> 以内の最寄り。ブロックしない。</summary>
    public bool TryGetFrame(TimeSpan position, out DxgiDecodedFrame? frame)
    {
        frame = null;
        if (!IsOpen)
        {
            return false;
        }

        return _cache.TryGetNearest(Math.Max(0, position.Ticks), Math.Max(0, EffectiveNearestTolerance.Ticks), out frame);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _isOpen = false;
        _disposed = true;
        _cache.Clear();
        _wake.Set();
        var worker = _worker;
        if (worker is not null && worker != Thread.CurrentThread)
        {
            worker.Join(TimeSpan.FromSeconds(3));
        }

        _worker = null;
        lock (_commandGate)
        {
            while (_commands.Count > 0)
            {
                _commands.Dequeue().Done.Set();
            }
        }
    }

    // ---- UI 側ヘルパ ----

    private void EnsureWorker()
    {
        if (_worker is { IsAlive: true })
        {
            return;
        }

        var thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "DxgiFramePump",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        _worker = thread;
        thread.Start();
    }

    private bool RunOnWorker(Action action, TimeSpan timeout)
    {
        if (_disposed || _worker is null)
        {
            return false;
        }

        if (Thread.CurrentThread == _worker)
        {
            action();
            return true;
        }

        using var done = new ManualResetEventSlim(false);
        lock (_commandGate)
        {
            _commands.Enqueue((action, done));
        }

        _wake.Set();
        return done.Wait(timeout);
    }

    // ---- ワーカー ----

    private void WorkerLoop()
    {
        var comInitialized = false;
        try
        {
            comInitialized = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED) >= 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        try
        {
            while (!_disposed)
            {
                _wake.WaitOne();
                if (_disposed)
                {
                    break;
                }

                RunPendingCommands();
                if (!_reader.IsOpen || !_isOpen)
                {
                    continue;
                }

                // 仕事が残っていて新しい命令が無い間は続けて読む（起床ごとに要求位置を取り直す）。
                while (!_disposed && _isOpen && PumpOnce() && !HasPendingCommands())
                {
                }
            }
        }
        catch (Exception)
        {
            // ワーカーは落とさず黙って止める（UI は最後のフレームを保持）。
        }
        finally
        {
            _isOpen = false;
            try
            {
                _reader.Close();
            }
            catch
            {
            }

            RunPendingCommands();
            if (comInitialized)
            {
                CoUninitialize();
            }
        }
    }

    private bool HasPendingCommands()
    {
        lock (_commandGate)
        {
            return _commands.Count > 0;
        }
    }

    private void RunPendingCommands()
    {
        while (true)
        {
            (Action Action, ManualResetEventSlim Done) cmd;
            lock (_commandGate)
            {
                if (_commands.Count == 0)
                {
                    return;
                }

                cmd = _commands.Dequeue();
            }

            try
            {
                if (!_disposed)
                {
                    cmd.Action();
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    cmd.Done.Set();
                }
                catch (ObjectDisposedException)
                {
                    // 待ち側が時間切れで破棄済み。
                }
            }
        }
    }

    private bool OpenOnWorker(string path, bool forceLimitedRange)
    {
        CloseOnWorker();
        if (!_reader.TryOpen(path, out var duration, out var width, out var height))
        {
            return false;
        }

        Duration = duration;
        Width = width;
        Height = height;
        ColorInfo = ColorInfoForSource(_reader.ColorInfo, forceLimitedRange);
        FrameDuration = _reader.FrameDuration;
        ResetPoolAndCache();
        _isOpen = true;
        return true;
    }

    private void CloseOnWorker()
    {
        _isOpen = false;
        _reader.Close();
        _cache.Clear();
        _pool.Clear();
        _frameBytes = 0;
        _hasRange = false;
        _lastSeekTicks = -1;
        _lastSkipEnd = long.MinValue;
        _catchingUp = false;
        _shuttleChaseGap = long.MaxValue;
        _scrubPreviewServedTicks = -1;
        _failed = false;
        Duration = TimeSpan.Zero;
        Width = 0;
        Height = 0;
        PixelFormat = DxgiFramePixelFormat.Bgra32;
        ColorInfo = VideoColorInfo.Default;
        Interlocked.Exchange(ref _requestedTicks, -1);
    }

    /// <summary>
    /// 開いたソースに使う色情報。<paramref name="forceLimitedRange"/>（MJPEG プロキシ）なら Range だけ Limited に固定し、
    /// 原色・伝達関数・行列は読み手の値（Normalized 済み）を残す。原ファイル（フルレンジを含む）はそのまま。
    /// </summary>
    internal static VideoColorInfo ColorInfoForSource(VideoColorInfo native, bool forceLimitedRange) =>
        forceLimitedRange ? native with { Range = VideoNominalRange.Limited } : native;

    /// <summary>寸法が決まった／変わったとき。プールとキャッシュを作り直し、容量をメモリ目安（形式別のバイト数）から決める。</summary>
    private void ResetPoolAndCache()
    {
        _cache.Clear();
        _pool.Clear();
        Width = _reader.Width;
        Height = _reader.Height;
        PixelFormat = _reader.PixelFormat;
        _frameBytes = _reader.FrameBytes;
        _hasRange = false;
        _lastSeekTicks = -1;
        _lastSkipEnd = long.MinValue;
        _shuttleChaseGap = long.MaxValue;
        _scrubPreviewServedTicks = -1;
        if (ExplicitCapacity <= 0 && _frameBytes > 0)
        {
            _cache.Capacity = (int)Math.Clamp(CacheBudgetBytes / _frameBytes, MinCacheFrames, MaxCacheFrames);
        }
    }

    /// <summary>1 回ぶん読む。true ならまだ続きがある（続けて呼ぶ）。</summary>
    private bool PumpOnce()
    {
        var target = Interlocked.Read(ref _requestedTicks);
        if (target < 0 || _failed)
        {
            return false;
        }

        // 追い出しの中心は窓 [target - 少し, target + 先読み] の中央。target 中心だと前方は容量の半分しか残らず、
        // 先読みの後半が捨て読みになって読み区間に穴が開く。
        var prefetchTicks = PrefetchTicksForCache();
        _cache.AnchorTicks = target + (prefetchTicks / 2);
        var prefetchEnd = target + prefetchTicks;

        // シャトル中の Seek 後の追いつきは、目標の少し先で一旦止める（長い先読みを捨てないため）。
        var catchUpEnd = CatchUpPrefetchEnd(target, Math.Max(1, FrameDuration.Ticks), prefetchEnd, _shuttle, _catchingUp);

        // 今の読み区間のうちキャッシュに残っている部分 [effStart, _rangeEnd)。前方が追い出されていれば縮む。
        var effStart = _rangeStart;
        if (_hasRange)
        {
            var kept = _cache.MinPtsAtOrAfter(_rangeStart);
            effStart = kept >= 0 && kept < _rangeEnd ? kept : _rangeEnd;
        }

        var scrubPreview = _scrubPreview;
        if (!scrubPreview)
        {
            _scrubPreviewServedTicks = -1;
        }

        if (_cache.TryGetCovering(target, out _))
        {
            // 手元にある。追いつきは終わったので遅れの観測を捨てる。今読んでいる区間の続きなら先読みだけ。
            _shuttleChaseGap = long.MaxValue;
            if (scrubPreview)
            {
                // スクラブ プレビュー中は先読みしない（次の要求はどこへ飛ぶか分からず、読んでも捨てになる）。
                return false;
            }

            if (_hasRange && !_reader.EndOfStream && target >= effStart && target < _rangeEnd && _rangeEnd < prefetchEnd)
            {
                return DecodeForward(prefetchEnd);
            }

            return false;
        }

        if (scrubPreview)
        {
            return PumpScrubPreview(target, prefetchEnd);
        }

        if (_hasRange && target >= effStart && target < _rangeEnd
            && !ShouldReseekInsideRange(target, _lastSkipEnd, _cache.LastEndTicks, _lastSeekTicks))
        {
            // 区間内の本物の隙間（PTS の飛び）。最寄りで出す。Seek し直しても同じ結果なので繰り返さない。
            return false;
        }

        // ここで target < _rangeEnd なら、読み手は既に target を過ぎたのに積めていない（捨て読み／追い出しの穴）。
        // 読み進めても戻れないので下の Seek へ落とす。

        var forwardReadLimit = EffectiveForwardReadLimit.Ticks;
        if (_hasRange && !_reader.EndOfStream && target >= _rangeEnd)
        {
            if (_shuttle)
            {
                // シャトル: 巻き戻しと同じく Seek で追従する。読み進めを許すのは、前回の起床より遅れが縮んでいる
                // （読みが 3 倍速に勝っていて、まもなく含むフレームが出る）間だけ。縮まないなら読み進めても
                // 永久に追いつけず、最寄り許容を越えた時点で止まって見えるので、下の Seek へ落とす。
                var gap = target - _rangeEnd;
                if (!ShouldSeekOnShuttleMiss(gap, _shuttleChaseGap, forwardReadLimit))
                {
                    _shuttleChaseGap = gap;
                    return DecodeForward(catchUpEnd);
                }
            }
            else if (_catchingUp || target - _rangeEnd < forwardReadLimit)
            {
                // 通常: 少し先、または Seek 後の追いつき中（キーフレーム間隔が長くても Seek し直さない）は読み進める。
                return DecodeForward(prefetchEnd);
            }
        }

        if (_hasRange && _reader.EndOfStream && target >= _rangeEnd)
        {
            // 終端より先: 最後のフレームを最寄りとして出す。
            return false;
        }

        if (_hasRange && _lastSeekTicks == target && target < _rangeStart)
        {
            // 同じ位置へ Seek したのに手前へ着地しなかった（先頭より前など）。繰り返さない。
            return false;
        }

        // 後退、大きく前進、またはシャトル中に読み進めで追いつけていない前進:
        // Seek（手前のキーフレームへ着地）してから目標まで読む。シャトル中は目標の少し先まで（滑走路を短く）。
        _reader.Seek(TimeSpan.FromTicks(target));
        _hasRange = false;
        _lastSkipEnd = long.MinValue;
        _catchingUp = true;
        _lastSeekTicks = target;
        _shuttleChaseGap = long.MaxValue;
        return DecodeForward(CatchUpPrefetchEnd(target, Math.Max(1, FrameDuration.Ticks), prefetchEnd, _shuttle, catchingUp: true));
    }

    /// <summary>
    /// スクラブ プレビューで、含むフレームが無いときの 1 回ぶん。TimeCaster の TryPresentScrubPreview と同じ方針:
    /// 少し先（≦ <see cref="ScrubPreviewForwardReadFrames"/> 枚）なら Seek せず数枚読み進めて正確な枚を出し、
    /// それ以外は Seek して <see cref="ScrubPreviewSamplesAfterSeek"/> 枚だけ読む（長 GOP なら着地したキーフレームが
    /// 最寄りとして出る。MJPEG プロキシなら目標の枚そのもの）。同じ要求位置にはもう読まない。
    /// 先読みはせず、常に false（続きなし）を返してワーカーを寝かせる。
    /// </summary>
    private bool PumpScrubPreview(long target, long prefetchEnd)
    {
        if (_scrubPreviewServedTicks == target)
        {
            // この位置の粗い絵は出し済み。正確な絵は静止後に通常モードへ戻ってから。
            return false;
        }

        var frameTicks = Math.Max(1, FrameDuration.Ticks);
        if (!ShouldSeekOnScrubPreviewMiss(target, _hasRange, _rangeEnd, _reader.EndOfStream, frameTicks))
        {
            _scrubPreviewServedTicks = target;
            DecodeForward(Math.Min(prefetchEnd, target + frameTicks), ScrubPreviewForwardReadFrames + 1);
            return false;
        }

        if (_hasRange && _reader.EndOfStream && target >= _rangeEnd)
        {
            // 終端より先: 最後のフレームを最寄りとして出す。
            _scrubPreviewServedTicks = target;
            return false;
        }

        if (_hasRange && _lastSeekTicks == target && target < _rangeStart)
        {
            // 同じ位置へ Seek したのに手前へ着地しなかった。繰り返さない。
            _scrubPreviewServedTicks = target;
            return false;
        }

        _reader.Seek(TimeSpan.FromTicks(target));
        _hasRange = false;
        _lastSkipEnd = long.MinValue;
        _catchingUp = true;
        _lastSeekTicks = target;
        _shuttleChaseGap = long.MaxValue;
        _scrubPreviewServedTicks = target;
        DecodeForward(prefetchEnd, ScrubPreviewSamplesAfterSeek);
        return false;
    }

    /// <summary>
    /// スクラブ プレビューで含むフレームが無いとき、Seek するか（true）／今の読み位置から数枚読み進めるか（false）。
    /// 読み進めは、読み手が目標の手前（target ≧ rangeEnd）にいて、差が <see cref="ScrubPreviewForwardReadFrames"/> 枚以内で、
    /// 終端に達していないときだけ。後退や大きな前進、未読（区間なし）は Seek。
    /// </summary>
    internal static bool ShouldSeekOnScrubPreviewMiss(long target, bool hasRange, long rangeEnd, bool endOfStream, long frameTicks)
    {
        if (!hasRange || endOfStream || target < rangeEnd)
        {
            return true;
        }

        return target - rangeEnd > ScrubPreviewForwardReadFrames * Math.Max(1, frameTicks);
    }

    /// <summary>
    /// 追いつき読みの先読み終端。シャトル中かつ Seek 後の追いつき中（<paramref name="catchingUp"/>）は
    /// target + <see cref="ShuttleSeekRunwayFrames"/> 枚で止め（通常の終端より手前なら）、目標の 1 枚をまず出して
    /// 要求位置を取り直す。目標を含むフレームが出た後の続きは通常の <paramref name="prefetchEnd"/> で読む。
    /// それ以外（通常モード／追いつき中でない）は <paramref name="prefetchEnd"/> のまま。
    /// </summary>
    internal static long CatchUpPrefetchEnd(long target, long frameTicks, long prefetchEnd, bool shuttle, bool catchingUp)
    {
        if (!shuttle || !catchingUp)
        {
            return prefetchEnd;
        }

        return Math.Min(prefetchEnd, target + (ShuttleSeekRunwayFrames * Math.Max(1, frameTicks)));
    }

    /// <summary>
    /// シャトル中、含むフレームが無く target が読み区間の先（gap = target - rangeEnd ≧ 0）にあるとき Seek するか。
    /// <list type="bullet">
    /// <item>gap が <paramref name="forwardReadLimit"/> 以上: 読み進めでは遠すぎる。Seek。</item>
    /// <item>前回の起床（<paramref name="previousGap"/>）より gap が縮んでいない: 読みが 3 倍速に負けている。
    /// 続けても追いつけないので巻き戻しと同じく Seek し、target 手前のキーフレームから出し直す。</item>
    /// <item>未観測（previousGap == long.MaxValue）または縮んでいる: 1 起床ぶん読み進めて様子を見る。
    /// 同じキーフレーム区間へ Seek し直して同じ枚を読み直す無駄を避ける。</item>
    /// </list>
    /// </summary>
    internal static bool ShouldSeekOnShuttleMiss(long gap, long previousGap, long forwardReadLimit)
    {
        if (gap >= forwardReadLimit)
        {
            return true;
        }

        return previousGap != long.MaxValue && gap >= previousGap;
    }

    /// <summary>
    /// 含むフレームが無い target が読み区間 [effStart, rangeEnd) の内側にあるとき、Seek し直すか。
    /// 捨て読みの範囲（target &lt; lastSkipEnd）やキャッシュ末尾より先（後ろ側の追い出し・捨て読み）は穴なので Seek。
    /// それ以外は積んだフレームの間の本物の PTS の飛び。同じ target へは 1 回だけ（Seek しても穴なら繰り返さない）。
    /// </summary>
    internal static bool ShouldReseekInsideRange(long target, long lastSkipEnd, long cachedEndTicks, long lastSeekTicks)
    {
        if (target == lastSeekTicks)
        {
            return false;
        }

        return target < lastSkipEnd || target >= cachedEndTicks;
    }

    /// <summary>
    /// 今のモードの先読み（ticks）を、キャッシュに収まる長さに抑えたもの。
    /// 容量を超えて読み進めると、その分は読んでも捨てられ（<see cref="DxgiFrameCache.WouldKeep"/> が false）、
    /// あとで要求位置がそこへ来たとき穴として Seek し直す羽目になる（キーフレームへ戻るぶん止まって見える）。
    /// </summary>
    private long PrefetchTicksForCache()
    {
        var frameTicks = Math.Max(1, FrameDuration.Ticks);
        var wanted = Math.Max(frameTicks, EffectivePrefetch.Ticks);
        var cacheSpan = (long)Math.Max(1, _cache.Capacity - PrefetchCacheMarginFrames) * frameTicks;
        return Math.Min(wanted, cacheSpan);
    }

    /// <summary>
    /// prefetchEnd を超えるまで読み進める（1 回の上限つき。<paramref name="maxSamples"/> が 0 ならモードの既定）。
    /// true なら続きがある。
    /// </summary>
    private bool DecodeForward(long prefetchEnd, int maxSamples = 0)
    {
        if (maxSamples <= 0)
        {
            maxSamples = EffectiveMaxSamplesPerWake;
        }

        for (var i = 0; i < maxSamples; i++)
        {
            // 要求位置が変わっていたら上位で取り直す。
            var target = Interlocked.Read(ref _requestedTicks);
            if (target < 0)
            {
                return false;
            }

            // サンプルは 1 回しか取れないので、次の PTS ≒ 直前の終端として「キャッシュに残るか」を見積もり、
            // 残らない（目標から容量ぶん以上手前、または既に近い枠で埋まっている）捨て読みはコピーを省く。
            // 目標〜先読み終端の枠は必ず積む（ここを捨てると要求位置がそこへ来たとき穴になり Seek が要る）。
            var wouldKeep = !_hasRange
                            || (_rangeEnd >= target && _rangeEnd < prefetchEnd)
                            || (_cache.WouldKeep(_rangeEnd)
                                && target - _rangeEnd < (long)_cache.Capacity * Math.Max(1, FrameDuration.Ticks));
            var buffer = wouldKeep ? RentBuffer() : null;
            var result = _reader.ReadNext(buffer, out var pts, out var duration);
            switch (result)
            {
                case DxgiReadResult.Frame:
                    if (!_hasRange)
                    {
                        _hasRange = true;
                        _rangeStart = pts;
                    }

                    _rangeEnd = pts + Math.Max(1, duration);
                    if (_rangeEnd > target)
                    {
                        _catchingUp = false;
                    }

                    if (buffer is not null)
                    {
                        _cache.Add(new DxgiDecodedFrame(pts, duration, Width, Height, PixelFormat, buffer, ColorInfo));
                    }
                    else
                    {
                        // 読み手は進んだがキャッシュには無い。ここまでは「埋まっている」とみなさない。
                        _lastSkipEnd = _rangeEnd;
                    }

                    if (_rangeEnd > prefetchEnd)
                    {
                        return false;
                    }

                    break;

                case DxgiReadResult.EndOfStream:
                    if (buffer is not null)
                    {
                        _pool.Push(buffer);
                    }

                    return false;

                case DxgiReadResult.NeedLargerBuffer:
                    ResetPoolAndCache();
                    break;

                default:
                    if (buffer is not null)
                    {
                        _pool.Push(buffer);
                    }

                    _failed = true;
                    return false;
            }
        }

        return true;
    }

    private byte[] RentBuffer()
    {
        while (_pool.Count > 0)
        {
            var b = _pool.Pop();
            if (b.Length == _frameBytes)
            {
                return b;
            }
        }

        return GC.AllocateUninitializedArray<byte>(_frameBytes);
    }

    /// <summary>キャッシュから追い出されたフレームのバッファを戻す（ワーカーの Add から呼ばれる）。</summary>
    private void ReturnBuffer(DxgiDecodedFrame frame)
    {
        if (frame.Pixels.Length == _frameBytes && _pool.Count < _cache.Capacity + 4)
        {
            _pool.Push(frame.Pixels);
        }
    }

    private const uint COINIT_MULTITHREADED = 0x0;

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
}
