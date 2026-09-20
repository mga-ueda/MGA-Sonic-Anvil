namespace MgaSonicAnvil.UI;

/// <summary>
/// タイル検索の条件判定。空白は AND、<c>|</c> は OR。
/// Alt+Enter ではヒットしなかったタブを閉じ、未保存は残す。
/// </summary>
internal static class TileSearchQuery
{
    private static readonly char[] OrSeparators = ['|', '｜'];
    private static readonly char[] AndSeparators = [' ', '\u3000', '\t'];

    public static List<string[]> Parse(string text)
    {
        var groups = new List<string[]>();
        foreach (var group in text.Split(OrSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var terms = group.Split(AndSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length > 0)
            {
                groups.Add(terms);
            }
        }

        return groups;
    }

    public static bool Matches(string displayName, IReadOnlyList<string[]> groups) =>
        MatchesAny([displayName], groups);

    /// <summary>
    /// AND 語はどれかの文字列に含まれればよい（列をまたいでよい）。OR グループはどれか一つ。
    /// </summary>
    public static bool MatchesAny(IEnumerable<string> texts, IReadOnlyList<string[]> groups)
    {
        if (groups.Count == 0)
        {
            return true;
        }

        foreach (var terms in groups)
        {
            var all = true;
            foreach (var term in terms)
            {
                var hit = false;
                foreach (var text in texts)
                {
                    if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = true;
                        break;
                    }
                }

                if (!hit)
                {
                    all = false;
                    break;
                }
            }

            if (all)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ヒットしたタブ、または未保存の編集があるタブは残す。</summary>
    public static bool ShouldKeep(bool matches, bool isDirty) => matches || isDirty;

    public static DocumentSession[] SessionsToDrop(IReadOnlyList<DocumentSession> sessions, IReadOnlyList<string[]> groups)
    {
        var drop = new List<DocumentSession>();
        foreach (var session in sessions)
        {
            if (ShouldKeep(Matches(session.DisplayName, groups), session.Document.IsDirty))
            {
                continue;
            }

            drop.Add(session);
        }

        return [.. drop];
    }
}
