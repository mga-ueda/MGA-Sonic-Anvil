namespace MgaSonicAnvil.Domain;

/// <summary>
/// VST3 検索フォルダの正規化。空のままならスキャナ既定を使う（既定値で埋めない）。
/// 無いフォルダも登録として残す（後で作る想定。UI で赤字表示）。
/// </summary>
internal static class Vst3SearchPathList
{
    public static string[] Resolve(IEnumerable<string>? saved)
    {
        if (saved is null)
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in saved)
        {
            if (!PathUtil.TryGetFullPathTrimmedSeparators(raw, out var full) || !seen.Add(full))
            {
                continue;
            }

            result.Add(full);
        }

        return [.. result];
    }
}
