using System.IO;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

internal readonly record struct Mp3EncodeOptions(
    int WindowsBitRateKbps,
    string LameExePath,
    string LameOptions);

/// <summary>MP3 出力の経路判定と LAME 引数。exe は同梱せず、ユーザー指定時だけ呼ぶ。</summary>
internal static class Mp3Encode
{
    public const int DefaultWindowsBitRateKbps = 192;

    /// <summary>VBR -V2 向けの推奨 LAME オプション。入出力パスは含めない。</summary>
    public const string DefaultLameOptions = "-V2 --noreplaygain";

    public static string ResolveLameOptions(string? options) =>
        string.IsNullOrWhiteSpace(options) ? DefaultLameOptions : options.Trim();

    public static readonly int[] WindowsBitRates = [128, 160, 192, 224, 256, 320];

    private static readonly int[] Mpeg1Layer3Rates =
        [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];

    public static bool TryResolveLameExe(string? path, out string exe)
    {
        exe = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0 || !File.Exists(trimmed))
        {
            return false;
        }

        exe = Path.GetFullPath(trimmed);
        return true;
    }

    public static bool UsesLame(string? path) => TryResolveLameExe(path, out _);

    public static int ClampWindowsBitRate(int kbps)
    {
        var nearest = Mpeg1Layer3Rates[0];
        var best = int.MaxValue;
        foreach (var rate in Mpeg1Layer3Rates)
        {
            var delta = Math.Abs(rate - kbps);
            if (delta < best)
            {
                best = delta;
                nearest = rate;
            }
        }

        return nearest;
    }

    public static IReadOnlyList<string> SplitArguments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    public static IReadOnlyList<string> BuildLameArguments(
        string? options,
        string inputWav,
        string outputMp3)
    {
        // Windows 向け lame.exe は POSIX の「--」を終端と見なさず unrecognized option になる。
        var args = new List<string>(SplitArguments(ResolveLameOptions(options)));
        args.Add(inputWav);
        args.Add(outputMp3);
        return args;
    }

    public static bool TryParseLameProgress(string? line, out double progress)
    {
        progress = 0;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var percentAt = line.IndexOf('%');
        if (percentAt > 0)
        {
            var start = percentAt - 1;
            while (start >= 0 && (char.IsDigit(line[start]) || line[start] == '.'))
            {
                start--;
            }

            var token = line[(start + 1)..percentAt];
            if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct)
                && pct is >= 0 and <= 100)
            {
                progress = pct / 100d;
                return true;
            }
        }

        var slash = line.IndexOf('/');
        if (slash <= 0 || slash + 1 >= line.Length)
        {
            return false;
        }

        var leftStart = slash - 1;
        while (leftStart >= 0 && char.IsDigit(line[leftStart]))
        {
            leftStart--;
        }

        var rightEnd = slash + 1;
        while (rightEnd < line.Length && char.IsDigit(line[rightEnd]))
        {
            rightEnd++;
        }

        if (!long.TryParse(line[(leftStart + 1)..slash], out var done)
            || !long.TryParse(line[(slash + 1)..rightEnd], out var total)
            || total <= 0
            || done < 0)
        {
            return false;
        }

        progress = Math.Clamp(done / (double)total, 0, 1);
        return true;
    }
}
