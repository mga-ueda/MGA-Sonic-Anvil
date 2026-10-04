using System.Collections;

namespace MgaSonicAnvil.Domain;

/// <summary>
/// グループ左のジャケット。行ごとには出さず、グループにつき1枚。
/// 既定はグループ内で最初にジャケットがある曲。アクティブ行が同じグループならその曲。
/// </summary>
internal static class LibraryGroupJacketPick
{
    public static LibraryFileRow? Resolve(IEnumerable items, LibraryFileRow? active)
    {
        LibraryFileRow? firstArt = null;
        LibraryFileRow? matched = null;
        foreach (var item in items)
        {
            if (item is not LibraryFileRow row)
            {
                continue;
            }

            if (matched is null && IsSameRow(row, active))
            {
                matched = row;
            }

            if (firstArt is null && row.HasArtwork)
            {
                firstArt = row;
            }

            if (matched is not null && firstArt is not null)
            {
                break;
            }
        }

        return matched ?? firstArt;
    }

    public static bool ContainsActive(IEnumerable items, LibraryFileRow? active)
    {
        if (active is null)
        {
            return false;
        }

        foreach (var item in items)
        {
            if (item is LibraryFileRow row && IsSameRow(row, active))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameRow(LibraryFileRow row, LibraryFileRow? active)
    {
        if (active is null)
        {
            return false;
        }

        if (ReferenceEquals(row, active))
        {
            return true;
        }

        return row.Tag is not null && ReferenceEquals(row.Tag, active.Tag);
    }
}
