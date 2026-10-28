using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using MgaSonicAnvil.Video.Dxgi;
using Xunit;
using static MgaSonicAnvil.Video.Dxgi.D3D11Native;

namespace MgaSonicAnvil.Tests;

public sealed class DxgiPresenterTests
{
    [Fact]
    public void TestPattern_IsBgraOfRequestedSizeAndOpaque()
    {
        var bgra = DxgiPrototypeFrameSource.GenerateTestPattern(TimeSpan.FromSeconds(1.25), 320, 180);
        Assert.Equal(320 * 180 * 4, bgra.Length);
        for (var i = 3; i < bgra.Length; i += 4)
        {
            Assert.Equal(0xFF, bgra[i]);
        }
    }

    [Fact]
    public void TestPattern_MovesWithTime()
    {
        var a = DxgiPrototypeFrameSource.GenerateTestPattern(TimeSpan.FromSeconds(0.2), 160, 90);
        var b = DxgiPrototypeFrameSource.GenerateTestPattern(TimeSpan.FromSeconds(0.9), 160, 90);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void UniformRect_LetterboxesWideSourceIntoTallTarget()
    {
        var (x, y, w, h) = D3D11ImagePresenter.UniformRect(1920, 1080, 1000, 1000);
        Assert.Equal(0, x, 3);
        Assert.Equal(1000, w, 3);
        Assert.Equal(562.5, h, 3);
        Assert.Equal((1000 - 562.5) / 2, y, 3);
    }

    [Fact]
    public void FrameSource_MissingFileFailsWithoutThrowing()
    {
        using var source = new DxgiPrototypeFrameSource();
        Assert.False(source.TryOpen(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4"), out _, out _, out _));
        Assert.False(source.TryReadFrameAt(TimeSpan.Zero, out var bgra, out _, out _, out _));
        Assert.Null(bgra);
    }

    [Fact]
    public void Presenter_PresentsFramesIntoSharedSurfaceForD3DImage()
    {
        RunSta(() =>
        {
            using var presenter = new D3D11ImagePresenter();
            presenter.AttachForComposition(320, 240);
            Assert.True(presenter.IsReady);
            Assert.True(presenter.HasShaderPipeline);
            Assert.NotEqual(IntPtr.Zero, presenter.D3d9Surface);
            Assert.Equal(320, presenter.BackBufferWidth);

            var image = new D3DImage();
            image.Lock();
            image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, presenter.D3d9Surface, true);
            Assert.Equal(320, image.PixelWidth);
            Assert.Equal(240, image.PixelHeight);

            presenter.Clear(0, 0, 0);
            var frame = DxgiPrototypeFrameSource.GenerateTestPattern(TimeSpan.FromSeconds(0.5), 640, 360);
            presenter.PresentBgraFrame(frame, 640, 360, 640 * 4);
            presenter.Brightness = 0.3f;
            presenter.PresentBgraFrame(frame, 640, 360, 640 * 4);
            presenter.PresentTexture(IntPtr.Zero);
            image.AddDirtyRect(new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight));
            image.Unlock();

            var recreated = 0;
            presenter.SurfaceRecreated += () => recreated++;
            presenter.Resize(200, 400);
            Assert.Equal(1, recreated);
            Assert.True(presenter.IsReady);
            Assert.Equal(400, presenter.BackBufferHeight);
            Assert.NotEqual(IntPtr.Zero, presenter.D3d9Surface);

            // 同寸の Resize ではサーフェスを作り直さない。
            presenter.Resize(200, 400);
            Assert.Equal(1, recreated);

            image.Lock();
            image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, presenter.D3d9Surface, true);
            Assert.Equal(200, image.PixelWidth);
            Assert.Equal(400, image.PixelHeight);
            presenter.PresentBgraFrame(frame, 640, 360, 640 * 4);
            image.AddDirtyRect(new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight));
            image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
            image.Unlock();
        });
    }

    [Fact]
    public void FrameSource_ReadsFramesFromLocalCaptureWhenPresent()
    {
        var path = LocalCapturePath();
        if (!File.Exists(path))
        {
            return;
        }

        RunSta(() =>
        {
            using var source = new DxgiPrototypeFrameSource();
            Assert.True(source.TryOpen(path, out var duration, out var width, out var height));
            Assert.True(duration > TimeSpan.Zero);
            Assert.True(width > 0 && height > 0);

            // NV12 が通れば Y stride = 幅、フォールバックの BGRA なら 幅*4。どちらでも寸法ぶんのバイトがある。
            Assert.True(source.TryReadFrameAt(TimeSpan.FromSeconds(1), out var pixels, out var w, out var h, out var stride));
            Assert.NotNull(pixels);
            Assert.Equal(DxgiDecodedFrame.StrideFor(source.PixelFormat, w), stride);
            Assert.Equal(source.Stride, stride);
            Assert.True(pixels!.Length >= DxgiDecodedFrame.RequiredBytes(source.PixelFormat, w, h));
            Assert.Equal(source.FrameBytes, DxgiDecodedFrame.RequiredBytes(source.PixelFormat, w, h));

            if (source.PixelFormat == DxgiFramePixelFormat.Nv12)
            {
                // 実映像の NV12 を presenter の YUV シェーダで描けて、不透明な画素が出る。
                var frame = new DxgiDecodedFrame(0, 1, w, h, DxgiFramePixelFormat.Nv12, pixels, source.ColorInfo);
                using var presenter = new D3D11ImagePresenter();
                presenter.AttachForComposition(320, 240);
                if (presenter.IsReady && presenter.HasShaderPipeline)
                {
                    presenter.UseMonitorProfile = false;
                    presenter.ApplyColorManagement(source.ColorInfo, IntPtr.Zero);
                    presenter.PresentNv12Frame(frame.YPlane, frame.Stride, frame.UvPlane, frame.UvStride, w, h);
                    Assert.True(presenter.TryReadBackPixel(160, 120, out var px));
                    Assert.Equal(0xFFu, px >> 24);
                }
            }

            // 後戻りシークと連続読み。
            Assert.True(source.TryReadFrameAt(TimeSpan.Zero, out _, out _, out _, out _));
            Assert.True(source.TryReadFrameAt(TimeSpan.FromMilliseconds(100), out _, out _, out _, out _));
        });
    }

    [Fact]
    public void DecodedFrame_Nv12LayoutAndRequiredBytes()
    {
        // 偶数寸法: Y = w*h、UV = w*(h/2)、合計 1.5 倍。
        Assert.Equal(1920 * 1080 * 3 / 2, DxgiDecodedFrame.RequiredBytes(DxgiFramePixelFormat.Nv12, 1920, 1080));
        Assert.Equal(1920 * 1080 * 4, DxgiDecodedFrame.RequiredBytes(DxgiFramePixelFormat.Bgra32, 1920, 1080));
        Assert.Equal(0, DxgiDecodedFrame.RequiredBytes(DxgiFramePixelFormat.Nv12, 0, 10));

        // 奇数寸法: UV stride は切り上げ、UV 行数も切り上げ。
        Assert.Equal(6, DxgiDecodedFrame.UvStrideFor(5));
        Assert.Equal((5 * 3) + (6 * 2), DxgiDecodedFrame.RequiredBytes(DxgiFramePixelFormat.Nv12, 5, 3));

        var nv12 = new DxgiDecodedFrame(0, 1, 4, 2, DxgiFramePixelFormat.Nv12, new byte[12], VideoColorInfo.Default);
        Assert.Equal(DxgiFramePixelFormat.Nv12, nv12.Format);
        Assert.Equal(4, nv12.Stride);
        Assert.Equal(4, nv12.UvStride);
        Assert.Equal(8, nv12.UvOffset);
        Assert.Equal(8, nv12.YPlane.Length);
        Assert.Equal(4, nv12.UvPlane.Length);

        var bgra = new DxgiDecodedFrame(0, 1, 4, 2, new byte[32], VideoColorInfo.Default);
        Assert.Equal(DxgiFramePixelFormat.Bgra32, bgra.Format);
        Assert.Equal(16, bgra.Stride);
        Assert.Equal(0, bgra.UvStride);
        Assert.True(bgra.UvPlane.IsEmpty);
        Assert.Equal(32, bgra.YPlane.Length);
    }

    [Fact]
    public void Presenter_DrawsNv12DecodedFramePlanes()
    {
        RunSta(() =>
        {
            using var presenter = new D3D11ImagePresenter();
            presenter.AttachForComposition(320, 240);
            Assert.True(presenter.IsReady && presenter.HasShaderPipeline);
            presenter.UseMonitorProfile = false;
            presenter.ApplyColorManagement(VideoColorInfo.Default, IntPtr.Zero);

            // 灰色 1 色の NV12 フレーム（Y=128, U=V=128）を DxgiDecodedFrame の平面ビューで渡す。
            const int w = 64, h = 48;
            var pixels = new byte[DxgiDecodedFrame.RequiredBytes(DxgiFramePixelFormat.Nv12, w, h)];
            Array.Fill(pixels, (byte)128);
            var frame = new DxgiDecodedFrame(0, 1, w, h, DxgiFramePixelFormat.Nv12, pixels, VideoColorInfo.Default);
            presenter.PresentNv12Frame(frame.YPlane, frame.Stride, frame.UvPlane, frame.UvStride, frame.Width, frame.Height);
            Assert.True(presenter.TryReadBackPixel(160, 120, out var gray));
            Assert.InRange((int)((gray >> 8) & 0xFF), 127, 133);
            Assert.Equal(0xFFu, gray >> 24);
        });
    }

    // ---- 光漏れ（ambient spill）の縮小ビットマップ ----

    [Fact]
    public void AmbientSpill_DownscaledSizeCapsLongestEdgeAndKeepsAspect()
    {
        // 最長辺が上限以下ならそのまま。
        Assert.Equal((320, 240), AmbientSpillFrame.DownscaledSize(320, 240));

        // 横長 1920x1080 → 最長辺 480、アスペクト維持（16:9）。
        var (w, h) = AmbientSpillFrame.DownscaledSize(1920, 1080);
        Assert.Equal(AmbientSpillFrame.MaxEdge, w);
        Assert.Equal(270, h);

        // 縦長もアスペクト維持（最長辺が上限）。
        var (pw, ph) = AmbientSpillFrame.DownscaledSize(1080, 1920);
        Assert.Equal(AmbientSpillFrame.MaxEdge, ph);
        Assert.Equal(270, pw);

        Assert.Equal((0, 0), AmbientSpillFrame.DownscaledSize(0, 100));
    }

    [Fact]
    public void AmbientSpill_FillBgraFromNv12GrayIsOpaqueGray()
    {
        const int w = 64, h = 48;
        var pixels = new byte[DxgiDecodedFrame.RequiredBytes(DxgiFramePixelFormat.Nv12, w, h)];
        Array.Fill(pixels, (byte)128);
        var frame = new DxgiDecodedFrame(0, 1, w, h, DxgiFramePixelFormat.Nv12, pixels, VideoColorInfo.Default);

        var (dw, dh) = AmbientSpillFrame.DownscaledSize(w, h);
        var dst = new byte[dw * dh * 4];
        AmbientSpillFrame.FillBgra(frame, dst, dw, dh);

        // リミテッド 709 の Y=128,U=V=128 は ~130 の無彩色、アルファは不透明。
        for (var i = 0; i < dst.Length; i += 4)
        {
            Assert.InRange((int)dst[i], 120, 140);
            Assert.InRange((int)dst[i + 1], 120, 140);
            Assert.InRange((int)dst[i + 2], 120, 140);
            Assert.Equal(0xFF, dst[i + 3]);
        }
    }

    [Fact]
    public void AmbientSpill_FillBgraFromBgraCopiesAndForcesOpaque()
    {
        const int w = 8, h = 8;
        var src = new byte[w * h * 4];
        for (var i = 0; i < src.Length; i += 4)
        {
            src[i] = 0x10;     // B
            src[i + 1] = 0x20; // G
            src[i + 2] = 0x30; // R
            src[i + 3] = 0x00; // 透明でも結果は不透明にする
        }

        var frame = new DxgiDecodedFrame(0, 1, w, h, src, VideoColorInfo.Default);
        // 等倍（最長辺が上限以下）。
        var dst = new byte[w * h * 4];
        AmbientSpillFrame.FillBgra(frame, dst, w, h);
        for (var i = 0; i < dst.Length; i += 4)
        {
            Assert.Equal(0x10, dst[i]);
            Assert.Equal(0x20, dst[i + 1]);
            Assert.Equal(0x30, dst[i + 2]);
            Assert.Equal(0xFF, dst[i + 3]);
        }
    }

    // ---- 非同期ポンプ ----

    [Fact]
    public void AsyncPump_MissingFileFailsWithoutThrowing()
    {
        using var pump = new DxgiAsyncFramePump();
        Assert.False(pump.Open(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4")));
        Assert.False(pump.IsOpen);
        pump.RequestPosition(TimeSpan.FromSeconds(1));
        Assert.False(pump.TryGetFrame(TimeSpan.FromSeconds(1), out var frame));
        Assert.Null(frame);
        pump.Close();
    }

    [Fact]
    public void AsyncPump_DecodesOnWorkerAndServesFromCache()
    {
        var path = LocalCapturePath();
        if (!File.Exists(path))
        {
            return;
        }

        using var pump = new DxgiAsyncFramePump();
        Assert.True(pump.Open(path));
        Assert.True(pump.IsOpen);
        Assert.True(pump.Duration > TimeSpan.Zero);
        Assert.True(pump.Width > 0 && pump.Height > 0);
        Assert.True(pump.FrameDuration > TimeSpan.Zero);

        var frame = WaitForFrame(pump, TimeSpan.FromSeconds(1));
        Assert.NotNull(frame);
        Assert.Equal(pump.Width, frame!.Width);
        Assert.Equal(pump.Height, frame.Height);
        Assert.Equal(pump.PixelFormat, frame.Format);
        Assert.Equal(DxgiDecodedFrame.StrideFor(frame.Format, frame.Width), frame.Stride);
        Assert.True(frame.Pixels.Length >= DxgiDecodedFrame.RequiredBytes(frame.Format, frame.Width, frame.Height));
        Assert.True(Math.Abs((frame.Pts - TimeSpan.FromSeconds(1)).TotalMilliseconds) < 300);
        Assert.True(pump.CachedFrameCount > 0);

        // 後戻り（キーフレームまで戻って読み直す）と、小さな前進（Seek 無しで読み進める）。
        var back = WaitForFrame(pump, TimeSpan.Zero);
        Assert.NotNull(back);
        Assert.True(back!.Pts <= TimeSpan.FromMilliseconds(300));

        var step = WaitForFrame(pump, TimeSpan.FromMilliseconds(100));
        Assert.NotNull(step);

        // 尺を超えた位置でも例外なく（最後のフレームか前のフレームで）返る。
        pump.RequestPosition(pump.Duration + TimeSpan.FromSeconds(5));
        Thread.Sleep(100);
        pump.TryGetFrame(pump.Duration + TimeSpan.FromSeconds(5), out _);

        pump.Close();
        Assert.False(pump.IsOpen);
        Assert.False(pump.TryGetFrame(TimeSpan.Zero, out _));

        // 閉じたあとに開き直せる。
        Assert.True(pump.Open(path));
        Assert.NotNull(WaitForFrame(pump, TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void AsyncPump_ShuttleModeWidensPrefetchSeekLimitAndTolerance()
    {
        using var pump = new DxgiAsyncFramePump();

        // 既定（通常）: 先読み 250ms／Seek 境界 1.5s／最寄り 250ms／1 起床 6 枚。
        Assert.False(pump.IsShuttleMode);
        Assert.Equal(TimeSpan.FromMilliseconds(250), pump.EffectivePrefetch);
        Assert.Equal(TimeSpan.FromSeconds(1.5), pump.EffectiveForwardReadLimit);
        Assert.Equal(TimeSpan.FromMilliseconds(250), pump.EffectiveNearestTolerance);
        Assert.Equal(6, pump.EffectiveMaxSamplesPerWake);

        // シャトル: 先読み 1.5〜2.5s、読み進め上限 4〜6s（遅れが縮まなければこれ未満でも Seek）、最寄り 1.5〜2s、1 起床 12〜18 枚。
        pump.SetShuttleMode(true);
        Assert.True(pump.IsShuttleMode);
        Assert.InRange(pump.EffectivePrefetch, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.5));
        Assert.InRange(pump.EffectiveForwardReadLimit, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6));
        Assert.InRange(pump.EffectiveNearestTolerance, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2));
        Assert.InRange(pump.EffectiveMaxSamplesPerWake, 12, 18);
        Assert.True(pump.EffectivePrefetch > pump.Prefetch);
        Assert.True(pump.EffectiveNearestTolerance > pump.NearestTolerance);

        // 呼び出し側が通常値をもっと広げていれば、シャトル中もそれを下回らない。
        pump.Prefetch = TimeSpan.FromSeconds(3);
        pump.NearestTolerance = TimeSpan.FromSeconds(3);
        Assert.Equal(TimeSpan.FromSeconds(3), pump.EffectivePrefetch);
        Assert.Equal(TimeSpan.FromSeconds(3), pump.EffectiveNearestTolerance);

        // 抜けると通常値へ戻る（同じ値の再設定は何もしない）。
        pump.SetShuttleMode(false);
        pump.SetShuttleMode(false);
        Assert.False(pump.IsShuttleMode);
        Assert.Equal(TimeSpan.FromSeconds(3), pump.EffectivePrefetch);
        Assert.Equal(TimeSpan.FromSeconds(1.5), pump.EffectiveForwardReadLimit);
        Assert.Equal(6, pump.EffectiveMaxSamplesPerWake);

        // 開いていなくても呼べる。
        pump.SetShuttleMode(true);
        pump.RequestPosition(TimeSpan.FromSeconds(1));
        Assert.False(pump.TryGetFrame(TimeSpan.FromSeconds(1), out _));
    }

    [Fact]
    public void AsyncPump_ShuttleModeKeepsServingAcrossSeekLimitJump()
    {
        var path = LocalCapturePath();
        if (!File.Exists(path))
        {
            return;
        }

        using var pump = new DxgiAsyncFramePump();
        Assert.True(pump.Open(path));
        pump.SetShuttleMode(true);

        var start = TimeSpan.FromSeconds(1);
        var first = WaitForFrame(pump, start);
        Assert.NotNull(first);

        // 通常なら Seek になる前進（1.5s 超、シャトル上限 5s 未満）。要求が止まっていれば遅れは縮むので読み進め、
        // 縮まなければ Seek。どちらでもフレームが出る。
        var ahead = start + TimeSpan.FromSeconds(3);
        if (ahead < pump.Duration)
        {
            var frame = WaitForFrame(pump, ahead);
            Assert.NotNull(frame);
            Assert.True(frame!.Covers(ahead.Ticks) || frame.DistanceTo(ahead.Ticks) <= pump.EffectiveNearestTolerance.Ticks);
        }

        // 抜けてもキャッシュは残り、同じ位置はそのまま引ける。
        pump.SetShuttleMode(false);
        Assert.True(pump.CachedFrameCount > 0);
        pump.Close();
    }

    [Fact]
    public void AsyncPump_ReseekDecisionTreatsSkipAndTailHolesAsHoles()
    {
        const long dur = 100;

        // 捨て読みの範囲（lastSkipEnd より手前）は穴 → Seek。
        Assert.True(DxgiAsyncFramePump.ShouldReseekInsideRange(500, lastSkipEnd: 800, cachedEndTicks: 2000, lastSeekTicks: -1));

        // キャッシュ末尾より先（後ろ側の追い出し・捨て読み）も穴 → Seek。
        Assert.True(DxgiAsyncFramePump.ShouldReseekInsideRange(2000, long.MinValue, cachedEndTicks: 2000, lastSeekTicks: -1));
        Assert.True(DxgiAsyncFramePump.ShouldReseekInsideRange(5 * dur, long.MinValue, cachedEndTicks: -1, lastSeekTicks: -1));

        // 積んだフレームの間の本物の PTS の飛びは Seek しない。
        Assert.False(DxgiAsyncFramePump.ShouldReseekInsideRange(1500, lastSkipEnd: 800, cachedEndTicks: 2000, lastSeekTicks: -1));

        // 同じ位置へ Seek 済みなら繰り返さない。
        Assert.False(DxgiAsyncFramePump.ShouldReseekInsideRange(500, lastSkipEnd: 800, cachedEndTicks: 2000, lastSeekTicks: 500));
    }

    [Fact]
    public void AsyncPump_SmallCacheStepsForwardWithoutStallingInPrefetchHoles()
    {
        var path = LocalCapturePath();
        if (!File.Exists(path))
        {
            return;
        }

        // 容量 6: 先読み（4 枚に抑えられる）を target 中心で追い出すと後半が捨て読みになり、
        // そこへ要求が来ると「区間内の隙間」で止まっていた。
        using var pump = new DxgiAsyncFramePump(cacheCapacity: 6);
        Assert.True(pump.Open(path));
        pump.SetShuttleMode(true);
        var step = pump.FrameDuration;
        var start = TimeSpan.FromSeconds(1);
        Assert.NotNull(WaitForFrame(pump, start));
        for (var i = 1; i <= 24; i++)
        {
            var pos = start + TimeSpan.FromTicks(step.Ticks * i) + TimeSpan.FromTicks(step.Ticks / 2);
            if (pos >= pump.Duration)
            {
                break;
            }

            var frame = WaitForFrame(pump, pos);
            Assert.NotNull(frame);
            Assert.True(frame!.Covers(pos.Ticks), $"step {i}: {frame.Pts} does not cover {pos}");
        }
    }

    [Fact]
    public void AsyncPump_ShuttleReturnsIntoSkippedGhostRangeServesAgain()
    {
        var path = LocalCapturePath();
        if (!File.Exists(path))
        {
            return;
        }

        using var pump = new DxgiAsyncFramePump(cacheCapacity: 6);
        Assert.True(pump.Open(path));
        pump.SetShuttleMode(true);
        var start = TimeSpan.FromSeconds(0.5);
        var far = start + TimeSpan.FromSeconds(3.5);
        if (far >= pump.Duration)
        {
            return;
        }

        Assert.NotNull(WaitForFrame(pump, start));

        // 5s 未満で要求が止まっている前進は遅れが縮むので読み進める。容量ぶんより手前は捨て読みになる。
        Assert.NotNull(WaitForFrame(pump, far));

        // 捨て読みした範囲（読み手は既に過ぎている）へ戻っても、Seek し直して含むフレームが出る。
        var ghost = far - TimeSpan.FromSeconds(1);
        var frame = WaitForFrame(pump, ghost);
        Assert.NotNull(frame);
        Assert.True(frame!.Covers(ghost.Ticks), $"{frame.Pts} does not cover {ghost}");

        // 続けて前進しても止まらない。
        var next = ghost + TimeSpan.FromTicks(pump.FrameDuration.Ticks * 5);
        var after = WaitForFrame(pump, next);
        Assert.NotNull(after);
        Assert.True(after!.Covers(next.Ticks));
    }

    [Fact]
    public void AsyncPump_ShuttleMissSeeksUnlessGapIsShrinking()
    {
        var limit = TimeSpan.FromSeconds(5).Ticks;
        var s = TimeSpan.TicksPerSecond;

        // 未観測（Seek 直後／含むフレームが出た直後）は 1 起床だけ読み進めて様子を見る。
        Assert.False(DxgiAsyncFramePump.ShouldSeekOnShuttleMiss(gap: 2 * s, previousGap: long.MaxValue, limit));

        // 遅れが縮んでいる（読みが 3 倍速に勝っている）なら読み進めを続ける。
        Assert.False(DxgiAsyncFramePump.ShouldSeekOnShuttleMiss(gap: 1 * s, previousGap: 2 * s, limit));

        // 縮まない／広がる（読みが負けている）なら、巻き戻しと同じく Seek。
        Assert.True(DxgiAsyncFramePump.ShouldSeekOnShuttleMiss(gap: 2 * s, previousGap: 2 * s, limit));
        Assert.True(DxgiAsyncFramePump.ShouldSeekOnShuttleMiss(gap: 3 * s, previousGap: 2 * s, limit));

        // 上限以上は縮んでいても Seek。
        Assert.True(DxgiAsyncFramePump.ShouldSeekOnShuttleMiss(gap: 5 * s, previousGap: 9 * s, limit));
        Assert.True(DxgiAsyncFramePump.ShouldSeekOnShuttleMiss(gap: 6 * s, previousGap: long.MaxValue, limit));
    }

    [Fact]
    public void AsyncPump_CatchUpPrefetchShortensOnlyWhileShuttleCatchingUp()
    {
        var frame = TimeSpan.FromSeconds(1.0 / 30).Ticks;
        var target = TimeSpan.FromSeconds(10).Ticks;
        var normalEnd = target + TimeSpan.FromSeconds(1.5).Ticks;

        // シャトル中の Seek 後の追いつき: 目標＋1〜2 枚で止める（長い滑走路を読まない）。
        var shortEnd = DxgiAsyncFramePump.CatchUpPrefetchEnd(target, frame, normalEnd, shuttle: true, catchingUp: true);
        Assert.True(shortEnd > target);
        Assert.InRange(shortEnd, target + frame, target + (3 * frame));

        // 通常の終端の方が手前なら、そちら。
        Assert.Equal(target + frame, DxgiAsyncFramePump.CatchUpPrefetchEnd(target, frame, target + frame, shuttle: true, catchingUp: true));

        // 追いついた後／通常モードはいつもの先読み。
        Assert.Equal(normalEnd, DxgiAsyncFramePump.CatchUpPrefetchEnd(target, frame, normalEnd, shuttle: true, catchingUp: false));
        Assert.Equal(normalEnd, DxgiAsyncFramePump.CatchUpPrefetchEnd(target, frame, normalEnd, shuttle: false, catchingUp: true));
    }

    [Fact]
    public void AsyncPump_ShuttleForwardSweepKeepsServingAdvancingFrames()
    {
        var path = LocalCapturePath();
        if (!File.Exists(path))
        {
            return;
        }

        // 小さなキャッシュで、要求位置を読みより速く前へ流す。旧実装は Seek せず読み進めで追い続け、
        // 最寄り許容を越えた時点で何も返さなくなった（止まって見える）。巻き戻しと同じく Seek で追従すれば、
        // 着地したキーフレーム以降が出続け、出るフレームの PTS も前へ進む。
        using var pump = new DxgiAsyncFramePump(cacheCapacity: 6);
        Assert.True(pump.Open(path));
        pump.SetShuttleMode(true);

        var start = TimeSpan.FromSeconds(0.5);
        var end = pump.Duration - TimeSpan.FromSeconds(0.5);
        if (end <= start + TimeSpan.FromSeconds(2))
        {
            return;
        }

        Assert.NotNull(WaitForFrame(pump, start));

        // 1 tick（10ms）ごとに 0.5s 進める（50 倍速相当）。
        var step = TimeSpan.FromMilliseconds(500);
        var servedCount = 0;
        var maxServedPts = TimeSpan.MinValue;
        DxgiDecodedFrame? last = null;
        for (var pos = start + step; pos < end; pos += step)
        {
            pump.RequestPosition(pos);
            Thread.Sleep(10);
            if (pump.TryGetFrame(pos, out var frame) && frame is not null)
            {
                servedCount++;
                if (frame.Pts > maxServedPts)
                {
                    maxServedPts = frame.Pts;
                }

                last = frame;
            }
        }

        // 流している間にもフレームが出ていて、その PTS は開始位置より前へ進んでいる。
        Assert.True(servedCount > 0);
        Assert.True(maxServedPts > start, $"served pts {maxServedPts} did not advance from {start}");
        Assert.NotNull(last);

        // 止まった要求位置には（Seek して）含むフレームが出る。
        var final = WaitForFrame(pump, end);
        Assert.NotNull(final);
        Assert.True(final!.Covers(end.Ticks) || final.DistanceTo(end.Ticks) <= pump.EffectiveNearestTolerance.Ticks);
        pump.Close();
    }

    // ---- 保持（Hold）: 直前の 1 枚が覆っている間は要求も Present もしない ----

    [Fact]
    public void FrameHold_HoldsOnlyWhileLastFrameCoversTarget()
    {
        var frame = TimeSpan.FromSeconds(1.0 / 30).Ticks;
        var pts = 10 * frame;

        // フレーム内（先頭・中・末尾手前）は保持。
        Assert.True(DxgiFrameHold.ShouldHold(pts, frame, pts, frame));
        Assert.True(DxgiFrameHold.ShouldHold(pts, frame, pts + (frame / 2), frame));
        Assert.True(DxgiFrameHold.ShouldHold(pts, frame, pts + frame - 1, frame));

        // 次のフレームの先頭（境界ちょうど）と手前のフレームは保持しない（1 倍再生で 1 枚おきに落とさない／1 コマ戻しが効く）。
        Assert.False(DxgiFrameHold.ShouldHold(pts, frame, pts + frame, frame));
        Assert.False(DxgiFrameHold.ShouldHold(pts, frame, pts - 1, frame));
        Assert.False(DxgiFrameHold.ShouldHold(pts, frame, pts - frame, frame));
        Assert.False(DxgiFrameHold.ShouldHold(pts, frame, pts + (2 * frame), frame));

        // 長さが取れていない（≦1 tick）フレームはソースのフレーム間隔 1 枚ぶんとみなす。
        Assert.True(DxgiFrameHold.ShouldHold(pts, 0, pts + (frame / 2), frame));
        Assert.True(DxgiFrameHold.ShouldHold(pts, 1, pts + frame - 1, frame));
        Assert.False(DxgiFrameHold.ShouldHold(pts, 1, pts + frame, frame));

        // VFR で 1 枚が長ければその長さで覆う。
        Assert.True(DxgiFrameHold.ShouldHold(pts, 3 * frame, pts + (2 * frame), frame));

        // 不正値は保持しない。
        Assert.False(DxgiFrameHold.ShouldHold(-1, frame, 0, frame));
        Assert.False(DxgiFrameHold.ShouldHold(pts, frame, -1, frame));
    }

    [Fact]
    public void FrameHold_DoesNotDropFramesAtRealtimeTick()
    {
        // 33ms tick × 33.3ms フレーム: 保持が「覆っている間だけ」なら、通る tick で出るフレームは単調に進み、
        // 1 枚おきに抜ける（f1 を飛ばして f2）ことがない。
        var frame = TimeSpan.FromSeconds(1.0 / 30).Ticks;
        var tick = TimeSpan.FromMilliseconds(33).Ticks;
        long lastPts = -1, lastDur = 0;
        var presented = new List<long>();
        for (long t = 0; t < 60 * frame; t += tick)
        {
            if (lastPts >= 0 && DxgiFrameHold.ShouldHold(lastPts, lastDur, t, frame))
            {
                continue;
            }

            var index = t / frame;
            lastPts = index * frame;
            lastDur = frame;
            presented.Add(index);
        }

        for (var i = 1; i < presented.Count; i++)
        {
            Assert.Equal(presented[i - 1] + 1, presented[i]);
        }

        Assert.Equal(0, presented[0]);
        Assert.True(presented.Count >= 58);
    }

    // ---- スクラブ プレビュー ----

    [Fact]
    public void AsyncPump_ScrubPreviewModeWidensToleranceAndResetsOnClose()
    {
        using var pump = new DxgiAsyncFramePump();
        Assert.False(pump.IsScrubPreviewMode);
        Assert.Equal(TimeSpan.FromMilliseconds(250), pump.EffectiveNearestTolerance);

        // プレビュー中: 着地したキーフレーム（GOP ぶん離れていることがある）を出すため最寄り許容を大きく。
        pump.SetScrubPreviewMode(true);
        pump.SetScrubPreviewMode(true);
        Assert.True(pump.IsScrubPreviewMode);
        Assert.True(pump.EffectiveNearestTolerance >= TimeSpan.FromSeconds(5));

        // シャトルより優先（同時に立っても大きい方）。
        pump.SetShuttleMode(true);
        Assert.True(pump.EffectiveNearestTolerance >= TimeSpan.FromSeconds(5));
        pump.SetShuttleMode(false);

        // 呼び出し側が通常値を広げていればそれを下回らない。
        pump.NearestTolerance = TimeSpan.FromSeconds(30);
        Assert.Equal(TimeSpan.FromSeconds(30), pump.EffectiveNearestTolerance);
        pump.NearestTolerance = TimeSpan.FromMilliseconds(250);

        // 戻すと通常値。先読みや Seek 境界はプレビューで変えない。
        pump.SetScrubPreviewMode(false);
        Assert.False(pump.IsScrubPreviewMode);
        Assert.Equal(TimeSpan.FromMilliseconds(250), pump.EffectiveNearestTolerance);
        Assert.Equal(TimeSpan.FromMilliseconds(250), pump.EffectivePrefetch);

        // 開いていなくても呼べる。Close で解除される。
        pump.SetScrubPreviewMode(true);
        pump.RequestPosition(TimeSpan.FromSeconds(1));
        Assert.False(pump.TryGetFrame(TimeSpan.FromSeconds(1), out _));
        pump.Close();
        Assert.False(pump.IsScrubPreviewMode);
    }

    [Fact]
    public void AsyncPump_ScrubPreviewMissSeeksUnlessJustAhead()
    {
        var frame = TimeSpan.FromSeconds(1.0 / 30).Ticks;
        var end = TimeSpan.FromSeconds(10).Ticks;

        // 読み手のすぐ先（3 枚以内）は Seek せず読み進める。
        Assert.False(DxgiAsyncFramePump.ShouldSeekOnScrubPreviewMiss(end, hasRange: true, rangeEnd: end, endOfStream: false, frame));
        Assert.False(DxgiAsyncFramePump.ShouldSeekOnScrubPreviewMiss(end + (3 * frame), true, end, false, frame));

        // それより先・後退・未読・終端は Seek。
        Assert.True(DxgiAsyncFramePump.ShouldSeekOnScrubPreviewMiss(end + (4 * frame), true, end, false, frame));
        Assert.True(DxgiAsyncFramePump.ShouldSeekOnScrubPreviewMiss(end - 1, true, end, false, frame));
        Assert.True(DxgiAsyncFramePump.ShouldSeekOnScrubPreviewMiss(end, hasRange: false, rangeEnd: end, endOfStream: false, frame));
        Assert.True(DxgiAsyncFramePump.ShouldSeekOnScrubPreviewMiss(end + frame, true, end, endOfStream: true, frame));
    }

    [Fact]
    public void AsyncPump_ScrubPreviewServesCoarseThenExactAfterLeavingPreview()
    {
        var path = LocalCapturePath();
        if (!File.Exists(path))
        {
            return;
        }

        using var pump = new DxgiAsyncFramePump();
        Assert.True(pump.Open(path));
        var tolerance = pump.NearestTolerance;

        // ドラッグ中: 飛んだ先で何かしら（含む枚か、着地したキーフレーム）が出る。
        pump.SetScrubPreviewMode(true);
        var target = TimeSpan.FromSeconds(Math.Min(2, pump.Duration.TotalSeconds * 0.5));
        DxgiDecodedFrame? coarse = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            pump.RequestPosition(target);
            if (pump.TryGetFrame(target, out coarse) && coarse is not null)
            {
                break;
            }

            Thread.Sleep(10);
        }

        Assert.NotNull(coarse);
        Assert.True(coarse!.Pts <= target + TimeSpan.FromTicks(pump.FrameDuration.Ticks * 3));

        // 同じ位置を要求し続けても先読みしない（プレビューは Seek＋数枚で止まる）。
        var before = pump.CachedFrameCount;
        for (var i = 0; i < 5; i++)
        {
            pump.RequestPosition(target);
            Thread.Sleep(20);
        }

        Assert.True(pump.CachedFrameCount <= before + 1, $"prefetched in preview: {before} -> {pump.CachedFrameCount}");

        // 静止後（プレビュー解除）: 正確に含む枚が出る。
        pump.SetScrubPreviewMode(false);
        var exact = WaitForFrame(pump, target);
        Assert.NotNull(exact);
        Assert.True(exact!.Covers(target.Ticks) || exact.DistanceTo(target.Ticks) <= tolerance.Ticks);
        pump.Close();
    }

    // ---- プロキシの色レンジ ----

    [Fact]
    public void AsyncPump_ProxyForcesLimitedRangeButKeepsOriginalsAndMatrix()
    {
        var full = new VideoColorInfo(VideoColorPrimaries.Bt709, VideoTransferFunction.Bt709, VideoYuvMatrix.Bt601, VideoNominalRange.Full);

        // プロキシ: Range だけ Limited。行列・原色・伝達関数は読み手の値のまま。
        var proxy = DxgiAsyncFramePump.ColorInfoForSource(full, forceLimitedRange: true);
        Assert.Equal(VideoNominalRange.Limited, proxy.Range);
        Assert.False(proxy.IsFullRange);
        Assert.Equal(VideoYuvMatrix.Bt601, proxy.Matrix);
        Assert.Equal(full.Primaries, proxy.Primaries);
        Assert.Equal(full.Transfer, proxy.Transfer);

        // 原ファイル: そのまま（フルレンジも潰さない）。
        Assert.Equal(full, DxgiAsyncFramePump.ColorInfoForSource(full, forceLimitedRange: false));
        Assert.Equal(VideoColorInfo.Default, DxgiAsyncFramePump.ColorInfoForSource(VideoColorInfo.Default, forceLimitedRange: true));

        // リミテッド固定の係数は既定（16..235）と同じで、シェーダ側の計算は変わらない。
        Assert.Equal(VideoColorMath.RangeParams(VideoNominalRange.Limited), VideoColorMath.RangeParams(proxy.Range));
    }

    private static DxgiDecodedFrame? WaitForFrame(DxgiAsyncFramePump pump, TimeSpan position)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            pump.RequestPosition(position);
            if (pump.TryGetFrame(position, out var frame) && frame is not null && frame.Covers(position.Ticks))
            {
                return frame;
            }

            Thread.Sleep(10);
        }

        pump.TryGetFrame(position, out var last);
        return last;
    }

    // ---- キャッシュ ----

    [Fact]
    public void FrameCache_FindsCoveringAndNearest()
    {
        var cache = new DxgiFrameCache(capacity: 8);
        var dur = TimeSpan.FromMilliseconds(33).Ticks;
        for (var i = 0; i < 5; i++)
        {
            cache.Add(MakeFrame(i * dur, dur));
        }

        Assert.Equal(5, cache.Count);
        Assert.True(cache.TryGetCovering((2 * dur) + 5, out var covering));
        Assert.Equal(2 * dur, covering!.PtsTicks);

        // 隙間（4 枚目の終端より先）は最寄り。許容外なら無し。
        Assert.False(cache.TryGetCovering(10 * dur, out _));
        Assert.True(cache.TryGetNearest((5 * dur) + 1, dur, out var nearest));
        Assert.Equal(4 * dur, nearest!.PtsTicks);
        Assert.False(cache.TryGetNearest(100 * dur, dur, out _));

        // 同じ PTS は置き換え。
        var replacement = MakeFrame(2 * dur, dur);
        cache.Add(replacement);
        Assert.Equal(5, cache.Count);
        Assert.True(cache.TryGetCovering(2 * dur, out var replaced));
        Assert.Same(replacement, replaced);
    }

    [Fact]
    public void FrameCache_EvictsFarthestFromAnchorAndKeepsServedBuffer()
    {
        var cache = new DxgiFrameCache(capacity: 4);
        var dur = TimeSpan.FromMilliseconds(10).Ticks;
        var evicted = new List<long>();
        cache.Evicted += f => evicted.Add(f.PtsTicks);

        cache.AnchorTicks = 10 * dur;
        for (var i = 0; i < 8; i++)
        {
            cache.Add(MakeFrame(i * dur, dur));
        }

        // アンカー（100ms）に近い 4 枚（40..70ms）が残る。
        Assert.Equal(4, cache.Count);
        var (min, max) = cache.PtsRange;
        Assert.Equal(4 * dur, min);
        Assert.Equal(7 * dur, max);
        Assert.Equal(new[] { 0, dur, 2 * dur, 3 * dur }, evicted);

        // 直前に渡したフレームは追い出されても通知しない（UI が使っている可能性がある）。
        Assert.True(cache.TryGetNearest(4 * dur, 0, out var served));
        Assert.Equal(4 * dur, served!.PtsTicks);
        evicted.Clear();
        cache.Add(MakeFrame(8 * dur, dur));
        Assert.Empty(evicted);
        Assert.Equal(5 * dur, cache.PtsRange.Min);

        Assert.True(cache.WouldKeep(9 * dur));
        Assert.False(cache.WouldKeep(0));

        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    private static DxgiDecodedFrame MakeFrame(long pts, long dur) => new(pts, dur, 2, 2, new byte[16], VideoColorInfo.Default);

    // ---- 色 ----

    [Fact]
    public void ColorInfo_DefaultsToBt709Limited()
    {
        var info = VideoColorInfo.Default;
        Assert.Equal(VideoColorPrimaries.Bt709, info.Primaries);
        Assert.Equal(VideoTransferFunction.Bt709, info.Transfer);
        Assert.Equal(VideoYuvMatrix.Bt709, info.Matrix);
        Assert.Equal(VideoNominalRange.Limited, info.Range);
        Assert.False(info.IsFullRange);
        Assert.False(info.IsHdr);

        // 不明は既定へ。SD は 601。
        var unknown = VideoColorInfo.FromMediaFoundation(0, 0, 0, 0);
        Assert.Equal(VideoColorInfo.Default, unknown.Normalized());
        Assert.Equal(VideoYuvMatrix.Bt601, unknown.Normalized(480).Matrix);

        var hdr = VideoColorInfo.FromMediaFoundation(9, 15, 4, 2);
        Assert.True(hdr.IsHdr);
        Assert.True(hdr.IsWideGamut);

        // 範囲外の生値は Unknown 扱い。
        Assert.Equal(VideoColorPrimaries.Unknown, VideoColorInfo.FromMediaFoundation(99, 0, 0, 0).Primaries);
    }

    [Fact]
    public void ColorMath_MatricesAreSane()
    {
        var limited = VideoColorMath.RangeParams(VideoNominalRange.Limited);
        Assert.Equal(16f / 255f, limited.YOffset, 4);
        Assert.Equal(255f / 219f, limited.YScale, 4);
        var full = VideoColorMath.RangeParams(VideoNominalRange.Full);
        Assert.Equal(0f, full.YOffset);
        Assert.Equal(1f, full.CScale);

        // 白（Y=1, Cb=Cr=0）は白。
        var m = VideoColorMath.YuvToRgb(VideoYuvMatrix.Bt709);
        Assert.Equal(1f, m[0]);
        Assert.Equal(1f, m[3]);
        Assert.Equal(1f, m[6]);

        Assert.True(VideoColorMath.IsIdentity(VideoColorMath.PrimariesToBt709(VideoColorPrimaries.Bt709)));
        Assert.False(VideoColorMath.IsIdentity(VideoColorMath.PrimariesToBt709(VideoColorPrimaries.Bt2020)));
        Assert.True(VideoColorMath.IsIdentity(VideoColorMath.Multiply(VideoColorMath.Identity, VideoColorMath.Identity)));

        Assert.True(VideoColorMath.TryInvert(VideoColorMath.SrgbToXyzD50, out var inv));
        var identity = VideoColorMath.Multiply(inv, VideoColorMath.SrgbToXyzD50);
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(i % 4 == 0 ? 1.0 : 0.0, identity[i], 4);
        }
    }

    [Fact]
    public void MonitorProfile_ParsesMatrixTrcProfileAndRejectsGarbage()
    {
        Assert.False(MonitorColorProfile.TryParse(new byte[10], out _, out _));
        Assert.False(MonitorColorProfile.TryParse(new byte[300], out _, out _));

        // sRGB 相当の合成プロファイル（colorant は D50 順応済みの sRGB、TRC はガンマ 2.2）。
        var srgb = BuildMatrixTrcProfile(
            r: (0.4361, 0.2225, 0.0139), g: (0.3851, 0.7169, 0.0971), b: (0.1431, 0.0606, 0.7141), gamma: 2.2);
        Assert.True(MonitorColorProfile.TryParse(srgb, out var gamma, out var matrix));
        Assert.Equal(2.2f, gamma, 2);
        Assert.True(VideoColorMath.IsIdentity(matrix, 0.01f));

        // 広色域（原色を外へ広げた）プロファイルは恒等でなく、白は白のまま。
        var wide = BuildMatrixTrcProfile(
            r: (0.5151, 0.2412, -0.0011), g: (0.2920, 0.6922, 0.0419), b: (0.1571, 0.0666, 0.7841), gamma: 2.4);
        Assert.True(MonitorColorProfile.TryParse(wide, out var gamma2, out var matrix2));
        Assert.Equal(2.4f, gamma2, 2);
        Assert.False(VideoColorMath.IsIdentity(matrix2, 0.01f));
        for (var row = 0; row < 3; row++)
        {
            Assert.Equal(1f, matrix2[row * 3] + matrix2[(row * 3) + 1] + matrix2[(row * 3) + 2], 3);
        }
    }

    /// <summary>最小の行列／TRC 型 ICC（v2、mntr、RGB→XYZ）を組む。</summary>
    private static byte[] BuildMatrixTrcProfile((double, double, double) r, (double, double, double) g, (double, double, double) b, double gamma)
    {
        var tags = new List<(string Sig, byte[] Data)>
        {
            ("rXYZ", XyzTag(r)),
            ("gXYZ", XyzTag(g)),
            ("bXYZ", XyzTag(b)),
            ("rTRC", CurvTag(gamma)),
            ("gTRC", CurvTag(gamma)),
            ("bTRC", CurvTag(gamma)),
        };
        var headerAndTable = 128 + 4 + (tags.Count * 12);
        var size = headerAndTable + tags.Sum(t => t.Data.Length);
        var icc = new byte[size];
        WriteBe32(icc, 0, (uint)size);
        WriteAscii(icc, 12, "mntr");
        WriteAscii(icc, 16, "RGB ");
        WriteAscii(icc, 20, "XYZ ");
        WriteAscii(icc, 36, "acsp");
        WriteBe32(icc, 128, (uint)tags.Count);
        var offset = headerAndTable;
        for (var i = 0; i < tags.Count; i++)
        {
            var at = 132 + (i * 12);
            WriteAscii(icc, at, tags[i].Sig);
            WriteBe32(icc, at + 4, (uint)offset);
            WriteBe32(icc, at + 8, (uint)tags[i].Data.Length);
            Array.Copy(tags[i].Data, 0, icc, offset, tags[i].Data.Length);
            offset += tags[i].Data.Length;
        }

        return icc;
    }

    private static byte[] XyzTag((double X, double Y, double Z) v)
    {
        var d = new byte[20];
        WriteAscii(d, 0, "XYZ ");
        WriteBe32(d, 8, (uint)(int)Math.Round(v.X * 65536));
        WriteBe32(d, 12, (uint)(int)Math.Round(v.Y * 65536));
        WriteBe32(d, 16, (uint)(int)Math.Round(v.Z * 65536));
        return d;
    }

    private static byte[] CurvTag(double gamma)
    {
        var d = new byte[14];
        WriteAscii(d, 0, "curv");
        WriteBe32(d, 8, 1);
        var g = (ushort)Math.Round(gamma * 256);
        d[12] = (byte)(g >> 8);
        d[13] = (byte)g;
        return d;
    }

    private static void WriteBe32(byte[] buf, int at, uint value)
    {
        buf[at] = (byte)(value >> 24);
        buf[at + 1] = (byte)(value >> 16);
        buf[at + 2] = (byte)(value >> 8);
        buf[at + 3] = (byte)value;
    }

    private static void WriteAscii(byte[] buf, int at, string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            buf[at + i] = (byte)s[i];
        }
    }

    // ---- presenter の追加経路 ----

    [Fact]
    public void Presenter_Nv12AndColorManagementDoNotBreakComposition()
    {
        RunSta(() =>
        {
            using var presenter = new D3D11ImagePresenter();
            presenter.AttachForComposition(320, 240);
            Assert.True(presenter.IsReady);
            Assert.True(presenter.HasShaderPipeline);

            // 既定（709／ICC が sRGB 同等）なら変換は切れている。HDR ソースなら入る。
            presenter.ApplyColorManagement(VideoColorInfo.Default, IntPtr.Zero);
            Assert.Equal(VideoColorInfo.Default, presenter.SourceColorInfo);

            // NV12 の CPU 平面: 灰色 1 色（Y=128, U=V=128）。リミテッドレンジ 709 なら (128-16)*255/219 ≒ 130 の無彩色。
            // モニタ ICC の影響を受けないよう、ここでは切る。
            presenter.UseMonitorProfile = false;
            presenter.ApplyColorManagement(VideoColorInfo.Default, IntPtr.Zero);
            Assert.False(presenter.IsColorTransformActive);
            const int w = 64, h = 48;
            var y = new byte[w * h];
            var uv = new byte[(w / 2) * 2 * (h / 2)];
            Array.Fill(y, (byte)128);
            Array.Fill(uv, (byte)128);
            presenter.PresentNv12Frame(y, w, uv, w, w, h);
            Assert.True(presenter.TryReadBackPixel(160, 120, out var gray));
            var (gb, gg, gr) = ((int)(gray & 0xFF), (int)((gray >> 8) & 0xFF), (int)((gray >> 16) & 0xFF));
            Assert.InRange(gr, 127, 133);
            Assert.InRange(gg, 127, 133);
            Assert.InRange(gb, 127, 133);
            Assert.Equal(0xFFu, gray >> 24);

            // フルレンジ指定なら Y=128 はそのまま 128。
            presenter.ApplyColorManagement(VideoColorInfo.Default with { Range = VideoNominalRange.Full }, IntPtr.Zero);
            presenter.PresentNv12Frame(y, w, uv, w, w, h);
            Assert.True(presenter.TryReadBackPixel(160, 120, out var full));
            Assert.InRange((int)((full >> 8) & 0xFF), 126, 130);

            // 黒帯は透明（光漏れが背面から透ける）。100x100 → 320x240 で左右に帯。
            presenter.PresentBgraFrame(DxgiPrototypeFrameSource.GenerateTestPattern(TimeSpan.Zero, 100, 100), 100, 100, 400);
            Assert.True(presenter.TryReadBackPixel(2, 120, out var bar));
            Assert.Equal(0u, bar);

            // HDR（BT.2020／PQ）ソースなら色変換が入る。暗い PQ 値は暗いまま。
            var hdr = VideoColorInfo.FromMediaFoundation(9, 15, 4, 2);
            presenter.ApplyColorManagement(hdr, IntPtr.Zero);
            Assert.True(presenter.IsColorTransformActive);
            Array.Fill(y, (byte)40);
            presenter.PresentNv12Frame(y, w, uv, w, w, h);
            Assert.True(presenter.TryReadBackPixel(160, 120, out var dark));
            Assert.InRange((int)((dark >> 8) & 0xFF), 0, 60);

            presenter.ApplyColorManagement(VideoColorInfo.Default, IntPtr.Zero);
            var frame = DxgiPrototypeFrameSource.GenerateTestPattern(TimeSpan.FromSeconds(0.5), 640, 360);
            presenter.PresentBgraFrame(frame, 640, 360, 640 * 4);
            Assert.False(presenter.IsColorTransformActive);

            // モニタプロファイルを有効にしても描ける（内容は環境依存なので値は見ない）。
            presenter.UseMonitorProfile = true;
            presenter.ApplyColorManagement(VideoColorInfo.Default, IntPtr.Zero);
            presenter.PresentBgraFrame(frame, 640, 360, 640 * 4);

            // 無効ハンドル・サイズ違いの引数は何もしない。
            presenter.PresentTexture(IntPtr.Zero);
            presenter.PresentNv12Frame(y, w, uv, w, w * 4, h * 4);
            Assert.True(presenter.IsReady);
        });
    }

    [Fact]
    public unsafe void Presenter_PresentTexture_CrossDeviceSharedStagingAndNv12()
    {
        RunSta(() =>
        {
            using var presenter = new D3D11ImagePresenter();
            presenter.AttachForComposition(64, 64);
            Assert.True(presenter.IsReady && presenter.HasShaderPipeline);
            presenter.UseMonitorProfile = false;
            presenter.ApplyColorManagement(VideoColorInfo.Default, IntPtr.Zero);

            // 別の D3D11 デバイス（同じアダプタ）。
            var hr = D3D11CreateDevice(
                IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero, 0, D3D11_SDK_VERSION, out var dev2, out _, out var ctx2);
            Assert.True(hr >= 0);
            try
            {
                // (1) MISC_SHARED の BGRA → 共有ハンドルで開いてシェーダ描画。
                var red = SolidBgra(64, 64, 0xFFFF0000u);
                var shared = CreateTexture(dev2, 64, 64, DXGI_FORMAT_B8G8R8A8_UNORM, D3D11_BIND_SHADER_RESOURCE, D3D11_RESOURCE_MISC_SHARED);
                fixed (byte* p = red)
                {
                    CtxUpdateSubresource(ctx2, shared, p, 64 * 4);
                }

                // 旧式共有ハンドルには同期が無いので、持ち主側の GPU 完了を待ってから渡す（本番は KEYEDMUTEX 推奨）。
                WaitGpu(dev2, ctx2, shared, 64, 64, DXGI_FORMAT_B8G8R8A8_UNORM);
                presenter.PresentTexture(shared);
                Assert.True(presenter.TryReadBackPixel(32, 32, out var px));
                Assert.Equal(0xFFFF0000u, px);
                Release(ref shared);

                // (2) 共有なし・バインドなし → 持ち主側ステージング経由。
                var green = SolidBgra(32, 32, 0xFF00FF00u);
                var plain = CreateTexture(dev2, 32, 32, DXGI_FORMAT_B8G8R8A8_UNORM, 0, 0);
                fixed (byte* p = green)
                {
                    CtxUpdateSubresource(ctx2, plain, p, 32 * 4);
                }

                CtxFlush(ctx2);
                presenter.PresentTexture(plain);
                Assert.True(presenter.TryReadBackPixel(32, 32, out px));
                Assert.Equal(0xFF00FF00u, px);
                Release(ref plain);

                // (3) 別デバイスの NV12（Y=128, UV=128）→ ステージング → 2 平面アップロード → YUV シェーダ。
                var nv12 = CreateTexture(dev2, 64, 64, DXGI_FORMAT_NV12, 0, 0);
                if (nv12 != IntPtr.Zero)
                {
                    var planes = new byte[64 * 64 + (64 * 32)];
                    Array.Fill(planes, (byte)128);
                    fixed (byte* p = planes)
                    {
                        CtxUpdateSubresource(ctx2, nv12, p, 64);
                    }

                    CtxFlush(ctx2);
                    presenter.PresentTexture(nv12);
                    Assert.True(presenter.TryReadBackPixel(32, 32, out px));
                    Assert.InRange((int)((px >> 8) & 0xFF), 127, 133);
                    Assert.InRange((int)(px & 0xFF), 127, 133);
                    Release(ref nv12);
                }

                // (4) 非対応形式（R16G16_UNORM）は透明クリア（帯と同じ扱い）。
                var odd = CreateTexture(dev2, 16, 16, DXGI_FORMAT_R16G16_UNORM, 0, 0);
                presenter.PresentTexture(odd);
                Assert.True(presenter.TryReadBackPixel(32, 32, out px));
                Assert.Equal(0u, px);
                Release(ref odd);
            }
            finally
            {
                Release(ref ctx2);
                Release(ref dev2);
            }
        });
    }

    /// <summary>ステージングへコピーして Map することで、そのデバイスの GPU 処理完了を待つ。</summary>
    private static void WaitGpu(IntPtr device, IntPtr ctx, IntPtr tex, int w, int h, int format)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE_STAGING,
            CPUAccessFlags = D3D11_CPU_ACCESS_READ,
        };
        if (DeviceCreateTexture2D(device, desc, out var staging) < 0)
        {
            return;
        }

        CtxCopyResource(ctx, staging, tex);
        if (CtxMap(ctx, staging, D3D11_MAP_READ, out _) >= 0)
        {
            CtxUnmap(ctx, staging);
        }

        Release(ref staging);
    }

    private static IntPtr CreateTexture(IntPtr device, int w, int h, int format, uint bind, uint misc)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE_DEFAULT,
            BindFlags = bind,
            MiscFlags = misc,
        };
        return DeviceCreateTexture2D(device, desc, out var tex) >= 0 ? tex : IntPtr.Zero;
    }

    private static byte[] SolidBgra(int w, int h, uint argb)
    {
        var data = new byte[w * h * 4];
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = (byte)argb;
            data[i + 1] = (byte)(argb >> 8);
            data[i + 2] = (byte)(argb >> 16);
            data[i + 3] = (byte)(argb >> 24);
        }

        return data;
    }

    private static string LocalCapturePath() => Path.Combine(TestDataDir(), "reference_signals", "captures", "digicheck_sweeps_2026-10-17.mp4");

    private static string TestDataDir([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
