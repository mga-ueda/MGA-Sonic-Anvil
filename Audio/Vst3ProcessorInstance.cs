using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MgaSonicAnvil.Audio;

/// <summary>1 プラグインインスタンス。失敗時は Faulted になり process は通さない。</summary>
internal sealed class Vst3ProcessorInstance : IVstSlotProcessor
{
    private readonly object _gate = new();
    private readonly Vst3Module _module;
    private readonly Vst3HostApplication _host;
    private readonly Vst3ComponentHandler _componentHandler = new();
    private readonly Vst3ParameterChanges _inputParamChanges = new();
    private readonly Vst3ParameterChanges _outputParamChanges = new();
    private readonly List<(uint Id, double Value)> _outputParamScratch = new();
    private IntPtr _component;
    private IntPtr _processor;
    private IntPtr _controller;
    private bool _controllerOwned;
    private bool _controllerReady;
    private IntPtr _plugView;
    private double _sampleRate = 48000;
    private int _maxBlock = 512;
    private int _channels = 2;
    private int _pluginChannels = 2;
    private int _pluginInChannels = 2;
    private int _pluginOutChannels = 2;
    private bool _active;
    private bool _disposed;
    private bool _faulted;
    private bool _editorOpening;
    private byte[]? _lastState;
    private float[][] _planarIn = [];
    private float[][] _planarOut = [];
    private IntPtr[] _inPtrs = [];
    private IntPtr[] _outPtrs = [];
    private GCHandle _inPtrsHandle;
    private GCHandle _outPtrsHandle;
    private IntPtr _busIn = IntPtr.Zero;
    private IntPtr _busOut = IntPtr.Zero;
    /// <summary>ProcessData.inputs / outputs 用。バス数分の AudioBusBuffers 連続領域。</summary>
    private IntPtr _inputsBusArray = IntPtr.Zero;
    private IntPtr _outputsBusArray = IntPtr.Zero;
    private int _numInputBuses = 1;
    private int _numOutputBuses = 1;
    private IntPtr _processContext = IntPtr.Zero;
    private long _projectTimeSamples;
    private long _continuousTimeSamples;

    private Vst3ProcessorInstance(
        Vst3Module module,
        Vst3HostApplication host,
        IntPtr component,
        IntPtr processor,
        string pluginPath,
        string? classId,
        string displayName,
        double sampleRate,
        int maxBlock,
        int channels)
    {
        _module = module;
        _host = host;
        _component = component;
        _processor = processor;
        PluginPath = pluginPath;
        ClassId = classId;
        DisplayName = displayName;
        _componentHandler.ParameterEdited = (id, value) => _inputParamChanges.QueueEdit(id, value);
        Reconfigure(sampleRate, maxBlock, channels);
    }

    public string PluginPath { get; }

    public string? ClassId { get; }

    public string DisplayName { get; }

    /// <summary>セットアップ後のプラグイン主バス入力チャンネル数。</summary>
    public int PluginInChannels
    {
        get
        {
            lock (_gate)
            {
                return _pluginInChannels;
            }
        }
    }

    /// <summary>セットアップ後のプラグイン主バス出力チャンネル数。</summary>
    public int PluginOutChannels
    {
        get
        {
            lock (_gate)
            {
                return _pluginOutChannels;
            }
        }
    }

    public bool Faulted
    {
        get
        {
            lock (_gate)
            {
                return _faulted;
            }
        }
    }

    /// <summary>直近の TryCreate 失敗理由（診断用）。成功時は null。</summary>
    internal static string? LastCreateFailure { get; private set; }

    /// <summary>直近の TryOpenEditor 失敗理由（診断用）。成功時は null。</summary>
    internal static string? LastEditorFailure { get; private set; }

    public static Vst3ProcessorInstance? TryCreate(
        VstInsertSlot slot,
        double sampleRate,
        int maxBlockSize,
        int channels)
    {
        LastCreateFailure = null;
        if (slot.IsEmpty || string.IsNullOrWhiteSpace(slot.PluginPath))
        {
            LastCreateFailure = "empty-slot";
            return null;
        }

        var module = Vst3Module.TryAcquire(slot.PluginPath);
        if (module is null)
        {
            LastCreateFailure = "module-acquire:" + (Vst3Module.LastAcquireFailure ?? "?");
            return null;
        }

        Vst3HostApplication? host = null;
        IntPtr component = IntPtr.Zero;
        IntPtr processor = IntPtr.Zero;
        try
        {
            if (!module.TryResolveClassId(slot.ClassId, slot.DisplayName, out var cid, out var info))
            {
                LastCreateFailure = "resolve-class";
                return null;
            }

            // IAudioProcessor で create すると initialize の vtable がずれる。
            // IComponent → FUnknown の順。FUnknown は IComponent と同一ポインタのことが多い。
            component = module.CreateInstance(cid, Vst3HostNative.IComponentIid);
            if (component == IntPtr.Zero)
            {
                component = module.CreateInstance(cid, Vst3HostNative.FUnknownIid);
            }

            if (component == IntPtr.Zero)
            {
                LastCreateFailure = "create-instance";
                return null;
            }

            if (Vst3HostNative.QueryInterface(component, Vst3HostNative.IComponentIid, out var asComp)
                == Vst3HostNative.KResultOk
                && asComp != IntPtr.Zero
                && asComp != component)
            {
                Vst3HostNative.Release(component);
                component = asComp;
            }
            else if (asComp != IntPtr.Zero && asComp == component)
            {
                // QI が同一ポインタを AddRef しただけなので 1 回戻す
                Vst3HostNative.Release(asComp);
            }

            host = new Vst3HostApplication();
            var initResult = InitializeComponentResult(component, host.ComPtr);
            if (initResult != Vst3HostNative.KResultOk)
            {
                LastCreateFailure = "initialize:" + initResult.ToString("X8");
                return null;
            }

            var procQi = Vst3HostNative.QueryInterface(
                component,
                Vst3HostNative.IAudioProcessorIid,
                out processor);
            if (processor == IntPtr.Zero)
            {
                // Soft Limit 等: QI が kResultFalse でも createInstance(IAudioProcessor) は通る
                processor = module.CreateInstance(cid, Vst3HostNative.IAudioProcessorIid);
            }

            if (processor == IntPtr.Zero)
            {
                LastCreateFailure = "qi-iaudioprocessor:" + procQi.ToString("X8");
                return null;
            }

            var classId = Vst3HostNative.ClassIdToBase64(cid);
            var display = string.IsNullOrWhiteSpace(slot.DisplayName) ? info.Name : slot.DisplayName!;
            var instance = new Vst3ProcessorInstance(
                module,
                host,
                component,
                processor,
                slot.PluginPath!,
                classId,
                display,
                sampleRate,
                maxBlockSize,
                channels);
            host = null;
            component = IntPtr.Zero;
            processor = IntPtr.Zero;
            module = null!;

            if (slot.StateBlob is { Length: > 0 })
            {
                instance.ApplyState(slot.StateBlob);
            }

            if (string.IsNullOrWhiteSpace(slot.ClassId))
            {
                slot.ClassId = instance.ClassId;
            }

            if (string.IsNullOrWhiteSpace(slot.DisplayName))
            {
                slot.DisplayName = instance.DisplayName;
            }

            if (instance.Faulted)
            {
                LastCreateFailure = "setup-or-activate";
                instance.Dispose();
                return null;
            }

            return instance;
        }
        catch (Exception ex)
        {
            LastCreateFailure = "exception:" + ex.GetType().Name + ":" + ex.Message;
            return null;
        }
        finally
        {
            if (processor != IntPtr.Zero)
            {
                Vst3HostNative.Release(processor);
            }

            if (component != IntPtr.Zero)
            {
                try
                {
                    Terminate(component);
                }
                catch
                {
                }

                Vst3HostNative.Release(component);
            }

            host?.Dispose();
            module?.Dispose();
        }
    }

