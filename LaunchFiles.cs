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

            if (!AudioCodec.IsOpenable(full)
                || result.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(full);
        }

        return result.ToArray();
    }

    private static bool IsFlag(string value) =>
        value.StartsWith('-') || (value.Length > 1 && value[0] == '/' && value[1] is not '\\' and not '/');
}
