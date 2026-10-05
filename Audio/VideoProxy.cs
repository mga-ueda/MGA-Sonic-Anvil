using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// MJPEG / Photo JPEG 以外の MOV / MP4 を、スクラブしやすい MJPEG AVI プロキシへ落とす。
/// ffmpeg は同梱せず PATH 上のものを呼ぶ。
/// </summary>
internal static class VideoProxy
{
    /// <summary>フレーム内完結の MJPEG。フレームレートはソースのまま。</summary>
    internal const string EncodeVersion = "mjpeg-avi-srcfps";
    internal const int DefaultRetentionDays = 7;

    /// <summary>
    /// out_time ベースのエンコード進捗が占める割合。残りは mux／フラッシュ待ち用。
    /// 以前は 99% で打ち切っていたため、AVI 書き出し終端で長く止まって見えていた。
    /// </summary>
    internal const double EncodeProgressShare = 0.88;

    internal const double FinalizeProgressCap = 0.99;

    internal static readonly int[] RetentionChoices = [1, 7, 14, 30];

    internal static string CacheDirectory => Path.Combine(AppStorage.RootDirectory, "video-proxy");

    internal static int ClampRetentionDays(int days)
    {
        for (var i = 0; i < RetentionChoices.Length; i++)
        {
            if (RetentionChoices[i] == days)
            {
                return days;
            }
        }

        return DefaultRetentionDays;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> DisplayBySource = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Task<string>> InFlight = new(StringComparer.Ordinal);
    private static readonly Regex DurationLine = new(
        @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OutTimeUs = new(
        @"^out_time_us=(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OutTimeMs = new(
        @"^out_time_ms=(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TotalSizeLine = new(
        @"^total_size=(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryGetDisplayPath(string sourcePath, out string displayPath)
    {
        lock (Gate)
        {
            return TryGetRemembered(sourcePath, out displayPath);
        }
    }

    public static bool TryGetCached(string sourcePath, out string proxyPath)
    {
        lock (Gate)
        {
            return TryGetCachedCore(sourcePath, out proxyPath);
        }
    }

    public static async Task<string> EnsurePlayableAsync(
        string sourcePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(sourcePath);
        }

        if (VideoCodecProbe.CanPlayWithoutProxy(sourcePath))
        {
            Remember(sourcePath, sourcePath);
            progress?.Report(1);
            return sourcePath;
        }

        if (TryGetCached(sourcePath, out var cached))
        {
            progress?.Report(1);
            return cached;
        }

        var key = SourceKey(sourcePath);
        Task<string> pending;
        lock (Gate)
        {
            if (TryGetCachedCore(sourcePath, out cached))
            {
                pending = Task.FromResult(cached);
            }
            else if (InFlight.TryGetValue(key, out var running))
            {
                pending = running;
            }
            else
            {
                pending = EncodeProxyAsync(sourcePath, progress, cancellationToken);
                InFlight[key] = pending;
            }
        }

        try
        {
            var path = await pending.ConfigureAwait(false);
            progress?.Report(1);
            return path;
        }
        finally
        {
            lock (Gate)
            {
                if (InFlight.TryGetValue(key, out var running) && ReferenceEquals(running, pending))
                {
                    InFlight.Remove(key);
                }
            }
        }
    }

    private static async Task<string> EncodeProxyAsync(
        string sourcePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            throw new InvalidOperationException(UiStrings.ErrorVideoProxyNeedsFfmpeg);
        }

        var output = CachePath(sourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temp = output + ".part";
        TryDelete(temp);
        try
        {
            await RunFfmpegAsync(ffmpeg, sourcePath, temp, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(temp) || new FileInfo(temp).Length <= 0)
            {
                throw new InvalidDataException(UiStrings.ErrorVideoProxyFailed);
            }

            progress?.Report(FinalizeProgressCap);
            TryDelete(output);
            File.Move(temp, output);
            progress?.Report(1);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        Remember(sourcePath, output);
        return output;
    }

    /// <summary>out_time／Duration をエンコード帯（0〜EncodeProgressShare）へ。超過分は仕上げ帯へゆるく伸ばす。</summary>
    internal static double MapEncodeProgress(long outTimeUs, long durationUs)
    {
        if (outTimeUs <= 0 || durationUs <= 0)
        {
            return 0;
        }

        var ratio = outTimeUs / (double)durationUs;
        if (ratio <= 1)
        {
            return ratio * EncodeProgressShare;
        }

        // Duration が短い／まだフレームが出ているとき: 仕上げ帯を食い潰さず少しずつ進める。
        var overtime = ratio - 1;
        var extra = (FinalizeProgressCap - EncodeProgressShare) * (1 - Math.Exp(-overtime * 2.5));
        return EncodeProgressShare + extra;
    }

    /// <summary>エンコード完了後の mux／フラッシュ待ち。ファイル成長と経過時間で仕上げ帯を埋める。</summary>
    internal static double MapFinalizeProgress(long sizeAtEncodeEnd, long currentSize, double elapsedSeconds)
    {
        if (elapsedSeconds < 0)
        {
            elapsedSeconds = 0;
        }

        var sizeDelta = Math.Max(0, currentSize - Math.Max(0, sizeAtEncodeEnd));
        var sizeSpan = Math.Max(Math.Max(0, sizeAtEncodeEnd) * 0.02, 8L * 1024 * 1024);
        var sizeT = Math.Clamp(sizeDelta / (double)sizeSpan, 0, 1);

        // 大きい .part ほど終端書き込みが長くなりやすい。
        var expectSec = Math.Clamp(Math.Max(0, sizeAtEncodeEnd) / (40.0 * 1024 * 1024), 2, 180);
        var timeT = 1 - Math.Exp(-elapsedSeconds / expectSec);
        var t = Math.Max(sizeT, timeT);
        return EncodeProgressShare + (FinalizeProgressCap - EncodeProgressShare) * t;
    }

    internal static string? FindFfmpeg()
    {
        var fromPath = FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg");
        if (fromPath is not null)
        {
            return fromPath;
        }

        var winget = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WinGet",
            "Links",
            "ffmpeg.exe");
        return File.Exists(winget) ? winget : null;
    }

    internal static string CachePath(string sourcePath)
    {
        var info = new FileInfo(sourcePath);
        var length = info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "0";
        var stamp = SourceKey(sourcePath) + "|" + length + "|" + EncodeVersion;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..16].ToLowerInvariant();
        return Path.Combine(CacheDirectory, hash + ".avi");
    }

    /// <summary>パス表記のゆれで別キャッシュにしない。更新日時はキーに入れない（再生のたびに変わるファイルがある）。</summary>
    internal static string SourceKey(string sourcePath)
    {
        try
        {
            return Path.GetFullPath(sourcePath).ToUpperInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return sourcePath.ToUpperInvariant();
        }
    }

    /// <summary>起動時と設定変更時。最終更新から保存日数を超えたプロキシと、打ち捨てられた .part を消す。</summary>
    public static int PruneExpired(int retentionDays, DateTime utcNow) =>
        PruneExpired(retentionDays, utcNow, CacheDirectory);

    internal static int PruneExpired(int retentionDays, DateTime utcNow, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return 0;
        }

        var keepDays = ClampRetentionDays(retentionDays);
        var proxyCutoff = utcNow - TimeSpan.FromDays(keepDays);
        var partCutoff = utcNow - TimeSpan.FromHours(6);
        var removed = 0;
        string[] files;
        try
        {
            files = Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (var file in files)
        {
            DateTime written;
            try
            {
                written = File.GetLastWriteTimeUtc(file);
            }
            catch
            {
                continue;
            }

            var ext = Path.GetExtension(file);
            var stale = ext.Equals(".part", StringComparison.OrdinalIgnoreCase)
                ? written < partCutoff
                : IsProxyExtension(ext) && written < proxyCutoff;
            if (!stale)
            {
                continue;
            }

            try
            {
                File.Delete(file);
                removed++;
            }
            catch
            {
            }
        }

        ForgetMissing();
        return removed;
    }

    private static void ForgetMissing()
    {
        lock (Gate)
        {
            var drop = new List<string>();
            foreach (var pair in DisplayBySource)
            {
                if (!string.Equals(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(pair.Value))
                {
                    drop.Add(pair.Key);
                }
            }

            for (var i = 0; i < drop.Count; i++)
            {
                DisplayBySource.Remove(drop[i]);
            }
        }
    }

    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
        }
    }

