using System.IO;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil;

/// <summary>コマンドラインの波形パスを正規化する。</summary>
internal static class LaunchFiles
{
    private static string[] _startup = [];

    public static void SetStartup(IReadOnlyList<string> paths) =>
        _startup = paths.Count == 0 ? [] : paths.ToArray();

    public static bool HasStartup => _startup.Length > 0;

    public static string[] TakeStartup()
    {
        var taken = _startup;
        _startup = [];
        return taken;
    }

    /// <summary>引数で新規に開いたタブを優先し、既に復旧済みならそのタブ。</summary>
    public static T? PreferOpened<T>(T? opened, T? existing) where T : class =>
        opened ?? existing;

    /// <summary>起動パスに MP3 / M4A が1つでもあればプレイヤーで開く。</summary>
    public static bool ContainsMp3(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var ext = Path.GetExtension(path);
            if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string[] Collect(IEnumerable<string> args)
    {
        var result = new List<string>();
        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw) || IsFlag(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim().Trim('"'));
            }
            catch
            {
                continue;
            }

            result.Add(full);
        }

        // 起動引数はプレイヤーも想定し、M4A を含める。
        return AudioCodec.CollectPlayerOpenable(result);
    }

    private static bool IsFlag(string value) =>
        value.StartsWith('-') || (value.Length > 1 && value[0] == '/' && value[1] is not '\\' and not '/');
}
