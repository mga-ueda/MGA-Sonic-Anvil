using System.Globalization;
using System.Text;
using System.Windows.Input;

namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイヤーのフォルダツリー向けインクリメンタルサーチ。表示名の部分一致（先頭・途中）。
/// Ctrl+F の絞り込み検索とは別で、選択を動かすだけ（プレイリストには載せない）。
/// </summary>
internal static class LibraryExplorerTypeahead
{
    /// <summary>打ち込みが止まってからトーストだけ消すまでの待ち。</summary>
    public static readonly TimeSpan ToastIdleClear = TimeSpan.FromSeconds(1);

    public static bool TryMapChar(Key key, ModifierKeys modifiers, out string character)
    {
        character = "";
        if (LibraryPlayerMode.IsExplorerExpandAll(key, modifiers)
            || LibraryPlayerMode.IsExplorerCollapseSubtree(key, modifiers)
            || LibraryPlayerMode.IsExplorerCollapseAllRoots(key, modifiers))
        {
            return false;
        }

        if (modifiers is not (ModifierKeys.None or ModifierKeys.Shift))
        {
            return false;
        }

        // Shift+8 は配下展開。無修飾の上段 0–9 とテンキーはフォルダ名の検索に使う。
        if (modifiers == ModifierKeys.None
            && (key is >= Key.D0 and <= Key.D9 || key is >= Key.NumPad0 and <= Key.NumPad9))
        {
            var digit = key is >= Key.NumPad0 and <= Key.NumPad9
                ? key - Key.NumPad0
                : key - Key.D0;
            character = ((char)('0' + digit)).ToString();
            return true;
        }

        if (key is >= Key.A and <= Key.Z)
        {
            // キーは常に小文字へ。照合側は大小無視なので Caps／Shift でも同じ。
            character = ((char)('a' + (key - Key.A))).ToString();
            return true;
        }

        return false;
    }

    public static bool IsTypeaheadText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var c in text)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>打った文字を常に末尾へ足す（同文字連打も積む。アイドル打ち直しはしない）。</summary>
    public static string Append(string query, string next)
    {
        if (string.IsNullOrEmpty(next))
        {
            return query ?? "";
        }

        next = NormalizeKey(next);
        if (next.Length == 0)
        {
            return query ?? "";
        }

        return string.IsNullOrEmpty(query) ? next : query + next;
    }

    /// <summary>
    /// 現在行がヒットなら維持。そうでなければ下方向→先頭から折り返し。
    /// </summary>
    public static int FindMatchIndex(
        IReadOnlyList<string> names,
        string query,
        int currentIndex)
    {
        if (names.Count == 0 || string.IsNullOrEmpty(query))
        {
            return -1;
        }

        if ((uint)currentIndex < (uint)names.Count && Matches(names[currentIndex], query))
        {
            return currentIndex;
        }

        return FindStepMatchIndex(names, query, currentIndex, direction: 1);
    }

    /// <summary>
    /// ヒット行を direction（+1／-1）へ進める。無ければ折り返す。どれも無ければ -1。
    /// </summary>
    public static int FindStepMatchIndex(
        IReadOnlyList<string> names,
        string query,
        int currentIndex,
        int direction)
    {
        if (names.Count == 0 || string.IsNullOrEmpty(query) || direction == 0)
        {
            return -1;
        }

        if (direction > 0)
        {
            var start = currentIndex < 0 ? 0 : currentIndex + 1;
            for (var i = start; i < names.Count; i++)
            {
                if (Matches(names[i], query))
                {
                    return i;
                }
            }

            var wrapEnd = currentIndex < 0 ? 0 : Math.Min(currentIndex + 1, names.Count);
            for (var i = 0; i < wrapEnd; i++)
            {
                if (Matches(names[i], query))
                {
                    return i;
                }
            }
        }
        else
        {
            var from = currentIndex < 0 ? names.Count - 1 : currentIndex - 1;
            for (var i = from; i >= 0; i--)
            {
                if (Matches(names[i], query))
                {
                    return i;
                }
            }

            var wrapStart = currentIndex < 0 ? names.Count - 1 : currentIndex;
            for (var i = names.Count - 1; i > wrapStart; i--)
            {
                if (Matches(names[i], query))
                {
                    return i;
                }
            }
        }

        // ヒットが自分だけならそのまま。
        if ((uint)currentIndex < (uint)names.Count && Matches(names[currentIndex], query))
        {
            return currentIndex;
        }

        return -1;
    }

    /// <summary>
    /// 大小・半角全角を正規化した部分一致（先頭でも途中でも可）。
    /// </summary>
    public static bool Matches(string name, string query)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(query))
        {
            return false;
        }

        var normalizedQuery = NormalizeKey(query);
        if (normalizedQuery.Length == 0)
        {
            return false;
        }

        var normalizedName = NormalizeKey(name.TrimStart());
        return normalizedName.Contains(normalizedQuery, StringComparison.Ordinal);
    }

    /// <summary>
    /// 表示名上でクエリに当たる最初の範囲（ハイライト用）。正規化後の位置を元文字列へ戻す。
    /// </summary>
    public static bool TryFindMatchRange(string name, string query, out int start, out int length)
    {
        start = 0;
        length = 0;
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(query))
        {
            return false;
        }

        var normalizedQuery = NormalizeKey(query);
        if (normalizedQuery.Length == 0)
        {
            return false;
        }

        var trimmed = name.TrimStart();
        var trimOffset = name.Length - trimmed.Length;
        var direct = trimmed.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase);
        if (direct >= 0)
        {
            start = trimOffset + direct;
            length = normalizedQuery.Length;
            return true;
        }

        var normalizedName = NormalizeKey(trimmed);
        var normIndex = normalizedName.IndexOf(normalizedQuery, StringComparison.Ordinal);
        if (normIndex < 0)
        {
            return false;
        }

        var normPos = 0;
        var rangeStart = -1;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var piece = NormalizeKey(trimmed[i].ToString());
            if (piece.Length == 0)
            {
                continue;
            }

            var pieceEnd = normPos + piece.Length;
            if (rangeStart < 0 && normPos <= normIndex && normIndex < pieceEnd)
            {
                rangeStart = i;
            }

            if (rangeStart >= 0 && pieceEnd >= normIndex + normalizedQuery.Length)
            {
                start = trimOffset + rangeStart;
                length = i - rangeStart + 1;
                return length > 0;
            }

            normPos = pieceEnd;
        }

        if (rangeStart >= 0)
        {
            start = trimOffset + rangeStart;
            length = trimmed.Length - rangeStart;
            return length > 0;
        }

        return false;
    }

    /// <summary>全角英数→半角、大文字→小文字。</summary>
    internal static string NormalizeKey(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var formKc = text.Normalize(NormalizationForm.FormKC);
        return formKc.ToLower(CultureInfo.InvariantCulture);
    }
}
