using System.Runtime.InteropServices;

namespace MgaSonicAnvil.Video.Dxgi;

/// <summary><see cref="DxgiPrototypeFrameSource.ReadNext"/> の結果。</summary>
internal enum DxgiReadResult
{
    /// <summary>1 枚読めた（dest が null なら PTS だけ）。</summary>
    Frame,

    /// <summary>終端。Seek するまで次は無い。</summary>
    EndOfStream,

    /// <summary>フォーマットが変わり dest が足りない。この 1 枚は捨てた。Width/Height を見て取り直す。</summary>
    NeedLargerBuffer,

    /// <summary>読めなかった（リーダー未オープン／エラー）。</summary>
    Failed,
}

/// <summary>
/// Media Foundation の同期 IMFSourceReader で 1 枚ずつ読む低レベル読み手。
/// 出力は NV12 を優先（デコーダ出力そのまま＝コピーだけ、YUV→RGB は表示側シェーダ）。NV12 が通らないときだけ
/// RGB32（リーダー内ソフト変換、重い）へフォールバックする。<see cref="PixelFormat"/> で確かめる。
/// 通常は <see cref="DxgiAsyncFramePump"/> がワーカースレッド上で使い、UI から直接呼ぶのはテスト用の
/// <see cref="TryReadFrameAt"/> だけ。単一スレッド前提（同時に複数スレッドから触らない）。
/// 開けないときは <see cref="GenerateTestPattern"/> で presenter だけ確かめられる。
/// <para>
/// 次の段階（未実装）: IMFDXGIDeviceManager を渡して DXVA（D3D11 テクスチャ出力）にすれば CPU コピーも消えるが、
/// デバイス共有・同期が重いのでこのパスでは扱わない。
/// </para>
/// </summary>
internal sealed unsafe class DxgiPrototypeFrameSource : IDisposable
{
    /// <summary>これより先へ飛ぶならシーク。手前なら読み進める。</summary>
    private static readonly long ForwardReadLimitTicks = TimeSpan.FromSeconds(1.5).Ticks;

    /// <summary>1 回の呼び出しで捨て読みする上限（UI を止めすぎない）。</summary>
    private const int MaxSamplesPerCall = 45;

    private static readonly long DefaultFrameTicks = TimeSpan.FromSeconds(1.0 / 30).Ticks;

    private IntPtr _reader;
    private bool _mfStarted;
    private DxgiFramePixelFormat _pixelFormat = DxgiFramePixelFormat.Bgra32;

    /// <summary>表示寸法（アパーチャ適用後）。</summary>
    private int _width;
    private int _height;

    /// <summary>MF_MT_FRAME_SIZE のサーフェス寸法（NV12 の H.264 デコーダ出力は 1088 行など、表示寸法より大きいことがある）。</summary>
    private int _surfaceWidth;
    private int _surfaceHeight;

    /// <summary>表示領域のサーフェス内オフセット（MF_MT_MINIMUM_DISPLAY_APERTURE）。普通は 0。</summary>
    private int _apertureX;
    private int _apertureY;
    private int _sourceStride;
    private byte[]? _frame;
    private bool _hasFrame;
    private long _frameTime;
    private long _frameDuration;
    private bool _endOfStream;
    private bool _catchingUp;

    public bool IsOpen => _reader != IntPtr.Zero;

    public TimeSpan Duration { get; private set; }

    public int Width => _width;

    public int Height => _height;

    /// <summary>リーダー出力の画素形式。開いた直後に決まり、途中のフォーマット変更でも変わらない（サブタイプは固定）。</summary>
    public DxgiFramePixelFormat PixelFormat => _pixelFormat;

    /// <summary><see cref="ReadNext"/> が dest に書くレイアウトの stride（BGRA: Width*4、NV12: Y 平面 = Width）。</summary>
    public int Stride => DxgiDecodedFrame.StrideFor(_pixelFormat, _width);

    /// <summary>dest に要るバイト数（<see cref="DxgiDecodedFrame.RequiredBytes"/>）。未オープンなら 0。</summary>
    public int FrameBytes => DxgiDecodedFrame.RequiredBytes(_pixelFormat, _width, _height);

    /// <summary>ネイティブのメディアタイプから読んだ色情報（RGB32 変換前のもの）。読めなければ既定。</summary>
    public VideoColorInfo ColorInfo { get; private set; } = VideoColorInfo.Default;

