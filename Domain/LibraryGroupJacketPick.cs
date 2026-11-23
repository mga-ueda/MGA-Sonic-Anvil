using System.Collections;

namespace MgaSonicAnvil.Domain;

/// <summary>
/// グループ左のジャケット。行ごとには出さず、グループにつき1枚。
/// グループ内で最初にジャケットがある曲（選択行には追従しない）。
/// </summary>
internal static class LibraryGroupJacketPick
{
    public static LibraryFileRow? Resolve(IEnumerable items, LibraryFileRow? active = null)
    {
        _ = active;
        foreach (var item in items)
        {
            if (item is LibraryFileRow { HasArtwork: true } row)
            {
                return row;
            }
        }

        return null;
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