    private static bool TryGetCachedCore(string sourcePath, out string proxyPath)
    {
        if (TryGetRemembered(sourcePath, out var remembered)
            && File.Exists(remembered)
            && new FileInfo(remembered).Length > 0
            && !string.Equals(remembered, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            Touch(remembered);
            proxyPath = remembered;
            return true;
        }

        proxyPath = CachePath(sourcePath);
        if (File.Exists(proxyPath) && new FileInfo(proxyPath).Length > 0)
        {
            Touch(proxyPath);
            RememberUnlocked(sourcePath, proxyPath);
            return true;
        }

        return false;
    }

    private static bool TryGetRemembered(string sourcePath, out string displayPath) =>
        DisplayBySource.TryGetValue(SourceKey(sourcePath), out displayPath!);

    private static void Remember(string source, string display)
    {
        lock (Gate)
        {
            RememberUnlocked(source, display);
        }
    }

    private static void RememberUnlocked(string source, string display) =>
        DisplayBySource[SourceKey(source)] = display;

    private static async Task RunFfmpegAsync(
        string ffmpeg,
        string input,
        string output,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in BuildFfmpegArguments(input, output))
        {
            start.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException(UiStrings.ErrorVideoProxyFailed);
        }

        var durationUs = 0L;
        var stderr = new StringBuilder();
        var state = new ProxyEncodeProgress();
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            lock (stderr)
            {
                if (stderr.Length > 0)
                {
                    stderr.AppendLine();
                }

                stderr.Append(e.Data);
            }

            var match = DurationLine.Match(e.Data);
            if (!match.Success)
            {
                return;
            }

            var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            durationUs = (long)Math.Round((((hours * 60) + minutes) * 60 + seconds) * 1_000_000);
        };

