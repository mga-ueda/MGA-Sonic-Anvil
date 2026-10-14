using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Config;

/// <summary>
/// プレイリスト表示に必要なキャッシュが揃っているか。
/// 波形列／ジャケット列が実際に有効な形式だけを要求する。
/// フォルダ層がすべて揃っているときだけ纏め載せに使う。
/// </summary>
internal static class LibraryPlaylistCache
{
    public static bool IsWarm(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !LibraryCatalog.TryGetFreshTags(path, out var tags))
        {
            return false;
        }

        var kindKey = KindKey(path);
        if (RequiresWaveform(kindKey) && !LibraryPlaylistDocuments.IsDocument(path))
        {
            foreach (var barCount in ResolveBarCounts(kindKey))
            {
                if (!LibraryMediaCache.TryGetWaveform(path, barCount, out _))
                {
                    return false;
                }
            }
        }

        if (RequiresJacket(kindKey) && !IsJacketSatisfied(path, tags, kindKey))
        {
            return false;
        }

        return true;
    }

    public static bool AreAllWarm(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < paths.Count; i++)
        {
            if (!IsWarm(paths[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool RequiresWaveformForPath(string path) =>
        RequiresWaveform(KindKey(path)) && !LibraryPlaylistDocuments.IsDocument(path);

    public static bool RequiresJacketForPath(string path) =>
        RequiresJacket(KindKey(path));

    /// <summary>形式に応じた列プリセットで波形がオンか。</summary>
    public static bool RequiresWaveform(string kindKey)
    {
        if (LibraryColumnFilter.IsPdfFamilyKind(kindKey))
        {
            return false;
        }

        return ContainsColumn(ColumnsForKind(kindKey), LibraryFileColumn.Waveform);
    }

    /// <summary>形式に応じた列プリセットでジャケットがオンか。</summary>
    public static bool RequiresJacket(string kindKey) =>
        ContainsColumn(ColumnsForKind(kindKey), LibraryFileColumn.Jacket);

    public static bool WaveformColumnEnabledInSettings()
    {
        var settings = AppStorage.Settings;
        return ContainsColumn(settings.ResolvedLibraryListColumnsWave(), LibraryFileColumn.Waveform)
            || ContainsColumn(settings.ResolvedLibraryListColumnsMp3(), LibraryFileColumn.Waveform)
            || ContainsColumn(settings.ResolvedLibraryListColumnsVideo(), LibraryFileColumn.Waveform)
            || ContainsColumn(settings.ResolvedLibraryListColumnsMixed(), LibraryFileColumn.Waveform);
    }

    public static bool JacketColumnEnabledInSettings()
    {
        var settings = AppStorage.Settings;
        return ContainsColumn(settings.ResolvedLibraryListColumnsWave(), LibraryFileColumn.Jacket)
            || ContainsColumn(settings.ResolvedLibraryListColumnsMp3(), LibraryFileColumn.Jacket)
            || ContainsColumn(settings.ResolvedLibraryListColumnsVideo(), LibraryFileColumn.Jacket)
            || ContainsColumn(settings.ResolvedLibraryListColumnsMixed(), LibraryFileColumn.Jacket);
    }

    private static bool IsJacketSatisfied(string path, AudioFileTags tags, string kindKey)
    {
        if (LibraryMediaCache.TryGetArtworkProbe(path, out _))
        {
            return true;
        }

        // 埋め込み音声はタグの HasArtwork が「本当にあるか」の一次情報。
        // 無しと分かっていれば画像キャッシュは不要。
        var kind = AudioCodec.DetectKind(path);
        if (LibraryArtworkScan.IsEmbeddedKind(kind) && tags.Probed && !tags.HasArtwork)
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<LibraryFileColumn> ColumnsForKind(string kindKey)
    {
        var settings = AppStorage.Settings;
        if (LibraryColumnFilter.IsPcmFamilyKind(kindKey))
        {
            return settings.ResolvedLibraryListColumnsWave();
        }

        if (LibraryColumnFilter.IsTagFamilyKind(kindKey))
        {
            return settings.ResolvedLibraryListColumnsMp3();
        }

        if (LibraryColumnFilter.IsVideoFamilyKind(kindKey)
            || LibraryColumnFilter.IsPdfFamilyKind(kindKey))
        {
            return settings.ResolvedLibraryListColumnsVideo();
        }

        return settings.ResolvedLibraryListColumnsMixed();
    }

    private static int[] ResolveBarCounts(string kindKey)
    {
        var settings = AppStorage.Settings;
        if (LibraryColumnFilter.IsTagFamilyKind(kindKey))
        {
            return [LibraryPlaylistWaveformSizes.BarCount(settings.ResolvedLibraryPlaylistWaveformSizeMp3())];
        }

        if (LibraryColumnFilter.IsPcmFamilyKind(kindKey))
        {
            return [LibraryPlaylistWaveformSizes.BarCount(settings.ResolvedLibraryPlaylistWaveformSizeWave())];
        }

        // 動画などは広い方。
        var wave = LibraryPlaylistWaveformSizes.BarCount(settings.ResolvedLibraryPlaylistWaveformSizeWave());
        var mp3 = LibraryPlaylistWaveformSizes.BarCount(settings.ResolvedLibraryPlaylistWaveformSizeMp3());
        return wave == mp3 ? [wave] : [wave, mp3];
    }

    private static string KindKey(string path)
    {
        var fromName = LibraryColumnFilter.KindFromFileName(Path.GetFileName(path) ?? path);
        if (fromName is not null)
        {
            return fromName;
        }

        return AudioCodec.DetectKind(path) switch
        {
            AudioFileKind.Mp3 => "MP3",
            AudioFileKind.M4a => "M4A",
            AudioFileKind.Aiff => "AIFF",
            AudioFileKind.Mp4 => "MP4",
            AudioFileKind.Mov => "MOV",
            AudioFileKind.Avi => "AVI",
            AudioFileKind.Mkv => "MKV",
            AudioFileKind.Webm => "WEBM",
            AudioFileKind.Mpg => "MPG",
            AudioFileKind.Pdf => "PDF",
            _ => "WAVE",
        };
    }

    private static bool ContainsColumn(IReadOnlyList<LibraryFileColumn> columns, LibraryFileColumn want)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i] == want)
            {
                return true;
            }
        }

        return false;
    }
}
