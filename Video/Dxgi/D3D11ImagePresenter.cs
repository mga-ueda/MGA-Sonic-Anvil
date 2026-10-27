using System.Text;
using static MgaSonicAnvil.Video.Dxgi.D3D11Native;
using static MgaSonicAnvil.Video.Dxgi.D3D9Native;

namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// D3D11 のオフスクリーン RT へフレームを Uniform（黒帯）で描き、同じ RT を D3D9Ex の共有サーフェスとして
/// <see cref="D3d9Surface"/> に出す（WPF の D3DImage.SetBackBuffer 用）。HWND もスワップチェーンも持たない。
/// スレッド: AttachForComposition／Resize／Present*／Dispose はすべて作成スレッド（UI）から呼ぶこと。
/// 描画のあとは D3D11 を Flush するだけ。D3DImage の Lock／AddDirtyRect／Unlock は呼び出し側の仕事。
/// <para>
/// 入力: BGRA（CPU バッファ／テクスチャ）と NV12・P010（テクスチャは Y／UV 平面 SRV、CPU は 2 平面アップロード）。
/// 色: <see cref="ApplyColorManagement"/> で決めた YUV 行列・レンジ・原色変換・伝達関数・モニタ ICC 近似を
/// ピクセルシェーダの定数で掛け、sRGB 相当で RT に書く（WPF は D3DImage を色管理しない）。
/// </para>
/// </summary>
internal sealed unsafe class D3D11ImagePresenter : IDisposable
{
    private const int ConstantBytes = 144;

    /// <summary>
    /// 定数レイアウト（float4 × 9）:
    /// gain(x=明るさ, y=ソース種別 0:BGRA 1:YUV2 平面, z=色変換 on/off)、yuvRange(yOff,yScale,cOff,cScale)、
    /// yuv0..2（Y'CbCr→R'G'B'）、gamut0..2（線形 RGB ソース→表示）、transfer(x=入力 EOTF 0:sRGB系 1:PQ 2:HLG 3:線形,
    /// y=出力 0:sRGB 1:べき, z=表示ガンマ, w=トーンマップのピーク)。
    /// </summary>
    private const string ShaderSource = """
        cbuffer Params : register(b0)
        {
            float4 gain;
            float4 yuvRange;
            float4 yuv0; float4 yuv1; float4 yuv2;
            float4 gamut0; float4 gamut1; float4 gamut2;
            float4 transfer;
        };
        Texture2D tex0 : register(t0);
        Texture2D tex1 : register(t1);
        SamplerState smp : register(s0);
        void VS(uint id : SV_VertexID, out float4 pos : SV_Position, out float2 uv : TEXCOORD0)
        {
            uv = float2((id << 1) & 2, id & 2);
            pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
        }
        float3 SrgbToLinear(float3 c)
        {
            return c <= 0.04045 ? c / 12.92 : pow(max((c + 0.055) / 1.055, 0), 2.4);
        }
        float3 LinearToSrgb(float3 c)
        {
            return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(max(c, 0), 1.0 / 2.4) - 0.055;
        }
        float3 PqToLinear(float3 e)
        {
            const float m1 = 0.1593017578125, m2 = 78.84375, c1 = 0.8359375, c2 = 18.8515625, c3 = 18.6875;
            float3 p = pow(max(e, 0), 1.0 / m2);
            return pow(max(p - c1, 0) / max(c2 - c3 * p, 1e-5), 1.0 / m1);
        }
        float3 HlgToLinear(float3 e)
        {
            const float a = 0.17883277, b = 0.28466892, c = 0.55991073;
            float3 s = e <= 0.5 ? e * e / 3.0 : (exp((e - c) / a) + b) / 12.0;
            float ys = dot(s, float3(0.2627, 0.6780, 0.0593));
            return s * pow(max(ys, 1e-4), 0.2);
        }
        float3 ToneMap(float3 x, float peak)
        {
            return x * (1 + x / (peak * peak)) / (1 + x);
        }
        float4 PS(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
        {
            float3 rgb;
            if (gain.y < 0.5)
            {
                rgb = tex0.Sample(smp, uv).rgb;
            }
            else
            {
                float y = tex0.Sample(smp, uv).r;
                float2 c = tex1.Sample(smp, uv).rg;
                float3 ycc = float3((y - yuvRange.x) * yuvRange.y, (c - yuvRange.z) * yuvRange.w);
                rgb = saturate(float3(dot(yuv0.xyz, ycc), dot(yuv1.xyz, ycc), dot(yuv2.xyz, ycc)));
            }
            if (gain.z > 0.5)
            {
                float3 lin;
                if (transfer.x < 0.5) lin = SrgbToLinear(rgb);
                else if (transfer.x < 1.5) lin = ToneMap(PqToLinear(rgb) * (10000.0 / 203.0), transfer.w);
                else if (transfer.x < 2.5) lin = ToneMap(HlgToLinear(rgb) * (1000.0 / 203.0), transfer.w);
                else lin = rgb;
                lin = saturate(float3(dot(gamut0.xyz, lin), dot(gamut1.xyz, lin), dot(gamut2.xyz, lin)));
                rgb = transfer.y < 0.5 ? LinearToSrgb(lin) : pow(lin, 1.0 / max(transfer.z, 0.1));
            }
            return float4(rgb * gain.x, 1);
        }
        """;

    private IntPtr _device;
    private IntPtr _context;
    private IntPtr _renderTarget;
    private IntPtr _rtv;

    private IntPtr _d3d9;
    private IntPtr _device9;
    private IntPtr _texture9;
    private IntPtr _surface9;

    private IntPtr _vs;
    private IntPtr _ps;
    private IntPtr _sampler;
    private IntPtr _constants;
    private readonly float[] _constantData = new float[ConstantBytes / 4];
    private bool _constantsDirty = true;
    private int _uploadedMode = -1;
    private float _uploadedGain = -1;

