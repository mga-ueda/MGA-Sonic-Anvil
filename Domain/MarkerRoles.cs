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

        var trimmed = comment.Trim();
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
