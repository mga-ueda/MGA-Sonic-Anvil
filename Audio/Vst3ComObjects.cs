using System.Runtime.InteropServices;

namespace MgaSonicAnvil.Audio;

/// <summary>ホスト側の IBStream（メモリ）。getState / setState 用。</summary>
internal unsafe sealed class Vst3MemoryStream : IDisposable
{
    private readonly object _gate = new();
    private byte[] _data;
    private int _position;
    private IntPtr _com;
    private GCHandle _selfHandle;
    private bool _disposed;
    private int _refCount = 1;

    // Keep delegates alive
    private readonly Vst3HostNative.QiFn _qi;
    private readonly Vst3HostNative.AddRefFn _addRef;
    private readonly Vst3HostNative.ReleaseFn _release;
    private readonly ReadFn _read;
    private readonly WriteFn _write;
    private readonly SeekFn _seek;
    private readonly TellFn _tell;

    public Vst3MemoryStream(byte[]? initial = null)
    {
        _data = initial is null ? [] : (byte[])initial.Clone();
        _qi = QueryInterface;
        _addRef = AddRef;
        _release = Release;
        _read = Read;
        _write = Write;
        _seek = Seek;
        _tell = Tell;
        _selfHandle = GCHandle.Alloc(this);
        _com = BuildComObject();
    }

    public IntPtr ComPtr => _com;

