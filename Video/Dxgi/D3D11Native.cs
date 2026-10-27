using System.Runtime.InteropServices;

namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// D3D11／DXGI／D3DCompiler の最小 P/Invoke（D3D9Ex 側は <see cref="D3D9Native"/>）。COM は vtable 番号で直接呼ぶ（ComImport のマーシャリングを避ける）。
/// </summary>
internal static unsafe class D3D11Native
{
    public const int S_OK = 0;
    public const int D3D_DRIVER_TYPE_HARDWARE = 1;
    public const int D3D_DRIVER_TYPE_WARP = 5;
    public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    public const uint D3D11_SDK_VERSION = 7;

    public const int DXGI_FORMAT_R16G16B16A16_FLOAT = 10;
    public const int DXGI_FORMAT_R10G10B10A2_UNORM = 24;
    public const int DXGI_FORMAT_R8G8B8A8_UNORM = 28;
    public const int DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = 29;
    public const int DXGI_FORMAT_R16G16_UNORM = 35;
    public const int DXGI_FORMAT_R8G8_UNORM = 49;
    public const int DXGI_FORMAT_R16_UNORM = 56;
    public const int DXGI_FORMAT_R8_UNORM = 61;
    public const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const int DXGI_FORMAT_B8G8R8X8_UNORM = 88;
    public const int DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = 91;
    public const int DXGI_FORMAT_NV12 = 103;
    public const int DXGI_FORMAT_P010 = 104;

    public const int D3D11_USAGE_DEFAULT = 0;
    public const int D3D11_USAGE_DYNAMIC = 2;
    public const int D3D11_USAGE_STAGING = 3;
    public const uint D3D11_BIND_CONSTANT_BUFFER = 0x4;
    public const uint D3D11_BIND_SHADER_RESOURCE = 0x8;
    public const uint D3D11_BIND_RENDER_TARGET = 0x20;
    public const uint D3D11_RESOURCE_MISC_SHARED = 0x2;
    public const uint D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX = 0x10;
    public const uint D3D11_RESOURCE_MISC_SHARED_NTHANDLE = 0x800;
    public const uint D3D11_CPU_ACCESS_WRITE = 0x10000;
    public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    public const int D3D11_MAP_READ = 1;
    public const int D3D11_MAP_WRITE_DISCARD = 4;
    public const int D3D11_FILTER_MIN_MAG_MIP_LINEAR = 0x15;
    public const int D3D11_TEXTURE_ADDRESS_CLAMP = 3;
    public const int D3D11_COMPARISON_NEVER = 1;
    public const int D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST = 4;
    public const int D3D11_SRV_DIMENSION_TEXTURE2D = 4;

    public static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    public static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IID_IDXGIResource = new("035f3ab4-482e-4e50-b41f-8a7f8bd8960b");
    public static readonly Guid IID_IDXGIKeyedMutex = new("9d8e1289-d7b3-465f-8126-250e349af85d");
    public static readonly Guid IID_ID3D10Multithread = new("9b7e4e00-342c-4106-a19f-4f2704f689f0");