    // BGRA 入力
    private IntPtr _frameTexture;
    private IntPtr _frameSrv;
    private int _frameWidth;
    private int _frameHeight;

    // YUV 2 平面入力（CPU／別デバイスからの退避用）
    private IntPtr _planeY;
    private IntPtr _planeYSrv;
    private IntPtr _planeUV;
    private IntPtr _planeUVSrv;
    private int _planeWidth;
    private int _planeHeight;
    private int _planeBytesPerSample;

    // 別デバイス退避
    private IntPtr _staging;
    private IntPtr _stagingOwner;
    private D3D11_TEXTURE2D_DESC _stagingDesc;
    private IntPtr _openedSharedHandle;
    private IntPtr _openedSharedTexture;
    private byte[]? _swizzleScratch;

    // 色
    private VideoColorInfo _sourceColor = VideoColorInfo.Default;
    private MonitorColorProfile? _monitorProfile;
    private bool _useMonitorProfile = true;

    public D3D11ImagePresenter()
    {
        // ApplyColorManagement を呼ばなくても 709／リミテッドの既定で YUV が描けるように。
        RebuildColorConstants();
    }

    /// <summary>Resize／デバイス作り直しでサーフェスが替わった。ホストは SetBackBuffer し直すこと。</summary>
    public event Action? SurfaceRecreated;

    public int BackBufferWidth { get; private set; }

    public int BackBufferHeight { get; private set; }

    /// <summary>D3D11 で描けて、D3D9 側の共有サーフェスもある。</summary>
    public bool IsReady => _rtv != IntPtr.Zero && _surface9 != IntPtr.Zero;

    /// <summary>シェーダ描画が使えるか。false なら拡縮なしの中央コピーに落ちる（YUV・色変換も不可）。</summary>
    public bool HasShaderPipeline => _vs != IntPtr.Zero && _ps != IntPtr.Zero && _sampler != IntPtr.Zero;

    /// <summary>描画の明るさ（0..1）。暗幕・フェードは WPF 側で合成されるので通常は 1 のまま。</summary>
    public float Brightness { get; set; } = 1;

    /// <summary>D3DImage.SetBackBuffer に渡す IDirect3DSurface9（level 0）。所有はこのクラス。</summary>
    public IntPtr D3d9Surface => _surface9;

    /// <summary>直近の <see cref="ApplyColorManagement"/> で渡したソース色情報。</summary>
    public VideoColorInfo SourceColorInfo => _sourceColor;

    /// <summary>モニタ ICC の近似を掛けるか。既定 on。sRGB と区別がつかないプロファイルは自動で素通し。</summary>
    public bool UseMonitorProfile
    {
        get => _useMonitorProfile;
        set
        {
            if (_useMonitorProfile != value)
            {
                _useMonitorProfile = value;
                RebuildColorConstants();
                _constantsDirty = true;
            }
        }
    }

    /// <summary>シェーダで色変換（EOTF／原色／ICC）を実際に掛けているか（恒等なら false）。</summary>
    public bool IsColorTransformActive { get; private set; }

    public void AttachForComposition(int width, int height)
    {
        ReleaseAll();
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (!CreateDevice() || !CreateD3D9Device() || !CreateSharedTarget(width, height))
        {
            ReleaseAll();
            return;
        }

        TryCreateShaderPipeline();
        CtxClearRenderTargetView(_context, _rtv, 0, 0, 0, 1);
        CtxFlush(_context);
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (!IsReady || (width == BackBufferWidth && height == BackBufferHeight))
        {
            return;
        }

        CtxOMSetRenderTargets(_context, IntPtr.Zero);
        ReleaseSharedTarget();
        if (!CreateSharedTarget(width, height))
        {
            ReleaseAll();
        }
        else
        {
            CtxClearRenderTargetView(_context, _rtv, 0, 0, 0, 1);
            CtxFlush(_context);
        }

        SurfaceRecreated?.Invoke();
    }

    public void Clear(float r, float g, float b)
    {
        if (!IsReady)
        {
            return;
        }

        CtxClearRenderTargetView(_context, _rtv, r, g, b, 1);
        Commit();
    }

    /// <summary>
    /// ソースの色情報とウィンドウ（0 ならプライマリモニタ）から、シェーダ定数の色変換を組み直す。
    /// 開いたとき・モニタ移動・DPI／ディスプレイ設定変更で呼ぶ。描画はしない（次の Present から反映）。
    /// </summary>
    public void ApplyColorManagement(VideoColorInfo info, IntPtr hwndOrZero)
    {
        _sourceColor = info;
        _monitorProfile = MonitorColorProfile.Load(hwndOrZero);
        // 定数は CPU 側で今組み（IsColorTransformActive をすぐ読めるように）、GPU へは次の描画で上げる。
        RebuildColorConstants();
        _constantsDirty = true;
    }

    public void PresentBgraFrame(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        if (!IsReady || width <= 0 || height <= 0 || stride < width * 4 || bgra.Length < (long)stride * (height - 1) + (width * 4))
        {
            return;
        }

        fixed (byte* src = bgra)
        {
            if (!UploadBgra(src, width, height, stride))
            {
                return;
            }
        }

        Commit();
    }

    /// <summary>
    /// NV12（8bit）の CPU バッファを描く。y は幅×高さ、uv は (幅/2)×(高さ/2) の UV 交互。シェーダ無しなら何もしない。
    /// </summary>
    public void PresentNv12Frame(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, int width, int height)
    {
        if (!IsReady || !HasShaderPipeline || width <= 0 || height <= 0 || yStride < width || uvStride < ((width + 1) / 2) * 2)
        {
            return;
        }

        var uvRows = (height + 1) / 2;
        if (y.Length < (long)yStride * (height - 1) + width || uv.Length < (long)uvStride * (uvRows - 1) + (((width + 1) / 2) * 2))
        {
            return;
        }

        fixed (byte* py = y)
        fixed (byte* puv = uv)
        {
            if (!UploadYuvPlanes(py, yStride, puv, uvStride, width, height, 1))
            {
                return;
            }
        }

        Commit();
    }