    public void Reconfigure(double sampleRate, int maxBlockSize, int channels)
    {
        lock (_gate)
        {
            if (_disposed || _faulted)
            {
                return;
            }

            _sampleRate = sampleRate > 0 ? sampleRate : 48000;
            _maxBlock = Math.Max(16, maxBlockSize);
            _channels = Math.Max(1, channels);
            try
            {
                DeactivateNoLock();
                if (!SetupNoLock())
                {
                    _faulted = true;
                    return;
                }

                if (!ActivateNoLock())
                {
                    _faulted = true;
                }
            }
            catch
            {
                _faulted = true;
            }
        }
    }

    public void ApplyState(byte[]? stateBlob)
    {
        lock (_gate)
        {
            if (_disposed || _faulted || _component == IntPtr.Zero)
            {
                return;
            }

            if (stateBlob is null || stateBlob.Length == 0)
            {
                return;
            }

            if (_lastState is not null
                && _lastState.Length == stateBlob.Length
                && _lastState.AsSpan().SequenceEqual(stateBlob))
            {
                return;
            }

            using var stream = new Vst3MemoryStream(stateBlob);
            if (!SetState(_component, stream.ComPtr))
            {
                return;
            }

            _lastState = (byte[])stateBlob.Clone();
            if (_controllerReady && _controller != IntPtr.Zero)
            {
                SyncControllerStateNoLock();
            }
        }
    }

    public byte[]? CaptureState()
    {
        lock (_gate)
        {
            if (_disposed || _faulted || _component == IntPtr.Zero)
            {
                return _lastState;
            }

            using var stream = new Vst3MemoryStream();
            if (!GetState(_component, stream.ComPtr))
            {
                return _lastState;
            }

            _lastState = stream.ToArray();
            return _lastState;
        }
    }

    public bool Process(float[] buffer, int offset, int sampleCount, int channels)
    {
        lock (_gate)
        {
            if (_disposed || _faulted || !_active || _processor == IntPtr.Zero)
            {
                return !_faulted;
            }

            var frames = sampleCount / Math.Max(1, channels);
            if (frames <= 0)
            {
                return true;
            }

            try
            {
                var frameOffset = 0;
                while (frameOffset < frames)
                {
                    var n = Math.Min(_maxBlock, frames - frameOffset);
                    var sampleOffset = offset + frameOffset * channels;
                    if (!ProcessBlockNoLock(buffer, sampleOffset, n, channels))
                    {
                        _faulted = true;
                        return false;
                    }

                    frameOffset += n;
                }

                return true;
            }
            catch (AccessViolationException)
            {
                // iZotope Trash 等: UI と process の競合でネイティブが壊れることがある。スロットを落としてアプリは生かす。
                _faulted = true;
                return false;
            }
            catch (SEHException)
            {
                _faulted = true;
                return false;
            }
        }
    }

    public void ResetEditor()
    {
        lock (_gate)
        {
            _editorOpening = false;
            CloseViewNoLock();
        }
    }

    public Vst3EditorSession? TryOpenEditor()
    {
        LastEditorFailure = null;
        lock (_gate)
        {
            if (_disposed || _faulted || _component == IntPtr.Zero)
            {
                LastEditorFailure = "disposed-or-faulted";
                return null;
            }

            // 前回の失敗／中断で view が残っている場合は捨ててから再試行
            if (_plugView != IntPtr.Zero)
            {
                CloseViewNoLock();
            }

            if (_editorOpening)
            {
                _editorOpening = false;
            }

            try
            {
                if (!EnsureControllerNoLock())
                {
                    LastEditorFailure = "ensure-controller";
                    return null;
                }

                // createView 前にもう一度 state を同期（Apogee 等で UI が空になるのを避ける）
                SyncControllerStateNoLock();
                _editorOpening = true;
            }
            catch (Exception ex)
            {
                LastEditorFailure = "exception:" + ex.GetType().Name + ":" + ex.Message;
                return null;
            }
        }

        // createView / SetFrame は _gate 外。長時間ロックすると再生 process とデッドロックする。
        IntPtr view = IntPtr.Zero;
        Vst3PlugFrame? frame = null;
        try
        {
            view = CreateEditorViewNoLock();
            if (view == IntPtr.Zero)
            {
                LastEditorFailure = "create-view";
                return null;
            }

            _ = SupportsHwnd(view);
            frame = new Vst3PlugFrame();
            SetFrame(view, frame.ComPtr);
        }
        catch (Exception ex)
        {
            LastEditorFailure = "exception:" + ex.GetType().Name + ":" + ex.Message;
            ReleaseViewQuiet(view);
            view = IntPtr.Zero;
            frame?.Dispose();
            return null;
        }
        finally
        {
            lock (_gate)
            {
                _editorOpening = false;
            }
        }

        lock (_gate)
        {
            if (_disposed || _faulted || _plugView != IntPtr.Zero)
            {
                LastEditorFailure = _disposed || _faulted ? "disposed-or-faulted" : "already-open";
                ReleaseViewQuiet(view);
                frame?.Dispose();
                return null;
            }

            _plugView = view;
            var title = DisplayName;
            Vst3EditorSession? session = null;
            session = new Vst3EditorSession(
                title,
                hwnd =>
                {
                    // Soft Limit 等は attached の戻り値が当てにならないことがあるので失敗でも続行
                    _ = AttachView(_plugView, hwnd);

                    var size = GetViewSize(_plugView);
                    OnViewSize(_plugView, size);
                    session?.RequestResize(size);
                },
                () =>
                {
                    lock (_gate)
                    {
                        CloseViewNoLock();
                    }
                },
                () => GetViewSize(_plugView),
                rect => OnViewSize(_plugView, rect),
                () => CanResizeView(_plugView),
                desired => CheckViewSizeConstraint(_plugView, desired),
                frame!);
            return session;
        }
    }

