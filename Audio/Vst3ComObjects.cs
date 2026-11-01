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
        // IBStream 要求にはメモリストリームを返す
        var cidSpan = new ReadOnlySpan<byte>(cid, 16);
        var iidSpan = new ReadOnlySpan<byte>(iid, 16);
        if (Vst3HostNative.IidEquals(Vst3HostNative.IBStreamIid, cidSpan)
            || Vst3HostNative.IidEquals(Vst3HostNative.IBStreamIid, iidSpan))
        {
            // 呼び出し側が Release する。GC 追跡は簡易のためリーク許容（稀な経路）。
            var stream = new Vst3MemoryStream();
            obj = stream.ComPtr;
            return Vst3HostNative.KResultOk;
        }

        return Vst3HostNative.KResultNotImplemented;
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
            || Vst3HostNative.IidEquals(Vst3HostNative.IComponentHandlerIid, span))
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

/// <summary>IComponentHandler の最小実装（パラメータ編集通知を受け流す）。</summary>
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
        _performEdit = (_, _, _) => Vst3HostNative.KResultOk;
        _endEdit = (_, _) => Vst3HostNative.KResultOk;
        _restart = (_, _) => Vst3HostNative.KResultOk;
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