    /// <summary>
    /// ID3D11Texture2D を描く。
    /// <list type="number">
    /// <item>同一デバイス: SHADER_RESOURCE 付きなら BGRA 系はそのまま、NV12／P010 は Y・UV 平面 SRV でシェーダ変換。
    /// SRV 不可なら RT と同寸・同形式のときだけ CopyResource、それ以外はステージング経由（下記 3）。</item>
    /// <item>別デバイスで MISC_SHARED（旧式ハンドル）: IDXGIResource::GetSharedHandle → OpenSharedResource でこちらに開いて 1 へ。
    /// KEYEDMUTEX 付きなら key 0 で Acquire／Release する。NT ハンドル共有（SHARED_NTHANDLE）は開けないので 3 へ。</item>
    /// <item>それ以外: 持ち主デバイスの即時コンテキストでステージングへコピー → Map → こちらの動的テクスチャへアップロード。
    /// 持ち主側が ID3D10Multithread 保護を有効にしていれば Enter／Leave で排他する（無効なら呼び出し側で直列化すること）。
    /// 対応形式は BGRA／BGRX／RGBA8／NV12／P010。</item>
    /// <item>どれも駄目なら黒。</item>
    /// </list>
    /// 制限: YUY2 などパック YUV、配列テクスチャ、MSAA は未対応（黒）。シェーダ無し環境では YUV も黒。
    /// 同期: 旧式共有ハンドルには GPU 間の同期が無い。持ち主側の書き込み完了を待たずに渡すと古い内容や黒を拾うので、
    /// 別デバイスから渡すなら KEYEDMUTEX 付きで作るか、呼び出し側で完了を待つこと（ステージング経由は Map で待つので安全）。
    /// </summary>
    public void PresentTexture(IntPtr texture2D)
    {
        if (!IsReady || texture2D == IntPtr.Zero)
        {
            return;
        }

        if (QueryInterface(texture2D, IID_ID3D11Texture2D, out var tex) < 0 || tex == IntPtr.Zero)
        {
            return;
        }

        var owner = IntPtr.Zero;
        try
        {
            DeviceChildGetDevice(tex, out owner);
            Texture2DGetDesc(tex, out var desc);
            var drawn = owner == _device
                ? PresentOwnedTexture(tex, desc)
                : TryPresentViaSharedHandle(tex, desc) || TryPresentViaStaging(owner, tex, desc);
            if (!drawn)
            {
                CtxClearRenderTargetView(_context, _rtv, 0, 0, 0, 1);
            }

            Commit();
        }
        finally
        {
            Release(ref owner);
            Release(ref tex);
        }
    }

    public void Dispose() => ReleaseAll();

