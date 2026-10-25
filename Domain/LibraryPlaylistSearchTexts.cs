using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Domain;

/// <summary>
/// ライブラリ索引の検索用テキスト。コメント／タイトル／アルバム／アーティスト／
/// 作曲／ジャンル／アルバムアーティストのみ（ファイル名とフォルダ名は別経路）。
/// </summary>
internal static class LibraryPlaylistSearchTexts
{
    public static string[] Build(AudioFileTags tags)
    {
        var texts = new List<string>(8);
        Add(texts, tags.Title);
        Add(texts, tags.Artist);
        Add(texts, tags.AlbumArtist);
        Add(texts, tags.Album);
        Add(texts, tags.Genre);
        Add(texts, tags.Composer);
        Add(texts, tags.Comment);
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
