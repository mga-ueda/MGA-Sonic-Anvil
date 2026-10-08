using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil.Domain;

/// <summary>プレイヤーのプレイリストに載せる PDF / 動画。編集オープンには含めない。</summary>
internal static class LibraryPlaylistDocuments
{
    public const double PdfSecondsPerPage = 5;

    public static bool ShowPdf { get; set; } = true;

    public static bool ShowMov { get; set; }

    public static bool ShowMp4 { get; set; }

    public static bool ShowAvi { get; set; }

    public static bool ShowMkv { get; set; }

    public static bool ShowWebm { get; set; }

    public static bool ShowMpg { get; set; }

    public static bool AnyEnabled =>
        ShowPdf || ShowMov || ShowMp4 || ShowAvi || ShowMkv || ShowWebm || ShowMpg;

    public static void Apply(
        bool pdf,
        bool mov,
        bool mp4,
        bool avi,
        bool mkv,
        bool webm,
        bool mpg)
    {
        ShowPdf = pdf;
        ShowMov = mov;
        ShowMp4 = mp4;
        ShowAvi = avi;
        ShowMkv = mkv;
        ShowWebm = webm;
        ShowMpg = mpg;
    }

    /// <summary>AVI / MKV / WebM / MPG は ffmpeg.exe が使えるときだけ載せる。</summary>
    public static void ApplyFromSettings(AppSettings settings)
    {
        var ffmpeg = VideoProxy.TryResolveFfmpegExe(settings.FfmpegExePath, out _);
        Apply(
            settings.LibraryShowPlaylistPdf,
            settings.LibraryShowPlaylistMov,
            settings.LibraryShowPlaylistMp4,
            settings.LibraryShowPlaylistAvi && ffmpeg,
            settings.LibraryShowPlaylistMkv && ffmpeg,
            settings.LibraryShowPlaylistWebm && ffmpeg,
            settings.LibraryShowPlaylistMpg && ffmpeg);
    }

    /// <summary>テスト用。全種をまとめてオン／オフ。</summary>
    public static void ApplyEnabled(bool enabled) =>
        Apply(enabled, enabled, enabled, enabled, enabled, enabled, enabled);

    public static bool IsDocument(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return IsPdfPath(path) || IsVideo(path);
    }

    public static bool ShouldList(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ShowPdf;
        }

        if (ext.Equals(".mov", StringComparison.OrdinalIgnoreCase))
        {
            return ShowMov;
        }

        if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return ShowMp4;
        }

        if (ext.Equals(".avi", StringComparison.OrdinalIgnoreCase))
        {
            return ShowAvi;
        }

        if (ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return ShowMkv;
        }

        if (ext.Equals(".webm", StringComparison.OrdinalIgnoreCase))
        {
            return ShowWebm;
        }

        if (IsMpgExtension(ext))
        {
            return ShowMpg;
        }

        return false;
    }

    public static bool IsVideo(string? path)
    {
        var ext = Path.GetExtension(path ?? string.Empty);
        return ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".avi", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || IsMpgExtension(ext);
    }

    public static bool IsPdfPath(string? path)
    {
        var ext = Path.GetExtension(path ?? string.Empty);
        return ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMpgExtension(string? extension)
    {
        var ext = extension ?? string.Empty;
        return ext.Equals(".mpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mpeg", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPdf(AudioFileKind kind) => kind == AudioFileKind.Pdf;

    public static bool IsVideo(AudioFileKind kind) =>
        kind is AudioFileKind.Mp4 or AudioFileKind.Mov
            or AudioFileKind.Avi or AudioFileKind.Mkv or AudioFileKind.Webm
            or AudioFileKind.Mpg;

    public static bool IsPdf(AudioDocument document) => IsPdf(document.SourceKind);

    public static bool IsVideo(AudioDocument document) => IsVideo(document.SourceKind);

    public static bool IsVisual(AudioFileKind kind) => IsPdf(kind) || IsVideo(kind);

    public static bool IsVisual(AudioDocument document) => IsVisual(document.SourceKind);

    public static bool BlocksEditor(AudioDocument document) => IsVisual(document);
}
