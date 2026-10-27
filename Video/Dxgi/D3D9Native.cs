using System.Runtime.InteropServices;

namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// D3DImage 受け渡し用の D3D9Ex 最小 P/Invoke。D3D11 の共有テクスチャを開いて IDirect3DSurface9 を得るだけ。
/// </summary>
internal static unsafe class D3D9Native
{
    public const uint D3D_SDK_VERSION = 32;
    public const int D3DDEVTYPE_HAL = 1;
    public const uint D3DCREATE_FPU_PRESERVE = 0x2;
    public const uint D3DCREATE_MULTITHREADED = 0x4;
    public const uint D3DCREATE_SOFTWARE_VERTEXPROCESSING = 0x20;
    public const uint D3DCREATE_HARDWARE_VERTEXPROCESSING = 0x40;
    public const int D3DSWAPEFFECT_DISCARD = 1;
    public const uint D3DPRESENT_INTERVAL_IMMEDIATE = 0x80000000;
    public const uint D3DUSAGE_RENDERTARGET = 0x1;
    public const int D3DFMT_A8R8G8B8 = 21;
    public const int D3DPOOL_DEFAULT = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DPRESENT_PARAMETERS
    {
        public uint BackBufferWidth;
        public uint BackBufferHeight;
        public int BackBufferFormat;
        public uint BackBufferCount;
        public int MultiSampleType;
        public uint MultiSampleQuality;
        public int SwapEffect;
        public IntPtr hDeviceWindow;
        public int Windowed;
        public int EnableAutoDepthStencil;
        public int AutoDepthStencilFormat;
        public uint Flags;
        public uint FullScreen_RefreshRateInHz;
        public uint PresentationInterval;
    }

    [DllImport("d3d9.dll", ExactSpelling = true)]
    public static extern int Direct3DCreate9Ex(uint sdkVersion, out IntPtr d3d9Ex);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetDesktopWindow();

    private static void* Slot(IntPtr obj, int index) => (*(void***)obj)[index];

    // IDirect3D9Ex
    public static uint D3D9GetAdapterCount(IntPtr d3d9) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(d3d9, 4))(d3d9);

    public static int D3D9CreateDeviceEx(IntPtr d3d9Ex, uint adapter, IntPtr focusWindow, uint behavior, ref D3DPRESENT_PARAMETERS pp, out IntPtr device)
    {
        fixed (D3DPRESENT_PARAMETERS* p = &pp)
        fixed (IntPtr* r = &device)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, uint, int, IntPtr, uint, D3DPRESENT_PARAMETERS*, IntPtr, IntPtr*, int>)
                Slot(d3d9Ex, 20))(d3d9Ex, adapter, D3DDEVTYPE_HAL, focusWindow, behavior, p, IntPtr.Zero, r);
        }
    }

    public static int D3D9GetAdapterLuid(IntPtr d3d9Ex, uint adapter, out LUID luid)
    {
        fixed (LUID* l = &luid)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, uint, LUID*, int>)Slot(d3d9Ex, 21))(d3d9Ex, adapter, l);
        }
    }

    // IDirect3DDevice9::CreateTexture（sharedHandle 非 0 なら既存の共有リソースを開く）
    public static int Device9CreateTexture(IntPtr device, uint width, uint height, uint usage, int format, int pool, out IntPtr texture, IntPtr sharedHandle)
    {
        var handle = sharedHandle;
        fixed (IntPtr* r = &texture)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, uint, int, int, IntPtr*, IntPtr*, int>)Slot(device, 23))(
                device, width, height, 1, usage, format, pool, r, &handle);
        }
    }

    // IDirect3DTexture9::GetSurfaceLevel
    public static int Texture9GetSurfaceLevel(IntPtr texture, uint level, out IntPtr surface)
    {
        fixed (IntPtr* r = &surface)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(texture, 18))(texture, level, r);
        }
    }
}
