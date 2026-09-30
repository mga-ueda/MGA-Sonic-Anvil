namespace MgaSonicAnvil.Domain;

/// <summary>プレイリスト波形列の幅。S が基準、M は 2 倍、L は 3 倍。</summary>
internal enum LibraryPlaylistWaveformSize
{
    S = 1,
    M = 2,
    L = 3,
}

internal static class LibraryPlaylistWaveformSizes
{
    /// <summary>S の棒本数。PeakPyramid.PlaylistBarCount と揃える。</summary>
    public const int BaseBarCount = 48;

    public static int BarCount(LibraryPlaylistWaveformSize size) =>
        BaseBarCount * Math.Clamp((int)size, 1, 3);

    public static LibraryPlaylistWaveformSize Parse(string? value)
    {
        if (string.Equals(value, "S", StringComparison.OrdinalIgnoreCase))
        {
            return LibraryPlaylistWaveformSize.S;
        }

        if (string.Equals(value, "M", StringComparison.OrdinalIgnoreCase))
        {
            return LibraryPlaylistWaveformSize.M;
        }

        return LibraryPlaylistWaveformSize.L;
    }

    public static string Format(LibraryPlaylistWaveformSize size) => size switch
    {
        LibraryPlaylistWaveformSize.S => "S",
        LibraryPlaylistWaveformSize.M => "M",
        _ => "L",
    };

    /// <summary>WAVE だけなら true。空リストは false。</summary>
    public static bool IsWaveOnly(IReadOnlyList<LibraryFileRow> rows)
    {
        if (rows.Count == 0)
        {
            return false;
        }

        foreach (var row in rows)
        {
            if (!string.Equals(row.Kind, "WAVE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static LibraryPlaylistWaveformSize Resolve(
        LibraryPlaylistWaveformSize preferred,
        bool autoLargeForWaveOnly,
        IReadOnlyList<LibraryFileRow> rows)
    {
        if (autoLargeForWaveOnly && IsWaveOnly(rows))
        {
            return LibraryPlaylistWaveformSize.L;
        }

        return preferred is LibraryPlaylistWaveformSize.S or LibraryPlaylistWaveformSize.M
            ? preferred
            : LibraryPlaylistWaveformSize.L;
    }
}
