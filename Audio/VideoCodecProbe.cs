using System.Buffers.Binary;
using System.IO;
using System.Text;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>MOV / MP4 / AVI の映像 fourcc。MJPEG / Photo JPEG 以外はプロキシが必要。MKV / WebM / MPG は常にプロキシ。</summary>
internal static class VideoCodecProbe
{
    public static bool CanPlayWithoutProxy(string path)
    {
        if (!TryReadVideoFourCcs(path, out var codecs) || codecs.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < codecs.Count; i++)
        {
            if (!IsDirectPlayFamily(codecs[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsDirectPlayFamily(string? fourcc)
    {
        if (string.IsNullOrWhiteSpace(fourcc) || fourcc.Length < 4)
        {
            return false;
        }

        var tag = fourcc.Length == 4 ? fourcc : fourcc[..4];
        return tag.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("mjpg", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("mjpa", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("mjpb", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("dmb1", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryReadVideoFourCcs(string path, out IReadOnlyList<string> fourccs) =>
        TryRead(path, out fourccs, out _);

    /// <summary>映像 fourcc と、音声の有無に依らないコンテナ尺（mvhd／mdhd）。</summary>
    public static bool TryRead(string path, out IReadOnlyList<string> fourccs, out double durationSeconds) =>
        TryRead(path, out fourccs, out durationSeconds, out _, out _, out _, out _, out _, out _, out _);

    /// <summary>
    /// 映像 fourcc・コンテナ尺に加え、音声トラックがあればレート／ch／bit。
    /// MediaFoundation が読めない ipcm などもボックスから取る。
    /// </summary>
    public static bool TryRead(
        string path,
        out IReadOnlyList<string> fourccs,
        out double durationSeconds,
        out int audioSampleRate,
        out int audioChannels,
        out int audioBits) =>
        TryRead(
            path,
            out fourccs,
            out durationSeconds,
            out audioSampleRate,
            out audioChannels,
            out audioBits,
            out _,
            out _,
            out _,
            out _);

    /// <summary>
    /// 映像 fourcc・尺・音声に加え、解像度とフレームレート（取れるとき）。
    /// </summary>
    public static bool TryRead(
        string path,
        out IReadOnlyList<string> fourccs,
        out double durationSeconds,
        out int audioSampleRate,
        out int audioChannels,
        out int audioBits,
        out int videoWidth,
        out int videoHeight,
        out double frameRate) =>
        TryRead(
            path,
            out fourccs,
            out durationSeconds,
            out audioSampleRate,
            out audioChannels,
            out audioBits,
            out videoWidth,
            out videoHeight,
            out frameRate,
            out _);

    /// <summary>
    /// 映像 fourcc・尺・音声フォーマットに加え、解像度・fps・音声 fourcc（取れるとき）。
    /// </summary>
    public static bool TryRead(
        string path,
        out IReadOnlyList<string> fourccs,
        out double durationSeconds,
        out int audioSampleRate,
        out int audioChannels,
        out int audioBits,
        out int videoWidth,
        out int videoHeight,
        out double frameRate,
        out string audioCodec)
    {
        fourccs = [];
        durationSeconds = 0;
        audioSampleRate = 0;
        audioChannels = 0;
        audioBits = 0;
        videoWidth = 0;
        videoHeight = 0;
        frameRate = 0;
        audioCodec = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        if (ext.Equals(".avi", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadAvi(
                path,
                out fourccs,
                out durationSeconds,
                out videoWidth,
                out videoHeight,
                out frameRate,
                out audioCodec);
        }

        if (ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || LibraryPlaylistDocuments.IsMpgExtension(ext))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var found = new List<string>();
            var movieSeconds = 0d;
            var trackSeconds = 0d;
            Walk(
                stream,
                0,
                stream.Length,
                found,
                inMoov: false,
                inTrak: false,
                trackTimescale: 0,
                trackIsVideo: false,
                ref movieSeconds,
                ref trackSeconds,
                ref audioSampleRate,
                ref audioChannels,
                ref audioBits,
                ref videoWidth,
                ref videoHeight,
                ref frameRate,
                ref audioCodec);
            durationSeconds = movieSeconds > 0 ? movieSeconds : trackSeconds;
            if (found.Count == 0
                && durationSeconds <= 0
                && audioSampleRate <= 0
                && videoWidth <= 0)
            {
                return false;
            }

            fourccs = found;
            return found.Count > 0
                || durationSeconds > 0
                || audioSampleRate > 0
                || videoWidth > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>AVI の vids / auds strh.fccHandler。尺は avih のフレーム数×μs/frame。</summary>
    private static bool TryReadAvi(
        string path,
        out IReadOnlyList<string> fourccs,
        out double durationSeconds,
        out int videoWidth,
        out int videoHeight,
        out double frameRate,
        out string audioCodec)
    {
        fourccs = [];
        durationSeconds = 0;
        videoWidth = 0;
        videoHeight = 0;
        frameRate = 0;
        audioCodec = string.Empty;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < 12)
            {
                return false;
            }

            var header = new byte[12];
            if (stream.Read(header, 0, 12) != 12)
            {
                return false;
            }

            if (!AsciiEquals(header, 0, "RIFF") || !AsciiEquals(header, 8, "AVI "))
            {
                return false;
            }

            var found = new List<string>();
            var microSecPerFrame = 0u;
            var totalFrames = 0u;
            var width = 0u;
            var height = 0u;
            var strhRate = 0u;
            var strhScale = 0u;
            WalkAvi(
                stream,
                12,
                stream.Length,
                found,
                ref microSecPerFrame,
                ref totalFrames,
                ref width,
                ref height,
                ref strhRate,
                ref strhScale,
                ref audioCodec);
            if (microSecPerFrame > 0 && totalFrames > 0)
            {
                durationSeconds = totalFrames * (microSecPerFrame / 1_000_000d);
            }

            if (microSecPerFrame > 0)
            {
                frameRate = 1_000_000d / microSecPerFrame;
            }
            else if (strhScale > 0 && strhRate > 0)
            {
                frameRate = strhRate / (double)strhScale;
            }

            videoWidth = (int)width;
            videoHeight = (int)height;

            if (found.Count == 0 && durationSeconds <= 0 && videoWidth <= 0)
            {
                return false;
            }

            fourccs = found;
            return found.Count > 0 || durationSeconds > 0 || videoWidth > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static void WalkAvi(
        Stream stream,
        long start,
        long end,
        List<string> found,
        ref uint microSecPerFrame,
        ref uint totalFrames,
        ref uint width,
        ref uint height,
        ref uint strhRate,
        ref uint strhScale,
        ref string audioCodec)
    {
        var offset = start;
        var header = new byte[8];
        var payload = new byte[56];
        while (offset + 8 <= end)
        {
            stream.Position = offset;
            if (stream.Read(header, 0, 8) != 8)
            {
                return;
            }

            var type = Encoding.ASCII.GetString(header, 0, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
            var payloadStart = offset + 8;
            var next = payloadStart + size;
            if (next > end || next < payloadStart)
            {
                return;
            }

            if (type == "LIST" && size >= 4)
            {
                stream.Position = payloadStart;
                if (stream.Read(payload, 0, 4) == 4)
                {
                    WalkAvi(
                        stream,
                        payloadStart + 4,
                        next,
                        found,
                        ref microSecPerFrame,
                        ref totalFrames,
                        ref width,
                        ref height,
                        ref strhRate,
                        ref strhScale,
                        ref audioCodec);
                }
            }
            else if (type == "avih" && size >= 40)
            {
                stream.Position = payloadStart;
                if (stream.Read(payload, 0, 40) == 40)
                {
                    if (microSecPerFrame == 0)
                    {
                        microSecPerFrame = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
                        totalFrames = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(16, 4));
                    }

                    if (width == 0)
                    {
                        width = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(32, 4));
                        height = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(36, 4));
                    }
                }
            }
            else if (type == "strh" && size >= 8)
            {
                var read = (int)Math.Min(size, (uint)payload.Length);
                stream.Position = payloadStart;
                if (stream.Read(payload, 0, read) != read)
                {
                    // fall through
                }
                else if (AsciiEquals(payload, 0, "vids"))
                {
                    var handler = Encoding.ASCII.GetString(payload, 4, 4);
                    if (!string.IsNullOrWhiteSpace(handler) && !found.Contains(handler, StringComparer.OrdinalIgnoreCase))
                    {
                        found.Add(handler);
                    }

                    if (read >= 28 && strhScale == 0)
                    {
                        strhScale = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(20, 4));
                        strhRate = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(24, 4));
                    }
                }
                else if (AsciiEquals(payload, 0, "auds") && audioCodec.Length == 0)
                {
                    var handler = Encoding.ASCII.GetString(payload, 4, 4).Trim();
                    if (!string.IsNullOrWhiteSpace(handler))
                    {
                        audioCodec = handler;
                    }
                }
            }

            // RIFF chunks are word-aligned.
            offset = next + (size & 1);
        }
    }

    private static bool AsciiEquals(byte[] buffer, int offset, string expected)
    {
        if (offset < 0 || offset + expected.Length > buffer.Length)
        {
            return false;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (buffer[offset + i] != (byte)expected[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void Walk(
        Stream stream,
        long start,
        long end,
        List<string> found,
        bool inMoov,
        bool inTrak,
        uint trackTimescale,
        bool trackIsVideo,
        ref double movieSeconds,
        ref double trackSeconds,
        ref int audioSampleRate,
        ref int audioChannels,
        ref int audioBits,
        ref int videoWidth,
        ref int videoHeight,
        ref double frameRate,
        ref string audioCodec)
    {
        var offset = start;
        var header = new byte[16];
        while (offset + 8 <= end)
        {
            if (!TryReadBox(stream, offset, end, header, out var size, out var type, out var headerSize))
            {
                return;
            }

            var payloadStart = offset + headerSize;
            var next = offset + size;
            if (next > end || next < payloadStart)
            {
                return;
            }

            if (type == "moov")
            {
                Walk(
                    stream,
                    payloadStart,
                    next,
                    found,
                    inMoov: true,
                    inTrak: false,
                    trackTimescale: 0,
                    trackIsVideo: false,
                    ref movieSeconds,
                    ref trackSeconds,
                    ref audioSampleRate,
                    ref audioChannels,
                    ref audioBits,
                    ref videoWidth,
                    ref videoHeight,
                    ref frameRate,
                    ref audioCodec);
            }
            else if (type == "trak")
            {
                Walk(
                    stream,
                    payloadStart,
                    next,
                    found,
                    inMoov: true,
                    inTrak: true,
                    trackTimescale: 0,
                    trackIsVideo: false,
                    ref movieSeconds,
                    ref trackSeconds,
                    ref audioSampleRate,
                    ref audioChannels,
                    ref audioBits,
                    ref videoWidth,
                    ref videoHeight,
                    ref frameRate,
                    ref audioCodec);
            }
            else if (type is "mdia" or "minf" or "stbl")
            {
                Walk(
                    stream,
                    payloadStart,
                    next,
                    found,
                    inMoov: true,
                    inTrak,
                    trackTimescale,
                    trackIsVideo,
                    ref movieSeconds,
                    ref trackSeconds,
                    ref audioSampleRate,
                    ref audioChannels,
                    ref audioBits,
                    ref videoWidth,
                    ref videoHeight,
                    ref frameRate,
                    ref audioCodec);
            }
            else if (inMoov && type == "mvhd")
            {
                if (TryReadHeaderDuration(stream, payloadStart, next, out var seconds) && seconds > movieSeconds)
                {
                    movieSeconds = seconds;
                }
            }
            else if (inMoov && type == "mdhd")
            {
                // 尺が 0 でも timescale は取る（stts の fps 用）。
                TryReadHeaderDuration(
                    stream,
                    payloadStart,
                    next,
                    out var seconds,
                    out var timescale);
                if (seconds > trackSeconds)
                {
                    trackSeconds = seconds;
                }

                if (inTrak && timescale > 0)
                {
                    trackTimescale = timescale;
                }
            }
            else if (inMoov && type == "stsd")
            {
                var before = found.Count;
                ReadStsd(
                    stream,
                    payloadStart,
                    next,
                    found,
                    ref audioSampleRate,
                    ref audioChannels,
                    ref audioBits,
                    ref videoWidth,
                    ref videoHeight,
                    ref audioCodec);
                if (found.Count > before)
                {
                    trackIsVideo = true;
                }
            }
            else if (inTrak
                && trackIsVideo
                && type == "stts"
                && frameRate <= 0
                && trackTimescale > 0)
            {
                if (TryReadSttsFrameRate(stream, payloadStart, next, trackTimescale, out var fps))
                {
                    frameRate = fps;
                }
            }

            if (size < 8)
            {
                return;
            }

            offset = next;
        }
    }

    /// <summary>mvhd / mdhd の timescale と duration。音声トラックが無くても映像尺が取れる。</summary>
    private static bool TryReadHeaderDuration(Stream stream, long start, long end, out double seconds) =>
        TryReadHeaderDuration(stream, start, end, out seconds, out _);

    private static bool TryReadHeaderDuration(
        Stream stream,
        long start,
        long end,
        out double seconds,
        out uint timescale)
    {
        seconds = 0;
        timescale = 0;
        var length = end - start;
        if (length < 20)
        {
            return false;
        }

        var body = new byte[(int)Math.Min(length, 32)];
        stream.Position = start;
        if (stream.Read(body, 0, body.Length) < 20)
        {
            return false;
        }

        ulong duration;
        if (body[0] == 1)
        {
            if (body.Length < 32)
            {
                return false;
            }

            timescale = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(20, 4));
            duration = BinaryPrimitives.ReadUInt64BigEndian(body.AsSpan(24, 8));
        }
        else
        {
            timescale = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(12, 4));
            duration = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4));
            if (duration == uint.MaxValue)
            {
                return false;
            }
        }

        if (timescale == 0)
        {
            return false;
        }

        // timescale は呼出側が fps 用に使えるよう、duration が 0 でも out に残す。
        if (duration == 0)
        {
            return false;
        }

        seconds = duration / (double)timescale;
        return seconds > 0;
    }

    /// <summary>stts 先頭エントリの sample_delta と mdhd timescale から fps。</summary>
    private static bool TryReadSttsFrameRate(
        Stream stream,
        long start,
        long end,
        uint timescale,
        out double frameRate)
    {
        frameRate = 0;
        if (timescale == 0 || end - start < 16)
        {
            return false;
        }

        var body = new byte[16];
        stream.Position = start;
        if (stream.Read(body, 0, 16) != 16)
        {
            return false;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(4, 4));
        if (entryCount == 0)
        {
            return false;
        }

        var sampleDelta = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(12, 4));
        if (sampleDelta == 0)
        {
            return false;
        }

        frameRate = timescale / (double)sampleDelta;
        return frameRate > 0 && frameRate < 1000;
    }

    private static void ReadStsd(
        Stream stream,
        long start,
        long end,
        List<string> found,
        ref int audioSampleRate,
        ref int audioChannels,
        ref int audioBits,
        ref int videoWidth,
        ref int videoHeight,
        ref string audioCodec)
    {
        if (end - start < 8)
        {
            return;
        }

        stream.Position = start + 4;
        var countBuf = new byte[4];
        if (stream.Read(countBuf, 0, 4) != 4)
        {
            return;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(countBuf);
        var offset = start + 8;
        var header = new byte[8];
        for (var i = 0; i < count && offset + 8 <= end; i++)
        {
            stream.Position = offset;
            if (stream.Read(header, 0, 8) != 8)
            {
                return;
            }

            var size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            if (size < 8)
            {
                return;
            }

            var fourcc = Encoding.ASCII.GetString(header, 4, 4);
            var entryEnd = Math.Min(end, offset + size);
            if (IsAudioSample(fourcc))
            {
                if (audioCodec.Length == 0)
                {
                    audioCodec = fourcc.TrimEnd();
                }

                TryParseAudioSample(
                    stream,
                    offset,
                    entryEnd,
                    ref audioSampleRate,
                    ref audioChannels,
                    ref audioBits);
            }
            else if (!IsHintOrMetaSample(fourcc))
            {
                found.Add(fourcc);
                if (videoWidth <= 0 && size >= 36)
                {
                    TryParseVisualSample(stream, offset, entryEnd, ref videoWidth, ref videoHeight);
                }
            }

            offset += size;
        }
    }

    /// <summary>VisualSampleEntry の width / height（エントリ先頭から 32／34）。</summary>
    private static void TryParseVisualSample(
        Stream stream,
        long start,
        long end,
        ref int videoWidth,
        ref int videoHeight)
    {
        if (end - start < 36)
        {
            return;
        }

        var body = new byte[36];
        stream.Position = start;
        if (stream.Read(body, 0, 36) != 36)
        {
            return;
        }

        var width = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(32, 2));
        var height = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(34, 2));
        if (width > 0 && height > 0)
        {
            videoWidth = width;
            videoHeight = height;
        }
    }

    internal static bool IsAudioSample(string fourcc) =>
        fourcc.Equals("mp4a", StringComparison.OrdinalIgnoreCase)
        || IsUncompressedAudio(fourcc)
        || fourcc.Equals("ulaw", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("alaw", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("aac ", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("opus", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("fLaC", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("ac-3", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("ec-3", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("samr", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// MOV/MP4 等の非圧縮 PCM 音声 fourcc。不明・圧縮系は false（ビット深度列は出さない）。
    /// </summary>
    internal static bool IsUncompressedAudio(string? fourcc)
    {
        if (string.IsNullOrWhiteSpace(fourcc))
        {
            return false;
        }

        var tag = fourcc.Trim();
        if (tag.Length >= 4)
        {
            tag = tag[..4];
        }

        return tag.Equals("ipcm", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("lpcm", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("sowt", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("twos", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("in24", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("in32", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("fl32", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("fl64", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("raw", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("pcm", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryParseAudioSample(
        Stream stream,
        long start,
        long end,
        ref int audioSampleRate,
        ref int audioChannels,
        ref int audioBits)
    {
        var length = end - start;
        if (length < 36)
        {
            return;
        }

        var body = new byte[(int)Math.Min(length, 256)];
        stream.Position = start;
        var read = stream.Read(body, 0, body.Length);
        if (read < 36)
        {
            return;
        }

        var channels = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(24, 2));
        var bits = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(26, 2));
        var packedRate = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(32, 4));
        var rate = (int)(packedRate >> 16);
        if (rate <= 0)
        {
            rate = packedRate > int.MaxValue ? 0 : (int)packedRate;
        }

        for (var i = 36; i + 10 <= read; i++)
        {
            if (body[i] != (byte)'p' || body[i + 1] != (byte)'c'
                || body[i + 2] != (byte)'m' || body[i + 3] != (byte)'C')
            {
                continue;
            }

            var pcmBits = body[i + 9];
            if (pcmBits is 8 or 16 or 24 or 32)
            {
                bits = pcmBits;
            }

            break;
        }

        if (rate > 0)
        {
            audioSampleRate = rate;
        }

        if (channels > 0)
        {
            audioChannels = channels;
        }

        if (bits > 0)
        {
            audioBits = bits;
        }
    }

    private static bool IsHintOrMetaSample(string fourcc) =>
        fourcc.Equals("tmcd", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("text", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("mett", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("metx", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadBox(
        Stream stream,
        long offset,
        long end,
        byte[] header,
        out long size,
        out string type,
        out int headerSize)
    {
        size = 0;
        type = string.Empty;
        headerSize = 8;
        stream.Position = offset;
        if (stream.Read(header, 0, 8) != 8)
        {
            return false;
        }

        var size32 = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        type = Encoding.ASCII.GetString(header, 4, 4);
        if (size32 == 1)
        {
            if (stream.Read(header, 8, 8) != 8)
            {
                return false;
            }

            size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8));
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            size = end - offset;
        }
        else
        {
            size = size32;
        }

        return size >= headerSize && offset + size <= end;
    }
}
