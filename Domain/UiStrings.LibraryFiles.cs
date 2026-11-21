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

    public static string LibraryNameConflict(string name, bool offerBatchChoices = false) =>
        offerBatchChoices
            ? Format(
                "「{0}」と同じ名前があります。上書き、全て上書き、別名、全て別名、キャンセルのどれにしますか？",
                "\"{0}\" already exists. Overwrite, overwrite all, another name, all another names, or cancel?",
                name)
            : Format(
                "「{0}」と同じ名前があります。上書き、別名、キャンセルのどれにしますか？",
                "\"{0}\" already exists. Overwrite, another name, or cancel?",
                name);

    /// <summary>名衝突確認の Yes 相当。Y アクセスキー（Alt+Y）。</summary>
    public static string LibraryOverwrite => Get("上書き(_Y)", "Overwrite(_Y)");
    /// <summary>名衝突確認の OK 相当（複数ペースト）。A アクセスキー（Alt+A）。</summary>
    public static string LibraryOverwriteAll => Get("全て上書き(_A)", "Overwrite _All");
    /// <summary>名衝突確認の No 相当。N アクセスキー（Alt+N）。</summary>
    public static string LibraryAlternate => Get("別名(_N)", "Another Name(_N)");
    /// <summary>名衝突確認の None 相当（複数ペースト）。B アクセスキー（Alt+B）。</summary>
    public static string LibraryAlternateAll => Get("全て別名(_B)", "All Another Names(_B)");
    /// <summary>名衝突確認の Cancel。C アクセスキー（Alt+C）。</summary>
    public static string LibraryCancel => Get("キャンセル(_C)", "_Cancel");
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
