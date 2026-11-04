using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>ロード済み .vst3 モジュール。ファクトリとクラス一覧。</summary>
internal sealed class Vst3Module : IDisposable
{
    private static readonly ConcurrentDictionary<string, Vst3Module> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>WaveShell の setHostContext 用。工場より長く生存させる。</summary>
    private static readonly Vst3HostApplication SharedFactoryHost = new();

    private readonly string _binaryPath;
    private IntPtr _module;
    private IntPtr _factory;
    private bool _disposed;
    private int _refCount = 1;

    private Vst3Module(string binaryPath, IntPtr module, IntPtr factory)
    {
        _binaryPath = binaryPath;
        _module = module;
        _factory = factory;
        // Waves: setHostContext の前後でクラス一覧が変わる。必ず先に呼ぶ。
        SetHostContext(factory, SharedFactoryHost.ComPtr);
        Classes = ReadClasses(factory);
    }

    public bool IsWaveShell => Vst3FactoryProbe.IsWaveShellPath(_binaryPath);

    public string BinaryPath => _binaryPath;

    public IReadOnlyList<Vst3ClassInfo> Classes { get; }

    public IntPtr Factory => _factory;

    public static Vst3Module? TryAcquire(string pluginPath)
    {
        var acquired = TryAcquireResolved(pluginPath);
        if (acquired is not null)
        {
            return acquired;
        }

        // 認識リストが x86 パスでも、64bit ホストでは Program Files 側を試す（Stutter Edit 等）。
        if (TryMapX86CommonFilesToProgramFiles(pluginPath, out var alt64)
            && !string.Equals(alt64, pluginPath, StringComparison.OrdinalIgnoreCase))
        {
            return TryAcquireResolved(alt64);
        }

        return null;
    }

    private static Vst3Module? TryAcquireResolved(string pluginPath)
    {
        if (!Vst3FactoryProbe.TryResolveBinary(pluginPath, out var binary))
        {
            return null;
        }

        // WaveShell は拒否しない（Waves プラグインはシェル内クラスとして展開する）
        var key = Path.GetFullPath(binary);
        if (Cache.TryGetValue(key, out var existing) && !existing._disposed)
        {
            Interlocked.Increment(ref existing._refCount);
            return existing;
        }

        // セッション復元経由でも Altiverb をデバッガ下で読むと InitDll から戻らない
        if (Vst3DebuggerLoadGuard.ShouldRefuseUnderDebugger(key))
        {
            return null;
        }

        var module = Vst3HostNative.LoadLibraryEx(key, IntPtr.Zero, Vst3HostNative.LoadWithAlteredSearchPath);
        if (module == IntPtr.Zero)
        {
            return null;
        }

        var factory = InitAndGetFactory(module);
        if (factory == IntPtr.Zero)
        {
            return null;
        }

        var created = new Vst3Module(key, module, factory);
        Cache[key] = created;
        return created;
    }

    /// <summary>
    /// <c>Program Files (x86)\Common Files\VST3\...</c> を
    /// <c>Program Files\Common Files\VST3\...</c> に写す。
    /// </summary>
    internal static bool TryMapX86CommonFilesToProgramFiles(string pluginPath, out string mapped)
    {
        mapped = string.Empty;
        if (string.IsNullOrWhiteSpace(pluginPath))
        {
            return false;
        }

        const string marker = @"\Program Files (x86)\Common Files\VST3";
        var idx = pluginPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return false;
        }

