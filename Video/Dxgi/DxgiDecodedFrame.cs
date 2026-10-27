namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>デコード済みフレームの画素形式。</summary>
internal enum DxgiFramePixelFormat
{
    /// <summary>BGRA 8bit（stride = Width*4）。MF に RGB32 へソフト変換させたもの（NV12 が通らないときのフォールバック）。</summary>
    Bgra32,

    /// <summary>
    /// NV12 8bit。Y 平面（stride = Width）の直後に UV 交互平面（stride = <see cref="DxgiDecodedFrame.UvStrideFor"/>）。
    /// 色変換は表示側の YUV シェーダが行うので、ワーカーはコピーだけで済む。
    /// </summary>
    Nv12,
}

/// <summary>
/// デコード済み 1 枚。BGRA か NV12（<see cref="Format"/>）。PTS と長さはメディア時間（100ns）。
/// <see cref="Pixels"/> はプール再利用されるので、キャッシュから追い出された後は中身が書き換わり得る。
/// UI はタイマー 1 回の中で使い切り、持ち越すのは参照の比較だけにすること。
/// </summary>
internal sealed class DxgiDecodedFrame
{
    /// <summary>BGRA の 1 枚（互換用）。</summary>
    public DxgiDecodedFrame(long ptsTicks, long durationTicks, int width, int height, byte[] bgra, VideoColorInfo colorInfo)
        : this(ptsTicks, durationTicks, width, height, DxgiFramePixelFormat.Bgra32, bgra, colorInfo)
    {
    }

    public DxgiDecodedFrame(long ptsTicks, long durationTicks, int width, int height, DxgiFramePixelFormat format, byte[] pixels, VideoColorInfo colorInfo)
    {
        PtsTicks = ptsTicks;
        DurationTicks = Math.Max(1, durationTicks);
        Width = width;
        Height = height;
        Format = format;
        Stride = StrideFor(format, width);
        UvStride = format == DxgiFramePixelFormat.Nv12 ? UvStrideFor(width) : 0;
        UvOffset = format == DxgiFramePixelFormat.Nv12 ? Stride * height : 0;
        Pixels = pixels;
        ColorInfo = colorInfo;
    }

    public long PtsTicks { get; }

    public long DurationTicks { get; }

    public TimeSpan Pts => TimeSpan.FromTicks(PtsTicks);

    public int Width { get; }

    public int Height { get; }

    public DxgiFramePixelFormat Format { get; }

    /// <summary>BGRA なら Width*4、NV12 なら Y 平面の stride（= Width）。</summary>
    public int Stride { get; }

    /// <summary>NV12 の UV 平面の stride。BGRA では 0。</summary>
    public int UvStride { get; }

    /// <summary>NV12 の UV 平面の先頭オフセット（= Stride*Height）。BGRA では 0。</summary>
    public int UvOffset { get; }

    /// <summary>画素バッファ（<see cref="Format"/> のレイアウト）。長さは <see cref="RequiredBytes"/> 以上。</summary>
    public byte[] Pixels { get; }

    /// <summary>ソースの色情報。NV12 では表示側の YUV→RGB に使い、BGRA では MF 変換済みなので参考用。</summary>
    public VideoColorInfo ColorInfo { get; }

    /// <summary>NV12 の Y 平面。BGRA では画素全体。</summary>
    public ReadOnlySpan<byte> YPlane => Pixels.AsSpan(0, Math.Min(Pixels.Length, Stride * Height));

    /// <summary>NV12 の UV 平面。BGRA では空。</summary>
    public ReadOnlySpan<byte> UvPlane =>
        Format == DxgiFramePixelFormat.Nv12
            ? Pixels.AsSpan(UvOffset, Math.Min(Pixels.Length - UvOffset, UvStride * ((Height + 1) / 2)))
            : ReadOnlySpan<byte>.Empty;

    public static int StrideFor(DxgiFramePixelFormat format, int width) =>
        format == DxgiFramePixelFormat.Nv12 ? width : width * 4;

    /// <summary>NV12 の UV 平面 stride（幅の半分 × 2 成分、奇数幅は切り上げ）。</summary>
    public static int UvStrideFor(int width) => ((width + 1) / 2) * 2;

    /// <summary>format・寸法のフレーム 1 枚に要るバイト数（BGRA: w*h*4、NV12: w*h*3/2 相当）。</summary>
    public static int RequiredBytes(DxgiFramePixelFormat format, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        return format == DxgiFramePixelFormat.Nv12
            ? (width * height) + (UvStrideFor(width) * ((height + 1) / 2))
            : width * height * 4;
    }

    /// <summary>[Pts, Pts+Duration) に ticks が入る。</summary>
    public bool Covers(long ticks) => ticks >= PtsTicks && ticks < PtsTicks + DurationTicks;

    public long DistanceTo(long ticks) => Covers(ticks) ? 0 : Math.Min(Math.Abs(ticks - PtsTicks), Math.Abs(ticks - (PtsTicks + DurationTicks - 1)));
}