    public byte[] ToArray()
    {
        lock (_gate)
        {
            return (byte[])_data.Clone();
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
            if (_com != IntPtr.Zero)
            {
                var vtable = Marshal.ReadIntPtr(_com);
                Marshal.FreeHGlobal(vtable);
                Marshal.FreeHGlobal(_com);
                _com = IntPtr.Zero;
            }

            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }
        }
    }

    private IntPtr BuildComObject()
    {
        // [vtable*][GCHandle]
        var obj = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var vtable = Marshal.AllocHGlobal(IntPtr.Size * 7);
        Marshal.WriteIntPtr(vtable, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_qi));
        Marshal.WriteIntPtr(vtable, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
        Marshal.WriteIntPtr(vtable, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_release));
        Marshal.WriteIntPtr(vtable, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_read));
        Marshal.WriteIntPtr(vtable, 4 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_write));
        Marshal.WriteIntPtr(vtable, 5 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_seek));
        Marshal.WriteIntPtr(vtable, 6 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_tell));
        Marshal.WriteIntPtr(obj, 0, vtable);
        Marshal.WriteIntPtr(obj, IntPtr.Size, GCHandle.ToIntPtr(_selfHandle));
        return obj;
    }

    private static Vst3MemoryStream From(IntPtr self)
    {
        var handle = GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size));
        return (Vst3MemoryStream)handle.Target!;
    }

    private unsafe int QueryInterface(IntPtr self, byte* iid, out IntPtr obj)
    {
        obj = IntPtr.Zero;
        var span = new ReadOnlySpan<byte>(iid, 16);
        if (Vst3HostNative.IidEquals(Vst3HostNative.IBStreamIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.FUnknownIid, span))
        {
            obj = self;
            AddRef(self);
            return Vst3HostNative.KResultOk;
        }

        return Vst3HostNative.KResultNotImplemented; // E_NOINTERFACE style
    }

    private uint AddRef(IntPtr self)
    {
        var me = From(self);
        return (uint)Interlocked.Increment(ref me._refCount);
    }

    private uint Release(IntPtr self)
    {
        // 寿命は using / Dispose が持つ。プラグインの Release では壊さない。
        var me = From(self);
        return (uint)Math.Max(0, Interlocked.Decrement(ref me._refCount));
    }

    private unsafe int Read(IntPtr self, void* buffer, int numBytes, int* numBytesRead)
    {
        var me = From(self);
        lock (me._gate)
        {
            var n = Math.Min(numBytes, me._data.Length - me._position);
            if (n < 0)
            {
                n = 0;
            }

            if (n > 0)
            {
                Marshal.Copy(me._data, me._position, (IntPtr)buffer, n);
                me._position += n;
            }

            if (numBytesRead != null)
            {
                *numBytesRead = n;
            }

            return n == numBytes ? Vst3HostNative.KResultOk : Vst3HostNative.KResultFalse;
        }
    }

    private unsafe int Write(IntPtr self, void* buffer, int numBytes, int* numBytesWritten)
    {
        var me = From(self);
        lock (me._gate)
        {
            var need = me._position + numBytes;
            if (need > me._data.Length)
            {
                Array.Resize(ref me._data, Math.Max(need, me._data.Length * 2 + 16));
            }

            if (numBytes > 0)
            {
                Marshal.Copy((IntPtr)buffer, me._data, me._position, numBytes);
                me._position += numBytes;
            }

            if (numBytesWritten != null)
            {
                *numBytesWritten = numBytes;
            }

            return Vst3HostNative.KResultOk;
        }
    }

    private unsafe int Seek(IntPtr self, long pos, int mode, long* result)
    {
        var me = From(self);
        lock (me._gate)
        {
            long next = mode switch
            {
                0 => pos, // set
                1 => me._position + pos, // cur
                2 => me._data.Length + pos, // end
                _ => -1,
            };
            if (next < 0)
            {
                return Vst3HostNative.KInvalidArgument;
            }

            me._position = (int)Math.Min(next, int.MaxValue);
            if (result != null)
            {
                *result = me._position;
            }

            return Vst3HostNative.KResultOk;
        }
    }

    private unsafe int Tell(IntPtr self, long* pos)
    {
        var me = From(self);
        lock (me._gate)
        {
            if (pos != null)
            {
                *pos = me._position;
            }

            return Vst3HostNative.KResultOk;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int ReadFn(IntPtr self, void* buffer, int numBytes, int* numBytesRead);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int WriteFn(IntPtr self, void* buffer, int numBytes, int* numBytesWritten);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int SeekFn(IntPtr self, long pos, int mode, long* result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int TellFn(IntPtr self, long* pos);
}

/// <summary>最小の IHostApplication。WaveShell 向けに IPlugInterfaceSupport も返す。</summary>
internal unsafe sealed class Vst3HostApplication : IDisposable
{
    private IntPtr _com;
    private IntPtr _supportCom;
    private GCHandle _selfHandle;
    private bool _disposed;
    private int _refCount = 1;

    private readonly Vst3HostNative.QiFn _qi;
    private readonly Vst3HostNative.AddRefFn _addRef;
    private readonly Vst3HostNative.ReleaseFn _release;
    private readonly GetNameFn _getName;
    private readonly CreateInstanceFn _createInstance;
    private readonly Vst3HostNative.QiFn _supportQi;
    private readonly IsSupportedFn _isSupported;

    public Vst3HostApplication()
    {
        _qi = QueryInterface;
        _addRef = AddRef;
        _release = Release;
        _getName = GetName;
        _createInstance = CreateInstance;
        _supportQi = SupportQueryInterface;
        _isSupported = IsPlugInterfaceSupported;
        _selfHandle = GCHandle.Alloc(this);
        _com = BuildComObject();
        _supportCom = BuildSupportComObject();
    }

    public IntPtr ComPtr => _com;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        FreeCom(ref _supportCom);
        FreeCom(ref _com);

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private static void FreeCom(ref IntPtr com)
    {
        if (com == IntPtr.Zero)
        {
            return;
        }

        var vtable = Marshal.ReadIntPtr(com);
        Marshal.FreeHGlobal(vtable);
        Marshal.FreeHGlobal(com);
        com = IntPtr.Zero;
    }

    private IntPtr BuildComObject()
    {
        var obj = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var vtable = Marshal.AllocHGlobal(IntPtr.Size * 5);
        Marshal.WriteIntPtr(vtable, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_qi));
        Marshal.WriteIntPtr(vtable, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
        Marshal.WriteIntPtr(vtable, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_release));
        Marshal.WriteIntPtr(vtable, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getName));
        Marshal.WriteIntPtr(vtable, 4 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_createInstance));
        Marshal.WriteIntPtr(obj, 0, vtable);
        Marshal.WriteIntPtr(obj, IntPtr.Size, GCHandle.ToIntPtr(_selfHandle));
        return obj;
    }

    private IntPtr BuildSupportComObject()
    {
        // IPlugInterfaceSupport は IHostApplication と vtable が衝突するので別 COM
        var obj = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var vtable = Marshal.AllocHGlobal(IntPtr.Size * 4);
        Marshal.WriteIntPtr(vtable, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_supportQi));
        Marshal.WriteIntPtr(vtable, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
        Marshal.WriteIntPtr(vtable, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_release));
        Marshal.WriteIntPtr(vtable, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_isSupported));
        Marshal.WriteIntPtr(obj, 0, vtable);
        Marshal.WriteIntPtr(obj, IntPtr.Size, GCHandle.ToIntPtr(_selfHandle));
        return obj;
    }

    private static Vst3HostApplication From(IntPtr self)
    {
        var handle = GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size));
        return (Vst3HostApplication)handle.Target!;
    }

    private unsafe int QueryInterface(IntPtr self, byte* iid, out IntPtr obj)
    {
        obj = IntPtr.Zero;
        var span = new ReadOnlySpan<byte>(iid, 16);
        if (Vst3HostNative.IidEquals(Vst3HostNative.IHostApplicationIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.FUnknownIid, span))
        {
            obj = self;
            AddRef(self);
            return Vst3HostNative.KResultOk;
        }

        if (Vst3HostNative.IidEquals(Vst3HostNative.IPlugInterfaceSupportIid, span))
        {
            var me = From(self);
            obj = me._supportCom;
            AddRef(self);
            return Vst3HostNative.KResultOk;
        }

        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    private unsafe int SupportQueryInterface(IntPtr self, byte* iid, out IntPtr obj)
    {
        obj = IntPtr.Zero;
        var span = new ReadOnlySpan<byte>(iid, 16);
        if (Vst3HostNative.IidEquals(Vst3HostNative.IPlugInterfaceSupportIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.FUnknownIid, span))
        {
            obj = self;
            AddRef(self);
            return Vst3HostNative.KResultOk;
        }

        if (Vst3HostNative.IidEquals(Vst3HostNative.IHostApplicationIid, span))
        {
            var me = From(self);
            obj = me._com;
            AddRef(self);
            return Vst3HostNative.KResultOk;
        }

        return unchecked((int)0x80004002);
    }

    private uint AddRef(IntPtr self) => (uint)Interlocked.Increment(ref From(self)._refCount);

    private uint Release(IntPtr self)
    {
        var n = Interlocked.Decrement(ref From(self)._refCount);
        return (uint)Math.Max(0, n);
    }

    private unsafe int GetName(IntPtr self, char* name)
    {
        // String128 = char16[128]
        const string host = "MGA Sonic Anvil";
        for (var i = 0; i < 127; i++)
        {
            name[i] = i < host.Length ? host[i] : '\0';
        }

        name[127] = '\0';
        return Vst3HostNative.KResultOk;
    }

    private unsafe int CreateInstance(IntPtr self, byte* cid, byte* iid, out IntPtr obj)
    {
        obj = IntPtr.Zero;
        var cidSpan = new ReadOnlySpan<byte>(cid, 16);
        var iidSpan = new ReadOnlySpan<byte>(iid, 16);

        // プロセッサ↔コントローラの IMessage（スペアナ FFT 等）はホストが生成する。
        if (Vst3HostNative.IidEquals(Vst3HostNative.IMessageIid, cidSpan)
            && Vst3HostNative.IidEquals(Vst3HostNative.IMessageIid, iidSpan))
        {
            var message = new Vst3HostMessage();
            obj = message.ComPtr;
            return Vst3HostNative.KResultOk;
        }

        if (Vst3HostNative.IidEquals(Vst3HostNative.IAttributeListIid, cidSpan)
            && Vst3HostNative.IidEquals(Vst3HostNative.IAttributeListIid, iidSpan))
        {
            var attrs = new Vst3HostAttributeList();
            obj = attrs.ComPtr;
            return Vst3HostNative.KResultOk;
        }

        if (Vst3HostNative.IidEquals(Vst3HostNative.IBStreamIid, cidSpan)
            || Vst3HostNative.IidEquals(Vst3HostNative.IBStreamIid, iidSpan))
        {
            // 呼び出し側が Release する。GC 追跡は簡易のためリーク許容（稀な経路）。
            var stream = new Vst3MemoryStream();
            obj = stream.ComPtr;
            return Vst3HostNative.KResultOk;
        }

        return Vst3HostNative.KResultFalse;
    }

    private unsafe int IsPlugInterfaceSupported(IntPtr self, byte* iid)
    {
        var span = new ReadOnlySpan<byte>(iid, 16);
        if (Vst3HostNative.IidEquals(Vst3HostNative.IComponentIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.IAudioProcessorIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.IEditControllerIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.IPlugViewIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.IConnectionPointIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.IBStreamIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.IHostApplicationIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.IComponentHandlerIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.IMessageIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.IAttributeListIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.IParameterChangesIid, span))
        {
            return Vst3HostNative.KResultTrue;
        }

        return Vst3HostNative.KResultFalse;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int GetNameFn(IntPtr self, char* name);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int CreateInstanceFn(IntPtr self, byte* cid, byte* iid, out IntPtr obj);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int IsSupportedFn(IntPtr self, byte* iid);
}

/// <summary>
/// IComponentHandler。UI の performEdit をプロセッサへ渡すためのキューに積む。
/// </summary>
internal unsafe sealed class Vst3ComponentHandler : IDisposable
{
    private IntPtr _com;
    private GCHandle _selfHandle;
    private bool _disposed;
    private int _refCount = 1;
    private readonly Vst3HostNative.QiFn _qi;
    private readonly Vst3HostNative.AddRefFn _addRef;
    private readonly Vst3HostNative.ReleaseFn _release;
    private readonly EditFn _beginEdit;
    private readonly PerformEditFn _performEdit;
    private readonly EditFn _endEdit;
    private readonly RestartFn _restart;

    public Vst3ComponentHandler()
    {
        _qi = QueryInterface;
        _addRef = AddRef;
        _release = Release;
        _beginEdit = (_, _) => Vst3HostNative.KResultOk;
        _performEdit = PerformEdit;
        _endEdit = (_, _) => Vst3HostNative.KResultOk;
        _restart = (_, _) => Vst3HostNative.KResultOk;
        _selfHandle = GCHandle.Alloc(this);
        _com = Build();
    }

    public IntPtr ComPtr => _com;

    /// <summary>UI スレッドから来るパラメータ編集。プロセッサが Process で吸い上げる。</summary>
    public Action<uint, double>? ParameterEdited { get; set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ParameterEdited = null;
        if (_com != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Marshal.ReadIntPtr(_com));
            Marshal.FreeHGlobal(_com);
            _com = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private IntPtr Build()
    {
        var obj = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var vt = Marshal.AllocHGlobal(IntPtr.Size * 7);
        Marshal.WriteIntPtr(vt, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_qi));
        Marshal.WriteIntPtr(vt, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
        Marshal.WriteIntPtr(vt, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_release));
        Marshal.WriteIntPtr(vt, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_beginEdit));
        Marshal.WriteIntPtr(vt, 4 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_performEdit));
        Marshal.WriteIntPtr(vt, 5 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_endEdit));
        Marshal.WriteIntPtr(vt, 6 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_restart));
        Marshal.WriteIntPtr(obj, 0, vt);
        Marshal.WriteIntPtr(obj, IntPtr.Size, GCHandle.ToIntPtr(_selfHandle));
        return obj;
    }

    private static Vst3ComponentHandler From(IntPtr self) =>
        (Vst3ComponentHandler)GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size)).Target!;

    private int PerformEdit(IntPtr self, uint id, double valueNormalized)
    {
        try
        {
            From(self).ParameterEdited?.Invoke(id, valueNormalized);
        }
        catch
        {
        }

        return Vst3HostNative.KResultOk;
    }

    private int QueryInterface(IntPtr self, byte* iid, out IntPtr obj)
    {
        obj = IntPtr.Zero;
        var span = new ReadOnlySpan<byte>(iid, 16);
        if (Vst3HostNative.IidEquals(Vst3HostNative.IComponentHandlerIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.FUnknownIid, span))
        {
            obj = self;
            AddRef(self);
            return Vst3HostNative.KResultOk;
        }

        return unchecked((int)0x80004002);
    }

    private uint AddRef(IntPtr self) => (uint)Interlocked.Increment(ref From(self)._refCount);

    private uint Release(IntPtr self) => (uint)Math.Max(0, Interlocked.Decrement(ref From(self)._refCount));

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EditFn(IntPtr self, uint id);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PerformEditFn(IntPtr self, uint id, double valueNormalized);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RestartFn(IntPtr self, int flags);
}

