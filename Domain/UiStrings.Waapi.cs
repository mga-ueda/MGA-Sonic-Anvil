namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public const string WaapiTitle = "WAAPI";
    public const string WaapiBadgeConnect = "CONNECT";
    public const string WaapiBadgeDisconnect = "DISCONNECT";
    public const string LabelWwise = "Wwise";
    public const string LabelUnnamedProject = "(無名)";
    public const string LabelAutoActive = "Auto Active";
    public const string LabelPlayMinusE = "Play -E";
    public const string ButtonExport = "EXPORT";

    public const string StatusChecking = "確認中…";
    public const string StatusDisconnected = "未接続";
    public const string StatusNoneSelected = "（未選択）";
    public const string StatusNoProject = "(プロジェクトなし)";

    public const string KeepTargetOnLabel = "- Keep Target -";
    public const string KeepTargetOffLabel = "- Not Keep Target -";

    public const string TipKeepTargetUnlock =
        "いまの作成先パスを固定します。その後 Wwise 上で選択を変えても、表示と EXPORT 先はこの固定パスのままです。";
    public const string TipKeepTargetLock = "作成先の固定を解除します。";
    public const string TipAutoActive = "EXPORT 後に Wwise を前面へ出します。";
    public const string TipPlayMinusE =
        "オンのとき、-E 部分（Exit Cue 以降）を再生します（Play post-exit）(E)。\n"
        + "アプリ内プレビューではループ折り返し時に -E を二重再生し、シークバーが 2 本になります。\n"
        + "EXPORT 時は Music Playlist Container の既定トランジションルール（Any to Any）へ反映します。";
    public const string TipWaapiToggle =
        "WAAPI の接続とエリア表示を切り替えます (W)。オフのときは Wwise へ接続しません。";
    public const string PreflightWaapiOff =
        "WAAPI がオフです。トランスポートの WAAPI をオンにすると接続します。";
    public const string TipExport =
        "現在の波形を Wwise Originals へ書き、Music Playlist Container としてインポートします (Ctrl+Shift+E)。\nWave 単体モード（マーカー / サンプルループ。Custom Cue は出しません）。";
    public const string TipWwiseProjectNameOpen = "この Wwise プロジェクトを開きます（既に開いていれば前面）。";
    public const string TipOutputFolder =
        "波形の書き出し先フォルダを選択します（接続中 Wwise プロジェクトの Originals 配下）。";
    public const string TipOutputPath =
        "元 WAV のコピー先フォルダです。横のフォルダボタンで変更できます。";
    public const string SelectOutputFolderTitle = "書き出し先フォルダを選択";
    public static string ErrSelectFolderFailed(string message) => $"フォルダを選べませんでした: {message}";

    public const string ErrEmptyWaapiResponse = "WAAPI 応答が空です。";
    public const string LogWaapiTimeout = "WAAPI が応答しません（タイムアウト）。";
    public const string LogWaapiConnectFailed = "WAAPI に接続できません。";
    public const string LogKeepTargetMemoryEmpty = "Keep Target のパスが空です。";
    public const string LogKeepTargetOtherProject = "Keep Target は別プロジェクトのパスです。";
    public static string LogKeepTargetReselectOk(string path) => $"Keep Target を再選択しました: {path}";
    public static string LogKeepTargetObjectMissing(string path) => $"Keep Target のオブジェクトがありません: {path}";
    public static string LogKeepTargetReselectFailed(string message) => $"Keep Target の再選択に失敗: {message}";

    public const string PreflightNoDocument = "開いている波形がありません。";
    public const string PreflightNoParts = "書き出せる区間がありません。";
    public const string PreflightNoOutputDir = "書き出し先フォルダがありません。";
    public static string PreflightBadOutputPath(string message) => $"書き出し先が不正です: {message}";
    public const string PreflightOutputMissing = "書き出し先フォルダが見つかりません。";
    public const string PreflightWaapiDisconnected = "WAAPI に接続していません。Wwise Authoring を起動してください。";
    public const string PreflightKeepTargetNoPath = "Keep Target がオンですが、作成先パスがありません。";
    public const string PreflightNoSelection = "Wwise で作成先オブジェクトを選択してください。";
    public const string PreflightNoProjectPath = "接続中プロジェクトの .wproj パスを取得できません。";
    public const string PreflightNoProjectRoot = "プロジェクトフォルダを解決できません。";
    public static string PreflightOriginalsResolveFailed(string message) => $"Originals を解決できません: {message}";
    public const string PreflightNotUnderOriginals = "書き出し先は接続中プロジェクトの Originals 配下である必要があります。";
    public const string PreflightOk = "EXPORT できます。";
    public static string PreflightOkKeepTarget(string path) => $"EXPORT できます（Keep Target: {path}）。";

    public const string DialogExportTitle = "EXPORT";
    public static string DialogExportFailed(string reason) => reason;
    public static string DialogExportSucceeded(string path) => $"Wwise へ書き出しました。\n{path}";

    public static string ErrMusicClipNotFound(string trackPath) =>
        $"Music Track 配下の MusicClip が見つかりません（{trackPath}）。";
    public static string ErrMusicClipAmbiguous(string trackPath, int count) =>
        $"Music Track 配下の MusicClip が {count} 件あり特定できません（{trackPath}）。";
    public const string ErrWorkUnitPathUnknown =
        "作成先の WWU ファイルパスを取得できませんでした。";
    public const string ErrProjectPathUnknown =
        "プロジェクト（.wproj）のパスを取得できなかったため WWU を編集できません。";
    public const string ErrProjectCloseTimeout =
        "Wwise プロジェクトのクローズ完了を確認できませんでした。WWU 直接編集を中止します。";
    public static string ErrPlayAtClipXmlMissing(string clipId, string wwuPath) =>
        $"WWU 内に MusicClip {clipId} が見つかりません（{wwuPath}）。プロジェクトの保存に失敗している可能性があります。";
    public static string ErrPlayAtVerifyFailed(string clipId, double expected, double? actual) =>
        $"PlayAt の検証に失敗しました（clip={clipId} 期待値={expected:0.###}ms 実値={(actual is null ? "(なし)" : actual.Value.ToString("0.###") + "ms")}）。";
    public static string ErrPlaylistAnyToAnyRuleMissing(string containerName, string wwuPath) =>
        $"WWU 内に Music Playlist Container {containerName} の既定トランジションルール（Any to Any）が見つかりません（{wwuPath}）。";
    public static string ErrPostExitVerifyFailed(string containerName, bool expected, bool actual) =>
        $"Play post-exit の検証に失敗しました（{containerName} 期待値={expected} 実値={actual}）。";
}
