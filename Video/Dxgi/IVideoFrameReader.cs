namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// <see cref="DxgiAsyncFramePump"/> のワーカーが使う 1 枚ずつの読み手。単一スレッド前提。
/// Media Foundation 版（<see cref="DxgiPrototypeFrameSource"/>）と ffmpeg パイプ版（<see cref="FfmpegPipeFrameSource"/>）がある。
/// </summary>
internal interface IVideoFrameReader : IDisposable
{
    bool IsOpen { get; }

    int Width { get; }

    int Height { get; }

    DxgiFramePixelFormat PixelFormat { get; }

    /// <summary><see cref="ReadNext"/> の dest に要るバイト数。</summary>
    int FrameBytes { get; }

    VideoColorInfo ColorInfo { get; }

    TimeSpan FrameDuration { get; }

    bool EndOfStream { get; }

    bool TryOpen(string path, out TimeSpan duration, out int width, out int height);

    /// <summary>position へシーク。以降の ReadNext はその手前（キーフレーム）以降から順に返す。</summary>
    void Seek(TimeSpan position);

    /// <summary>次の 1 枚。dest が null なら画素は捨てて PTS だけ返す。</summary>
    DxgiReadResult ReadNext(byte[]? dest, out long ptsTicks, out long durationTicks);

    void Close();
}