    /// <summary>D3D11_SHADER_RESOURCE_VIEW_DESC（Texture2D 用に union を 4 uint で表す）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_SHADER_RESOURCE_VIEW_DESC
    {
        public int Format;
        public int ViewDimension;
        public uint MostDetailedMip;
        public uint MipLevels;
        public uint Reserved0;
        public uint Reserved1;

        public static D3D11_SHADER_RESOURCE_VIEW_DESC Texture2D(int format) => new()
        {
            Format = format,
            ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D,
            MostDetailedMip = 0,
            MipLevels = 1,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_ADAPTER_DESC
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public D3D9Native.LUID AdapterLuid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public int Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_BUFFER_DESC
    {
        public uint ByteWidth;
        public int Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
        public uint StructureByteStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public void* pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_SAMPLER_DESC
    {
        public int Filter;
        public int AddressU;
        public int AddressV;
        public int AddressW;
        public float MipLODBias;
        public uint MaxAnisotropy;
        public int ComparisonFunc;
        public fixed float BorderColor[4];
        public float MinLOD;
        public float MaxLOD;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_VIEWPORT
    {
        public float TopLeftX;
        public float TopLeftY;
        public float Width;
        public float Height;
        public float MinDepth;
        public float MaxDepth;
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3dcompiler_47.dll", ExactSpelling = true)]
    public static extern int D3DCompile(
        byte* srcData,
        nuint srcDataSize,
        byte* sourceName,
        IntPtr defines,
        IntPtr include,
        byte* entryPoint,
        byte* target,
        uint flags1,
        uint flags2,
        out IntPtr code,
        out IntPtr errorMsgs);

    // ---- vtable 呼び出し ----

    private static void* Slot(IntPtr obj, int index) => (*(void***)obj)[index];

    public static int QueryInterface(IntPtr obj, in Guid iid, out IntPtr result)
    {
        fixed (Guid* p = &iid)
        fixed (IntPtr* r = &result)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(obj, 0))(obj, p, r);
        }
    }

    public static void Release(ref IntPtr obj)
    {
        var p = obj;
        obj = IntPtr.Zero;
        if (p != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(p, 2))(p);
        }
    }

    /// <summary>IUnknown::AddRef（他所のデバイスを掴んでおくとき）。</summary>
    public static void DeviceAddRef(IntPtr obj)
    {
        if (obj != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(obj, 1))(obj);
        }
    }

    // ID3DBlob
    public static void* BlobPointer(IntPtr blob) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, void*>)Slot(blob, 3))(blob);

    public static nuint BlobSize(IntPtr blob) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, nuint>)Slot(blob, 4))(blob);

    // IDXGIObject::GetParent / IDXGIDevice::GetAdapter
    public static int DxgiGetParent(IntPtr obj, in Guid iid, out IntPtr parent)
    {
        fixed (Guid* p = &iid)
        fixed (IntPtr* r = &parent)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(obj, 6))(obj, p, r);
        }
    }

    public static int DxgiDeviceGetAdapter(IntPtr dxgiDevice, out IntPtr adapter)
    {
        fixed (IntPtr* r = &adapter)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(dxgiDevice, 7))(dxgiDevice, r);
        }
    }

    // IDXGIAdapter::GetDesc
    public static int AdapterGetDesc(IntPtr adapter, out DXGI_ADAPTER_DESC desc)
    {
        fixed (DXGI_ADAPTER_DESC* d = &desc)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC*, int>)Slot(adapter, 8))(adapter, d);
        }
    }

    // IDXGIResource::GetSharedHandle（CloseHandle 不要の旧式共有ハンドル）
    public static int ResourceGetSharedHandle(IntPtr resource, out IntPtr handle)
    {
        fixed (IntPtr* r = &handle)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(resource, 8))(resource, r);
        }
    }

    // ID3D11Device
    public static int DeviceCreateBuffer(IntPtr device, in D3D11_BUFFER_DESC desc, out IntPtr buffer)
    {
        fixed (D3D11_BUFFER_DESC* d = &desc)
        fixed (IntPtr* r = &buffer)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_BUFFER_DESC*, IntPtr, IntPtr*, int>)Slot(device, 3))(
                device, d, IntPtr.Zero, r);
        }
    }

    public static int DeviceCreateTexture2D(IntPtr device, in D3D11_TEXTURE2D_DESC desc, out IntPtr texture)
    {
        fixed (D3D11_TEXTURE2D_DESC* d = &desc)
        fixed (IntPtr* r = &texture)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, IntPtr, IntPtr*, int>)Slot(device, 5))(
                device, d, IntPtr.Zero, r);
        }
    }

    public static int DeviceCreateShaderResourceView(IntPtr device, IntPtr resource, out IntPtr srv)
    {
        fixed (IntPtr* r = &srv)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)Slot(device, 7))(
                device, resource, IntPtr.Zero, r);
        }
    }

    /// <summary>形式を明示した SRV（NV12 の Y／UV 平面ビューなど）。</summary>
    public static int DeviceCreateShaderResourceView(IntPtr device, IntPtr resource, in D3D11_SHADER_RESOURCE_VIEW_DESC desc, out IntPtr srv)
    {
        fixed (D3D11_SHADER_RESOURCE_VIEW_DESC* d = &desc)
        fixed (IntPtr* r = &srv)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, D3D11_SHADER_RESOURCE_VIEW_DESC*, IntPtr*, int>)Slot(device, 7))(
                device, resource, d, r);
        }
    }

    public static int DeviceCreateRenderTargetView(IntPtr device, IntPtr resource, out IntPtr rtv)
    {
        fixed (IntPtr* r = &rtv)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)Slot(device, 9))(
                device, resource, IntPtr.Zero, r);
        }
    }

    public static int DeviceCreateVertexShader(IntPtr device, void* code, nuint size, out IntPtr shader)
    {
        fixed (IntPtr* r = &shader)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, void*, nuint, IntPtr, IntPtr*, int>)Slot(device, 12))(
                device, code, size, IntPtr.Zero, r);
        }
    }

    public static int DeviceCreatePixelShader(IntPtr device, void* code, nuint size, out IntPtr shader)
    {
        fixed (IntPtr* r = &shader)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, void*, nuint, IntPtr, IntPtr*, int>)Slot(device, 15))(
                device, code, size, IntPtr.Zero, r);
        }
    }

    public static int DeviceCreateSamplerState(IntPtr device, in D3D11_SAMPLER_DESC desc, out IntPtr sampler)
    {
        fixed (D3D11_SAMPLER_DESC* d = &desc)
        fixed (IntPtr* r = &sampler)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_SAMPLER_DESC*, IntPtr*, int>)Slot(device, 23))(device, d, r);
        }
    }

    // ID3D11Device::OpenSharedResource（別デバイスの共有ハンドルをこのデバイスで開く）
    public static int DeviceOpenSharedResource(IntPtr device, IntPtr sharedHandle, in Guid iid, out IntPtr resource)
    {
        fixed (Guid* i = &iid)
        fixed (IntPtr* r = &resource)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)Slot(device, 28))(device, sharedHandle, i, r);
        }
    }

    public static int DeviceGetDeviceRemovedReason(IntPtr device) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(device, 39))(device);

    // ID3D11Device::GetImmediateContext（AddRef 済みで返る）
    public static void DeviceGetImmediateContext(IntPtr device, out IntPtr context)
    {
        fixed (IntPtr* r = &context)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(device, 40))(device, r);
        }
    }

    // IDXGIKeyedMutex
    public static int KeyedMutexAcquireSync(IntPtr mutex, ulong key, uint milliseconds) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, int>)Slot(mutex, 8))(mutex, key, milliseconds);

    public static int KeyedMutexReleaseSync(IntPtr mutex, ulong key) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, ulong, int>)Slot(mutex, 9))(mutex, key);

    // ID3D10Multithread（他デバイスの即時コンテキストを借りるときの排他。保護が無効なら Enter は no-op）
    public static void MultithreadEnter(IntPtr multithread) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, void>)Slot(multithread, 3))(multithread);

    public static void MultithreadLeave(IntPtr multithread) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, void>)Slot(multithread, 4))(multithread);

    // ID3D11DeviceChild::GetDevice（AddRef 済みで返る）
    public static void DeviceChildGetDevice(IntPtr child, out IntPtr device)
    {
        fixed (IntPtr* r = &device)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(child, 3))(child, r);
        }
    }

    // ID3D11Texture2D::GetDesc
    public static void Texture2DGetDesc(IntPtr texture, out D3D11_TEXTURE2D_DESC desc)
    {
        fixed (D3D11_TEXTURE2D_DESC* d = &desc)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, void>)Slot(texture, 10))(texture, d);
        }
    }

    // ID3D11DeviceContext
    public static void CtxPSSetShaderResources(IntPtr ctx, IntPtr srv)
    {
        var p = srv;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)Slot(ctx, 8))(ctx, 0, 1, &p);
    }

    /// <summary>t0／t1 をまとめて束縛（YUV 2 平面用。両方 0 なら解除）。</summary>
    public static void CtxPSSetShaderResources(IntPtr ctx, IntPtr srv0, IntPtr srv1)
    {
        var p = stackalloc IntPtr[2] { srv0, srv1 };
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)Slot(ctx, 8))(ctx, 0, 2, p);
    }

    public static void CtxPSSetShader(IntPtr ctx, IntPtr shader) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, uint, void>)Slot(ctx, 9))(ctx, shader, IntPtr.Zero, 0);

    public static void CtxPSSetSamplers(IntPtr ctx, IntPtr sampler)
    {
        var p = sampler;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)Slot(ctx, 10))(ctx, 0, 1, &p);
    }

    public static void CtxVSSetShader(IntPtr ctx, IntPtr shader) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, uint, void>)Slot(ctx, 11))(ctx, shader, IntPtr.Zero, 0);

    public static void CtxDraw(IntPtr ctx, uint vertexCount, uint startVertex) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, void>)Slot(ctx, 13))(ctx, vertexCount, startVertex);

    public static int CtxMap(IntPtr ctx, IntPtr resource, int mapType, out D3D11_MAPPED_SUBRESOURCE mapped)
    {
        fixed (D3D11_MAPPED_SUBRESOURCE* m = &mapped)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, D3D11_MAPPED_SUBRESOURCE*, int>)Slot(ctx, 14))(
                ctx, resource, 0, mapType, 0, m);
        }
    }

    public static void CtxUnmap(IntPtr ctx, IntPtr resource) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Slot(ctx, 15))(ctx, resource, 0);

    public static void CtxPSSetConstantBuffers(IntPtr ctx, IntPtr buffer)
    {
        var p = buffer;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)Slot(ctx, 16))(ctx, 0, 1, &p);
    }

    public static void CtxIASetInputLayout(IntPtr ctx, IntPtr layout) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)Slot(ctx, 17))(ctx, layout);

    public static void CtxIASetPrimitiveTopology(IntPtr ctx, int topology) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int, void>)Slot(ctx, 24))(ctx, topology);

    public static void CtxOMSetRenderTargets(IntPtr ctx, IntPtr rtv)
    {
        var p = rtv;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, IntPtr, void>)Slot(ctx, 33))(
            ctx, rtv == IntPtr.Zero ? 0u : 1u, rtv == IntPtr.Zero ? null : &p, IntPtr.Zero);
    }

    public static void CtxRSSetViewport(IntPtr ctx, in D3D11_VIEWPORT viewport)
    {
        fixed (D3D11_VIEWPORT* v = &viewport)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, D3D11_VIEWPORT*, void>)Slot(ctx, 44))(ctx, 1, v);
        }
    }

    public static void CtxCopySubresourceRegion(IntPtr ctx, IntPtr dst, uint dstX, uint dstY, IntPtr src, uint* srcBox) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, uint*, void>)Slot(ctx, 46))(
            ctx, dst, 0, dstX, dstY, 0, src, 0, srcBox);

    public static void CtxCopyResource(IntPtr ctx, IntPtr dst, IntPtr src) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)Slot(ctx, 47))(ctx, dst, src);

    public static void CtxUpdateSubresource(IntPtr ctx, IntPtr dst, void* data, uint rowPitch) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr, void*, uint, uint, void>)Slot(ctx, 48))(
            ctx, dst, 0, IntPtr.Zero, data, rowPitch, 0);

    public static void CtxClearRenderTargetView(IntPtr ctx, IntPtr rtv, float r, float g, float b, float a)
    {
        var color = stackalloc float[4] { r, g, b, a };
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, float*, void>)Slot(ctx, 50))(ctx, rtv, color);
    }

    public static void CtxClearState(IntPtr ctx) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, void>)Slot(ctx, 110))(ctx);

    public static void CtxFlush(IntPtr ctx) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, void>)Slot(ctx, 111))(ctx);
}
