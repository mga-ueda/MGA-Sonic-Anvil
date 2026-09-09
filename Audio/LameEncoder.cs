using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

internal static class LameEncoder
{
    public static void Encode(
        AudioDocument document,
        string path,
        string lameExe,
        string? options,
        IProgress<double>? progress = null)
    {
        var tempDir = PreferAsciiDirectory(Path.GetTempPath());
        var wav = Path.Combine(tempDir, $"mga-anvil-{Guid.NewGuid():N}.wav");
        var mp3 = Path.Combine(tempDir, $"mga-anvil-{Guid.NewGuid():N}.mp3");
        try
        {
            var wavProgress = progress is null
                ? null
                : new Progress<double>(p => progress.Report(p * 0.45));
            AudioCodec.SavePcm16Wave(document, wav, wavProgress);
            progress?.Report(0.45);
            Run(PreferShortPath(lameExe), options, wav, mp3, progress);
            progress?.Report(0.95);
            var destDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(mp3, path, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            TryDelete(wav);
            TryDelete(mp3);
        }
    }

    private static void Run(
        string lameExe,
        string? options,
        string inputWav,
        string outputMp3,
        IProgress<double>? progress)
    {
        var console = ConsoleEncoding();
        var start = new ProcessStartInfo
        {
            FileName = lameExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = console,
            StandardErrorEncoding = console,
            WorkingDirectory = Path.GetDirectoryName(inputWav) ?? PreferAsciiDirectory(Path.GetTempPath()),
        };
        foreach (var arg in Mp3Encode.BuildLameArguments(options, inputWav, outputMp3))
        {
            start.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = start };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(UiStrings.ErrorLameFailed);
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{UiStrings.ErrorLameFailed}\n{ex.Message}", ex);
        }

        var errTask = Task.Run(() => PumpStream(process.StandardError, stderr, progress));
        var outTask = Task.Run(() => PumpStream(process.StandardOutput, stdout, progress));
        process.WaitForExit();
        Task.WaitAll(errTask, outTask);

        var log = CombineLog(stderr.ToString(), stdout.ToString());
        if (process.ExitCode != 0
            || !File.Exists(outputMp3)
            || new FileInfo(outputMp3).Length <= 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(log)
                    ? UiStrings.ErrorLameFailed
                    : $"{UiStrings.ErrorLameFailed}\n{log.Trim()}");
        }
    }

    private static void PumpStream(StreamReader reader, StringBuilder log, IProgress<double>? progress)
    {
        var buffer = new char[512];
        var line = new StringBuilder();
        while (true)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (c is '\r' or '\n')
                {
                    FlushLameLine(line, log, progress);
                }
                else
                {
                    line.Append(c);
                }
            }
        }

        FlushLameLine(line, log, progress);
    }

    private static void FlushLameLine(StringBuilder line, StringBuilder log, IProgress<double>? progress)
    {
        if (line.Length == 0)
        {
            return;
        }

        var text = line.ToString();
        line.Clear();
        if (!string.IsNullOrWhiteSpace(text))
        {
            log.AppendLine(text);
        }

        if (progress is not null && Mp3Encode.TryParseLameProgress(text, out var parsed))
        {
            progress.Report(0.45 + parsed * 0.50);
        }
    }

    private static string CombineLog(string stderr, string stdout)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return stdout;
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return stderr;
        }

        return stderr + Environment.NewLine + stdout;
    }

    private static Encoding ConsoleEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            return Encoding.Default;
        }
    }

    private static string PreferAsciiDirectory(string path)
    {
        var shortPath = PreferShortPath(path);
        if (IsAsciiPath(shortPath))
        {
            return shortPath;
        }

        try
        {
            var windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            if (Directory.Exists(windowsTemp) && IsAsciiPath(windowsTemp))
            {
                return windowsTemp;
            }
        }
        catch
        {
            // ユーザー Temp を使う。
        }

        return shortPath;
    }

    private static bool IsAsciiPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var c in path)
        {
            if (c > 127)
            {
                return false;
            }
        }

        return true;
    }

    private static string PreferShortPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        try
        {
            var buffer = new StringBuilder(260);
            var length = GetShortPathName(path, buffer, buffer.Capacity);
            if (length > buffer.Capacity)
            {
                buffer.Capacity = length;
                length = GetShortPathName(path, buffer, buffer.Capacity);
            }

            if (length > 0)
            {
                var shortPath = buffer.ToString();
                if (!string.IsNullOrWhiteSpace(shortPath))
                {
                    return shortPath;
                }
            }
        }
        catch
        {
            // 短いパスが取れなければそのまま渡す。
        }

        return path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

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
            // 一時ファイルが残っても変換結果は書けている。
        }
    }
}
