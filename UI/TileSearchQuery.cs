using System.Text;
using System.Text.RegularExpressions;

namespace MgaSonicAnvil.UI;

/// <summary>
/// タイル検索の条件判定。空白は AND、<c>|</c> は OR。
/// <c>"..."</c> は中の空白を分けず、その文字列の連続一致。
/// <c>*</c> は任意の文字列、<c>?</c> は任意の一文字（語に含むとき全体マッチ）。
/// Alt+Enter ではヒットしなかったタブを閉じ、未保存は残す。
/// </summary>
internal static class TileSearchQuery
{
    private static readonly char[] OrSeparators = ['|', '｜'];
    private static readonly char[] AndSeparators = [' ', '\u3000', '\t'];
    private static readonly TimeSpan WildcardMatchTimeout = TimeSpan.FromMilliseconds(250);

    public static List<string[]> Parse(string text)
    {
        var groups = new List<string[]>();
        foreach (var group in SplitOutsideQuotes(text, OrSeparators))
        {
            var terms = SplitTerms(group);
            if (terms.Length > 0)
            {
                groups.Add(terms);
            }
        }

        return groups;
    }

    private static List<string> SplitOutsideQuotes(string text, char[] separators)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        foreach (var c in text)
        {
            if (IsQuote(c))
            {
                inQuote = !inQuote;
                current.Append(c);
                continue;
            }

            if (!inQuote && Array.IndexOf(separators, c) >= 0)
            {
                AddPart(parts, current);
                continue;
            }

            current.Append(c);
        }

        AddPart(parts, current);
        return parts;
    }

    private static string[] SplitTerms(string group)
    {
        var terms = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        foreach (var c in group)
        {
            if (IsQuote(c))
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && Array.IndexOf(AndSeparators, c) >= 0)
            {
                AddPart(terms, current);
                continue;
            }

            current.Append(c);
        }

        AddPart(terms, current);
        return [.. terms];
    }

    private static void AddPart(List<string> parts, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        parts.Add(current.ToString());
        current.Clear();
    }

    private static bool IsQuote(char c) => c is '"' or '\uFF02';

    public static bool Matches(string displayName, IReadOnlyList<string[]> groups) =>
        MatchesAny([displayName], groups);

    /// <summary>
    /// AND 語はどれかの文字列に含まれればよい（列をまたいでよい）。OR グループはどれか一つ。
    /// ワイルドカード無しは部分一致。有りは語全体をパターンとして照合する。
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
                if (!TermMatchesAny(texts, term))
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

    private static bool TermMatchesAny(IEnumerable<string> texts, string term)
    {
        if (HasWildcard(term))
        {
            Regex? regex;
            try
            {
                regex = GlobToRegex(term);
            }
            catch (ArgumentException)
            {
                return false;
            }

            foreach (var text in texts)
            {
                try
                {
                    if (regex.IsMatch(text))
                    {
                        return true;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                }
            }

            return false;
        }

        foreach (var text in texts)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasWildcard(string term) =>
        term.Contains('*') || term.Contains('?');

    private static Regex GlobToRegex(string pattern) =>
        new(
            "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            WildcardMatchTimeout);

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
