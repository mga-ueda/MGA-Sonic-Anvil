using System.Linq;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// ライブ 8 スロット。文書切替時は構造キーが同じなら reload せず state／bypass だけ反映。
/// エディタ滞在中は同一スロットの同一プラグインを極力維持（バイパスの有無だけではアンロードしない）。
/// タブ切替で空きになるスロットは破棄せずオーナー別に退避し、戻ったら同一インスタンスを復帰する。
/// 未ロードのバイパススロットは遅延生成（解除時／EnsureSlotLoaded）。
/// プレイヤーモード（playerBypass）では Process が no-op かつプラグインを読まず、既存ライブは退避する。
/// エディタ復帰（playerBypass: false）で非バイパス分を復帰／ロードする。
/// Load／Dispose は短時間の書込ロック外。Process は参照スナップショット後に DSP（再生中差し替えで固まらない）。
/// </summary>
internal sealed class VstInsertEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly IVstSlotProcessorFactory _factory;
    private readonly IVstSlotProcessor?[] _live = new IVstSlotProcessor?[VstInsertChain.MaxSlots];
    private readonly bool[] _bypass = new bool[VstInsertChain.MaxSlots];
    private readonly bool[] _faulted = new bool[VstInsertChain.MaxSlots];
    /// <summary>エディタ createView／attach 中。bypass と別（dry を通さず無音にする）。</summary>
    private readonly bool[] _uiSuspended = new bool[VstInsertChain.MaxSlots];
    private readonly IVstSlotProcessor?[] _processScratch = new IVstSlotProcessor?[VstInsertChain.MaxSlots];
    /// <summary>非アクティブ文書のライブ退避。キーは DocumentSession など owner。</summary>
    private readonly Dictionary<object, IVstSlotProcessor?[]> _parked = new();
    private string _structuralKey = string.Empty;
    private VstInsertChain? _applied;
    private object? _owner;
    private double _sampleRate = 48000;
    private int _maxBlockSize = 512;
    private int _channels = 2;
    private bool _playerBypass = true;
    private bool _exclusiveLoad;
    private bool _disposed;
    private int _reloadCount;
    private int _stateApplyCount;
    private int _processUsers;

    public VstInsertEngine(IVstSlotProcessorFactory? factory = null)
    {
        _factory = factory ?? Vst3SlotProcessorFactory.Instance;
    }

    public int ReloadCount
    {
        get
        {
            lock (_gate)
            {
                return _reloadCount;
            }
        }
    }

    public int StateApplyCount
    {
        get
        {
            lock (_gate)
            {
                return _stateApplyCount;
            }
        }
    }

    private int _cachedLatencyFrames;

    /// <summary>直近に測った getLatencySamples 合計。描画スレッドはここを読む。</summary>
    public int ActiveLatencyFrames
    {
        get
        {
            lock (_gate)
            {
                return _disposed || _playerBypass ? 0 : _cachedLatencyFrames;
            }
        }
    }

    /// <summary>再生コールバックから呼ぶ。プラグインに問い合わせてキャッシュを更新する。</summary>
    public int RefreshActiveLatency()
    {
        double sampleRate;
        lock (_gate)
        {
            if (_disposed || _playerBypass)
            {
                _cachedLatencyFrames = 0;
                return 0;
            }

            sampleRate = _sampleRate > 0 ? _sampleRate : 48000;
        }

        var value = QueryActiveLatencyFrames(sampleRate);
        lock (_gate)
        {
            _cachedLatencyFrames = value;
        }

        return value;
    }

    public bool PlayerBypass
    {
        get
        {
            lock (_gate)
            {
                return _playerBypass;
            }
        }
    }

    public void Reconfigure(double sampleRate, int maxBlockSize, int channels)
    {
        sampleRate = sampleRate > 0 ? sampleRate : 48000;
        maxBlockSize = Math.Max(16, maxBlockSize);
        channels = Math.Max(1, channels);

        IVstSlotProcessor?[] snapshot;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Math.Abs(_sampleRate - sampleRate) < 0.01
                && _maxBlockSize == maxBlockSize
                && _channels == channels)
            {
                return;
            }

            _sampleRate = sampleRate;
            _maxBlockSize = maxBlockSize;
            _channels = channels;
            snapshot = (IVstSlotProcessor?[])_live.Clone();
        }

        for (var i = 0; i < snapshot.Length; i++)
        {
            var live = snapshot[i];
            if (live is null)
            {
                continue;
            }

            try
            {
                live.Reconfigure(sampleRate, maxBlockSize, channels);
            }
            catch
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_live[i], live))
                    {
                        MarkFaultedNoLock(i);
                    }
                }
            }
        }
    }

    public void Apply(object? owner, VstInsertChain? chain, bool playerBypass)
    {
        var toDispose = new List<IVstSlotProcessor>(VstInsertChain.MaxSlots);
        var createAt = new int[VstInsertChain.MaxSlots];
        var createSlots = new VstInsertSlot?[VstInsertChain.MaxSlots];
        var createCount = 0;
        double sampleRate;
        int maxBlock;
        int channels;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var previousOwner = _owner;
            var ownerChanged = !ReferenceEquals(previousOwner, owner);
            _playerBypass = playerBypass;
            _owner = owner;
            sampleRate = _sampleRate;
            maxBlock = _maxBlockSize;
            channels = _channels;

            // プレイヤー: チェーンメタだけ保持し、ライブは破棄せずオーナーへ退避。
            if (playerBypass)
            {
                if (owner is not null)
                {
                    ParkAllLiveNoLock(owner, toDispose);
                }
                else
                {
                    CollectUnloadAllNoLock(toDispose);
                }

                ApplyPlayerMetaNoLock(chain);
            }
            else if (chain is null || chain.IsEmpty)
            {
                if (ownerChanged && previousOwner is not null)
                {
                    ParkAllLiveNoLock(previousOwner, toDispose);
                }
                else
                {
                    CollectUnloadAllNoLock(toDispose);
                }

                _applied = chain;
                _structuralKey = chain?.StructuralKey ?? string.Empty;
            }
            else
            {
                var key = chain.StructuralKey;
                if (string.Equals(key, _structuralKey, StringComparison.Ordinal) && _applied is not null)
                {
                    // 構造同じ → state／bypass のみ。バイパス解除で未生成ならここで遅延ロードを積む。
                    ApplyParametersOnlyNoLock(chain);
                    CollectDeferredCreatesNoLock(chain, createAt, createSlots, ref createCount);
                    _applied = chain;
                }
                else
                {
                    for (var i = 0; i < VstInsertChain.MaxSlots; i++)
                    {
                        var want = chain.Slots[i];
                        var have = _live[i];

                        if (want.IsEmpty)
                        {
                            if (ownerChanged && previousOwner is not null)
                            {
                                ParkSlotNoLock(previousOwner, i, toDispose);
                            }
                            else
                            {
                                DetachSlotNoLock(i, toDispose);
                            }

                            _bypass[i] = false;
                            _faulted[i] = false;
                            _uiSuspended[i] = false;
                            continue;
                        }

                        // 同一プラグインならバイパスの違いだけでは外さない。
                        if (IsSamePlugin(have, want))
                        {
                            ApplyStateNoLock(i, have!, want.StateBlob);
                            _faulted[i] = have!.Faulted;
                            want.Faulted = have.Faulted;
                            _bypass[i] = want.Bypassed || want.Faulted;
                            continue;
                        }

                        if (have is not null)
                        {
                            if (ownerChanged && previousOwner is not null)
                            {
                                ParkSlotNoLock(previousOwner, i, toDispose);
                            }
                            else
                            {
                                DetachSlotNoLock(i, toDispose);
                            }
                        }

                        // 退避から復帰できれば新規ロードしない。
                        if (TryRestoreParkedNoLock(owner, i, want))
                        {
                            continue;
                        }

                        // 別プラグイン／新規。バイパス中はまだ作らない（表示名はチェーン側）。
                        if (want.Bypassed)
                        {
                            _bypass[i] = true;
                            _faulted[i] = false;
                            want.Faulted = false;
                            _uiSuspended[i] = false;
                            continue;
                        }

                        createAt[createCount] = i;
                        createSlots[createCount] = want;
                        createCount++;
                        _bypass[i] = true;
                        _faulted[i] = false;
                        _uiSuspended[i] = false;
                    }

                    _structuralKey = key;
                    _applied = chain;
                }
            }
        }

        // ExclusiveLoad 中（Process 停止・同一 STA）は同期 Dispose。
        // Soft Limit を MTA で Dispose しつつ Altiverb を UI で create すると COM デッドロックする。
        // Exclusive 以外は遅延破棄。
        var restoreBypass = false;
        var forcedBypass = false;
        if (toDispose.Count > 0 || createCount > 0)
        {
            bool exclusive;
            lock (_gate)
            {
                restoreBypass = _playerBypass;
                _playerBypass = true;
                forcedBypass = true;
                exclusive = _exclusiveLoad;
            }

            if (exclusive)
            {
                DisposeQuiet(toDispose);
            }
            else
            {
                ScheduleDispose(toDispose);
            }

            toDispose.Clear();
        }

        try
        {
            if (createCount == 0)
            {
                return;
            }

            var created = new IVstSlotProcessor?[createCount];
            for (var n = 0; n < createCount; n++)
            {
                created[n] = TryCreate(createSlots[n]!, sampleRate, maxBlock, channels);
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    ScheduleDispose(created);
                    return;
                }

                for (var n = 0; n < createCount; n++)
                {
                    var i = createAt[n];
                    var want = createSlots[n]!;
                    if (_applied is null
                        || want.IsEmpty
                        || !_applied.Slots[i].StructuralEquals(want))
                    {
                        if (created[n] is not null)
                        {
                            ScheduleDispose(new[] { created[n]! });
                        }

                        created[n] = null;
                        continue;
                    }

                    var instance = created[n];
                    _live[i] = instance;
                    _reloadCount++;
                    if (instance is null || instance.Faulted)
                    {
                        _faulted[i] = true;
                        want.Faulted = true;
                        _applied.Slots[i].Faulted = true;
                        _bypass[i] = true;
                        if (instance is { Faulted: true })
                        {
                            _live[i] = null;
                            ScheduleDispose(new[] { instance });
                        }
                    }
                    else
                    {
                        _faulted[i] = false;
                        want.Faulted = false;
                        _applied.Slots[i].Faulted = false;
                        ApplyStateNoLock(i, instance, want.StateBlob);
                        _bypass[i] = want.Bypassed;
                    }
                }

                // create 後に ClassId が埋まることがあるので、キーを最新化する。
                _structuralKey = _applied?.StructuralKey ?? string.Empty;
            }
        }
        finally
        {
            if (forcedBypass)
            {
                lock (_gate)
                {
                    _playerBypass = restoreBypass;
                }
            }
        }
    }

    /// <summary>
    /// パス一致、かつ ClassId が両方あるときだけ厳密一致。片方が空なら同一とみなす。
    /// </summary>
    private static bool IsSamePlugin(IVstSlotProcessor? have, VstInsertSlot want)
    {
        if (have is null || want.IsEmpty)
        {
            return false;
        }

        if (!string.Equals(have.PluginPath, want.PluginPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var haveId = have.ClassId ?? string.Empty;
        var wantId = want.ClassId ?? string.Empty;
        if (haveId.Length == 0 || wantId.Length == 0)
        {
            return true;
        }

        return string.Equals(haveId, wantId, StringComparison.Ordinal);
    }

    public void Forget(object? owner)
    {
        List<IVstSlotProcessor>? toDispose = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            toDispose = [];
            if (owner is not null && !ReferenceEquals(_owner, owner))
            {
                // 非アクティブ文書の閉じる／削除: 退避分だけ破棄。
                CollectParkedOwnerNoLock(owner, toDispose);
            }
            else
            {
                CollectUnloadAllNoLock(toDispose);
                if (owner is not null)
                {
                    CollectParkedOwnerNoLock(owner, toDispose);
                }
                else
                {
                    CollectAllParkedNoLock(toDispose);
                }

                _owner = null;
                _applied = null;
                _structuralKey = string.Empty;
            }
        }

        if (toDispose is { Count: > 0 })
        {
            ScheduleDispose(toDispose);
        }
    }

    /// <summary>差し替え中など、Process を完全に止める（待ちはしない）。</summary>
    public void BeginExclusiveLoad()
    {
        lock (_gate)
        {
            _exclusiveLoad = true;
        }
    }

    public void EndExclusiveLoad()
    {
        lock (_gate)
        {
            _exclusiveLoad = false;
        }
    }

    /// <summary>
    /// process 利用者ゼロを待つ。UI 同期コンテキスト上では Task.Delay でメッセージを捌く。
    /// Thread.Sleep は使わない（プラグインの SendMessage 待ちとデッドロックするため）。
    /// </summary>
    public Task WaitForProcessUsersAsync(int timeoutMs) => WaitForProcessUsersAsyncCore(timeoutMs);

    /// <summary>
    /// 差し替えなど、一時的にそのスロットの process だけ止める（ユーザー bypass とは別）。
    /// GUI を開くだけなら使わない（wet を維持する）。
    /// </summary>
    public void SetSlotSuspended(int index, bool suspended)
    {
        lock (_gate)
        {
            if ((uint)index >= (uint)_uiSuspended.Length)
            {
                return;
            }

            _uiSuspended[index] = suspended;
        }
    }

    public void CaptureStatesInto(VstInsertChain chain) => CaptureStatesOutsideEngineLock(chain);

    /// <returns>state を書き込んだ適用中チェーン。無ければ null。</returns>
    public VstInsertChain? CaptureAppliedStates()
    {
        VstInsertChain? applied;
        lock (_gate)
        {
            applied = _applied;
        }

        CaptureStatesOutsideEngineLock(applied);
        return applied;
    }

    /// <summary>
    /// CaptureState は instance._gate を取る。engine._gate 保持中に呼ぶと
    /// Process の fault 経路（instance→engine）とデッドロックする。
    /// </summary>
    private void CaptureStatesOutsideEngineLock(VstInsertChain? chain)
    {
        if (chain is null)
        {
            return;
        }

        var snapshot = new IVstSlotProcessor?[VstInsertChain.MaxSlots];
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            for (var i = 0; i < VstInsertChain.MaxSlots; i++)
            {
                if (!chain.Slots[i].IsEmpty)
                {
                    snapshot[i] = _live[i];
                }
            }
        }

        var blobs = new byte[]?[VstInsertChain.MaxSlots];
        for (var i = 0; i < snapshot.Length; i++)
        {
            var live = snapshot[i];
            if (live is null)
            {
                continue;
            }

            try
            {
                blobs[i] = live.CaptureState();
            }
            catch
            {
            }
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            for (var i = 0; i < blobs.Length; i++)
            {
                if (blobs[i] is null || !ReferenceEquals(_live[i], snapshot[i]))
                {
                    continue;
                }

                if (!chain.Slots[i].IsEmpty)
                {
                    chain.Slots[i].StateBlob = blobs[i];
                }
            }
        }
    }

    public void Process(
        float[] buffer,
        int offset,
        int sampleCount,
        int channels,
        VstTransport? transport = null)
    {
        if (sampleCount <= 0 || channels <= 0)
        {
            return;
        }

        Interlocked.Increment(ref _processUsers);
        try
        {
            lock (_gate)
            {
                if (_disposed || _playerBypass || _exclusiveLoad)
                {
                    return;
                }
            }

            ProcessActiveSlots(buffer, offset, sampleCount, channels, transport ?? VstTransport.FreeRunning);
        }
        finally
        {
            Interlocked.Decrement(ref _processUsers);
        }
    }

    /// <summary>テール検出の無音しきい値（dBFS）。残響がこれ未満で Hold 分続けば打ち切る。</summary>
    public const double OfflineTailThresholdDb = -90;

    /// <summary>しきい値未満がこの時間続けばテール終了とみなす。</summary>
    public const int OfflineTailHoldMs = 100;

    /// <summary>テール延長の上限（秒）。無限残響プラグインへの保険。</summary>
    public const double OfflineTailMaxSeconds = 60;

    /// <summary>
    /// 初回 process が追いつかず付く先頭無音の補正上限（秒）。
    /// getLatencySamples とは別。これを超える遅れは意図したディレイとみなし動かさない。
    /// </summary>
    public const double OfflineStartupMaxSeconds = 5;

    /// <summary>
    /// 再生コールバックを止めつつ、現在のライブチェーンで interleaved を処理する。
    /// バイパス／fault スロットは通さない。呼び出し側で再生停止と CaptureState を済ませること。
    /// 直前タブの wet 残留を落とすため、処理前に DSP リセット＋短い無音フラッシュを行う
    /// （ライブ再生の温存経路は通らない）。
    /// <paramref name="includeTail"/> が true のときは無音を足して残響を鳴らし切り、
    /// 元配列より長い新しい配列を返す（元配列は変更しない）。false ならその場処理して同じ配列を返す。
    /// </summary>
    public async Task<float[]> ProcessOfflineAsync(
        float[] interleaved,
        int channels,
        IProgress<double>? progress = null,
        bool includeTail = false,
        CancellationToken cancellationToken = default)
    {
        channels = Math.Max(1, channels);
        if (interleaved.Length < channels)
        {
            progress?.Report(1);
            return interleaved;
        }

        BeginExclusiveLoad();
        try
        {
            await WaitForProcessUsersAsync(2000).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            int maxBlock;
            double sampleRate;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_playerBypass)
                {
                    progress?.Report(1);
                    return interleaved;
                }

                maxBlock = Math.Max(16, _maxBlockSize);
                sampleRate = _sampleRate > 0 ? _sampleRate : 48000;
            }

            // 共有ライブのまま直列バウンスするため、ファイルごとに残留を捨てる。
            PrepareLiveSlotsForOffline(channels, maxBlock);
            cancellationToken.ThrowIfCancellationRequested();
            var latency = QueryActiveLatencyFrames(sampleRate);
            var totalFrames = interleaved.Length / channels;
            var onsetThreshold = SilentSkip.LinearFromDb(OfflineTailThresholdDb);
            var sourceOnset = VstOfflineStartupGap.FindOnset(
                interleaved,
                frameOffset: 0,
                totalFrames,
                channels,
                onsetThreshold);

            if (!includeTail)
            {
                var frames = totalFrames;
                ProcessOfflineRange(
                    interleaved,
                    frameOffset: 0,
                    frameCount: frames,
                    channels,
                    maxBlock,
                    progress,
                    progressStart: 0,
                    progressEnd: 1,
                    cancellationToken);
                latency = Math.Max(latency, QueryActiveLatencyFrames(sampleRate));
                CompensateLatency(interleaved, 0, frames, channels, latency, maxBlock);
                CompensateStartupSilence(
                    interleaved,
                    sourceOnset,
                    0,
                    frames,
                    channels,
                    maxBlock,
                    sampleRate,
                    onsetThreshold);
                progress?.Report(1);
                return interleaved;
            }

            var sourceFrames = interleaved.Length / channels;
            var maxTailFrames = Math.Max(
                maxBlock,
                (int)Math.Ceiling(sampleRate * OfflineTailMaxSeconds));
            var holdFrames = Math.Max(
                1,
                (int)Math.Ceiling(sampleRate * OfflineTailHoldMs / 1000.0));
            var threshold = SilentSkip.LinearFromDb(OfflineTailThresholdDb);

            // 本体はその場用コピー。テールはブロック単位で足してから結合する。
            var body = (float[])interleaved.Clone();
            ProcessOfflineRange(
                body,
                frameOffset: 0,
                frameCount: sourceFrames,
                channels,
                maxBlock,
                progress,
                progressStart: 0,
                progressEnd: 0.85,
                cancellationToken);
            latency = Math.Max(latency, QueryActiveLatencyFrames(sampleRate));
            CompensateLatency(body, 0, sourceFrames, channels, latency, maxBlock);
            CompensateStartupSilence(
                body,
                sourceOnset,
                0,
                sourceFrames,
                channels,
                maxBlock,
                sampleRate,
                onsetThreshold);

            var silence = new float[maxBlock * channels];
            // テールは可変長。無音ホールド分も含めて溜め、最後に可聴末尾へ切り詰める。
            var tail = new float[Math.Min(maxTailFrames, maxBlock * 8) * channels];
            var tailFrames = 0;
            var lastAudibleTailFrame = -1;
            var quietRun = 0;
            var tailFramesDone = 0;
            var tailDone = false;
            while (!tailDone && tailFramesDone < maxTailFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var n = Math.Min(maxBlock, maxTailFrames - tailFramesDone);
                Array.Clear(silence, 0, n * channels);

                Interlocked.Increment(ref _processUsers);
                try
                {
                    ProcessActiveSlots(silence, 0, n * channels, channels, VstTransport.FreeRunning);
                }
                finally
                {
                    Interlocked.Decrement(ref _processUsers);
                }

                EnsureTailCapacity(ref tail, (tailFrames + n) * channels);
                Array.Copy(silence, 0, tail, tailFrames * channels, n * channels);

                for (var i = 0; i < n; i++)
                {
                    var peak = PeakAbsInBlock(silence, i, channels);
                    if (peak >= threshold)
                    {
                        quietRun = 0;
                        lastAudibleTailFrame = tailFrames + i;
                        continue;
                    }

                    quietRun++;
                    if (quietRun >= holdFrames)
                    {
                        tailDone = true;
                        // ホールド突入以降は捨てるので、ここまでのフレーム数は後で切り詰める。
                        break;
                    }
                }

                tailFrames += n;
                tailFramesDone += n;
                var tailRatio = tailFramesDone / (double)maxTailFrames;
                progress?.Report(0.85 + (0.15 * Math.Clamp(tailRatio, 0, 1)));
            }

            var keepTailFrames = lastAudibleTailFrame + 1;
            if (keepTailFrames <= 0)
            {
                progress?.Report(1);
                return body;
            }

            var combined = new float[checked((sourceFrames + keepTailFrames) * channels)];
            Array.Copy(body, 0, combined, 0, body.Length);
            Array.Copy(tail, 0, combined, body.Length, keepTailFrames * channels);
            progress?.Report(1);
            return combined;
        }
        finally
        {
            EndExclusiveLoad();
        }
    }

    private static void EnsureTailCapacity(ref float[] tail, int neededSamples)
    {
        if (neededSamples <= tail.Length)
        {
            return;
        }

        var next = tail.Length;
        while (next < neededSamples)
        {
            next = checked(next * 2);
        }

        Array.Resize(ref tail, next);
    }

    private void ProcessOfflineRange(
        float[] interleaved,
        int frameOffset,
        int frameCount,
        int channels,
        int maxBlock,
        IProgress<double>? progress,
        double progressStart,
        double progressEnd,
        CancellationToken cancellationToken)
    {
        if (frameCount <= 0)
        {
            return;
        }

        var block = Math.Max(16, maxBlock);
        var span = Math.Max(1e-9, progressEnd - progressStart);
        for (var i = 0; i < frameCount; i += block)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var n = Math.Min(block, frameCount - i);
            var frame = frameOffset + i;
            Interlocked.Increment(ref _processUsers);
            try
            {
                ProcessActiveSlots(interleaved, frame * channels, n * channels, channels, VstTransport.FreeRunning);
            }
            finally
            {
                Interlocked.Decrement(ref _processUsers);
            }

            var ratio = (i + n) / (double)frameCount;
            progress?.Report(progressStart + (span * Math.Clamp(ratio, 0, 1)));
        }
    }

    private static float PeakAbsInBlock(float[] interleaved, int frameInBlock, int channels)
    {
        var offset = frameInBlock * channels;
        var peak = 0f;
        for (var ch = 0; ch < channels; ch++)
        {
            var abs = Math.Abs(interleaved[offset + ch]);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        return peak;
    }

    /// <summary>
    /// オフライン直前専用。ライブ Process は呼ばない。
    /// setProcessing トグル後、約 0.5 秒の無音を捨てバッファへ通して残響を吐き切る。
    /// </summary>
    private void PrepareLiveSlotsForOffline(int channels, int maxBlock)
    {
        channels = Math.Max(1, channels);
        maxBlock = Math.Max(16, maxBlock);

        var toReset = new List<IVstSlotProcessor>(VstInsertChain.MaxSlots);
        int flushFrames;
        lock (_gate)
        {
            if (_disposed || _playerBypass)
            {
                return;
            }

            for (var i = 0; i < _live.Length; i++)
            {
                if (_uiSuspended[i] || _bypass[i] || _faulted[i] || _live[i] is null)
                {
                    continue;
                }

                toReset.Add(_live[i]!);
            }

            if (toReset.Count == 0)
            {
                return;
            }

            flushFrames = Math.Max(maxBlock * 2, (int)Math.Ceiling(_sampleRate * 0.5));
        }

        foreach (var live in toReset)
        {
            try
            {
                live.ResetProcessing();
            }
            catch
            {
            }
        }

        var silence = new float[maxBlock * channels];
        var remaining = flushFrames;
        while (remaining > 0)
        {
            var n = Math.Min(maxBlock, remaining);
            Array.Clear(silence, 0, n * channels);
            Interlocked.Increment(ref _processUsers);
            try
            {
                ProcessActiveSlots(silence, 0, n * channels, channels, OfflinePrimeTransport);
            }
            finally
            {
                Interlocked.Decrement(ref _processUsers);
            }

            remaining -= n;
        }

        ResetActiveTransportClocks();
    }

    /// <summary>残留フラッシュは無音なので、プロジェクト時刻を進めずファイル頭の 0 に留める。</summary>
    private static readonly VstTransport OfflinePrimeTransport = VstTransport.Live(false, 0);

    private void ResetActiveTransportClocks()
    {
        IVstSlotProcessor?[] slots;
        lock (_gate)
        {
            slots = SnapshotActiveSlotsNoLock();
        }

        foreach (var live in slots)
        {
            if (live is null)
            {
                continue;
            }

            try
            {
                live.ResetTransportClock();
            }
            catch
            {
            }
        }
    }

    private int QueryActiveLatencyFrames(double sampleRate)
    {
        IVstSlotProcessor?[] slots;
        lock (_gate)
        {
            slots = SnapshotActiveSlotsNoLock();
        }

        long sum = 0;
        foreach (var live in slots)
        {
            if (live is null)
            {
                continue;
            }

            try
            {
                sum += Math.Max(0, live.LatencySamples);
            }
            catch
            {
            }
        }

        var cap = (long)Math.Ceiling(Math.Max(1, sampleRate) * OfflineTailMaxSeconds);
        return (int)Math.Min(Math.Max(0, sum), cap);
    }

    private IVstSlotProcessor?[] SnapshotActiveSlotsNoLock()
    {
        var slots = new IVstSlotProcessor?[_live.Length];
        for (var i = 0; i < _live.Length; i++)
        {
            if (_uiSuspended[i] || _bypass[i] || _faulted[i] || _live[i] is null)
            {
                continue;
            }

            slots[i] = _live[i];
        }

        return slots;
    }

    /// <summary>
    /// getLatencySamples 分だけ出力が遅れているので、先頭の無音を捨て、
    /// 末尾に残った同じ長さを無音入力で回収して元の位置へ戻す。
    /// </summary>
    private void CompensateLatency(
        float[] interleaved,
        int frameOffset,
        int frameCount,
        int channels,
        int latencyFrames,
        int maxBlock)
    {
        if (latencyFrames <= 0 || frameCount <= 0)
        {
            return;
        }

        var recover = new float[latencyFrames * channels];
        ProcessSilenceFrames(recover, latencyFrames, channels, maxBlock);
        var dest = frameOffset * channels;
        var count = frameCount * channels;
        if (latencyFrames < frameCount)
        {
            var skip = latencyFrames * channels;
            Array.Copy(interleaved, dest + skip, interleaved, dest, count - skip);
            Array.Copy(recover, 0, interleaved, dest + count - skip, skip);
            return;
        }

        Array.Copy(recover, (latencyFrames - frameCount) * channels, interleaved, dest, count);
    }

    /// <summary>
    /// レイテンシー補正のあと、入力の出だしより出力が遅れている無音だけ戻す。
    /// プラグインが初回ブロックを無音で埋めた分で、getLatencySamples には含まれない。
    /// </summary>
    private void CompensateStartupSilence(
        float[] processed,
        int sourceOnset,
        int frameOffset,
        int frameCount,
        int channels,
        int maxBlock,
        double sampleRate,
        float threshold)
    {
        if (sourceOnset < 0 || frameCount <= 0 || channels <= 0)
        {
            return;
        }

        var maxExtra = (int)Math.Min(
            int.MaxValue / 4,
            Math.Ceiling(Math.Max(1, sampleRate) * OfflineStartupMaxSeconds));
        if (maxExtra <= 0)
        {
            return;
        }

        var scan = Math.Min(frameCount, sourceOnset + maxExtra + 1);
        var outputOnset = VstOfflineStartupGap.FindOnset(
            processed,
            frameOffset,
            scan,
            channels,
            threshold);
        if (outputOnset >= 0)
        {
            var extra = VstOfflineStartupGap.ExtraFrames(sourceOnset, outputOnset, maxExtra);
            if (extra > 0)
            {
                CompensateLatency(processed, frameOffset, frameCount, channels, extra, maxBlock);
            }

            return;
        }

        if (scan < frameCount || frameCount > maxExtra)
        {
            return;
        }

        RecoverStartupFromSilence(
            processed,
            frameOffset,
            frameCount,
            channels,
            maxBlock,
            sourceOnset,
            maxExtra,
            threshold);
    }

    /// <summary>
    /// 本体が丸ごと無音のとき、遅れがファイルより長い。無音を足して出だしを探し、キャップ内なら戻す。
    /// </summary>
    private void RecoverStartupFromSilence(
        float[] processed,
        int frameOffset,
        int frameCount,
        int channels,
        int maxBlock,
        int sourceOnset,
        int maxExtra,
        float threshold)
    {
        var probe = new float[maxExtra * channels];
        var produced = 0;
        var onset = -1;
        var block = Math.Max(16, maxBlock);
        var need = 0;
        while (produced < maxExtra)
        {
            var n = Math.Min(block, maxExtra - produced);
            Interlocked.Increment(ref _processUsers);
            try
            {
                ProcessActiveSlots(probe, produced * channels, n * channels, channels, VstTransport.FreeRunning);
            }
            finally
            {
                Interlocked.Decrement(ref _processUsers);
            }

            if (onset < 0)
            {
                var found = VstOfflineStartupGap.FindOnset(probe, produced, n, channels, threshold);
                if (found >= 0)
                {
                    onset = produced + found;
                    var extra = frameCount + onset - sourceOnset;
                    if (extra <= 0 || extra > maxExtra)
                    {
                        return;
                    }

                    need = extra;
                }
            }

            produced += n;
            if (onset >= 0 && produced >= need)
            {
                break;
            }
        }

        if (onset < 0)
        {
            return;
        }

        var shift = frameCount + onset - sourceOnset;
        if (shift <= 0 || shift > produced)
        {
            return;
        }

        for (var i = 0; i < frameCount; i++)
        {
            var srcFrame = i + shift;
            if (srcFrame < frameCount)
            {
                continue;
            }

            var probeFrame = srcFrame - frameCount;
            if (probeFrame >= produced)
            {
                return;
            }

            Array.Copy(
                probe,
                probeFrame * channels,
                processed,
                (frameOffset + i) * channels,
                channels);
        }
    }

    private void ProcessSilenceFrames(float[] destination, int frameCount, int channels, int maxBlock)
    {
        var block = Math.Max(16, maxBlock);
        for (var i = 0; i < frameCount; i += block)
        {
            var n = Math.Min(block, frameCount - i);
            Interlocked.Increment(ref _processUsers);
            try
            {
                ProcessActiveSlots(destination, i * channels, n * channels, channels, VstTransport.FreeRunning);
            }
            finally
            {
                Interlocked.Decrement(ref _processUsers);
            }
        }
    }

    private void ProcessActiveSlots(
        float[] buffer,
        int offset,
        int sampleCount,
        int channels,
        VstTransport transport)
    {
        int liveCount;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // _uiSuspended は差し替え用。GUI 表示だけでは立てない。
            liveCount = 0;
            for (var i = 0; i < _live.Length; i++)
            {
                if (_uiSuspended[i] || _bypass[i] || _faulted[i] || _live[i] is null)
                {
                    _processScratch[i] = null;
                    continue;
                }

                _processScratch[i] = _live[i];
                liveCount++;
            }

            if (liveCount == 0)
            {
                return;
            }
        }

        // ロック外で DSP。Apply の差し替えが待たされない。
        for (var i = 0; i < _processScratch.Length; i++)
        {
            var live = _processScratch[i];
            _processScratch[i] = null;
            if (live is null)
            {
                continue;
            }

            try
            {
                live.ApplyTransport(transport);
                if (!live.Process(buffer, offset, sampleCount, channels))
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_live[i], live))
                        {
                            MarkFaultedNoLock(i);
                        }
                    }
                }
            }
            catch
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_live[i], live))
                    {
                        MarkFaultedNoLock(i);
                    }
                }
            }
        }
    }

    public IVstSlotProcessor? TryGetLive(int index)
    {
        lock (_gate)
        {
            return (uint)index < (uint)_live.Length ? _live[index] : null;
        }
    }

    /// <summary>
    /// バイパス中のライブをライブ経路から外す。
    /// オーナーがあれば park（Undo／バイパス解除で再生成せず復帰）。無ければ破棄。
    /// バウンス直後など、オフラインで触ったインスタンスをライブ＋バイパスのまま残さないため
    /// （その組み合わせはタブ閉じの Capture／Dispose で落ちることがある）。
    /// park 前に ResetProcessing し、復帰後の再生頭が残響末尾にならないようにする。
    /// </summary>
    public void UnloadBypassedLiveSlots()
    {
        var toPark = new List<(int Index, IVstSlotProcessor Live)>(VstInsertChain.MaxSlots);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (var i = 0; i < _live.Length; i++)
            {
                if (!_bypass[i] || _live[i] is null)
                {
                    continue;
                }

                toPark.Add((i, _live[i]!));
            }
        }

        if (toPark.Count == 0)
        {
            return;
        }

        foreach (var (_, live) in toPark)
        {
            try
            {
                live.ResetProcessing();
            }
            catch
            {
            }
        }

        List<IVstSlotProcessor>? toDispose = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var owner = _owner;
            foreach (var (index, live) in toPark)
            {
                if (!ReferenceEquals(_live[index], live) || !_bypass[index])
                {
                    continue;
                }

                toDispose ??= new List<IVstSlotProcessor>(VstInsertChain.MaxSlots);
                if (owner is not null)
                {
                    ParkSlotNoLock(owner, index, toDispose);
                }
                else
                {
                    DetachSlotNoLock(index, toDispose);
                }
            }
        }

        if (toDispose is not null)
        {
            ScheduleDispose(toDispose);
        }
    }

    /// <summary>
    /// バイパス deferred で未生成のスロットを実体化する（エディタ表示用）。
    /// 既に載っていれば何もしない。バイパスフラグはそのまま。
    /// プレイヤーモード中はロードしない。
    /// </summary>
    public void EnsureSlotLoaded(int index)
    {
        VstInsertSlot? want = null;
        double sampleRate;
        int maxBlock;
        int channels;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_playerBypass
                || (uint)index >= (uint)_live.Length
                || _live[index] is not null
                || _applied is null)
            {
                return;
            }

            want = _applied.Slots[index];
            if (want.IsEmpty)
            {
                return;
            }

            if (TryRestoreParkedNoLock(_owner, index, want))
            {
                return;
            }

            sampleRate = _sampleRate;
            maxBlock = _maxBlockSize;
            channels = _channels;
        }

        var instance = TryCreate(want, sampleRate, maxBlock, channels);
        lock (_gate)
        {
            if (_disposed
                || _applied is null
                || !_applied.Slots[index].StructuralEquals(want)
                || _live[index] is not null)
            {
                if (instance is not null)
                {
                    ScheduleDispose(new[] { instance });
                }

                return;
            }

            _live[index] = instance;
            _reloadCount++;
            if (instance is null || instance.Faulted)
            {
                _faulted[index] = true;
                want.Faulted = true;
                _applied.Slots[index].Faulted = true;
                _bypass[index] = true;
                if (instance is { Faulted: true })
                {
                    _live[index] = null;
                    ScheduleDispose(new[] { instance });
                }
            }
            else
            {
                _faulted[index] = false;
                want.Faulted = false;
                _applied.Slots[index].Faulted = false;
                ApplyStateNoLock(index, instance, want.StateBlob);
                _bypass[index] = want.Bypassed;
            }
        }
    }

    /// <summary>
    /// ライブプロセッサをスロット間で移す／入れ替える（破棄しない）。
    /// 空きへ移す場合も同じ。呼び出し側でチェーンの MoveSlot を先に済ませ、同じ chain を渡す。
    /// </summary>
    public void MoveSlot(int from, int to, VstInsertChain chain)
    {
        if ((uint)from >= (uint)_live.Length
            || (uint)to >= (uint)_live.Length
            || from == to)
        {
            return;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            (_live[from], _live[to]) = (_live[to], _live[from]);
            (_bypass[from], _bypass[to]) = (_bypass[to], _bypass[from]);
            (_faulted[from], _faulted[to]) = (_faulted[to], _faulted[from]);
            (_uiSuspended[from], _uiSuspended[to]) = (_uiSuspended[to], _uiSuspended[from]);
            _applied = chain;
            _structuralKey = chain.StructuralKey;
            _reloadCount++;
        }
    }

    public void Dispose()
    {
        List<IVstSlotProcessor> toDispose;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            toDispose = [];
            CollectUnloadAllNoLock(toDispose);
            CollectAllParkedNoLock(toDispose);
            _owner = null;
            _applied = null;
        }

        ScheduleDispose(toDispose);
    }

    private void ScheduleDispose(IEnumerable<IVstSlotProcessor?> items)
    {
        var list = items.Where(i => i is not null).Cast<IVstSlotProcessor>().ToList();
        if (list.Count == 0)
        {
            return;
        }

        // 作成した STA（通常は UI）へ戻して破棄。MTA の Task.Run で Dispose すると
        // 次のプラグイン create とデッドロックすることがある。
        var sync = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            await WaitForProcessUsersAsyncCore(2000).ConfigureAwait(false);
            await Task.Delay(30).ConfigureAwait(false);
            if (sync is not null)
            {
                sync.Post(_ => DisposeQuiet(list), null);
            }
            else
            {
                DisposeQuiet(list);
            }
        });
    }

    private async Task WaitForProcessUsersAsyncCore(int timeoutMs)
    {
        var start = Environment.TickCount64;
        while (Volatile.Read(ref _processUsers) > 0
               && Environment.TickCount64 - start < timeoutMs)
        {
            // ConfigureAwait(true): UI に戻ってポンプする
            await Task.Delay(15).ConfigureAwait(true);
        }
    }

    private void ApplyParametersOnlyNoLock(VstInsertChain chain)
    {
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            var want = chain.Slots[i];
            _bypass[i] = want.Bypassed || want.Faulted || _faulted[i];
            var live = _live[i];
            if (live is null || want.IsEmpty)
            {
                continue;
            }

            ApplyStateNoLock(i, live, want.StateBlob);
            want.Faulted = live.Faulted || _faulted[i];
            if (want.Faulted)
            {
                _bypass[i] = true;
            }
        }
    }

    /// <summary>
    /// プレイヤー時: ライブなしのままチェーンメタと bypass／fault フラグだけ同期する。
    /// </summary>
    private void ApplyPlayerMetaNoLock(VstInsertChain? chain)
    {
        _applied = chain;
        _structuralKey = chain?.StructuralKey ?? string.Empty;
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            if (chain is null || chain.Slots[i].IsEmpty)
            {
                _bypass[i] = false;
                _faulted[i] = false;
                _uiSuspended[i] = false;
                continue;
            }

            var want = chain.Slots[i];
            _bypass[i] = want.Bypassed || want.Faulted;
            _faulted[i] = want.Faulted;
            _uiSuspended[i] = false;
        }
    }

    /// <summary>
    /// 構造キー同じ経路で、バイパス解除済みなのに未生成のスロットを create キューへ入れる。
    /// </summary>
    private void CollectDeferredCreatesNoLock(
        VstInsertChain chain,
        int[] createAt,
        VstInsertSlot?[] createSlots,
        ref int createCount)
    {
        if (_playerBypass)
        {
            return;
        }

        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            var want = chain.Slots[i];
            if (want.IsEmpty || want.Bypassed || want.Faulted || _live[i] is not null)
            {
                continue;
            }

            if (TryRestoreParkedNoLock(_owner, i, want))
            {
                continue;
            }

            createAt[createCount] = i;
            createSlots[createCount] = want;
            createCount++;
            _bypass[i] = true;
            _faulted[i] = false;
        }
    }

    private void ApplyStateNoLock(int index, IVstSlotProcessor live, byte[]? state)
    {
        try
        {
            live.ApplyState(state);
            _stateApplyCount++;
        }
        catch
        {
            MarkFaultedNoLock(index);
        }
    }

    private IVstSlotProcessor? TryCreate(
        VstInsertSlot slot,
        double sampleRate,
        int maxBlockSize,
        int channels)
    {
        try
        {
            return _factory.TryCreate(slot, sampleRate, maxBlockSize, channels);
        }
        catch
        {
            return null;
        }
    }

    private void MarkFaultedNoLock(int index)
    {
        _faulted[index] = true;
        _bypass[index] = true;
        if (_applied is not null && (uint)index < (uint)_applied.Slots.Length)
        {
            _applied.Slots[index].Faulted = true;
        }
    }

    private void CollectUnloadAllNoLock(List<IVstSlotProcessor> toDispose)
    {
        for (var i = 0; i < _live.Length; i++)
        {
            DetachSlotNoLock(i, toDispose);
            _bypass[i] = false;
            _faulted[i] = false;
            _uiSuspended[i] = false;
        }
    }

    private void ParkAllLiveNoLock(object owner, List<IVstSlotProcessor> toDispose)
    {
        for (var i = 0; i < _live.Length; i++)
        {
            ParkSlotNoLock(owner, i, toDispose);
            _bypass[i] = false;
            _faulted[i] = false;
            _uiSuspended[i] = false;
        }
    }

    private void ParkSlotNoLock(object owner, int index, List<IVstSlotProcessor> toDispose)
    {
        var live = _live[index];
        _live[index] = null;
        if (live is null)
        {
            return;
        }

        if (!_parked.TryGetValue(owner, out var slots))
        {
            slots = new IVstSlotProcessor?[VstInsertChain.MaxSlots];
            _parked[owner] = slots;
        }

        var existing = slots[index];
        slots[index] = live;
        if (existing is not null && !ReferenceEquals(existing, live))
        {
            toDispose.Add(existing);
        }
    }

    private bool TryRestoreParkedNoLock(object? owner, int index, VstInsertSlot want)
    {
        if (owner is null
            || want.IsEmpty
            || !_parked.TryGetValue(owner, out var slots))
        {
            return false;
        }

        var parked = slots[index];
        if (!IsSamePlugin(parked, want))
        {
            return false;
        }

        slots[index] = null;
        _live[index] = parked;
        ApplyStateNoLock(index, parked!, want.StateBlob);
        _faulted[index] = parked!.Faulted;
        want.Faulted = parked.Faulted;
        _bypass[index] = want.Bypassed || want.Faulted;
        _uiSuspended[index] = false;
        return true;
    }

    private void CollectParkedOwnerNoLock(object owner, List<IVstSlotProcessor> toDispose)
    {
        if (!_parked.Remove(owner, out var slots))
        {
            return;
        }

        for (var i = 0; i < slots.Length; i++)
        {
            var parked = slots[i];
            slots[i] = null;
            if (parked is not null)
            {
                toDispose.Add(parked);
            }
        }
    }

    private void CollectAllParkedNoLock(List<IVstSlotProcessor> toDispose)
    {
        foreach (var owner in _parked.Keys.ToArray())
        {
            CollectParkedOwnerNoLock(owner, toDispose);
        }
    }

    private void DetachSlotNoLock(int index, List<IVstSlotProcessor> toDispose)
    {
        var live = _live[index];
        _live[index] = null;
        if (live is not null)
        {
            toDispose.Add(live);
        }
    }

    private static void DisposeQuiet(IEnumerable<IVstSlotProcessor?> items)
    {
        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            try
            {
                item.Dispose();
            }
            catch
            {
            }
        }
    }
}