    private static void ReleaseViewQuiet(IntPtr view)
    {
        if (view == IntPtr.Zero)
        {
            return;
        }

        try
        {
            SetFrame(view, IntPtr.Zero);
        }
        catch
        {
        }

        try
        {
            Vst3HostNative.Release(view);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                DeactivateNoLock();
            }
            catch
            {
            }

            CloseViewNoLock();
            FreeBusBuffersNoLock();

            if (_processor != IntPtr.Zero)
            {
                Vst3HostNative.Release(_processor);
                _processor = IntPtr.Zero;
            }

            if (_controller != IntPtr.Zero)
            {
                try
                {
                    if (_controllerOwned)
                    {
                        Terminate(_controller);
                    }
                }
                catch
                {
                }

                Vst3HostNative.Release(_controller);
                _controller = IntPtr.Zero;
            }

            if (_component != IntPtr.Zero)
            {
                try
                {
                    Terminate(_component);
                }
                catch
                {
                }

                Vst3HostNative.Release(_component);
                _component = IntPtr.Zero;
            }

            _componentHandler.Dispose();
            _inputParamChanges.Dispose();
            _outputParamChanges.Dispose();
            _host.Dispose();
            _module.Dispose();
        }
    }

    private bool SetupNoLock()
    {
        _numInputBuses = Math.Max(1, GetAudioBusCount(_component, Vst3HostNative.BusDirectionInput));
        _numOutputBuses = Math.Max(1, GetAudioBusCount(_component, Vst3HostNative.BusDirectionOutput));
        // プラグイン既定のバス構成を先に読む（Waves Mono / 5.1 / PS22 Mono→Stereo 等）。
        ApplyPreferredBusArrangementsNoLock();
        EnsureScratchNoLock(force: true);

        unsafe
        {
            var vt = *(IntPtr**)_processor;
            // 拒否されても setupProcessing で再判定するプラグインがあるので、ここでは落とさない
            var canSize = Marshal.GetDelegateForFunctionPointer<CanProcessSampleSizeFn>(vt[5]);
            canSize(_processor, Vst3HostNative.SymbolicSampleSize32);
        }

        ActivateMainBuses(_component);
        // Activate 後にバス数が変わることは稀だが、ProcessData 長はここで確定させる。
        _numInputBuses = Math.Max(1, GetAudioBusCount(_component, Vst3HostNative.BusDirectionInput));
        _numOutputBuses = Math.Max(1, GetAudioBusCount(_component, Vst3HostNative.BusDirectionOutput));
        // Activate 後に getBusArrangement が確定する実装向けに再読込。
        RefreshPluginChannelCountsNoLock();
        EnsureScratchNoLock(force: true);

        var setup = new Vst3HostNative.ProcessSetup
        {
            ProcessMode = Vst3HostNative.ProcessModeRealtime,
            SymbolicSampleSize = Vst3HostNative.SymbolicSampleSize32,
            MaxSamplesPerBlock = _maxBlock,
            SampleRate = _sampleRate,
        };

        unsafe
        {
            var vt = *(IntPtr**)_processor;
            var setupProcessing = Marshal.GetDelegateForFunctionPointer<SetupProcessingFn>(vt[7]);
            if (setupProcessing(_processor, &setup) != Vst3HostNative.KResultOk)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// ホストがステレオでも、プラグイン既定が Mono / 5.1 ならそちらを採用する。
    /// Flux 等は不正な setBus で AV するため、既定が取れたら極力 setBus を呼ばない。
    /// </summary>
    private void ApplyPreferredBusArrangementsNoLock()
    {
        var hostArr = Vst3HostNative.SpeakerArrangementForChannelCount(_channels);
        var hintArr = GuessArrangementFromName(DisplayName, ClassId);
        var defaultIn = TryGetBusArrangement(_processor, Vst3HostNative.BusDirectionInput, 0, out var din)
            ? din
            : 0UL;
        var defaultOut = TryGetBusArrangement(_processor, Vst3HostNative.BusDirectionOutput, 0, out var dout)
            ? dout
            : 0UL;

        var chosenIn = defaultIn != 0 ? defaultIn : hostArr;
        var chosenOut = defaultOut != 0 ? defaultOut : hostArr;
        var needSet = true;

        // 既定がホスト（またはヒント）と一致／妥当なら setBus 省略（TraxSf 等の AV 回避）。
        if (defaultIn != 0 && defaultOut != 0)
        {
            var defInCh = Vst3HostNative.SpeakerChannelCount(defaultIn);
            var defOutCh = Vst3HostNative.SpeakerChannelCount(defaultOut);
            var hostWantsMono = _channels <= 1;
            var defIsUsable = defInCh >= 1 && defOutCh >= 1 && defInCh <= 16 && defOutCh <= 16;
            var hostMismatchMono = hostWantsMono && defInCh > 1;
            if (defIsUsable && !hostMismatchMono)
            {
                chosenIn = defaultIn;
                chosenOut = defaultOut;
                needSet = false;
            }
        }

        if (needSet)
        {
            var inCandidates = new ulong[10];
            var outCandidates = new ulong[10];
            var nCand = 0;
            if (LooksLikeMonoToStereoName(DisplayName, ClassId))
            {
                nCand = AddBusCandidate(
                    inCandidates,
                    outCandidates,
                    nCand,
                    Vst3HostNative.SpeakerMono,
                    Vst3HostNative.SpeakerStereo);
            }
            else if (hintArr != 0)
            {
                nCand = AddBusCandidate(inCandidates, outCandidates, nCand, hintArr, hintArr);
            }

            if (defaultIn != 0 || defaultOut != 0)
            {
                nCand = AddBusCandidate(
                    inCandidates,
                    outCandidates,
                    nCand,
                    defaultIn != 0 ? defaultIn : hostArr,
                    defaultOut != 0 ? defaultOut : hostArr);
            }

            nCand = AddBusCandidate(inCandidates, outCandidates, nCand, hostArr, hostArr);
            if (_channels <= 1)
            {
                nCand = AddBusCandidate(
                    inCandidates,
                    outCandidates,
                    nCand,
                    Vst3HostNative.SpeakerMono,
                    Vst3HostNative.SpeakerMono);
            }

            // Waves Mono: 既定が取れずホストがステレオのときだけ Mono を試す。
            if (hintArr == Vst3HostNative.SpeakerMono)
            {
                nCand = AddBusCandidate(
                    inCandidates,
                    outCandidates,
                    nCand,
                    Vst3HostNative.SpeakerMono,
                    Vst3HostNative.SpeakerMono);
            }

            if (hintArr == Vst3HostNative.Speaker51 || hintArr == Vst3HostNative.Speaker50)
            {
                nCand = AddBusCandidate(inCandidates, outCandidates, nCand, hintArr, hintArr);
            }

            for (var c = 0; c < nCand; c++)
            {
                if (TrySetAllBusArrangementsNoLock(inCandidates[c], outCandidates[c]))
                {
                    chosenIn = inCandidates[c];
                    chosenOut = outCandidates[c];
                    break;
                }
            }
        }

        // setBus 後（または省略後）の実値を優先。
        if (TryGetBusArrangement(_processor, Vst3HostNative.BusDirectionInput, 0, out din) && din != 0)
        {
            chosenIn = din;
        }

        if (TryGetBusArrangement(_processor, Vst3HostNative.BusDirectionOutput, 0, out dout) && dout != 0)
        {
            chosenOut = dout;
        }

        _pluginInChannels = Math.Max(1, Math.Min(16, Vst3HostNative.SpeakerChannelCount(chosenIn)));
        _pluginOutChannels = Math.Max(1, Math.Min(16, Vst3HostNative.SpeakerChannelCount(chosenOut)));
        // Flux Epure 等は 16ch 既定のまま Process する（setBus で絞ると AV する実装がある）。
        _pluginChannels = Math.Max(_pluginInChannels, _pluginOutChannels);
    }

    private void RefreshPluginChannelCountsNoLock()
    {
        if (TryGetBusArrangement(_processor, Vst3HostNative.BusDirectionInput, 0, out var din)
            && din != 0)
        {
            _pluginInChannels = Math.Max(1, Vst3HostNative.SpeakerChannelCount(din));
        }

        if (TryGetBusArrangement(_processor, Vst3HostNative.BusDirectionOutput, 0, out var dout)
            && dout != 0)
        {
            _pluginOutChannels = Math.Max(1, Vst3HostNative.SpeakerChannelCount(dout));
        }

        _pluginChannels = Math.Max(1, Math.Max(_pluginInChannels, _pluginOutChannels));
    }

    private static int AddBusCandidate(
        ulong[] inCandidates,
        ulong[] outCandidates,
        int count,
        ulong inn,
        ulong outt)
    {
        if (inn == 0 || outt == 0 || count >= inCandidates.Length)
        {
            return count;
        }

        for (var i = 0; i < count; i++)
        {
            if (inCandidates[i] == inn && outCandidates[i] == outt)
            {
                return count;
            }
        }

        inCandidates[count] = inn;
        outCandidates[count] = outt;
        return count + 1;
    }

    private bool TrySetAllBusArrangementsNoLock(ulong mainIn, ulong mainOut)
    {
        try
        {
            unsafe
            {
                var vt = *(IntPtr**)_processor;
                var setBus = Marshal.GetDelegateForFunctionPointer<SetBusArrangementsFn>(vt[3]);
                var inArr = stackalloc ulong[_numInputBuses];
                var outArr = stackalloc ulong[_numOutputBuses];
                for (var i = 0; i < _numInputBuses; i++)
                {
                    // サイドチェイン等はメインと同じ並びを渡す（iZotope）。拒否時は次候補へ。
                    inArr[i] = mainIn;
                }

                for (var i = 0; i < _numOutputBuses; i++)
                {
                    outArr[i] = mainOut;
                }

                return setBus(_processor, inArr, _numInputBuses, outArr, _numOutputBuses)
                    == Vst3HostNative.KResultOk;
            }
        }
        catch (AccessViolationException)
        {
            // Flux TraxSf 等: 非対応 arrangement で AV する。次候補／既定へ。
            return false;
        }
        catch (SEHException)
        {
            return false;
        }
    }

    private static bool TryGetBusArrangement(IntPtr processor, int direction, int index, out ulong arrangement)
    {
        arrangement = 0;
        if (processor == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            unsafe
            {
                var vt = *(IntPtr**)processor;
                var getBus = Marshal.GetDelegateForFunctionPointer<GetBusArrangementFn>(vt[4]);
                ulong arr = 0;
                if (getBus(processor, direction, index, &arr) != Vst3HostNative.KResultOk || arr == 0)
                {
                    return false;
                }

                arrangement = arr;
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static ulong GuessArrangementFromName(string? displayName, string? classId)
    {
        var text = ((displayName ?? string.Empty) + " " + DecodeClassIdAscii(classId)).ToLowerInvariant();
        if (LooksLikeMonoToStereoName(displayName, classId))
        {
            return 0;
        }

        if (text.Contains("5.1", StringComparison.Ordinal))
        {
            return Vst3HostNative.Speaker51;
        }

        if (text.Contains("5.0", StringComparison.Ordinal))
        {
            return Vst3HostNative.Speaker50;
        }

        if (text.Contains("mono", StringComparison.Ordinal))
        {
            return Vst3HostNative.SpeakerMono;
        }

        return 0;
    }

    private static bool LooksLikeMonoToStereoName(string? displayName, string? classId)
    {
        var text = ((displayName ?? string.Empty) + " " + DecodeClassIdAscii(classId)).ToLowerInvariant();
        return text.Contains("mono/stereo", StringComparison.Ordinal)
            || text.Contains("mono to stereo", StringComparison.Ordinal)
            || text.Contains("xsplit", StringComparison.Ordinal);
    }

    private static string DecodeClassIdAscii(string? classIdBase64)
    {
        if (string.IsNullOrWhiteSpace(classIdBase64))
        {
            return string.Empty;
        }

        try
        {
            var bytes = Convert.FromBase64String(classIdBase64);
            return System.Text.Encoding.ASCII.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool ActivateNoLock()
    {
        if (!SetActive(_component, true))
        {
            return false;
        }

        // Soft Limit 等は activate 後にコントローラが揃らないと createView が null になる。
        // ExclusiveLoad 中の生成なら process とぶつからない。
        try
        {
            EnsureControllerNoLock();
        }
        catch
        {
        }

        unsafe
        {
            var vt = *(IntPtr**)_processor;
            var setProcessing = Marshal.GetDelegateForFunctionPointer<SetProcessingFn>(vt[8]);
            setProcessing(_processor, 1);
        }

        _projectTimeSamples = 0;
        _continuousTimeSamples = 0;
        _active = true;
        return true;
    }

    private void DeactivateNoLock()
    {
        if (!_active)
        {
            return;
        }

        try
        {
            unsafe
            {
                if (_processor != IntPtr.Zero)
                {
                    var vt = *(IntPtr**)_processor;
                    var setProcessing = Marshal.GetDelegateForFunctionPointer<SetProcessingFn>(vt[8]);
                    setProcessing(_processor, 0);
                }
            }
        }
        catch
        {
        }

        try
        {
            SetActive(_component, false);
        }
        catch
        {
        }

        _active = false;
    }

    private bool ProcessBlockNoLock(float[] buffer, int offset, int frames, int channels)
    {
        EnsureScratchNoLock();
        var pc = _pluginChannels;
        var pin = Math.Max(1, _pluginInChannels);
        var pout = Math.Max(1, _pluginOutChannels);
        var hostCh = Math.Max(1, channels);

        // 入出力は別バッファ。iZotope Trash 等は in-place（同一ポインタ）だと AccessViolation する。
        for (var c = 0; c < pc; c++)
        {
            Array.Clear(_planarIn[c], 0, frames);
            Array.Clear(_planarOut[c], 0, frames);
            _inPtrs[c] = Marshal.UnsafeAddrOfPinnedArrayElement(_planarIn[c], 0);
            _outPtrs[c] = Marshal.UnsafeAddrOfPinnedArrayElement(_planarOut[c], 0);
        }

        // ホスト interleaved → プラグイン planar（Mono 専用はステレオをダウンミックス）
        if (pin == 1 && hostCh >= 2)
        {
            var plane = _planarIn[0];
            for (var i = 0; i < frames; i++)
            {
                var baseIdx = offset + i * hostCh;
                plane[i] = 0.5f * (buffer[baseIdx] + buffer[baseIdx + 1]);
            }
        }
        else
        {
            var copy = Math.Min(pin, hostCh);
            for (var c = 0; c < copy; c++)
            {
                var plane = _planarIn[c];
                for (var i = 0; i < frames; i++)
                {
                    plane[i] = buffer[offset + i * hostCh + c];
                }
            }
        }

        // メインバス＋非アクティブな追加バス（サイドチェイン等）。バス配列長は getBusCount と一致させる。
        var busIn = new Vst3HostNative.AudioBusBuffers
        {
            NumChannels = pin,
            SilenceFlags = 0,
            ChannelBuffers = _inPtrsHandle.AddrOfPinnedObject(),
        };
        var busOut = new Vst3HostNative.AudioBusBuffers
        {
            NumChannels = pout,
            SilenceFlags = 0,
            ChannelBuffers = _outPtrsHandle.AddrOfPinnedObject(),
        };
        var inactive = new Vst3HostNative.AudioBusBuffers
        {
            NumChannels = 0,
            SilenceFlags = 0,
            ChannelBuffers = IntPtr.Zero,
        };

        WriteBusArray(_inputsBusArray, _numInputBuses, busIn, inactive);
        WriteBusArray(_outputsBusArray, _numOutputBuses, busOut, inactive);
        // 互換: 単一バス用ポインタもメインと同じ内容にしておく。
        Marshal.StructureToPtr(busIn, _busIn, false);
        Marshal.StructureToPtr(busOut, _busOut, false);

        // UI（performEdit）→ このブロックの IParameterChanges へ
        _inputParamChanges.BeginProcessBlock();
        _outputParamChanges.BeginOutputBlock();
        WriteProcessContextNoLock();

        var data = new Vst3HostNative.ProcessData
        {
            ProcessMode = Vst3HostNative.ProcessModeRealtime,
            SymbolicSampleSize = Vst3HostNative.SymbolicSampleSize32,
            NumSamples = frames,
            NumInputs = _numInputBuses,
            NumOutputs = _numOutputBuses,
            Inputs = _inputsBusArray,
            Outputs = _outputsBusArray,
            InputParameterChanges = _inputParamChanges.ComPtr,
            OutputParameterChanges = _outputParamChanges.ComPtr,
            ProcessContext = _processContext,
        };

        unsafe
        {
            var vt = *(IntPtr**)_processor;
            var process = Marshal.GetDelegateForFunctionPointer<ProcessFn>(vt[9]);
            // kResultFalse でもバッファは触っていることがある。例外以外は通す。
            process(_processor, &data);
        }

        ForwardOutputParamsToControllerNoLock();
        _projectTimeSamples += frames;
        _continuousTimeSamples += frames;

        // プラグイン planar → ホスト interleaved（Mono 出力は L/R に複製）
        if (pout == 1 && hostCh >= 2)
        {
            var mono = _planarOut[0];
            for (var i = 0; i < frames; i++)
            {
                var baseIdx = offset + i * hostCh;
                var s = mono[i];
                buffer[baseIdx] = s;
                buffer[baseIdx + 1] = s;
                for (var c = 2; c < hostCh; c++)
                {
                    buffer[baseIdx + c] = 0f;
                }
            }
        }
        else
        {
            var copy = Math.Min(pout, hostCh);
            for (var i = 0; i < frames; i++)
            {
                var baseIdx = offset + i * hostCh;
                for (var c = 0; c < copy; c++)
                {
                    buffer[baseIdx + c] = _planarOut[c][i];
                }

                for (var c = copy; c < hostCh; c++)
                {
                    buffer[baseIdx + c] = 0f;
                }
            }
        }

        return true;
    }

    private void WriteProcessContextNoLock()
    {
        // FabFilter 等のスペアナは ProcessContext（再生時刻）無しだと描画を止めることがある。
        // 音声バッファ自体は通っていても（EQ は効く）スペアナだけ欠ける。
        var ctx = new Vst3HostNative.ProcessContext
        {
            State = unchecked((uint)(
                Vst3HostNative.ProcessContextPlaying
                | Vst3HostNative.ProcessContextSystemTimeValid
                | Vst3HostNative.ProcessContextContTimeValid
                | Vst3HostNative.ProcessContextTempoValid
                | Vst3HostNative.ProcessContextTimeSigValid)),
            SampleRate = _sampleRate > 0 ? _sampleRate : 48000,
            ProjectTimeSamples = _projectTimeSamples,
            SystemTime = (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency)),
            ContinousTimeSamples = _continuousTimeSamples,
            Tempo = 120,
            TimeSigNumerator = 4,
            TimeSigDenominator = 4,
        };
        Marshal.StructureToPtr(ctx, _processContext, false);
    }

    private void ForwardOutputParamsToControllerNoLock()
    {
        if (_controller == IntPtr.Zero)
        {
            return;
        }

        _outputParamChanges.CollectLastValues(_outputParamScratch);
        if (_outputParamScratch.Count == 0)
        {
            return;
        }

        unsafe
        {
            var vt = *(IntPtr**)_controller;
            // IEditController::setParamNormalized = index 14
            var setParam = Marshal.GetDelegateForFunctionPointer<SetParamNormalizedFn>(vt[14]);
            foreach (var (id, value) in _outputParamScratch)
            {
                try
                {
                    setParam(_controller, id, value);
                }
                catch
                {
                }
            }
        }
    }

    private void EnsureScratchNoLock(bool force = false)
    {
        var pc = Math.Max(
            1,
            Math.Max(
                Math.Max(_pluginInChannels, _pluginOutChannels),
                Math.Max(_pluginChannels, _channels >= 1 ? Math.Min(_channels, 8) : 1)));
        _pluginChannels = pc;
        var inBuses = Math.Max(1, _numInputBuses);
        var outBuses = Math.Max(1, _numOutputBuses);
        if (!force
            && _planarIn.Length == pc
            && _planarIn[0].Length >= _maxBlock
            && _busIn != IntPtr.Zero
            && _inputsBusArray != IntPtr.Zero
            && _outputsBusArray != IntPtr.Zero)
        {
            return;
        }

        FreeBusBuffersNoLock();
        _numInputBuses = inBuses;
        _numOutputBuses = outBuses;
        _planarIn = new float[pc][];
        _planarOut = new float[pc][];
        for (var c = 0; c < pc; c++)
        {
            _planarIn[c] = GC.AllocateArray<float>(_maxBlock, pinned: true);
            _planarOut[c] = GC.AllocateArray<float>(_maxBlock, pinned: true);
        }

        _inPtrs = new IntPtr[pc];
        _outPtrs = new IntPtr[pc];
        _inPtrsHandle = GCHandle.Alloc(_inPtrs, GCHandleType.Pinned);
        _outPtrsHandle = GCHandle.Alloc(_outPtrs, GCHandleType.Pinned);
        var busSize = Marshal.SizeOf<Vst3HostNative.AudioBusBuffers>();
        _busIn = Marshal.AllocHGlobal(busSize);
        _busOut = Marshal.AllocHGlobal(busSize);
        _inputsBusArray = Marshal.AllocHGlobal(busSize * inBuses);
        _outputsBusArray = Marshal.AllocHGlobal(busSize * outBuses);
        _processContext = Marshal.AllocHGlobal(Marshal.SizeOf<Vst3HostNative.ProcessContext>());
    }

    private static void WriteBusArray(
        IntPtr array,
        int busCount,
        Vst3HostNative.AudioBusBuffers main,
        Vst3HostNative.AudioBusBuffers inactive)
    {
        var size = Marshal.SizeOf<Vst3HostNative.AudioBusBuffers>();
        for (var i = 0; i < busCount; i++)
        {
            Marshal.StructureToPtr(i == 0 ? main : inactive, array + (i * size), false);
        }
    }

    private static int GetAudioBusCount(IntPtr component, int direction)
    {
        if (component == IntPtr.Zero)
        {
            return 1;
        }

        unsafe
        {
            var vt = *(IntPtr**)component;
            var getBusCount = Marshal.GetDelegateForFunctionPointer<GetBusCountFn>(vt[7]);
            return Math.Max(0, getBusCount(component, Vst3HostNative.MediaTypeAudio, direction));
        }
    }

    private void FreeBusBuffersNoLock()
    {
        if (_inPtrsHandle.IsAllocated)
        {
            _inPtrsHandle.Free();
        }

        if (_outPtrsHandle.IsAllocated)
        {
            _outPtrsHandle.Free();
        }

        if (_busIn != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_busIn);
            _busIn = IntPtr.Zero;
        }

        if (_busOut != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_busOut);
            _busOut = IntPtr.Zero;
        }

        if (_inputsBusArray != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_inputsBusArray);
            _inputsBusArray = IntPtr.Zero;
        }

        if (_outputsBusArray != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_outputsBusArray);
            _outputsBusArray = IntPtr.Zero;
        }

        if (_processContext != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_processContext);
            _processContext = IntPtr.Zero;
        }
    }

    private bool EnsureControllerNoLock()
    {
        if (_controllerReady && _controller != IntPtr.Zero)
        {
            return true;
        }

        // 単体コンポーネントが IEditController を兼ねる場合
        if (Vst3HostNative.QueryInterface(_component, Vst3HostNative.IEditControllerIid, out var combined)
            == Vst3HostNative.KResultOk
            && combined != IntPtr.Zero)
        {
            _controller = combined;
            _controllerOwned = false;
        }
        else
        {
            var cid = new byte[16];
            if (!TryGetControllerClassId(_component, cid))
            {
                return false;
            }

            var created = _module.CreateInstance(cid, Vst3HostNative.IEditControllerIid);
            if (created == IntPtr.Zero)
            {
                created = _module.CreateInstance(cid, Vst3HostNative.FUnknownIid);
                if (created == IntPtr.Zero)
                {
                    return false;
                }

                if (Vst3HostNative.QueryInterface(created, Vst3HostNative.IEditControllerIid, out var asEdit)
                    == Vst3HostNative.KResultOk
                    && asEdit != IntPtr.Zero)
                {
                    Vst3HostNative.Release(created);
                    created = asEdit;
                }
            }

            if (!InitializeComponent(created, _host.ComPtr))
            {
                Vst3HostNative.Release(created);
                return false;
            }

            _controller = created;
            _controllerOwned = true;
            ConnectPoints(_component, _controller);
        }

        SetComponentHandler(_controller, _componentHandler.ComPtr);
        SyncControllerStateNoLock();
        _controllerReady = true;
        return true;
    }

    private void SyncControllerStateNoLock()
    {
        if (_controller == IntPtr.Zero || _component == IntPtr.Zero)
        {
            return;
        }

        using var stream = new Vst3MemoryStream();
        if (!GetState(_component, stream.ComPtr))
        {
            return;
        }

        // 読み位置を先頭へ
        SeekStreamStart(stream.ComPtr);
        SetComponentState(_controller, stream.ComPtr);
    }

    private IntPtr CreateEditorViewNoLock()
    {
        if (_controller == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // プラグインによって "editor" / "Editor" / 空文字 など
        foreach (var typeName in new[] { Vst3HostNative.ViewTypeEditor, "Editor", "edit", "" })
        {
            unsafe
            {
                var vt = *(IntPtr**)_controller;
                var createView = Marshal.GetDelegateForFunctionPointer<CreateViewFn>(vt[17]);
                var nameBytes = System.Text.Encoding.UTF8.GetBytes(typeName + "\0");
                fixed (byte* name = nameBytes)
                {
                    var view = createView(_controller, name);
                    if (view != IntPtr.Zero)
                    {
                        return view;
                    }
                }
            }
        }

        return IntPtr.Zero;
    }

    private void CloseViewNoLock()
    {
        if (_plugView == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Removed(_plugView);
        }
        catch
        {
        }

        try
        {
            SetFrame(_plugView, IntPtr.Zero);
        }
        catch
        {
        }

        Vst3HostNative.Release(_plugView);
        _plugView = IntPtr.Zero;
    }

    private static bool TryGetControllerClassId(IntPtr component, byte[] cid)
    {
        if (cid.Length < 16)
        {
            return false;
        }

        unsafe
        {
            var vt = *(IntPtr**)component;
            var getCid = Marshal.GetDelegateForFunctionPointer<GetControllerClassIdFn>(vt[5]);
            fixed (byte* p = cid)
            {
                return getCid(component, p) == Vst3HostNative.KResultOk;
            }
        }
    }

    private static void ConnectPoints(IntPtr component, IntPtr controller)
    {
        if (Vst3HostNative.QueryInterface(component, Vst3HostNative.IConnectionPointIid, out var cpComp)
                != Vst3HostNative.KResultOk
            || cpComp == IntPtr.Zero)
        {
            return;
        }

        if (Vst3HostNative.QueryInterface(controller, Vst3HostNative.IConnectionPointIid, out var cpCtrl)
                != Vst3HostNative.KResultOk
            || cpCtrl == IntPtr.Zero)
        {
            Vst3HostNative.Release(cpComp);
            return;
        }

        try
        {
            unsafe
            {
                var vtA = *(IntPtr**)cpComp;
                var vtB = *(IntPtr**)cpCtrl;
                var connectA = Marshal.GetDelegateForFunctionPointer<ConnectFn>(vtA[3]);
                var connectB = Marshal.GetDelegateForFunctionPointer<ConnectFn>(vtB[3]);
                connectA(cpComp, cpCtrl);
                connectB(cpCtrl, cpComp);
            }
        }
        finally
        {
            Vst3HostNative.Release(cpCtrl);
            Vst3HostNative.Release(cpComp);
        }
    }

    private static void SetComponentHandler(IntPtr controller, IntPtr handler)
    {
        unsafe
        {
            var vt = *(IntPtr**)controller;
            var setHandler = Marshal.GetDelegateForFunctionPointer<SetComponentHandlerFn>(vt[16]);
            setHandler(controller, handler);
        }
    }

    private static void SetComponentState(IntPtr controller, IntPtr stream)
    {
        unsafe
        {
            var vt = *(IntPtr**)controller;
            var setComponentState = Marshal.GetDelegateForFunctionPointer<StateFn>(vt[5]);
            setComponentState(controller, stream);
        }
    }

    private static void SeekStreamStart(IntPtr stream)
    {
        unsafe
        {
            var vt = *(IntPtr**)stream;
            // IBStream.seek = index 5。
            // 自前ストリームの vtable は managed SeekFn なので GetDelegate で別型へは戻せない。
            var seek = (delegate* unmanaged[Stdcall]<IntPtr, long, int, long*, int>)vt[5];
            seek(stream, 0, 0, null);
        }
    }

    private static bool SupportsHwnd(IntPtr view)
    {
        unsafe
        {
            var vt = *(IntPtr**)view;
            var isPlatform = Marshal.GetDelegateForFunctionPointer<IsPlatformTypeSupportedFn>(vt[3]);
            var typeBytes = System.Text.Encoding.UTF8.GetBytes(Vst3HostNative.PlatformTypeHwnd + "\0");
            fixed (byte* type = typeBytes)
            {
                return isPlatform(view, type) == Vst3HostNative.KResultOk;
            }
        }
    }

    private static void SetFrame(IntPtr view, IntPtr frame)
    {
        unsafe
        {
            var vt = *(IntPtr**)view;
            var setFrame = Marshal.GetDelegateForFunctionPointer<SetFrameFn>(vt[12]);
            setFrame(view, frame);
        }
    }

    private static bool AttachView(IntPtr view, IntPtr hwnd)
    {
        unsafe
        {
            var vt = *(IntPtr**)view;
            var attached = Marshal.GetDelegateForFunctionPointer<AttachedFn>(vt[4]);
            var typeBytes = System.Text.Encoding.UTF8.GetBytes(Vst3HostNative.PlatformTypeHwnd + "\0");
            fixed (byte* type = typeBytes)
            {
                var result = attached(view, hwnd, type);
                return result == Vst3HostNative.KResultOk || result == Vst3HostNative.KResultTrue;
            }
        }
    }

    private static void Removed(IntPtr view)
    {
        unsafe
        {
            var vt = *(IntPtr**)view;
            var removed = Marshal.GetDelegateForFunctionPointer<RemovedFn>(vt[5]);
            removed(view);
        }
    }

    private static Vst3HostNative.ViewRect GetViewSize(IntPtr view)
    {
        var rect = new Vst3HostNative.ViewRect { Left = 0, Top = 0, Right = 400, Bottom = 300 };
        if (view == IntPtr.Zero)
        {
            return rect;
        }

        unsafe
        {
            var vt = *(IntPtr**)view;
            var getSize = Marshal.GetDelegateForFunctionPointer<GetSizeFn>(vt[9]);
            getSize(view, &rect);
        }

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            rect.Right = rect.Left + 400;
            rect.Bottom = rect.Top + 300;
        }

        return rect;
    }

    private static void OnViewSize(IntPtr view, Vst3HostNative.ViewRect rect)
    {
        if (view == IntPtr.Zero)
        {
            return;
        }

        unsafe
        {
            var vt = *(IntPtr**)view;
            var onSize = Marshal.GetDelegateForFunctionPointer<OnSizeFn>(vt[10]);
            onSize(view, &rect);
        }
    }

    private static bool CanResizeView(IntPtr view)
    {
        if (view == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            unsafe
            {
                var vt = *(IntPtr**)view;
                var canResize = Marshal.GetDelegateForFunctionPointer<CanResizeFn>(vt[13]);
                var r = canResize(view);
                return r == Vst3HostNative.KResultOk || r == Vst3HostNative.KResultTrue;
            }
        }
        catch
        {
            return false;
        }
    }

    private static Vst3HostNative.ViewRect? CheckViewSizeConstraint(
        IntPtr view,
        Vst3HostNative.ViewRect desired)
    {
        if (view == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            unsafe
            {
                var vt = *(IntPtr**)view;
                var check = Marshal.GetDelegateForFunctionPointer<CheckSizeConstraintFn>(vt[14]);
                var rect = desired;
                var r = check(view, &rect);
                if (r != Vst3HostNative.KResultOk && r != Vst3HostNative.KResultTrue)
                {
                    return null;
                }

                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return null;
                }

                return rect;
            }
        }
        catch
        {
            return desired.Width > 0 && desired.Height > 0 ? desired : null;
        }
    }

    private static bool InitializeComponent(IntPtr component, IntPtr hostContext) =>
        InitializeComponentResult(component, hostContext) == Vst3HostNative.KResultOk;

    private static int InitializeComponentResult(IntPtr component, IntPtr hostContext)
    {
        try
        {
            unsafe
            {
                var vt = *(IntPtr**)component;
                var init = Marshal.GetDelegateForFunctionPointer<InitializeFn>(vt[3]);
                return init(component, hostContext);
            }
        }
        catch (AccessViolationException)
        {
            // Flux Trax 等: initialize でネイティブ AV。ロード失敗として扱う。
            return unchecked((int)0x80004005);
        }
        catch (SEHException)
        {
            return unchecked((int)0x80004005);
        }
    }

    private static void Terminate(IntPtr component)
    {
        unsafe
        {
            var vt = *(IntPtr**)component;
            var term = Marshal.GetDelegateForFunctionPointer<TerminateFn>(vt[4]);
            term(component);
        }
    }

    private static bool SetActive(IntPtr component, bool active)
    {
        unsafe
        {
            var vt = *(IntPtr**)component;
            var setActive = Marshal.GetDelegateForFunctionPointer<SetActiveFn>(vt[11]);
            return setActive(component, active ? 1 : 0) == Vst3HostNative.KResultOk;
        }
    }

    private static bool SetState(IntPtr component, IntPtr stream)
    {
        unsafe
        {
            var vt = *(IntPtr**)component;
            var setState = Marshal.GetDelegateForFunctionPointer<StateFn>(vt[12]);
            return setState(component, stream) == Vst3HostNative.KResultOk;
        }
    }

    private static bool GetState(IntPtr component, IntPtr stream)
    {
        unsafe
        {
            var vt = *(IntPtr**)component;
            var getState = Marshal.GetDelegateForFunctionPointer<StateFn>(vt[13]);
            return getState(component, stream) == Vst3HostNative.KResultOk;
        }
    }

    private static void ActivateMainBuses(IntPtr component)
    {
        unsafe
        {
            var vt = *(IntPtr**)component;
            var getBusCount = Marshal.GetDelegateForFunctionPointer<GetBusCountFn>(vt[7]);
            var activateBus = Marshal.GetDelegateForFunctionPointer<ActivateBusFn>(vt[10]);
            var nIn = Math.Max(1, getBusCount(component, Vst3HostNative.MediaTypeAudio, Vst3HostNative.BusDirectionInput));
            var nOut = Math.Max(1, getBusCount(component, Vst3HostNative.MediaTypeAudio, Vst3HostNative.BusDirectionOutput));
            // メインのみ有効。FabFilter 等のサイドチェインは無効化。
            for (var i = 0; i < nIn; i++)
            {
                activateBus(
                    component,
                    Vst3HostNative.MediaTypeAudio,
                    Vst3HostNative.BusDirectionInput,
                    i,
                    i == 0 ? 1 : 0);
            }

            for (var i = 0; i < nOut; i++)
            {
                activateBus(
                    component,
                    Vst3HostNative.MediaTypeAudio,
                    Vst3HostNative.BusDirectionOutput,
                    i,
                    i == 0 ? 1 : 0);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBusCountFn(IntPtr self, int type, int dir);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitializeFn(IntPtr self, IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TerminateFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetActiveFn(IntPtr self, int state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int StateFn(IntPtr self, IntPtr stream);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ActivateBusFn(IntPtr self, int type, int dir, int index, int state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int SetBusArrangementsFn(
        IntPtr self, ulong* inputs, int numIns, ulong* outputs, int numOuts);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int GetBusArrangementFn(
        IntPtr self, int direction, int index, ulong* arrangement);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CanProcessSampleSizeFn(IntPtr self, int symbolicSampleSize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int SetupProcessingFn(IntPtr self, Vst3HostNative.ProcessSetup* setup);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetProcessingFn(IntPtr self, int state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int ProcessFn(IntPtr self, Vst3HostNative.ProcessData* data);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetParamNormalizedFn(IntPtr self, uint id, double value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int GetControllerClassIdFn(IntPtr self, byte* classId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ConnectFn(IntPtr self, IntPtr other);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetComponentHandlerFn(IntPtr self, IntPtr handler);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate IntPtr CreateViewFn(IntPtr self, byte* name);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int IsPlatformTypeSupportedFn(IntPtr self, byte* type);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int AttachedFn(IntPtr self, IntPtr parent, byte* type);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RemovedFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int GetSizeFn(IntPtr self, Vst3HostNative.ViewRect* size);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int OnSizeFn(IntPtr self, Vst3HostNative.ViewRect* newSize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CanResizeFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int CheckSizeConstraintFn(IntPtr self, Vst3HostNative.ViewRect* rect);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetFrameFn(IntPtr self, IntPtr frame);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int StreamSeekFn(IntPtr self, long pos, int mode, long* result);
}
