using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal sealed partial class LibraryBrowserView
{
    internal static LibraryFileRow CreateRow(DocumentSession session) =>
        CreateRow(
            session,
            DefaultAudioFormat.SampleRate,
            DefaultAudioFormat.BitsPerSample,
            ChannelLayout.Stereo.Channels);

    private LibraryFileRow CreateRowWithDefaults(DocumentSession session)
    {
        var row = CreateRow(session, _defaultSampleRate, _defaultBitsPerSample, _defaultChannels);
        row.CutPending = IsCutPath(session.Document.SourcePath);
        return row;
    }

    internal static LibraryFileRow CreateRow(
        DocumentSession session,
        int defaultSampleRate,
        int defaultBitsPerSample,
        int defaultChannels)
    {
        var document = session.Document;
        var path = document.SourcePath;
        // ディスクキャッシュがあれば行生成時に載せる（非同期1件ずつ差し替えで選択を壊さない）。
        var tags = HydrateIntegratedLufsFromCache(document);
        var deferred = document.IsDeferredLoad;
        // MP3 は Xing と Media Foundation／実デコードがエンコーダ遅延ぶん（数十 ms）ずれる。
        // リストは最初に出したタグ尺のままにする（ホバーやピーク確定でミリ秒が跳ねない）。
        // PDF のページ秒は時計用だけで、時間列には出さない。
        // 読み込み済みの Wave / AIFF は編集後の尺。タグ尺のままだとエディタの結果が残る。
        var livePcm = !deferred
            && document.SourceKind is AudioFileKind.Wave or AudioFileKind.Aiff
            && document.DurationSeconds > 0;
        var duration = LibraryPlaylistDocuments.IsPdf(document)
            ? 0
            : livePcm
                ? document.DurationSeconds
                : tags.DurationSeconds > 0
                    ? tags.DurationSeconds
                    : deferred ? 0 : document.DurationSeconds;
        var tagRateOk = AudioCodec.IsPlausibleAudioSampleRate(tags.SampleRate);
        var hideAudioFormat = LibraryPlaylistDocuments.IsVisual(document) && !tagRateOk;
        // PDF と音声の無い動画の 48kHz/16bit は再生時計用ダミー。列には出さない。
        // 動画はタグの音声フォーマットを優先（ホバー時の MF プローブで列が 0.001kHz 等に跳ねない）。
        var preferTagFormat = deferred || LibraryPlaylistDocuments.IsVideo(document);
        // MP3 / M4A にビット深度は無い（デコード仮定の 16bit を列に出さない）。ビットレートは出す。
        // 動画は非圧縮 PCM 音声（ipcm 等）だけビット深度を出す。圧縮・不明は空欄。
        var hideBitDepth = document.SourceKind is AudioFileKind.Mp3 or AudioFileKind.M4a
            || (LibraryPlaylistDocuments.IsVideo(document)
                && !VideoCodecProbe.IsUncompressedAudio(tags.AudioCodec));
        // WAVE / AIFF の「ビットレート」は PCM から機械的に出るだけなので列は空欄。圧縮音声だけ出す。
        var hideBitRate = document.SourceKind is AudioFileKind.Wave or AudioFileKind.Aiff;
        var sampleRate = hideAudioFormat
            ? 0
            : preferTagFormat && tagRateOk
                ? tags.SampleRate
                : deferred ? 0 : document.SampleRate;
        var bits = hideAudioFormat || hideBitDepth
            ? 0
            : preferTagFormat && tags.BitsPerSample > 0
                ? tags.BitsPerSample
                : deferred ? 0 : document.BitsPerSample;
        var channels = hideAudioFormat
            ? 0
            : preferTagFormat && tags.Channels > 0
                ? tags.Channels
                : deferred ? 0 : document.Channels;
        var bitRate = hideBitRate ? 0 : tags.BitRateKbps;
        if (!hideBitRate && bitRate <= 0 && sampleRate > 0 && channels > 0 && bits > 0)
        {
            bitRate = (int)Math.Round(sampleRate * channels * bits / 1000d);
        }

        var hasArt = document.HasArtwork || tags.HasArtwork;
        var row = new LibraryFileRow
        {
            Tag = session,
            Name = session.DisplayName,
            Title = tags.Title,
            Artist = tags.Artist,
            AlbumArtist = tags.AlbumArtist,
            Album = tags.Album,
            Track = tags.Track,
            TrackNumber = tags.TrackNumber,
            Disc = tags.Disc,
            DiscNumber = tags.DiscNumber,
            Year = tags.Year,
            YearNumber = tags.YearNumber,
            Genre = tags.Genre,
            Composer = tags.Composer,
            Comment = tags.Comment,
            Kind = FormatKind(document.SourceKind),
            DurationSeconds = duration,
            DurationText = duration > 0 ? UiStrings.FormatDuration(duration) : string.Empty,
            SampleRate = sampleRate,
            SampleRateText = sampleRate > 0 ? UiStrings.FormatSampleRate(sampleRate) : string.Empty,
            BitDepth = bits,
            BitDepthText = bits > 0 ? UiStrings.FormatBitDepth(bits) : string.Empty,
            Channels = channels,
            ChannelsText = channels > 0 ? UiStrings.FormatChannels(channels) : string.Empty,
            SampleRateNonDefault = LibraryPlayerMode.HighlightsFormatValue(sampleRate, defaultSampleRate),
            BitDepthNonDefault = LibraryPlayerMode.HighlightsFormatValue(bits, defaultBitsPerSample),
            ChannelsNonDefault = LibraryPlayerMode.HighlightsFormatValue(channels, defaultChannels),
            BitRateKbps = bitRate,
            BitRateText = LibraryPlaylistDocuments.IsVideo(document)
                ? UiStrings.FormatVideoBitRate(bitRate)
                : UiStrings.FormatBitRate(bitRate),
            FileBytes = document.FileBytes,
            SizeText = UiStrings.FormatFileBytesCompact(document.FileBytes),
            FileDate = document.FileLastWriteTime ?? default,
            DateText = UiStrings.FormatFileDate(document.FileLastWriteTime),
            Folder = string.IsNullOrEmpty(path)
                ? string.Empty
                : Path.GetDirectoryName(path) ?? string.Empty,
            ParentFolder = LibraryFileList.ParentFolderName(path),
            HasArtwork = hasArt,
            JacketText = hasArt ? UiStrings.LibraryJacketMark : string.Empty,
            VideoWidth = tags.VideoWidth,
            VideoHeight = tags.VideoHeight,
            VideoSizeText = UiStrings.FormatVideoSize(tags.VideoWidth, tags.VideoHeight),
            FrameRate = tags.FrameRate,
            FrameRateText = UiStrings.FormatFrameRate(tags.FrameRate),
            VideoCodec = tags.VideoCodec,
            AudioCodec = tags.AudioCodec,
            IntegratedLufs = AudioCodec.IsUncompressedPcm(document.SourceKind)
                ? tags.IntegratedLufs
                : null,
            LoudnessText = AudioCodec.IsUncompressedPcm(document.SourceKind)
                ? UiStrings.FormatIntegratedLufs(tags.IntegratedLufs)
                : string.Empty,
        };
        row.LoudnessTone = LibraryFileRow.ToneFor(
            row.IntegratedLufs,
            AppStorage.Settings.ResolvedLoudnessTargetLufs());
        ApplyFileNameMarker(row);
        return row;
    }

    /// <summary>
    /// library-media-cache の .lufs を Tags へ載せる。
    /// ファイルのサイズか更新日時がキャッシュと違うときは、メモリ上の値を捨てて測り直す。
    /// </summary>
}
