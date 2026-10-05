using System.IO;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Domain;

/// <summary>プレイヤーのプレイリストに載せる PDF / 動画。編集オープンには含めない。</summary>
internal static class LibraryPlaylistDocuments
{
    public const double PdfSecondsPerPage = 5;

    public static bool ShowPdf { get; set; } = true;

    public static bool ShowMov { get; set; } = true;

    public static bool ShowMp4 { get; set; } = true;

    public static bool AnyEnabled => ShowPdf || ShowMov || ShowMp4;

    public static void Apply(bool pdf, bool mov, bool mp4)
    {
        ShowPdf = pdf;
        ShowMov = mov;
        ShowMp4 = mp4;
    }

    /// <summary>テスト用。3 種をまとめてオン／オフ。</summary>
    public static void ApplyEnabled(bool enabled) => Apply(enabled, enabled, enabled);

    public static bool IsDocument(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        return ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
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

        return false;
    }

    public static bool IsVideo(string? path)
    {
        var ext = Path.GetExtension(path ?? string.Empty);
        return ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPdf(AudioFileKind kind) => kind == AudioFileKind.Pdf;

    public static bool IsVideo(AudioFileKind kind) =>
        kind is AudioFileKind.Mp4 or AudioFileKind.Mov;

    public static bool IsPdf(AudioDocument document) => IsPdf(document.SourceKind);

    public static bool IsVideo(AudioDocument document) => IsVideo(document.SourceKind);

    public static bool IsVisual(AudioFileKind kind) => IsPdf(kind) || IsVideo(kind);

    public static bool IsVisual(AudioDocument document) => IsVisual(document.SourceKind);

    public static bool BlocksEditor(AudioDocument document) => IsVisual(document);
}
