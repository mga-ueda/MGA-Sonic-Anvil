using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Domain;

/// <summary>
/// プレイリスト列と同じ表記。ライブラリ検索は、ファイル名やフォルダ名とこれを横断する。
/// </summary>
internal static class LibraryPlaylistSearchTexts
{
    public static string[] Build(
        string path,
        long size,
        long mtimeUtcTicks,
        AudioFileTags tags,
        string? loudnessText = null)
    {
        var kind = AudioCodec.DetectKind(path);
        var texts = new List<string>(24);
        Add(texts, Path.GetFileName(path));
        Add(texts, tags.Title);
        Add(texts, tags.Artist);
        Add(texts, tags.AlbumArtist);
        Add(texts, tags.Album);
        Add(texts, tags.Track);
        Add(texts, tags.Disc);
        Add(texts, tags.Year);
        Add(texts, tags.Genre);
        Add(texts, tags.Composer);
        Add(texts, tags.Comment);
        Add(texts, FormatKind(kind));

        if (!LibraryPlaylistDocuments.IsPdf(kind) && tags.DurationSeconds > 0)
        {
            Add(texts, UiStrings.FormatDuration(tags.DurationSeconds));
        }

        var tagRateOk = AudioCodec.IsPlausibleAudioSampleRate(tags.SampleRate);
        var hideAudioFormat = LibraryPlaylistDocuments.IsVisual(kind) && !tagRateOk;
        var hideBitDepth = kind is AudioFileKind.Mp3 or AudioFileKind.M4a
            || (LibraryPlaylistDocuments.IsVideo(kind)
                && !VideoCodecProbe.IsUncompressedAudio(tags.AudioCodec));
        var hideBitRate = kind is AudioFileKind.Wave or AudioFileKind.Aiff;
        var sampleRate = hideAudioFormat ? 0 : tags.SampleRate;
        var bits = hideAudioFormat || hideBitDepth ? 0 : tags.BitsPerSample;
        var channels = hideAudioFormat ? 0 : tags.Channels;
        var bitRate = hideBitRate ? 0 : tags.BitRateKbps;
        if (!hideBitRate && bitRate <= 0 && sampleRate > 0 && channels > 0 && bits > 0)
        {
            bitRate = (int)Math.Round(sampleRate * channels * bits / 1000d);
        }

        if (sampleRate > 0)
        {
            Add(texts, UiStrings.FormatSampleRate(sampleRate));
        }

        if (bits > 0)
        {
            Add(texts, UiStrings.FormatBitDepth(bits));
        }

        if (channels > 0)
        {
            Add(texts, UiStrings.FormatChannels(channels));
        }

        Add(
            texts,
            LibraryPlaylistDocuments.IsVideo(kind)
                ? UiStrings.FormatVideoBitRate(bitRate)
                : UiStrings.FormatBitRate(bitRate));
        Add(texts, UiStrings.FormatFileBytesCompact(size));
        if (mtimeUtcTicks > 0)
        {
            var local = DateTime.SpecifyKind(new DateTime(mtimeUtcTicks), DateTimeKind.Utc).ToLocalTime();
            Add(texts, UiStrings.FormatFileDate(local));
        }

        var folder = Path.GetDirectoryName(path);
        Add(texts, folder);
        Add(texts, LibraryFileList.ParentFolderName(path));
        if (tags.HasArtwork)
        {
            Add(texts, UiStrings.LibraryJacketMark);
        }

        Add(texts, UiStrings.FormatVideoSize(tags.VideoWidth, tags.VideoHeight));
        Add(texts, UiStrings.FormatFrameRate(tags.FrameRate));
        Add(texts, tags.VideoCodec);
        Add(texts, tags.AudioCodec);
        if (AudioCodec.IsUncompressedPcm(kind))
        {
            Add(texts, loudnessText);
        }

        return texts.Count == 0 ? [] : [.. texts];
    }

    public static string FormatKind(AudioFileKind kind) => kind switch
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

    private static void Add(List<string> texts, string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            texts.Add(text);
        }
    }
}