/// <summary>IParameterChanges / IParamValueQueue。1 ブロック分の UI→プロセッサ伝達。</summary>
internal unsafe sealed class Vst3ParameterChanges : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<uint, double> _pending = new();
    private readonly List<QueueEntry> _block = new();
    private IntPtr _com;
    private GCHandle _selfHandle;
    private bool _disposed;
    private int _refCount = 1;
    private readonly Vst3HostNative.QiFn _qi;
    private readonly Vst3HostNative.AddRefFn _addRef;
    private readonly Vst3HostNative.ReleaseFn _release;
    private readonly GetParameterCountFn _getCount;
    private readonly GetParameterDataFn _getData;
    private readonly AddParameterDataFn _addData;

    public Vst3ParameterChanges()
    {
        _qi = QueryInterface;
        _addRef = AddRef;
        _release = Release;
        _getCount = GetParameterCount;
        _getData = GetParameterData;
        _addData = AddParameterData;
        _selfHandle = GCHandle.Alloc(this);
        _com = Build();
    }

    public IntPtr ComPtr => _com;

    public void QueueEdit(uint id, double valueNormalized)
    {
        lock (_gate)
        {
            _pending[id] = valueNormalized;
        }
    }

    /// <summary>Process 直前に呼ぶ。pending をこのブロックのキューへ移す。</summary>
    public void BeginProcessBlock()
    {
        lock (_gate)
        {
            ClearBlockNoLock();
            foreach (var kv in _pending)
            {
                _block.Add(QueueEntry.WithPoint(kv.Key, kv.Value));
            }

            _pending.Clear();
        }
    }

    /// <summary>出力用。プラグインが addParameterData / addPoint する空リスト。</summary>
    public void BeginOutputBlock()
    {
        lock (_gate)
        {
            ClearBlockNoLock();
        }
    }

    public void CollectLastValues(List<(uint Id, double Value)> dest)
    {
        dest.Clear();
        lock (_gate)
        {
            foreach (var q in _block)
            {
                if (q.TryGetLastValue(out var value))
                {
                    dest.Add((q.ParameterId, value));
                }
            }
        }
    }

    private void ClearBlockNoLock()
    {
        foreach (var q in _block)
        {
            q.Dispose();
        }

        _block.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            foreach (var q in _block)
            {
                q.Dispose();
            }

            _block.Clear();
            _pending.Clear();
        }

        if (_com != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Marshal.ReadIntPtr(_com));
            Marshal.FreeHGlobal(_com);
            _com = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private IntPtr Build()
    {
        var obj = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var vt = Marshal.AllocHGlobal(IntPtr.Size * 6);
        Marshal.WriteIntPtr(vt, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_qi));
        Marshal.WriteIntPtr(vt, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
        Marshal.WriteIntPtr(vt, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_release));
        Marshal.WriteIntPtr(vt, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getCount));
        Marshal.WriteIntPtr(vt, 4 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getData));
        Marshal.WriteIntPtr(vt, 5 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addData));
        Marshal.WriteIntPtr(obj, 0, vt);
        Marshal.WriteIntPtr(obj, IntPtr.Size, GCHandle.ToIntPtr(_selfHandle));
        return obj;
    }

    private static Vst3ParameterChanges From(IntPtr self) =>
        (Vst3ParameterChanges)GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size)).Target!;

    private int QueryInterface(IntPtr self, byte* iid, out IntPtr obj)
    {
        obj = IntPtr.Zero;
        var span = new ReadOnlySpan<byte>(iid, 16);
        if (Vst3HostNative.IidEquals(Vst3HostNative.IParameterChangesIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.FUnknownIid, span))
        {
            obj = self;
            AddRef(self);
            return Vst3HostNative.KResultOk;
        }

        return unchecked((int)0x80004002);
    }

    private uint AddRef(IntPtr self) => (uint)Interlocked.Increment(ref From(self)._refCount);

    private uint Release(IntPtr self) => (uint)Math.Max(0, Interlocked.Decrement(ref From(self)._refCount));

    private int GetParameterCount(IntPtr self)
    {
        var me = From(self);
        lock (me._gate)
        {
            return me._block.Count;
        }
    }

    private IntPtr GetParameterData(IntPtr self, int index)
    {
        var me = From(self);
        lock (me._gate)
        {
            if ((uint)index >= (uint)me._block.Count)
            {
                return IntPtr.Zero;
            }

            var q = me._block[index];
            q.AddRefPublic();
            return q.ComPtr;
        }
    }

    private IntPtr AddParameterData(IntPtr self, uint* id, int* index)
    {
        if (id is null || index is null)
        {
            return IntPtr.Zero;
        }

        var me = From(self);
        lock (me._gate)
        {
            var entry = QueueEntry.Empty(*id);
            *index = me._block.Count;
            me._block.Add(entry);
            entry.AddRefPublic();
            return entry.ComPtr;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetParameterCountFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetParameterDataFn(IntPtr self, int index);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate IntPtr AddParameterDataFn(IntPtr self, uint* id, int* index);

    private sealed class QueueEntry : IDisposable
    {
        private IntPtr _com;
        private GCHandle _selfHandle;
        private bool _disposed;
        private int _refCount = 1;
        private readonly uint _id;
        private double _value;
        private int _sampleOffset;
        private int _pointCount;
        private readonly Vst3HostNative.QiFn _qi;
        private readonly Vst3HostNative.AddRefFn _addRef;
        private readonly Vst3HostNative.ReleaseFn _release;
        private readonly GetIdFn _getId;
        private readonly GetPointCountFn _getPointCount;
        private readonly GetPointFn _getPoint;
        private readonly AddPointFn _addPoint;

        private QueueEntry(uint id, double value, int pointCount)
        {
            _id = id;
            _value = value;
            _pointCount = pointCount;
            _qi = QueryInterface;
            _addRef = AddRef;
            _release = Release;
            _getId = GetId;
            _getPointCount = GetPointCount;
            _getPoint = GetPoint;
            _addPoint = AddPoint;
            _selfHandle = GCHandle.Alloc(this);
            _com = Build();
        }

        public static QueueEntry WithPoint(uint id, double value) => new(id, value, 1);

        public static QueueEntry Empty(uint id) => new(id, 0, 0);

        public uint ParameterId => _id;

        public IntPtr ComPtr => _com;

        public bool TryGetLastValue(out double value)
        {
            value = _value;
            return _pointCount > 0;
        }

        public void AddRefPublic() => AddRef(_com);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_com != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Marshal.ReadIntPtr(_com));
                Marshal.FreeHGlobal(_com);
                _com = IntPtr.Zero;
            }

            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }
        }

        private IntPtr Build()
        {
            var obj = Marshal.AllocHGlobal(IntPtr.Size * 2);
            var vt = Marshal.AllocHGlobal(IntPtr.Size * 7);
            Marshal.WriteIntPtr(vt, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_qi));
            Marshal.WriteIntPtr(vt, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
            Marshal.WriteIntPtr(vt, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_release));
            Marshal.WriteIntPtr(vt, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getId));
            Marshal.WriteIntPtr(vt, 4 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getPointCount));
            Marshal.WriteIntPtr(vt, 5 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getPoint));
            Marshal.WriteIntPtr(vt, 6 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addPoint));
            Marshal.WriteIntPtr(obj, 0, vt);
            Marshal.WriteIntPtr(obj, IntPtr.Size, GCHandle.ToIntPtr(_selfHandle));
            return obj;
        }

        private static QueueEntry From(IntPtr self) =>
            (QueueEntry)GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size)).Target!;

        private int QueryInterface(IntPtr self, byte* iid, out IntPtr obj)
        {
            obj = IntPtr.Zero;
            var span = new ReadOnlySpan<byte>(iid, 16);
            if (Vst3HostNative.IidEquals(Vst3HostNative.IParamValueQueueIid, span)
                || Vst3HostNative.IidEquals(Vst3HostNative.FUnknownIid, span))
            {
                obj = self;
                AddRef(self);
                return Vst3HostNative.KResultOk;
            }

            return unchecked((int)0x80004002);
        }

        private uint AddRef(IntPtr self) => (uint)Interlocked.Increment(ref From(self)._refCount);

        private uint Release(IntPtr self) => (uint)Math.Max(0, Interlocked.Decrement(ref From(self)._refCount));

        private uint GetId(IntPtr self) => From(self)._id;

        private int GetPointCount(IntPtr self) => From(self)._pointCount;

        private int GetPoint(IntPtr self, int index, int* sampleOffset, double* value)
        {
            var me = From(self);
            if ((uint)index >= (uint)me._pointCount || sampleOffset is null || value is null)
            {
                return Vst3HostNative.KInvalidArgument;
            }

            *sampleOffset = me._sampleOffset;
            *value = me._value;
            return Vst3HostNative.KResultOk;
        }

        private int AddPoint(IntPtr self, int sampleOffset, double value, int* index)
        {
            if (index is null)
            {
                return Vst3HostNative.KInvalidArgument;
            }

            var me = From(self);
            me._sampleOffset = sampleOffset;
            me._value = value;
            me._pointCount = 1;
            *index = 0;
            return Vst3HostNative.KResultOk;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint GetIdFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetPointCountFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int GetPointFn(IntPtr self, int index, int* sampleOffset, double* value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int AddPointFn(IntPtr self, int sampleOffset, double value, int* index);
    }
}

