namespace MgaSonicAnvil.Audio;

/// <summary>インサート 1 スロット分の音声プロセッサ。テストではモックを差し込める。</summary>
internal interface IVstSlotProcessor : IDisposable
{
    string PluginPath { get; }

    string? ClassId { get; }

    string DisplayName { get; }

    bool Faulted { get; }

    void Reconfigure(double sampleRate, int maxBlockSize, int channels);

    /// <summary>state が同一参照／同一内容なら何もしない。</summary>
    void ApplyState(byte[]? stateBlob);

    byte[]? CaptureState();

    /// <summary>
    /// interleaved float をその場で処理。失敗したら false（呼び出し側が Faulted 扱い）。
    /// </summary>
    bool Process(float[] buffer, int offset, int sampleCount, int channels);

    /// <summary>
    /// オフライン処理直前用。ディレイ／リバーブ等の内部バッファを捨てる。
    /// ライブ再生の温存経路では呼ばない。
    /// </summary>
    void ResetProcessing();

    /// <summary>
    /// プラグイン GUI（IPlugView）を用意する。UI スレッドから呼ぶ。
    /// 失敗・非対応は null。
    /// </summary>
    Vst3EditorSession? TryOpenEditor();

    /// <summary>開きかけ／残骸の IPlugView を捨てる。</summary>
    void ResetEditor();
}

/// <summary>開いている IPlugView セッション。ウィンドウが閉じたら Dispose。</summary>
internal sealed class Vst3EditorSession : IDisposable
{
    private readonly Action<IntPtr> _attach;
    private readonly Action _detach;
    private readonly Func<Vst3HostNative.ViewRect> _getSize;
    private readonly Action<Vst3HostNative.ViewRect> _onSize;
    private readonly Func<bool> _canResize;
    private readonly Func<Vst3HostNative.ViewRect, Vst3HostNative.ViewRect?> _checkSize;
    private readonly Vst3PlugFrame _frame;
    private bool _disposed;

    public Vst3EditorSession(
        string title,
        Action<IntPtr> attach,
        Action detach,
        Func<Vst3HostNative.ViewRect> getSize,
        Action<Vst3HostNative.ViewRect> onSize,
        Func<bool> canResize,
        Func<Vst3HostNative.ViewRect, Vst3HostNative.ViewRect?> checkSize,
        Vst3PlugFrame frame)
    {
        Title = title;
        _attach = attach;
        _detach = detach;
        _getSize = getSize;
        _onSize = onSize;
        _canResize = canResize;
        _checkSize = checkSize;
        _frame = frame;
        _frame.ResizeRequested = r => ResizeFromPlugin?.Invoke(r);
    }

    public string Title { get; }

    public event Action<Vst3HostNative.ViewRect>? ResizeFromPlugin;

    public Vst3HostNative.ViewRect GetSize() => _getSize();

    public bool CanResize() => _canResize();

    /// <summary>制約を適用したサイズ。拒否時は null。</summary>
    public Vst3HostNative.ViewRect? CheckSizeConstraint(Vst3HostNative.ViewRect desired) =>
        _checkSize(desired);

    public void Attach(IntPtr hwndParent) => _attach(hwndParent);

    public void NotifySize(Vst3HostNative.ViewRect rect) => _onSize(rect);

    /// <summary>attach 後など、ホスト側ウィンドウをプラグイン希望サイズへ合わせる。</summary>
    public void RequestResize(Vst3HostNative.ViewRect rect) => ResizeFromPlugin?.Invoke(rect);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _detach();
        }
        catch
        {
        }

        _frame.ResizeRequested = null;
        _frame.Dispose();
    }
}

internal interface IVstSlotProcessorFactory
{
    /// <summary>ロード失敗時は null（スロットを Faulted にする）。</summary>
    IVstSlotProcessor? TryCreate(VstInsertSlot slot, double sampleRate, int maxBlockSize, int channels);
}