        mapped = string.Concat(
            pluginPath.AsSpan(0, idx),
            @"\Program Files\Common Files\VST3",
            pluginPath.AsSpan(idx + marker.Length));
        return File.Exists(mapped) || Directory.Exists(mapped);
    }

    /// <summary>InitDll → GetPluginFactory。失敗時は Zero（モジュールは解放済み）。</summary>
    private static IntPtr InitAndGetFactory(IntPtr module)
    {
        // VST3 Windows: InitDll → GetPluginFactory。Init 無しだとインスタンスが動かないことが多い。
        try
        {
            CallInitDll(module);
        }
        catch
        {
            Vst3HostNative.FreeLibrary(module);
            return IntPtr.Zero;
        }

        var proc = Vst3HostNative.GetProcAddress(module, "GetPluginFactory");
        if (proc == IntPtr.Zero)
        {
            CallExitDll(module);
            Vst3HostNative.FreeLibrary(module);
            return IntPtr.Zero;
        }

        var getFactory = Marshal.GetDelegateForFunctionPointer<Vst3HostNative.GetFactoryFn>(proc);
        IntPtr factory;
        try
        {
            factory = getFactory();
        }
        catch
        {
            factory = IntPtr.Zero;
        }

        if (factory == IntPtr.Zero)
        {
            CallExitDll(module);
            Vst3HostNative.FreeLibrary(module);
        }

        return factory;
    }

    public void AddRef() => Interlocked.Increment(ref _refCount);

    public void Dispose()
    {
        if (Interlocked.Decrement(ref _refCount) > 0)
        {
            return;
        }

        Cache.TryRemove(_binaryPath, out _);
        if (_factory != IntPtr.Zero)
        {
            Vst3HostNative.Release(_factory);
            _factory = IntPtr.Zero;
        }

        if (_module != IntPtr.Zero)
        {
            try
            {
                CallExitDll(_module);
            }
            catch
            {
            }

            Vst3HostNative.FreeLibrary(_module);
            _module = IntPtr.Zero;
        }

        _disposed = true;
    }

    private static void CallInitDll(IntPtr module)
    {
        var proc = Vst3HostNative.GetProcAddress(module, "InitDll");
        if (proc == IntPtr.Zero)
        {
            return;
        }

        var init = Marshal.GetDelegateForFunctionPointer<InitExitDllFn>(proc);
        if (!init())
        {
            throw new InvalidOperationException("InitDll failed");
        }
    }

    private static void CallExitDll(IntPtr module)
    {
        var proc = Vst3HostNative.GetProcAddress(module, "ExitDll");
        if (proc == IntPtr.Zero)
        {
            return;
        }

        var exit = Marshal.GetDelegateForFunctionPointer<InitExitDllFn>(proc);
        exit();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool InitExitDllFn();

    public bool TryResolveClassId(
        string? preferredBase64,
        string? preferredName,
        out byte[] cid,
        out Vst3ClassInfo info)
    {
        cid = new byte[16];
        info = default;
        if (Classes.Count == 0)
        {
            return false;
        }

        if (Vst3HostNative.TryParseClassId(preferredBase64, cid))
        {
            foreach (var c in Classes)
            {
                if (c.Cid.AsSpan().SequenceEqual(cid))
                {
                    info = c;
                    return true;
                }
            }
        }

        // WaveShell は名前で特定（インデックスは setHostContext 後にずれるため使わない）
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            foreach (var c in Classes)
            {
                if (string.Equals(c.Name, preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    c.Cid.CopyTo(cid, 0);
                    info = c;
                    return true;
                }
            }

            foreach (var c in Classes)
            {
                if (c.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase)
                    || preferredName.Contains(c.Name, StringComparison.OrdinalIgnoreCase))
                {
                    c.Cid.CopyTo(cid, 0);
                    info = c;
                    return true;
                }
            }
        }

        // シェル全体を選んだ場合のフォールバックは危険（別プラグインが載る）ので WaveShell では失敗
        if (IsWaveShell)
        {
            return false;
        }

        var preferred = Classes
            .OrderBy(c => Vst3EffectClass.NamePreference(c.Category))
            .ThenByDescending(c => c.Name.Length)
            .First();
        preferred.Cid.CopyTo(cid, 0);
        info = preferred;
        return true;
    }

    private static void SetHostContext(IntPtr factory, IntPtr hostContext)
    {
        if (factory == IntPtr.Zero || hostContext == IntPtr.Zero)
        {
            return;
        }

        if (Vst3HostNative.QueryInterface(factory, Vst3HostNative.IPluginFactory3Iid, out var factory3)
                != Vst3HostNative.KResultOk
            || factory3 == IntPtr.Zero)
        {
            return;
        }

        try
        {
            unsafe
            {
                var vt = *(IntPtr**)factory3;
                // IPluginFactory3: 8=getClassInfoUnicode, 9=setHostContext
                var setHost = Marshal.GetDelegateForFunctionPointer<SetHostContextFn>(vt[9]);
                setHost(factory3, hostContext);
            }
        }
        finally
        {
            Vst3HostNative.Release(factory3);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetHostContextFn(IntPtr self, IntPtr context);

    public IntPtr CreateInstance(byte[] cid, byte[] iid)
    {
        if (_factory == IntPtr.Zero || cid.Length != 16 || iid.Length != 16)
        {
            return IntPtr.Zero;
        }

        unsafe
        {
            var vt = *(IntPtr**)_factory;
            // IPluginFactory.createInstance = index 6
            var create = Marshal.GetDelegateForFunctionPointer<CreateInstanceFn>(vt[6]);
            fixed (byte* cidPtr = cid)
            fixed (byte* iidPtr = iid)
            {
                IntPtr obj = IntPtr.Zero;
                var result = create(_factory, cidPtr, iidPtr, &obj);
                // 戻り値が Ok でなくても obj が付く実装がある
                if (obj != IntPtr.Zero)
                {
                    return obj;
                }

                _ = result;
                return IntPtr.Zero;
            }
        }
    }

    private static List<Vst3ClassInfo> ReadClasses(IntPtr factory)
    {
        var list = new List<Vst3ClassInfo>();
        var targets = new List<(IntPtr Ptr, bool Release)> { (factory, false) };
        if (Vst3HostNative.QueryInterface(factory, Vst3HostNative.IPluginFactory2Iid, out var factory2)
            == Vst3HostNative.KResultOk
            && factory2 != IntPtr.Zero)
        {
            targets.Add((factory2, true));
        }

        try
        {
            // Factory2 があればそちらだけ（ベース工場との二重登録を避ける）
            var source = targets.Count > 1 ? targets[^1].Ptr : targets[0].Ptr;
            var count = CountClasses(source);
            for (var i = 0; i < count; i++)
            {
                if (!TryGetClassInfo2(source, i, out var info)
                    && !TryGetClassInfo(source, i, out info))
                {
                    continue;
                }

                if (!Vst3EffectClass.IsUsableEffectClass(info.Category, info.SubCategories))
                {
                    continue;
                }

                // Instrument / OnlyARA はアプリ側で認識除外済みでも、保険でスキップ
                // （Instrument|Fx も楽器扱い。旧判定の「Fx を含むなら通す」は使わない）
                if (Vst3PluginMetadata.IsInstrumentCategory(info.SubCategories)
                    || Vst3PluginMetadata.IsOnlyAraCategory(info.SubCategories))
                {
                    continue;
                }

                list.Add(info);
            }
        }
        finally
        {
            foreach (var (ptr, release) in targets)
            {
                if (release)
                {
                    Vst3HostNative.Release(ptr);
                }
            }
        }

        return list;
    }

    private static int CountClasses(IntPtr factory)
    {
        unsafe
        {
            var vt = *(IntPtr**)factory;
            var countClasses = Marshal.GetDelegateForFunctionPointer<CountClassesFn>(vt[4]);
            return countClasses(factory);
        }
    }

    private static bool TryGetClassInfo2(IntPtr factory2, int index, out Vst3ClassInfo info)
    {
        info = default;
        unsafe
        {
            var vt = *(IntPtr**)factory2;
            var getClassInfo2 = Marshal.GetDelegateForFunctionPointer<GetClassInfo2Fn>(vt[7]);
            var raw = new byte[Vst3ClassInfo.RawSize];
            fixed (byte* p = raw)
            {
                if (getClassInfo2(factory2, index, p) != Vst3HostNative.KResultOk)
                {
                    return false;
                }
            }

            info = Vst3ClassInfo.Parse(raw);
            return true;
        }
    }

    private static bool TryGetClassInfo(IntPtr factory, int index, out Vst3ClassInfo info)
    {
        info = default;
        unsafe
        {
            var vt = *(IntPtr**)factory;
            var getClassInfo = Marshal.GetDelegateForFunctionPointer<GetClassInfoFn>(vt[5]);
            var raw = new byte[Vst3ClassInfo.PClassInfoSize];
            fixed (byte* p = raw)
            {
                if (getClassInfo(factory, index, p) != Vst3HostNative.KResultOk)
                {
                    return false;
                }
            }

            info = Vst3ClassInfo.ParsePClassInfo(raw);
            return true;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int CreateInstanceFn(IntPtr self, byte* cid, byte* iid, IntPtr* obj);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CountClassesFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int GetClassInfo2Fn(IntPtr self, int index, byte* info);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate int GetClassInfoFn(IntPtr self, int index, byte* info);
}

internal readonly struct Vst3ClassInfo
{
    public const int RawSize = 16 + 4 + 32 + 64 + 4 + 128 + 64 + 64 + 64;
    public const int PClassInfoSize = 16 + 4 + 32 + 64;

    public byte[] Cid { get; init; }
    public string Category { get; init; }
    public string Name { get; init; }
    public string SubCategories { get; init; }
    public string Vendor { get; init; }
    public string Version { get; init; }

    public static Vst3ClassInfo Parse(byte[] raw)
    {
        var cid = new byte[16];
        Buffer.BlockCopy(raw, 0, cid, 0, 16);
        var offset = 16 + 4;
        var category = Vst3HostNative.ReadCString(raw, offset, 32);
        offset += 32;
        var name = Vst3HostNative.ReadCString(raw, offset, 64);
        offset += 64;
        offset += 4;
        var sub = Vst3HostNative.ReadCString(raw, offset, 128);
        offset += 128;
        var vendor = Vst3HostNative.ReadCString(raw, offset, 64);
        offset += 64;
        var version = Vst3HostNative.ReadCString(raw, offset, 64);
        return new Vst3ClassInfo
        {
            Cid = cid,
            Category = category,
            Name = name,
            SubCategories = sub,
            Vendor = vendor,
            Version = version,
        };
    }

    public static Vst3ClassInfo ParsePClassInfo(byte[] raw)
    {
        var cid = new byte[16];
        Buffer.BlockCopy(raw, 0, cid, 0, 16);
        var category = Vst3HostNative.ReadCString(raw, 20, 32);
        var name = Vst3HostNative.ReadCString(raw, 52, 64);
        return new Vst3ClassInfo
        {
            Cid = cid,
            Category = category,
            Name = name,
            SubCategories = category,
            Vendor = string.Empty,
            Version = string.Empty,
        };
    }
}