/// <summary>ホストが createInstance する IMessage。プロセッサ→コントローラの私有通信。</summary>
internal unsafe sealed class Vst3HostMessage : IDisposable
{
    private IntPtr _com;
    private GCHandle _selfHandle;
    private bool _disposed;
    private int _refCount = 1;
    private IntPtr _idPtr;
    private Vst3HostAttributeList? _attributes;
    private readonly Vst3HostNative.QiFn _qi;
    private readonly Vst3HostNative.AddRefFn _addRef;
    private readonly Vst3HostNative.ReleaseFn _release;
    private readonly GetMessageIdFn _getId;
    private readonly SetMessageIdFn _setId;
    private readonly GetAttributesFn _getAttributes;

    public Vst3HostMessage()
    {
        _qi = QueryInterface;
        _addRef = AddRef;
        _release = Release;
        _getId = GetMessageId;
        _setId = SetMessageId;
        _getAttributes = GetAttributes;
        _selfHandle = GCHandle.Alloc(this);
        _com = Build();
    }

    public IntPtr ComPtr => _com;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        FreeId();
        _attributes?.Dispose();
        _attributes = null;
        if (_com != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Marshal.ReadIntPtr(_com));
            Marshal.FreeHGlobal(_com);
            _com = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private IntPtr Build()
    {
        var obj = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var vt = Marshal.AllocHGlobal(IntPtr.Size * 6);
        Marshal.WriteIntPtr(vt, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_qi));
        Marshal.WriteIntPtr(vt, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
        Marshal.WriteIntPtr(vt, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_release));
        Marshal.WriteIntPtr(vt, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getId));
        Marshal.WriteIntPtr(vt, 4 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_setId));
        Marshal.WriteIntPtr(vt, 5 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getAttributes));
        Marshal.WriteIntPtr(obj, 0, vt);
        Marshal.WriteIntPtr(obj, IntPtr.Size, GCHandle.ToIntPtr(_selfHandle));
        return obj;
    }

    private static Vst3HostMessage From(IntPtr self) =>
        (Vst3HostMessage)GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size)).Target!;

    private void FreeId()
    {
        if (_idPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_idPtr);
            _idPtr = IntPtr.Zero;
        }
    }

    private int QueryInterface(IntPtr self, byte* iid, out IntPtr obj)
    {
        obj = IntPtr.Zero;
        var span = new ReadOnlySpan<byte>(iid, 16);
        if (Vst3HostNative.IidEquals(Vst3HostNative.IMessageIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.FUnknownIid, span))
        {
            obj = self;
            AddRef(self);
            return Vst3HostNative.KResultOk;
        }

        return unchecked((int)0x80004002);
    }

    private uint AddRef(IntPtr self) => (uint)Interlocked.Increment(ref From(self)._refCount);

    private uint Release(IntPtr self)
    {
        var me = From(self);
        var n = Interlocked.Decrement(ref me._refCount);
        if (n == 0)
        {
            me.Dispose();
        }

        return (uint)Math.Max(0, n);
    }

    private byte* GetMessageId(IntPtr self) => (byte*)From(self)._idPtr;

    private void SetMessageId(IntPtr self, byte* id)
    {
        var me = From(self);
        me.FreeId();
        if (id == null)
        {
            return;
        }

        var len = 0;
        while (id[len] != 0)
        {
            len++;
        }

        me._idPtr = Marshal.AllocHGlobal(len + 1);
        Buffer.MemoryCopy(id, (void*)me._idPtr, len + 1, len + 1);
    }

    private IntPtr GetAttributes(IntPtr self)
    {
        var me = From(self);
        me._attributes ??= new Vst3HostAttributeList();
        return me._attributes.ComPtr;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate byte* GetMessageIdFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate void SetMessageIdFn(IntPtr self, byte* id);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetAttributesFn(IntPtr self);
}

