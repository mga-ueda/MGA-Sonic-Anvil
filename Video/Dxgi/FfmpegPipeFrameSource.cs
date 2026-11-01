using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// Media Foundation で開けない／GPU で読めない動画用。ffmpeg.exe に NV12 の生フレームを標準出力へ書かせて 1 枚ずつ読む。
/// プロキシは作らない（ファイルに書かない）。
/// <list type="bullet">
/// <item>寸法は <see cref="DxgiPrototypeFrameSource.FitOutputSize"/> の箱へ縮め（回転メタデータは ffmpeg が適用）、
/// 色は BT.709／リミテッドレンジへ揃えて出させる（<see cref="ColorInfo"/> は固定）。</item>
/// <item>出力は固定フレームレート（ソースの fps）。PTS は「Seek 位置 + 枚数 × 1 枚の長さ」。</item>
/// <item>Seek はプロセスを止め、次の <see cref="ReadNext"/> で <c>-ss</c> 付きで起動し直す（-ss は入力側＝正確シーク）。</item>
/// </list>
/// 単一スレッド前提（<see cref="DxgiAsyncFramePump"/> のワーカーだけが触る）。
/// </summary>
internal sealed partial class FfmpegPipeFrameSource : IVideoFrameReader
{
    private const int ProbeTimeoutMs = 15_000;
    private const int MaxErrorLines = 12;
    private static readonly double DefaultFps = 30;

    private readonly string _ffmpeg;
    private readonly Queue<string> _stderrTail = new();
    private string? _path;
    private string? _fpsArgument;
    private long _frameTicks = TimeSpan.FromSeconds(1.0 / DefaultFps).Ticks;
    private Process? _process;
    private Stream? _stdout;
    private long _segmentStartTicks;
    private long _segmentFrames;
    private bool _endOfStream;
    private byte[]? _scratch;
    private byte[]? _heldFirstFrame;
    private bool _replayHeldFrame;