    /// <summary>テスト用: RT の 1 画素を CPU に読み戻す（BGRA を 0xAARRGGBB で）。GPU を待つので遅い。</summary>
    internal bool TryReadBackPixel(int x, int y, out uint pixel)
    {
        pixel = 0;
        if (!IsReady || x < 0 || y < 0 || x >= BackBufferWidth || y >= BackBufferHeight)
        {
            return false;
        }

        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)BackBufferWidth,
            Height = (uint)BackBufferHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE_STAGING,
            CPUAccessFlags = D3D11_CPU_ACCESS_READ,
        };
        if (DeviceCreateTexture2D(_device, desc, out var staging) < 0 || staging == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            CtxCopyResource(_context, staging, _renderTarget);
            if (CtxMap(_context, staging, D3D11_MAP_READ, out var mapped) < 0)
            {
                return false;
            }

            try
            {
                pixel = *(uint*)((byte*)mapped.pData + ((long)y * mapped.RowPitch) + ((long)x * 4));
                return true;
            }
            finally
            {
                CtxUnmap(_context, staging);
            }
        }
        finally
        {
            Release(ref staging);
        }
    }

    // ---- 入力経路 ----

    /// <summary>自デバイスのテクスチャを描く（形式に応じてシェーダ／コピー／ステージング）。</summary>
    private bool PresentOwnedTexture(IntPtr tex, in D3D11_TEXTURE2D_DESC desc)
    {
        if (desc.ArraySize != 1 || desc.SampleDesc.Count > 1)
        {
            return false;
        }

        var w = (int)desc.Width;
        var h = (int)desc.Height;
        if (HasShaderPipeline && (desc.BindFlags & D3D11_BIND_SHADER_RESOURCE) != 0)
        {
            if (IsPlanarYuv(desc.Format))
            {
                var (yFormat, uvFormat) = PlaneFormats(desc.Format);
                var yDesc = D3D11_SHADER_RESOURCE_VIEW_DESC.Texture2D(yFormat);
                var uvDesc = D3D11_SHADER_RESOURCE_VIEW_DESC.Texture2D(uvFormat);
                if (DeviceCreateShaderResourceView(_device, tex, yDesc, out var srvY) >= 0)
                {
                    if (DeviceCreateShaderResourceView(_device, tex, uvDesc, out var srvUV) >= 0)
                    {
                        DrawLetterboxed(srvY, srvUV, w, h, yuv: true);
                        Release(ref srvUV);
                        Release(ref srvY);
                        return true;
                    }

                    Release(ref srvY);
                }

                return false;
            }

            if (IsRgbSampleable(desc.Format) && DeviceCreateShaderResourceView(_device, tex, out var srv) >= 0)
            {
                DrawLetterboxed(srv, IntPtr.Zero, w, h, yuv: false);
                Release(ref srv);
                return true;
            }
        }

        if (w == BackBufferWidth && h == BackBufferHeight && desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM)
        {
            CtxCopyResource(_context, _renderTarget, tex);
            return true;
        }

        return TryPresentViaStaging(_device, tex, desc);
    }

    private bool TryPresentViaSharedHandle(IntPtr tex, in D3D11_TEXTURE2D_DESC desc)
    {
        if ((desc.MiscFlags & (D3D11_RESOURCE_MISC_SHARED | D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX)) == 0
            || (desc.MiscFlags & D3D11_RESOURCE_MISC_SHARED_NTHANDLE) != 0)
        {
            return false;
        }

        var resource = IntPtr.Zero;
        var handle = IntPtr.Zero;
        try
        {
            if (QueryInterface(tex, IID_IDXGIResource, out resource) < 0 || ResourceGetSharedHandle(resource, out handle) < 0 || handle == IntPtr.Zero)
            {
                return false;
            }
        }
        finally
        {
            Release(ref resource);
        }

        if (_openedSharedTexture == IntPtr.Zero || _openedSharedHandle != handle)
        {
            Release(ref _openedSharedTexture);
            _openedSharedHandle = IntPtr.Zero;
            if (DeviceOpenSharedResource(_device, handle, IID_ID3D11Texture2D, out _openedSharedTexture) < 0 || _openedSharedTexture == IntPtr.Zero)
            {
                _openedSharedTexture = IntPtr.Zero;
                return false;
            }

            _openedSharedHandle = handle;
        }

        var mutex = IntPtr.Zero;
        var acquired = false;
        try
        {
            if ((desc.MiscFlags & D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX) != 0
                && QueryInterface(_openedSharedTexture, IID_IDXGIKeyedMutex, out mutex) >= 0
                && mutex != IntPtr.Zero)
            {
                acquired = KeyedMutexAcquireSync(mutex, 0, 100) == S_OK;
                if (!acquired)
                {
                    return false;
                }
            }

            // 開いた側の desc は元と同じ（BindFlags も引き継ぐ）。
            Texture2DGetDesc(_openedSharedTexture, out var openedDesc);
            return PresentOwnedTexture(_openedSharedTexture, openedDesc);
        }
        finally
        {
            if (acquired)
            {
                KeyedMutexReleaseSync(mutex, 0);
            }

            Release(ref mutex);
        }
    }

    /// <summary>持ち主デバイスでステージングへ落として CPU 経由でこちらへ上げる（遅いが確実）。</summary>
    private bool TryPresentViaStaging(IntPtr ownerDevice, IntPtr tex, in D3D11_TEXTURE2D_DESC desc)
    {
        if (ownerDevice == IntPtr.Zero || desc.ArraySize != 1 || desc.SampleDesc.Count > 1 || !IsCpuUploadable(desc.Format))
        {
            return false;
        }

        if (!EnsureStaging(ownerDevice, desc))
        {
            return false;
        }

        DeviceGetImmediateContext(ownerDevice, out var ctx);
        if (ctx == IntPtr.Zero)
        {
            return false;
        }

        var multithread = IntPtr.Zero;
        var entered = false;
        try
        {
            if (QueryInterface(ctx, IID_ID3D10Multithread, out multithread) >= 0 && multithread != IntPtr.Zero)
            {
                MultithreadEnter(multithread);
                entered = true;
            }

            CtxCopyResource(ctx, _staging, tex);
            if (CtxMap(ctx, _staging, D3D11_MAP_READ, out var mapped) < 0)
            {
                return false;
            }

            try
            {
                var w = (int)desc.Width;
                var h = (int)desc.Height;
                var p = (byte*)mapped.pData;
                var pitch = (int)mapped.RowPitch;
                switch (desc.Format)
                {
                    case DXGI_FORMAT_B8G8R8A8_UNORM:
                    case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
                    case DXGI_FORMAT_B8G8R8X8_UNORM:
                        return UploadBgra(p, w, h, pitch);
                    case DXGI_FORMAT_R8G8B8A8_UNORM:
                    case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
                        return UploadRgbaAsBgra(p, w, h, pitch);
                    case DXGI_FORMAT_NV12:
                        // ステージングの NV12 は Y 平面の直後（RowPitch × Height）に UV 平面が続く。
                        return UploadYuvPlanes(p, pitch, p + ((long)pitch * h), pitch, w, h, 1);
                    case DXGI_FORMAT_P010:
                        return UploadYuvPlanes(p, pitch, p + ((long)pitch * h), pitch, w, h, 2);
                    default:
                        return false;
                }
            }
            finally
            {
                CtxUnmap(ctx, _staging);
            }
        }
        finally
        {
            if (entered)
            {
                MultithreadLeave(multithread);
            }

            Release(ref multithread);
            Release(ref ctx);
        }
    }

    private bool EnsureStaging(IntPtr ownerDevice, in D3D11_TEXTURE2D_DESC desc)
    {
        if (_staging != IntPtr.Zero
            && _stagingOwner == ownerDevice
            && _stagingDesc.Width == desc.Width
            && _stagingDesc.Height == desc.Height
            && _stagingDesc.Format == desc.Format)
        {
            return true;
        }

        ReleaseStaging();
        var staging = new D3D11_TEXTURE2D_DESC
        {
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desc.Format,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE_STAGING,
            CPUAccessFlags = D3D11_CPU_ACCESS_READ,
        };
        if (DeviceCreateTexture2D(ownerDevice, staging, out _staging) < 0 || _staging == IntPtr.Zero)
        {
            _staging = IntPtr.Zero;
            return false;
        }

        // 持ち主デバイスを掴んでおく（ステージングの寿命の間は生きていてもらう）。
        _stagingOwner = ownerDevice;
        DeviceAddRef(ownerDevice);
        _stagingDesc = desc;
        return true;
    }

    private void ReleaseStaging()
    {
        Release(ref _staging);
        Release(ref _stagingOwner);
        _stagingDesc = default;
    }

    // ---- アップロード ----

    private bool UploadBgra(byte* src, int width, int height, int stride)
    {
        if (!EnsureFrameTexture(width, height))
        {
            return false;
        }

        if (HasShaderPipeline)
        {
            if (CtxMap(_context, _frameTexture, D3D11_MAP_WRITE_DISCARD, out var mapped) < 0)
            {
                return false;
            }

            var dst = (byte*)mapped.pData;
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(src + ((long)y * stride), dst + ((long)y * mapped.RowPitch), mapped.RowPitch, rowBytes);
            }

            CtxUnmap(_context, _frameTexture);
            DrawLetterboxed(_frameSrv, IntPtr.Zero, width, height, yuv: false);
        }
        else
        {
            CtxUpdateSubresource(_context, _frameTexture, src, (uint)stride);
            CopyCentered(_frameTexture, width, height);
        }

        return true;
    }

    private bool UploadRgbaAsBgra(byte* src, int width, int height, int stride)
    {
        var rowBytes = width * 4;
        if (_swizzleScratch is null || _swizzleScratch.Length < rowBytes * height)
        {
            _swizzleScratch = new byte[rowBytes * height];
        }

        fixed (byte* dst = _swizzleScratch)
        {
            for (var y = 0; y < height; y++)
            {
                var s = src + ((long)y * stride);
                var d = dst + ((long)y * rowBytes);
                for (var x = 0; x < rowBytes; x += 4)
                {
                    d[x] = s[x + 2];
                    d[x + 1] = s[x + 1];
                    d[x + 2] = s[x];
                    d[x + 3] = s[x + 3];
                }
            }

            return UploadBgra(dst, width, height, rowBytes);
        }
    }

    /// <summary>Y（R8／R16）と UV（R8G8／R16G16）を動的テクスチャに上げ、YUV モードで描く。</summary>
    private bool UploadYuvPlanes(byte* y, int yPitch, byte* uv, int uvPitch, int width, int height, int bytesPerSample)
    {
        if (!HasShaderPipeline || !EnsurePlaneTextures(width, height, bytesPerSample))
        {
            return false;
        }

        var chromaW = (width + 1) / 2;
        var chromaH = (height + 1) / 2;
        if (CtxMap(_context, _planeY, D3D11_MAP_WRITE_DISCARD, out var my) < 0)
        {
            return false;
        }

        var rowY = width * bytesPerSample;
        for (var r = 0; r < height; r++)
        {
            Buffer.MemoryCopy(y + ((long)r * yPitch), (byte*)my.pData + ((long)r * my.RowPitch), my.RowPitch, rowY);
        }

        CtxUnmap(_context, _planeY);

        if (CtxMap(_context, _planeUV, D3D11_MAP_WRITE_DISCARD, out var muv) < 0)
        {
            return false;
        }

        var rowUV = chromaW * 2 * bytesPerSample;
        for (var r = 0; r < chromaH; r++)
        {
            Buffer.MemoryCopy(uv + ((long)r * uvPitch), (byte*)muv.pData + ((long)r * muv.RowPitch), muv.RowPitch, rowUV);
        }

        CtxUnmap(_context, _planeUV);
        DrawLetterboxed(_planeYSrv, _planeUVSrv, width, height, yuv: true);
        return true;
    }

    private bool EnsurePlaneTextures(int width, int height, int bytesPerSample)
    {
        if (_planeY != IntPtr.Zero && _planeUV != IntPtr.Zero && width == _planeWidth && height == _planeHeight && bytesPerSample == _planeBytesPerSample)
        {
            return true;
        }

        ReleasePlaneTextures();
        var yFormat = bytesPerSample == 2 ? DXGI_FORMAT_R16_UNORM : DXGI_FORMAT_R8_UNORM;
        var uvFormat = bytesPerSample == 2 ? DXGI_FORMAT_R16G16_UNORM : DXGI_FORMAT_R8G8_UNORM;
        if (!CreateDynamicSrvTexture(width, height, yFormat, out _planeY, out _planeYSrv)
            || !CreateDynamicSrvTexture((width + 1) / 2, (height + 1) / 2, uvFormat, out _planeUV, out _planeUVSrv))
        {
            ReleasePlaneTextures();
            return false;
        }

        _planeWidth = width;
        _planeHeight = height;
        _planeBytesPerSample = bytesPerSample;
        return true;
    }

    private bool CreateDynamicSrvTexture(int width, int height, int format, out IntPtr texture, out IntPtr srv)
    {
        srv = IntPtr.Zero;
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)Math.Max(1, width),
            Height = (uint)Math.Max(1, height),
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE_DYNAMIC,
            BindFlags = D3D11_BIND_SHADER_RESOURCE,
            CPUAccessFlags = D3D11_CPU_ACCESS_WRITE,
        };
        if (DeviceCreateTexture2D(_device, desc, out texture) < 0 || texture == IntPtr.Zero)
        {
            texture = IntPtr.Zero;
            return false;
        }

        if (DeviceCreateShaderResourceView(_device, texture, out srv) < 0)
        {
            srv = IntPtr.Zero;
            Release(ref texture);
            return false;
        }

        return true;
    }

    private void ReleasePlaneTextures()
    {
        Release(ref _planeUVSrv);
        Release(ref _planeUV);
        Release(ref _planeYSrv);
        Release(ref _planeY);
        _planeWidth = 0;
        _planeHeight = 0;
        _planeBytesPerSample = 0;
    }

    private static bool IsPlanarYuv(int format) => format is DXGI_FORMAT_NV12 or DXGI_FORMAT_P010;

    private static (int Y, int UV) PlaneFormats(int format) =>
        format == DXGI_FORMAT_P010 ? (DXGI_FORMAT_R16_UNORM, DXGI_FORMAT_R16G16_UNORM) : (DXGI_FORMAT_R8_UNORM, DXGI_FORMAT_R8G8_UNORM);

    private static bool IsRgbSampleable(int format) => format is
        DXGI_FORMAT_B8G8R8A8_UNORM or DXGI_FORMAT_B8G8R8A8_UNORM_SRGB or DXGI_FORMAT_B8G8R8X8_UNORM
        or DXGI_FORMAT_R8G8B8A8_UNORM or DXGI_FORMAT_R8G8B8A8_UNORM_SRGB
        or DXGI_FORMAT_R10G10B10A2_UNORM or DXGI_FORMAT_R16G16B16A16_FLOAT;

    private static bool IsCpuUploadable(int format) => format is
        DXGI_FORMAT_B8G8R8A8_UNORM or DXGI_FORMAT_B8G8R8A8_UNORM_SRGB or DXGI_FORMAT_B8G8R8X8_UNORM
        or DXGI_FORMAT_R8G8B8A8_UNORM or DXGI_FORMAT_R8G8B8A8_UNORM_SRGB
        or DXGI_FORMAT_NV12 or DXGI_FORMAT_P010;

    // ---- デバイス・RT ----

    private bool CreateDevice()
    {
        var hr = D3D11CreateDevice(
            IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero, 0, D3D11_SDK_VERSION, out _device, out _, out _context);
        if (hr < 0)
        {
            // GPU が無い／リモート環境は WARP で続ける（D3D9 側に同じアダプタが無ければ共有できず失敗する）。
            hr = D3D11CreateDevice(
                IntPtr.Zero, D3D_DRIVER_TYPE_WARP, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero, 0, D3D11_SDK_VERSION, out _device, out _, out _context);
        }

        return hr >= 0 && _device != IntPtr.Zero && _context != IntPtr.Zero;
    }

    /// <summary>D3D11 と同じアダプタ（LUID 一致）で D3D9Ex デバイスを作る。共有ハンドルはアダプタをまたげない。</summary>
    private bool CreateD3D9Device()
    {
        if (!TryGetAdapterLuid(out var luid))
        {
            return false;
        }

        try
        {
            if (Direct3DCreate9Ex(D3D_SDK_VERSION, out _d3d9) < 0 || _d3d9 == IntPtr.Zero)
            {
                _d3d9 = IntPtr.Zero;
                return false;
            }
        }
        catch (DllNotFoundException)
        {
            return false;
        }

        var adapter = uint.MaxValue;
        var count = D3D9GetAdapterCount(_d3d9);
        for (uint i = 0; i < count; i++)
        {
            if (D3D9GetAdapterLuid(_d3d9, i, out var l) >= 0 && l.LowPart == luid.LowPart && l.HighPart == luid.HighPart)
            {
                adapter = i;
                break;
            }
        }

        if (adapter == uint.MaxValue)
        {
            return false;
        }

        // バックバッファは使わない。ウィンドウモードの最小構成（WPF の描画スレッドからも触られるので MULTITHREADED）。
        var desktop = GetDesktopWindow();
        var pp = new D3DPRESENT_PARAMETERS
        {
            BackBufferWidth = 1,
            BackBufferHeight = 1,
            BackBufferCount = 1,
            SwapEffect = D3DSWAPEFFECT_DISCARD,
            hDeviceWindow = desktop,
            Windowed = 1,
            PresentationInterval = D3DPRESENT_INTERVAL_IMMEDIATE,
        };
        const uint common = D3DCREATE_MULTITHREADED | D3DCREATE_FPU_PRESERVE;
        if (D3D9CreateDeviceEx(_d3d9, adapter, desktop, common | D3DCREATE_HARDWARE_VERTEXPROCESSING, ref pp, out _device9) >= 0
            && _device9 != IntPtr.Zero)
        {
            return true;
        }

        _device9 = IntPtr.Zero;
        return D3D9CreateDeviceEx(_d3d9, adapter, desktop, common | D3DCREATE_SOFTWARE_VERTEXPROCESSING, ref pp, out _device9) >= 0
               && _device9 != IntPtr.Zero;
    }

    private bool TryGetAdapterLuid(out LUID luid)
    {
        luid = default;
        var dxgiDevice = IntPtr.Zero;
        var adapter = IntPtr.Zero;
        try
        {
            if (QueryInterface(_device, IID_IDXGIDevice, out dxgiDevice) < 0
                || DxgiDeviceGetAdapter(dxgiDevice, out adapter) < 0
                || AdapterGetDesc(adapter, out var desc) < 0)
            {
                return false;
            }

            luid = desc.AdapterLuid;
            return true;
        }
        finally
        {
            Release(ref adapter);
            Release(ref dxgiDevice);
        }
    }

    /// <summary>SHARED の BGRA RT を作り、その共有ハンドルを D3D9 テクスチャとして開く。</summary>
    private bool CreateSharedTarget(int width, int height)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE,
            MiscFlags = D3D11_RESOURCE_MISC_SHARED,
        };
        if (DeviceCreateTexture2D(_device, desc, out _renderTarget) < 0 || _renderTarget == IntPtr.Zero)
        {
            _renderTarget = IntPtr.Zero;
            return false;
        }

        if (DeviceCreateRenderTargetView(_device, _renderTarget, out _rtv) < 0)
        {
            _rtv = IntPtr.Zero;
            return false;
        }

        var resource = IntPtr.Zero;
        try
        {
            if (QueryInterface(_renderTarget, IID_IDXGIResource, out resource) < 0
                || ResourceGetSharedHandle(resource, out var shared) < 0
                || shared == IntPtr.Zero)
            {
                return false;
            }

            if (Device9CreateTexture(_device9, (uint)width, (uint)height, D3DUSAGE_RENDERTARGET, D3DFMT_A8R8G8B8, D3DPOOL_DEFAULT, out _texture9, shared) < 0
                || _texture9 == IntPtr.Zero
                || Texture9GetSurfaceLevel(_texture9, 0, out _surface9) < 0)
            {
                _surface9 = IntPtr.Zero;
                return false;
            }
        }
        finally
        {
            Release(ref resource);
        }

        BackBufferWidth = width;
        BackBufferHeight = height;
        return true;
    }

    private void TryCreateShaderPipeline()
    {
        try
        {
            var vsBlob = Compile("VS", "vs_4_0");
            var psBlob = Compile("PS", "ps_4_0");
            try
            {
                if (vsBlob == IntPtr.Zero
                    || psBlob == IntPtr.Zero
                    || DeviceCreateVertexShader(_device, BlobPointer(vsBlob), BlobSize(vsBlob), out _vs) < 0
                    || DeviceCreatePixelShader(_device, BlobPointer(psBlob), BlobSize(psBlob), out _ps) < 0)
                {
                    ReleaseShaderPipeline();
                    return;
                }
            }
            finally
            {
                Release(ref vsBlob);
                Release(ref psBlob);
            }

            var sampler = new D3D11_SAMPLER_DESC
            {
                Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR,
                AddressU = D3D11_TEXTURE_ADDRESS_CLAMP,
                AddressV = D3D11_TEXTURE_ADDRESS_CLAMP,
                AddressW = D3D11_TEXTURE_ADDRESS_CLAMP,
                MaxAnisotropy = 1,
                ComparisonFunc = D3D11_COMPARISON_NEVER,
                MaxLOD = float.MaxValue,
            };
            var cb = new D3D11_BUFFER_DESC
            {
                ByteWidth = ConstantBytes,
                Usage = D3D11_USAGE_DEFAULT,
                BindFlags = D3D11_BIND_CONSTANT_BUFFER,
            };
            if (DeviceCreateSamplerState(_device, sampler, out _sampler) < 0
                || DeviceCreateBuffer(_device, cb, out _constants) < 0)
            {
                ReleaseShaderPipeline();
            }
        }
        catch (DllNotFoundException)
        {
            // d3dcompiler_47 が無い環境は中央コピーで続ける。
            ReleaseShaderPipeline();
        }
    }

    private static IntPtr Compile(string entry, string target)
    {
        var src = Encoding.UTF8.GetBytes(ShaderSource);
        var entryBytes = Encoding.ASCII.GetBytes(entry + "\0");
        var targetBytes = Encoding.ASCII.GetBytes(target + "\0");
        fixed (byte* s = src)
        fixed (byte* e = entryBytes)
        fixed (byte* t = targetBytes)
        {
            var hr = D3DCompile(s, (nuint)src.Length, null, IntPtr.Zero, IntPtr.Zero, e, t, 0, 0, out var code, out var errors);
            Release(ref errors);
            if (hr < 0)
            {
                Release(ref code);
                return IntPtr.Zero;
            }

            return code;
        }
    }

    private bool EnsureFrameTexture(int width, int height)
    {
        if (_frameTexture != IntPtr.Zero && width == _frameWidth && height == _frameHeight)
        {
            return true;
        }

        Release(ref _frameSrv);
        Release(ref _frameTexture);
        var shader = HasShaderPipeline;
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = shader ? D3D11_USAGE_DYNAMIC : D3D11_USAGE_DEFAULT,
            BindFlags = shader ? D3D11_BIND_SHADER_RESOURCE : 0,
            CPUAccessFlags = shader ? D3D11_CPU_ACCESS_WRITE : 0,
        };
        if (DeviceCreateTexture2D(_device, desc, out _frameTexture) < 0)
        {
            _frameTexture = IntPtr.Zero;
            return false;
        }

        if (shader && DeviceCreateShaderResourceView(_device, _frameTexture, out _frameSrv) < 0)
        {
            Release(ref _frameTexture);
            return false;
        }

        _frameWidth = width;
        _frameHeight = height;
        return true;
    }

    // ---- 描画 ----

    private void DrawLetterboxed(IntPtr srv0, IntPtr srv1, int srcWidth, int srcHeight, bool yuv)
    {
        CtxClearRenderTargetView(_context, _rtv, 0, 0, 0, 1);
        var rect = UniformRect(srcWidth, srcHeight, BackBufferWidth, BackBufferHeight);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        UploadConstants(yuv ? 1 : 0);
        CtxOMSetRenderTargets(_context, _rtv);
        CtxRSSetViewport(_context, new D3D11_VIEWPORT
        {
            TopLeftX = rect.X,
            TopLeftY = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            MaxDepth = 1,
        });
        CtxIASetInputLayout(_context, IntPtr.Zero);
        CtxIASetPrimitiveTopology(_context, D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        CtxVSSetShader(_context, _vs);
        CtxPSSetShader(_context, _ps);
        CtxPSSetShaderResources(_context, srv0, srv1);
        CtxPSSetSamplers(_context, _sampler);
        CtxPSSetConstantBuffers(_context, _constants);
        CtxDraw(_context, 3, 0);
        // 借り物の SRV を解放しても束縛が残らないように外す。
        CtxPSSetShaderResources(_context, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>明るさ・ソース種別・色変換の定数を必要なときだけ上げる。</summary>
    private void UploadConstants(int mode)
    {
        var gain = Math.Clamp(Brightness, 0f, 1f);
        if (!_constantsDirty && mode == _uploadedMode && Math.Abs(gain - _uploadedGain) < 0.001f)
        {
            return;
        }

        var c = _constantData;
        c[0] = gain;
        c[1] = mode;
        fixed (float* p = c)
        {
            CtxUpdateSubresource(_context, _constants, p, ConstantBytes);
        }

        _uploadedMode = mode;
        _uploadedGain = gain;
        _constantsDirty = false;
    }

    /// <summary>
    /// <see cref="_sourceColor"/> と <see cref="_monitorProfile"/> から定数を組む。
    /// 全部恒等（709／sRGB 系、ICC も sRGB 同等）なら色変換を切って素通し（8bit RT のバンディングを避ける）。
    /// </summary>
    private void RebuildColorConstants()
    {
        var c = _constantData;
        var info = _sourceColor.Normalized();
        var (yOff, yScale, cOff, cScale) = VideoColorMath.RangeParams(info.Range);
        c[4] = yOff;
        c[5] = yScale;
        c[6] = cOff;
        c[7] = cScale;

        var yuv = VideoColorMath.YuvToRgb(info.Matrix);
        WriteRows(c, 8, yuv);

        var gamut = VideoColorMath.PrimariesToBt709(info.Primaries);
        var inputEotf = info.Transfer switch
        {
            VideoTransferFunction.Pq => 1f,
            VideoTransferFunction.Hlg => 2f,
            VideoTransferFunction.Linear or VideoTransferFunction.LinearRel => 3f,
            _ => 0f,
        };
        var outputMode = 0f;
        var displayGamma = 2.2f;
        var profile = _monitorProfile;
        if (_useMonitorProfile && profile is { Parsed: true, IsSrgbLike: false })
        {
            gamut = VideoColorMath.Multiply(profile.DisplayFromSrgbLinear, gamut);
            outputMode = 1f;
            displayGamma = profile.Gamma;
        }

        WriteRows(c, 20, gamut);
        c[32] = inputEotf;
        c[33] = outputMode;
        c[34] = displayGamma;
        c[35] = 1000f / 203f;

        var active = inputEotf != 0f || outputMode != 0f || !VideoColorMath.IsIdentity(gamut);
        c[2] = active ? 1f : 0f;
        IsColorTransformActive = active;
    }

    private static void WriteRows(float[] c, int at, ReadOnlySpan<float> m3x3)
    {
        for (var row = 0; row < 3; row++)
        {
            c[at + (row * 4)] = m3x3[row * 3];
            c[at + (row * 4) + 1] = m3x3[(row * 3) + 1];
            c[at + (row * 4) + 2] = m3x3[(row * 3) + 2];
            c[at + (row * 4) + 3] = 0;
        }
    }

    /// <summary>シェーダ無し: 拡縮できないので中央へ等倍コピー（はみ出しは切る）。明るさも掛からない。</summary>
    private void CopyCentered(IntPtr texture, int width, int height)
    {
        CtxClearRenderTargetView(_context, _rtv, 0, 0, 0, 1);
        var copyW = Math.Min(width, BackBufferWidth);
        var copyH = Math.Min(height, BackBufferHeight);
        var srcX = (width - copyW) / 2;
        var srcY = (height - copyH) / 2;
        var dstX = (BackBufferWidth - copyW) / 2;
        var dstY = (BackBufferHeight - copyH) / 2;
        var box = stackalloc uint[6] { (uint)srcX, (uint)srcY, 0, (uint)(srcX + copyW), (uint)(srcY + copyH), 1 };
        CtxCopySubresourceRegion(_context, _renderTarget, (uint)dstX, (uint)dstY, texture, box);
    }

    internal static (float X, float Y, float Width, float Height) UniformRect(int srcWidth, int srcHeight, int dstWidth, int dstHeight)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0)
        {
            return (0, 0, 0, 0);
        }

        var scale = Math.Min(dstWidth / (double)srcWidth, dstHeight / (double)srcHeight);
        var w = srcWidth * scale;
        var h = srcHeight * scale;
        return ((float)((dstWidth - w) * 0.5), (float)((dstHeight - h) * 0.5), (float)w, (float)h);
    }

    /// <summary>
    /// コマンドを GPU へ送るだけ（待たない）。WPF は D3D9 側から同じ RT を読む。
    /// デバイス喪失なら同じ寸法で作り直し、サーフェスが替わったことを通知する。
    /// </summary>
    private void Commit()
    {
        CtxFlush(_context);
        if (DeviceGetDeviceRemovedReason(_device) >= 0)
        {
            return;
        }

        var w = BackBufferWidth;
        var h = BackBufferHeight;
        AttachForComposition(w, h);
        SurfaceRecreated?.Invoke();
    }

    private void ReleaseShaderPipeline()
    {
        Release(ref _constants);
        Release(ref _sampler);
        Release(ref _ps);
        Release(ref _vs);
        _uploadedGain = -1;
        _uploadedMode = -1;
        _constantsDirty = true;
    }

    private void ReleaseSharedTarget()
    {
        Release(ref _surface9);
        Release(ref _texture9);
        Release(ref _rtv);
        Release(ref _renderTarget);
        BackBufferWidth = 0;
        BackBufferHeight = 0;
    }

    private void ReleaseAll()
    {
        if (_context != IntPtr.Zero)
        {
            CtxClearState(_context);
        }

        Release(ref _openedSharedTexture);
        _openedSharedHandle = IntPtr.Zero;
        ReleaseStaging();
        ReleasePlaneTextures();
        Release(ref _frameSrv);
        Release(ref _frameTexture);
        _frameWidth = 0;
        _frameHeight = 0;
        ReleaseShaderPipeline();
        ReleaseSharedTarget();
        Release(ref _device9);
        Release(ref _d3d9);
        Release(ref _context);
        Release(ref _device);
    }
}
