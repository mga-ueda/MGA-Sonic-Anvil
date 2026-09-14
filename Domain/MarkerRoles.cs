namespace MgaSonicAnvil.Domain;

internal enum MarkerRole
{
    None,
    Anacrusis,
    Loop,
    Exit,
    Remove,
}

/// <summary>IM Importer と同じ予約語。大文字小文字は区別しない。</summary>
internal static class MarkerRoles
{
    public static MarkerRole FromComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return MarkerRole.None;
        }

        var text = comment.AsSpan().Trim();
        var found = MarkerRole.None;
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (!IsDash(text[i]))
            {
                continue;
            }

            var role = RoleOf(text[i + 1]);
            if (role == MarkerRole.None)
            {
                continue;
            }

            var after = i + 2;
            if (after < text.Length && char.IsLetterOrDigit(text[after]))
            {
                continue;
            }

            found = role;
        }

        return found;
    }

    public static string Normalize(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return string.Empty;
        }

        return UppercaseReservedTags(comment.Trim());
    }

    private static string UppercaseReservedTags(string trimmed)
    {
        var chars = trimmed.ToCharArray();
        var changed = false;
        for (var i = 0; i < chars.Length - 1; i++)
        {
            if (!IsDash(chars[i]))
            {
                continue;
            }

            if (RoleOf(chars[i + 1]) == MarkerRole.None)
            {
                continue;
            }

            var after = i + 2;
            if (after < chars.Length && char.IsLetterOrDigit(chars[after]))
            {
                continue;
            }

            var upper = char.ToUpperInvariant(chars[i + 1]);
            if (chars[i + 1] == upper)
            {
                continue;
            }

            chars[i + 1] = upper;
            changed = true;
        }

        return changed ? new string(chars) : trimmed;
    }

    /// <summary>
    /// 接尾辞が無ければコメントを予約タグへ置き換える。既に別の役割があるときは触らない。
    /// </summary>
    public static string EnsureTag(string? comment, MarkerRole role)
    {
        var tag = RoleTag(role);
        if (tag.Length == 0)
        {
            return Normalize(comment);
        }

        var current = FromComment(comment);
        if (current == role || current != MarkerRole.None)
        {
            return Normalize(comment);
        }

        return tag;
    }

    public static string RoleTag(MarkerRole role) => role switch
    {
        MarkerRole.Anacrusis => "-A",
        MarkerRole.Loop => "-L",
        MarkerRole.Exit => "-E",
        MarkerRole.Remove => "-R",
        _ => string.Empty,
    };

    /// <summary>
    /// EXPORT 計画用。ドキュメントのコメントは書き換えない。
    /// <c>-L</c> の直後の接尾辞なしマーカーを <c>-E</c> として扱う。
    /// サンプルループ終端に接尾辞なしマーカーがあれば同様。マーカーは増やさない。
    /// </summary>
    public static MarkerSnapshot[] WithAutoExitComments(
        IReadOnlyList<MarkerSnapshot> markers,
        WaveSelection sampleLoop,
        long frameCount)
    {
        var list = DistinctByFrame(markers);
        for (var i = 0; i + 1 < list.Count; i++)
        {
            if (FromComment(list[i].Comment) != MarkerRole.Loop)
            {
                continue;
            }

            if (FromComment(list[i + 1].Comment) != MarkerRole.None)
            {
                continue;
            }

            list[i + 1] = list[i + 1] with
            {
                Comment = EnsureTag(list[i + 1].Comment, MarkerRole.Exit),
            };
        }

        if (!sampleLoop.IsEmpty
            && sampleLoop.EndFrame > 0
            && sampleLoop.EndFrame < frameCount)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Frame != sampleLoop.EndFrame)
                {
                    continue;
                }

                if (FromComment(list[i].Comment) == MarkerRole.None)
                {
                    list[i] = list[i] with
                    {
                        Comment = EnsureTag(list[i].Comment, MarkerRole.Exit),
                    };
                }

                break;
            }
        }

        return list.ToArray();
    }

    private static List<MarkerSnapshot> DistinctByFrame(IReadOnlyList<MarkerSnapshot> markers)
    {
        var list = new List<MarkerSnapshot>(markers.Count);
        foreach (var marker in markers.OrderBy(item => item.Frame))
        {
            if (list.Count > 0 && list[^1].Frame == marker.Frame)
            {
                list[^1] = marker;
                continue;
            }

            list.Add(marker);
        }

        return list;
    }

    private static bool IsDash(char c) => c is '-' or 'ー' or '−';

    private static MarkerRole RoleOf(char tag) => char.ToUpperInvariant(tag) switch
    {
        'A' => MarkerRole.Anacrusis,
        'L' => MarkerRole.Loop,
        'E' => MarkerRole.Exit,
        'R' => MarkerRole.Remove,
        _ => MarkerRole.None,
    };
}
