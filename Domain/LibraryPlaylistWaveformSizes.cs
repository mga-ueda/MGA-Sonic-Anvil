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
        BaseBarCount * (int)Clamp(size);

    public static LibraryPlaylistWaveformSize Clamp(LibraryPlaylistWaveformSize size) =>
        size is LibraryPlaylistWaveformSize.S or LibraryPlaylistWaveformSize.M
            ? size
            : LibraryPlaylistWaveformSize.L;

    public static LibraryPlaylistWaveformSize Parse(string? value) =>
        Parse(value, LibraryPlaylistWaveformSize.L);

    public static LibraryPlaylistWaveformSize Parse(string? value, LibraryPlaylistWaveformSize fallback)
    {
        if (string.Equals(value, "S", StringComparison.OrdinalIgnoreCase))
        {
            return LibraryPlaylistWaveformSize.S;
        }

        if (string.Equals(value, "M", StringComparison.OrdinalIgnoreCase))
        {
            return LibraryPlaylistWaveformSize.M;
        }

        if (string.Equals(value, "L", StringComparison.OrdinalIgnoreCase))
        {
            return LibraryPlaylistWaveformSize.L;
        }

        return Clamp(fallback);
    }

    public static string Format(LibraryPlaylistWaveformSize size) => size switch
    {
        LibraryPlaylistWaveformSize.S => "S",
        LibraryPlaylistWaveformSize.M => "M",
        _ => "L",
    };

    /// <summary>
    /// WAVE / AIFF だけなら Wave 側、MP3 / M4A だけなら MP3 側。
    /// 空・混在は広い方（列は1本なので、狭い設定で片方が潰れないようにする）。
    /// </summary>
    public static LibraryPlaylistWaveformSize Resolve(
        LibraryPlaylistWaveformSize wave,
        LibraryPlaylistWaveformSize mp3,
        IReadOnlyList<LibraryFileRow> rows)
    {
        wave = Clamp(wave);
        mp3 = Clamp(mp3);
        return LibraryColumnFilter.ClassifyPlaylistColumns(rows) switch
        {
            LibraryPlaylistColumnKind.Wave => wave,
            LibraryPlaylistColumnKind.Mp3 => mp3,
            _ => wave >= mp3 ? wave : mp3,
        };
    }
}