/// <summary>IAttributeList。IMessage のペイロード。</summary>
internal unsafe sealed class Vst3HostAttributeList : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AttrValue> _values = new(StringComparer.Ordinal);
    private IntPtr _com;
    private GCHandle _selfHandle;
    private bool _disposed;
    private int _refCount = 1;
    private readonly Vst3HostNative.QiFn _qi;
    private readonly Vst3HostNative.AddRefFn _addRef;
    private readonly Vst3HostNative.ReleaseFn _release;
    private readonly SetIntFn _setInt;
    private readonly GetIntFn _getInt;
    private readonly SetFloatFn _setFloat;
    private readonly GetFloatFn _getFloat;
    private readonly SetStringFn _setString;
    private readonly GetStringFn _getString;
    private readonly SetBinaryFn _setBinary;
    private readonly GetBinaryFn _getBinary;

    public Vst3HostAttributeList()
    {
        _qi = QueryInterface;
        _addRef = AddRef;
        _release = Release;
        _setInt = SetInt;
        _getInt = GetInt;
        _setFloat = SetFloat;
        _getFloat = GetFloat;
        _setString = SetString;
        _getString = GetString;
        _setBinary = SetBinary;
        _getBinary = GetBinary;
        _selfHandle = GCHandle.Alloc(this);
        _com = Build();
    }

    public IntPtr ComPtr => _com;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            foreach (var kv in _values)
            {
                kv.Value.Dispose();
            }

            _values.Clear();
        }

        if (_com != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Marshal.ReadIntPtr(_com));
            Marshal.FreeHGlobal(_com);
            _com = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private IntPtr Build()
    {
        var obj = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var vt = Marshal.AllocHGlobal(IntPtr.Size * 11);
        Marshal.WriteIntPtr(vt, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_qi));
        Marshal.WriteIntPtr(vt, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
        Marshal.WriteIntPtr(vt, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_release));
        Marshal.WriteIntPtr(vt, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_setInt));
        Marshal.WriteIntPtr(vt, 4 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getInt));
        Marshal.WriteIntPtr(vt, 5 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_setFloat));
        Marshal.WriteIntPtr(vt, 6 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getFloat));
        Marshal.WriteIntPtr(vt, 7 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_setString));
        Marshal.WriteIntPtr(vt, 8 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getString));
        Marshal.WriteIntPtr(vt, 9 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_setBinary));
        Marshal.WriteIntPtr(vt, 10 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_getBinary));
        Marshal.WriteIntPtr(obj, 0, vt);
        Marshal.WriteIntPtr(obj, IntPtr.Size, GCHandle.ToIntPtr(_selfHandle));
        return obj;
    }

    private static Vst3HostAttributeList From(IntPtr self) =>
        (Vst3HostAttributeList)GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size)).Target!;

    private static string? AttrKey(byte* id)
    {
        if (id == null)
        {
            return null;
        }

        return Marshal.PtrToStringAnsi((IntPtr)id);
    }

    private int QueryInterface(IntPtr self, byte* iid, out IntPtr obj)
    {
        obj = IntPtr.Zero;
        var span = new ReadOnlySpan<byte>(iid, 16);
        if (Vst3HostNative.IidEquals(Vst3HostNative.IAttributeListIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.FUnknownIid, span))
        {
            obj = self;
            AddRef(self);
            return Vst3HostNative.KResultOk;
        }

        return unchecked((int)0x80004002);
    }

    private uint AddRef(IntPtr self) => (uint)Interlocked.Increment(ref From(self)._refCount);

    private uint Release(IntPtr self)
    {
        var me = From(self);
        var n = Interlocked.Decrement(ref me._refCount);
        if (n == 0)
        {
            me.Dispose();
        }

        return (uint)Math.Max(0, n);
    }

    private int SetInt(IntPtr self, byte* id, long value)
    {
        var key = AttrKey(id);
        if (key is null)
        {
            return Vst3HostNative.KInvalidArgument;
        }

        var me = From(self);
        lock (me._gate)
        {
            if (me._values.TryGetValue(key, out var old))
            {
                old.Dispose();
            }

            me._values[key] = AttrValue.Int(value);
        }

        return Vst3HostNative.KResultOk;
    }

    private int GetInt(IntPtr self, byte* id, long* value)
    {
        var key = AttrKey(id);
        if (key is null || value is null)
        {
            return Vst3HostNative.KInvalidArgument;
        }

        var me = From(self);
        lock (me._gate)
        {
            if (!me._values.TryGetValue(key, out var attr) || attr.Kind != AttrKind.Int)
            {
                return Vst3HostNative.KResultFalse;
            }

            *value = attr.IntValue;
            return Vst3HostNative.KResultOk;
        }
    }

    private int SetFloat(IntPtr self, byte* id, double value)
    {
        var key = AttrKey(id);
        if (key is null)
        {
            return Vst3HostNative.KInvalidArgument;
        }

        var me = From(self);
        lock (me._gate)
        {
            if (me._values.TryGetValue(key, out var old))
            {
                old.Dispose();
            }

            me._values[key] = AttrValue.Float(value);
        }

        return Vst3HostNative.KResultOk;
    }

    private int GetFloat(IntPtr self, byte* id, double* value)
    {
        var key = AttrKey(id);
        if (key is null || value is null)
        {
            return Vst3HostNative.KInvalidArgument;
        }

        var me = From(self);
        lock (me._gate)
        {
            if (!me._values.TryGetValue(key, out var attr) || attr.Kind != AttrKind.Float)
            {
                return Vst3HostNative.KResultFalse;
            }

            *value = attr.FloatValue;
            return Vst3HostNative.KResultOk;
        }
    }

    private int SetString(IntPtr self, byte* id, char* value)
    {
        var key = AttrKey(id);
        if (key is null || value is null)
        {
            return Vst3HostNative.KInvalidArgument;
        }

        var me = From(self);
        lock (me._gate)
        {
            if (me._values.TryGetValue(key, out var old))
            {
                old.Dispose();
            }

            me._values[key] = AttrValue.String(Marshal.PtrToStringUni((IntPtr)value) ?? string.Empty);
        }

        return Vst3HostNative.KResultOk;
    }

    private int GetString(IntPtr self, byte* id, char* value, uint sizeInBytes)
    {
        var key = AttrKey(id);
        if (key is null || value is null || sizeInBytes < sizeof(char))
        {
            return Vst3HostNative.KInvalidArgument;
        }

        var me = From(self);
        lock (me._gate)
        {
            if (!me._values.TryGetValue(key, out var attr) || attr.Kind != AttrKind.String)
            {
                return Vst3HostNative.KResultFalse;
            }

            var chars = Math.Max(1, (int)(sizeInBytes / sizeof(char)));
            var src = attr.StringValue ?? string.Empty;
            var n = Math.Min(src.Length, chars - 1);
            for (var i = 0; i < n; i++)
            {
                value[i] = src[i];
            }

            value[n] = '\0';
            return Vst3HostNative.KResultOk;
        }
    }

    private int SetBinary(IntPtr self, byte* id, void* data, uint sizeInBytes)
    {
        var key = AttrKey(id);
        if (key is null || data is null)
        {
            return Vst3HostNative.KInvalidArgument;
        }

        var me = From(self);
        var copy = new byte[sizeInBytes];
        if (sizeInBytes > 0)
        {
            Marshal.Copy((IntPtr)data, copy, 0, (int)sizeInBytes);
        }

        lock (me._gate)
        {
            if (me._values.TryGetValue(key, out var old))
            {
                old.Dispose();
            }

            me._values[key] = AttrValue.Binary(copy);
        }

        return Vst3HostNative.KResultOk;
    }

    private int GetBinary(IntPtr self, byte* id, void** data, uint* sizeInBytes)
    {
        var key = AttrKey(id);
        if (key is null || data is null || sizeInBytes is null)
        {
            return Vst3HostNative.KInvalidArgument;
        }

        var me = From(self);
        lock (me._gate)
        {
            if (!me._values.TryGetValue(key, out var attr) || attr.Kind != AttrKind.Binary)
            {
                *data = null;
                *sizeInBytes = 0;
                return Vst3HostNative.KResultFalse;
            }

            *data = (void*)attr.BinaryPtr;
            *sizeInBytes = (uint)attr.BinaryLength;
            return Vst3HostNative.KResultOk;
        }
    }

    private enum AttrKind
    {
        Int,
        Float,
        String,
        Binary,
    }

    private sealed class AttrValue : IDisposable
    {
        public AttrKind Kind { get; private init; }
        public long IntValue { get; private init; }
        public double FloatValue { get; private init; }
        public string? StringValue { get; private init; }
        public IntPtr BinaryPtr { get; private init; }
        public int BinaryLength { get; private init; }

        public static AttrValue Int(long v) => new() { Kind = AttrKind.Int, IntValue = v };

        public static AttrValue Float(double v) => new() { Kind = AttrKind.Float, FloatValue = v };

        public static AttrValue String(string v) => new() { Kind = AttrKind.String, StringValue = v };

        public static AttrValue Binary(byte[] data)
        {
            var ptr = data.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(data.Length);
            if (data.Length > 0)
            {
                Marshal.Copy(data, 0, ptr, data.Length);
            }

            return new AttrValue
            {
                Kind = AttrKind.Binary,
                BinaryPtr = ptr,
                BinaryLength = data.Length,
            };
        }

        public void Dispose()
        {
            if (BinaryPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(BinaryPtr);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int SetIntFn(IntPtr self, byte* id, long value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int GetIntFn(IntPtr self, byte* id, long* value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int SetFloatFn(IntPtr self, byte* id, double value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int GetFloatFn(IntPtr self, byte* id, double* value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int SetStringFn(IntPtr self, byte* id, char* value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int GetStringFn(IntPtr self, byte* id, char* value, uint sizeInBytes);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int SetBinaryFn(IntPtr self, byte* id, void* data, uint sizeInBytes);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int GetBinaryFn(IntPtr self, byte* id, void** data, uint* sizeInBytes);
}

/// <summary>IPlugFrame。プラグインからのリサイズ要求をウィンドウへ伝える。</summary>
internal unsafe sealed class Vst3PlugFrame : IDisposable
{
    private IntPtr _com;
    private GCHandle _selfHandle;
    private bool _disposed;
    private int _refCount = 1;
    private readonly Vst3HostNative.QiFn _qi;
    private readonly Vst3HostNative.AddRefFn _addRef;
    private readonly Vst3HostNative.ReleaseFn _release;
    private readonly ResizeViewFn _resizeView;
    public Action<Vst3HostNative.ViewRect>? ResizeRequested;

    public Vst3PlugFrame()
    {
        _qi = QueryInterface;
        _addRef = AddRef;
        _release = Release;
        _resizeView = ResizeView;
        _selfHandle = GCHandle.Alloc(this);
        _com = Build();
    }

    public IntPtr ComPtr => _com;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ResizeRequested = null;
        if (_com != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Marshal.ReadIntPtr(_com));
            Marshal.FreeHGlobal(_com);
            _com = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private IntPtr Build()
    {
        var obj = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var vt = Marshal.AllocHGlobal(IntPtr.Size * 4);
        Marshal.WriteIntPtr(vt, 0 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_qi));
        Marshal.WriteIntPtr(vt, 1 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
        Marshal.WriteIntPtr(vt, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_release));
        Marshal.WriteIntPtr(vt, 3 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(_resizeView));
        Marshal.WriteIntPtr(obj, 0, vt);
        Marshal.WriteIntPtr(obj, IntPtr.Size, GCHandle.ToIntPtr(_selfHandle));
        return obj;
    }

    private static Vst3PlugFrame From(IntPtr self) =>
        (Vst3PlugFrame)GCHandle.FromIntPtr(Marshal.ReadIntPtr(self, IntPtr.Size)).Target!;

    private int QueryInterface(IntPtr self, byte* iid, out IntPtr obj)
    {
        obj = IntPtr.Zero;
        var span = new ReadOnlySpan<byte>(iid, 16);
        if (Vst3HostNative.IidEquals(Vst3HostNative.IPlugFrameIid, span)
            || Vst3HostNative.IidEquals(Vst3HostNative.FUnknownIid, span))
        {
            obj = self;
            AddRef(self);
            return Vst3HostNative.KResultOk;
        }

        return unchecked((int)0x80004002);
    }

    private uint AddRef(IntPtr self) => (uint)Interlocked.Increment(ref From(self)._refCount);

    private uint Release(IntPtr self) => (uint)Math.Max(0, Interlocked.Decrement(ref From(self)._refCount));

    private int ResizeView(IntPtr self, IntPtr view, Vst3HostNative.ViewRect* newSize)
    {
        if (newSize == null)
        {
            return Vst3HostNative.KInvalidArgument;
        }

        var rect = *newSize;
        From(self).ResizeRequested?.Invoke(rect);
        return Vst3HostNative.KResultOk;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int ResizeViewFn(IntPtr self, IntPtr view, Vst3HostNative.ViewRect* newSize);
}