    public FfmpegPipeFrameSource(string ffmpegExe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegExe);
        _ffmpeg = ffmpegExe;
    }

    public bool IsOpen => _path is not null;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public DxgiFramePixelFormat PixelFormat => DxgiFramePixelFormat.Nv12;

    public int FrameBytes => DxgiDecodedFrame.RequiredBytes(DxgiFramePixelFormat.Nv12, Width, Height);

    public VideoColorInfo ColorInfo => VideoColorInfo.Default;

    public TimeSpan FrameDuration => TimeSpan.FromTicks(_frameTicks);

    public bool EndOfStream => _endOfStream;

    /// <summary>直近の失敗（開けない・デコードできない）の ffmpeg のエラー行。無ければ空。</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>プローブで分かったコーデック名（ffmpeg 表記。例: prores, hevc）。</summary>
    public string CodecName { get; private set; } = string.Empty;

    public bool TryOpen(string path, out TimeSpan duration, out int width, out int height)
    {
        Close();
        duration = TimeSpan.Zero;
        width = 0;
        height = 0;
        LastError = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !File.Exists(_ffmpeg))
        {
            return false;
        }

        if (!RunProbe(path, out var log))
        {
            LastError = LastMeaningfulLine(log);
            return false;
        }

        if (!TryParseProbe(log, out var info))
        {
            LastError = LastMeaningfulLine(log);
            return false;
        }

        var (w, h) = DxgiPrototypeFrameSource.FitOutputSize(info.DisplayWidth, info.DisplayHeight);
        if (w <= 0 || h <= 0)
        {
            return false;
        }

        var fps = info.Fps > 0 ? info.Fps : DefaultFps;
        _fpsArgument = fps.ToString("0.###", CultureInfo.InvariantCulture);
        _frameTicks = Math.Max(1, (long)Math.Round(TimeSpan.TicksPerSecond / fps));
        Width = w;
        Height = h;
        CodecName = info.Codec;
        _path = path;
        _segmentStartTicks = 0;
        _segmentFrames = 0;
        _endOfStream = false;

        // 実際にデコードできるか先頭の 1 枚で確かめる（デコーダが無い・壊れたファイルはここで落とす）。
        // プロセスは止めず、直後の先頭への Seek ではこの 1 枚から続ける（起動し直しを 1 回省く）。
        var probe = new byte[FrameBytes];
        if (ReadNext(probe, out _, out _) != DxgiReadResult.Frame)
        {
            var error = LastError;
            Close();
            LastError = error;
            return false;
        }

        _heldFirstFrame = probe;
        duration = info.Duration;
        width = w;
        height = h;
        return true;
    }

    public void Seek(TimeSpan position)
    {
        if (_heldFirstFrame is not null && position.Ticks <= 0 && _segmentStartTicks == 0 && _segmentFrames == 1)
        {
            _replayHeldFrame = true;
            _endOfStream = false;
            return;
        }

        _heldFirstFrame = null;
        _replayHeldFrame = false;
        StopProcess();
        _segmentStartTicks = Math.Max(0, position.Ticks);
        _segmentFrames = 0;
        _endOfStream = false;
    }

    public DxgiReadResult ReadNext(byte[]? dest, out long ptsTicks, out long durationTicks)
    {
        ptsTicks = 0;
        durationTicks = 0;
        if (_path is null || Width <= 0 || Height <= 0)
        {
            return DxgiReadResult.Failed;
        }

        if (_endOfStream)
        {
            return DxgiReadResult.EndOfStream;
        }

        if (dest is not null && dest.Length < FrameBytes)
        {
            return DxgiReadResult.NeedLargerBuffer;
        }

        var held = _heldFirstFrame;
        var replay = _replayHeldFrame;
        _heldFirstFrame = null;
        _replayHeldFrame = false;
        if (replay && held is not null)
        {
            if (dest is not null)
            {
                held.AsSpan(0, FrameBytes).CopyTo(dest);
            }

            ptsTicks = 0;
            durationTicks = _frameTicks;
            return DxgiReadResult.Frame;
        }

        if (_stdout is null && !StartProcess())
        {
            return DxgiReadResult.Failed;
        }

        var target = dest ?? (_scratch is { } s && s.Length >= FrameBytes ? s : _scratch = new byte[FrameBytes]);
        var got = ReadFully(_stdout!, target, FrameBytes);
        if (got < FrameBytes)
        {
            var exitedWithError = WaitExitCode() is { } code && code != 0;
            if (_segmentFrames == 0 && exitedWithError)
            {
                LastError = TailError();
                StopProcess();
                return DxgiReadResult.Failed;
            }

            StopProcess();
            _endOfStream = true;
            return DxgiReadResult.EndOfStream;
        }

        ptsTicks = _segmentStartTicks + (_segmentFrames * _frameTicks);
        durationTicks = _frameTicks;
        _segmentFrames++;
        return DxgiReadResult.Frame;
    }

    public void Close()
    {
        StopProcess();
        _path = null;
        _fpsArgument = null;
        Width = 0;
        Height = 0;
        CodecName = string.Empty;
        _segmentStartTicks = 0;
        _segmentFrames = 0;
        _endOfStream = false;
        _scratch = null;
        _heldFirstFrame = null;
        _replayHeldFrame = false;
        _frameTicks = TimeSpan.FromSeconds(1.0 / DefaultFps).Ticks;
    }

    public void Dispose() => Close();

    /// <summary>デコード用の引数。<paramref name="startTicks"/> が 0 より大きければ入力側 -ss（正確シーク）。</summary>
    internal static IReadOnlyList<string> BuildDecodeArguments(string input, long startTicks, int width, int height, string fps)
    {
        var args = new List<string>(40)
        {
            "-hide_banner",
            "-nostdin",
            "-loglevel",
            "error",
            "-hwaccel",
            "auto",
        };
        if (startTicks > 0)
        {
            args.Add("-ss");
            args.Add(TimeSpan.FromTicks(startTicks).TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture));
        }

        args.AddRange(
        [
            "-i",
            input,
            "-map",
            "0:v:0",
            "-an",
            "-sn",
            "-dn",
            "-vf",
            string.Create(
                CultureInfo.InvariantCulture,
                $"scale={width}:{height}:flags=bicubic:in_range=auto:out_range=tv:out_color_matrix=bt709,format=nv12"),
            "-r",
            fps,
            "-fps_mode",
            "cfr",
            "-f",
            "rawvideo",
            "-pix_fmt",
            "nv12",
            "pipe:1",
        ]);
        return args;
    }

    /// <summary><c>ffmpeg -i</c> の stderr から最初の映像ストリーム（アートワーク除く）の情報を読む。</summary>
    internal static bool TryParseProbe(string log, out FfmpegVideoProbe info)
    {
        info = default;
        if (string.IsNullOrEmpty(log))
        {
            return false;
        }

        var lines = log.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var duration = TimeSpan.Zero;
        for (var i = 0; i < lines.Length; i++)
        {
            var d = DurationLine().Match(lines[i]);
            if (d.Success)
            {
                duration = TimeSpan.FromSeconds(
                    (int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture) * 3600)
                    + (int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture) * 60)
                    + double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture));
                break;
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var stream = VideoStreamLine().Match(lines[i]);
            if (!stream.Success || lines[i].Contains("(attached pic)", StringComparison.Ordinal))
            {
                continue;
            }

            var rest = stream.Groups[2].Value;
            var size = FrameSize().Match(rest);
            if (!size.Success)
            {
                continue;
            }

            var width = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
            var height = int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
            var fps = ParseRate(FpsToken().Match(rest)) is var f and > 0 ? f : ParseRate(TbrToken().Match(rest));

            // 次の Stream 行までが、このストリームの付帯情報（Side data の回転など）。
            var rotation = 0;
            for (var j = i + 1; j < lines.Length && !StreamLine().IsMatch(lines[j]); j++)
            {
                var r = RotationLine().Match(lines[j]);
                if (r.Success && double.TryParse(r.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees))
                {
                    rotation = (((int)Math.Round(degrees) % 360) + 360) % 360;
                    break;
                }
            }

            var swap = rotation is 90 or 270;
            info = new FfmpegVideoProbe(
                stream.Groups[1].Value,
                swap ? height : width,
                swap ? width : height,
                fps,
                duration);
            return width > 0 && height > 0;
        }

        return false;
    }

    internal static string LastMeaningfulLine(string log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return string.Empty;
        }

        var lines = log.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0
                || line.StartsWith("At least one output file must be specified", StringComparison.Ordinal))
            {
                continue;
            }

            return line.Length > 240 ? line[..240] : line;
        }

        return string.Empty;
    }

    private static double ParseRate(Match match)
    {
        if (!match.Success)
        {
            return 0;
        }

        var text = match.Groups[1].Value;
        var scale = 1.0;
        if (text.EndsWith('k'))
        {
            scale = 1000;
            text = text[..^1];
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 && v < 1000 * scale
            ? v * scale
            : 0;
    }

    private bool RunProbe(string path, out string log)
    {
        log = string.Empty;
        var start = new ProcessStartInfo(_ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-hide_banner", "-nostdin", "-i", LameEncoder.PreferShortPath(path) })
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            var read = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ProbeTimeoutMs))
            {
                TryKill(process);
                return false;
            }

            log = read.Wait(ProbeTimeoutMs) ? read.Result : string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log = ex.Message;
            return false;
        }
    }

    private bool StartProcess()
    {
        StopProcess();
        if (_path is null || _fpsArgument is null)
        {
            return false;
        }

        var start = new ProcessStartInfo(_ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in BuildDecodeArguments(LameEncoder.PreferShortPath(_path), _segmentStartTicks, Width, Height, _fpsArgument))
        {
            start.ArgumentList.Add(arg);
        }

        lock (_stderrTail)
        {
            _stderrTail.Clear();
        }

        try
        {
            var process = new Process { StartInfo = start };
            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                {
                    return;
                }

                lock (_stderrTail)
                {
                    _stderrTail.Enqueue(e.Data.Trim());
                    while (_stderrTail.Count > MaxErrorLines)
                    {
                        _stderrTail.Dequeue();
                    }
                }
            };
            if (!process.Start())
            {
                process.Dispose();
                return false;
            }

            process.BeginErrorReadLine();
            _process = process;
            _stdout = process.StandardOutput.BaseStream;
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private void StopProcess()
    {
        var process = _process;
        _process = null;
        _stdout = null;
        if (process is null)
        {
            return;
        }

        TryKill(process);
        try
        {
            // Kill は非同期。閉じた直後に元ファイルを移動・削除できるよう、ハンドルが離れるまで少し待つ。
            process.WaitForExit(2000);
        }
        catch (InvalidOperationException)
        {
        }

        process.Dispose();
    }

    private int? WaitExitCode()
    {
        var process = _process;
        if (process is null)
        {
            return null;
        }

        try
        {
            if (!process.WaitForExit(2000))
            {
                return null;
            }

            // 非同期の stderr 読みを最後まで流し込む。
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private string TailError()
    {
        lock (_stderrTail)
        {
            return _stderrTail.Count == 0 ? string.Empty : _stderrTail.Last();
        }
    }

    private static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        var total = 0;
        try
        {
            while (total < count)
            {
                var n = stream.Read(buffer, total, count - total);
                if (n <= 0)
                {
                    break;
                }

                total += n;
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }

        return total;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex DurationLine();

    [GeneratedRegex(@"^\s*Stream #\d+:\d+\S*: Video: ([\w-]+)(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoStreamLine();

    [GeneratedRegex(@"^\s*Stream #\d+:\d+", RegexOptions.CultureInvariant)]
    private static partial Regex StreamLine();

    [GeneratedRegex(@"(?<![\w.])(\d{2,5})x(\d{2,5})(?![\w])", RegexOptions.CultureInvariant)]
    private static partial Regex FrameSize();

    [GeneratedRegex(@"([\d.]+k?)\s+fps\b", RegexOptions.CultureInvariant)]
    private static partial Regex FpsToken();

    [GeneratedRegex(@"([\d.]+k?)\s+tbr\b", RegexOptions.CultureInvariant)]
    private static partial Regex TbrToken();

    [GeneratedRegex(@"rotation of (-?[\d.]+) degrees", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RotationLine();
}

/// <summary><c>ffmpeg -i</c> から読んだ映像ストリーム。寸法は回転適用後（表示上の向き）。</summary>
internal readonly record struct FfmpegVideoProbe(string Codec, int DisplayWidth, int DisplayHeight, double Fps, TimeSpan Duration);
