namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public static string WaveMenuClearMarker => Get("マーカーを削除(_D)", "Clear Marker (_D)");
    public static string WaveMenuClearMarkers => Get("選択したマーカーを削除(_D)", "Clear Selected Markers (_D)");
    public static string WaveMenuClearRegion => Get("リージョンを削除(_G)", "Clear Re_gion");
    public static string WaveMenuClearLoop => Get("ループを削除(_L)", "Clear _Loop");

    public static string WaveMenuPlayFromHere => Get("ここから再生(_Y)", "Pla_y from Here");
    public static string WaveMenuSeekHere => Get("再生位置をここに移動(_J)", "_Jump Playhead Here");
    public static string WaveMenuAddMarkerHere => Get("ここにマーカーを追加(_M)", "Add _Marker Here");
    public static string WaveMenuSelectSpanHere => Get("この区間を選択(_S)", "Select This _Span");

    /// <summary>クリックした位置。再生・移動・マーカー・区間・時間のコピーと、当たったマーカー等の削除。</summary>
    public static string WaveMenuCatHere => Get("この位置(_H)", "_Here");
    public static string WaveMenuCatEdit => Get("編集(_E)", "_Edit");
    public static string WaveMenuCatAudio => Get("オーディオ(_A)", "_Audio");
    public static string WaveMenuCatFormat => Get("変換(_N)", "Co_nvert");
    public static string WaveMenuCatTimeline => Get("タイムライン(_T)", "_Timeline");
    public static string WaveMenuCatPlayback => Get("再生(_P)", "_Playback");
    public static string WaveMenuCatView => Get("表示(_V)", "_View");
    /// <summary>表示の中。波形／スペクトログラム／重ね／ラウドネス。</summary>
    public static string WaveMenuCatAnalysis => Get("解析(_A)", "_Analysis");
    /// <summary>表示の中。F8〜F12。</summary>
    public static string WaveMenuCatScreen => Get("画面(_F)", "Screen (_F)");
    /// <summary>表示の中。動画／PDF の本再生。</summary>
    public static string WaveMenuCatVideo => Get("映像(_Q)", "Video (_Q)");
    /// <summary>表示の中。チャンネルソロ。</summary>
    public static string WaveMenuCatSolo => Get("ソロ(_N)", "Solo (_N)");
    /// <summary>表示の中。時間ズームと振幅ズーム。</summary>
    public static string WaveMenuCatZoom => Get("ズーム(_Z)", "_Zoom");
    /// <summary>再生の中。プレイヤーの曲送りと自動再生。</summary>
    public static string WaveMenuCatPlaylist => Get("プレイリスト(_Y)", "Playlist (_Y)");
    /// <summary>再生の中。ファイル端と画面端への移動。</summary>
    public static string WaveMenuCatSeek => Get("移動(_S)", "_Seek");
    public static string WaveMenuCatFile => Get("ファイル(_F)", "_File");
    public static string WaveMenuCatTabs => Get("タブ(_B)", "Ta_bs");
    public static string WaveMenuCatExport => Get("書き出し(_X)", "E_xport");
    public static string WaveMenuCatWwise => Get("Wwise(_W)", "_Wwise");
    public static string WaveMenuCatOther => Get("その他(_O)", "_Other");

    public static string WaveMenuUndo => Get("元に戻す(_U)", "_Undo");
    public static string WaveMenuRedo => Get("やり直し(_R)", "_Redo");
    public static string WaveMenuCut => Get("切り取り(_T)", "Cu_t");
    public static string WaveMenuCopy => Get("コピー(_C)", "_Copy");
    public static string WaveMenuPaste => Get("貼り付け(_P)", "_Paste");
    /// <summary>タブメニューの「編集データを～にペーストする」と文言をそろえる。編集カテゴリでは V が「貼り付け」系と競合しやすいためアクセスキーは H。</summary>
    public static string WaveMenuPasteHistory => Get("編集データをペーストする(_H)", "Paste Copied Edits (_H)");
    public static string WaveMenuPasteHistorySelected => Get("選択したタブにペーストする(_H)", "Paste Copied Edits to Selected Tabs (_H)");
    public static string WaveMenuPasteHistoryAll => Get("編集データを全てにペーストする(_H)", "Paste Copied Edits to All Tabs (_H)");
    public static string WaveMenuDelete => Get("削除(_D)", "_Delete");
    public static string WaveMenuSilenceSelection => Get("選択範囲を無音化(_M)", "_Mute Selection");
    public static string WaveMenuInsertSilence => Get("無音を挿入(_I)", "_Insert Silence");
    public static string WaveMenuInsertSilenceSelected => Get("選択ファイルに無音を挿入(_I)", "_Insert Silence in Selected Files");
    public static string WaveMenuDetectSilence => Get("無音・ヒット検出(_S)", "_Silence & Hit Detection");
    public static string WaveMenuDetectSilenceSelected => Get("選択ファイルを無音・ヒット検出(_S)", "_Silence & Hit Detection in Selected Files");
    public static string WaveMenuSelectAll => Get("すべて選択(_A)", "Select _All");
    public static string WaveMenuClearSelection => Get("選択を解除(_L)", "C_lear Selection");
    public static string WaveMenuSelectToStart => Get("先頭まで選択(_B)", "Select to _Beginning");
    public static string WaveMenuSelectToEnd => Get("末尾まで選択(_N)", "Select to E_nd");
    public static string WaveMenuCopySelectionTime => Get("選択範囲の時間をコピー(_K)", "Copy Selection Time (_K)");
    public static string WaveMenuCopySelectionTimeSelected => Get("選択ファイルの選択範囲の時間をコピー(_K)", "Copy Selected Selection Times (_K)");
    /// <summary>ルートメニューで Wwise(_W) と重ならないよう Q。</summary>
    public static string WaveMenuCopySelectionTimePath => Get("選択範囲の時間をフルパスでコピー(_Q)", "Copy Selection Time with Full Path (_Q)");
    public static string WaveMenuCopySelectionTimePathSelected => Get("選択ファイルの選択範囲の時間をフルパスでコピー(_Q)", "Copy Selected Selection Times with Full Path (_Q)");
    public static string WaveMenuHistory => Get("編集履歴(_E)", "_Edit History");

    public static string WaveMenuFadeIn => Get("フェードイン(_I)", "Fade _In");
    public static string WaveMenuFadeInSelected => Get("選択ファイルをフェードイン(_I)", "Fade _In Selected Files");
    public static string WaveMenuFadeOut => Get("フェードアウト(_O)", "Fade _Out");
    public static string WaveMenuFadeOutSelected => Get("選択ファイルをフェードアウト(_O)", "Fade _Out Selected Files");
    public static string WaveMenuFadeAround => Get("シークバー前後をフェード(_X)", "Fade Around See_k Bar");
    public static string WaveMenuNormalize => Get("ノーマライズ(_N)", "_Normalize");
    public static string WaveMenuNormalizeSelected => Get("選択ファイルをノーマライズ(_N)", "_Normalize Selected Files");
    public static string WaveMenuNormalizePerRegion => Get("リージョン毎に波形をノーマライズ(_G)", "Normalize Waveform per Re_gion");
    public static string WaveMenuNormalizePerRegionSelected => Get("選択ファイルをリージョン毎にノーマライズ(_G)", "Normalize Selected Files per Re_gion");
    public static string WaveMenuVolume => Get("音量(_U)", "Vol_ume");
    public static string WaveMenuVolumeSelected => Get("選択ファイルの音量(_U)", "Vol_ume of Selected Files");
    public static string WaveMenuPitchPreviewUp => Get("ピッチシフトしてプレビュー（半音上げる）(_U)", "Pitch Shift Preview (Semitone _Up)");
    public static string WaveMenuPitchPreviewDown => Get("ピッチシフトしてプレビュー（半音下げる）(_D)", "Pitch Shift Preview (Semitone _Down)");
    public static string WaveMenuPitchPreviewReset => Get("ピッチシフトをリセット(_0)", "Reset Pitch Shift (_0)");
    public static string WaveMenuPitch => Get("ピッチ(_P)", "_Pitch");
    public static string WaveMenuPitchSelected => Get("選択ファイルのピッチ(_P)", "_Pitch of Selected Files");
    public static string WaveMenuTimeStretch => Get("タイムストレッチ(_T)", "_Time Stretch");
    public static string WaveMenuTimeStretchSelected => Get("選択ファイルをタイムストレッチ(_T)", "_Time Stretch Selected Files");
    public static string WaveMenuReverse => Get("リバース(_R)", "_Reverse");
    public static string WaveMenuReverseSelected => Get("選択ファイルをリバース(_R)", "_Reverse Selected Files");

    public static string WaveMenuSampleRate => Get("サンプルレート(_S)", "_Sample Rate");
    public static string WaveMenuSampleRateSelected => Get("選択ファイルのサンプルレート(_S)", "_Sample Rate of Selected Files");
    public static string WaveMenuBitDepth => Get("ビット深度(_B)", "_Bit Depth");
    public static string WaveMenuBitDepthSelected => Get("選択ファイルのビット深度(_B)", "_Bit Depth of Selected Files");
    public static string WaveMenuChannels => Get("チャンネル(_C)", "_Channels");
    public static string WaveMenuChannelsSelected => Get("選択ファイルのチャンネル(_C)", "_Channels of Selected Files");

    public static string WaveMenuAddMarker => Get("マーカーを追加(_M)", "Add _Marker");
    public static string WaveMenuRenameMarker => Get("マーカー名を編集(_N)", "Re_name Marker");
    public static string WaveMenuDeleteMarkers => Get("マーカーを削除(_D)", "_Delete Markers");
    public static string WaveMenuDeleteAllMarkers => Get("すべてのマーカーを削除(_K)", "Delete All Mar_kers");
    public static string WaveMenuSetRegion => Get("選択範囲をリージョンに(_G)", "Set Re_gion from Selection");
    public static string WaveMenuRenameRegion => Get("リージョン名を編集(_E)", "R_ename Region");
    public static string WaveMenuDeleteRegions => Get("選択したリージョンを削除(_L)", "Delete Selected Regions (_L)");
    public static string WaveMenuDeleteAllRegions => Get("すべてのリージョンを削除(_R)", "Delete All _Regions");
    public static string WaveMenuSetLoop => Get("選択範囲をループに(_O)", "Set Sample L_oop from Selection");
    public static string WaveMenuClearLoopItem => Get("ループを削除(_C)", "_Clear Loop");
    /// <summary>再生カテゴリへ移動。ショートカット表示は Q。</summary>
    public static string WaveMenuRangeClick => Get("マーカークリック(_Q)", "Marker Click (_Q)");
    public static string WaveMenuPrevMarker => Get("前のマーカーへ(_P)", "_Previous Marker");
    public static string WaveMenuNextMarker => Get("次のマーカーへ(_X)", "Ne_xt Marker");

    public static string WaveMenuPlay => Get("再生(_P)", "_Play");
    public static string WaveMenuStopToStart => Get("開始位置で停止(_P)", "Stop to Start (_P)");
    public static string WaveMenuPauseHere => Get("その場で一時停止(_E)", "Paus_e Here");
    public static string WaveMenuPreroll => Get("3秒前から再生(_R)", "Play from 3s Befo_re");
    public static string WaveMenuRestart => Get("やり直し再生(_A)", "Rest_art Playback");
    public static string WaveMenuLoopPlay => Get("ループ再生(_L)", "_Loop Play");
    public static string WaveMenuPlayExit => Get("Play -E(_X)", "Play -E (_X)");
    /// <summary>再生カテゴリ内で Preroll(R) と重ならないよう U。ショートカット表示は Alt+R。</summary>
    public static string WaveMenuShuffle => Get("ランダム再生(_U)", "Sh_uffle");
    /// <summary>再生カテゴリ内で Restart(A)／GoEnd(N) と重ならないよう M。ショートカット表示は Alt+N。</summary>
    public static string WaveMenuAutoPlayNext => Get("次の曲を自動再生(_M)", "Auto ne_xt");
    /// <summary>再生カテゴリ内。ショートカット表示は Alt+P。チェックはライブラリの Auto Play と同じ。</summary>
    public static string WaveMenuAutoPlay => Get("追加時に自動再生(_I)", "Auto Play on Add (_I)");
    /// <summary>再生カテゴリ内。P／N／R はプレイリストの前後曲と頭出し。アクセスキーは再生(P) などと重ならない。</summary>
    public static string WaveMenuPreviousTrack => Get("前の曲(_K)", "Previous Track (_K)");
    public static string WaveMenuNextTrack => Get("次の曲(_J)", "Next Track (_J)");
    public static string WaveMenuRestartTrack => Get("頭から再生(_D)", "Restart Track (_D)");
    public static string WaveMenuRecord => Get("録音(_C)", "Re_cord");
    public static string WaveMenuGoStart => Get("先頭へ(_H)", "Go to Start (_H)");
    public static string WaveMenuGoEnd => Get("末尾へ(_N)", "Go to E_nd");
    public static string WaveMenuViewLeft => Get("画面左端へ(_Z)", "Go to View Left (_Z)");
    public static string WaveMenuViewRight => Get("画面右端へ(_T)", "Go to View Righ_t");

    public static string WaveMenuTimeZoomIn => Get("時間拡大(_I)", "Time Zoom _In");
    public static string WaveMenuTimeZoomOut => Get("時間縮小(_U)", "Time Zoom O_ut");
    public static string WaveMenuTimeZoomMax => Get("時間最大(_T)", "_Time Zoom Max");
    public static string WaveMenuTimeZoomFit => Get("全体表示(_F)", "_Fit All");
    public static string WaveMenuAmpZoomIn => Get("振幅拡大(_A)", "_Amplitude Zoom In");
    public static string WaveMenuAmpZoomOut => Get("振幅縮小(_S)", "Amplitude Zoom Out (_S)");
    public static string WaveMenuAmpZoomMax => Get("振幅最大(_V)", "Amplitude Zoom Max (_V)");
    public static string WaveMenuAmpZoomReset => Get("振幅リセット(_B)", "Reset Amplitude (_B)");
    public static string WaveMenuCenterPlayhead => Get("再生ヘッドを中央へ(_C)", "_Center Playhead");
    public static string WaveMenuCenterLock => Get("センターロック(_K)", "Center Loc_k");
    public static string WaveMenuViewWaveform => Get("波形(_W)", "_Waveform");
    public static string WaveMenuViewSpectrogram => Get("スペクトログラム(_S)", "_Spectrogram");
    public static string WaveMenuViewOverlay => Get("重ね表示(_Y)", "Overla_y");
    public static string WaveMenuViewLoudness => Get("ラウドネス(_L)", "_Loudness");
    public static string WaveMenuTileOff => Get("タブのまま(_B)", "Keep Ta_bs");
    public static string WaveMenuTileHorizontal => Get("左右に並べる(_1)", "Side by Side (_1)");
    public static string WaveMenuTileVertical => Get("上下に並べる(_2)", "Stacked (_2)");
    public static string WaveMenuTileGrid => Get("上下左右に並べる(_3)", "Grid (_3)");
    public static string WaveMenuSoloNext => Get("次のチャンネルをソロ(_N)", "_Next Channel Solo");
    public static string WaveMenuSoloPrev => Get("前のチャンネルをソロ(_P)", "_Previous Channel Solo");
    public static string WaveMenuSoloClear => Get("ソロを解除(_E)", "Cl_ear Solo");
    public static string WaveMenuFocusTime => Get("時間へ移動(_G)", "Focus Time (_G)");
    public static string WaveMenuSilentSkip => Get("Silent Skip(_H)", "Silent Skip (_H)");
    public static string WaveMenuAlwaysOnTop => Get("常に最前面(_T)", "Always on _Top");
    /// <summary>その他。マニュアル(M) と重ならない O。</summary>
    public static string WaveMenuSettings => Get("設定(_O)", "Settings (_O)");
    /// <summary>その他。GitHub(G) と重ならない R。</summary>
    public static string WaveMenuColorPanel => Get("色設定(_R)", "Colo_r Settings");
    public static string WaveMenuMaximizeWaveform => Get("フルスクリーン(_F)", "_Fullscreen");
    public static string WaveMenuMaximizeAnalyzers => Get("フルスクリーン（アナライザー）(_A)", "Fullscreen with _Analyzers");
    public static string WaveMenuMaximizeLibrary => Get("プレイヤー(_B)", "Player (_B)");
    public static string WaveMenuVideoMini => Get("動画ミニ(_M)", "Video _Mini");
    public static string WaveMenuMinimalChrome => Get("波形だけ(_D)", "Playlist and Waveform (_D)");
    public static string WaveMenuVideoFullscreen => Get("映像を全画面(_Q)", "Full Screen Video (_Q)");
    public static string WaveMenuVideoMiniZoomIn => Get("ミニ窓を拡大(_U)", "Enlarge Mini Window (_U)");
    public static string WaveMenuVideoMiniZoomOut => Get("ミニ窓を縮小(_Z)", "Shrink Mini Window (_Z)");
    public static string WaveMenuVideoHud => Get("映像オーバーレイ(_X)", "Video Overlay (_X)");
    public static string WaveMenuVideoTimecode => Get("タイムコード(_1)", "Timecode (_1)");
    public static string WaveMenuPdfBackgroundPin => Get("PDFを背景に固定(_2)", "Pin PDF Background (_2)");
    public static string WaveMenuExitRealPlayback => Get("プレビューに戻る(_3)", "Back to Preview (_3)");
    public static string WaveMenuForceVideoProxy => Get("プロキシを生成(_4)", "Encode Proxy (_4)");

    public static string WaveMenuNew => Get("新規(_N)", "_New");
    public static string WaveMenuOpen => Get("開く(_O)", "_Open");
    public static string WaveMenuSave => Get("上書き保存(_S)", "_Save");
    public static string WaveMenuSaveSelected => Get("選択ファイルを保存(_S)", "_Save Selected Files");
    public static string WaveMenuSaveAs => Get("名前を付けて保存(_A)", "Save _As");
    public static string WaveMenuSaveAsSelected => Get("選択ファイルを名前を付けて保存(_A)", "Save Selected Files _As");
    public static string WaveMenuSaveMp3 => Get("MP3 として保存(_M)", "Save as _MP3");
    public static string WaveMenuRenameFile => Get("ファイル名を変更(_F)", "Rename _File");
    /// <summary>ファイルカテゴリ内。Rename(F)／Duplicate(U)／Delete(D) と重ならない E／P。</summary>
    public static string WaveMenuCopyFileName => Get("ファイル名をコピー(_E)", "Copy File Nam_e");
    public static string WaveMenuCopyFileNameSelected => Get("選択ファイルのファイル名をコピー(_E)", "Copy Selected File Nam_es");
    public static string WaveMenuCopyFilePath => Get("フルパスをコピー(_P)", "Copy Full _Path");
    public static string WaveMenuCopyFilePathSelected => Get("選択ファイルのフルパスをコピー(_P)", "Copy Selected Full _Paths");
    public static string WaveMenuDuplicateFile => Get("ファイルを複製(_U)", "D_uplicate File");
    public static string WaveMenuDuplicateFileSelected => Get("選択ファイルを複製(_U)", "D_uplicate Selected Files");
    /// <summary>書き出しカテゴリへ移動。J は既存の Wave／MP3／セパレートと重ならない。</summary>
    public static string WaveMenuMergeTabs => Get("選択ファイルをバウンス(_J)", "Bounce Selected Files (_J)");
    public static string WaveMenuDeleteFile => Get("ファイルを削除(_D)", "_Delete File");
    public static string WaveMenuDeleteFileSelected => Get("選択ファイルを削除(_D)", "_Delete Selected Files");
    /// <summary>タブカテゴリ。閉じる(S) や全選択(A) と重ならない I。</summary>
    public static string WaveMenuCopyAllTabTimes => Get("すべてのファイルの時間を表示(_I)", "Show All File T_imes");
    public static string WaveMenuCopySelectedTabTimes => Get("選択ファイルの時間を表示(_I)", "Show Selected File T_imes");
    public static string WaveMenuReopenTab => Get("閉じたタブを再開(_T)", "Reopen Closed _Tab");
    /// <summary>タイル配置がタブカテゴリへ来て「タブのまま(B)」と重なるため、アクセスキーは B をやめて N。</summary>
    public static string WaveMenuNextTab => Get("次のタブ(_N)", "_Next Tab");
    public static string WaveMenuPrevTab => Get("前のタブ(_V)", "Pre_vious Tab");
    public static string WaveMenuFindTabs => Get("タブを検索(_F)", "Find Tabs (_F)");
    public static string WaveMenuClearTileSearch => Get("検索を解除(_C)", "Clear Tab Search (_C)");
    /// <summary>Wwise カテゴリ内。Play -E(X) と重なる X をやめて E。</summary>
    public static string WaveMenuWaapiPanel => Get("WAAPI パネル(_P)", "WAAPI _Panel");
    public static string WaveMenuKeepTarget => Get("Keep Target(_K)", "Keep Target (_K)");
    public static string WaveMenuAutoActive => Get("Auto Active(_A)", "Auto Active (_A)");
    public static string WaveMenuWwiseExport => Get("Wwise へ EXPORT(_E)", "EXPORT to Wwise (_E)");
    public static string WaveMenuLaunchWwise => Get("プロジェクトを起動(_L)", "Launch Project (_L)");
    public static string WaveMenuOutputFolder => Get("書き出し先フォルダ(_F)", "Output Folder (_F)");
    public static string WaveMenuQuit => Get("終了(_Q)", "_Quit");

    public static string WaveMenuExportWave => Get("Wave で書き出す(_W)", "Export _Wave");
    public static string WaveMenuExportWaveSelected => Get("選択したタブを Wave で書き出す(_W)", "Export Selected Tabs as _Wave");
    public static string WaveMenuExportMp3 => Get("MP3 で書き出す(_M)", "Export _MP3");
    public static string WaveMenuExportMp3Selected => Get("選択したタブを MP3 で書き出す(_M)", "Export Selected Tabs as _MP3");
    public static string WaveMenuExportByMarkers => Get("マーカーでセパレート(_K)", "Separate by Mar_kers");
    public static string WaveMenuExportByMarkersSelected => Get("選択したタブをマーカーでセパレート(_K)", "Separate Selected Tabs by Mar_kers");
    public static string WaveMenuExportByRegions => Get("リージョンでセパレート(_G)", "Separate by Re_gions");
    public static string WaveMenuExportByRegionsSelected => Get("選択したタブをリージョンでセパレート(_G)", "Separate Selected Tabs by Re_gions");
    public static string WaveMenuExportByChannels => Get("チャンネルごとに書き出す(_C)", "Export by _Channel");
    public static string WaveMenuExportByChannelsSelected => Get("選択したタブをチャンネルごとに書き出す(_C)", "Export Selected Tabs by _Channel");
    public static string WaveMenuExportAllWave => Get("すべてのタブを Wave で保存(_E)", "Save All Tabs as Wav_e");
    /// <summary>書き出しカテゴリへ移動したため、MP3 で書き出す(M) と重なる M をやめて A。</summary>
    public static string WaveMenuExportAllMp3 => Get("すべてのタブを MP3 で保存(_A)", "Save All Tabs as MP3 (_A)");

    public static string WaveMenuManual => Get("マニュアル(_M)", "_Manual");
    public static string WaveMenuGitHub => Get("GitHub(_G)", "_GitHub");
}
