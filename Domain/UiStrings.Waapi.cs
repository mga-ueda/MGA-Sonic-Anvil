namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public static string WaapiTitle => Get("WAAPI", "WAAPI");
    public static string WaapiBadgeConnect => Get("CONNECT", "CONNECT");
    public static string WaapiBadgeDisconnect => Get("DISCONNECT", "DISCONNECT");
    public static string LabelWwise => Get("Wwise", "Wwise");
    public static string LabelWwisePrefetchLength => Get("Prefetch Length", "Prefetch Length");
    public static string LabelWwiseLookAheadTime => Get("Look-ahead Time", "Look-ahead Time");
    public static string ErrorWwisePrefetchLengthRange => Get(
        "Prefetch Length は 0 から 10000 の ms で入力してください。",
        "Enter a Prefetch Length between 0 and 10000 ms.");
    public static string ErrorWwiseLookAheadTimeRange => Get(
        "Look-ahead Time は 0 から 10000 の ms で入力してください。",
        "Enter a Look-ahead Time between 0 and 10000 ms.");
    public static string TipWwisePrefetchLength => Get(
        "EXPORT で先頭 Music Track に書く Prefetch Length。先頭は Zero Latency。Stream は常にオン（設定しない）。0 から 10000。既定 500。",
        "Prefetch Length written on the first Music Track at EXPORT. The first track is Zero Latency. Stream is always on (not a setting). From 0 to 10000. Default 500.");
    public static string TipWwiseLookAheadTime => Get(
        "EXPORT で 2 本目以降の Music Track に書く Look-ahead Time。先頭は Zero Latency のため 50 ms 固定。0 から 10000。既定 500。",
        "Look-ahead Time written on later Music Tracks at EXPORT. The first track stays at 50 ms because it is Zero Latency. From 0 to 10000. Default 500.");
    public static string LabelUnnamedProject => Get("(無名)", "(unnamed)");
    public static string LabelAutoActive => Get("Auto Active", "Auto Active");
    public static string LabelPlayMinusE => Get("Play -E", "Play -E");
    public static string ButtonExport => Get("EXPORT", "EXPORT");

    public static string StatusChecking => Get("確認中…", "Checking…");
    public static string StatusDisconnected => Get("未接続", "Disconnected");
    public static string StatusNoneSelected => Get("（未選択）", "(none selected)");
    public static string StatusNoProject => Get("(プロジェクトなし)", "(no project)");

    public static string KeepTargetOnLabel => Get("- Keep Target -", "- Keep Target -");
    public static string KeepTargetOffLabel => Get("- Not Keep Target -", "- Not Keep Target -");

    public static string TipKeepTargetUnlock => Get(
        "いまの作成先パスを固定します。その後 Wwise 上で選択を変えても、表示と EXPORT 先はこの固定パスのままです。",
        "Pin the current destination path. Later Wwise selection changes will not move the display or EXPORT target.");
    public static string TipKeepTargetLock => Get("作成先の固定を解除します。", "Unpin the destination path.");
    public static string TipAutoActive => Get(
        "EXPORT 後に Wwise を前面へ出します。",
        "Bring Wwise to the front after EXPORT.");
    public static string TipPlayMinusE => Get(
        "オンのとき、-E 部分（Exit Cue 以降）を再生します（Play post-exit）(Alt+E)。WAAPI オンのときだけ切り替わります。\n"
        + "アプリ内プレビューではループ折り返し時に -E を二重再生し、シークバーが 2 本になります。\n"
        + "EXPORT 時は Music Playlist Container の既定トランジションルール（Any to Any）へ反映します。",
        "When on, plays the -E section (after the Exit Cue) (Play post-exit) (Alt+E). The key works only while WAAPI is on.\n"
        + "In-app preview double-plays -E on a loop wrap so two playheads appear.\n"
        + "EXPORT writes this to the Music Playlist Container default transition rule (Any to Any).");
    public static string TipWaapiToggle => Get(
        "WAAPI の接続とエリア表示を切り替えます (W)。オフのときは Wwise へ接続しません。Ctrl+Alt+Shift+W でプロジェクトを起動（バーも出します）。",
        "Toggle WAAPI connection and the WAAPI area (W). Off means no connection to Wwise. Ctrl+Alt+Shift+W launches the project (and shows the bar).");
    public static string PreflightWaapiOff => Get(
        "WAAPI がオフです。レベルメーター上の WAAPI をオンにすると接続します。",
        "WAAPI is off. Turn on WAAPI above the level meter to connect.");
    public static string TipExport => Get(
        "現在の波形を Wwise Originals へ書き、Music Playlist Container としてインポートします (Ctrl+Shift+E)。\nWAAPI 接続中だけ有効。Wave 単体モード（-A / -L / -E とサンプルループ。Custom Cue にはしない）。",
        "Write the current wave into Wwise Originals and import it as a Music Playlist Container (Ctrl+Shift+E).\nEnabled only while WAAPI is connected. Wave-only mode (-A / -L / -E and the sample loop. Markers are not Custom Cues).");
    public static string TipWwiseProjectName => Get(
        "接続中の Wwise プロジェクトです。",
        "The connected Wwise project.");
    public static string TipWwiseProjectNameOpen => Get(
        "この Wwise プロジェクトを開きます（既に開いていれば前面）。クリック、または Ctrl+Alt+Shift+W。WAAPI がオフでもバーを出して起動します。覚えた .wproj が無ければ起動せず、その旨を出します。関連付けが Launcher でも、版に合う Wwise.exe を直接起動します。",
        "Open this Wwise project (or bring it to the front if it is already open). Click, or Ctrl+Alt+Shift+W. Works with WAAPI off: the bar is shown, then the project launches. If no .wproj is remembered, it stays on the bar and reports that it cannot launch. Even if the association is the Launcher, the matching Wwise.exe is started directly.");
    public static string TipOutputFolder => Get(
        "波形の書き出し先フォルダを選択します（接続中 Wwise プロジェクトの Originals 配下）。",
        "Choose the wave export folder (under Originals of the connected Wwise project).");
    public static string TipOutputPath => Get(
        "元 WAV のコピー先フォルダです。横のフォルダボタンで変更できます。",
        "Folder where the source WAV is copied. Change it with the folder button.");
    public static string TipWaapiConnection => Get(
        "Wwise Authoring との接続状態です。CONNECT なら接続中、DISCONNECT なら未接続。",
        "Connection to Wwise Authoring. CONNECT is linked, DISCONNECT is not.");
    public static string TipWwiseVersion => Get(
        "接続中の Wwise のバージョンです。",
        "Version of the connected Wwise.");
    public static string TipWaapiTargetPath => Get(
        "Wwise の作成先オブジェクトです。Wwise で選択すると更新されます。Keep Target で固定できます。",
        "Wwise destination object. Updates when you select in Wwise. Keep Target can pin it.");
    public static string SelectOutputFolderTitle => Get("書き出し先フォルダを選択", "Select export folder");
    public static string ErrSelectFolderFailed(string message) => Format(
        "フォルダを選べませんでした: {0}",
        "Could not choose a folder: {0}",
        message);

    public static string ErrEmptyWaapiResponse => Get("WAAPI 応答が空です。", "The WAAPI response was empty.");
    public static string LogWaapiTimeout => Get("WAAPI が応答しません（タイムアウト）。", "WAAPI did not respond (timeout).");
    public static string LogWaapiConnectFailed => Get("WAAPI に接続できません。", "Could not connect to WAAPI.");
    public static string LogKeepTargetMemoryEmpty => Get("Keep Target のパスが空です。", "The Keep Target path is empty.");
    public static string LogKeepTargetOtherProject => Get(
        "Keep Target は別プロジェクトのパスです。",
        "Keep Target points at another project.");
    public static string LogKeepTargetReselectOk(string path) => Format(
        "Keep Target を再選択しました: {0}",
        "Reselected Keep Target: {0}",
        path);
    public static string LogKeepTargetObjectMissing(string path) => Format(
        "Keep Target のオブジェクトがありません: {0}",
        "Keep Target object is missing: {0}",
        path);
    public static string LogKeepTargetReselectFailed(string message) => Format(
        "Keep Target の再選択に失敗: {0}",
        "Failed to reselect Keep Target: {0}",
        message);

    public static string LogWwiseProjectPathMissing => Get(
        "Wwise プロジェクトのファイルパスが不明なため開けません。Wwise でプロジェクトを開いた状態で一度接続してください。",
        "Cannot open the Wwise project because its file path is unknown. Connect once with the project open in Wwise.");

    public static string LogWwiseProjectFileMissing(string path) => Format(
        "Wwise プロジェクトファイルが見つかりません: {0}",
        "Wwise project file not found: {0}",
        path);

    public static string LogWwiseProjectBroughtToFront(string projectName) => Format(
        "Wwise を前面に表示しました: {0}",
        "Brought Wwise to the front: {0}",
        projectName);

    public static string LogWwiseBroughtToFront => Get(
        "Wwise を前面にしました。",
        "Brought Wwise to the foreground.");

    public static string LogWwiseBringToFrontFailed(string detail) => Format(
        "Wwise の前面化に失敗しました: {0}",
        "Failed to bring Wwise to the foreground: {0}",
        detail);

    public static string LogWwiseProjectOpened(string projectName) => Format(
        "Wwise プロジェクトを開きました: {0}",
        "Opened Wwise project: {0}",
        projectName);

    public static string LogWwiseProjectOpenRequestFailed(string detail) => Format(
        "Wwise への WAAPI 呼び出しに失敗しました（起動済みのため二重起動は行いません）: {0}",
        "WAAPI call to Wwise failed (skipped launching another instance because Wwise is already running): {0}",
        detail);

    public static string LogWwiseProjectShellOpen(string projectName) => Format(
        "Wwise プロジェクトを起動しました: {0}",
        "Launched Wwise project: {0}",
        projectName);

    public static string LogWwiseProjectOpenFailed(string message) => Format(
        "Wwise プロジェクトを開けませんでした: {0}",
        "Failed to open Wwise project: {0}",
        message);

    public static string PreflightNoDocument => Get("開いている波形がありません。", "No wave is open.");
    public static string PreflightNoParts => Get("書き出せる区間がありません。", "There is no range to export.");
    public static string PreflightNoOutputDir => Get("書き出し先フォルダがありません。", "No export folder is set.");
    public static string PreflightBadOutputPath(string message) => Format(
        "書き出し先が不正です: {0}",
        "The export path is invalid: {0}",
        message);
    public static string PreflightOutputMissing => Get(
        "書き出し先フォルダが見つかりません。",
        "The export folder was not found.");
    public static string PreflightWaapiDisconnected => Get(
        "WAAPI に接続していません。Wwise Authoring を起動してください。",
        "Not connected to WAAPI. Start Wwise Authoring.");
    public static string PreflightKeepTargetNoPath => Get(
        "Keep Target がオンですが、作成先パスがありません。",
        "Keep Target is on, but no destination path is set.");
    public static string PreflightNoSelection => Get(
        "Wwise で作成先オブジェクトを選択してください。",
        "Select a destination object in Wwise.");
    public static string PreflightNoProjectPath => Get(
        "接続中プロジェクトの .wproj パスを取得できません。",
        "Could not get the connected project's .wproj path.");
    public static string PreflightNoProjectRoot => Get(
        "プロジェクトフォルダを解決できません。",
        "Could not resolve the project folder.");
    public static string PreflightOriginalsResolveFailed(string message) => Format(
        "Originals を解決できません: {0}",
        "Could not resolve Originals: {0}",
        message);
    public static string PreflightNotUnderOriginals => Get(
        "書き出し先は接続中プロジェクトの Originals 配下である必要があります。",
        "The export folder must be under Originals of the connected project.");
    public static string PreflightOk => Get("EXPORT できます。", "Ready to EXPORT.");
    public static string PreflightOkKeepTarget(string path) => Format(
        "EXPORT できます（Keep Target: {0}）。",
        "Ready to EXPORT (Keep Target: {0}).",
        path);

    public static string DialogExportTitle => Get("EXPORT", "EXPORT");
    public static string DialogExportFailed(string reason) => reason;
    public static string DialogExportSucceeded(string path) => Format(
        "Wwise へ書き出しました。\n{0}",
        "Exported to Wwise.\n{0}",
        path);

    public static string ErrMusicClipNotFound(string trackPath) => Format(
        "Music Track 配下の MusicClip が見つかりません（{0}）。",
        "No MusicClip was found under the Music Track ({0}).",
        trackPath);
    public static string ErrMusicClipAmbiguous(string trackPath, int count) => Format(
        "Music Track 配下の MusicClip が {1} 件あり特定できません（{0}）。",
        "Found {1} MusicClips under the Music Track and could not pick one ({0}).",
        trackPath,
        count);
    public static string ErrWorkUnitPathUnknown => Get(
        "作成先の WWU ファイルパスを取得できませんでした。",
        "Could not get the destination WWU file path.");
    public static string ErrProjectPathUnknown => Get(
        "プロジェクト（.wproj）のパスを取得できなかったため WWU を編集できません。",
        "Could not get the project (.wproj) path, so the WWU cannot be edited.");
    public static string ErrProjectCloseTimeout => Get(
        "Wwise プロジェクトのクローズ完了を確認できませんでした。WWU 直接編集を中止します。",
        "Could not confirm that the Wwise project finished closing. Direct WWU editing is aborted.");
    public static string ErrPlayAtClipXmlMissing(string clipId, string wwuPath) => Format(
        "WWU 内に MusicClip {0} が見つかりません（{1}）。プロジェクトの保存に失敗している可能性があります。",
        "MusicClip {0} was not found in the WWU ({1}). The project may have failed to save.",
        clipId,
        wwuPath);
    public static string ErrPlayAtVerifyFailed(string clipId, double expected, double? actual) => Format(
        "PlayAt の検証に失敗しました（clip={0} 期待値={1:0.###}ms 実値={2}）。",
        "PlayAt verification failed (clip={0} expected={1:0.###}ms actual={2}).",
        clipId,
        expected,
        actual is null ? Get("(なし)", "(none)") : actual.Value.ToString("0.###") + "ms");
    public static string ErrPlaylistAnyToAnyRuleMissing(string containerName, string wwuPath) => Format(
        "WWU 内に Music Playlist Container {0} の既定トランジションルール（Any to Any）が見つかりません（{1}）。",
        "The default transition rule (Any to Any) for Music Playlist Container {0} was not found in the WWU ({1}).",
        containerName,
        wwuPath);
    public static string ErrPostExitVerifyFailed(string containerName, bool expected, bool actual) => Format(
        "Play post-exit の検証に失敗しました（{0} 期待値={1} 実値={2}）。",
        "Play post-exit verification failed ({0} expected={1} actual={2}).",
        containerName,
        expected,
        actual);
}
