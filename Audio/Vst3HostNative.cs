using System.Runtime.InteropServices;
using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>VST3 ホスト用の IID・LoadLibrary・FUnknown ヘルパ。</summary>
internal static class Vst3HostNative
{
    public const int KResultOk = 0;
    public const int KResultTrue = 0;
    public const int KResultFalse = 1;
    public const int KResultNotImplemented = -2147467263; // 0x80004001
    public const int KInvalidArgument = -2147024809; // 0x80070057
    public const uint LoadWithAlteredSearchPath = 0x00000008;

    public const int SymbolicSampleSize32 = 0;
    public const int ProcessModeRealtime = 0;
    public const int MediaTypeAudio = 0;
    public const int BusDirectionInput = 0;
    public const int BusDirectionOutput = 1;
    public const int BusTypeMain = 0;

    /// <summary>kSpeakerL | kSpeakerR</summary>
    public const ulong SpeakerStereo = 0x3;
    public const ulong SpeakerMono = 0x1;

    // IID は Steinberg COM_COMPATIBLE INLINE_UID（DECLARE_CLASS_IID）のバイト列。
    // Windows GUID の並びとは異なるので手で並べ替えないこと。

    // DECLARE_CLASS_IID (IComponent, 0xE831FF31, 0xF2D54301, 0x928EBBEE, 0x25697802)
    public static readonly byte[] IComponentIid =
    [
        0x31, 0xFF, 0x31, 0xE8, 0xD5, 0xF2, 0x01, 0x43,
        0x92, 0x8E, 0xBB, 0xEE, 0x25, 0x69, 0x78, 0x02,
    ];

    // DECLARE_CLASS_IID (IAudioProcessor, 0x42043F99, 0xB7DA453C, 0xA569E79D, 0x9AAEC33D)
    public static readonly byte[] IAudioProcessorIid =
    [
        0x99, 0x3F, 0x04, 0x42, 0xDA, 0xB7, 0x3C, 0x45,
        0xA5, 0x69, 0xE7, 0x9D, 0x9A, 0xAE, 0xC3, 0x3D,
    ];

    // DECLARE_CLASS_IID (IEditController, 0xDCD7BBE3, 0x7742448D, 0xA874AACC, 0x979C759E)
    public static readonly byte[] IEditControllerIid =
    [
        0xE3, 0xBB, 0xD7, 0xDC, 0x42, 0x77, 0x8D, 0x44,
        0xA8, 0x74, 0xAA, 0xCC, 0x97, 0x9C, 0x75, 0x9E,
    ];

    // DECLARE_CLASS_IID (IPlugView, 0x5BC32507, 0xD06049EA, 0xA6151B52, 0x2B755B29)
    public static readonly byte[] IPlugViewIid =
    [
        0x07, 0x25, 0xC3, 0x5B, 0x60, 0xD0, 0xEA, 0x49,
        0xA6, 0x15, 0x1B, 0x52, 0x2B, 0x75, 0x5B, 0x29,
    ];

    // DECLARE_CLASS_IID (IPlugFrame, 0x367FAF01, 0xAFA94693, 0x8D4DA2A0, 0xED0882A3)
    public static readonly byte[] IPlugFrameIid =
    [
        0x01, 0xAF, 0x7F, 0x36, 0xA9, 0xAF, 0x93, 0x46,
        0x8D, 0x4D, 0xA2, 0xA0, 0xED, 0x08, 0x82, 0xA3,
    ];

    // DECLARE_CLASS_IID (IConnectionPoint, 0x70A4156F, 0x6E6E4026, 0x989148BF, 0xAA60D8D1)
    public static readonly byte[] IConnectionPointIid =
    [
        0x6F, 0x15, 0xA4, 0x70, 0x6E, 0x6E, 0x26, 0x40,
        0x98, 0x91, 0x48, 0xBF, 0xAA, 0x60, 0xD8, 0xD1,
    ];