        process.BeginErrorReadLine();
        using var pulseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stdoutTask = ReadProgressAsync(
            process.StandardOutput,
            () => durationUs,
            state,
            progress,
            cancellationToken);
        var pulseTask = PulseFinalizeProgressAsync(process, output, state, progress, pulseCts.Token);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            pulseCts.Cancel();
            try
            {
                await pulseTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await stdoutTask.ConfigureAwait(false);
        state.Report(progress, FinalizeProgressCap);
        if (process.ExitCode != 0)
        {
            string log;
            lock (stderr)
            {
                log = stderr.ToString();
            }

            throw new InvalidDataException(UiStrings.ErrorVideoProxyFailedDetail(LastFfmpegError(log)));
        }
    }

    private static bool IsProxyExtension(string ext) =>
        ext.Equals(".avi", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> BuildFfmpegArguments(string input, string output)
    {
        var source = LameEncoder.PreferShortPath(input);
        return
        [
            "-hide_banner",
            "-nostdin",
            "-y",
            "-progress",
            "pipe:1",
            "-nostats",
            "-i",
            source,
            "-map",
            "0:v:0",
            "-map",
            "0:a:0?",
            "-c:v",
            "mjpeg",
            "-q:v",
            "5",
            "-pix_fmt",
            "yuvj420p",
            "-c:a",
            "pcm_s16le",
            "-ac",
            "2",
            "-ar",
            "48000",
            "-f",
            "avi",
            output,
        ];
    }

    internal static string LastFfmpegError(string log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return string.Empty;
        }

        var lines = log.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("frame=", StringComparison.Ordinal))
            {
                continue;
            }

            return line.Length > 240 ? line[..240] : line;
        }

        return string.Empty;
    }

    private static async Task ReadProgressAsync(
        StreamReader reader,
        Func<long> durationUs,
        ProxyEncodeProgress state,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            if (line.Equals("progress=end", StringComparison.Ordinal))
            {
                state.MarkEncodeTimeComplete();
                state.Report(progress, FinalizeProgressCap);
                continue;
            }

            var sizeMatch = TotalSizeLine.Match(line);
            if (sizeMatch.Success
                && long.TryParse(sizeMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var size)
                && size > 0)
            {
                state.TotalSize = size;
                var total = durationUs();
                if (total <= 0)
                {
                    // Duration 未取得時は書き出しバイトでゆるく進める。
                    var creep = EncodeProgressShare * (1 - Math.Exp(-size / (50.0 * 1024 * 1024)));
                    state.Report(progress, creep);
                }
            }

            long us = 0;
            var msMatch = OutTimeMs.Match(line);
            if (msMatch.Success
                && long.TryParse(msMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var ms))
            {
                us = ms * 1000;
            }
            else
            {
                var usMatch = OutTimeUs.Match(line);
                if (usMatch.Success)
                {
                    long.TryParse(usMatch.Groups[1].Value, CultureInfo.InvariantCulture, out us);
                }
            }

            if (us <= 0)
            {
                continue;
            }

            var duration = durationUs();
            if (duration <= 0)
            {
                continue;
            }

            if (us >= duration)
            {
                state.MarkEncodeTimeComplete();
            }

            state.Report(progress, MapEncodeProgress(us, duration));
        }
    }

    private static async Task PulseFinalizeProgressAsync(
        Process process,
        string tempPath,
        ProxyEncodeProgress state,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        long sizeAtEncodeEnd = -1;
        var encodeEndTick = 0L;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (process.HasExited)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);

            if (!state.EncodeTimeComplete && state.Value < EncodeProgressShare - 0.001)
            {
                continue;
            }

            if (sizeAtEncodeEnd < 0)
            {
                sizeAtEncodeEnd = Math.Max(state.TotalSize, TryFileLength(tempPath));
                encodeEndTick = Environment.TickCount64;
            }

            var currentSize = Math.Max(state.TotalSize, TryFileLength(tempPath));
            var elapsed = (Environment.TickCount64 - encodeEndTick) / 1000.0;
            state.Report(progress, MapFinalizeProgress(sizeAtEncodeEnd, currentSize, elapsed));
        }
    }

    private static long TryFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class ProxyEncodeProgress
    {
        private readonly object _gate = new();

        public double Value { get; private set; }

        public long TotalSize { get; set; }

        public bool EncodeTimeComplete { get; private set; }

        public void MarkEncodeTimeComplete()
        {
            lock (_gate)
            {
                EncodeTimeComplete = true;
            }
        }

        public void Report(IProgress<double>? progress, double value)
        {
            value = Math.Clamp(value, 0, FinalizeProgressCap);
            lock (_gate)
            {
                if (value < Value)
                {
                    return;
                }

                Value = value;
            }

            progress?.Report(value);
        }
    }

    private static string? FindOnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
