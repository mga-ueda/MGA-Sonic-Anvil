using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// D3D11 で描いた共有 RT を D3DImage として出す普通の WPF 要素。HWND を持たないので airspace は無く、
/// 上に重ねた WPF 要素（暗幕・格子・HUD・クローム）や Opacity はそのまま合成される。
/// 黒帯は presenter 側で描くので Image は Fill。presenter は初めて見えたときに作り、Unloaded で捨てる。
/// 色: <see cref="SetSourceColorInfo"/> で受けたソース色情報と、載っているモニタの ICC を presenter に渡す。
/// モニタ移動（LocationChanged）・DPI 変更・ディスプレイ設定変更で読み直す。
/// </summary>
internal sealed class DxgiVideoHost : Border
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.Fill,
        IsHitTestVisible = false,
    };

    private D3DImage? _d3dImage;
    private IntPtr _boundSurface;
    private VideoColorInfo _sourceColor = VideoColorInfo.Default;
    private string? _colorMonitor;
    private Window? _hookedWindow;
    private bool _displayHooked;

    public DxgiVideoHost()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Background = Brushes.Black;
        SnapsToDevicePixels = true;
        Child = _image;
        IsVisibleChanged += (_, _) => EnsurePresenter();
        Loaded += (_, _) =>
        {
            EnsurePresenter();
            HookDisplayChanges();
        };
        Unloaded += (_, _) =>
        {
            UnhookDisplayChanges();
            DisposePresenter();
        };
        SizeChanged += (_, _) => ResizePresenter();
    }

    public D3D11ImagePresenter? Presenter { get; private set; }

    /// <summary>
    /// サーフェスの結び直しや色設定の変更で、RT の中身が無効になった回数。
    /// 呼び出し側は「同じフレームでもこれが変わったら描き直す」判断に使う。
    /// </summary>
    public int RenderVersion { get; private set; }

    /// <summary>ソースの色情報。開いたときに渡す。presenter が無ければ作られたときに反映。</summary>
    public void SetSourceColorInfo(VideoColorInfo info)
    {
        _sourceColor = info;
        RefreshColorManagement(force: true);
    }

    /// <summary>
    /// デコード済みフレームを形式に応じて描く。NV12 は presenter の YUV シェーダへ平面のまま渡す（色は
    /// <see cref="SetSourceColorInfo"/> で渡した情報で変換）。シェーダが無い環境では CPU で BGRA に落として描く。
    /// </summary>
    public void PresentFrame(DxgiDecodedFrame frame)
    {
        if (frame.Format == DxgiFramePixelFormat.Nv12)
        {
            if (Presenter is { HasShaderPipeline: false })
            {
                PresentBgraFrame(Nv12ToBgraFallback(frame), frame.Width, frame.Height, frame.Width * 4);
                return;
            }

            PresentNv12Frame(frame.YPlane, frame.Stride, frame.UvPlane, frame.UvStride, frame.Width, frame.Height);
            return;
        }

        PresentBgraFrame(frame.Pixels, frame.Width, frame.Height, frame.Stride);
    }

    private byte[]? _nv12Fallback;

    /// <summary>シェーダ無し環境用の NV12→BGRA ソフト変換（整数近似、フレームの行列・レンジに従う）。通常は通らない。</summary>
    private byte[] Nv12ToBgraFallback(DxgiDecodedFrame frame)
    {
        var w = frame.Width;
        var h = frame.Height;
        var size = w * h * 4;
        if (_nv12Fallback is null || _nv12Fallback.Length != size)
        {
            _nv12Fallback = new byte[size];
        }

        var info = frame.ColorInfo.Normalized(h);
        var full = info.IsFullRange;
        // 係数（×1024）: 601 と 709 のみ区別。2020 は 709 で近似（フォールバック経路なので精度は求めない）。
        var bt601 = info.Matrix == VideoYuvMatrix.Bt601;
        int kr = bt601 ? 1436 : 1613, kgU = bt601 ? 352 : 192, kgV = bt601 ? 731 : 479, kb = bt601 ? 1815 : 1900;
        var yScale = full ? 1024 : 1196; // 255/219
        var yOffset = full ? 0 : 16;
        var cScale = full ? 1024 : 1169; // 255/224

        var src = frame.Pixels;
        var dst = _nv12Fallback;
        var uvRowStride = frame.UvStride;
        var uvBase = frame.UvOffset;
        for (var y = 0; y < h; y++)
        {
            var yRow = y * frame.Stride;
            var uvRow = uvBase + ((y / 2) * uvRowStride);
            var dRow = y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var yy = ((src[yRow + x] - yOffset) * yScale) >> 10;
                var uvIndex = uvRow + ((x / 2) * 2);
                var u = ((src[uvIndex] - 128) * cScale) >> 10;
                var v = ((src[uvIndex + 1] - 128) * cScale) >> 10;
                var r = yy + ((kr * v) >> 10);
                var g = yy - ((kgU * u) >> 10) - ((kgV * v) >> 10);
                var b = yy + ((kb * u) >> 10);
                var d = dRow + (x * 4);
                dst[d] = (byte)Math.Clamp(b, 0, 255);
                dst[d + 1] = (byte)Math.Clamp(g, 0, 255);
                dst[d + 2] = (byte)Math.Clamp(r, 0, 255);
                dst[d + 3] = 0xFF;
            }
        }

        return dst;
    }

    public void PresentNv12Frame(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, int width, int height)
    {
        var presenter = Presenter;
        if (presenter is null)
        {
            return;
        }

        var locked = Lock();
        try
        {
            presenter.PresentNv12Frame(y, yStride, uv, uvStride, width, height);
        }
        finally
        {
            UnlockDirty(locked);
        }
    }

    public void PresentBgraFrame(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        var presenter = Presenter;
        if (presenter is null)
        {
            return;
        }

        // Lock 中は WPF の描画スレッドが前面バッファへ写さないので、描きかけを拾わない。
        var locked = Lock();
        try
        {
            presenter.PresentBgraFrame(bgra, width, height, stride);
        }
        finally
        {
            UnlockDirty(locked);
        }
    }

    public void Clear(float r = 0, float g = 0, float b = 0) => Draw(p => p.Clear(r, g, b));

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ResizePresenter();
        // DPI が変わる＝別モニタへ移った可能性が高い。
        RefreshColorManagement(force: true);
    }

    private IntPtr Hwnd() => (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;

    /// <summary>モニタが替わったとき（force なら常に）presenter の色定数を組み直す。</summary>
    private void RefreshColorManagement(bool force)
    {
        var presenter = Presenter;
        if (presenter is null)
        {
            return;
        }

        var hwnd = Hwnd();
        var monitor = MonitorColorProfile.GetMonitorDeviceName(hwnd);
        if (!force && string.Equals(monitor, _colorMonitor, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _colorMonitor = monitor;
        presenter.ApplyColorManagement(_sourceColor, hwnd);
        RenderVersion++;
    }

    private void HookDisplayChanges()
    {
        if (!_displayHooked)
        {
            try
            {
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                _displayHooked = true;
            }
            catch
            {
                // サービス環境などで SystemEvents が使えないときは諦める。
            }
        }

        var window = Window.GetWindow(this);
        if (window is not null && !ReferenceEquals(window, _hookedWindow))
        {
            if (_hookedWindow is not null)
            {
                _hookedWindow.LocationChanged -= OnWindowLocationChanged;
            }

            _hookedWindow = window;
            window.LocationChanged += OnWindowLocationChanged;
        }
    }

    private void UnhookDisplayChanges()
    {
        if (_displayHooked)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _displayHooked = false;
        }

        if (_hookedWindow is not null)
        {
            _hookedWindow.LocationChanged -= OnWindowLocationChanged;
            _hookedWindow = null;
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e) => RefreshColorManagement(force: false);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // SystemEvents は別スレッドから来ることがある。
        if (Dispatcher.CheckAccess())
        {
            RefreshColorManagement(force: true);
        }
        else
        {
            Dispatcher.BeginInvoke(() => RefreshColorManagement(force: true));
        }
    }

    private void Draw(Action<D3D11ImagePresenter> draw)
    {
        var presenter = Presenter;
        if (presenter is null)
        {
            return;
        }

        var locked = Lock();
        try
        {
            draw(presenter);
        }
        finally
        {
            UnlockDirty(locked);
        }
    }

    private void EnsurePresenter()
    {
        if (Presenter is not null || !IsLoaded || !IsVisible)
        {
            return;
        }

        var (w, h) = PixelSize();
        var presenter = new D3D11ImagePresenter();
        presenter.AttachForComposition(w, h);
        if (!presenter.IsReady)
        {
            presenter.Dispose();
            return;
        }

        Presenter = presenter;
        _d3dImage = new D3DImage();
        _d3dImage.IsFrontBufferAvailableChanged += OnFrontBufferAvailableChanged;
        _image.Source = _d3dImage;
        presenter.SurfaceRecreated += RebindSurface;
        RebindSurface();
        _colorMonitor = null;
        RefreshColorManagement(force: true);
    }

    private void DisposePresenter()
    {
        if (_d3dImage is not null)
        {
            _d3dImage.IsFrontBufferAvailableChanged -= OnFrontBufferAvailableChanged;
            _d3dImage.Lock();
            _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
            _d3dImage.Unlock();
            _d3dImage = null;
        }

        _image.Source = null;
        _boundSurface = IntPtr.Zero;
        if (Presenter is not null)
        {
            Presenter.SurfaceRecreated -= RebindSurface;
            Presenter.Dispose();
            Presenter = null;
        }
    }

    private void ResizePresenter()
    {
        if (Presenter is null)
        {
            EnsurePresenter();
            return;
        }

        var (w, h) = PixelSize();
        if (w == Presenter.BackBufferWidth && h == Presenter.BackBufferHeight)
        {
            return;
        }

        // Resize は SurfaceRecreated → RebindSurface で SetBackBuffer し直す。
        Presenter.Resize(w, h);
    }

    /// <summary>サーフェスが替わったら（Resize・デバイス作り直し）SetBackBuffer をやり直す。</summary>
    private void RebindSurface()
    {
        if (_d3dImage is null)
        {
            return;
        }

        var surface = Presenter?.D3d9Surface ?? IntPtr.Zero;
        _d3dImage.Lock();
        try
        {
            // 第 3 引数: GPU 前面バッファが使えない環境（RDP など）はソフト描画へ写して出す。
            _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface, true);
            _boundSurface = surface;
            if (surface != IntPtr.Zero)
            {
                _d3dImage.AddDirtyRect(new Int32Rect(0, 0, _d3dImage.PixelWidth, _d3dImage.PixelHeight));
            }
        }
        finally
        {
            _d3dImage.Unlock();
        }

        // 新しい RT は黒なので、同じフレームでも描き直してもらう。
        RenderVersion++;
    }

    private void OnFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 画面ロック・UAC などで前面バッファが戻ったら、同じサーフェスを結び直す。
        if (_d3dImage?.IsFrontBufferAvailable == true)
        {
            RebindSurface();
        }
    }

    private bool Lock()
    {
        if (_d3dImage is null)
        {
            return false;
        }

        _d3dImage.Lock();
        return true;
    }

    private void UnlockDirty(bool locked)
    {
        if (!locked || _d3dImage is null)
        {
            return;
        }

        if (_boundSurface != IntPtr.Zero && _d3dImage.PixelWidth > 0 && _d3dImage.PixelHeight > 0)
        {
            _d3dImage.AddDirtyRect(new Int32Rect(0, 0, _d3dImage.PixelWidth, _d3dImage.PixelHeight));
        }

        _d3dImage.Unlock();
    }

    private (int Width, int Height) PixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return (
            Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY)));
    }
}
