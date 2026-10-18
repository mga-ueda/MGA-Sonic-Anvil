namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public static string ErrorLibraryFileOp => Get(
        "ファイル操作に失敗しました。",
        "The file operation failed.");

    public static string ErrorLibraryNotFound => Get(
        "見つかりません。",
        "Not found.");

    public static string ErrorLibraryAccess => Get(
        "書き込み権限がありません。",
        "Write permission was denied.");

    public static string ErrorLibraryDiskFull => Get(
        "ディスクの空き容量がありません。",
        "There is not enough disk space.");

    public static string ErrorLibraryReparse => Get(
        "このフォルダはリンクなのでコピーできません。",
        "This folder is a link and cannot be copied.");

    public static string LibrarySamePlace => Get(
        "同じ場所なのでできません。",
        "This is the same location, so it cannot be done.");

    public static string LibraryNameConflict(string name) => Format(
        "「{0}」と同じ名前があります。上書き、リネーム、キャンセルのどれにしますか？",
        "\"{0}\" already exists. Overwrite, rename, or cancel?",
        name);

    public static string LibraryOverwrite => Get("上書き", "Overwrite");
    public static string LibraryRename => Get("リネーム", "Rename");
    public static string LibraryCancel => Get("キャンセル", "Cancel");
    public static string LibraryRenameTitle => Get("名前の変更", "Rename");
    public static string LibraryNewFolderTitle => Get("新しいフォルダ", "New Folder");

    public static string ConfirmRemoveFavorite(string name) => Format(
        "「{0}」をお気に入りから削除します。よろしいですか？",
        "Remove \"{0}\" from Favorites?",
        name);

    public static string ConfirmRemoveFavorites(int count) => Format(
        "選択した {0} 件をお気に入りから削除します。よろしいですか？",
        "Remove the {0} selected items from Favorites?",
        count);

    public static string ConfirmDeleteSelectedItems(int count) => Format(
        "選択した {0} 個をディスクから削除しますか？この操作は元に戻せません。",
        "Delete the {0} selected items from disk? This cannot be undone.",
        count);
}
