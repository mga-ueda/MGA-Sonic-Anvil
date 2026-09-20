using System.Windows.Input;

namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイヤーのフォルダツリー向けインクリメンタルサーチ。表示名の先頭一致。
/// Ctrl+F の絞り込み検索とは別で、選択を動かすだけ（プレイリストには載せない）。
/// </summary>
internal static class LibraryExplorerTypeahead
{
    public static readonly TimeSpan IdleReset = TimeSpan.FromSeconds(1);

    public static bool TryMapChar(Key key, ModifierKeys modifiers, out string character)
    {
        character = "";
        if (LibraryPlayerMode.IsExplorerExpandAll(key, modifiers)
            || LibraryPlayerMode.IsExplorerCollapseSubtree(key, modifiers))
        {
            return false;
        }

        if (modifiers is not (ModifierKeys.None or ModifierKeys.Shift))
        {
            return false;
        }

        // テンキーはプレイヤーの再生操作のまま。上段の数字だけフォルダ名に使う。
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return false;
        }

        if (key is >= Key.A and <= Key.Z)
        {
            character = ((char)('a' + (key - Key.A))).ToString();
            return true;
        }

        if (modifiers == ModifierKeys.None && key is >= Key.D0 and <= Key.D9)
        {
            character = ((char)('0' + (key - Key.D0))).ToString();
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

    public static string Append(
        string query,
        string next,
        DateTimeOffset now,
        DateTimeOffset lastInput,
        TimeSpan idleReset)
    {
        if (string.IsNullOrEmpty(next))
        {
            return query ?? "";
        }

        if (string.IsNullOrEmpty(query) || now - lastInput > idleReset)
        {
            return next;
        }

        return query + next;
    }

    /// <summary>
    /// 今の選択がまだヒットなら動かさない。外れたら先頭から最初のヒット。無ければ -1。
    /// </summary>
    public static int FindMatchIndex(IReadOnlyList<string> names, string query, int currentIndex)
    {
        if (names.Count == 0 || string.IsNullOrEmpty(query))
        {
            return -1;
        }

        if ((uint)currentIndex < (uint)names.Count && Matches(names[currentIndex], query))
        {
            return currentIndex;
        }

        for (var i = 0; i < names.Count; i++)
        {
            if (Matches(names[i], query))
            {
                return i;
            }
        }

        return -1;
    }

    public static bool Matches(string name, string query) =>
        name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase);
}
