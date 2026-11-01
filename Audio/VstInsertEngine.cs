using System.Linq;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// ライブ 8 スロット。文書切替時は構造キーが同じなら reload せず state／bypass だけ反映。
/// プレイヤーモードでは Process が no-op（ハードバイパス）。
/// Load／Dispose は短時間の書込ロック外。Process は参照スナップショット後に DSP（再生中差し替えで固まらない）。
/// </summary>
internal sealed class VstInsertEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly IVstSlotProcessorFactory _factory;
    private readonly IVstSlotProcessor?[] _live = new IVstSlotProcessor?[VstInsertChain.MaxSlots];
    private readonly bool[] _bypass = new bool[VstInsertChain.MaxSlots];
    private readonly bool[] _faulted = new bool[VstInsertChain.MaxSlots];
    private readonly IVstSlotProcessor?[] _processScratch = new IVstSlotProcessor?[VstInsertChain.MaxSlots];
    private readonly bool[] _bypassScratch = new bool[VstInsertChain.MaxSlots];
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
            _playerBypass = playerBypass;
            _owner = owner;
            sampleRate = _sampleRate;
            maxBlock = _maxBlockSize;
            channels = _channels;

            if (chain is null || chain.IsEmpty)
            {
                CollectUnloadAllNoLock(toDispose);
                _applied = chain;
                _structuralKey = chain?.StructuralKey ?? string.Empty;
            }
            else
            {
                var key = chain.StructuralKey;
                if (string.Equals(key, _structuralKey, StringComparison.Ordinal) && _applied is not null)
                {
                    ApplyParametersOnlyNoLock(chain);
                    _applied = chain;
                    return;
                }

                for (var i = 0; i < VstInsertChain.MaxSlots; i++)
                {
                    var want = chain.Slots[i];
                    var have = _live[i];
                    var same = have is not null
                        && !want.IsEmpty
                        && string.Equals(have.PluginPath, want.PluginPath, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            have.ClassId ?? string.Empty,
                            want.ClassId ?? string.Empty,
                            StringComparison.Ordinal);

                    if (want.IsEmpty)
                    {
                        DetachSlotNoLock(i, toDispose);
                        _bypass[i] = false;
                        _faulted[i] = false;
                        continue;
                    }

                    if (!same)
                    {
                        DetachSlotNoLock(i, toDispose);
                        createAt[createCount] = i;
                        createSlots[createCount] = want;
                        createCount++;
                        _bypass[i] = true;
                        _faulted[i] = false;
                    }
                    else if (have is not null)
                    {
                        ApplyStateNoLock(i, have, want.StateBlob);
                        _faulted[i] = have.Faulted;
                        want.Faulted = have.Faulted;
                        _bypass[i] = want.Bypassed || want.Faulted;
                    }
                }

                _structuralKey = key;
                _applied = chain;
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

    public void Forget(object? owner)
    {
        List<IVstSlotProcessor>? toDispose = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (owner is not null && !ReferenceEquals(_owner, owner))
            {
                return;
            }

            toDispose = [];
            CollectUnloadAllNoLock(toDispose);
            _owner = null;
            _applied = null;
            _structuralKey = string.Empty;
        }

        if (toDispose is not null)
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

    /// <summary>エディタ表示中など、一時的にスロットを通さない。</summary>
    public void SetSlotSuspended(int index, bool suspended)
    {
        lock (_gate)
        {
            if ((uint)index >= (uint)_bypass.Length)
            {
                return;
            }

            if (suspended)
            {
                _bypass[index] = true;
            }
            else if (_applied is not null)
            {
                var want = _applied.Slots[index];
                _bypass[index] = want.Bypassed || want.Faulted || _faulted[index] || _live[index] is null;
            }
        }
    }

    public void CaptureStatesInto(VstInsertChain chain) => CaptureStatesOutsideEngineLock(chain);

    public void CaptureAppliedStates()
    {
        VstInsertChain? applied;
        lock (_gate)
        {
            applied = _applied;
        }

        CaptureStatesOutsideEngineLock(applied);
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

    public void Process(float[] buffer, int offset, int sampleCount, int channels)
    {
        if (sampleCount <= 0 || channels <= 0)
        {
            return;
        }

        Interlocked.Increment(ref _processUsers);
        try
        {
            int liveCount;
            lock (_gate)
            {
                if (_disposed || _playerBypass || _exclusiveLoad)
                {
                    return;
                }

                liveCount = 0;
                for (var i = 0; i < _live.Length; i++)
                {
                    if (_bypass[i] || _faulted[i] || _live[i] is null)
                    {
                        _processScratch[i] = null;
                        continue;
                    }

                    _processScratch[i] = _live[i];
                    _bypassScratch[i] = false;
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
        finally
        {
            Interlocked.Decrement(ref _processUsers);
        }
    }

    public bool IsSlotBypassed(int index)
    {
        lock (_gate)
        {
            return (uint)index < (uint)_bypass.Length && _bypass[index];
        }
    }

    public IVstSlotProcessor? TryGetLive(int index)
    {
        lock (_gate)
        {
            return (uint)index < (uint)_live.Length ? _live[index] : null;
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