    /// <summary>MF_MT_FRAME_RATE から。無ければ 1/30 秒。</summary>
    public TimeSpan FrameDuration { get; private set; } = TimeSpan.FromTicks(DefaultFrameTicks);

    /// <summary>直近の ReadNext が終端に達した。</summary>
    public bool EndOfStream => _endOfStream;

    /// <summary>
    /// position へシーク。MF は手前のキーフレームへ着地し、以降の ReadNext はそこから順に返す
    /// （target 手前のフレームも返るので、呼び出し側が捨てるかキャッシュに入れる）。
    /// </summary>
    public void Seek(TimeSpan position)
    {
        if (_reader == IntPtr.Zero)
        {
            return;
        }

        Seek(Math.Max(0, position.Ticks));
    }

    /// <summary>
    /// 次のサンプルを 1 枚読む。dest に <see cref="PixelFormat"/> のレイアウト（<see cref="DxgiDecodedFrame"/> と同じ、
    /// 長さ <see cref="FrameBytes"/> 以上）を書く。dest が null ならコピーせず PTS だけ返す
    /// （キャッシュに残らない捨て読みのコスト削減用。NV12 ならデコードだけで RGB 変換もコピーも無い）。
    /// ストリームティック等の空サンプルは内部で読み飛ばす。
    /// </summary>
    public DxgiReadResult ReadNext(byte[]? dest, out long ptsTicks, out long durationTicks)
    {
        ptsTicks = 0;
        durationTicks = 0;
        if (_reader == IntPtr.Zero || _width <= 0 || _height <= 0)
        {
            return DxgiReadResult.Failed;
        }

        if (_endOfStream)
        {
            return DxgiReadResult.EndOfStream;
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var hr = Mf.ReaderReadSample(_reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out var flags, out var timestamp, out var sample);
            if (hr < 0)
            {
                Mf.Release(ref sample);
                return DxgiReadResult.Failed;
            }

            try
            {
                if ((flags & Mf.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
                {
                    RefreshFormat();
                }

                if ((flags & Mf.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    _endOfStream = true;
                    return DxgiReadResult.EndOfStream;
                }

                if (sample == IntPtr.Zero)
                {
                    continue;
                }

                ptsTicks = timestamp;
                durationTicks = Mf.SampleGetDuration(sample, out var d) >= 0 && d > 0 ? d : FrameDuration.Ticks;
                if (dest is null)
                {
                    return DxgiReadResult.Frame;
                }

                if (dest.Length < FrameBytes)
                {
                    return DxgiReadResult.NeedLargerBuffer;
                }

                return CopySample(sample, dest) ? DxgiReadResult.Frame : DxgiReadResult.Failed;
            }
            finally
            {
                Mf.Release(ref sample);
            }
        }

        return DxgiReadResult.Failed;
    }

    public bool TryOpen(string path, out TimeSpan duration, out int width, out int height)
    {
        Close();
        duration = TimeSpan.Zero;
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            if (Mf.MFStartup(Mf.MF_VERSION, 0) < 0)
            {
                return false;
            }

            _mfStarted = true;
            if (!OpenReader(path))
            {
                Close();
                return false;
            }

            Duration = ReadDuration();
            duration = Duration;
            width = _width;
            height = _height;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            // N 版 Windows など MF が無い環境。
            Close();
            return false;
        }
    }

    /// <summary>
    /// テスト用の同期読み。pixels は <see cref="PixelFormat"/> のレイアウト（stride は BGRA なら Width*4、NV12 なら Y の Width）。
    /// </summary>
    public bool TryReadFrameAt(TimeSpan position, out byte[]? pixels, out int width, out int height, out int stride)
    {
        width = _width;
        height = _height;
        stride = Stride;
        pixels = null;
        if (_reader == IntPtr.Zero || _width <= 0 || _height <= 0)
        {
            return false;
        }

        var target = Math.Max(0, position.Ticks);
        if (_hasFrame && target >= _frameTime && target < _frameTime + Math.Max(1, _frameDuration))
        {
            pixels = _frame;
            return true;
        }

        var behind = _hasFrame && target < _frameTime;
        var farAhead = _hasFrame && !_catchingUp && target - _frameTime > ForwardReadLimitTicks;
        if ((!_hasFrame && !_catchingUp) || behind || farAhead)
        {
            Seek(target);
        }
        else if (_endOfStream)
        {
            pixels = _hasFrame ? _frame : null;
            return pixels is not null;
        }

        ReadForward(target);
        pixels = _hasFrame ? _frame : null;
        width = _width;
        height = _height;
        stride = Stride;
        return pixels is not null;
    }

    public void Close()
    {
        Mf.Release(ref _reader);
        if (_mfStarted)
        {
            _mfStarted = false;
            try
            {
                Mf.MFShutdown();
            }
            catch
            {
            }
        }

        _pixelFormat = DxgiFramePixelFormat.Bgra32;
        _width = 0;
        _height = 0;
        _surfaceWidth = 0;
        _surfaceHeight = 0;
        _apertureX = 0;
        _apertureY = 0;
        _sourceStride = 0;
        _frame = null;
        _hasFrame = false;
        _frameTime = 0;
        _frameDuration = 0;
        _endOfStream = false;
        _catchingUp = false;
        Duration = TimeSpan.Zero;
        ColorInfo = VideoColorInfo.Default;
        FrameDuration = TimeSpan.FromTicks(DefaultFrameTicks);
    }

    public void Dispose() => Close();

    /// <summary>
    /// MF が使えないときのデモ用。横に流れる白帯＋経過時間（mm:ss.ff）を BGRA（stride = w*4）で返す。
    /// </summary>
    public static byte[] GenerateTestPattern(TimeSpan t, int w, int h)
    {
        w = Math.Max(1, w);
        h = Math.Max(1, h);
        var buffer = new byte[w * h * 4];
        FillTestPattern(buffer, t, w, h);
        return buffer;
    }

    /// <summary><see cref="GenerateTestPattern"/> の再利用版。buffer は w*h*4 以上。</summary>
    public static void FillTestPattern(Span<byte> buffer, TimeSpan t, int w, int h)
    {
        if (w <= 0 || h <= 0 || buffer.Length < w * h * 4)
        {
            return;
        }

        var seconds = Math.Max(0, t.TotalSeconds);
        var barWidth = Math.Max(1, w / 16);
        var barX = (int)((seconds % 2.0) / 2.0 * (w + barWidth)) - barWidth;
        fixed (byte* p = buffer)
        {
            var px = (uint*)p;
            for (var y = 0; y < h; y++)
            {
                // 縦グラデの紺。
                var shade = (uint)(24 + (40 * y / h));
                var bg = 0xFF000000u | (shade << 0) | ((shade / 2) << 8) | ((shade / 4) << 16);
                var row = px + ((long)y * w);
                for (var x = 0; x < w; x++)
                {
                    row[x] = x >= barX && x < barX + barWidth ? 0xFFFFFFFFu : bg;
                }
            }

            // 秒の進み（下端）。
            var tickH = Math.Max(1, h / 40);
            var tickW = (int)((seconds % 1.0) * w);
            for (var y = h - tickH; y < h; y++)
            {
                var row = px + ((long)y * w);
                for (var x = 0; x < tickW; x++)
                {
                    row[x] = 0xFF30C0FFu;
                }
            }

            var text = $"{(int)(seconds / 60):00}:{(int)(seconds % 60):00}.{(int)(seconds * 100 % 100):00}";
            var scale = Math.Max(2, h / 40);
            DrawText(px, w, h, text, scale, scale * 2, scale * 2, 0xFFFFFFFFu);
        }
    }

    private bool OpenReader(string path)
    {
        var attrs = IntPtr.Zero;
        try
        {
            if (Mf.MFCreateAttributes(out attrs, 1) < 0)
            {
                return false;
            }

            // 変換（YUY2→NV12 や、フォールバックの →RGB32）をソースリーダー内で行わせる。
            Mf.AttrSetUInt32(attrs, Mf.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            if (Mf.MFCreateSourceReaderFromURL(path, attrs, out _reader) < 0 || _reader == IntPtr.Zero)
            {
                _reader = IntPtr.Zero;
                return false;
            }

            Mf.ReaderSetStreamSelection(_reader, Mf.MF_SOURCE_READER_ALL_STREAMS, false);
            if (Mf.ReaderSetStreamSelection(_reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, true) < 0)
            {
                return false;
            }

            // 出力形式を決める前に、元の色情報とフレームレートを控える。
            ReadNativeColorInfo();

            // NV12 優先（デコーダ出力そのまま。YUV→RGB は表示側の GPU）。駄目なら RGB32（リーダー内ソフト変換）。
            if (TrySetOutputSubtype(Mf.MFVideoFormat_NV12))
            {
                _pixelFormat = DxgiFramePixelFormat.Nv12;
            }
            else if (TrySetOutputSubtype(Mf.MFVideoFormat_RGB32))
            {
                _pixelFormat = DxgiFramePixelFormat.Bgra32;
            }
            else
            {
                return false;
            }

            return RefreshFormat();
        }
        finally
        {
            Mf.Release(ref attrs);
        }
    }

    private bool TrySetOutputSubtype(Guid subtype)
    {
        if (Mf.MFCreateMediaType(out var type) < 0)
        {
            return false;
        }

        try
        {
            Mf.AttrSetGuid(type, Mf.MF_MT_MAJOR_TYPE, Mf.MFMediaType_Video);
            Mf.AttrSetGuid(type, Mf.MF_MT_SUBTYPE, subtype);
            if (Mf.ReaderSetCurrentMediaType(_reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, type) < 0)
            {
                return false;
            }

            // 設定が通っても実際の現在タイプで確かめる（別のサブタイプへ丸められることがある）。
            if (Mf.ReaderGetCurrentMediaType(_reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out var current) < 0)
            {
                return false;
            }

            try
            {
                return Mf.AttrGetGuid(current, Mf.MF_MT_SUBTYPE, out var actual) >= 0 && actual == subtype;
            }
            finally
            {
                Mf.Release(ref current);
            }
        }
        finally
        {
            Mf.Release(ref type);
        }
    }

    /// <summary>ネイティブ（デコーダ出力）メディアタイプの色属性。無い属性は Unknown のまま（Normalized で既定化）。</summary>
    private void ReadNativeColorInfo()
    {
        if (Mf.ReaderGetNativeMediaType(_reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, out var type) < 0 || type == IntPtr.Zero)
        {
            return;
        }

        try
        {
            uint primaries = 0, transfer = 0, matrix = 0, range = 0;
            Mf.AttrGetUInt32(type, Mf.MF_MT_VIDEO_PRIMARIES, out primaries);
            Mf.AttrGetUInt32(type, Mf.MF_MT_TRANSFER_FUNCTION, out transfer);
            Mf.AttrGetUInt32(type, Mf.MF_MT_YUV_MATRIX, out matrix);
            Mf.AttrGetUInt32(type, Mf.MF_MT_VIDEO_NOMINAL_RANGE, out range);
            var height = 0;
            if (Mf.AttrGetUInt64(type, Mf.MF_MT_FRAME_SIZE, out var size) >= 0)
            {
                height = (int)(size & 0xFFFFFFFF);
            }

            ColorInfo = VideoColorInfo.FromMediaFoundation(primaries, transfer, matrix, range).Normalized(height);

            if (Mf.AttrGetUInt64(type, Mf.MF_MT_FRAME_RATE, out var rate) >= 0)
            {
                var num = (uint)(rate >> 32);
                var den = (uint)(rate & 0xFFFFFFFF);
                if (num > 0 && den > 0)
                {
                    var ticks = (long)(TimeSpan.TicksPerSecond * (double)den / num);
                    if (ticks > 0)
                    {
                        FrameDuration = TimeSpan.FromTicks(ticks);
                    }
                }
            }
        }
        finally
        {
            Mf.Release(ref type);
        }
    }

    private bool RefreshFormat()
    {
        if (Mf.ReaderGetCurrentMediaType(_reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out var type) < 0)
        {
            return false;
        }

        try
        {
            if (Mf.AttrGetUInt64(type, Mf.MF_MT_FRAME_SIZE, out var size) < 0)
            {
                return false;
            }

            // サブタイプは Open で固定したものと同じはずだが、念のため現在タイプから読み直す。
            if (Mf.AttrGetGuid(type, Mf.MF_MT_SUBTYPE, out var subtype) >= 0)
            {
                if (subtype == Mf.MFVideoFormat_NV12)
                {
                    _pixelFormat = DxgiFramePixelFormat.Nv12;
                }
                else if (subtype == Mf.MFVideoFormat_RGB32)
                {
                    _pixelFormat = DxgiFramePixelFormat.Bgra32;
                }
            }

            _surfaceWidth = (int)(size >> 32);
            _surfaceHeight = (int)(size & 0xFFFFFFFF);
            if (_surfaceWidth <= 0 || _surfaceHeight <= 0)
            {
                return false;
            }

            // 表示領域（NV12 のデコーダ出力は 1920x1088 のように下に余白を持つことがある）。無ければサーフェス全体。
            _width = _surfaceWidth;
            _height = _surfaceHeight;
            _apertureX = 0;
            _apertureY = 0;
            if (TryReadAperture(type, out var ax, out var ay, out var aw, out var ah)
                && aw > 0 && ah > 0 && ax >= 0 && ay >= 0 && ax + aw <= _surfaceWidth && ay + ah <= _surfaceHeight)
            {
                _apertureX = ax;
                _apertureY = ay;
                _width = aw;
                _height = ah;
            }

            var bytesPerPixel = _pixelFormat == DxgiFramePixelFormat.Nv12 ? 1 : 4;
            _sourceStride = Mf.AttrGetUInt32(type, Mf.MF_MT_DEFAULT_STRIDE, out var s) >= 0 && s != 0
                ? unchecked((int)s)
                : _surfaceWidth * bytesPerPixel;

            var bytes = FrameBytes;
            if (_frame is null || _frame.Length != bytes)
            {
                _frame = new byte[bytes];
                _hasFrame = false;
            }

            return true;
        }
        finally
        {
            Mf.Release(ref type);
        }
    }

    /// <summary>MF_MT_MINIMUM_DISPLAY_APERTURE（無ければ GEOMETRIC）の MFVideoArea。</summary>
    private static bool TryReadAperture(IntPtr type, out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        var blob = stackalloc byte[16];
        if (Mf.AttrGetBlob(type, Mf.MF_MT_MINIMUM_DISPLAY_APERTURE, blob, 16, out var got) < 0 || got != 16)
        {
            if (Mf.AttrGetBlob(type, Mf.MF_MT_GEOMETRIC_APERTURE, blob, 16, out got) < 0 || got != 16)
            {
                return false;
            }
        }

        // MFVideoArea: MFOffset{WORD fract; short value;} OffsetX, OffsetY; SIZE{LONG cx, cy} Area。小数部は捨てる。
        x = *(short*)(blob + 2);
        y = *(short*)(blob + 6);
        width = *(int*)(blob + 8);
        height = *(int*)(blob + 12);
        return true;
    }

    private TimeSpan ReadDuration()
    {
        var pv = default(Mf.PROPVARIANT);
        if (Mf.ReaderGetPresentationAttribute(_reader, Mf.MF_SOURCE_READER_MEDIASOURCE, Mf.MF_PD_DURATION, ref pv) < 0)
        {
            return TimeSpan.Zero;
        }

        var ticks = pv.vt == Mf.VT_UI8 ? pv.value : 0;
        Mf.PropVariantClear(ref pv);
        return ticks > 0 ? TimeSpan.FromTicks(ticks) : TimeSpan.Zero;
    }

    private void Seek(long target)
    {
        var pv = new Mf.PROPVARIANT { vt = Mf.VT_I8, value = target };
        Mf.ReaderSetCurrentPosition(_reader, pv);
        _hasFrame = false;
        _endOfStream = false;
        _catchingUp = true;
    }

    /// <summary>target を含むサンプルまで捨て読み。上限に達したらその時点の 1 枚を出し、次回続きから読む。</summary>
    private void ReadForward(long target)
    {
        for (var i = 0; i < MaxSamplesPerCall; i++)
        {
            var hr = Mf.ReaderReadSample(_reader, Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out var flags, out var timestamp, out var sample);
            if (hr < 0)
            {
                Mf.Release(ref sample);
                _catchingUp = false;
                return;
            }

            try
            {
                if ((flags & Mf.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
                {
                    RefreshFormat();
                }

                if ((flags & Mf.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    _endOfStream = true;
                    _catchingUp = false;
                    return;
                }

                if (sample == IntPtr.Zero)
                {
                    continue;
                }

                var duration = Mf.SampleGetDuration(sample, out var d) >= 0 && d > 0 ? d : FrameDuration.Ticks;
                var reached = timestamp + duration > target;
                if (!reached && i < MaxSamplesPerCall - 1)
                {
                    continue;
                }

                if (_frame is not null && CopySample(sample, _frame))
                {
                    _hasFrame = true;
                    _frameTime = timestamp;
                    _frameDuration = duration;
                }

                _catchingUp = !reached;
                return;
            }
            finally
            {
                Mf.Release(ref sample);
            }
        }
    }

    /// <summary>サンプルを dest（<see cref="DxgiDecodedFrame"/> のレイアウト）へコピー。アパーチャ分だけ切り出す。</summary>
    private bool CopySample(IntPtr sample, byte[] dest)
    {
        if (dest.Length < FrameBytes || Mf.SampleConvertToContiguousBuffer(sample, out var buffer) < 0)
        {
            return false;
        }

        try
        {
            if (Mf.BufferLock(buffer, out var data, out var length) < 0)
            {
                return false;
            }

            try
            {
                fixed (byte* dst = dest)
                {
                    return _pixelFormat == DxgiFramePixelFormat.Nv12
                        ? CopyNv12(data, length, dst)
                        : CopyBgra(data, length, dst);
                }
            }
            finally
            {
                Mf.BufferUnlock(buffer);
            }
        }
        finally
        {
            Mf.Release(ref buffer);
        }
    }

    private bool CopyBgra(byte* data, uint length, byte* dst)
    {
        var rowBytes = _width * 4;
        var pitch = Math.Max(Math.Abs(_sourceStride), _surfaceWidth * 4);
        var bottomUp = _sourceStride < 0;

        // バッファに入っている行数（サーフェス基準）。
        var surfaceRows = (int)Math.Min(_surfaceHeight, (length + pitch - (_surfaceWidth * 4)) / pitch);
        var copied = 0;
        for (var y = 0; y < _height; y++)
        {
            var surfaceRow = _apertureY + y;
            if (bottomUp)
            {
                surfaceRow = _surfaceHeight - 1 - surfaceRow;
            }

            if (surfaceRow < 0 || surfaceRow >= surfaceRows)
            {
                continue;
            }

            var src = data + ((long)surfaceRow * pitch) + ((long)_apertureX * 4);
            Buffer.MemoryCopy(src, dst + ((long)y * rowBytes), rowBytes, rowBytes);
            copied++;
        }

        return copied > 0;
    }

    /// <summary>
    /// 連続化した NV12 は Y 平面（pitch × サーフェス高）の直後に UV 平面。UV の先頭はバッファ長から逆算する
    /// （サーフェス高がメディアタイプと違う＝余白行付きの出力でもずれないように）。奇数行アパーチャは 1 行切り上げ。
    /// </summary>
    private bool CopyNv12(byte* data, uint length, byte* dst)
    {
        var pitch = Math.Max(Math.Abs(_sourceStride), _surfaceWidth);
        if (pitch <= 0)
        {
            return false;
        }

        // length == pitch * rows * 3 / 2 なら rows が実際の Y 行数。割り切れなければメディアタイプの高さを信じる。
        var yRows = _surfaceHeight;
        var inferred = length * 2L / (3L * pitch);
        if (inferred > 0 && inferred * 3L * pitch == length * 2L)
        {
            yRows = (int)inferred;
        }

        var uvBase = (long)pitch * yRows;
        if (uvBase > length)
        {
            return false;
        }

        var uvRowsAvailable = (length - uvBase) / pitch;

        // Y 平面。
        var yStride = _width;
        var copied = 0;
        for (var y = 0; y < _height; y++)
        {
            var surfaceRow = _apertureY + y;
            if (surfaceRow >= yRows)
            {
                break;
            }

            Buffer.MemoryCopy(data + ((long)surfaceRow * pitch) + _apertureX, dst + ((long)y * yStride), yStride, yStride);
            copied++;
        }

        // UV 平面（半分の高さ、2 成分交互）。
        var uvStride = DxgiDecodedFrame.UvStrideFor(_width);
        var uvRows = (_height + 1) / 2;
        var uvDst = dst + ((long)yStride * _height);
        var uvX = (_apertureX / 2) * 2;
        var uvRowBytes = Math.Min(uvStride, pitch - uvX);
        for (var r = 0; r < uvRows && uvRowBytes > 0; r++)
        {
            var surfaceRow = (_apertureY / 2) + r;
            if (surfaceRow >= uvRowsAvailable)
            {
                break;
            }

            Buffer.MemoryCopy(data + uvBase + ((long)surfaceRow * pitch) + uvX, uvDst + ((long)r * uvStride), uvStride, uvRowBytes);
        }

        return copied > 0;
    }

    private static readonly string[] Glyphs =
    [
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111",
    ];

    private const string ColonGlyph = "000010000010000";
    private const string DotGlyph = "000000000000010";

    private static void DrawText(uint* px, int w, int h, string text, int scale, int x0, int y0, uint color)
    {
        var x = x0;
        foreach (var c in text)
        {
            var glyph = c switch
            {
                >= '0' and <= '9' => Glyphs[c - '0'],
                ':' => ColonGlyph,
                '.' => DotGlyph,
                _ => null,
            };
            if (glyph is not null)
            {
                for (var gy = 0; gy < 5; gy++)
                {
                    for (var gx = 0; gx < 3; gx++)
                    {
                        if (glyph[(gy * 3) + gx] != '1')
                        {
                            continue;
                        }

                        for (var sy = 0; sy < scale; sy++)
                        {
                            var py = y0 + (gy * scale) + sy;
                            if (py < 0 || py >= h)
                            {
                                continue;
                            }

                            for (var sx = 0; sx < scale; sx++)
                            {
                                var pxX = x + (gx * scale) + sx;
                                if (pxX >= 0 && pxX < w)
                                {
                                    px[((long)py * w) + pxX] = color;
                                }
                            }
                        }
                    }
                }
            }

            x += 4 * scale;
        }
    }

    /// <summary>Media Foundation の最小 P/Invoke（vtable 直呼び）。</summary>
    private static class Mf
    {
        public const uint MF_VERSION = 0x00020070;
        public const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
        public const uint MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFE;
        public const uint MF_SOURCE_READER_MEDIASOURCE = 0xFFFFFFFF;
        public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x2;
        public const uint MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED = 0x20;
        public const ushort VT_I8 = 20;
        public const ushort VT_UI8 = 21;

        public static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
        public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid MF_PD_DURATION = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
        public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid MF_MT_VIDEO_PRIMARIES = new("dbfbe4d7-0740-4ee0-8192-850ab0e21935");
        public static readonly Guid MF_MT_TRANSFER_FUNCTION = new("5fb0fce6-be3c-4b7a-a08f-a27b4a000e7b");
        public static readonly Guid MF_MT_YUV_MATRIX = new("3e23d450-2c75-4d25-a00e-b91670d12327");
        public static readonly Guid MF_MT_VIDEO_NOMINAL_RANGE = new("c21b8ee5-b956-4071-8daf-325edf5cab11");
        public static readonly Guid MF_MT_MINIMUM_DISPLAY_APERTURE = new("d7388766-18fe-48c6-a177-ee894867c8c4");
        public static readonly Guid MF_MT_GEOMETRIC_APERTURE = new("66758743-7e5f-400d-980a-aa8596c85696");
        public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
        public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");

        /// <summary>FourCC 'NV12'。</summary>
        public static readonly Guid MFVideoFormat_NV12 = new("3231564e-0000-0010-8000-00aa00389b71");

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct PROPVARIANT
        {
            [FieldOffset(0)]
            public ushort vt;

            [FieldOffset(8)]
            public long value;
        }

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFStartup(uint version, uint flags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateAttributes(out IntPtr attributes, uint initialSize);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMediaType(out IntPtr mediaType);

        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        public static extern int MFCreateSourceReaderFromURL(
            [MarshalAs(UnmanagedType.LPWStr)] string url,
            IntPtr attributes,
            out IntPtr reader);

        [DllImport("ole32.dll", ExactSpelling = true)]
        public static extern int PropVariantClear(ref PROPVARIANT pv);

        private static void* Slot(IntPtr obj, int index) => (*(void***)obj)[index];

        public static void Release(ref IntPtr obj)
        {
            var p = obj;
            obj = IntPtr.Zero;
            if (p != IntPtr.Zero)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(p, 2))(p);
            }
        }

        // IMFAttributes
        public static int AttrGetUInt32(IntPtr attrs, in Guid key, out uint value)
        {
            fixed (Guid* k = &key)
            fixed (uint* v = &value)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Slot(attrs, 7))(attrs, k, v);
            }
        }

        public static int AttrGetUInt64(IntPtr attrs, in Guid key, out ulong value)
        {
            fixed (Guid* k = &key)
            fixed (ulong* v = &value)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong*, int>)Slot(attrs, 8))(attrs, k, v);
            }
        }

        public static int AttrGetGuid(IntPtr attrs, in Guid key, out Guid value)
        {
            fixed (Guid* k = &key)
            fixed (Guid* v = &value)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Slot(attrs, 10))(attrs, k, v);
            }
        }

        /// <summary>IMFAttributes::GetBlob。buf が足りなければ失敗（blobSize に必要量）。</summary>
        public static int AttrGetBlob(IntPtr attrs, in Guid key, byte* buf, uint bufSize, out uint blobSize)
        {
            fixed (Guid* k = &key)
            fixed (uint* n = &blobSize)
            {
                *n = 0;
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, byte*, uint, uint*, int>)Slot(attrs, 15))(attrs, k, buf, bufSize, n);
            }
        }

        public static int AttrSetUInt32(IntPtr attrs, in Guid key, uint value)
        {
            fixed (Guid* k = &key)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Slot(attrs, 21))(attrs, k, value);
            }
        }

        public static int AttrSetGuid(IntPtr attrs, in Guid key, in Guid value)
        {
            fixed (Guid* k = &key)
            fixed (Guid* v = &value)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Slot(attrs, 24))(attrs, k, v);
            }
        }

        // IMFSourceReader
        public static int ReaderSetStreamSelection(IntPtr reader, uint stream, bool selected) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, int, int>)Slot(reader, 4))(reader, stream, selected ? 1 : 0);

        public static int ReaderGetNativeMediaType(IntPtr reader, uint stream, uint index, out IntPtr type)
        {
            fixed (IntPtr* t = &type)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, int>)Slot(reader, 5))(reader, stream, index, t);
            }
        }

        public static int ReaderGetCurrentMediaType(IntPtr reader, uint stream, out IntPtr type)
        {
            fixed (IntPtr* t = &type)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(reader, 6))(reader, stream, t);
            }
        }

        public static int ReaderSetCurrentMediaType(IntPtr reader, uint stream, IntPtr type) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Slot(reader, 7))(reader, stream, IntPtr.Zero, type);

        public static int ReaderSetCurrentPosition(IntPtr reader, in PROPVARIANT position)
        {
            var format = Guid.Empty;
            fixed (PROPVARIANT* p = &position)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, PROPVARIANT*, int>)Slot(reader, 8))(reader, &format, p);
            }
        }

        public static int ReaderReadSample(IntPtr reader, uint stream, out uint flags, out long timestamp, out IntPtr sample)
        {
            uint actual;
            fixed (uint* f = &flags)
            fixed (long* ts = &timestamp)
            fixed (IntPtr* s = &sample)
            {
                *s = IntPtr.Zero;
                return ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)Slot(reader, 9))(
                    reader, stream, 0, &actual, f, ts, s);
            }
        }

        public static int ReaderGetPresentationAttribute(IntPtr reader, uint stream, in Guid key, ref PROPVARIANT value)
        {
            fixed (Guid* k = &key)
            fixed (PROPVARIANT* v = &value)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, PROPVARIANT*, int>)Slot(reader, 12))(reader, stream, k, v);
            }
        }

        // IMFSample
        public static int SampleGetDuration(IntPtr sample, out long duration)
        {
            fixed (long* d = &duration)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, long*, int>)Slot(sample, 37))(sample, d);
            }
        }

        public static int SampleConvertToContiguousBuffer(IntPtr sample, out IntPtr buffer)
        {
            fixed (IntPtr* b = &buffer)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(sample, 41))(sample, b);
            }
        }

        // IMFMediaBuffer
        public static int BufferLock(IntPtr buffer, out byte* data, out uint currentLength)
        {
            uint max;
            fixed (byte** d = &data)
            fixed (uint* c = &currentLength)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, d, &max, c);
            }
        }

        public static int BufferUnlock(IntPtr buffer) =>
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(buffer, 4))(buffer);
    }
}