    // DECLARE_CLASS_IID (IComponentHandler, 0x93A0BEA3, 0x0BD045DB, 0x8E890B0C, 0xC1E46AC6)
    public static readonly byte[] IComponentHandlerIid =
    [
        0xA3, 0xBE, 0xA0, 0x93, 0xD0, 0x0B, 0xDB, 0x45,
        0x8E, 0x89, 0x0B, 0x0C, 0xC1, 0xE4, 0x6A, 0xC6,
    ];

    public const string PlatformTypeHwnd = "HWND";
    public const string ViewTypeEditor = "editor";

    [StructLayout(LayoutKind.Sequential)]
    public struct ViewRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Math.Max(0, Right - Left);

        public int Height => Math.Max(0, Bottom - Top);
    }

    // DECLARE_CLASS_IID (IBStream, 0xC3BF6EA2, 0x30994752, 0x9B6BF990, 0x1EE33E9B)
    public static readonly byte[] IBStreamIid =
    [
        0xA2, 0x6E, 0xBF, 0xC3, 0x99, 0x30, 0x52, 0x47,
        0x9B, 0x6B, 0xF9, 0x90, 0x1E, 0xE3, 0x3E, 0x9B,
    ];

    // DECLARE_CLASS_IID (IHostApplication, 0x58E595CC, 0xDB2D4969, 0x8B6AAF8C, 0x36A664E5)
    public static readonly byte[] IHostApplicationIid =
    [
        0xCC, 0x95, 0xE5, 0x58, 0x2D, 0xDB, 0x69, 0x49,
        0x8B, 0x6A, 0xAF, 0x8C, 0x36, 0xA6, 0x64, 0xE5,
    ];

    // IPlugInterfaceSupport（WaveShell setHostContext）。SDK の COM_COMPATIBLE 並び。
    public static readonly byte[] IPlugInterfaceSupportIid =
    [
        0x9E, 0x8B, 0xB5, 0x4F, 0x91, 0x14, 0xB0, 0x44,
        0xB0, 0xDD, 0x1E, 0xE0, 0x21, 0x9D, 0x5B, 0x5F,
    ];

    // DECLARE_CLASS_IID (IPluginFactory, 0x7A4D811C, 0x52114A1F, 0xAED9D2EE, 0x0B43BF9F)
    public static readonly byte[] IPluginFactoryIid =
    [
        0x1C, 0x81, 0x4D, 0x7A, 0x11, 0x52, 0x1F, 0x4A,
        0xAE, 0xD9, 0xD2, 0xEE, 0x0B, 0x43, 0xBF, 0x9F,
    ];

    // DECLARE_CLASS_IID (IPluginFactory2, 0x0007B650, 0xF24B4C0B, 0xA464EDB9, 0xF00B2ABB)
    public static readonly byte[] IPluginFactory2Iid =
    [
        0x50, 0xB6, 0x07, 0x00, 0x4B, 0xF2, 0x0B, 0x4C,
        0xA4, 0x64, 0xED, 0xB9, 0xF0, 0x0B, 0x2A, 0xBB,
    ];

    // DECLARE_CLASS_IID (IPluginFactory3, 0x4555A2AB, 0xC1234E57, 0x9B122910, 0x36878931)
    public static readonly byte[] IPluginFactory3Iid =
    [
        0xAB, 0xA2, 0x55, 0x45, 0x23, 0xC1, 0x57, 0x4E,
        0x9B, 0x12, 0x29, 0x10, 0x36, 0x87, 0x89, 0x31,
    ];

    // FUnknown {00000000-0000-0000-C000-000000000046}
    public static readonly byte[] FUnknownIid =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
    ];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr GetFactoryFn();

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32", SetLastError = true)]
    public static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    public static bool IidEquals(byte[] a, ReadOnlySpan<byte> b)
    {
        if (a.Length != 16 || b.Length != 16)
        {
            return false;
        }

        for (var i = 0; i < 16; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    public static int QueryInterface(IntPtr obj, byte[] iid, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (obj == IntPtr.Zero)
        {
            return -1;
        }

        unsafe
        {
            var vt = *(IntPtr**)obj;
            // out IntPtr ではなく IntPtr* で渡す（out だと void** が壊れる環境がある）
            var qi = Marshal.GetDelegateForFunctionPointer<QiPtrFn>(vt[0]);
            IntPtr local = IntPtr.Zero;
            int code;
            fixed (byte* iidPtr = iid)
            {
                code = qi(obj, iidPtr, &local);
            }

            result = local;
            return code;
        }
    }

    public static uint AddRef(IntPtr obj)
    {
        if (obj == IntPtr.Zero)
        {
            return 0;
        }

        unsafe
        {
            var vt = *(IntPtr**)obj;
            var addRef = Marshal.GetDelegateForFunctionPointer<AddRefFn>(vt[1]);
            return addRef(obj);
        }
    }

    public static uint Release(IntPtr obj)
    {
        if (obj == IntPtr.Zero)
        {
            return 0;
        }

        unsafe
        {
            var vt = *(IntPtr**)obj;
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseFn>(vt[2]);
            return release(obj);
        }
    }

    public static string ClassIdToBase64(ReadOnlySpan<byte> cid)
    {
        if (cid.Length != 16)
        {
            return string.Empty;
        }

        return Convert.ToBase64String(cid);
    }

    public static bool TryParseClassId(string? text, Span<byte> dest)
    {
        if (dest.Length < 16 || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(text);
            if (bytes.Length != 16)
            {
                return false;
            }

            bytes.CopyTo(dest);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string ReadCString(byte[] raw, int offset, int max)
    {
        var end = offset;
        var limit = Math.Min(raw.Length, offset + max);
        while (end < limit && raw[end] != 0)
        {
            end++;
        }

        return end == offset ? string.Empty : Encoding.UTF8.GetString(raw, offset, end - offset).Trim();
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public unsafe delegate int QiFn(IntPtr self, byte* iid, out IntPtr obj);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public unsafe delegate int QiPtrFn(IntPtr self, byte* iid, IntPtr* obj);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate uint AddRefFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate uint ReleaseFn(IntPtr self);

    /// <summary>MSVC x64 の ProcessSetup と一致（int×3 のあと double は 8 バイト境界）。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct ProcessSetup
    {
        [FieldOffset(0)] public int ProcessMode;
        [FieldOffset(4)] public int SymbolicSampleSize;
        [FieldOffset(8)] public int MaxSamplesPerBlock;
        [FieldOffset(16)] public double SampleRate;
    }

    /// <summary>MSVC x64 AudioBusBuffers。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct AudioBusBuffers
    {
        [FieldOffset(0)] public int NumChannels;
        [FieldOffset(8)] public ulong SilenceFlags;
        [FieldOffset(16)] public IntPtr ChannelBuffers;
    }

    /// <summary>MSVC x64 ProcessData（ポインタはオフセット 24 から）。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 80)]
    public struct ProcessData
    {
        [FieldOffset(0)] public int ProcessMode;
        [FieldOffset(4)] public int SymbolicSampleSize;
        [FieldOffset(8)] public int NumSamples;
        [FieldOffset(12)] public int NumInputs;
        [FieldOffset(16)] public int NumOutputs;
        [FieldOffset(24)] public IntPtr Inputs;
        [FieldOffset(32)] public IntPtr Outputs;
        [FieldOffset(40)] public IntPtr InputParameterChanges;
        [FieldOffset(48)] public IntPtr OutputParameterChanges;
        [FieldOffset(56)] public IntPtr InputEvents;
        [FieldOffset(64)] public IntPtr OutputEvents;
        [FieldOffset(72)] public IntPtr ProcessContext;
    }
}
