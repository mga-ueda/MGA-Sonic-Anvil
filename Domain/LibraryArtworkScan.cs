using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Domain;

/// <summary>プレイリストのジャケット読み。動画／PDF はグループでも全件。MP3 / M4A はグループにつき1件。</summary>
internal static class LibraryArtworkScan
{
    public static bool CanRead(AudioFileKind kind) =>
        IsEmbeddedKind(kind) || IsVideoKind(kind) || IsPdfKind(kind);

    public static bool IsEmbeddedKind(AudioFileKind kind) =>
        kind is AudioFileKind.Mp3 or AudioFileKind.M4a;

    public static bool IsVideoKind(AudioFileKind kind) =>
        kind is AudioFileKind.Mp4 or AudioFileKind.Mov;

    public static bool IsPdfKind(AudioFileKind kind) => kind == AudioFileKind.Pdf;

    public static bool ShouldLoad(
        bool grouped,
        AudioFileKind kind,
        bool isSelected,
        string groupKey,
        HashSet<string> seen)
    {
        if (IsVideoKind(kind) || IsPdfKind(kind))
        {
            return true;
        }

        if (!grouped || !IsEmbeddedKind(kind))
        {
            return false;
        }

        if (isSelected)
        {
            return true;
        }

        return seen.Add(groupKey);
    }

    /// <summary>動画／PDF だけのグループは左の1枚ではなく各行にジャケット枠を出す。</summary>
    public static bool UsesRowJackets(System.Collections.IEnumerable items)
    {
        var any = false;
        foreach (var item in items)
        {
            if (item is not LibraryFileRow row)
            {
                continue;
            }

            if (!LibraryColumnFilter.IsVisualFamilyKind(LibraryColumnFilter.EffectiveKind(row)))
            {
                return false;
            }

            any = true;
        }

        return any;
    }
}
